using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private enum MascotState { Gone, Push, Dance, WalkOff, RushIn, PushOut, Enter, RunOff, React, Poke, Dizzy, Zip, ZipFall, FlipArt, Whoosh, Act, Stroll, Peek, Skate, Exit, Fly, Magic, Paint, Crate, Ufo, Claw, Sleeve, Jigsaw, Catapult, Held, Dropped, HighFive }

        private enum MascotVisit { Dance, Loud, Wave, Peekaboo, Polish, AirGuitar, Conductor, Paused, Selfie, Skateboard, Balloon, Superhero, Fishing, Climb, SkipRope, PaperPlane, Sweep, Karaoke, Juggling, Kite }

        private enum MascotSwap { Push, Zip, Flip, Whoosh, Magic, Paint, Crate, Ufo, Claw, Sleeve, Jigsaw, Catapult }

        private enum MascotMood { Normal, Loud, Quiet }

        private const double MascotStageWidth = 1040;
        private const double MascotCoverLeft = 220;
        private const double MascotWidth = 200;
        private const double MascotHeight = 240;
        private const double CabinetWidth = 152;
        private const double CabinetHeight = 170;
        private const double CabinetLeft = 24;
        private const double CabinetTop = 32;
        private const double MascotOffstage = -680;
        private const double MascotPushSeconds = 1.7;
        private const double MascotDanceSeconds = 2.0;
        private const double MascotWalkSpeed = 300;
        private const double MascotStompSpeed = 230;
        private const double MascotWalkTarget = -600;
        private const double MascotRushSpeed = 1500;
        private const double MascotRushMinSeconds = 0.35;
        private const double MascotPushOutSeconds = 1.0;
        private const double MascotRushStart = MascotStageWidth + 260;
        private const double MascotDanceSpot = MascotCoverLeft - MascotWidth + 14;
        private const double MascotEnterStart = -320;
        private const double MascotReactSeconds = 2.6;
        private const double MascotPokeSeconds = 0.7;
        private const double MascotDizzySeconds = 2.8;
        private const int MascotPokesToFall = 5;
        private const int MascotLoudAbove = 60;
        private static readonly TimeSpan MascotPokeWindow = TimeSpan.FromSeconds(2.5);
        private const double MascotFlipSeconds = 0.9;
        private const double MascotWhooshSpeed = 2200;
        private const double MascotWhooshSpinSeconds = 1.8;
        private const double MascotWhooshTurns = 2;
        private const double MascotWhooshStart = -320;
        private const double MascotWhooshEnd = MascotStageWidth + 320;
        private double _mascotSpinAt;
        private const double MascotActSeconds = 4.5;
        private const double MascotLookSeconds = 1.2;
        private const double MascotRightSpot = MascotCoverLeft + StageCover - 14;
        private const double MascotVisitChance = 0.3;
        private const double MascotNoteSeconds = 1.6;
        private const double MascotNoteEvery = 0.35;
        private static readonly TimeSpan MascotQuietBeforeVisit = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan MascotBetweenVisits = TimeSpan.FromMinutes(1);
        private static readonly string[] MascotNoteGlyphs = ["♪", "♫", "♬"];
        private readonly DispatcherTimer _mascotVisitTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        private readonly Canvas _mascotNotes = new() { IsHitTestVisible = false };
        private readonly List<(TextBlock Note, double Born, double Drift)> _mascotNoteList = [];
        private MascotVisit _mascotVisit;
        private bool _mascotFromRight;
        private DateTime _mascotLastActive = DateTime.UtcNow;
        private DateTime _mascotLastArtChange = DateTime.UtcNow;
        private DateTime _mascotLastVisit = DateTime.MinValue;
        private double _mascotNextNote;
        private const double MascotZipSeconds = 1.8;
        private const double MascotZipDistance = 760;
        private static readonly Point MascotWireHand = new(16, 66);
        private const double MascotHangTop = 40;
        private const double MascotZipFallSeconds = 2.0;
        private const double MascotWireRetractSeconds = 0.5;

        private readonly Canvas _mascot = new() { Width = MascotWidth, Height = MascotHeight };
        private readonly ScaleTransform _mascotFacing = new();
        private readonly RotateTransform _mascotLean = new();
        private readonly TranslateTransform _mascotBounce = new();
        private readonly RotateTransform _mascotLeftArm = new();
        private readonly RotateTransform _mascotRightArm = new();
        private readonly RotateTransform _mascotLeftLeg = new();
        private readonly RotateTransform _mascotRightLeg = new();
        private readonly ScaleTransform _mascotCone = new();
        private readonly ScaleTransform _mascotEyes = new();
        private readonly TranslateTransform _mascotGaze = new();
        private readonly Canvas _mascotBrows = new() { IsVisible = false };
        private bool _mascotAngry;
        private readonly Ellipse _mascotShadow = new() { Width = 130, Height = 16, Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)) };
        private readonly Canvas _mascotSpeed = new() { Opacity = 0 };
        private readonly FrameTimer _mascotTimer = new();
        private MascotState _mascotState = MascotState.Gone;
        private DateTime _mascotStart = DateTime.UtcNow;
        private double _mascotRunFrom;
        private double _mascotRushSeconds = MascotRushMinSeconds;
        private double _mascotEnterSpeed = MascotRushSpeed;
        private MascotState _mascotAfterEnter = MascotState.Dance;
        private MascotState _mascotResume = MascotState.WalkOff;
        private bool _mascotEncore;
        private MascotMood _mascotMoodSeen;
        private readonly List<DateTime> _mascotPokes = [];
        private readonly Canvas _zipLayer = new() { IsHitTestVisible = false, IsVisible = false };
        private readonly Line _zipWire = new() { Stroke = new SolidColorBrush(Color.Parse("#B8BDC4")), StrokeThickness = 3 };
        private MascotSwap _mascotSwap;
        private bool _mascotSwapped;

        private bool MascotBusy => _mascotState is MascotState.Push or MascotState.RushIn or MascotState.PushOut or MascotState.Zip or MascotState.FlipArt or MascotState.Whoosh
                                       or MascotState.Magic or MascotState.Paint or MascotState.Crate or MascotState.Ufo or MascotState.Claw or MascotState.Sleeve
                                       or MascotState.Jigsaw or MascotState.Catapult ||
                                   _mascotState == MascotState.Enter && _mascotAfterEnter is MascotState.FlipArt or MascotState.Magic or MascotState.Paint or MascotState.Crate
                                       or MascotState.Ufo or MascotState.Claw or MascotState.Sleeve or MascotState.Jigsaw or MascotState.Catapult;

        private double MascotSpot => _mascotFromRight ? MascotRightSpot : MascotDanceSpot;

        private double MascotSide => _mascotFromRight ? -1 : 1;

        private void StartMascotEnter(MascotState then, double speed, bool fromRight = false)
        {
            _mascotFromRight = fromRight;
            _mascotRunFrom = _mascotState != MascotState.Gone ? Canvas.GetLeft(_mascot) : fromRight ? MascotWhooshEnd : MascotEnterStart;
            _mascotAfterEnter = then;
            _mascotEnterSpeed = speed;
            SetMascotState(MascotState.Enter);
            UpdateMascotTimer();
        }

        private void PokeMascot()
        {
            if (_mascotState is not (MascotState.Dance or MascotState.WalkOff or MascotState.React or MascotState.Poke or MascotState.Act)) return;
            var now = DateTime.UtcNow;
            _mascotPokes.RemoveAll(t => now - t > MascotPokeWindow);
            _mascotPokes.Add(now);
            if (_mascotPokes.Count >= MascotPokesToFall)
            {
                _mascotPokes.Clear();
                SetMascotState(MascotState.Dizzy);
                return;
            }
            if (_mascotState != MascotState.Poke) _mascotResume = _mascotState;
            SetMascotState(MascotState.Poke);
        }

        private MascotMood CurrentMood()
        {
            if (_npIp is not { } ip || !_volumes.TryGetValue(TargetOf(ip), out var volume)) return MascotMood.Normal;
            return volume.Muted || volume.Volume == 0 ? MascotMood.Quiet : volume.Volume > MascotLoudAbove ? MascotMood.Loud : MascotMood.Normal;
        }

        private void MascotVolumeChanged()
        {
            if (NpStyle != NowPlayingStyle.Mascot || !_npOverlay.IsVisible) return;
            if (_npIp is { } ip && _volumes.TryGetValue(TargetOf(ip), out var level)) MascotVolumeTurned(level.Muted ? 0 : level.Volume);
            var mood = CurrentMood();
            if (mood == _mascotMoodSeen) return;
            _mascotMoodSeen = mood;
            if (mood != MascotMood.Normal && _mascotState == MascotState.Gone) StartMascotEnter(MascotState.React, MascotWalkSpeed);
        }

        private void MoodPose(double s, MascotMood mood)
        {
            if (mood == MascotMood.Loud)
            {
                double wince = Math.Sin(s * 10);
                Pose(lean: Math.Sin(s * 20) * 3, bounce: -Math.Abs(wince) * 3, arms: (196, -196), legs: (wince * 6, -wince * 6));
                _mascotEyes.ScaleY = 0.35;
                return;
            }
            double shrug = Math.Max(0, Math.Sin(s * Math.PI * 1.2));
            Pose(lean: 0, bounce: -shrug * 6, arms: (55 + shrug * 15, -55 - shrug * 15), legs: (0, Math.Sin(s * Math.PI * 4) > 0 ? -18 : 0));
        }

        private void StartMascotPush()
        {
            SetMascotState(MascotState.Push);
            Canvas.SetLeft(_npCoverCard, MascotOffstage);
            _npCoverCard.Opacity = 1;
            UpdateMascotTimer();
        }

        private void StartMascotSwap()
        {
            HideMascotTricks();
            ResetCoverPlacement();
            _npFadeTimer.Stop();
            _npCoverOld.Opacity = 1;
            foreach (var (_, faded) in _npArtShapes) faded.Opacity = 1;
            BlendBackdrops(1);
            _mascotEncore = false;
            _mascotAngry = false;
            _mascotFromRight = false;
            _mascotLastArtChange = DateTime.UtcNow;
            _mascot.ZIndex = 0;
            ClearMascotNotes();
            var regular = Enum.GetValues<MascotSwap>().Where(kind => kind != MascotSwap.Ufo).ToArray();
            _mascotSwap = Random.Shared.NextDouble() < MascotUfoChance ? MascotSwap.Ufo : PickOther(regular, _mascotSwap, _mascotSwapped);
            if (_mascotSwap == MascotSwap.Jigsaw && !PrepareJigsaw()) _mascotSwap = MascotSwap.Crate;
            _mascotSwapped = true;
            _zipLayer.IsVisible = false;
            _npCoverCard.ClearValue(Canvas.TopProperty);
            if (_mascotSwap == MascotSwap.Whoosh)
            {
                Canvas.SetLeft(_npCoverCard, MascotCoverLeft);
                _mascotRunFrom = _mascotState == MascotState.Gone ? MascotWhooshStart : Canvas.GetLeft(_mascot);
                _mascotSpinAt = Math.Max(0, (MascotCoverLeft + StageCover / 2 - MascotWidth / 2 - _mascotRunFrom) / MascotWhooshSpeed);
                SetMascotState(MascotState.Whoosh);
                UpdateMascotTimer();
                return;
            }
            if (_mascotSwap == MascotSwap.Flip)
            {
                Canvas.SetLeft(_npCoverCard, MascotCoverLeft);
                StartMascotEnter(MascotState.FlipArt, MascotRushSpeed);
                return;
            }
            if (_mascotSwap is MascotSwap.Magic or MascotSwap.Paint or MascotSwap.Crate or MascotSwap.Ufo or MascotSwap.Claw or MascotSwap.Sleeve or MascotSwap.Jigsaw
                or MascotSwap.Catapult)
            {
                Canvas.SetLeft(_npCoverCard, MascotCoverLeft);
                StartMascotEnter(_mascotSwap switch
                {
                    MascotSwap.Magic => MascotState.Magic,
                    MascotSwap.Paint => MascotState.Paint,
                    MascotSwap.Crate => MascotState.Crate,
                    MascotSwap.Claw => MascotState.Claw,
                    MascotSwap.Sleeve => MascotState.Sleeve,
                    MascotSwap.Jigsaw => MascotState.Jigsaw,
                    MascotSwap.Catapult => MascotState.Catapult,
                    _ => MascotState.Ufo
                }, MascotRushSpeed);
                return;
            }
            _mascotRunFrom = _mascotState == MascotState.Gone ? MascotRushStart : Canvas.GetLeft(_mascot);
            _mascotRushSeconds = Math.Max(MascotRushMinSeconds, Math.Abs(MascotPushOutLeft - _mascotRunFrom) / MascotRushSpeed);
            SetMascotState(MascotState.RushIn);
            UpdateMascotTimer();
        }

        private static double MascotPushOutLeft => MascotCoverLeft + StageCover - 14;

        private void SetMascotState(MascotState state)
        {
            _mascotState = state;
            _mascotStart = DateTime.UtcNow;
            if (state == MascotState.Gone)
            {
                HideMascotExtras();
                _mascot.ZIndex = 0;
                Canvas.SetLeft(_mascot, MascotWalkTarget - MascotWidth);
            }
            if (state == MascotState.Act) _mascotNextNote = 0;
        }

        private void UpdateMascotTimer()
        {
            bool shown = _npOverlay.IsVisible && NpStyle == NowPlayingStyle.Mascot;
            bool run = shown && _mascotState != MascotState.Gone;
            if (run && !_mascotTimer.IsEnabled) _mascotTimer.Start();
            else if (!run) _mascotTimer.Stop();
            if (shown && !_mascotVisitTimer.IsEnabled) _mascotVisitTimer.Start();
            else if (!shown) _mascotVisitTimer.Stop();
        }

        private void CheckMascotVisit()
        {
            var now = DateTime.UtcNow;
            if (_mascotState != MascotState.Gone || !_npOverlay.IsVisible) return;
            var quietSince = _mascotLastActive > _mascotLastArtChange ? _mascotLastActive : _mascotLastArtChange;
            if (now - quietSince < MascotQuietBeforeVisit || now - _mascotLastVisit < MascotBetweenVisits) return;
            if (Random.Shared.NextDouble() >= MascotVisitChance) return;
            _mascotLastVisit = now;
            StartMascotVisit(PickMascotVisit());
        }

        private void LookAtPhone()
        {
            if (_mascotVisit != MascotVisit.Selfie) return;
            _mascotRightArm.Angle = -115;
            Center(_propPhone, Hand(true, -115, 50));
            (_mascotGaze.X, _mascotGaze.Y) = (5, 4);
        }

        private void ClearMascotNotes()
        {
            _mascotNotes.Children.Clear();
            _mascotNoteList.Clear();
        }

        private void StepMascotNotes(double s)
        {
            if (s >= _mascotNextNote && s < MascotActLength() - 0.6)
            {
                _mascotNextNote = s + MascotNoteEvery;
                var note = new TextBlock
                {
                    Text = MascotNoteGlyphs[Random.Shared.Next(MascotNoteGlyphs.Length)], FontSize = 26 + Random.Shared.Next(12),
                    Foreground = Random.Shared.Next(2) == 0 ? Palette.AccentText : Brushes.White
                };
                _mascotNotes.Children.Add(note);
                _mascotNoteList.Add((note, s, Random.Shared.NextDouble() * 2 - 1));
            }
            for (int i = _mascotNoteList.Count - 1; i >= 0; i--)
            {
                var (note, born, drift) = _mascotNoteList[i];
                double age = (s - born) / MascotNoteSeconds;
                if (age >= 1 || age < 0)
                {
                    _mascotNotes.Children.Remove(note);
                    _mascotNoteList.RemoveAt(i);
                    continue;
                }
                Canvas.SetLeft(note, 90 + drift * 60 * age + Math.Sin(age * 9 + drift * 3) * 10);
                Canvas.SetTop(note, 10 - age * 150);
                note.Opacity = age < 0.15 ? age / 0.15 : 1 - (age - 0.15) / 0.85;
            }
        }

        private void FinishMascotSwap()
        {
            _npCoverOld.Opacity = 0;
            _npCoverOld.Source = null;
            foreach (var (_, faded) in _npArtShapes)
            {
                faded.Opacity = 0;
                faded.Fill = null;
            }
            ReleaseNowPlayingBackdrop();
            BlendBackdrops(0);
        }

        private void StepMascot()
        {
            double s = (DateTime.UtcNow - _mascotStart).TotalSeconds;
            double top = MascotOffsetTop();
            _mascotSpeed.Opacity = 0;
            _mascotFacing.ScaleX = 1;
            if (_mascotState != MascotState.Gone) _mascotLastActive = DateTime.UtcNow;
            HideMascotProps();
            HideMascotIdle();
            HideMascotTricks();
            HideMascotStageProps();
            HideMascotExtras();
            _mascotScale.ScaleX = _mascotScale.ScaleY = 1;
            _mascotHeadroom = 0;
            _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1;
            _mascotGaze.X = _mascotGaze.Y = 0;
            _mascotShadow.Opacity = 1;
            _mascotBrows.IsVisible = _mascotAngry;
            var mood = CurrentMood();
            switch (_mascotState)
            {
                case MascotState.Push:
                {
                    double p = Math.Min(1, s / MascotPushSeconds);
                    double cover = MascotOffstage + (MascotCoverLeft - MascotOffstage) * (1 - Math.Pow(1 - p, 3));
                    Canvas.SetLeft(_npCoverCard, cover);
                    Canvas.SetLeft(_mascot, cover - MascotWidth + 14);
                    if (_npBackdropOld.Source != null) BlendBackdrops(1 - p);
                    Pose(lean: 14, bounce: -Math.Abs(Math.Sin(s * 14)) * 5, arms: (-95, -85), legs: Run(s, 16, 38));
                    if (p >= 1)
                    {
                        if (_npBackdropOld.Source != null || _npCoverOld.Source != null) FinishMascotSwap();
                        SetMascotState(MascotState.Dance);
                    }
                    break;
                }
                case MascotState.Dance:
                {
                    double jump = s > 1.45 ? -Math.Sin(Math.Min(1, (s - 1.45) / 0.5) * Math.PI) * 46 : 0;
                    Canvas.SetLeft(_mascot, MascotDanceSpot);
                    if (mood != MascotMood.Normal) MoodPose(s, mood);
                    else CheerDance(s, jump);
                    if (LyricsShown && _npControls?.Playing.IsVisible == true)
                    {
                        _mascotRightArm.Angle = -150;
                        Sing(s, LyricsSinging());
                    }
                    if (s >= MascotDanceSeconds)
                    {
                        _mascotRunFrom = Canvas.GetLeft(_mascot);
                        _mascotCone.ScaleX = _mascotCone.ScaleY = 1;
                        SetMascotState(_mascotEncore ? MascotState.RunOff : MascotState.WalkOff);
                        _mascotEncore = false;
                    }
                    break;
                }
                case MascotState.RushIn:
                {
                    double p = Math.Min(1, s / _mascotRushSeconds);
                    double x = _mascotRunFrom + (MascotPushOutLeft - _mascotRunFrom) * p;
                    bool right = MascotPushOutLeft > _mascotRunFrom;
                    _mascotFacing.ScaleX = right ? 1 : -1;
                    Canvas.SetLeft(_mascot, x);
                    _mascotSpeed.Opacity = p < 0.85 ? 1 : (1 - p) / 0.15;
                    RunPose(s, right ? 1 : -1);
                    if (p >= 1) SetMascotState(MascotState.PushOut);
                    break;
                }
                case MascotState.PushOut:
                {
                    double p = Math.Min(1, s / MascotPushOutSeconds);
                    double cover = MascotCoverLeft + (MascotOffstage - MascotCoverLeft) * p * p;
                    _mascotFacing.ScaleX = -1;
                    Canvas.SetLeft(_npCoverCard, cover);
                    Canvas.SetLeft(_mascot, cover + StageCover - 14);
                    Pose(lean: -14, bounce: -Math.Abs(Math.Sin(s * 14)) * 5, arms: (-95, -85), legs: Run(s, 16, 38));
                    if (p >= 1)
                    {
                        _npCoverOld.Opacity = 0;
                        foreach (var (_, faded) in _npArtShapes) faded.Opacity = 0;
                        if (_mascotSwap == MascotSwap.Zip)
                        {
                            _zipLayer.IsVisible = true;
                            _zipLayer.Opacity = 1;
                            SetMascotState(MascotState.Zip);
                        }
                        else StartMascotPush();
                    }
                    break;
                }
                case MascotState.Zip:
                {
                    double p = Math.Min(1, s / MascotZipSeconds);
                    double travel = (1 - Math.Pow(1 - p, 2.4)) - 1;
                    double coverTop = travel * MascotZipDistance;
                    Canvas.SetLeft(_npCoverCard, MascotCoverLeft);
                    Canvas.SetTop(_npCoverCard, coverTop);
                    Canvas.SetLeft(_mascot, MascotCoverLeft - MascotWidth);
                    top = coverTop + MascotHangTop;
                    PlaceWire(top);
                    _mascotShadow.Opacity = 0;
                    if (_npBackdropOld.Source != null) BlendBackdrops(1 - p);
                    double sway = Math.Sin(s * 7) * 12;
                    Pose(lean: 0, bounce: 0, arms: (170, -118), legs: (14 + sway, 4 + sway));
                    if (p >= 1)
                    {
                        if (_npBackdropOld.Source != null || _npCoverOld.Source != null) FinishMascotSwap();
                        _npCoverCard.ClearValue(Canvas.TopProperty);
                        SetMascotState(MascotState.ZipFall);
                    }
                    break;
                }
                case MascotState.ZipFall:
                {
                    const double fromX = MascotCoverLeft - MascotWidth;
                    double retract = Math.Min(1, s / MascotWireRetractSeconds);
                    PlaceWire(MascotHangTop - retract * retract * (MascotHangTop + MascotWireHand.Y + 500));
                    if (retract >= 1) _zipLayer.IsVisible = false;
                    double fall = Math.Clamp((s - 0.1) / 0.8, 0, 1);
                    Canvas.SetLeft(_mascot, fromX + (MascotDanceSpot - fromX) * fall);
                    top = MascotHangTop + (MascotOffsetTop() - MascotHangTop) * fall * fall;
                    _mascotShadow.Opacity = fall;
                    double lean = s < 0.9 ? -85 * fall : s < 1.5 ? -85 : s < 2.0 ? -85 * (2.0 - s) / 0.5 : 0;
                    if (s is >= 0.9 and < 1.5) _mascotEyes.ScaleY = 0.15;
                    Pose(lean: lean, bounce: 0, arms: s < 0.9 ? (140 + Math.Sin(s * 30) * 30, -140 - Math.Sin(s * 30) * 30) : (30, -30), legs: s < 0.9 ? (Math.Sin(s * 26) * 30, -Math.Sin(s * 26) * 30) : (0, 0));
                    if (s >= MascotZipFallSeconds)
                    {
                        _mascotRunFrom = MascotDanceSpot;
                        SetMascotState(MascotState.RunOff);
                    }
                    break;
                }
                case MascotState.Act:
                {
                    Canvas.SetLeft(_mascot, MascotSpot);
                    _mascotFacing.ScaleX = MascotSide;
                    top = MascotOffsetTop() + StepMascotAct(s);
                    if (s >= MascotActLength()) FinishMascotAct();
                    break;
                }
                case MascotState.Fly:
                    top = MascotOffsetTop() + StepMascotFly(s);
                    break;
                case MascotState.Magic:
                    top = MascotOffsetTop() + StepMascotMagic(s);
                    break;
                case MascotState.Paint:
                    top = MascotOffsetTop() + StepMascotPaint(s);
                    break;
                case MascotState.Crate:
                    top = MascotOffsetTop() + StepMascotCrate(s);
                    break;
                case MascotState.Ufo:
                    top = MascotOffsetTop() + StepMascotUfo(s);
                    break;
                case MascotState.Claw:
                    top = MascotOffsetTop() + StepMascotClaw(s);
                    break;
                case MascotState.Sleeve:
                    top = MascotOffsetTop() + StepMascotSleeve(s);
                    break;
                case MascotState.Jigsaw:
                    top = MascotOffsetTop() + StepMascotJigsaw(s);
                    break;
                case MascotState.Catapult:
                    top = MascotOffsetTop() + StepMascotCatapult(s);
                    break;
                case MascotState.Held:
                    top = StepMascotHeld(s);
                    break;
                case MascotState.Dropped:
                    top = StepMascotDropped(s);
                    break;
                case MascotState.HighFive:
                    StepMascotHighFive(s);
                    break;
                case MascotState.Exit:
                    top = MascotOffsetTop() + StepMascotExit(s);
                    break;
                case MascotState.Peek:
                    StepMascotPeek(s);
                    break;
                case MascotState.Skate:
                    StepMascotSkate(s);
                    break;
                case MascotState.Stroll:
                {
                    double from = MascotSpot, to = _mascotFromRight ? MascotDanceSpot : MascotRightSpot, dir = Math.Sign(to - from);
                    double walk = Math.Abs(to - from) / MascotWalkSpeed;
                    if (s < walk)
                    {
                        _mascot.ZIndex = -1;
                        _mascotFacing.ScaleX = dir;
                        Canvas.SetLeft(_mascot, from + dir * s * MascotWalkSpeed);
                        WalkPose(s, dir);
                        LookAtPhone();
                    }
                    else if (s < walk + MascotLookSeconds)
                    {
                        _mascot.ZIndex = 0;
                        Canvas.SetLeft(_mascot, to);
                        _mascotFacing.ScaleX = s - walk < MascotLookSeconds / 2 ? -dir : dir;
                        Pose(lean: 0, bounce: 0, arms: (20, -20), legs: (0, 0));
                        LookAtPhone();
                    }
                    else
                    {
                        double x = to + dir * (s - walk - MascotLookSeconds) * MascotWalkSpeed;
                        _mascotFacing.ScaleX = dir;
                        Canvas.SetLeft(_mascot, x);
                        WalkPose(s, dir);
                        LookAtPhone();
                        if (dir > 0 ? x >= MascotWhooshEnd : x <= MascotWalkTarget)
                        {
                            SetMascotState(MascotState.Gone);
                            UpdateMascotTimer();
                        }
                    }
                    break;
                }
                case MascotState.Whoosh:
                {
                    double x = Math.Min(MascotWhooshEnd, _mascotRunFrom + s * MascotWhooshSpeed);
                    Canvas.SetLeft(_mascot, x);
                    _mascotSpeed.Opacity = 1;
                    double stride = Math.Sin(s * 30);
                    Pose(lean: 20, bounce: -Math.Abs(stride) * 6, arms: (-70 + stride * 55, 70 - stride * 55), legs: Run(s, 30, 50));
                    double p = Math.Clamp((s - _mascotSpinAt) / MascotWhooshSpinSeconds, 0, 1);
                    double angle = (1 - Math.Pow(1 - p, 3)) * MascotWhooshTurns * 360;
                    double face = angle % 360;
                    _npCoverFlip.AngleY = face is > 90 and < 270 ? face - 180 : face >= 270 ? face - 360 : face;
                    _npCoverOld.Opacity = angle < 90 ? 1 : 0;
                    foreach (var (_, faded) in _npArtShapes) faded.Opacity = _npCoverOld.Opacity;
                    if (_npBackdropOld.Source != null) BlendBackdrops(1 - p);
                    if (p >= 1 && x >= MascotWhooshEnd)
                    {
                        _npCoverFlip.AngleY = 0;
                        FinishMascotSwap();
                        SetMascotState(MascotState.Gone);
                        UpdateMascotTimer();
                    }
                    break;
                }
                case MascotState.FlipArt:
                {
                    double p = Math.Min(1, s / MascotFlipSeconds);
                    double angle = Ease(p) * 180;
                    bool front = angle < 90;
                    _npCoverFlip.AngleY = front ? angle : angle - 180;
                    _npCoverOld.Opacity = front ? 1 : 0;
                    foreach (var (_, faded) in _npArtShapes) faded.Opacity = _npCoverOld.Opacity;
                    if (_npBackdropOld.Source != null) BlendBackdrops(1 - Ease(p));
                    Canvas.SetLeft(_mascot, MascotDanceSpot);
                    double swing = Math.Min(1, p * 2.5);
                    Pose(lean: 10 * (1 - p), bounce: -Math.Sin(p * Math.PI) * 50, arms: (120, -40 - swing * 110), legs: (-18 * (1 - p), 18 * (1 - p)));
                    if (p >= 1)
                    {
                        _npCoverFlip.AngleY = 0;
                        FinishMascotSwap();
                        SetMascotState(MascotState.Dance);
                    }
                    break;
                }
                case MascotState.Enter:
                {
                    double target = MascotSpot;
                    double dir = Math.Sign(target - _mascotRunFrom);
                    double x = _mascotRunFrom + dir * s * _mascotEnterSpeed;
                    bool arrived = dir == 0 || (dir > 0 ? x >= target : x <= target);
                    bool rushing = _mascotEnterSpeed > MascotWalkSpeed;
                    Canvas.SetLeft(_mascot, arrived ? target : x);
                    _mascotFacing.ScaleX = dir < 0 ? -1 : 1;
                    _mascotSpeed.Opacity = rushing ? 1 : 0;
                    if (rushing) RunPose(s, dir < 0 ? -1 : 1);
                    else WalkPose(s, dir < 0 ? -1 : 1);
                    if (arrived) SetMascotState(_mascotAfterEnter);
                    break;
                }
                case MascotState.React:
                {
                    Canvas.SetLeft(_mascot, MascotDanceSpot);
                    if (!VolumePose(s))
                    {
                        if (mood != MascotMood.Normal) MoodPose(s, mood);
                        else Pose(lean: 0, bounce: Math.Sin(s * 2) * 2, arms: (20, -150 + Math.Sin(s * 10) * 25), legs: (0, 0));
                    }
                    if (s >= MascotReactSeconds && !VolumeTurnedRecently)
                    {
                        _mascotRunFrom = MascotDanceSpot;
                        SetMascotState(MascotState.WalkOff);
                    }
                    break;
                }
                case MascotState.Poke:
                {
                    double p = Math.Min(1, s / MascotPokeSeconds);
                    _mascotFacing.ScaleX = _mascotResume == MascotState.WalkOff ? -1 : 1;
                    _mascotEyes.ScaleX = _mascotEyes.ScaleY = 1.35;
                    _mascotCone.ScaleX = _mascotCone.ScaleY = 1 + Math.Abs(Math.Sin(s * 30)) * 0.15 * (1 - p);
                    Pose(lean: 0, bounce: -Math.Sin(p * Math.PI) * 40, arms: (130, -130), legs: (-15, 15));
                    if (p >= 1)
                    {
                        _mascotCone.ScaleX = _mascotCone.ScaleY = 1;
                        _mascotRunFrom = Canvas.GetLeft(_mascot);
                        SetMascotState(_mascotResume);
                    }
                    break;
                }
                case MascotState.Dizzy:
                {
                    double lean = s < 0.4 ? -s / 0.4 * 85 : s < 1.0 ? -85 : s < 1.5 ? -(1.5 - s) / 0.5 * 85 : Math.Sin(s * 6) * 10 * Math.Max(0, (MascotDizzySeconds - s) / 1.3);
                    if (s is >= 0.4 and < 1.0) _mascotEyes.ScaleY = 0.15;
                    Pose(lean: lean, bounce: 0, arms: s < 0.4 ? (140, -140) : (30 + Math.Sin(s * 6) * 20, -30 + Math.Sin(s * 6) * 20), legs: (0, 0));
                    if (s >= MascotDizzySeconds)
                    {
                        _mascotRunFrom = Canvas.GetLeft(_mascot);
                        _mascotAngry = true;
                        SetMascotState(MascotState.WalkOff);
                    }
                    break;
                }
                case MascotState.RunOff:
                {
                    double dir = _mascotFromRight ? 1 : -1;
                    double x = _mascotRunFrom + dir * s * MascotRushSpeed;
                    _mascotFacing.ScaleX = dir;
                    _mascotSpeed.Opacity = 1;
                    Canvas.SetLeft(_mascot, x);
                    RunPose(s, dir);
                    if (dir < 0 ? x <= MascotWalkTarget : x >= MascotWhooshEnd)
                    {
                        SetMascotState(MascotState.Gone);
                        UpdateMascotTimer();
                    }
                    break;
                }
                case MascotState.WalkOff:
                {
                    double x = _mascotRunFrom - s * (_mascotAngry ? MascotStompSpeed : MascotWalkSpeed);
                    _mascotFacing.ScaleX = -1;
                    Canvas.SetLeft(_mascot, x);
                    if (_mascotAngry)
                    {
                        _mascotEyes.ScaleY = 0.7;
                        double stomp = Math.Sin(s * 7);
                        Pose(lean: -9, bounce: -Math.Abs(stomp) * 10, arms: (35 - stomp * 10, -35 - stomp * 10), legs: Run(s, 7, 34));
                    }
                    else WalkPose(s, -1);
                    if (x <= MascotWalkTarget)
                    {
                        _mascotAngry = false;
                        SetMascotState(MascotState.Gone);
                        UpdateMascotTimer();
                    }
                    break;
                }
            }
            VolumeWind(s);
            ApplyMascotHeadroom(_mascotHeadroom);
            Canvas.SetTop(_mascot, top);
        }

        private void PlaceWire(double mascotTop)
        {
            double x = MascotCoverLeft - MascotWidth + MascotWireHand.X;
            _zipWire.StartPoint = new Point(x, -2000);
            _zipWire.EndPoint = new Point(x, mascotTop + MascotWireHand.Y);
        }

        private static double MascotOffsetTop() => StageCover - MascotHeight + 8;
    }
}
