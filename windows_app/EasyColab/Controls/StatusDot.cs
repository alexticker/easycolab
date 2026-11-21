using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace EasyColab.Controls;

/// <summary>
/// Port of Swift <c>StatusDot(color:)</c>: an 8×8 filled circle.
/// <c>Color</c> is a <see cref="Brush"/> so theme brushes can be used
/// (e.g. <c>{ThemeResource StudioSystemGreenBrush}</c>, <c>StudioSystemOrangeBrush</c>, <c>StudioSecondaryOpacity45Brush</c>).
/// <c>IsPulsing</c> (Windows addition, default off — Swift has no animation) adds a soft expanding ring.
/// </summary>
public sealed class StatusDot : Grid
{
    private readonly Ellipse _dot;
    private readonly Ellipse _pulse;
    private readonly ScaleTransform _pulseScale = new();
    private Storyboard? _storyboard;

    public StatusDot()
    {
        Width = 8;
        Height = 8;
        VerticalAlignment = VerticalAlignment.Center;
        _pulse = new Ellipse
        {
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = _pulseScale,
        };
        _dot = new Ellipse();
        Children.Add(_pulse);
        Children.Add(_dot);
        ApplyColor();
        Unloaded += (_, _) => StopPulse();
        Loaded += (_, _) => { if (IsPulsing) StartPulse(); };
    }

    public Brush? Color
    {
        get => (Brush?)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Brush), typeof(StatusDot),
        new PropertyMetadata(null, (d, _) => ((StatusDot)d).ApplyColor()));

    public bool IsPulsing
    {
        get => (bool)GetValue(IsPulsingProperty);
        set => SetValue(IsPulsingProperty, value);
    }

    public static readonly DependencyProperty IsPulsingProperty = DependencyProperty.Register(
        nameof(IsPulsing), typeof(bool), typeof(StatusDot),
        new PropertyMetadata(false, (d, e) =>
        {
            var s = (StatusDot)d;
            if ((bool)e.NewValue) s.StartPulse(); else s.StopPulse();
        }));

    private void ApplyColor()
    {
        _dot.Fill = Color;
        _pulse.Fill = Color;
    }

    private void StartPulse()
    {
        if (_storyboard is not null || !IsLoaded) return;
        var duration = new Duration(TimeSpan.FromSeconds(1.2));
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var sx = new DoubleAnimation { From = 1.0, To = 2.4, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(sx, _pulseScale);
        Storyboard.SetTargetProperty(sx, nameof(ScaleTransform.ScaleX));
        var sy = new DoubleAnimation { From = 1.0, To = 2.4, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(sy, _pulseScale);
        Storyboard.SetTargetProperty(sy, nameof(ScaleTransform.ScaleY));
        var op = new DoubleAnimation { From = 0.55, To = 0.0, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(op, _pulse);
        Storyboard.SetTargetProperty(op, nameof(UIElement.Opacity));

        sb.Children.Add(sx);
        sb.Children.Add(sy);
        sb.Children.Add(op);
        _storyboard = sb;
        sb.Begin();
    }

    private void StopPulse()
    {
        _storyboard?.Stop();
        _storyboard = null;
        _pulse.Opacity = 0;
    }
}
