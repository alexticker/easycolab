using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace EasyColab.Controls;

/// <summary>
/// 1:1 port of Swift <c>buildSmoothTelemetryPaths(samples:size:topInset:bottomInset:)</c>:
/// Catmull-Rom → cubic Bézier spline (1/6 tangents) with control points clamped into the plot band, flat runs as
/// straight segments, plus a closed area path down to the bottom edge.
/// </summary>
public sealed class SmoothTelemetryPaths
{
    private readonly struct Segment
    {
        public Segment(bool isLine, Point cp1, Point cp2, Point to)
        {
            IsLine = isLine;
            Cp1 = cp1;
            Cp2 = cp2;
            To = to;
        }

        public bool IsLine { get; }
        public Point Cp1 { get; }
        public Point Cp2 { get; }
        public Point To { get; }
    }

    private readonly List<Segment> _segments = new();
    private readonly double _height;

    /// <summary>Sample points in plot coordinates (empty when nothing can be drawn).</summary>
    public IReadOnlyList<Point> Points { get; }

    public bool IsEmpty => Points.Count == 0;

    private SmoothTelemetryPaths(IReadOnlyList<Point> points, double height)
    {
        Points = points;
        _height = height;
    }

    public static SmoothTelemetryPaths Build(
        IReadOnlyList<double> samples,
        Size size,
        double topInset = 5,
        double bottomInset = 4)
    {
        if (samples.Count == 0 || size.Width <= 2 || size.Height <= (topInset + bottomInset + 2))
        {
            return new SmoothTelemetryPaths(Array.Empty<Point>(), size.Height);
        }

        var usableH = size.Height - topInset - bottomInset;
        Point[] pts;
        if (samples.Count == 1)
        {
            var y = topInset + usableH * (1.0 - Math.Min(1.0, Math.Max(0.0, Safe(samples[0]))));
            pts = new[] { new Point(0, y), new Point(size.Width, y) };
        }
        else
        {
            var stepX = size.Width / (samples.Count - 1);
            pts = new Point[samples.Count];
            for (var idx = 0; idx < samples.Count; idx++)
            {
                var clamped = Math.Min(1.0, Math.Max(0.0, Safe(samples[idx])));
                pts[idx] = new Point(idx * stepX, topInset + usableH * (1.0 - clamped));
            }
        }

        var minY = topInset;
        var maxY = size.Height - bottomInset;
        var result = new SmoothTelemetryPaths(pts, size.Height);

        for (var i = 0; i < pts.Length - 1; i++)
        {
            var p0 = i > 0 ? pts[i - 1] : pts[i];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = (i + 2 < pts.Length) ? pts[i + 2] : p2;

            var dx = p2.X - p1.X;
            // 若相邻区间完全平坦，直接使用直线段防止任何贝塞尔过冲
            if (Math.Abs(p2.Y - p1.Y) < 0.25)
            {
                result._segments.Add(new Segment(true, default, default, p2));
            }
            else
            {
                var cp1 = new Point(p1.X + dx / 3.0, Math.Min(maxY, Math.Max(minY, p1.Y + (p2.Y - p0.Y) / 6.0)));
                var cp2 = new Point(p2.X - dx / 3.0, Math.Min(maxY, Math.Max(minY, p2.Y - (p3.Y - p1.Y) / 6.0)));
                result._segments.Add(new Segment(false, cp1, cp2, p2));
            }
        }

        return result;
    }

    private static double Safe(double v) => double.IsNaN(v) ? 0.0 : v;

    private PathFigure BuildLineFigure()
    {
        var fig = new PathFigure { StartPoint = Points[0], IsClosed = false, IsFilled = false };
        foreach (var s in _segments)
        {
            if (s.IsLine)
                fig.Segments.Add(new LineSegment { Point = s.To });
            else
                fig.Segments.Add(new BezierSegment { Point1 = s.Cp1, Point2 = s.Cp2, Point3 = s.To });
        }
        return fig;
    }

    /// <summary>Fresh geometry for the stroked spline (a geometry instance can only be used by one Path).</summary>
    public Geometry CreateLineGeometry()
    {
        var geo = new PathGeometry();
        if (IsEmpty) return geo;
        geo.Figures.Add(BuildLineFigure());
        return geo;
    }

    /// <summary>Fresh geometry for the gradient area (spline + down to the bottom edge, closed).</summary>
    public Geometry CreateAreaGeometry()
    {
        var geo = new PathGeometry();
        if (IsEmpty) return geo;
        var fig = BuildLineFigure();
        fig.IsFilled = true;
        fig.Segments.Add(new LineSegment { Point = new Point(Points[^1].X, _height) });
        fig.Segments.Add(new LineSegment { Point = new Point(Points[0].X, _height) });
        fig.IsClosed = true;
        geo.Figures.Add(fig);
        return geo;
    }
}
