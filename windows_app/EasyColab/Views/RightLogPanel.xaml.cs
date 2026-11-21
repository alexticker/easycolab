using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace EasyColab.Views;

/// <summary>
/// Port of Swift <c>rightLogPanel</c> and <c>logLineColor</c> in <c>MainStudioView.swift</c>:
/// live task summary strip + syntax-colored dark streaming console with auto-scroll.
/// </summary>
public sealed partial class RightLogPanel : UserControl
{
    private static readonly SolidColorBrush ErrorBrush = new(Windows.UI.Color.FromArgb(0xFF, 0xFA, 0x75, 0x75));
    private static readonly SolidColorBrush DividerBrush = new(Windows.UI.Color.FromArgb(0xFF, 0x75, 0xCC, 0xFA));
    private static readonly SolidColorBrush SuccessBrush = new(Windows.UI.Color.FromArgb(0xFF, 0x59, 0xEB, 0x8C));
    private static readonly SolidColorBrush WarningBrush = new(Windows.UI.Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24));
    private static readonly SolidColorBrush DefaultBrush = new(Windows.UI.Color.FromArgb(0xFF, 0xDE, 0xDE, 0xDE));

    private bool _autoScroll = true;
    private bool _isAttached;

    public StudioViewModel ViewModel => StudioViewModel.Shared;

    public RightLogPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => SyncSummary();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach();
        SyncSummary();
        RebuildAllLines();
        ScrollToBottomIfEnabled(force: true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Detach();
    }

    public void Attach()
    {
        if (_isAttached) return;
        _isAttached = true;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.LiveConsoleLines.CollectionChanged += OnConsoleLinesChanged;
    }

    public void Detach()
    {
        if (!_isAttached) return;
        _isAttached = false;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.LiveConsoleLines.CollectionChanged -= OnConsoleLinesChanged;
    }

    private bool IsTaskStreaming =>
        ViewModel.IsWakingColab
        || ViewModel.IsDownloadingWeights
        || ViewModel.IsLoadingModel
        || ViewModel.IsUnloadingModel
        || ViewModel.IsGenerating
        || ViewModel.ComfyStarting
        || ViewModel.ComfyDownloadingWeight
        || ViewModel.ComfySyncingDrive
        || ViewModel.ComfyQueueRunning > 0;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case null:
            case "":
                SyncSummary();
                RebuildAllLines();
                break;
            case nameof(StudioViewModel.DisplayStatusMessage):
            case nameof(StudioViewModel.StatusMessage):
            case nameof(StudioViewModel.IsWakingColab):
            case nameof(StudioViewModel.IsDownloadingWeights):
            case nameof(StudioViewModel.IsLoadingModel):
            case nameof(StudioViewModel.IsUnloadingModel):
            case nameof(StudioViewModel.IsGenerating):
            case nameof(StudioViewModel.ModelTaskProgress):
            case nameof(StudioViewModel.ProgressValue):
            case nameof(StudioViewModel.ComfyStarting):
            case nameof(StudioViewModel.ComfyDownloadingWeight):
            case nameof(StudioViewModel.ComfySyncingDrive):
            case nameof(StudioViewModel.ComfyQueueRunning):
            case nameof(StudioViewModel.ComfyProgress):
                SyncSummary();
                break;
            case nameof(StudioViewModel.LiveConsoleLines):
                RebuildAllLines();
                break;
        }
    }

    private void OnConsoleLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null && e.NewStartingIndex == LogRichText.Blocks.Count:
                foreach (var item in e.NewItems)
                {
                    if (item is string line)
                    {
                        LogRichText.Blocks.Add(BuildLineParagraph(line));
                    }
                }
                ScrollToBottomIfEnabled();
                break;

            case NotifyCollectionChangedAction.Remove when e.OldStartingIndex >= 0 && e.OldItems is not null && e.OldStartingIndex + e.OldItems.Count <= LogRichText.Blocks.Count:
                for (int i = 0; i < e.OldItems.Count; i++)
                {
                    LogRichText.Blocks.RemoveAt(e.OldStartingIndex);
                }
                break;

            default:
                RebuildAllLines();
                break;
        }
    }

    private void SyncSummary()
    {
        bool streaming = IsTaskStreaming;
        SummaryIcon.Glyph = streaming ? "\uE945" : "\uE946";
        SummaryIcon.Foreground = ResolveThemeBrush(streaming ? "StudioSystemAccentBrush" : "StudioTextSecondaryBrush");
        SummaryStatusText.Text = ViewModel.DisplayStatusMessage;

        if (ViewModel.IsDownloadingWeights || ViewModel.IsLoadingModel)
        {
            SummaryProgressBar.Visibility = Visibility.Visible;
            SummaryProgressBar.Value = Math.Clamp(ViewModel.ModelTaskProgress, 0.0, 1.0);
        }
        else if (ViewModel.IsGenerating && ViewModel.ProgressValue > 0)
        {
            SummaryProgressBar.Visibility = Visibility.Visible;
            SummaryProgressBar.Value = Math.Clamp(ViewModel.ProgressValue, 0.0, 1.0);
        }
        else if (ViewModel.ComfyStarting || ViewModel.ComfyDownloadingWeight)
        {
            SummaryProgressBar.Visibility = Visibility.Visible;
            SummaryProgressBar.Value = Math.Clamp(ViewModel.ComfyProgress, 0.0, 1.0);
        }
        else
        {
            SummaryProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void RebuildAllLines()
    {
        LogRichText.Blocks.Clear();
        foreach (var line in ViewModel.LiveConsoleLines)
        {
            LogRichText.Blocks.Add(BuildLineParagraph(line));
        }
        ScrollToBottomIfEnabled();
    }

    private static Paragraph BuildLineParagraph(string rawLine)
    {
        var rendered = LocalizeConsoleLine(rawLine);
        var p = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = LogLineBrush(rendered),
        };
        p.Inlines.Add(new Run { Text = rendered });
        return p;
    }

    /// <summary>Port of Swift <c>logLineColor(_ line: String)</c> in <c>MainStudioView.swift</c>.</summary>
    public static SolidColorBrush LogLineBrush(string line)
    {
        var lower = line.ToLowerInvariant();
        if (line.Contains("失败", StringComparison.Ordinal)
            || line.Contains("错误", StringComparison.Ordinal)
            || line.Contains("异常", StringComparison.Ordinal)
            || line.Contains("ERROR", StringComparison.Ordinal)
            || lower.Contains("failed", StringComparison.Ordinal)
            || lower.Contains("error:", StringComparison.Ordinal)
            || lower.Contains("exception", StringComparison.Ordinal)
            || lower.Contains("traceback", StringComparison.Ordinal))
        {
            return ErrorBrush;
        }
        if (line.Contains("───", StringComparison.Ordinal))
        {
            return DividerBrush;
        }
        if (line.Contains('%')
            || line.Contains("已装载", StringComparison.Ordinal)
            || line.Contains("成功", StringComparison.Ordinal)
            || line.Contains("完成", StringComparison.Ordinal)
            || lower.Contains("loaded", StringComparison.Ordinal)
            || lower.Contains("succeeded", StringComparison.Ordinal)
            || lower.Contains("successfully", StringComparison.Ordinal)
            || lower.Contains("completed", StringComparison.Ordinal)
            || lower.Contains("ready in vram", StringComparison.Ordinal))
        {
            return SuccessBrush;
        }
        if (line.Contains("警告", StringComparison.Ordinal)
            || line.Contains("WARN", StringComparison.Ordinal)
            || lower.Contains("warning:", StringComparison.Ordinal))
        {
            return WarningBrush;
        }
        return DefaultBrush;
    }

    private void LogScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        double remaining = LogScrollViewer.ScrollableHeight - LogScrollViewer.VerticalOffset;
        if (remaining <= 16)
        {
            _autoScroll = true;
            JumpBottomButton.Visibility = Visibility.Collapsed;
        }
        else if (!e.IsIntermediate && remaining > 28)
        {
            _autoScroll = false;
            JumpBottomButton.Visibility = Visibility.Visible;
        }
    }

    private void JumpBottomButton_Click(object sender, RoutedEventArgs e)
    {
        _autoScroll = true;
        JumpBottomButton.Visibility = Visibility.Collapsed;
        ScrollToBottomIfEnabled(force: true);
    }

    private void ScrollToBottomIfEnabled(bool force = false)
    {
        if (!_autoScroll && !force) return;
        DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            LogScrollViewer.UpdateLayout();
            LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null, true);
        });
    }

    private Brush ResolveThemeBrush(string key)
    {
        var color = EasyColab.Controls.StudioColors.ResolveThemeColor(
            key,
            ActualTheme,
            Windows.UI.Color.FromArgb(0xFF, 0x94, 0xA3, 0xB8));
        return new SolidColorBrush(color);
    }
}
