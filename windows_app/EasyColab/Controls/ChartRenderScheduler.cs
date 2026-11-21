using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyColab.Controls;

/// <summary>
/// Shared plumbing for the telemetry charts: observes in-place mutations of a sample list (when the bound list
/// implements <see cref="INotifyCollectionChanged"/>) and coalesces many property changes into one render per UI tick.
/// </summary>
internal sealed class ChartRenderScheduler
{
    private readonly FrameworkElement _owner;
    private readonly Action _render;
    private bool _pending;

    public ChartRenderScheduler(FrameworkElement owner, Action render)
    {
        _owner = owner;
        _render = render;
    }

    public void Schedule()
    {
        if (_pending) return;
        var dq = _owner.DispatcherQueue;
        if (dq is null)
        {
            _render();
            return;
        }
        _pending = true;
        if (!dq.TryEnqueue(() =>
            {
                _pending = false;
                _render();
            }))
        {
            _pending = false;
        }
    }

    public void Rewire(object? oldValue, object? newValue)
    {
        if (oldValue is INotifyCollectionChanged o) o.CollectionChanged -= OnCollectionChanged;
        if (newValue is INotifyCollectionChanged n) n.CollectionChanged += OnCollectionChanged;
        Schedule();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Schedule();

    /// <summary>Independent subscription slot (one per sample-list property).</summary>
    public Rewirer CreateRewirer() => new(this);

    public sealed class Rewirer
    {
        private readonly ChartRenderScheduler _owner;

        internal Rewirer(ChartRenderScheduler owner) => _owner = owner;

        public void Rewire(object? oldValue, object? newValue) => _owner.Rewire(oldValue, newValue);
    }

    /// <summary>Snapshot (Swift arrays are value types; copying avoids enumerating a list mutated mid-render).</summary>
    public static double[] Snapshot(IReadOnlyList<double>? list)
    {
        if (list is null || list.Count == 0) return Array.Empty<double>();
        var arr = new double[list.Count];
        for (var i = 0; i < arr.Length; i++) arr[i] = list[i];
        return arr;
    }

    /// <summary><c>samples.isEmpty ? Array(repeating: pct, count: 14) : samples</c>.</summary>
    public static double[] PlotSeries(double[] samples, double currentPct) =>
        samples.Length == 0 ? Enumerable.Repeat(currentPct, 14).ToArray() : samples;

    /// <summary><c>total &gt; 0 ? min(1, max(0, used / total)) : 0</c>.</summary>
    public static double Pct(double used, double total) =>
        total > 0 ? Math.Min(1.0, Math.Max(0.0, used / total)) : 0.0;

    public static void Place(UIElement el, double centerX, double centerY, double width, double height)
    {
        Canvas.SetLeft(el, centerX - width / 2.0);
        Canvas.SetTop(el, centerY - height / 2.0);
    }
}
