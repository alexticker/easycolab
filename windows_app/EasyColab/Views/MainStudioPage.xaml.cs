using System.Collections.Specialized;
using System.ComponentModel;
using EasyColab.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace EasyColab.Views;

/// <summary>
/// Native Windows 11 Fluent 2 main shell: unified 48px Mica title bar, transparent left control sidebar,
/// elevated rounded center workspace, collapsible right log surface, and topmost modal overlay host.
/// </summary>
public sealed partial class MainStudioPage : UserControl
{
    private bool _showRightLogPanel = true;
    private bool _isAttached;
    private bool _isShowingDisconnectDialog;
    private readonly PointerEventHandler _pointerActivityHandler;
    private readonly KeyEventHandler _keyActivityHandler;

    public StudioViewModel ViewModel => StudioViewModel.Shared;

    public MainStudioPage()
    {
        InitializeComponent();
        _showRightLogPanel = App.MainAppWindow?.ShowRightLogPanelState ?? true;

        _pointerActivityHandler = (_, _) => ViewModel.RecordLocalInteraction();
        _keyActivityHandler = (_, _) => ViewModel.RecordLocalInteraction();
        AddHandler(PointerPressedEvent, _pointerActivityHandler, handledEventsToo: true);
        AddHandler(KeyDownEvent, _keyActivityHandler, handledEventsToo: true);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => SyncHeaderDynamicState();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach();
        ApplyLogPanelVisibility();
        SyncHeaderDynamicState();
        RegisterTitleBarDragArea();

        if (ViewModel.ShowDisconnectConfirm)
        {
            _ = ShowDisconnectConfirmDialogAsync();
        }
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
        if (App.MainAppWindow is not null)
        {
            App.MainAppWindow.ThemePreferenceChanged += OnThemePreferenceChanged;
        }
    }

    public void Detach()
    {
        if (!_isAttached) return;
        _isAttached = false;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.LiveConsoleLines.CollectionChanged -= OnConsoleLinesChanged;
        if (App.MainAppWindow is not null)
        {
            App.MainAppWindow.ThemePreferenceChanged -= OnThemePreferenceChanged;
        }
        LeftSidebarPanel.Detach();
        LogPanelView.Detach();
    }

    private void OnThemePreferenceChanged(object? sender, EventArgs e)
    {
        SyncThemeButtonVisual();
    }

    public void RegisterTitleBarDragArea()
    {
        App.MainAppWindow?.SetCustomTitleBar(TitleBarDragRegion);
        UpdateCaptionInsets();
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

    private void OnConsoleLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncLogHeaderBadge();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case null:
            case "":
                SyncHeaderDynamicState();
                break;

            case nameof(StudioViewModel.ShowDisconnectConfirm):
                if (ViewModel.ShowDisconnectConfirm)
                {
                    _ = ShowDisconnectConfirmDialogAsync();
                }
                break;

            case nameof(StudioViewModel.IsConnected):
            case nameof(StudioViewModel.DisplayGpuName):
            case nameof(StudioViewModel.VramUsedGB):
            case nameof(StudioViewModel.VramTotalGB):
            case nameof(StudioViewModel.RamUsedGB):
            case nameof(StudioViewModel.RamTotalGB):
            case nameof(StudioViewModel.DiskFreeGB):
            case nameof(StudioViewModel.DiskTotalGB):
                SyncWorkspaceHeaderStatus();
                break;

            case nameof(StudioViewModel.IsWakingColab):
            case nameof(StudioViewModel.IsDownloadingWeights):
            case nameof(StudioViewModel.IsLoadingModel):
            case nameof(StudioViewModel.IsUnloadingModel):
            case nameof(StudioViewModel.IsGenerating):
            case nameof(StudioViewModel.ComfyStarting):
            case nameof(StudioViewModel.ComfyDownloadingWeight):
            case nameof(StudioViewModel.ComfySyncingDrive):
            case nameof(StudioViewModel.ComfyQueueRunning):
            case nameof(StudioViewModel.LiveConsoleLines):
                SyncLogHeaderBadge();
                break;
        }
    }

    private void SyncHeaderDynamicState()
    {
        bool isEn = LanguageManager.Shared.IsEN;
        LanguageToggleLabel.Text = isEn ? "中文" : "EN";
        ToolTipService.SetToolTip(
            LanguageToggleButton,
            isEn ? "切换界面为简体中文 (Ctrl+Shift+L)" : "Switch UI to English (Ctrl+Shift+L)");

        SyncWorkspaceHeaderStatus();
        SyncLogHeaderBadge();
        SyncThemeButtonVisual();
        SyncToggleLogButtonVisual();
    }

    private void SyncThemeButtonVisual()
    {
        var pref = App.MainAppWindow?.ThemePreference ?? ElementTheme.Default;
        ThemeToggleIcon.Glyph = pref switch
        {
            ElementTheme.Light => "\uE706", // Sun
            ElementTheme.Dark => "\uE708",  // Moon
            _ => "\uE793",                  // Auto / System theme
        };
        string tip = pref switch
        {
            ElementTheme.Light => L("当前：浅色模式（点击切换深色）", "Theme: Light (click for Dark)"),
            ElementTheme.Dark => L("当前：深色模式（点击跟随系统）", "Theme: Dark (click to follow Windows)"),
            _ => L("当前：跟随系统主题（点击切换浅色）", "Theme: System (click for Light)"),
        };
        ToolTipService.SetToolTip(ThemeToggleButton, tip);
    }

    private void SyncWorkspaceHeaderStatus()
    {
        HeaderConnectionDot.Color = ResolveThemeBrush(
            ViewModel.IsConnected ? "StudioSystemGreenBrush" : "StudioSecondaryOpacity45Brush");

        if (ViewModel.IsConnected)
        {
            var ramTag = ViewModel.RamTotalGB > 0
                ? L(
                    $" · 内存 {WidgetFormat.Fmt(ViewModel.RamUsedGB, 1)}/{WidgetFormat.Fmt(ViewModel.RamTotalGB, 0)}G",
                    $" · RAM {WidgetFormat.Fmt(ViewModel.RamUsedGB, 1)}/{WidgetFormat.Fmt(ViewModel.RamTotalGB, 0)}G")
                : string.Empty;
            var diskTag = ViewModel.DiskTotalGB > 0
                ? L(
                    $" · 磁盘可用 {WidgetFormat.Fmt(ViewModel.DiskFreeGB, 1)}/{WidgetFormat.Fmt(ViewModel.DiskTotalGB, 0)}G",
                    $" · Disk Free {WidgetFormat.Fmt(ViewModel.DiskFreeGB, 1)}/{WidgetFormat.Fmt(ViewModel.DiskTotalGB, 0)}G")
                : string.Empty;
            HeaderTelemetryText.Text = L(
                $"{ViewModel.DisplayGpuName} · 显存 {WidgetFormat.Fmt(ViewModel.VramUsedGB, 1)}/{WidgetFormat.Fmt(ViewModel.VramTotalGB, 0)}G{ramTag}{diskTag}",
                $"{ViewModel.DisplayGpuName} · VRAM {WidgetFormat.Fmt(ViewModel.VramUsedGB, 1)}/{WidgetFormat.Fmt(ViewModel.VramTotalGB, 0)}G{ramTag}{diskTag}");
        }
        else
        {
            HeaderTelemetryText.Text = L("开源多模型云端工作台", "Open Multi-Model Cloud Studio");
        }
    }

    private void SyncLogHeaderBadge()
    {
        bool streaming = IsTaskStreaming;
        LogLiveDot.Fill = ResolveThemeBrush(streaming ? "StudioSystemGreenBrush" : "StudioSecondaryOpacity45Brush");
        LogLiveBadgeText.Text = streaming
            ? L("实时传输", "Live")
            : L($"{ViewModel.LiveConsoleLines.Count} 行", $"{ViewModel.LiveConsoleLines.Count} lines");
        LogLiveBadgeText.Foreground = ResolveThemeBrush(streaming ? "StudioSystemGreenBrush" : "StudioTextSecondaryBrush");
    }

    private void SyncToggleLogButtonVisual()
    {
        ToggleLogPanelIcon.Foreground = ResolveThemeBrush(
            _showRightLogPanel ? "StudioSystemAccentBrush" : "StudioTextSecondaryBrush");
        ToolTipService.SetToolTip(
            ToggleLogPanelButton,
            _showRightLogPanel
                ? L("收起右侧运行日志面板", "Hide right run log panel")
                : L("展开右侧运行日志面板", "Show right run log panel"));
    }

    private void ApplyLogPanelVisibility()
    {
        RightLogColumnHost.Visibility = _showRightLogPanel ? Visibility.Visible : Visibility.Collapsed;
        SyncToggleLogButtonVisual();
    }

    private void UpdateCaptionInsets()
    {
        double inset = 138.0;
        if (App.MainAppWindow is { } win && XamlRoot is { } xr)
        {
            int raw = win.AppWindow.TitleBar.RightInset;
            if (raw > 0 && xr.RasterizationScale > 0)
            {
                inset = Math.Clamp(raw / xr.RasterizationScale, 128.0, 180.0);
            }
        }
        TitleBarCaptionSpacer.Width = new GridLength(inset);
    }

    private async Task ShowDisconnectConfirmDialogAsync()
    {
        if (_isShowingDisconnectDialog || XamlRoot is null) return;
        _isShowingDisconnectDialog = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = L("释放所有 Colab 实例？", "Release All Colab Instances?"),
                Content = L(
                    "账号下所有运行中的 Colab GPU 实例将被销毁，计费立即归零。",
                    "All active Colab GPU runtimes under your account will be terminated and CU billing will stop immediately."),
                PrimaryButtonText = L("释放", "Release"),
                CloseButtonText = L("取消", "Cancel"),
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = ActualTheme,
            };

            var result = await dialog.ShowAsync();
            ViewModel.ShowDisconnectConfirm = false;

            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DisconnectRemoteColabAsync();
            }
        }
        catch
        {
            ViewModel.ShowDisconnectConfirm = false;
        }
        finally
        {
            _isShowingDisconnectDialog = false;
        }
    }

    // ── Header Button Handlers ──

    private void LanguageToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleLanguage();
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        App.MainAppWindow?.CycleThemePreference();
    }

    private void OpenGuideButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenOnboardingGuide(0);
    }

    private void TemplatesMenuFlyout_Opening(object? sender, object e)
    {
        TemplatesMenuFlyout.Items.Clear();
        var modality = ViewModel.ActiveModelSpec.Modality;
        var matching = PRESET_TEMPLATES
            .Where(t => t.Category == modality || (modality == ModelModality.Video && t.Category == ModelModality.VideoAudio))
            .ToList();
        var source = matching.Count > 0 ? matching : PRESET_TEMPLATES;

        foreach (var tpl in source)
        {
            var captured = tpl;
            var item = new MenuFlyoutItem { Text = tpl.Name };
            item.Click += (_, _) =>
            {
                ViewModel.VisualPrompt = captured.Visual;
                if (!string.IsNullOrEmpty(captured.Soundscape))
                    ViewModel.SoundscapePrompt = captured.Soundscape;
                if (!string.IsNullOrEmpty(captured.Music))
                    ViewModel.MusicPrompt = captured.Music;
            };
            TemplatesMenuFlyout.Items.Add(item);
        }
    }

    private void OpenOutputFolderButton_Click(object sender, RoutedEventArgs e)
    {
        LocalRuntimePaths.OpenInExplorer(ViewModel.LocalSaveDir);
    }

    private void ToggleLogPanelButton_Click(object sender, RoutedEventArgs e)
    {
        _showRightLogPanel = !_showRightLogPanel;
        if (App.MainAppWindow is not null)
            App.MainAppWindow.ShowRightLogPanelState = _showRightLogPanel;
        ApplyLogPanelVisibility();
    }

    private void CopyLogsButton_Click(object sender, RoutedEventArgs e)
    {
        var text = string.Join("\n", ViewModel.LiveConsoleLines.Select(LocalizeConsoleLine));
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
        ViewModel.StatusMessage = L("已复制全部运行日志到剪贴板", "Copied all run logs to clipboard");
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearConsoleLines();
    }

    private Brush ResolveThemeBrush(string key)
    {
        var color = StudioColors.ResolveThemeColor(key, ActualTheme, Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80));
        return new SolidColorBrush(color);
    }
}
