using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WinColor = Windows.UI.Color;

namespace EasyColab.Controls;

/// <summary>
/// Port of Swift <c>CombinedMultiMetricChartCard</c>: one 88 pt plot overlaying VRAM (area + line), RAM and Disk
/// splines with 100/50/0 % axis labels, hover crosshair, last-point dots and a legend row per metric
/// (rows / lines are shown only when the metric's total &gt; 0). Samples are fractions 0…1.
/// </summary>
public sealed partial class CombinedMultiMetricChartCard : UserControl
{
    // Swift: vramColor / ramColor / diskColor
    private static readonly WinColor VramColor = StudioColors.VramBlue;
    private static readonly WinColor RamColor = StudioColors.RamGreen;
    private static readonly WinColor DiskColor = StudioColors.DiskCyan;

    private readonly ChartRenderScheduler _scheduler;
    private readonly ChartRenderScheduler.Rewirer _vramWire;
    private readonly ChartRenderScheduler.Rewirer _ramWire;
    private readonly ChartRenderScheduler.Rewirer _diskWire;
    private double? _hoverX;
    private SmoothTelemetryPaths? _vBuilt;
    private SmoothTelemetryPaths? _rBuilt;
    private SmoothTelemetryPaths? _dBuilt;

    public CombinedMultiMetricChartCard()
    {
        InitializeComponent();
        _scheduler = new ChartRenderScheduler(this, Render);
        _vramWire = _scheduler.CreateRewirer();
        _ramWire = _scheduler.CreateRewirer();
        _diskWire = _scheduler.CreateRewirer();

        var area = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        area.GradientStops.Add(new GradientStop { Color = StudioColors.WithOpacity(VramColor, 0.18), Offset = 0 });
        area.GradientStops.Add(new GradientStop { Color = StudioColors.WithOpacity(VramColor, 0.01), Offset = 1 });
        VramAreaPath.Fill = area;

        var vb = new SolidColorBrush(VramColor);
        var rb = new SolidColorBrush(RamColor);
        var db = new SolidColorBrush(DiskColor);
        VramLinePath.Stroke = vb;
        RamLinePath.Stroke = rb;
        DiskLinePath.Stroke = db;
        VramLastDot.Fill = vb;
        RamLastDot.Fill = rb;
        DiskLastDot.Fill = db;
        VramSwatch.Background = vb;
        RamSwatch.Background = rb;
        DiskSwatch.Background = db;
        VramLegendPct.Foreground = vb;
        RamLegendPct.Foreground = rb;
        DiskLegendPct.Foreground = db;

        PlotHost.SizeChanged += (_, _) => Render();
        Loaded += (_, _) => Render();
    }

    // ── Dependency properties (Swift init parameters) ──────────────────────────────────────────

    public double VramUsed { get => (double)GetValue(VramUsedProperty); set => SetValue(VramUsedProperty, value); }
    public static readonly DependencyProperty VramUsedProperty = RegisterDouble(nameof(VramUsed));

    public double VramTotal { get => (double)GetValue(VramTotalProperty); set => SetValue(VramTotalProperty, value); }
    public static readonly DependencyProperty VramTotalProperty = RegisterDouble(nameof(VramTotal));

    public IReadOnlyList<double>? VramSamples
    {
        get => (IReadOnlyList<double>?)GetValue(VramSamplesProperty);
        set => SetValue(VramSamplesProperty, value);
    }

    public static readonly DependencyProperty VramSamplesProperty = DependencyProperty.Register(
        nameof(VramSamples), typeof(IReadOnlyList<double>), typeof(CombinedMultiMetricChartCard),
        new PropertyMetadata(null, (d, e) => ((CombinedMultiMetricChartCard)d)._vramWire.Rewire(e.OldValue, e.NewValue)));

    public double RamUsed { get => (double)GetValue(RamUsedProperty); set => SetValue(RamUsedProperty, value); }
    public static readonly DependencyProperty RamUsedProperty = RegisterDouble(nameof(RamUsed));

    public double RamTotal { get => (double)GetValue(RamTotalProperty); set => SetValue(RamTotalProperty, value); }
    public static readonly DependencyProperty RamTotalProperty = RegisterDouble(nameof(RamTotal));

    public IReadOnlyList<double>? RamSamples
    {
        get => (IReadOnlyList<double>?)GetValue(RamSamplesProperty);
        set => SetValue(RamSamplesProperty, value);
    }

    public static readonly DependencyProperty RamSamplesProperty = DependencyProperty.Register(
        nameof(RamSamples), typeof(IReadOnlyList<double>), typeof(CombinedMultiMetricChartCard),
        new PropertyMetadata(null, (d, e) => ((CombinedMultiMetricChartCard)d)._ramWire.Rewire(e.OldValue, e.NewValue)));

    public double DiskUsed { get => (double)GetValue(DiskUsedProperty); set => SetValue(DiskUsedProperty, value); }
    public static readonly DependencyProperty DiskUsedProperty = RegisterDouble(nameof(DiskUsed));

    public double DiskTotal { get => (double)GetValue(DiskTotalProperty); set => SetValue(DiskTotalProperty, value); }
    public static readonly DependencyProperty DiskTotalProperty = RegisterDouble(nameof(DiskTotal));

    public IReadOnlyList<double>? DiskSamples
    {
        get => (IReadOnlyList<double>?)GetValue(DiskSamplesProperty);
        set => SetValue(DiskSamplesProperty, value);
    }

    public static readonly DependencyProperty DiskSamplesProperty = DependencyProperty.Register(
        nameof(DiskSamples), typeof(IReadOnlyList<double>), typeof(CombinedMultiMetricChartCard),
        new PropertyMetadata(null, (d, e) => ((CombinedMultiMetricChartCard)d)._diskWire.Rewire(e.OldValue, e.NewValue)));

    private static DependencyProperty RegisterDouble(string name) => DependencyProperty.Register(
        name, typeof(double), typeof(CombinedMultiMetricChartCard),
        new PropertyMetadata(0.0, (d, _) => ((CombinedMultiMetricChartCard)d)._scheduler.Schedule()));

    // ── Rendering ──────────────────────────────────────────────────────────────────────────────

    private void Render()
    {
        var vramUsed = VramUsed; var vramTotal = VramTotal;
        var ramUsed = RamUsed; var ramTotal = RamTotal;
        var diskUsed = DiskUsed; var diskTotal = DiskTotal;

        var vPct = ChartRenderScheduler.Pct(vramUsed, vramTotal);
        var rPct = ChartRenderScheduler.Pct(ramUsed, ramTotal);
        var dPct = ChartRenderScheduler.Pct(diskUsed, diskTotal);

        var vSeries = ChartRenderScheduler.PlotSeries(ChartRenderScheduler.Snapshot(VramSamples), vPct);
        var rSeries = ChartRenderScheduler.PlotSeries(ChartRenderScheduler.Snapshot(RamSamples), rPct);
        var dSeries = ChartRenderScheduler.PlotSeries(ChartRenderScheduler.Snapshot(DiskSamples), dPct);

        var size = new Size(PlotHost.ActualWidth, PlotHost.ActualHeight);
        _vBuilt = SmoothTelemetryPaths.Build(vSeries, size, topInset: 6, bottomInset: 5);
        _rBuilt = SmoothTelemetryPaths.Build(rSeries, size, topInset: 6, bottomInset: 5);
        _dBuilt = SmoothTelemetryPaths.Build(dSeries, size, topInset: 6, bottomInset: 5);

        VramAreaPath.Data = _vBuilt.CreateAreaGeometry();
        DiskLinePath.Data = diskTotal > 0 ? _dBuilt.CreateLineGeometry() : null;
        RamLinePath.Data = ramTotal > 0 ? _rBuilt.CreateLineGeometry() : null;
        VramLinePath.Data = vramTotal > 0 ? _vBuilt.CreateLineGeometry() : null;

        LegendRow(VramLegendRow, VramLegendLabel, VramLegendValue, VramLegendPct,
            vramTotal > 0, L("显存 VRAM", "VRAM"), vPct, vramUsed, vramTotal);
        LegendRow(RamLegendRow, RamLegendLabel, RamLegendValue, RamLegendPct,
            ramTotal > 0, L("内存 RAM", "System RAM"), rPct, ramUsed, ramTotal);
        LegendRow(DiskLegendRow, DiskLegendLabel, DiskLegendValue, DiskLegendPct,
            diskTotal > 0, L("硬盘 Disk", "Disk"), dPct, diskUsed, diskTotal);

        RenderHover();
    }

    private static void LegendRow(
        FrameworkElement row, TextBlock label, TextBlock value, TextBlock pctText,
        bool visible, string labelText, double pct, double used, double total)
    {
        row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        label.Text = labelText;
        value.Text = $"{WidgetFormat.Fmt1(used)} / {WidgetFormat.Fmt1(total)} GB";
        // String(format: "%3.0f%%", pct * 100)
        pctText.Text = WidgetFormat.Fmt(pct * 100, 0).PadLeft(3) + "%";
    }

    private void RenderHover()
    {
        var w = PlotHost.ActualWidth;
        var h = PlotHost.ActualHeight;
        if (_hoverX is double hx && w > 1)
        {
            var clampedX = Math.Min(w, Math.Max(0, hx));
            HoverLine.X1 = clampedX;
            HoverLine.X2 = clampedX;
            HoverLine.Y1 = 0;
            HoverLine.Y2 = h;
            HoverLine.Visibility = Visibility.Visible;
            VramLastDot.Visibility = Visibility.Collapsed;
            RamLastDot.Visibility = Visibility.Collapsed;
            DiskLastDot.Visibility = Visibility.Collapsed;
            return;
        }

        HoverLine.Visibility = Visibility.Collapsed;
        PlaceLastDot(VramLastDot, VramTotal > 0, _vBuilt);
        PlaceLastDot(RamLastDot, RamTotal > 0, _rBuilt);
        PlaceLastDot(DiskLastDot, DiskTotal > 0, _dBuilt);
    }

    private static void PlaceLastDot(FrameworkElement dot, bool enabled, SmoothTelemetryPaths? built)
    {
        if (enabled && built is { IsEmpty: false })
        {
            var lp = built.Points[^1];
            ChartRenderScheduler.Place(dot, lp.X, lp.Y, dot.Width, dot.Height);
            dot.Visibility = Visibility.Visible;
        }
        else
        {
            dot.Visibility = Visibility.Collapsed;
        }
    }

    private void PlotHost_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        _hoverX = e.GetCurrentPoint(PlotHost).Position.X;
        RenderHover();
    }

    private void PlotHost_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hoverX = null;
        RenderHover();
    }
}
