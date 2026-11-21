using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WinColor = Windows.UI.Color;

namespace EasyColab.Controls;

/// <summary>
/// Port of Swift <c>SingleMetricLineChartCard</c> (TelemetryAndWidgets.swift): header (glowing dot, title, % capsule,
/// "used/total G · free" readout) and a 38 pt smooth area chart with hover crosshair + GB/% readout.
/// <para>
/// <c>Samples</c> are fractions 0…1 (same as Swift <c>vramHistoryPct</c> etc.). When empty, a flat line at the current
/// percentage (14 samples) is drawn. Effective colour: <c>IsCritical</c> → red, <c>IsWarning</c> → orange, else <c>AccentColor</c>.
/// </para>
/// </summary>
public sealed partial class SingleMetricLineChartCard : UserControl
{
    private readonly ChartRenderScheduler _scheduler;
    private readonly SolidColorBrush _effectiveBrush = new();
    private readonly SolidColorBrush _capsuleBrush = new();
    private readonly SolidColorBrush _hoverLineBrush = new();
    private readonly SolidColorBrush _haloBrush = new();
    private readonly SolidColorBrush _labelFillBrush = new();
    private readonly SolidColorBrush _labelBorderBrush = new();
    private readonly SolidColorBrush _usageAlertBrush = new();
    private readonly GradientStop _areaTop = new() { Offset = 0 };
    private readonly GradientStop _areaBottom = new() { Offset = 1 };
    private WinColor _effectiveColor;
    private readonly SoftShadow _dotShadow;

    private double? _hoverLocationX;
    private SmoothTelemetryPaths? _built;
    private double[] _plotSamples = Array.Empty<double>();

    public SingleMetricLineChartCard()
    {
        InitializeComponent();
        _scheduler = new ChartRenderScheduler(this, Render);

        var areaBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
        };
        areaBrush.GradientStops.Add(_areaTop);
        areaBrush.GradientStops.Add(_areaBottom);
        AreaPath.Fill = areaBrush;
        LinePath.Stroke = _effectiveBrush;
        HeaderDot.Fill = _effectiveBrush;
        PercentText.Foreground = _effectiveBrush;
        PercentCapsule.Background = _capsuleBrush;
        HoverLine.Stroke = _hoverLineBrush;
        HoverDot.Fill = _effectiveBrush;
        HoverLabel.Background = _labelFillBrush;
        HoverLabel.BorderBrush = _labelBorderBrush;
        LastHalo.Fill = _haloBrush;
        LastDot.Fill = _effectiveBrush;

        // `.shadow(color: effectiveColor.opacity(0.55), radius: 2.5)` on the header dot.
        _dotShadow = new SoftShadow(DotShadowHost, ellipse: true, cornerRadius: 0, radius: 2.5, offsetY: 0,
            shapeFill: _ => _effectiveColor,
            shadowColor: _ => (_effectiveColor, 0.55f));

        PlotHost.SizeChanged += (_, _) => Render();
        ActualThemeChanged += (_, _) => Render();
        Loaded += (_, _) => Render();
    }

    // ── Dependency properties (Swift init parameters) ──────────────────────────────────────────

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SingleMetricLineChartCard), new PropertyMetadata(string.Empty, OnAnyChanged));

    public double UsedGB
    {
        get => (double)GetValue(UsedGBProperty);
        set => SetValue(UsedGBProperty, value);
    }

    public static readonly DependencyProperty UsedGBProperty = DependencyProperty.Register(
        nameof(UsedGB), typeof(double), typeof(SingleMetricLineChartCard), new PropertyMetadata(0.0, OnAnyChanged));

    public double FreeGB
    {
        get => (double)GetValue(FreeGBProperty);
        set => SetValue(FreeGBProperty, value);
    }

    public static readonly DependencyProperty FreeGBProperty = DependencyProperty.Register(
        nameof(FreeGB), typeof(double), typeof(SingleMetricLineChartCard), new PropertyMetadata(0.0, OnAnyChanged));

    public double TotalGB
    {
        get => (double)GetValue(TotalGBProperty);
        set => SetValue(TotalGBProperty, value);
    }

    public static readonly DependencyProperty TotalGBProperty = DependencyProperty.Register(
        nameof(TotalGB), typeof(double), typeof(SingleMetricLineChartCard), new PropertyMetadata(0.0, OnAnyChanged));

    /// <summary>History as fractions 0…1. Replace the list or bind an <c>ObservableCollection&lt;double&gt;</c>.</summary>
    public IReadOnlyList<double>? Samples
    {
        get => (IReadOnlyList<double>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<double>), typeof(SingleMetricLineChartCard),
        new PropertyMetadata(null, (d, e) => ((SingleMetricLineChartCard)d)._scheduler.Rewire(e.OldValue, e.NewValue)));

    public WinColor AccentColor
    {
        get => (WinColor)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(
        nameof(AccentColor), typeof(WinColor), typeof(SingleMetricLineChartCard),
        new PropertyMetadata(StudioColors.VramBlue, OnAnyChanged));

    public bool IsWarning
    {
        get => (bool)GetValue(IsWarningProperty);
        set => SetValue(IsWarningProperty, value);
    }

    public static readonly DependencyProperty IsWarningProperty = DependencyProperty.Register(
        nameof(IsWarning), typeof(bool), typeof(SingleMetricLineChartCard), new PropertyMetadata(false, OnAnyChanged));

    public bool IsCritical
    {
        get => (bool)GetValue(IsCriticalProperty);
        set => SetValue(IsCriticalProperty, value);
    }

    public static readonly DependencyProperty IsCriticalProperty = DependencyProperty.Register(
        nameof(IsCritical), typeof(bool), typeof(SingleMetricLineChartCard), new PropertyMetadata(false, OnAnyChanged));

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SingleMetricLineChartCard)d)._scheduler.Schedule();

    // ── Rendering ──────────────────────────────────────────────────────────────────────────────

    private WinColor EffectiveColor(ElementTheme theme)
    {
        if (IsCritical) return StudioColors.SystemRed(theme);
        if (IsWarning) return StudioColors.SystemOrange(theme);
        return AccentColor;
    }

    private void Render()
    {
        var theme = ActualTheme;
        var dark = theme == ElementTheme.Dark;
        var eff = EffectiveColor(theme);
        _effectiveColor = eff;
        _dotShadow.UpdateColors();

        _effectiveBrush.Color = eff;
        _capsuleBrush.Color = StudioColors.WithOpacity(eff, dark ? 0.18 : 0.12);
        _hoverLineBrush.Color = StudioColors.WithOpacity(eff, 0.55);
        _haloBrush.Color = StudioColors.WithOpacity(eff, 0.28);
        _labelBorderBrush.Color = StudioColors.WithOpacity(eff, 0.45);
        var cardFill = StudioColors.ResolveThemeColor("StudioWorkspaceCardFillBrush", theme,
            dark ? WinColor.FromArgb(0xFF, 0x20, 0x22, 0x29) : WinColor.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        _labelFillBrush.Color = StudioColors.WithOpacity(cardFill, 0.94);
        _areaTop.Color = StudioColors.WithOpacity(eff, dark ? 0.34 : 0.24);
        _areaBottom.Color = StudioColors.WithOpacity(eff, 0.02);

        var usedGB = UsedGB;
        var totalGB = TotalGB;
        var freeGB = FreeGB;
        var currentPct = ChartRenderScheduler.Pct(usedGB, totalGB);
        _plotSamples = ChartRenderScheduler.PlotSeries(ChartRenderScheduler.Snapshot(Samples), currentPct);

        TitleText.Text = Title ?? string.Empty;
        PercentText.Text = WidgetFormat.Percent0(currentPct);
        var f1 = WidgetFormat.Fmt1;
        UsageText.Text = L(
            $"{f1(usedGB)}/{f1(totalGB)}G · 余{f1(freeGB)}G",
            $"{f1(usedGB)}/{f1(totalGB)}G · {f1(freeGB)}G free");
        if (IsCritical)
        {
            _usageAlertBrush.Color = StudioColors.SystemRed(theme);
            UsageText.Foreground = _usageAlertBrush;
        }
        else if (IsWarning)
        {
            _usageAlertBrush.Color = StudioColors.SystemOrange(theme);
            UsageText.Foreground = _usageAlertBrush;
        }
        else
        {
            UsageText.ClearValue(TextBlock.ForegroundProperty); // → style: StudioTextSecondaryBrush
        }

        var size = new Size(PlotHost.ActualWidth, PlotHost.ActualHeight);
        _built = SmoothTelemetryPaths.Build(_plotSamples, size);
        AreaPath.Data = _built.CreateAreaGeometry();
        LinePath.Data = _built.CreateLineGeometry();

        RenderHover();
    }

    private void RenderHover()
    {
        var built = _built;
        var w = PlotHost.ActualWidth;
        var h = PlotHost.ActualHeight;

        int? hoverIdx = null;
        if (_hoverLocationX is double hx && w > 1 && built is { IsEmpty: false })
        {
            var ratio = Math.Min(1.0, Math.Max(0.0, hx / w));
            hoverIdx = Math.Min(built.Points.Count - 1,
                Math.Max(0, (int)Math.Round(ratio * (built.Points.Count - 1), MidpointRounding.AwayFromZero)));
        }

        if (built is not null && hoverIdx is int hi && hi < built.Points.Count && hi < _plotSamples.Length)
        {
            var hp = built.Points[hi];
            var hPct = _plotSamples[hi];
            var hGB = hPct * TotalGB;

            HoverLine.X1 = hp.X;
            HoverLine.X2 = hp.X;
            HoverLine.Y1 = 0;
            HoverLine.Y2 = h;
            HoverLine.Visibility = Visibility.Visible;

            ChartRenderScheduler.Place(HoverDot, hp.X, hp.Y, HoverDot.Width, HoverDot.Height);
            HoverDot.Visibility = Visibility.Visible;

            HoverLabelText.Text = $"{WidgetFormat.Fmt1(hGB)} GB ({WidgetFormat.Percent0(hPct)})";
            HoverLabel.Visibility = Visibility.Visible;
            HoverLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var ds = HoverLabel.DesiredSize;
            ChartRenderScheduler.Place(HoverLabel, Math.Min(Math.Max(46, hp.X), w - 46), 11, ds.Width, ds.Height);

            LastHalo.Visibility = Visibility.Collapsed;
            LastDot.Visibility = Visibility.Collapsed;
        }
        else
        {
            HoverLine.Visibility = Visibility.Collapsed;
            HoverDot.Visibility = Visibility.Collapsed;
            HoverLabel.Visibility = Visibility.Collapsed;

            if (built is { IsEmpty: false })
            {
                var lastPt = built.Points[^1];
                ChartRenderScheduler.Place(LastHalo, lastPt.X, lastPt.Y, LastHalo.Width, LastHalo.Height);
                ChartRenderScheduler.Place(LastDot, lastPt.X, lastPt.Y, LastDot.Width, LastDot.Height);
                LastHalo.Visibility = Visibility.Visible;
                LastDot.Visibility = Visibility.Visible;
            }
            else
            {
                LastHalo.Visibility = Visibility.Collapsed;
                LastDot.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void PlotHost_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        _hoverLocationX = e.GetCurrentPoint(PlotHost).Position.X;
        RenderHover();
    }

    private void PlotHost_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hoverLocationX = null;
        RenderHover();
    }
}
