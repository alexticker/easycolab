using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using EasyColab.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace EasyColab.Views;

/// <summary>
/// Port of Swift <c>sidebarPanel</c>, <c>colabConnectionRows</c>, <c>usageRows</c>,
/// <c>openModelHubRows</c>, <c>adaptiveParamRows</c>, and <c>seedAndBatchControls</c>
/// from <c>MainStudioView.swift</c>.
/// </summary>
public sealed partial class SidebarPanel : UserControl
{
    private static readonly int[] ContextPresets = [8, 16, 32, 64, 128, 256];
    private static readonly int[] HubContextPresets = [8, 16, 32, 64, 128, 192, 256];
    private static readonly (int Frames, string Tag)[] VideoDurationPresets =
    [
        (90, "4s"),
        (124, "5s"),
        (192, "8s"),
        (243, "10s"),
        (294, "12s"),
        (362, "15s"),
    ];
    private static readonly (string Key, string Zh, string En)[] ReasoningEfforts =
    [
        ("low", "精简", "Low"),
        ("medium", "均衡", "Med"),
        ("high", "深入", "High"),
        ("xhigh", "极深", "XHigh"),
    ];

    private bool _isAttached;
    private bool _syncingUi = true;
    private bool _showFileList;
    private string _lastModelListSig = string.Empty;
    private string _lastCoResidentSig = string.Empty;
    private string _lastFileListSig = string.Empty;
    private string _lastImageResSig = string.Empty;
    private string _lastHubCtxSig = string.Empty;

    public StudioViewModel ViewModel => StudioViewModel.Shared;

    public SidebarPanel()
    {
        InitializeComponent();
        _showFileList = App.MainAppWindow?.ShowFileListState ?? false;
        PopulateStaticPickers();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) =>
        {
            _lastModelListSig = string.Empty;
            _lastCoResidentSig = string.Empty;
            _lastFileListSig = string.Empty;
            SyncAll();
        };
    }

    private void PopulateStaticPickers()
    {
        VideoResolutionPicker.Items.Clear();
        foreach (var opt in VIDEO_RESOLUTIONS)
        {
            VideoResolutionPicker.Items.Add(new ComboBoxItem { Content = opt.Label, Tag = opt });
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach();
        SyncAll();
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
        ViewModel.ChatHistory.CollectionChanged += OnChatHistoryChanged;
    }

    public void Detach()
    {
        if (!_isAttached) return;
        _isAttached = false;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.ChatHistory.CollectionChanged -= OnChatHistoryChanged;
    }

    private void OnChatHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncClearChatButton();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName;
        if (string.IsNullOrEmpty(name))
        {
            _lastModelListSig = string.Empty;
            _lastCoResidentSig = string.Empty;
            _lastFileListSig = string.Empty;
            _lastImageResSig = string.Empty;
            _lastHubCtxSig = string.Empty;
            SyncAll();
            return;
        }

        switch (name)
        {
            case nameof(StudioViewModel.IsConnected):
            case nameof(StudioViewModel.CliLoggedIn):
            case nameof(StudioViewModel.AccountEmail):
            case nameof(StudioViewModel.DisplayAccountLabel):
            case nameof(StudioViewModel.ShowOAuthCodeInput):
            case nameof(StudioViewModel.OauthCodeText):
            case nameof(StudioViewModel.WakeGPUChoice):
            case nameof(StudioViewModel.IsWakingColab):
            case nameof(StudioViewModel.MaskServerURL):
            case nameof(StudioViewModel.ColabServerURL):
            case nameof(StudioViewModel.AdminPassword):
            case nameof(StudioViewModel.DisplayGpuName):
            case nameof(StudioViewModel.DrivePersisted):
            case nameof(StudioViewModel.CombinedTelemetryChartMode):
                SyncColabConnectionCard();
                SyncUsageCard();
                SyncModelHubActionsAndStatus();
                SyncAdaptiveParamsCard();
                break;

            case nameof(StudioViewModel.VramUsedGB):
            case nameof(StudioViewModel.VramFreeGB):
            case nameof(StudioViewModel.VramTotalGB):
            case nameof(StudioViewModel.VramHistoryPct):
            case nameof(StudioViewModel.RamUsedGB):
            case nameof(StudioViewModel.RamFreeGB):
            case nameof(StudioViewModel.RamTotalGB):
            case nameof(StudioViewModel.RamHistoryPct):
            case nameof(StudioViewModel.DiskUsedGB):
            case nameof(StudioViewModel.DiskFreeGB):
            case nameof(StudioViewModel.DiskTotalGB):
            case nameof(StudioViewModel.DiskHistoryPct):
            case nameof(StudioViewModel.ShmTotalGB):
            case nameof(StudioViewModel.ShmFreeGB):
            case nameof(StudioViewModel.DriveTotalGB):
            case nameof(StudioViewModel.DriveFreeGB):
                SyncTelemetrySection();
                break;

            case nameof(StudioViewModel.OfficialCUBalance):
            case nameof(StudioViewModel.DisplayRemainingCU):
            case nameof(StudioViewModel.OfficialBurnRate):
            case nameof(StudioViewModel.DisplayBurnRate):
            case nameof(StudioViewModel.FormattedUptime):
            case nameof(StudioViewModel.UptimeSeconds):
            case nameof(StudioViewModel.IdleTimeoutMinutes):
            case nameof(StudioViewModel.IdleRemainingSeconds):
            case nameof(StudioViewModel.FormattedIdleRemaining):
            case nameof(StudioViewModel.ActiveVMCount):
                SyncUsageCard();
                break;

            case nameof(StudioViewModel.SelectedModelId):
            case nameof(StudioViewModel.CustomModels):
            case nameof(StudioViewModel.AllAvailableModels):
            case nameof(StudioViewModel.ActiveModelSpec):
            case nameof(StudioViewModel.LoadedModelId):
            case nameof(StudioViewModel.LoadedModelIds):
            case nameof(StudioViewModel.LoadedModelsDetail):
            case nameof(StudioViewModel.ModelLoaded):
            case nameof(StudioViewModel.IsSelectedModelLoadedInVRAM):
            case nameof(StudioViewModel.IsUnloadingModel):
            case nameof(StudioViewModel.IsDownloadingWeights):
            case nameof(StudioViewModel.IsLoadingModel):
            case nameof(StudioViewModel.ModelTaskProgress):
            case nameof(StudioViewModel.ModelFiles):
            case nameof(StudioViewModel.DownloadedCount):
            case nameof(StudioViewModel.TotalFileCount):
            case nameof(StudioViewModel.AllDownloaded):
            case nameof(StudioViewModel.TotalDiskSizeGB):
            case nameof(StudioViewModel.HfToken):
            case nameof(StudioViewModel.ComfyRunning):
            case nameof(StudioViewModel.ComfyStarting):
            case nameof(StudioViewModel.ComfyDownloadingWeight):
            case nameof(StudioViewModel.ComfyProgress):
            case nameof(StudioViewModel.ComfyPublicURL):
            case nameof(StudioViewModel.ComfyQueueRunning):
            case nameof(StudioViewModel.ComfyQueuePending):
                SyncModelHubCard();
                SyncAdaptiveParamsCard();
                break;

            case nameof(StudioViewModel.TaskModeId):
            case nameof(StudioViewModel.ComfyPersistenceLabel):
            case nameof(StudioViewModel.DisplayComfyPersistenceLabel):
            case nameof(StudioViewModel.ComfyDrivePersisted):
            case nameof(StudioViewModel.ComfyCustomNodesCount):
            case nameof(StudioViewModel.ComfyModelsCount):
            case nameof(StudioViewModel.ComfyUnloadOthersOnStart):
            case nameof(StudioViewModel.ComfySyncingDrive):
            case nameof(StudioViewModel.EnableThinking):
            case nameof(StudioViewModel.ReasoningEffort):
            case nameof(StudioViewModel.ContextLengthK):
            case nameof(StudioViewModel.ContextLengthTokens):
            case nameof(StudioViewModel.LoadedContextLengthTokens):
            case nameof(StudioViewModel.MaxNewTokens):
            case nameof(StudioViewModel.Temperature):
            case nameof(StudioViewModel.TopP):
            case nameof(StudioViewModel.SelectedImageResolution):
            case nameof(StudioViewModel.SelectedVideoResolution):
            case nameof(StudioViewModel.NumFrames):
            case nameof(StudioViewModel.Fps):
            case nameof(StudioViewModel.InferenceSteps):
            case nameof(StudioViewModel.DenoisingStrength):
            case nameof(StudioViewModel.Seed):
            case nameof(StudioViewModel.RandomizeSeed):
            case nameof(StudioViewModel.BatchCount):
            case nameof(StudioViewModel.IsGenerating):
                SyncHubContextSection();
                SyncAdaptiveParamsCard();
                break;
        }
    }

    private void SyncAll()
    {
        SyncColabConnectionCard();
        SyncTelemetrySection();
        SyncUsageCard();
        SyncModelHubCard();
        SyncAdaptiveParamsCard();
    }

    // ── Card 1: Colab Compute Instance ──

    private void SyncColabConnectionCard()
    {
        _syncingUi = true;
        try
        {
            ColabInstanceCard.Badge = ViewModel.IsConnected ? L("在线", "Online") : L("离线", "Offline");

            AccountLabelText.Text = ViewModel.DisplayAccountLabel;
            AccountLabelText.Foreground = ResolveBrush(ViewModel.CliLoggedIn ? "StudioSystemGreenBrush" : "StudioTextSecondaryBrush");
            ToolTipService.SetToolTip(
                AccountLabelText,
                string.IsNullOrEmpty(ViewModel.AccountEmail)
                    ? L("当前 Google 账号状态", "Google account connection status")
                    : ViewModel.DisplayAccountLabel);

            AccountRefreshButton.Visibility = ViewModel.CliLoggedIn ? Visibility.Visible : Visibility.Collapsed;
            AccountMenuButton.Visibility = ViewModel.CliLoggedIn ? Visibility.Visible : Visibility.Collapsed;
            AccountBindButton.Visibility = ViewModel.CliLoggedIn ? Visibility.Collapsed : Visibility.Visible;

            OAuthCodeRow.Visibility = ViewModel.ShowOAuthCodeInput ? Visibility.Visible : Visibility.Collapsed;
            if (OAuthCodeBox.Text != ViewModel.OauthCodeText)
                OAuthCodeBox.Text = ViewModel.OauthCodeText;

            WakeControlsRow.Visibility = ViewModel.CliLoggedIn ? Visibility.Visible : Visibility.Collapsed;
            SelectComboByTag(GpuPicker, ViewModel.WakeGPUChoice);

            WakeButton.IsEnabled = !ViewModel.IsWakingColab;
            WakeSpinner.IsActive = ViewModel.IsWakingColab;
            WakeSpinner.Visibility = ViewModel.IsWakingColab ? Visibility.Visible : Visibility.Collapsed;
            WakeBoltIcon.Visibility = ViewModel.IsWakingColab ? Visibility.Collapsed : Visibility.Visible;
            WakeButtonText.Text = ViewModel.IsWakingColab
                ? L("唤醒部署中...", "Deploying...")
                : L("唤醒实例并部署", "Wake & Deploy");
            StopWakeButton.Visibility = ViewModel.IsWakingColab ? Visibility.Visible : Visibility.Collapsed;

            ServerUrlTextBox.Visibility = ViewModel.MaskServerURL ? Visibility.Collapsed : Visibility.Visible;
            ServerUrlPasswordBox.Visibility = ViewModel.MaskServerURL ? Visibility.Visible : Visibility.Collapsed;
            if (ServerUrlTextBox.Text != ViewModel.ColabServerURL)
                ServerUrlTextBox.Text = ViewModel.ColabServerURL;
            if (ServerUrlPasswordBox.Password != ViewModel.ColabServerURL)
                ServerUrlPasswordBox.Password = ViewModel.ColabServerURL;

            MaskUrlIcon.Glyph = ViewModel.MaskServerURL ? "\uED1A" : "\uE7B3";
            MaskUrlIcon.Foreground = ResolveBrush(ViewModel.MaskServerURL ? "StudioSystemAccentBrush" : "StudioTextSecondaryBrush");
            ToolTipService.SetToolTip(
                MaskUrlToggleButton,
                ViewModel.MaskServerURL
                    ? L("取消打码，显示真实隧道地址", "Reveal tunnel URL")
                    : L("录屏/截图防泄密：打码隐藏公网隧道地址", "Privacy mode: redact public tunnel URL for screenshots"));

            ConnectRefreshButton.Content = ViewModel.IsConnected ? L("刷新", "Refresh") : L("连接", "Connect");

            if (AdminPasswordBox.Password != ViewModel.AdminPassword)
                AdminPasswordBox.Password = ViewModel.AdminPassword;

            GpuStatusDot.Color = ResolveBrush(ViewModel.IsConnected ? "StudioSystemGreenBrush" : "StudioSystemOrangeBrush");
            GpuNameText.Text = ViewModel.DisplayGpuName;
            DrivePersistedBadge.Visibility = ViewModel.DrivePersisted ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _syncingUi = false;
        }

        SyncTelemetrySection();
    }

    private void SyncTelemetrySection()
    {
        bool hasAny = ViewModel.VramTotalGB > 0 || ViewModel.RamTotalGB > 0 || ViewModel.DiskTotalGB > 0;
        TelemetryModeToggleButton.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
        TelemetryChartsContainer.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;

        if (!hasAny) return;

        bool combined = ViewModel.CombinedTelemetryChartMode;
        TelemetryModeIcon.Glyph = combined ? "\uE8A9" : "\uE9D2";
        TelemetryModeText.Text = combined ? L("分项", "Split") : L("同屏", "Combined");
        ToolTipService.SetToolTip(
            TelemetryModeToggleButton,
            combined
                ? L("切换为显存/内存/硬盘独立折线面积图", "Switch to separate VRAM / RAM / Disk area charts")
                : L("切换为显存/内存/硬盘三线同屏对比图", "Switch to combined 3-line telemetry comparison chart"));

        CombinedChartCard.Visibility = combined ? Visibility.Visible : Visibility.Collapsed;
        SingleChartsPanel.Visibility = combined ? Visibility.Collapsed : Visibility.Visible;

        if (combined)
        {
            CombinedChartCard.VramUsed = ViewModel.VramUsedGB;
            CombinedChartCard.VramTotal = ViewModel.VramTotalGB;
            CombinedChartCard.VramSamples = ViewModel.VramHistoryPct;
            CombinedChartCard.RamUsed = ViewModel.RamUsedGB;
            CombinedChartCard.RamTotal = ViewModel.RamTotalGB;
            CombinedChartCard.RamSamples = ViewModel.RamHistoryPct;
            CombinedChartCard.DiskUsed = ViewModel.DiskUsedGB;
            CombinedChartCard.DiskTotal = ViewModel.DiskTotalGB;
            CombinedChartCard.DiskSamples = ViewModel.DiskHistoryPct;
        }
        else
        {
            VramChartCard.Visibility = ViewModel.VramTotalGB > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (ViewModel.VramTotalGB > 0)
            {
                VramChartCard.UsedGB = ViewModel.VramUsedGB;
                VramChartCard.FreeGB = ViewModel.VramFreeGB;
                VramChartCard.TotalGB = ViewModel.VramTotalGB;
                VramChartCard.Samples = ViewModel.VramHistoryPct;
                VramChartCard.IsWarning = ViewModel.IsVramWarning;
                VramChartCard.IsCritical = ViewModel.IsVramCritical;
            }

            RamChartCard.Visibility = ViewModel.RamTotalGB > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (ViewModel.RamTotalGB > 0)
            {
                RamChartCard.UsedGB = ViewModel.RamUsedGB;
                RamChartCard.FreeGB = ViewModel.RamFreeGB;
                RamChartCard.TotalGB = ViewModel.RamTotalGB;
                RamChartCard.Samples = ViewModel.RamHistoryPct;
                RamChartCard.IsWarning = ViewModel.IsRamWarning;
                RamChartCard.IsCritical = ViewModel.IsRamCritical;
            }

            DiskChartCard.Visibility = ViewModel.DiskTotalGB > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (ViewModel.DiskTotalGB > 0)
            {
                DiskChartCard.UsedGB = ViewModel.DiskUsedGB;
                DiskChartCard.FreeGB = ViewModel.DiskFreeGB;
                DiskChartCard.TotalGB = ViewModel.DiskTotalGB;
                DiskChartCard.Samples = ViewModel.DiskHistoryPct;
                DiskChartCard.IsWarning = ViewModel.IsDiskWarning;
                DiskChartCard.IsCritical = ViewModel.IsDiskCritical;
            }
        }

        bool showShmDrive = ViewModel.ShmTotalGB >= 1.0 || ViewModel.DriveTotalGB > 0;
        ShmDriveRow.Visibility = showShmDrive ? Visibility.Visible : Visibility.Collapsed;
        if (showShmDrive)
        {
            ShmFreeText.Text = ViewModel.ShmTotalGB >= 1.0
                ? L(
                    $"内存盘可用 {WidgetFormat.Fmt(ViewModel.ShmFreeGB, 1)}/{WidgetFormat.Fmt(ViewModel.ShmTotalGB, 0)}G",
                    $"/dev/shm Free {WidgetFormat.Fmt(ViewModel.ShmFreeGB, 1)}/{WidgetFormat.Fmt(ViewModel.ShmTotalGB, 0)}G")
                : string.Empty;
            DriveFreeText.Text = ViewModel.DriveTotalGB > 0
                ? L(
                    $"云盘可用 {WidgetFormat.Fmt(ViewModel.DriveFreeGB, 1)}G",
                    $"Drive Free {WidgetFormat.Fmt(ViewModel.DriveFreeGB, 1)}G")
                : string.Empty;
        }
    }

    // ── Card 2: Compute Quota & Auto-Sleep ──

    private void SyncUsageCard()
    {
        _syncingUi = true;
        try
        {
            CuBalanceLabel.Text = ViewModel.OfficialCUBalance.HasValue
                ? L("账户余额", "CU Balance")
                : L("预计余额", "Est. Balance");
            CuBalanceValue.Text = $"{WidgetFormat.Fmt(ViewModel.DisplayRemainingCU, 2)} CU";

            BurnRateValue.Text = $"{WidgetFormat.Fmt(ViewModel.DisplayBurnRate, 2)} CU/h";
            BurnRateValue.Foreground = ResolveBrush(
                ViewModel.DisplayBurnRate > 0 ? "StudioAccentOrangeBrush" : "StudioTextSecondaryBrush");

            UptimeValue.Text = ViewModel.FormattedUptime;

            SelectComboByTag(IdleTimeoutPicker, ViewModel.IdleTimeoutMinutes.ToString(CultureInfo.InvariantCulture));

            bool showIdleRem = ViewModel.IsConnected && ViewModel.IdleTimeoutMinutes > 0;
            IdleRemainingText.Visibility = showIdleRem ? Visibility.Visible : Visibility.Collapsed;
            if (showIdleRem)
                IdleRemainingText.Text = ViewModel.FormattedIdleRemaining;

            ReleaseAllButtonText.Text = ViewModel.ActiveVMCount > 0
                ? L($"释放全部实例 ({ViewModel.ActiveVMCount} 台运行中)", $"Release All Instances ({ViewModel.ActiveVMCount} Running)")
                : L("释放全部实例", "Release All Instances");
        }
        finally
        {
            _syncingUi = false;
        }
    }

    // ── Card 3: Open Model Center ──

    private void SyncModelHubCard()
    {
        var allModels = ViewModel.AllAvailableModels;
        OpenModelCenterCard.Badge = ViewModel.LoadedModelIds.Count == 0
            ? $"HF {allModels.Count}"
            : L($"共驻 {ViewModel.LoadedModelIds.Count} 模型", $"{ViewModel.LoadedModelIds.Count} in VRAM");

        // Rebuild model list only when selection/list/loaded state changes
        var modelSig = $"{ViewModel.SelectedModelId}|{string.Join(",", ViewModel.LoadedModelIds)}|{ViewModel.LoadedModelId}|{ViewModel.ModelLoaded}|{ViewModel.ComfyRunning}|{ViewModel.ComfyQueueRunning > 0}|{string.Join(",", allModels.Select(m => m.Id + ":" + m.Name))}";
        if (modelSig != _lastModelListSig)
        {
            _lastModelListSig = modelSig;
            RebuildModelList(allModels);
        }

        // Rebuild co-resident list when changed
        var coSig = $"{ViewModel.IsUnloadingModel}|{string.Join(",", ViewModel.LoadedModelsDetail.Select(d => $"{d.Id}:{d.ContextLength}:{d.VramGb}"))}";
        if (coSig != _lastCoResidentSig)
        {
            _lastCoResidentSig = coSig;
            RebuildCoResidentList();
        }

        SyncModelHubActionsAndStatus();
    }

    private void RebuildModelList(IReadOnlyList<OpenModelSpec> allModels)
    {
        ModelListPanel.Children.Clear();

        var accentBrush = ResolveBrush("StudioSystemAccentBrush");
        var accentFillBrush = ResolveBrush("StudioAccentBlueFillBrush");
        var inputFillBrush = ResolveBrush("StudioInputFillBrush");
        var greenBrush = ResolveBrush("StudioSystemGreenBrush");
        var greenFillBrush = ResolveBrush("StudioAccentGreenFillBrush");
        var primaryText = ResolveBrush("StudioTextPrimaryBrush");
        var secondaryText = ResolveBrush("StudioTextSecondaryBrush");
        var whiteBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
        var transparentBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        foreach (var spec in allModels)
        {
            bool isSelected = ViewModel.SelectedModelId == spec.Id;
            bool isLoaded = ViewModel.LoadedModelIds.Contains(spec.Id)
                || (ViewModel.LoadedModelId == spec.Id && ViewModel.ModelLoaded);

            var rowGrid = new Grid { ColumnSpacing = 9 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Icon box
            var iconBox = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(6),
                Background = isSelected ? accentBrush : accentFillBrush,
                Child = new FontIcon
                {
                    Glyph = MapSfSymbolToGlyph(spec.Icon),
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = isSelected ? whiteBrush : accentBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Grid.SetColumn(iconBox, 0);
            rowGrid.Children.Add(iconBox);

            // Title + live badge + subtitle
            var infoStack = new StackPanel { Spacing = 1.5, VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            titleRow.Children.Add(new TextBlock
            {
                Text = spec.Name,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = primaryText,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            if (spec.Modality == ModelModality.ComfyUI && ViewModel.ComfyRunning)
            {
                titleRow.Children.Add(MakeGreenPill(
                    ViewModel.ComfyQueueRunning > 0 ? L("渲染中", "Rendering") : L("画布在线", "Canvas Live"),
                    greenBrush, greenFillBrush));
            }
            else if (isLoaded)
            {
                titleRow.Children.Add(MakeGreenPill(L("显存常驻", "In VRAM"), greenBrush, greenFillBrush));
            }

            infoStack.Children.Add(titleRow);
            infoStack.Children.Add(new TextBlock
            {
                Text = spec.Badge,
                FontSize = 10.5,
                Foreground = secondaryText,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(infoStack, 1);
            rowGrid.Children.Add(infoStack);

            // Custom model delete button
            if (spec.IsCustom)
            {
                var captured = spec;
                var delBtn = new Button
                {
                    Style = (Style)Resources["CompactIconButtonStyle"],
                    Content = new FontIcon { Glyph = "\uE74D", FontSize = 10.5, Foreground = secondaryText },
                };
                ToolTipService.SetToolTip(delBtn, L($"移除自定义模型 {spec.Name}", $"Remove custom model {spec.Name}"));
                delBtn.Click += async (_, _) => await ViewModel.RemoveCustomHFModelAsync(captured);
                Grid.SetColumn(delBtn, 2);
                rowGrid.Children.Add(delBtn);
            }

            if (isSelected)
            {
                var check = new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 13,
                    Foreground = accentBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(check, 3);
                rowGrid.Children.Add(check);
            }

            var border = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(8),
                Background = isSelected ? accentFillBrush : inputFillBrush,
                BorderBrush = isSelected ? accentBrush : transparentBrush,
                BorderThickness = new Thickness(1),
                Child = rowGrid,
            };

            var specCaptured = spec;
            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Style = (Style)Application.Current.Resources["StudioPlainButtonStyle"],
                Content = border,
            };
            btn.Click += (_, _) => ViewModel.SwitchSelectedModel(specCaptured);

            if (spec.IsCustom)
            {
                var flyout = new MenuFlyout();
                var removeItem = new MenuFlyoutItem
                {
                    Text = L("移除该自定义模型", "Remove Custom Model"),
                    Icon = new FontIcon { Glyph = "\uE74D" },
                };
                removeItem.Click += async (_, _) => await ViewModel.RemoveCustomHFModelAsync(specCaptured);
                flyout.Items.Add(removeItem);
                btn.ContextFlyout = flyout;
            }

            ModelListPanel.Children.Add(btn);
        }
    }

    private static Border MakeGreenPill(string text, Brush fg, Brush bg) =>
        new()
        {
            Padding = new Thickness(5, 1, 5, 1),
            CornerRadius = new CornerRadius(6),
            Background = bg,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = fg,
            },
        };

    private void RebuildCoResidentList()
    {
        var list = ViewModel.LoadedModelsDetail;
        CoResidentSection.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (list.Count == 0) return;

        CoResidentHeaderText.Text = L(
            $"GPU 显存共驻模型 ({list.Count})",
            $"Co-Resident in VRAM ({list.Count})");
        UnloadAllModelsButton.Visibility = list.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        UnloadAllModelsButton.IsEnabled = !ViewModel.IsUnloadingModel;

        CoResidentListPanel.Children.Clear();
        var inputFill = ResolveBrush("StudioInputFillBrush");
        var greenBrush = ResolveBrush("StudioSystemGreenBrush");
        var accentBrush = ResolveBrush("StudioSystemAccentBrush");
        var accentFill = ResolveBrush("StudioAccentBlueFillBrush");
        var primaryText = ResolveBrush("StudioTextPrimaryBrush");
        var secondaryText = ResolveBrush("StudioTextSecondaryBrush");
        var monoFont = (FontFamily)Application.Current.Resources["StudioMonoFont"];

        foreach (var item in list)
        {
            var g = new Grid { ColumnSpacing = 6 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new StatusDot { Color = greenBrush };
            Grid.SetColumn(dot, 0);
            g.Children.Add(dot);

            var nameText = new TextBlock
            {
                Text = item.LocalizedName,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = primaryText,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(nameText, 1);
            g.Children.Add(nameText);

            if (item.ContextLength is { } ctx && ctx > 0)
            {
                var ctxPill = new Border
                {
                    Padding = new Thickness(4, 1, 4, 1),
                    CornerRadius = new CornerRadius(6),
                    Background = accentFill,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = $"{ctx / 1024}K",
                        FontFamily = monoFont,
                        FontSize = 9.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = accentBrush,
                    },
                };
                Grid.SetColumn(ctxPill, 2);
                g.Children.Add(ctxPill);
            }

            if (item.VramGb is { } gb && gb > 0)
            {
                var vramText = new TextBlock
                {
                    Text = $"~{WidgetFormat.Fmt(gb, 1)}G",
                    FontFamily = monoFont,
                    FontSize = 10,
                    Foreground = secondaryText,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(vramText, 3);
                g.Children.Add(vramText);
            }

            var capturedId = item.Id;
            var unloadBtn = new Button
            {
                Style = (Style)Resources["CompactIconButtonStyle"],
                IsEnabled = !ViewModel.IsUnloadingModel,
                Content = new FontIcon { Glyph = "\uE711", FontSize = 10.5, Foreground = secondaryText },
            };
            ToolTipService.SetToolTip(
                unloadBtn,
                L($"从 GPU 显存卸载 {item.LocalizedName}", $"Unload {item.LocalizedName} from GPU VRAM"));
            unloadBtn.Click += async (_, _) => await ViewModel.UnloadRemoteModelFromVRAMAsync(capturedId);
            Grid.SetColumn(unloadBtn, 4);
            g.Children.Add(unloadBtn);

            CoResidentListPanel.Children.Add(new Border
            {
                Padding = new Thickness(7, 4, 7, 4),
                CornerRadius = new CornerRadius(6),
                Background = inputFill,
                Child = g,
            });
        }
    }

    private void SyncModelHubActionsAndStatus()
    {
        bool isComfy = ViewModel.ActiveModelSpec.Modality == ModelModality.ComfyUI;
        ComfyHubSection.Visibility = isComfy ? Visibility.Visible : Visibility.Collapsed;
        StandardHubSection.Visibility = isComfy ? Visibility.Collapsed : Visibility.Visible;

        if (isComfy)
        {
            ComfyCanvasStatusText.Text = ViewModel.ComfyRunning
                ? L("在线 (端口 8188)", "Online (Port 8188)")
                : (ViewModel.ComfyStarting ? L("部署启动中...", "Starting...") : L("未启动", "Stopped"));
            ComfyCanvasStatusText.Foreground = ResolveBrush(
                ViewModel.ComfyRunning
                    ? "StudioSystemGreenBrush"
                    : (ViewModel.ComfyStarting ? "StudioAccentOrangeBrush" : "StudioTextSecondaryBrush"));

            bool showProgress = ViewModel.ComfyStarting || ViewModel.ComfyDownloadingWeight;
            ComfyHubProgressBar.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
            if (showProgress)
                ComfyHubProgressBar.Value = Math.Clamp(ViewModel.ComfyProgress, 0.0, 1.0);

            bool hasRunningUrl = ViewModel.ComfyRunning && !string.IsNullOrEmpty(ViewModel.ComfyPublicURL);
            ComfyRunningButtonsGrid.Visibility = hasRunningUrl ? Visibility.Visible : Visibility.Collapsed;
            StartComfyButton.Visibility = hasRunningUrl ? Visibility.Collapsed : Visibility.Visible;

            StartComfyButton.IsEnabled = ViewModel.IsConnected && !ViewModel.ComfyStarting;
            StartComfySpinner.IsActive = ViewModel.ComfyStarting;
            StartComfySpinner.Visibility = ViewModel.ComfyStarting ? Visibility.Visible : Visibility.Collapsed;
            StartComfyIcon.Visibility = ViewModel.ComfyStarting ? Visibility.Collapsed : Visibility.Visible;
            StartComfyText.Text = ViewModel.ComfyStarting
                ? L("正在部署...", "Deploying...")
                : L("启动 ComfyUI 画布", "Start ComfyUI Canvas");
            return;
        }

        // Standard (non-ComfyUI) model hub
        HfRepoIdText.Text = ViewModel.ActiveModelSpec.RepoId;

        DiskWeightsSummaryText.Text = ViewModel.AllDownloaded
            ? L(
                $"已就绪 {ViewModel.DownloadedCount}/{ViewModel.TotalFileCount} ({WidgetFormat.Fmt(ViewModel.TotalDiskSizeGB, 1)} GB)",
                $"Ready {ViewModel.DownloadedCount}/{ViewModel.TotalFileCount} ({WidgetFormat.Fmt(ViewModel.TotalDiskSizeGB, 1)} GB)")
            : L(
                $"{ViewModel.DownloadedCount}/{ViewModel.TotalFileCount} 个文件",
                $"{ViewModel.DownloadedCount}/{ViewModel.TotalFileCount} files");
        DiskWeightsSummaryText.Foreground = ResolveBrush(
            ViewModel.AllDownloaded ? "StudioSystemGreenBrush" : "StudioTextSecondaryBrush");

        FileListToggleText.Text = L(
            $"查看权重分片 ({ViewModel.ModelFiles.Count})",
            $"View Weight Shards ({ViewModel.ModelFiles.Count})");
        FileListChevron.Glyph = _showFileList ? "\uE70D" : "\uE76C";
        FileListContainer.Visibility = _showFileList ? Visibility.Visible : Visibility.Collapsed;

        var fileSig = string.Join("|", ViewModel.ModelFiles.Select(f => $"{f.Name}:{f.Exists}:{f.SizeGb:F1}"));
        if (fileSig != _lastFileListSig)
        {
            _lastFileListSig = fileSig;
            RebuildFileList();
        }

        _syncingUi = true;
        try
        {
            if (HfTokenPasswordBox.Password != ViewModel.HfToken)
                HfTokenPasswordBox.Password = ViewModel.HfToken;
        }
        finally
        {
            _syncingUi = false;
        }

        SyncHubContextSection();

        // Offline warning banner
        OfflineBanner.Visibility = ViewModel.IsConnected ? Visibility.Collapsed : Visibility.Visible;
        if (!ViewModel.IsConnected)
        {
            bool showWake = ViewModel.CliLoggedIn;
            bool showReconnect = !showWake && !string.IsNullOrEmpty(ViewModel.NormalizedBaseURL);
            OfflineWakeButton.Visibility = showWake ? Visibility.Visible : Visibility.Collapsed;
            OfflineWakeButton.IsEnabled = !ViewModel.IsWakingColab;
            OfflineWakeButton.Content = ViewModel.IsWakingColab ? L("唤醒中...", "Waking...") : L("一键唤醒", "Wake");
            OfflineReconnectButton.Visibility = showReconnect ? Visibility.Visible : Visibility.Collapsed;
        }

        bool busyTask = ViewModel.IsDownloadingWeights || ViewModel.IsLoadingModel;
        ModelTaskProgressBar.Visibility = busyTask ? Visibility.Visible : Visibility.Collapsed;
        if (busyTask)
            ModelTaskProgressBar.Value = Math.Clamp(ViewModel.ModelTaskProgress, 0.0, 1.0);

        bool canAct = ViewModel.IsConnected
            && !ViewModel.IsDownloadingWeights
            && !ViewModel.IsLoadingModel
            && !ViewModel.IsUnloadingModel;

        DownloadWeightsButton.IsEnabled = canAct;
        DownloadSpinner.IsActive = ViewModel.IsDownloadingWeights;
        DownloadSpinner.Visibility = ViewModel.IsDownloadingWeights ? Visibility.Visible : Visibility.Collapsed;
        DownloadIcon.Visibility = ViewModel.IsDownloadingWeights ? Visibility.Collapsed : Visibility.Visible;
        DownloadButtonText.Text = ViewModel.IsDownloadingWeights
            ? L("下载中...", "Downloading...")
            : (ViewModel.AllDownloaded ? L("校验权重", "Verify Weights") : L("下载权重", "Download Weights"));

        LoadVramButton.IsEnabled = canAct;
        LoadVramSpinner.IsActive = ViewModel.IsLoadingModel;
        LoadVramSpinner.Visibility = ViewModel.IsLoadingModel ? Visibility.Visible : Visibility.Collapsed;
        LoadVramIcon.Visibility = ViewModel.IsLoadingModel ? Visibility.Collapsed : Visibility.Visible;
        LoadVramButtonText.Text = ViewModel.IsLoadingModel
            ? L("装载中...", "Loading...")
            : (ViewModel.IsSelectedModelLoadedInVRAM ? L("重新装载", "Reload") : L("加载到显存", "Load to VRAM"));

        UnloadActiveModelButton.Visibility = ViewModel.IsSelectedModelLoadedInVRAM ? Visibility.Visible : Visibility.Collapsed;
        UnloadActiveModelButton.IsEnabled = !ViewModel.IsUnloadingModel;
    }

    private void RebuildFileList()
    {
        FileListItemsPanel.Children.Clear();
        var greenBrush = ResolveBrush("StudioSystemGreenBrush");
        var primaryText = ResolveBrush("StudioTextPrimaryBrush");
        var secondaryText = ResolveBrush("StudioTextSecondaryBrush");
        var monoFont = (FontFamily)Application.Current.Resources["StudioMonoFont"];

        foreach (var file in ViewModel.ModelFiles)
        {
            var g = new Grid { ColumnSpacing = 5 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new FontIcon
            {
                Glyph = file.Exists ? "\uE73E" : "\uEA3A",
                FontSize = 10,
                Foreground = file.Exists ? greenBrush : secondaryText,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(icon, 0);
            g.Children.Add(icon);

            var v = new StackPanel { Spacing = 1 };
            v.Children.Add(new TextBlock
            {
                Text = file.Name,
                FontFamily = monoFont,
                FontSize = 10.5,
                Foreground = primaryText,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (!string.IsNullOrEmpty(file.LocalizedDesc))
            {
                v.Children.Add(new TextBlock
                {
                    Text = file.LocalizedDesc,
                    FontSize = 9.5,
                    Foreground = secondaryText,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
            Grid.SetColumn(v, 1);
            g.Children.Add(v);

            var sizeText = new TextBlock
            {
                Text = file.Exists ? $"{WidgetFormat.Fmt(file.SizeGb, 1)}G" : L("未下", "Pending"),
                FontSize = 10,
                Foreground = secondaryText,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(sizeText, 2);
            g.Children.Add(sizeText);

            ToolTipService.SetToolTip(g, file.LocalizedDesc ?? file.Name);
            FileListItemsPanel.Children.Add(g);
        }
    }

    private void SyncHubContextSection()
    {
        bool isLlm = ViewModel.ActiveModelSpec.Modality == ModelModality.LlmVLM;
        HubContextSection.Visibility = isLlm ? Visibility.Visible : Visibility.Collapsed;
        if (!isLlm) return;

        var pickerKs = HubContextPresets.Contains(ViewModel.ContextLengthK)
            ? HubContextPresets.ToList()
            : HubContextPresets.Append(ViewModel.ContextLengthK).OrderBy(k => k).ToList();

        var sig = string.Join(",", pickerKs);
        _syncingUi = true;
        try
        {
            if (sig != _lastHubCtxSig)
            {
                _lastHubCtxSig = sig;
                HubContextPicker.Items.Clear();
                foreach (var k in pickerKs)
                {
                    var label = k == 256 ? L("256K (最大)", "256K (Max)") : $"{k}K ({k * 1024})";
                    HubContextPicker.Items.Add(new ComboBoxItem { Content = label, Tag = k.ToString(CultureInfo.InvariantCulture) });
                }
            }
            SelectComboByTag(HubContextPicker, ViewModel.ContextLengthK.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            _syncingUi = false;
        }

        if (ViewModel.SelectedModelId == "qwen38_27b_gguf"
            && ViewModel.IsSelectedModelLoadedInVRAM
            && ViewModel.LoadedContextLengthTokens > 0
            && ViewModel.LoadedContextLengthTokens != ViewModel.ContextLengthTokens)
        {
            HubContextHintText.Visibility = Visibility.Visible;
            HubContextHintText.Foreground = ResolveBrush("StudioAccentOrangeBrush");
            HubContextHintText.Text = L(
                $"当前显存 KV 为 {ViewModel.LoadedContextLengthTokens / 1024}K，点「重新装载」切换至 {ViewModel.ContextLengthK}K",
                $"VRAM KV cache is {ViewModel.LoadedContextLengthTokens / 1024}K; click 'Reload' to apply {ViewModel.ContextLengthK}K");
        }
        else if (ViewModel.SelectedModelId == "qwen38_27b_gguf" && ViewModel.ContextLengthK >= 128)
        {
            HubContextHintText.Visibility = Visibility.Visible;
            HubContextHintText.Foreground = ResolveBrush("StudioTextSecondaryBrush");
            HubContextHintText.Text = L(
                "提示：日常对话选 32K/64K 可达峰值 90~110 tok/s；256K 预分配超长槽位约为 80+ tok/s",
                "Tip: 32K/64K hits peak 90–110 tok/s; 256K pre-allocated context runs at ~80+ tok/s");
        }
        else
        {
            HubContextHintText.Visibility = Visibility.Collapsed;
        }
    }

    // ── Card 4: Adaptive Parameters ──

    private void SyncAdaptiveParamsCard()
    {
        var modality = ViewModel.ActiveModelSpec.Modality;

        AdaptiveParamsCard.Title = modality switch
        {
            ModelModality.ComfyUI => L("ComfyUI 云盘持久化与控制", "ComfyUI Persistence & Controls"),
            ModelModality.LlmVLM => L("推理与对话参数", "Reasoning & Chat Params"),
            _ => L("规格与采样参数", "Specs & Sampling Params"),
        };
        AdaptiveParamsCard.Icon = modality == ModelModality.ComfyUI ? "\uE753" : "\uE9E9";

        ComfyParamsPanel.Visibility = modality == ModelModality.ComfyUI ? Visibility.Visible : Visibility.Collapsed;
        LlmParamsPanel.Visibility = modality == ModelModality.LlmVLM ? Visibility.Visible : Visibility.Collapsed;
        ImageParamsPanel.Visibility = modality == ModelModality.Image ? Visibility.Visible : Visibility.Collapsed;
        VideoParamsPanel.Visibility = modality is ModelModality.Video or ModelModality.VideoAudio
            ? Visibility.Visible
            : Visibility.Collapsed;
        SeedAndBatchPanel.Visibility = modality is ModelModality.Image or ModelModality.Video or ModelModality.VideoAudio
            ? Visibility.Visible
            : Visibility.Collapsed;

        _syncingUi = true;
        try
        {
            switch (modality)
            {
                case ModelModality.ComfyUI:
                    SyncComfyParams();
                    break;
                case ModelModality.LlmVLM:
                    SyncLlmParams();
                    break;
                case ModelModality.Image:
                    SyncImageParams();
                    SyncSeedAndBatch("张");
                    break;
                case ModelModality.Video:
                case ModelModality.VideoAudio:
                    SyncVideoParams();
                    SyncSeedAndBatch("条");
                    break;
            }
        }
        finally
        {
            _syncingUi = false;
        }
    }

    private void SyncComfyParams()
    {
        ComfyPersistenceValueText.Text = ViewModel.DisplayComfyPersistenceLabel;
        ComfyPersistenceValueText.Foreground = ResolveBrush(
            ViewModel.ComfyDrivePersisted ? "StudioSystemGreenBrush" : "StudioTextSecondaryBrush");

        ComfySavedCountsText.Text = L(
            $"{ViewModel.ComfyCustomNodesCount} 个节点 · {ViewModel.ComfyModelsCount} 个权重",
            $"{ViewModel.ComfyCustomNodesCount} nodes · {ViewModel.ComfyModelsCount} weights");

        ComfyQueueText.Text = L(
            $"运行 {ViewModel.ComfyQueueRunning} · 排队 {ViewModel.ComfyQueuePending}",
            $"Running {ViewModel.ComfyQueueRunning} · Pending {ViewModel.ComfyQueuePending}");
        ComfyQueueText.Foreground = ResolveBrush(
            ViewModel.ComfyQueueRunning > 0 ? "StudioSystemGreenBrush" : "StudioTextSecondaryBrush");

        ComfyUnloadOthersToggle.IsOn = ViewModel.ComfyUnloadOthersOnStart;

        ComfySyncDriveButton.IsEnabled = ViewModel.IsConnected && !ViewModel.ComfySyncingDrive;
        ComfySyncSpinner.IsActive = ViewModel.ComfySyncingDrive;
        ComfySyncSpinner.Visibility = ViewModel.ComfySyncingDrive ? Visibility.Visible : Visibility.Collapsed;
        ComfySyncIcon.Visibility = ViewModel.ComfySyncingDrive ? Visibility.Collapsed : Visibility.Visible;
        ComfySyncButtonText.Text = ViewModel.ComfySyncingDrive
            ? L("同步中...", "Syncing...")
            : L("立即同步云盘", "Sync to Drive");

        ComfyFreeVramButton.IsEnabled = ViewModel.IsConnected && ViewModel.ComfyRunning;
    }

    private void SyncLlmParams()
    {
        EnableThinkingToggle.IsOn = ViewModel.EnableThinking;
        ReasoningEffortPanel.Visibility = ViewModel.EnableThinking ? Visibility.Visible : Visibility.Collapsed;
        if (ViewModel.EnableThinking)
        {
            ReasoningEffortValueText.Text = ViewModel.ReasoningEffort.ToUpperInvariant();
            RebuildReasoningEffortButtons();
        }

        if (Math.Abs(ContextLengthNumberBox.Value - ViewModel.ContextLengthK) > 0.01)
            ContextLengthNumberBox.Value = ViewModel.ContextLengthK;
        if (Math.Abs(ContextLengthSlider.Value - ViewModel.ContextLengthK) > 0.01)
            ContextLengthSlider.Value = ViewModel.ContextLengthK;
        RebuildContextPresetButtons();

        ContextCapacityText.Text = L(
            $"总容量 {ViewModel.ContextLengthTokens} Tokens" + (ViewModel.LoadedContextLengthTokens > 0 ? $" · 当前已加载 {ViewModel.LoadedContextLengthTokens / 1024}K" : ""),
            $"Capacity {ViewModel.ContextLengthTokens} tokens" + (ViewModel.LoadedContextLengthTokens > 0 ? $" · Loaded {ViewModel.LoadedContextLengthTokens / 1024}K" : ""));

        MaxNewTokensValueText.Text = $"{(int)ViewModel.MaxNewTokens} Tokens";
        if (Math.Abs(MaxNewTokensSlider.Value - ViewModel.MaxNewTokens) > 0.5)
            MaxNewTokensSlider.Value = ViewModel.MaxNewTokens;

        TemperatureValueText.Text = $"{WidgetFormat.Fmt(ViewModel.Temperature, 2)} · Top-P {WidgetFormat.Fmt(ViewModel.TopP, 2)} · Top-K 20";
        if (Math.Abs(TemperatureSlider.Value - ViewModel.Temperature) > 0.001)
            TemperatureSlider.Value = ViewModel.Temperature;

        SyncClearChatButton();
    }

    private void SyncClearChatButton()
    {
        ClearChatHistoryButton.IsEnabled = ViewModel.ChatHistory.Count > 0 && !ViewModel.IsGenerating;
    }

    private void RebuildReasoningEffortButtons()
    {
        ReasoningEffortButtonsGrid.Children.Clear();
        var accent = ResolveBrush("StudioSystemAccentBrush");
        var accentFill = ResolveBrush("StudioAccentBlueFillBrush");
        var idleFill = ResolveBrush("StudioPrimaryOpacity06Brush");
        var secondary = ResolveBrush("StudioTextSecondaryBrush");
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        for (int i = 0; i < ReasoningEfforts.Length; i++)
        {
            var (key, zh, en) = ReasoningEfforts[i];
            bool active = ViewModel.ReasoningEffort == key;
            var capturedKey = key;

            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Style = (Style)Application.Current.Resources["StudioPlainButtonStyle"],
                Content = new Border
                {
                    Padding = new Thickness(0, 4, 0, 4),
                    CornerRadius = new CornerRadius(5),
                    Background = active ? accentFill : idleFill,
                    BorderBrush = active ? accent : transparent,
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = L(zh, en),
                        FontSize = 10.5,
                        FontWeight = active ? FontWeights.Bold : FontWeights.Medium,
                        Foreground = active ? accent : secondary,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            };
            btn.Click += (_, _) => ViewModel.ReasoningEffort = capturedKey;
            Grid.SetColumn(btn, i);
            ReasoningEffortButtonsGrid.Children.Add(btn);
        }
    }

    private void RebuildContextPresetButtons()
    {
        ContextPresetButtonsGrid.Children.Clear();
        var accent = ResolveBrush("StudioSystemAccentBrush");
        var accentFill = ResolveBrush("StudioAccentBlueFillBrush");
        var idleFill = ResolveBrush("StudioPrimaryOpacity06Brush");
        var secondary = ResolveBrush("StudioTextSecondaryBrush");
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var monoFont = (FontFamily)Application.Current.Resources["StudioMonoFont"];

        for (int i = 0; i < ContextPresets.Length; i++)
        {
            int k = ContextPresets[i];
            bool active = ViewModel.ContextLengthK == k;
            int capturedK = k;

            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Style = (Style)Application.Current.Resources["StudioPlainButtonStyle"],
                Content = new Border
                {
                    Padding = new Thickness(0, 3, 0, 3),
                    CornerRadius = new CornerRadius(5),
                    Background = active ? accentFill : idleFill,
                    BorderBrush = active ? accent : transparent,
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = $"{k}K",
                        FontFamily = monoFont,
                        FontSize = 10,
                        FontWeight = active ? FontWeights.Bold : FontWeights.Medium,
                        Foreground = active ? accent : secondary,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            };
            btn.Click += async (_, _) =>
            {
                ViewModel.ContextLengthK = capturedK;
                await ViewModel.SyncContextLengthToColabAsync(announce: true);
            };
            Grid.SetColumn(btn, i);
            ContextPresetButtonsGrid.Children.Add(btn);
        }
    }

    private void SyncImageParams()
    {
        var list = ViewModel.ActiveImageResolutions;
        var sig = string.Join(",", list.Select(r => r.Id));
        if (sig != _lastImageResSig)
        {
            _lastImageResSig = sig;
            ImageResolutionPicker.Items.Clear();
            foreach (var opt in list)
            {
                ImageResolutionPicker.Items.Add(new ComboBoxItem { Content = opt.Label, Tag = opt });
            }
        }
        SelectResolutionCombo(ImageResolutionPicker, ViewModel.SelectedImageResolution);

        bool isH3Image = ViewModel.IsMiniMaxH3ImageModel;
        ImageFixedStepsRow.Visibility = isH3Image ? Visibility.Visible : Visibility.Collapsed;
        ImageStepsSliderPanel.Visibility = isH3Image ? Visibility.Collapsed : Visibility.Visible;

        if (!isH3Image)
        {
            ImageStepsValueText.Text = L($"{(int)ViewModel.InferenceSteps} 步", $"{(int)ViewModel.InferenceSteps} steps");
            var clamped = Math.Clamp(ViewModel.InferenceSteps, 4.0, 50.0);
            if (Math.Abs(ImageStepsSlider.Value - clamped) > 0.01)
                ImageStepsSlider.Value = clamped;
        }

        bool showDenoise = ViewModel.SelectedModelId == "z_image_turbo" && ViewModel.TaskModeId == "i2i_edit";
        DenoisingStrengthPanel.Visibility = showDenoise ? Visibility.Visible : Visibility.Collapsed;
        if (showDenoise)
        {
            DenoisingStrengthValueText.Text = WidgetFormat.Fmt(ViewModel.DenoisingStrength, 2);
            if (Math.Abs(DenoisingStrengthSlider.Value - ViewModel.DenoisingStrength) > 0.001)
                DenoisingStrengthSlider.Value = ViewModel.DenoisingStrength;
        }
    }

    private void SyncVideoParams()
    {
        SelectResolutionCombo(VideoResolutionPicker, ViewModel.SelectedVideoResolution);

        double fps = Math.Max(1.0, ViewModel.Fps);
        VideoFramesValueText.Text = L(
            $"{(int)ViewModel.NumFrames} 帧 · 约 {WidgetFormat.Fmt(ViewModel.NumFrames / fps, 1)} 秒",
            $"{(int)ViewModel.NumFrames} frames · ~{WidgetFormat.Fmt(ViewModel.NumFrames / fps, 1)}s");
        if (Math.Abs(VideoFramesSlider.Value - ViewModel.NumFrames) > 0.5)
            VideoFramesSlider.Value = Math.Clamp(ViewModel.NumFrames, 90.0, 362.0);
        RebuildVideoDurationButtons();

        VideoStepsValueText.Text = L($"{(int)ViewModel.InferenceSteps} 步", $"{(int)ViewModel.InferenceSteps} steps");
        var clampedSteps = Math.Clamp(ViewModel.InferenceSteps, 10.0, 60.0);
        if (Math.Abs(VideoStepsSlider.Value - clampedSteps) > 0.5)
            VideoStepsSlider.Value = clampedSteps;
    }

    private void RebuildVideoDurationButtons()
    {
        VideoDurationButtonsGrid.Children.Clear();
        var accent = ResolveBrush("StudioSystemAccentBrush");
        var accentFill = ResolveBrush("StudioAccentBlueFillBrush");
        var idleFill = ResolveBrush("StudioPrimaryOpacity06Brush");
        var secondary = ResolveBrush("StudioTextSecondaryBrush");
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var monoFont = (FontFamily)Application.Current.Resources["StudioMonoFont"];

        for (int i = 0; i < VideoDurationPresets.Length; i++)
        {
            var (frames, tag) = VideoDurationPresets[i];
            bool isSel = (int)ViewModel.NumFrames == frames;
            int capturedFrames = frames;

            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Style = (Style)Application.Current.Resources["StudioPlainButtonStyle"],
                Content = new Border
                {
                    Padding = new Thickness(0, 2.5, 0, 2.5),
                    CornerRadius = new CornerRadius(5),
                    Background = isSel ? accentFill : idleFill,
                    BorderBrush = isSel ? accent : transparent,
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = tag,
                        FontFamily = monoFont,
                        FontSize = 10,
                        FontWeight = isSel ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = isSel ? accent : secondary,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            };
            btn.Click += (_, _) => ViewModel.NumFrames = capturedFrames;
            Grid.SetColumn(btn, i);
            VideoDurationButtonsGrid.Children.Add(btn);
        }
    }

    private void SyncSeedAndBatch(string unitLabelZh)
    {
        var seedStr = ViewModel.Seed.ToString(CultureInfo.InvariantCulture);
        if (SeedTextBox.Text != seedStr)
            SeedTextBox.Text = seedStr;
        SeedTextBox.IsEnabled = !ViewModel.RandomizeSeed;
        RandomizeSeedCheckBox.IsChecked = ViewModel.RandomizeSeed;

        BatchButtonsGrid.Children.Clear();
        var accent = ResolveBrush("StudioSystemAccentBrush");
        var primaryText = ResolveBrush("StudioTextPrimaryBrush");
        var whiteBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        for (int i = 0; i < 4; i++)
        {
            int count = i + 1;
            bool isSelected = ViewModel.BatchCount == count;
            int capturedCount = count;

            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Style = (Style)Application.Current.Resources["StudioPlainButtonStyle"],
                Content = new Border
                {
                    Padding = new Thickness(0, 4, 0, 4),
                    CornerRadius = new CornerRadius(6),
                    Background = isSelected ? accent : transparent,
                    Child = new TextBlock
                    {
                        Text = L($"{count} {unitLabelZh}", $"{count}×"),
                        FontSize = 11.5,
                        FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = isSelected ? whiteBrush : primaryText,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            };
            btn.Click += (_, _) => ViewModel.BatchCount = capturedCount;
            Grid.SetColumn(btn, i);
            BatchButtonsGrid.Children.Add(btn);
        }
    }

    // ── Event Handlers ──

    private async void AccountRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshOfficialColabAccountUsageAsync();
    }

    private async void SwitchAccountMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartGoogleOAuthLoginAsync(switchingAccount: true);
    }

    private async void SignOutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LogoutGoogleAccountAsync();
    }

    private async void AccountBindButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartGoogleOAuthLoginAsync(switchingAccount: false);
    }

    private void OAuthCodeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingUi) return;
        ViewModel.OauthCodeText = OAuthCodeBox.Text;
    }

    private async void OAuthConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SubmitGoogleOAuthCodeAsync();
    }

    private void OAuthCancelButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowOAuthCodeInput = false;
        ViewModel.OauthCodeText = string.Empty;
    }

    private void GpuPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi) return;
        if (GpuPicker.SelectedItem is ComboBoxItem { Tag: string gpu })
            ViewModel.WakeGPUChoice = gpu;
    }

    private async void WakeButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.WakeColabHeadlessAsync();
    }

    private void StopWakeButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.StopActiveWakeProcess();
    }

    private void ServerUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingUi) return;
        ViewModel.ColabServerURL = ServerUrlTextBox.Text;
    }

    private void ServerUrlPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingUi) return;
        ViewModel.ColabServerURL = ServerUrlPasswordBox.Password;
    }

    private void MaskUrlToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MaskServerURL = !ViewModel.MaskServerURL;
    }

    private async void ConnectRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.CheckColabStatusAsync();
    }

    private void GuideHelpButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenOnboardingGuide(0);
    }

    private void AdminPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingUi) return;
        ViewModel.AdminPassword = AdminPasswordBox.Password;
    }

    private void CopyAdminPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var dp = new DataPackage();
        dp.SetText(ViewModel.AdminPassword ?? string.Empty);
        Clipboard.SetContent(dp);
        ViewModel.StatusMessage = L("已复制管理密码到剪贴板", "Copied management password to clipboard");
    }

    private void TelemetryModeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CombinedTelemetryChartMode = !ViewModel.CombinedTelemetryChartMode;
    }

    private async void IdleTimeoutPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi) return;
        if (IdleTimeoutPicker.SelectedItem is ComboBoxItem { Tag: string raw }
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mins))
        {
            await ViewModel.UpdateIdleTimeoutConfigAsync(mins);
        }
    }

    private void ReleaseAllButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowDisconnectConfirm = true;
    }

    private void AddCustomModelButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CustomModelErrorMessage = string.Empty;
        ViewModel.ShowAddCustomModelSheet = true;
    }

    private async void UnloadAllModelsButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.UnloadRemoteModelFromVRAMAsync("all");
    }

    private void OpenComfyCanvasButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenComfyUIInBrowser();
    }

    private async void StopComfyButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.StopRemoteComfyUIAsync();
    }

    private async void StartComfyButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartRemoteComfyUIAsync(forceRestart: false, autoOpenBrowser: true);
    }

    private void OpenHfRepoButton_Click(object sender, RoutedEventArgs e)
    {
        var repo = ViewModel.ActiveModelSpec.RepoId;
        if (!string.IsNullOrWhiteSpace(repo))
            LocalRuntimePaths.OpenUrl($"https://huggingface.co/{repo}");
    }

    private void ToggleFileListButton_Click(object sender, RoutedEventArgs e)
    {
        _showFileList = !_showFileList;
        if (App.MainAppWindow is not null)
            App.MainAppWindow.ShowFileListState = _showFileList;
        FileListChevron.Glyph = _showFileList ? "\uE70D" : "\uE76C";
        FileListContainer.Visibility = _showFileList ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HfTokenPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingUi) return;
        ViewModel.HfToken = HfTokenPasswordBox.Password;
    }

    private async void HubContextPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi) return;
        if (HubContextPicker.SelectedItem is ComboBoxItem { Tag: string raw }
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int k))
        {
            ViewModel.ContextLengthK = Math.Clamp(k, 4, 256);
            await ViewModel.SyncContextLengthToColabAsync(announce: true);
        }
    }

    private async void DownloadWeightsButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartRemoteDownloadWeightsAsync();
    }

    private async void LoadVramButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadRemoteModelToVRAMAsync();
    }

    private async void UnloadActiveModelButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.UnloadRemoteModelFromVRAMAsync(ViewModel.SelectedModelId);
    }

    private void ComfyUnloadOthersToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncingUi) return;
        ViewModel.ComfyUnloadOthersOnStart = ComfyUnloadOthersToggle.IsOn;
    }

    private async void ComfySyncDriveButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SyncRemoteComfyUIToDriveAsync();
    }

    private async void ComfyFreeVramButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.FreeRemoteComfyUIVRAMAsync();
    }

    private void EnableThinkingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncingUi) return;
        ViewModel.EnableThinking = EnableThinkingToggle.IsOn;
    }

    private async void ContextLengthNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncingUi || double.IsNaN(args.NewValue)) return;
        int clamped = Math.Clamp((int)Math.Round(args.NewValue), 4, 256);
        if (ViewModel.ContextLengthK != clamped)
        {
            ViewModel.ContextLengthK = clamped;
            await ViewModel.SyncContextLengthToColabAsync(announce: true);
        }
    }

    private void ContextLengthSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingUi) return;
        int snapped = Math.Clamp((int)(Math.Round(e.NewValue / 4.0) * 4.0), 4, 256);
        if (ViewModel.ContextLengthK != snapped)
            ViewModel.ContextLengthK = snapped;
    }

    private async void ContextLengthSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        await ViewModel.SyncContextLengthToColabAsync(announce: true);
    }

    private void MaxNewTokensSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingUi) return;
        double snapped = Math.Clamp(Math.Round(e.NewValue / 512.0) * 512.0, 1024.0, 65536.0);
        if (Math.Abs(ViewModel.MaxNewTokens - snapped) > 0.5)
            ViewModel.MaxNewTokens = snapped;
    }

    private void TemperatureSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingUi) return;
        double rounded = Math.Clamp(Math.Round(e.NewValue, 2), 0.1, 1.5);
        if (Math.Abs(ViewModel.Temperature - rounded) > 0.001)
            ViewModel.Temperature = rounded;
    }

    private void ClearChatHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ChatHistory.Clear();
    }

    private void ImageResolutionPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi) return;
        if (ImageResolutionPicker.SelectedItem is ComboBoxItem { Tag: ResolutionOption opt })
            ViewModel.SelectedImageResolution = opt;
    }

    private void ImageStepsSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingUi) return;
        double snapped = Math.Clamp(Math.Round(e.NewValue), 4.0, 50.0);
        if (Math.Abs(ViewModel.InferenceSteps - snapped) > 0.01)
            ViewModel.InferenceSteps = snapped;
    }

    private void DenoisingStrengthSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingUi) return;
        double rounded = Math.Clamp(Math.Round(e.NewValue, 2), 0.2, 1.0);
        if (Math.Abs(ViewModel.DenoisingStrength - rounded) > 0.001)
            ViewModel.DenoisingStrength = rounded;
    }

    private void VideoResolutionPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi) return;
        if (VideoResolutionPicker.SelectedItem is ComboBoxItem { Tag: ResolutionOption opt })
            ViewModel.SelectedVideoResolution = opt;
    }

    private void VideoFramesSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingUi) return;
        double snapped = Math.Clamp(Math.Round((e.NewValue - 5.0) / 17.0) * 17.0 + 5.0, 90.0, 362.0);
        if (Math.Abs(ViewModel.NumFrames - snapped) > 0.5)
            ViewModel.NumFrames = snapped;
    }

    private void VideoStepsSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingUi) return;
        double snapped = Math.Clamp(Math.Round(e.NewValue / 5.0) * 5.0, 10.0, 60.0);
        if (Math.Abs(ViewModel.InferenceSteps - snapped) > 0.5)
            ViewModel.InferenceSteps = snapped;
    }

    private void SeedTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingUi) return;
        if (long.TryParse(SeedTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seed))
            ViewModel.Seed = seed;
    }

    private void RandomizeSeedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingUi) return;
        ViewModel.RandomizeSeed = RandomizeSeedCheckBox.IsChecked == true;
    }

    // ── Helpers ──

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem { Tag: string t } && t == tag)
            {
                if (combo.SelectedIndex != i)
                    combo.SelectedIndex = i;
                return;
            }
        }
    }

    private static void SelectResolutionCombo(ComboBox combo, ResolutionOption target)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem { Tag: ResolutionOption opt } && opt.Id == target.Id)
            {
                if (combo.SelectedIndex != i)
                    combo.SelectedIndex = i;
                return;
            }
        }
        if (combo.Items.Count > 0 && combo.SelectedIndex < 0)
            combo.SelectedIndex = 0;
    }

    private Brush ResolveBrush(string key)
    {
        var color = StudioColors.ResolveThemeColor(
            key,
            ActualTheme,
            Windows.UI.Color.FromArgb(0xFF, 0x94, 0xA3, 0xB8));
        return new SolidColorBrush(color);
    }

    private static string MapSfSymbolToGlyph(string sfSymbol) => sfSymbol switch
    {
        "film.stack" or "film" => "\uE714",
        "camera.aperture" or "camera.metering.matrix" => "\uE722",
        "point.3.connected.trianglepath.dotted" => "\uE9D5",
        "brain" => "\uE950",
        "cpu" => "\uE950",
        "paintbrush.pointed" => "\uE790",
        "bolt.square" or "bolt" => "\uE945",
        "sparkles.rectangle.stack" => "\uE734",
        "shippingbox.fill" => "\uE7B8",
        _ => "\uE71D",
    };
}
