using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Windows.Foundation;

namespace EasyColab.Controls;

/// <summary>
/// Port of Swift <c>TelemetryGridBackground</c>: dashed 3×3 grid (lines at 25/50/75 % both axes),
/// stroke white 7.5 % (dark) / black 6.5 % (light), width 0.65, dash [2.5, 2.5].
/// The Swift <c>colorScheme</c> parameter is replaced by the element's <c>ActualTheme</c>.
/// XAML: <c>&lt;controls:TelemetryGridBackground /&gt;</c> (fills its container; not hit-test visible).
/// </summary>
public sealed class TelemetryGridBackground : Grid
{
    private const double LineWidth = 0.65;
    private readonly Path _path;

    public TelemetryGridBackground()
    {
        IsHitTestVisible = false;
        _path = new Path
        {
            StrokeThickness = LineWidth,
            // WinUI dash lengths are in multiples of StrokeThickness: 2.5pt / 0.65.
            StrokeDashArray = new DoubleCollection { 2.5 / LineWidth, 2.5 / LineWidth },
        };
        Children.Add(_path);
        SizeChanged += (_, _) => Rebuild();
        ActualThemeChanged += (_, _) => UpdateStroke();
        Loaded += (_, _) => UpdateStroke();
        UpdateStroke();
    }

    private void UpdateStroke()
    {
        var fallback = ActualTheme == ElementTheme.Dark
            ? StudioColors.WithOpacity(Microsoft.UI.Colors.White, 0.075)
            : StudioColors.WithOpacity(Microsoft.UI.Colors.Black, 0.065);
        _path.Stroke = new SolidColorBrush(StudioColors.ResolveThemeColor("StudioTelemetryGridLineBrush", ActualTheme, fallback));
    }

    private void Rebuild()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var geo = new PathGeometry();
        foreach (var frac in new[] { 0.25, 0.50, 0.75 })
        {
            var y = h * frac;
            geo.Figures.Add(Line(new Point(0, y), new Point(w, y)));
        }
        foreach (var frac in new[] { 0.25, 0.50, 0.75 })
        {
            var x = w * frac;
            geo.Figures.Add(Line(new Point(x, 0), new Point(x, h)));
        }
        _path.Data = geo;
    }

    private static PathFigure Line(Point a, Point b)
    {
        var f = new PathFigure { StartPoint = a, IsClosed = false, IsFilled = false };
        f.Segments.Add(new LineSegment { Point = b });
        return f;
    }
}
