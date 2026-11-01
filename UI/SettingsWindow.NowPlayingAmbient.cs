using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {

        private readonly Grid _npAmbient = new();
        private readonly Border _npAmbientControls = new()
        {
            Width = 620, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(26, 18), CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            Transitions = FadeTransitions()
        };
        private bool _npDrifting;
        private readonly Rotate3DTransform _npAmbientFlip = new() { Depth = 1500 };

        private void BuildAmbient()
        {
            var art = new Rectangle { Width = 360, Height = 360, RadiusX = 10, RadiusY = 10, Fill = Palette.Input };
            var old = new Rectangle { Width = 360, Height = 360, RadiusX = 10, RadiusY = 10, Opacity = 0, IsHitTestVisible = false };
            _npArtShapes.Add((art, old));
            var shadow = new DropShadowEffect { BlurRadius = 20, OffsetX = 0, OffsetY = 2, Color = Colors.Black, Opacity = 0.55 };
            var title = new TextBlock
            {
                FontSize = 60, FontWeight = FontWeight.Light, Foreground = Brushes.White, TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 1180, Margin = new Thickness(0, 44, 0, 0), Effect = shadow
            };
            var artist = new TextBlock
            {
                FontSize = 28, FontWeight = FontWeight.Light, Foreground = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)),
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 1180, Margin = new Thickness(0, 8, 0, 0),
                Effect = shadow
            };
            _npTitleCopies.Add(title);
            _npArtistCopies.Add(artist);
            var column = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 30, 0, 0),
                Children =
                {
                    new Border
                    {
                        Width = 360, Height = 360, CornerRadius = new CornerRadius(10), BoxShadow = BoxShadows.Parse("0 24 70 0 #B0000000"),
                        HorizontalAlignment = HorizontalAlignment.Center, Child = new Panel { Children = { art, old } },
                        RenderTransform = _npAmbientFlip
                    },
                    title, artist
                }
            };
            _npAmbient.Children.Add(new Viewbox
            {
                Stretch = Stretch.Uniform, Margin = new Thickness(40, 64, 40, 24),
                Child = new Panel { Width = 1280, Height = 900, Children = { column, _npAmbientControls } }
            });
            _npBackdropLayer.SizeChanged += (_, _) =>
            {
                if (ElementComposition.GetElementVisual(_npBackdropLayer) is { } visual) visual.CenterPoint = BackdropCenter();
            };
        }

        private Vector3D BackdropCenter() => new(_npBackdropLayer.Bounds.Width / 2, _npBackdropLayer.Bounds.Height / 2, 0);

        private void UpdateAmbientDrift(bool drift)
        {
            if (drift == _npDrifting || ElementComposition.GetElementVisual(_npBackdropLayer) is not { } visual) return;
            _npDrifting = drift;
            visual.StopAnimation("Scale");
            visual.StopAnimation("RotationAngle");
            visual.Scale = new Vector3D(1, 1, 1);
            visual.RotationAngle = 0;
            if (!drift) return;
            visual.CenterPoint = BackdropCenter();
            var scale = visual.Compositor.CreateVector3DKeyFrameAnimation();
            scale.InsertKeyFrame(0, new Vector3D(1.12, 1.12, 1), new SineEaseInOut());
            scale.InsertKeyFrame(0.5f, new Vector3D(1.32, 1.32, 1), new SineEaseInOut());
            scale.InsertKeyFrame(1, new Vector3D(1.12, 1.12, 1), new SineEaseInOut());
            scale.Duration = TimeSpan.FromSeconds(28);
            scale.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Scale", scale);
            var turn = visual.Compositor.CreateScalarKeyFrameAnimation();
            turn.InsertKeyFrame(0, -0.05f, new SineEaseInOut());
            turn.InsertKeyFrame(0.5f, 0.05f, new SineEaseInOut());
            turn.InsertKeyFrame(1, -0.05f, new SineEaseInOut());
            turn.Duration = TimeSpan.FromSeconds(43);
            turn.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("RotationAngle", turn);
        }
    }
}
