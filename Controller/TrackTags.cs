using System.Buffers.Binary;
using System.Text;

namespace WiimControl;

sealed record TrackTagInfo(string Title, string Artist, string Album, byte[]? Picture)
{
    internal static readonly TrackTagInfo Empty = new(string.Empty, string.Empty, string.Empty, null);
}

static class TrackTags
{
    private const int FrontCover = 3;
    private const int MaxAtomDepth = 8;
    private const int Id3v1Size = 128;
    private static readonly string[] Mp4Containers = ["moov", "udta", "meta", "ilst"];
    private static readonly Encoding Latin1 = Encoding.Latin1;

    private sealed class Collected
    {
        public string Title = string.Empty, Artist = string.Empty, AlbumArtist = string.Empty, Album = string.Empty;
        public byte[]? Picture;
        public bool FrontPicture;

        public void AddPicture(byte[] image, bool front)
        {
            if (image.Length == 0 || FrontPicture || (Picture != null && !front)) return;
            Picture = image;
            FrontPicture = front;
        }

        public TrackTagInfo Result() => new(Title.Trim(), (Artist.Length > 0 ? Artist : AlbumArtist).Trim(), Album.Trim(), Picture);
    }

    internal static TrackTagInfo Read(ReadOnlySpan<byte> file)
    {
        var found = new Collected();
        try
        {
            if (file.StartsWith("fLaC"u8)) ReadFlac(file, found);
            else if (file.StartsWith("ID3"u8)) ReadId3(file, found);
            else if (file.Length > 8 && file[4..8].SequenceEqual("ftyp"u8)) ReadAtoms(file, found, inList: false, 0);
            if (found.Title.Length == 0 && found.Artist.Length == 0 && file.Length >= Id3v1Size && file[^Id3v1Size..].StartsWith("TAG"u8))
                ReadId3v1(file[^Id3v1Size..], found);
        }
        catch (ArgumentOutOfRangeException) { }
        catch (IndexOutOfRangeException) { }
        catch (ArgumentException) { }
        return found.Result();
    }

    private static int ReadInt(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadInt32BigEndian(data[at..]);

    private static void ReadFlac(ReadOnlySpan<byte> file, Collected found)
    {
        int at = 4;
        while (at + 4 <= file.Length)
        {
            bool last = (file[at] & 0x80) != 0;
            int type = file[at] & 0x7F;
            int length = (file[at + 1] << 16) | (file[at + 2] << 8) | file[at + 3];
            int body = at + 4;
            if (body + length > file.Length) break;
            var block = file.Slice(body, length);
            if (type == 4) ReadVorbisComments(block, found);
            else if (type == 6) ReadFlacPicture(block, found);
            if (last) break;
            at = body + length;
        }
    }

    private static void ReadVorbisComments(ReadOnlySpan<byte> block, Collected found)
    {
        int at = 4 + BinaryPrimitives.ReadInt32LittleEndian(block);
        int count = BinaryPrimitives.ReadInt32LittleEndian(block[at..]);
        at += 4;
        for (int i = 0; i < count && at + 4 <= block.Length; i++)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(block[at..]);
            string comment = Encoding.UTF8.GetString(block.Slice(at + 4, length));
            at += 4 + length;
            int equals = comment.IndexOf('=');
            if (equals <= 0) continue;
            string value = comment[(equals + 1)..];
            switch (comment[..equals].ToUpperInvariant())
            {
                case "TITLE" when found.Title.Length == 0: found.Title = value; break;
                case "ARTIST" when found.Artist.Length == 0: found.Artist = value; break;
                case "ALBUMARTIST" when found.AlbumArtist.Length == 0: found.AlbumArtist = value; break;
                case "ALBUM" when found.Album.Length == 0: found.Album = value; break;
            }
        }
    }

    private static void ReadFlacPicture(ReadOnlySpan<byte> picture, Collected found)
    {
        int pictureType = ReadInt(picture, 0);
        int p = 4;
        p += 4 + ReadInt(picture, p);
        p += 4 + ReadInt(picture, p);
        p += 16;
        int size = ReadInt(picture, p);
        found.AddPicture(picture.Slice(p + 4, size).ToArray(), pictureType == FrontCover);
    }

    private static int SyncSafe(ReadOnlySpan<byte> data, int at) =>
        ((data[at] & 0x7F) << 21) | ((data[at + 1] & 0x7F) << 14) | ((data[at + 2] & 0x7F) << 7) | (data[at + 3] & 0x7F);

    private static void ReadId3(ReadOnlySpan<byte> file, Collected found)
    {
        int version = file[3];
        bool unsynchronised = (file[5] & 0x80) != 0;
        int tagSize = SyncSafe(file, 6);
        var tag = file.Slice(10, Math.Min(tagSize, file.Length - 10)).ToArray();
        if (unsynchronised && version < 4) tag = RemoveUnsynchronisation(tag);
        int at = 0;
        if (version >= 3 && (file[5] & 0x40) != 0) at = version == 4 ? SyncSafe(tag, 0) : ReadInt(tag, 0) + 4;
        int headerSize = version == 2 ? 6 : 10;
        while (at + headerSize <= tag.Length)
        {
            string id = Encoding.ASCII.GetString(tag, at, version == 2 ? 3 : 4);
            if (id[0] == '\0') break;
            int size = version switch
            {
                2 => (tag[at + 3] << 16) | (tag[at + 4] << 8) | tag[at + 5],
                4 => SyncSafe(tag, at + 4),
                _ => ReadInt(tag, at + 4)
            };
            int body = at + headerSize;
            if (size <= 0 || body + size > tag.Length) break;
            var frame = tag.AsSpan(body, size);
            switch (id)
            {
                case "TIT2" or "TT2" when found.Title.Length == 0: found.Title = Id3Text(frame); break;
                case "TPE1" or "TP1" when found.Artist.Length == 0: found.Artist = Id3Text(frame); break;
                case "TPE2" or "TP2" when found.AlbumArtist.Length == 0: found.AlbumArtist = Id3Text(frame); break;
                case "TALB" or "TAL" when found.Album.Length == 0: found.Album = Id3Text(frame); break;
                case "APIC" or "PIC":
                    if (Id3Picture(frame, version == 2) is var (pictureType, image)) found.AddPicture(image, pictureType == FrontCover);
                    break;
            }
            at = body + size;
        }
    }

    private static string Id3Text(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2) return string.Empty;
        var text = frame[1..];
        string value = frame[0] switch
        {
            1 => Utf16(text),
            2 => Encoding.BigEndianUnicode.GetString(text),
            3 => Encoding.UTF8.GetString(text),
            _ => Latin1.GetString(text)
        };
        int end = value.IndexOf('\0');
        return (end >= 0 ? value[..end] : value).Trim();
    }

    private static string Utf16(ReadOnlySpan<byte> text)
    {
        if (text.Length >= 2 && text[0] == 0xFE && text[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(text[2..]);
        if (text.Length >= 2 && text[0] == 0xFF && text[1] == 0xFE) return Encoding.Unicode.GetString(text[2..]);
        return Encoding.Unicode.GetString(text);
    }

    private static byte[] RemoveUnsynchronisation(byte[] tag)
    {
        var output = new List<byte>(tag.Length);
        for (int i = 0; i < tag.Length; i++)
        {
            output.Add(tag[i]);
            if (tag[i] == 0xFF && i + 1 < tag.Length && tag[i + 1] == 0x00) i++;
        }
        return [.. output];
    }

    private static (int Type, byte[] Image)? Id3Picture(ReadOnlySpan<byte> frame, bool shortFormat)
    {
        int encoding = frame[0];
        int at = 1;
        if (shortFormat) at += 3;
        else
        {
            int end = frame[at..].IndexOf((byte)0);
            if (end < 0) return null;
            at += end + 1;
        }
        int type = frame[at++];
        if (encoding is 1 or 2)
        {
            while (at + 1 < frame.Length && (frame[at] != 0 || frame[at + 1] != 0)) at += 2;
            at += 2;
        }
        else
        {
            int end = frame[at..].IndexOf((byte)0);
            if (end < 0) return null;
            at += end + 1;
        }
        return at < frame.Length ? (type, frame[at..].ToArray()) : null;
    }

    private static void ReadId3v1(ReadOnlySpan<byte> tag, Collected found)
    {
        static string Field(ReadOnlySpan<byte> bytes)
        {
            int end = bytes.IndexOf((byte)0);
            return Latin1.GetString(end >= 0 ? bytes[..end] : bytes).Trim();
        }
        if (found.Title.Length == 0) found.Title = Field(tag.Slice(3, 30));
        if (found.Artist.Length == 0) found.Artist = Field(tag.Slice(33, 30));
        if (found.Album.Length == 0) found.Album = Field(tag.Slice(63, 30));
    }

    private static void ReadAtoms(ReadOnlySpan<byte> data, Collected found, bool inList, int depth)
    {
        if (depth > MaxAtomDepth) return;
        int at = 0;
        while (at + 8 <= data.Length)
        {
            long size = (uint)ReadInt(data, at);
            int header = 8;
            if (size == 1)
            {
                size = BinaryPrimitives.ReadInt64BigEndian(data[(at + 8)..]);
                header = 16;
            }
            else if (size == 0) size = data.Length - at;
            if (size < header || at + size > data.Length) return;
            string type = Latin1.GetString(data.Slice(at + 4, 4));
            var body = data.Slice(at + header, (int)size - header);
            if (inList) ReadListItem(type, body, found);
            else if (Mp4Containers.Contains(type))
            {
                if (type == "meta" && body.Length >= 8 && !body.Slice(4, 4).SequenceEqual("hdlr"u8)) body = body[4..];
                ReadAtoms(body, found, type == "ilst", depth + 1);
            }
            at += (int)size;
        }
    }

    private static void ReadListItem(string type, ReadOnlySpan<byte> item, Collected found)
    {
        if (item.Length < 16 || !item.Slice(4, 4).SequenceEqual("data"u8)) return;
        int dataSize = ReadInt(item, 0);
        if (dataSize < 16 || dataSize > item.Length) return;
        var value = item.Slice(16, dataSize - 16);
        switch (type)
        {
            case "©nam" when found.Title.Length == 0: found.Title = Encoding.UTF8.GetString(value); break;
            case "©ART" when found.Artist.Length == 0: found.Artist = Encoding.UTF8.GetString(value); break;
            case "aART" when found.AlbumArtist.Length == 0: found.AlbumArtist = Encoding.UTF8.GetString(value); break;
            case "©alb" when found.Album.Length == 0: found.Album = Encoding.UTF8.GetString(value); break;
            case "covr": found.AddPicture(value.ToArray(), true); break;
        }
    }
}
