using Microsoft.UI.Dispatching;

namespace EasyColab.Services;

/// <summary>
/// UI-thread marshalling helper (Swift <c>DispatchQueue.main.async</c> / <c>@MainActor</c> equivalent).
/// Captured in App.OnLaunched. All view-model observable properties must be mutated on the UI thread.
/// </summary>
public static class UiDispatcher
{
    private static DispatcherQueue? _queue;

    public static void Initialize(DispatcherQueue queue) => _queue = queue;

    public static bool HasAccess => _queue is null || _queue.HasThreadAccess;

    /// <summary>Run on the UI thread (inline when already on it).</summary>
    public static void Run(Action action)
    {
        if (_queue is null || _queue.HasThreadAccess) { action(); return; }
        _queue.TryEnqueue(() => action());
    }

    /// <summary>Always enqueue (like DispatchQueue.main.async even from main).</summary>
    public static void Post(Action action)
    {
        if (_queue is null) { action(); return; }
        _queue.TryEnqueue(() => action());
    }

    public static Task RunAsync(Action action)
    {
        if (_queue is null || _queue.HasThreadAccess) { action(); return Task.CompletedTask; }
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.TryEnqueue(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    /// <summary>Equivalent of DispatchQueue.main.asyncAfter.</summary>
    public static void After(TimeSpan delay, Action action)
    {
        _ = Task.Delay(delay).ContinueWith(_ => Post(action), TaskScheduler.Default);
    }
}
