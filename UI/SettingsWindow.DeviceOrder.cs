using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private static readonly TimeSpan DragSlideTime = TimeSpan.FromMilliseconds(160);
        private const double DragScrollEdge = 48, DragScrollStep = 14;

        private sealed class DragUnit(string ip, List<Control> parts)
        {
            public string Ip { get; } = ip;
            public List<Control> Parts { get; } = parts;
            public Rect Origin { get; } = new(parts[0].Bounds.TopLeft, parts[^1].Bounds.BottomRight);
            public Vector Shift { get; private set; }
            public int Slide { get; set; }

            public void Apply(Vector shift)
            {
                Shift = shift;
                foreach (var part in Parts)
                {
                    if (part.RenderTransform is not TranslateTransform move) part.RenderTransform = move = new TranslateTransform();
                    move.X = shift.X;
                    move.Y = shift.Y;
                }
            }

            public void Clear()
            {
                foreach (var part in Parts)
                {
                    part.RenderTransform = null;
                    part.ZIndex = 0;
                }
            }
        }

        private sealed class DeviceDrag(Panel container, bool grid, List<DragUnit> units, DragUnit dragged, Point grab)
        {
            public Panel Container { get; } = container;
            public bool Grid { get; } = grid;
            public List<DragUnit> Units { get; } = units;
            public List<DragUnit> Order { get; } = [.. units];
            public DragUnit Dragged { get; } = dragged;
            public Point Grab { get; } = grab;
            public double Gap { get; } = units.Count > 1 && !grid ? Math.Max(0, units[1].Origin.Top - units[0].Origin.Bottom) : 0;
        }

        private DeviceDrag? _deviceDrag;
        private bool _rebuildAfterDrag;

        private Border DeviceDragHandle(string ip, double size = 32, double iconSize = 20)
        {
            var handle = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent,
                Cursor = new Cursor(_owner.CompactDevices ? StandardCursorType.SizeAll : StandardCursorType.SizeNorthSouth), VerticalAlignment = VerticalAlignment.Center,
                Child = new PathIcon
                {
                    Data = Icons.DragHandle, Width = iconSize, Height = iconSize, Foreground = Palette.TextMuted,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            ToolTip.SetTip(handle, "Drag to change the order");
            handle.PointerEntered += (_, _) => handle.Background = Palette.InputHover;
            handle.PointerExited += (_, _) => { if (_deviceDrag == null) handle.Background = Brushes.Transparent; };
            handle.PointerPressed += (_, e) => StartDeviceDrag(ip, handle, e);
            handle.PointerMoved += (_, e) => MoveDeviceDrag(e);
            handle.PointerReleased += (_, e) =>
            {
                EndDeviceDrag();
                e.Pointer.Capture(null);
                handle.Background = handle.IsPointerOver ? Palette.InputHover : Brushes.Transparent;
            };
            handle.PointerCaptureLost += (_, _) => EndDeviceDrag();
            handle.Tapped += (_, e) => e.Handled = true;
            return handle;
        }

        private (Panel Container, bool Grid, List<DragUnit> Units)? DragUnits()
        {
            if (_owner.CompactDevices)
            {
                if (_compactGrid is not { } grid) return null;
                var cells = grid.Children.Select(child => _cachedCards.FirstOrDefault(c => c.Card.Slot == child))
                    .Where(c => c.Ip != null).Select(c => new DragUnit(c.Ip, [c.Card.Slot])).ToList();
                return (grid, true, cells);
            }
            var groups = new List<(string Ip, List<Control> Parts)>();
            foreach (var child in _deviceList.Children)
            {
                if (_cachedCards.FirstOrDefault(c => c.Card.Slot == child) is { Ip: { } ip }) groups.Add((ip, [child]));
                else if (groups.Count > 0 && _cachedCards.Any(c => c.Card.Members == child)) groups[^1].Parts.Add(child);
            }
            return (_deviceList, false, groups.Select(g => new DragUnit(g.Ip, g.Parts)).ToList());
        }

        private void StartDeviceDrag(string ip, Border handle, PointerPressedEventArgs e)
        {
            if (_deviceDrag != null || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            if (DragUnits() is not { } found || found.Units.Count < 2 || found.Units.FirstOrDefault(u => u.Ip == ip) is not { } dragged) return;
            var grab = e.GetPosition(found.Container) - dragged.Origin.TopLeft;
            _deviceDrag = new DeviceDrag(found.Container, found.Grid, found.Units, dragged, new Point(grab.X, grab.Y));
            foreach (var part in dragged.Parts) part.ZIndex = 1;
            dragged.Apply(default);
            e.Pointer.Capture(handle);
            e.Handled = true;
        }

        private static Point SlotOf(DeviceDrag drag, int position)
        {
            if (drag.Grid) return drag.Units[position].Origin.TopLeft;
            return new Point(drag.Units[0].Origin.Left, drag.Units[0].Origin.Top + drag.Order.Take(position).Sum(u => u.Origin.Height + drag.Gap));
        }

        private static int DropPosition(DeviceDrag drag, Point topLeft)
        {
            var size = drag.Dragged.Origin.Size;
            var centre = topLeft + new Vector(size.Width / 2, size.Height / 2);
            if (drag.Grid)
                return drag.Units.Select((u, i) => (Index: i, Distance: Point.Distance(u.Origin.Center, centre))).MinBy(c => c.Distance).Index;
            return drag.Units.Where(u => u != drag.Dragged).Count(u => centre.Y > u.Origin.Center.Y);
        }

        private void MoveDeviceDrag(PointerEventArgs e)
        {
            if (_deviceDrag is not { } drag) return;
            if (drag.Container.FindAncestorOfType<ScrollViewer>() is { } scroll)
            {
                double inView = e.GetPosition(scroll).Y;
                if (inView < DragScrollEdge) scroll.Offset = scroll.Offset.WithY(Math.Max(0, scroll.Offset.Y - DragScrollStep));
                else if (inView > scroll.Viewport.Height - DragScrollEdge)
                    scroll.Offset = scroll.Offset.WithY(Math.Min(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y + DragScrollStep));
            }
            var topLeft = e.GetPosition(drag.Container) - drag.Grab;
            int position = DropPosition(drag, topLeft);
            drag.Order.Clear();
            drag.Order.AddRange(drag.Units.Where(u => u != drag.Dragged));
            drag.Order.Insert(position, drag.Dragged);
            for (int i = 0; i < drag.Order.Count; i++)
            {
                var unit = drag.Order[i];
                if (unit != drag.Dragged) SlideTo(drag, unit, SlotOf(drag, i) - unit.Origin.TopLeft);
            }
            drag.Dragged.Apply(topLeft - drag.Dragged.Origin.TopLeft);
        }

        private void SlideTo(DeviceDrag drag, DragUnit unit, Vector target, Action? done = null)
        {
            if ((unit.Shift - target).Length < 0.5 && unit.Slide == 0)
            {
                unit.Apply(target);
                done?.Invoke();
                return;
            }
            var from = unit.Shift;
            int slide = ++unit.Slide;
            Animate(drag.Container, DragSlideTime, progress =>
            {
                if (unit.Slide != slide) return;
                double eased = 1 - (1 - progress) * (1 - progress);
                unit.Apply(from + (target - from) * eased);
            }, () =>
            {
                if (unit.Slide != slide) return;
                unit.Slide = 0;
                done?.Invoke();
            });
        }

        private void EndDeviceDrag()
        {
            if (_deviceDrag is not { } drag) return;
            _deviceDrag = null;
            int index = drag.Order.IndexOf(drag.Dragged);
            SlideTo(drag, drag.Dragged, SlotOf(drag, index) - drag.Dragged.Origin.TopLeft, () =>
            {
                foreach (var unit in drag.Units) unit.Clear();
                bool rebuild = _rebuildAfterDrag;
                _rebuildAfterDrag = false;
                string? after = index > 0 ? drag.Order[index - 1].Ip : null, before = index < drag.Order.Count - 1 ? drag.Order[index + 1].Ip : null;
                if (!_owner.MoveDeviceBetween(drag.Dragged.Ip, after, before))
                {
                    if (rebuild) RebuildDeviceCards();
                    return;
                }
                if (!drag.Grid) SetRows(_deviceList, drag.Order.SelectMany(u => u.Parts));
                LoadFromOwner();
                _ = RefreshDeviceStatusesAsync();
            });
        }
    }
}
