using System.ComponentModel;
using EasyColab.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Media.Core;
using WinColor = Windows.UI.Color;

namespace EasyColab.Views.Workspace;

public sealed partial class ComfyUIWorkspaceView : UserControl
{
    public StudioViewModel ViewModel => StudioViewModel.Shared;

    private static readonly (string Key, string Zh, string En)[] ModelSubfolders =
    [
        ("diffusion_models", "diffusion_models (DiT / 视频主干模型)", "diffusion_models (DiT / Video Backbones)"),
        ("checkpoints", "checkpoints (完整大模型)", "checkpoints (Full Checkpoints)"),
        ("loras", "loras (LoRA 风格/运镜微调)", "loras (Style & Camera LoRAs)"),
        ("vae", "vae (3D 视频 / 图像 VAE)", "vae (3D Video / Image VAE)"),
        ("text_encoders", "text_encoders (UMT5 / Qwen / CLIP 编码器)", "text_encoders (UMT5 / Qwen / CLIP)"),
        ("clip_vision", "clip_vision (首尾帧 / 图生视频视觉编码器)", "clip_vision (I2V / FLF2V Vision Encoders)"),
        ("controlnet", "controlnet (姿态 / 深度 / VACE 控制)", "controlnet (Pose / Depth / VACE)"),
        ("upscale_models", "upscale_models (高清超分放大)", "upscale_models (Super-Resolution Upscalers)"),
        ("audio_encoders", "audio_encoders (音频驱动 / 配音节点)", "audio_encoders (Audio-Driven / Foley Nodes)"),
        ("gguf", "gguf (量化视频/图像主干)", "gguf (Quantized Video / Image Backbones)"),
    ];

    private bool _isSubscribed;
    private bool _syncingSubfolder;
    private string? _loadedVideoPath;
    private string? _loadedImagePath;
    private MediaSource? _currentMediaSource;

    // Keyed row caches for zero-flicker in-place updates during 1.5s status polling
    private readonly Dictionary<string, PresetPackRowWidgets> _packRows = new();
    private string[] _packRowOrder = [];

    private readonly Dictionary<string, RecentOutputRowWidgets> _outputRows = new();
    private string[] _outputRowOrder = [];

    public ComfyUIWorkspaceView()
    {
        InitializeComponent();

        InitializeStaticTextsAndTooltips();
        PopulateSubfolderCombo();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        SyncAll();
    }

    private void InitializeStaticTextsAndTooltips()
    {
        // Strings containing single quotes / apostrophes are assigned here to avoid XAML markup extension escaping issues.
        CanvasCard.Subtitle = L(
            "预装 ComfyUI + ComfyUI-Manager + VideoHelperSuite，自动复用 EasyColab 的 G4 保活、显存/内存/磁盘折线监控与自动休眠",
            "Preinstalled with ComfyUI + ComfyUI-Manager + VideoHelperSuite, sharing EasyColab's G4 keep-alive, live VRAM/RAM/Disk telemetry & idle auto-sleep");

        ToolTipService.SetToolTip(RestartCanvasButton, L(
            "在 Manager 安装新自定义节点后可点击一键重启并自动持久化至云盘",
            "Restart ComfyUI after installing custom nodes in Manager and auto-sync to Google Drive"));
        ToolTipService.SetToolTip(FreeVramButton, L(
            "释放 ComfyUI 模型显存缓存",
            "Free ComfyUI VRAM cache"));
        ToolTipService.SetToolTip(StopCanvasButton, L(
            "同步云盘并停止 ComfyUI 进程",
            "Sync to Google Drive & stop ComfyUI process"));

        string step1 = L("点击下方「一键配齐模型与工作流」", "Click '1-Click Install Pack' below");
        string step2 = L("在画布最左侧点「工作流」选中文模板", "Open 'Workflows' sidebar & pick template");
        string step3 = L("上传 1~2 张图片点右上角「运行」出片", "Upload 1–2 images & click 'Run'");

        StepChip1Text.Text = step1;
        ToolTipService.SetToolTip(StepChip1, step1);
        StepChip2Text.Text = step2;
        ToolTipService.SetToolTip(StepChip2, step2);
        StepChip3Text.Text = step3;
        ToolTipService.SetToolTip(StepChip3, step3);
    }

    private void PopulateSubfolderCombo()
    {
        _syncingSubfolder = true;
        try
        {
            WeightSubfolderCombo.Items.Clear();
            foreach (var (_, zh, en) in ModelSubfolders)
            {
                WeightSubfolderCombo.Items.Add(L(zh, en));
            }
            SyncSubfolderSelection();
        }
        finally
        {
            _syncingSubfolder = false;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ViewModel.MediaSelected += OnViewModelMediaSelected;
            ActualThemeChanged += OnActualThemeChanged;
            _isSubscribed = true;
        }
        SyncAll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_isSubscribed)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.MediaSelected -= OnViewModelMediaSelected;
            ActualThemeChanged -= OnActualThemeChanged;
            _isSubscribed = false;
        }
        ReleaseVideoPlayer();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        SyncCanvasCard();
        SyncPresetPacksCard();
        SyncPersistenceCard();
        SyncRecentOutputsCard();
    }

    private void OnViewModelMediaSelected(object? sender, string? path)
    {
        SyncMediaPreview();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(StudioViewModel.IsConnected):
            case nameof(StudioViewModel.ComfyRunning):
            case nameof(StudioViewModel.ComfyStarting):
            case nameof(StudioViewModel.ComfyDownloadingWeight):
            case nameof(StudioViewModel.ComfyInstallingPackId):
            case nameof(StudioViewModel.ComfyProgress):
            case nameof(StudioViewModel.ComfyStatusMessage):
            case nameof(StudioViewModel.DisplayComfyStatusMessage):
            case nameof(StudioViewModel.ComfyPublicURL):
            case nameof(StudioViewModel.MaskServerURL):
            case nameof(StudioViewModel.ComfyQueueRunning):
            case nameof(StudioViewModel.ComfyQueuePending):
                SyncCanvasCard();
                SyncPresetPacksCard();
                SyncWeightDownloaderCard();
                SyncPersistenceCard();
                break;

            case nameof(StudioViewModel.ComfyPresetPacks):
                SyncPresetPacksCard();
                break;

            case nameof(StudioViewModel.ComfySyncingDrive):
            case nameof(StudioViewModel.ComfyModelsCount):
            case nameof(StudioViewModel.ComfyCustomNodesCount):
            case nameof(StudioViewModel.ComfyWorkflowsCount):
            case nameof(StudioViewModel.ComfyPersistRoot):
            case nameof(StudioViewModel.ComfyDrivePersisted):
            case nameof(StudioViewModel.ComfyPersistenceLabel):
            case nameof(StudioViewModel.DisplayComfyPersistenceLabel):
                SyncPersistenceCard();
                break;

            case nameof(StudioViewModel.ComfyWeightSubfolder):
                SyncSubfolderSelection();
                break;

            case nameof(StudioViewModel.ComfyRecentOutputs):
            case nameof(StudioViewModel.ComfyPullingOutputRelPath):
                SyncRecentOutputsCard();
                break;

            case nameof(StudioViewModel.CurrentMediaURL):
            case nameof(StudioViewModel.CurrentPreviewImage):
            case nameof(StudioViewModel.IsCurrentMediaImage):
            case nameof(StudioViewModel.IsCurrentMediaVideo):
            case nameof(StudioViewModel.MediaElapsedMap):
                SyncMediaPreview();
                break;
        }
    }

    private void SyncAll()
    {
        SyncCanvasCard();
        SyncPresetPacksCard();
        SyncPersistenceCard();
        SyncWeightDownloaderCard();
        SyncSubfolderSelection();
        SyncRecentOutputsCard();
        SyncMediaPreview();
    }

    // ── Card 1: Canvas Launch & Status ──

    private void SyncCanvasCard()
    {
        var vm = ViewModel;
        bool runningWithUrl = vm.ComfyRunning && !string.IsNullOrEmpty(vm.ComfyPublicURL);

        // Status capsule
        if (vm.ComfyRunning)
        {
            var green = ResolveThemeBrush("StudioSystemGreenBrush");
            CanvasStatusDot.Color = green;
            CanvasStatusDot.IsPulsing = vm.ComfyQueueRunning > 0;
            CanvasStatusLabel.Text = vm.ComfyQueueRunning > 0
                ? L($"队列渲染中 ({vm.ComfyQueueRunning} 运行 / {vm.ComfyQueuePending} 排队)",
                    $"Rendering ({vm.ComfyQueueRunning} active / {vm.ComfyQueuePending} queued)")
                : L("画布在线待命", "Canvas Ready");
            CanvasStatusLabel.Foreground = green;
        }
        else if (vm.ComfyStarting)
        {
            var orange = ResolveThemeBrush("StudioSystemOrangeBrush");
            CanvasStatusDot.Color = orange;
            CanvasStatusDot.IsPulsing = true;
            CanvasStatusLabel.Text = L("正在部署启动...", "Starting...");
            CanvasStatusLabel.Foreground = orange;
        }
        else
        {
            CanvasStatusDot.Color = ResolveThemeBrush("StudioSecondaryOpacity45Brush");
            CanvasStatusDot.IsPulsing = false;
            CanvasStatusLabel.Text = L("未启动", "Stopped");
            CanvasStatusLabel.Foreground = ResolveThemeBrush("StudioTextSecondaryBrush");
        }

        // Action bar toggle
        RunningActionsPanel.Visibility = runningWithUrl ? Visibility.Visible : Visibility.Collapsed;
        StartCanvasButton.Visibility = runningWithUrl ? Visibility.Collapsed : Visibility.Visible;

        if (runningWithUrl)
        {
            RestartCanvasButton.IsEnabled = !vm.ComfyStarting;
            FreeVramButton.IsEnabled = vm.IsConnected && vm.ComfyRunning;
            StopCanvasButton.IsEnabled = vm.IsConnected && vm.ComfyRunning;
        }
        else
        {
            StartCanvasButton.IsEnabled = vm.IsConnected && !vm.ComfyStarting;
            StartCanvasProgressRing.IsActive = vm.ComfyStarting;
            StartCanvasProgressRing.Visibility = vm.ComfyStarting ? Visibility.Visible : Visibility.Collapsed;
            StartCanvasIcon.Visibility = vm.ComfyStarting ? Visibility.Collapsed : Visibility.Visible;
            StartCanvasLabel.Text = vm.ComfyStarting
                ? L("正在部署云端 ComfyUI 并恢复 Google Drive 软链与节点...",
                    "Deploying Cloud ComfyUI & restoring Google Drive symlinks and custom nodes...")
                : (vm.IsConnected
                    ? L("一键拉起云端 ComfyUI 并在浏览器打开全屏画布", "Deploy & Open Fullscreen Cloud ComfyUI Canvas")
                    : L("请先在左侧唤醒或连接 G4 实例", "Wake or connect a G4 instance on the left first"));
        }

        // Progress bar
        bool showProgress = vm.ComfyStarting || vm.ComfyDownloadingWeight;
        ComfyTaskProgressBar.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        if (showProgress)
        {
            ComfyTaskProgressBar.Value = Math.Clamp(vm.ComfyProgress, 0.0, 1.0);
        }

        // Status strip
        CanvasStatusInfoIcon.Glyph = vm.ComfyRunning ? "\uE930" : "\uE946";
        CanvasStatusInfoIcon.Foreground = vm.ComfyRunning
            ? ResolveThemeBrush("StudioSystemGreenBrush")
            : ResolveThemeBrush("StudioSystemAccentBrush");
        CanvasStatusMessageText.Text = vm.DisplayComfyStatusMessage;

        if (runningWithUrl)
        {
            CanvasPublicUrlText.Text = vm.MaskedURLDisplay(vm.ComfyPublicURL);
            CanvasPublicUrlText.Visibility = Visibility.Visible;
        }
        else
        {
            CanvasPublicUrlText.Visibility = Visibility.Collapsed;
        }
    }

    // ── Card 2: Preset Workflow Packs ──

    private void SyncPresetPacksCard()
    {
        var vm = ViewModel;
        var packs = vm.ComfyPresetPacks ?? [];
        int readyCount = 0;
        for (int i = 0; i < packs.Count; i++)
        {
            if (packs[i].Ready) readyCount++;
        }

        PresetReadyCountText.Text = L($"已就绪 {readyCount} / {packs.Count} 套", $"Ready {readyCount} / {packs.Count}");
        PresetReadyCountText.Foreground = readyCount > 0
            ? ResolveThemeBrush("StudioSystemGreenBrush")
            : ResolveThemeBrush("StudioTextSecondaryBrush");

        string[] nextOrder = packs.Select(p => p.Id).ToArray();
        bool orderChanged = nextOrder.Length != _packRowOrder.Length || !nextOrder.SequenceEqual(_packRowOrder);

        if (orderChanged)
        {
            PresetPacksContainer.Children.Clear();
            _packRows.Clear();
            _packRowOrder = nextOrder;

            foreach (var pack in packs)
            {
                var widgets = BuildPresetPackRow(pack);
                _packRows[pack.Id] = widgets;
                PresetPacksContainer.Children.Add(widgets.Root);
            }
        }

        foreach (var pack in packs)
        {
            if (_packRows.TryGetValue(pack.Id, out var widgets))
            {
                UpdatePresetPackRow(widgets, pack);
            }
        }
    }

    private PresetPackRowWidgets BuildPresetPackRow(ComfyPresetPackItem initialPack)
    {
        var monoFont = Application.Current.Resources.TryGetValue("StudioMonoFont", out var mf) && mf is FontFamily ff
            ? ff
            : new FontFamily("Cascadia Mono, Consolas");

        var root = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
        };

        var outerStack = new StackPanel { Spacing = 9 };
        root.Child = outerStack;

        var topGrid = new Grid { ColumnSpacing = 12 };
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outerStack.Children.Add(topGrid);

        // Left icon tile (36x36)
        var iconBox = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(9),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var iconGlyph = new FontIcon
        {
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconBox.Child = iconGlyph;
        Grid.SetColumn(iconBox, 0);
        topGrid.Children.Add(iconBox);

        // Middle details
        var middleStack = new StackPanel { Spacing = 4 };
        Grid.SetColumn(middleStack, 1);
        topGrid.Children.Add(middleStack);

        var titleWrap = new SimpleWrapPanel { HorizontalSpacing = 7, VerticalSpacing = 4 };
        middleStack.Children.Add(titleWrap);

        var titleText = new TextBlock
        {
            FontSize = 13.5,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleWrap.Children.Add(titleText);

        var badgePill = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var badgeText = new TextBlock
        {
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
        };
        badgePill.Child = badgeText;
        titleWrap.Children.Add(badgePill);

        var readyPill = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var readyText = new TextBlock
        {
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Text = L("已配齐就绪", "Ready"),
        };
        readyPill.Child = readyText;
        titleWrap.Children.Add(readyPill);

        var scenarioText = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
        };
        middleStack.Children.Add(scenarioText);

        var metaWrap = new SimpleWrapPanel
        {
            HorizontalSpacing = 6,
            VerticalSpacing = 4,
            Margin = new Thickness(0, 1, 0, 0),
        };
        middleStack.Children.Add(metaWrap);

        var weightMetaStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var weightIcon = new FontIcon { FontSize = 10.5, Glyph = "\uEDA2", VerticalAlignment = VerticalAlignment.Center };
        var weightSummaryText = new TextBlock
        {
            FontFamily = monoFont,
            FontSize = 10.5,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
        };
        weightMetaStack.Children.Add(weightIcon);
        weightMetaStack.Children.Add(weightSummaryText);
        metaWrap.Children.Add(weightMetaStack);

        var dotSep = new TextBlock
        {
            Text = "·",
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        metaWrap.Children.Add(dotSep);

        // Right action column
        var actionHost = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
        };
        Grid.SetColumn(actionHost, 2);
        topGrid.Children.Add(actionHost);

        // Ready actions
        var readyActionsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        var rebuildBtn = new Button
        {
            Content = L("重写中文模板", "Rebuild Templates"),
            FontSize = 11.5,
            Padding = new Thickness(8, 4, 8, 4),
            MinHeight = 28,
            CornerRadius = new CornerRadius(6),
        };
        ToolTipService.SetToolTip(rebuildBtn, L(
            "重新生成该工作流包的标准中文傻瓜注释画布文件",
            "Regenerate annotated workflow JSON templates for this pack"));

        var openBtn = new Button
        {
            Padding = new Thickness(10, 5, 10, 5),
            MinHeight = 28,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(0),
        };
        var openContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        openContent.Children.Add(new FontIcon { FontSize = 11.5, Glyph = "\uE774", Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) });
        openContent.Children.Add(new TextBlock
        {
            Text = L("打开画布直接使用", "Open in Canvas"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        });
        openBtn.Content = openContent;

        readyActionsRow.Children.Add(rebuildBtn);
        readyActionsRow.Children.Add(openBtn);
        actionHost.Children.Add(readyActionsRow);

        // Install action
        var installBtn = new Button
        {
            Padding = new Thickness(11, 6, 11, 6),
            MinHeight = 30,
            CornerRadius = new CornerRadius(6),
        };
        if (Application.Current.Resources.TryGetValue("StudioPrimaryButtonStyle", out var accentStyle) && accentStyle is Style style)
        {
            installBtn.Style = style;
        }
        var installContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var installRing = new ProgressRing { Width = 13, Height = 13, IsActive = false, Visibility = Visibility.Collapsed };
        var installIcon = new FontIcon { FontSize = 12, Glyph = "\uE945" };
        var installLabel = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold };
        installContent.Children.Add(installRing);
        installContent.Children.Add(installIcon);
        installContent.Children.Add(installLabel);
        installBtn.Content = installContent;
        actionHost.Children.Add(installBtn);

        // Progress footer (shown when installing this pack)
        var progressFooter = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 1, Value = 0 };
        var progressStatusText = new TextBlock
        {
            FontSize = 11,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        progressFooter.Children.Add(progressBar);
        progressFooter.Children.Add(progressStatusText);
        outerStack.Children.Add(progressFooter);

        var widgets = new PresetPackRowWidgets
        {
            Pack = initialPack,
            Root = root,
            IconBox = iconBox,
            IconGlyph = iconGlyph,
            TitleText = titleText,
            BadgePill = badgePill,
            BadgeText = badgeText,
            ReadyPill = readyPill,
            ReadyText = readyText,
            ScenarioText = scenarioText,
            WeightIcon = weightIcon,
            WeightSummaryText = weightSummaryText,
            DotSeparator = dotSep,
            MetaWrap = metaWrap,
            ReadyActionsRow = readyActionsRow,
            RebuildButton = rebuildBtn,
            OpenButton = openBtn,
            InstallButton = installBtn,
            InstallRing = installRing,
            InstallIcon = installIcon,
            InstallLabel = installLabel,
            ProgressFooter = progressFooter,
            ProgressBar = progressBar,
            ProgressStatusText = progressStatusText,
        };

        rebuildBtn.Click += async (_, _) =>
        {
            await ViewModel.InstallComfyPresetPackAsync(widgets.Pack, openWhenReady: false);
        };
        openBtn.Click += async (_, _) =>
        {
            if (ViewModel.ComfyRunning && !string.IsNullOrEmpty(ViewModel.ComfyPublicURL))
            {
                ViewModel.OpenComfyUIInBrowser();
            }
            else
            {
                await ViewModel.StartRemoteComfyUIAsync(forceRestart: false, autoOpenBrowser: true);
            }
        };
        installBtn.Click += async (_, _) =>
        {
            await ViewModel.InstallComfyPresetPackAsync(widgets.Pack, openWhenReady: true);
        };

        return widgets;
    }

    private void UpdatePresetPackRow(PresetPackRowWidgets w, ComfyPresetPackItem pack)
    {
        w.Pack = pack;
        var vm = ViewModel;
        bool isInstallingThis = !string.IsNullOrEmpty(vm.ComfyInstallingPackId) && vm.ComfyInstallingPackId == pack.Id;
        bool isAnyBusy = vm.ComfyDownloadingWeight || !string.IsNullOrEmpty(vm.ComfyInstallingPackId) || vm.ComfyStarting;

        var accentColor = StudioColors.SystemAccent(ActualTheme);
        var greenColor = StudioColors.SystemGreen(ActualTheme);
        var accentBrush = ResolveThemeBrush("StudioSystemAccentBrush");
        var greenBrush = ResolveThemeBrush("StudioSystemGreenBrush");
        var primaryBrush = ResolveThemeBrush("StudioTextPrimaryBrush");
        var secondaryBrush = ResolveThemeBrush("StudioTextSecondaryBrush");
        var inputFillBrush = ResolveThemeBrush("StudioInputFillBrush");
        var cardBorderBrush = ResolveThemeBrush("StudioCardBorderBrush");
        var pillNeutralBrush = ResolveThemeBrush("StudioPrimaryOpacity06Brush");

        // Card fill & border
        w.Root.Background = inputFillBrush;
        w.Root.BorderBrush = pack.Ready
            ? new SolidColorBrush(StudioColors.WithOpacity(greenColor, 0.45))
            : cardBorderBrush;
        w.Root.BorderThickness = new Thickness(pack.Ready ? 1.1 : 0.7);

        // Left icon
        w.IconBox.Background = pack.Ready
            ? new SolidColorBrush(StudioColors.WithOpacity(greenColor, 0.14))
            : new SolidColorBrush(StudioColors.WithOpacity(accentColor, 0.12));
        w.IconGlyph.Glyph = pack.Ready
            ? "\uE930"
            : (pack.Id.Contains("minimax", StringComparison.OrdinalIgnoreCase) || pack.Id.Contains("ltx", StringComparison.OrdinalIgnoreCase)
                ? "\uE8D6"
                : "\uE8B9");
        w.IconGlyph.Foreground = pack.Ready ? greenBrush : accentBrush;

        // Title & badges
        w.TitleText.Text = pack.LocalizedTitle;
        w.TitleText.Foreground = primaryBrush;

        w.BadgeText.Text = pack.LocalizedBadge;
        w.BadgeText.Foreground = accentBrush;
        w.BadgePill.Background = new SolidColorBrush(StudioColors.WithOpacity(accentColor, 0.13));

        w.ReadyPill.Visibility = pack.Ready ? Visibility.Visible : Visibility.Collapsed;
        w.ReadyPill.Background = new SolidColorBrush(StudioColors.WithOpacity(greenColor, 0.13));
        w.ReadyText.Foreground = greenBrush;

        // Scenario & weight summary
        w.ScenarioText.Text = pack.LocalizedScenario;
        w.ScenarioText.Foreground = secondaryBrush;

        w.WeightIcon.Foreground = secondaryBrush;
        w.WeightSummaryText.Foreground = secondaryBrush;
        w.WeightSummaryText.Text = L(
            $"权重 {pack.InstalledModels}/{pack.TotalModels} · 约 {WidgetFormat.Fmt1(pack.ApproxSizeGb)} GB",
            $"Weights {pack.InstalledModels}/{pack.TotalModels} · ~{WidgetFormat.Fmt1(pack.ApproxSizeGb)} GB");
        w.DotSeparator.Foreground = secondaryBrush;

        // Workflow pills (rebuild only when workflow list changes)
        var workflows = pack.Workflows ?? [];
        string wfKey = string.Join("|", workflows);
        if (w.WorkflowKey != wfKey)
        {
            w.WorkflowKey = wfKey;
            while (w.MetaWrap.Children.Count > 2)
            {
                w.MetaWrap.Children.RemoveAt(w.MetaWrap.Children.Count - 1);
            }
            w.WorkflowPillBorders.Clear();
            w.WorkflowPillTexts.Clear();

            foreach (var wfName in workflows)
            {
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var txt = new TextBlock
                {
                    Text = ComfyPresetPackItem.LocalizedWorkflowLabel(wfName),
                    FontSize = 10.5,
                    FontWeight = FontWeights.Medium,
                    MaxLines = 1,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Opacity = 0.85,
                };
                pill.Child = txt;
                w.MetaWrap.Children.Add(pill);
                w.WorkflowPillBorders.Add(pill);
                w.WorkflowPillTexts.Add(txt);
            }
        }
        foreach (var pill in w.WorkflowPillBorders) pill.Background = pillNeutralBrush;
        foreach (var txt in w.WorkflowPillTexts) txt.Foreground = primaryBrush;

        // Action buttons
        w.ReadyActionsRow.Visibility = pack.Ready ? Visibility.Visible : Visibility.Collapsed;
        w.InstallButton.Visibility = pack.Ready ? Visibility.Collapsed : Visibility.Visible;

        if (pack.Ready)
        {
            w.RebuildButton.IsEnabled = vm.IsConnected && !isAnyBusy;
            w.OpenButton.IsEnabled = vm.IsConnected && !isAnyBusy;
            w.OpenButton.Background = greenBrush;
        }
        else
        {
            w.InstallButton.IsEnabled = vm.IsConnected && !isAnyBusy;
            w.InstallRing.IsActive = isInstallingThis;
            w.InstallRing.Visibility = isInstallingThis ? Visibility.Visible : Visibility.Collapsed;
            w.InstallIcon.Visibility = isInstallingThis ? Visibility.Collapsed : Visibility.Visible;
            int pct = (int)Math.Round(vm.ComfyProgress * 100.0);
            w.InstallLabel.Text = isInstallingThis
                ? L($"正在极速配齐 ({pct}%)...", $"Installing ({pct}%)...")
                : L("一键配齐模型与工作流", "1-Click Install Pack");
        }

        // Installing progress footer
        w.ProgressFooter.Visibility = isInstallingThis ? Visibility.Visible : Visibility.Collapsed;
        if (isInstallingThis)
        {
            w.ProgressBar.Value = Math.Clamp(vm.ComfyProgress, 0.0, 1.0);
            w.ProgressStatusText.Text = vm.DisplayComfyStatusMessage;
            w.ProgressStatusText.Foreground = accentBrush;
        }
    }

    // ── Card 3: Persistence Matrix ──

    private void SyncPersistenceCard()
    {
        var vm = ViewModel;

        SyncDriveButton.IsEnabled = vm.IsConnected && !vm.ComfySyncingDrive;
        SyncDriveProgressRing.IsActive = vm.ComfySyncingDrive;
        SyncDriveProgressRing.Visibility = vm.ComfySyncingDrive ? Visibility.Visible : Visibility.Collapsed;
        SyncDriveIcon.Visibility = vm.ComfySyncingDrive ? Visibility.Collapsed : Visibility.Visible;
        SyncDriveLabel.Text = vm.ComfySyncingDrive
            ? L("正在同步...", "Syncing...")
            : L("立即同步至云盘", "Sync to Drive Now");

        ModelsCountValueText.Text = L($"{vm.ComfyModelsCount} 个权重文件", $"{vm.ComfyModelsCount} weight files");
        CustomNodesCountValueText.Text = L($"{vm.ComfyCustomNodesCount} 个节点插件", $"{vm.ComfyCustomNodesCount} custom nodes");
        WorkflowsCountValueText.Text = L($"{vm.ComfyWorkflowsCount} 份工作流", $"{vm.ComfyWorkflowsCount} workflows");

        PersistRootText.Text = vm.ComfyPersistRoot ?? string.Empty;
        PersistenceLabelText.Text = vm.DisplayComfyPersistenceLabel;
        PersistenceLabelText.Foreground = vm.ComfyDrivePersisted
            ? ResolveThemeBrush("StudioSystemGreenBrush")
            : ResolveThemeBrush("StudioTextSecondaryBrush");
    }

    // ── Card 4: 8-Thread Weight Downloader ──

    private void SyncSubfolderSelection()
    {
        if (_syncingSubfolder) return;
        _syncingSubfolder = true;
        try
        {
            string target = ViewModel.ComfyWeightSubfolder ?? "diffusion_models";
            int idx = Array.FindIndex(ModelSubfolders, item => string.Equals(item.Key, target, StringComparison.Ordinal));
            WeightSubfolderCombo.SelectedIndex = idx >= 0 ? idx : 0;
        }
        finally
        {
            _syncingSubfolder = false;
        }
    }

    private void SyncWeightDownloaderCard()
    {
        var vm = ViewModel;
        DownloadWeightButton.IsEnabled = vm.IsConnected && !vm.ComfyDownloadingWeight;
        DownloadWeightProgressRing.IsActive = vm.ComfyDownloadingWeight;
        DownloadWeightProgressRing.Visibility = vm.ComfyDownloadingWeight ? Visibility.Visible : Visibility.Collapsed;
        DownloadWeightIcon.Visibility = vm.ComfyDownloadingWeight ? Visibility.Collapsed : Visibility.Visible;
        DownloadWeightLabel.Text = vm.ComfyDownloadingWeight
            ? L("极速下载中...", "Downloading...")
            : L("8 线程下载至云盘", "8-Thread Download to Drive");
    }

    // ── Card 5 Right: Recent Outputs List ──

    private void SyncRecentOutputsCard()
    {
        var vm = ViewModel;
        var outputs = vm.ComfyRecentOutputs ?? [];

        bool hasOutputs = outputs.Count > 0;
        RecentOutputsEmptyState.Visibility = hasOutputs ? Visibility.Collapsed : Visibility.Visible;
        RecentOutputsScrollViewer.Visibility = hasOutputs ? Visibility.Visible : Visibility.Collapsed;

        if (!hasOutputs)
        {
            if (_outputRowOrder.Length > 0)
            {
                RecentOutputsContainer.Children.Clear();
                _outputRows.Clear();
                _outputRowOrder = [];
            }
            return;
        }

        string[] nextOrder = outputs.Select(o => o.RelPath).ToArray();
        bool orderChanged = nextOrder.Length != _outputRowOrder.Length || !nextOrder.SequenceEqual(_outputRowOrder);
        if (orderChanged)
        {
            RecentOutputsContainer.Children.Clear();
            _outputRows.Clear();
            _outputRowOrder = nextOrder;

            foreach (var item in outputs)
            {
                var row = BuildRecentOutputRow(item);
                _outputRows[item.RelPath] = row;
                RecentOutputsContainer.Children.Add(row.Root);
            }
        }

        foreach (var item in outputs)
        {
            if (_outputRows.TryGetValue(item.RelPath, out var row))
            {
                UpdateRecentOutputRow(row, item);
            }
        }
    }

    private RecentOutputRowWidgets BuildRecentOutputRow(ComfyOutputMediaItem initialItem)
    {
        var root = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 6, 8, 6),
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Child = grid;

        var typeIcon = new FontIcon
        {
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(typeIcon, 0);
        grid.Children.Add(typeIcon);

        var infoStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var nameText = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = FontWeights.Medium,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var metaText = new TextBlock
        {
            FontSize = 10,
        };
        infoStack.Children.Add(nameText);
        infoStack.Children.Add(metaText);
        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        var pullBtn = new Button
        {
            Padding = new Thickness(7, 4, 7, 4),
            MinHeight = 26,
            MinWidth = 30,
            CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(pullBtn, L(
            "拉取到本机目录并在左侧播放器预览",
            "Pull to local PC folder & preview in player"));

        var btnHost = new Grid();
        var pullRing = new ProgressRing { Width = 12, Height = 12, IsActive = false, Visibility = Visibility.Collapsed };
        var pullIcon = new FontIcon { FontSize = 12, Glyph = "\uE896" };
        btnHost.Children.Add(pullRing);
        btnHost.Children.Add(pullIcon);
        pullBtn.Content = btnHost;
        Grid.SetColumn(pullBtn, 2);
        grid.Children.Add(pullBtn);

        var widgets = new RecentOutputRowWidgets
        {
            Item = initialItem,
            Root = root,
            TypeIcon = typeIcon,
            FilenameText = nameText,
            MetaText = metaText,
            PullButton = pullBtn,
            PullRing = pullRing,
            PullIcon = pullIcon,
        };

        pullBtn.Click += async (_, _) =>
        {
            await ViewModel.DownloadComfyOutputToMacAsync(widgets.Item);
        };

        return widgets;
    }

    private void UpdateRecentOutputRow(RecentOutputRowWidgets w, ComfyOutputMediaItem item)
    {
        w.Item = item;
        var vm = ViewModel;
        bool isVideo = string.Equals(item.MediaType, "video", StringComparison.OrdinalIgnoreCase);
        bool isPullingThis = vm.ComfyPullingOutputRelPath == item.RelPath;

        w.Root.Background = ResolveThemeBrush("StudioInputFillBrush");
        w.TypeIcon.Glyph = isVideo ? "\uE714" : "\uEB9F";
        w.TypeIcon.Foreground = ResolveThemeBrush("StudioSystemAccentBrush");

        w.FilenameText.Text = item.Filename;
        w.FilenameText.Foreground = ResolveThemeBrush("StudioTextPrimaryBrush");
        ToolTipService.SetToolTip(w.FilenameText, item.Filename);

        w.MetaText.Text = $"{WidgetFormat.Fmt1(item.SizeMb ?? 0)} MB · {(isVideo ? L("视频", "Video") : L("图像", "Image"))}";
        w.MetaText.Foreground = ResolveThemeBrush("StudioTextSecondaryBrush");

        w.PullButton.IsEnabled = vm.ComfyPullingOutputRelPath is null;
        w.PullRing.IsActive = isPullingThis;
        w.PullRing.Visibility = isPullingThis ? Visibility.Visible : Visibility.Collapsed;
        w.PullIcon.Visibility = isPullingThis ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── Card 5 Left: Output Media Preview ──

    private void SyncMediaPreview()
    {
        var vm = ViewModel;
        string? url = vm.CurrentMediaURL;
        double? elapsed = vm.ElapsedForMedia(url);

        if (elapsed is { } el && el > 0)
        {
            PreviewElapsedText.Text = L($"生成耗时 {FormatDuration(el)}", $"Elapsed {FormatDuration(el)}");
            PreviewElapsedPill.Visibility = Visibility.Visible;
            PreviewCompactElapsedText.Text = FormatDuration(el, compact: true);
            PreviewCompactElapsedPill.Visibility = Visibility.Visible;
        }
        else
        {
            PreviewElapsedPill.Visibility = Visibility.Collapsed;
            PreviewCompactElapsedPill.Visibility = Visibility.Collapsed;
        }

        if (vm.IsCurrentMediaImage && !string.IsNullOrEmpty(vm.CurrentPreviewImage) && File.Exists(vm.CurrentPreviewImage))
        {
            ReleaseVideoPlayer();
            PreviewVideoPlayer.Visibility = Visibility.Collapsed;
            PreviewEmptyState.Visibility = Visibility.Collapsed;
            PreviewImageControl.Visibility = Visibility.Visible;

            if (_loadedImagePath != vm.CurrentPreviewImage)
            {
                _loadedImagePath = vm.CurrentPreviewImage;
                try
                {
                    PreviewImageControl.Source = new BitmapImage(new Uri(vm.CurrentPreviewImage));
                }
                catch
                {
                    PreviewImageControl.Source = null;
                }
            }
        }
        else if (vm.IsCurrentMediaVideo && !string.IsNullOrEmpty(url) && File.Exists(url))
        {
            PreviewImageControl.Visibility = Visibility.Collapsed;
            PreviewImageControl.Source = null;
            _loadedImagePath = null;
            PreviewEmptyState.Visibility = Visibility.Collapsed;
            PreviewVideoPlayer.Visibility = Visibility.Visible;

            if (_loadedVideoPath != url)
            {
                ReleaseVideoPlayer();
                _loadedVideoPath = url;
                try
                {
                    _currentMediaSource = MediaSource.CreateFromUri(new Uri(url));
                    PreviewVideoPlayer.Source = _currentMediaSource;
                }
                catch
                {
                    _loadedVideoPath = null;
                }
            }
        }
        else
        {
            ReleaseVideoPlayer();
            PreviewImageControl.Visibility = Visibility.Collapsed;
            PreviewImageControl.Source = null;
            _loadedImagePath = null;
            PreviewVideoPlayer.Visibility = Visibility.Collapsed;
            PreviewEmptyState.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrEmpty(url))
        {
            PreviewMediaBar.Visibility = Visibility.Visible;
            PreviewMediaTypeIcon.Glyph = vm.IsCurrentMediaImage ? "\uEB9F" : "\uE714";
            string fileName = Path.GetFileName(url);
            PreviewFilenameText.Text = fileName;
            ToolTipService.SetToolTip(PreviewFilenameText, url);
            PreviewPrimaryActionButton.Content = vm.IsCurrentMediaVideo
                ? L("重播", "Replay")
                : L("打开", "Open");
        }
        else
        {
            PreviewMediaBar.Visibility = Visibility.Collapsed;
        }
    }

    private void ReleaseVideoPlayer()
    {
        try
        {
            PreviewVideoPlayer.MediaPlayer?.Pause();
            PreviewVideoPlayer.Source = null;
        }
        catch { }

        try
        {
            _currentMediaSource?.Dispose();
        }
        catch { }

        _currentMediaSource = null;
        _loadedVideoPath = null;
    }

    // ── Event Handlers ──

    private void OnOpenCanvasClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenComfyUIInBrowser();
    }

    private void OnCopyCanvasUrlClick(object sender, RoutedEventArgs e)
    {
        CopyToPasteboard(
            ViewModel.ComfyPublicURL,
            L("已复制 ComfyUI 公网画布地址到剪贴板", "Copied ComfyUI public canvas URL to clipboard"));
    }

    private async void OnRestartCanvasClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartRemoteComfyUIAsync(forceRestart: true, autoOpenBrowser: false);
    }

    private async void OnFreeVramClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.FreeRemoteComfyUIVRAMAsync();
    }

    private async void OnStopCanvasClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.StopRemoteComfyUIAsync();
    }

    private async void OnStartCanvasClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartRemoteComfyUIAsync(forceRestart: false, autoOpenBrowser: true);
    }

    private async void OnSyncDriveClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.SyncRemoteComfyUIToDriveAsync();
    }

    private void OnWeightSubfolderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSubfolder) return;
        int idx = WeightSubfolderCombo.SelectedIndex;
        if (idx >= 0 && idx < ModelSubfolders.Length)
        {
            ViewModel.ComfyWeightSubfolder = ModelSubfolders[idx].Key;
        }
    }

    private async void OnDownloadWeightClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartComfyWeightDownloadAsync();
    }

    private void OnPreviewPrimaryActionClick(object sender, RoutedEventArgs e)
    {
        var url = ViewModel.CurrentMediaURL;
        if (string.IsNullOrEmpty(url)) return;

        if (ViewModel.IsCurrentMediaVideo)
        {
            try
            {
                if (PreviewVideoPlayer.MediaPlayer is { } player)
                {
                    player.PlaybackSession.Position = TimeSpan.Zero;
                    player.Play();
                }
            }
            catch { }
        }
        else
        {
            LocalRuntimePaths.OpenInExplorer(url, reveal: false);
        }
    }

    private void OnRevealMediaInExplorerClick(object sender, RoutedEventArgs e)
    {
        var url = ViewModel.CurrentMediaURL;
        if (string.IsNullOrEmpty(url)) return;
        LocalRuntimePaths.OpenInExplorer(url, reveal: true);
    }

    // ── Helpers ──

    private void CopyToPasteboard(string text, string tip)
    {
        try
        {
            var pkg = new DataPackage();
            pkg.SetText(text ?? string.Empty);
            Clipboard.SetContent(pkg);
            Clipboard.Flush();
        }
        catch { }
        ViewModel.StatusMessage = tip;
    }

    private SolidColorBrush ResolveThemeBrush(string key)
    {
        var color = StudioColors.ResolveThemeColor(key, ActualTheme, WinColor.FromArgb(0xFF, 0x94, 0xA3, 0xB8));
        return new SolidColorBrush(color);
    }

    // ── Nested Row Holders & Lightweight WrapPanel ──

    private sealed class PresetPackRowWidgets
    {
        public required ComfyPresetPackItem Pack { get; set; }
        public required Border Root { get; init; }
        public required Border IconBox { get; init; }
        public required FontIcon IconGlyph { get; init; }
        public required TextBlock TitleText { get; init; }
        public required Border BadgePill { get; init; }
        public required TextBlock BadgeText { get; init; }
        public required Border ReadyPill { get; init; }
        public required TextBlock ReadyText { get; init; }
        public required TextBlock ScenarioText { get; init; }
        public required FontIcon WeightIcon { get; init; }
        public required TextBlock WeightSummaryText { get; init; }
        public required TextBlock DotSeparator { get; init; }
        public required SimpleWrapPanel MetaWrap { get; init; }
        public string WorkflowKey { get; set; } = string.Empty;
        public List<Border> WorkflowPillBorders { get; } = [];
        public List<TextBlock> WorkflowPillTexts { get; } = [];
        public required StackPanel ReadyActionsRow { get; init; }
        public required Button RebuildButton { get; init; }
        public required Button OpenButton { get; init; }
        public required Button InstallButton { get; init; }
        public required ProgressRing InstallRing { get; init; }
        public required FontIcon InstallIcon { get; init; }
        public required TextBlock InstallLabel { get; init; }
        public required StackPanel ProgressFooter { get; init; }
        public required ProgressBar ProgressBar { get; init; }
        public required TextBlock ProgressStatusText { get; init; }
    }

    private sealed class RecentOutputRowWidgets
    {
        public required ComfyOutputMediaItem Item { get; set; }
        public required Border Root { get; init; }
        public required FontIcon TypeIcon { get; init; }
        public required TextBlock FilenameText { get; init; }
        public required TextBlock MetaText { get; init; }
        public required Button PullButton { get; init; }
        public required ProgressRing PullRing { get; init; }
        public required FontIcon PullIcon { get; init; }
    }

    /// <summary>Minimal horizontal wrapping panel for workflow pills and title badges.</summary>
    private sealed class SimpleWrapPanel : Panel
    {
        public double HorizontalSpacing { get; set; } = 6;
        public double VerticalSpacing { get; set; } = 4;

        protected override Size MeasureOverride(Size availableSize)
        {
            double maxWidth = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
            double x = 0, y = 0, rowHeight = 0, totalWidth = 0;

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                child.Measure(new Size(maxWidth, double.PositiveInfinity));
                var desired = child.DesiredSize;

                if (x > 0 && x + desired.Width > maxWidth)
                {
                    totalWidth = Math.Max(totalWidth, x - HorizontalSpacing);
                    x = 0;
                    y += rowHeight + VerticalSpacing;
                    rowHeight = 0;
                }

                x += desired.Width + HorizontalSpacing;
                rowHeight = Math.Max(rowHeight, desired.Height);
            }

            if (x > 0) totalWidth = Math.Max(totalWidth, x - HorizontalSpacing);
            return new Size(Math.Min(totalWidth, maxWidth), y + rowHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double maxWidth = finalSize.Width;
            double x = 0, y = 0, rowHeight = 0;

            var rowItems = new List<(UIElement Element, double X, double Width, double Height)>();

            void FlushRow()
            {
                foreach (var (el, ix, iw, ih) in rowItems)
                {
                    double iy = y + Math.Max(0, (rowHeight - ih) / 2.0);
                    el.Arrange(new Rect(ix, iy, iw, ih));
                }
                rowItems.Clear();
            }

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                var desired = child.DesiredSize;

                if (x > 0 && x + desired.Width > maxWidth)
                {
                    FlushRow();
                    x = 0;
                    y += rowHeight + VerticalSpacing;
                    rowHeight = 0;
                }

                rowItems.Add((child, x, Math.Min(desired.Width, maxWidth), desired.Height));
                x += desired.Width + HorizontalSpacing;
                rowHeight = Math.Max(rowHeight, desired.Height);
            }

            FlushRow();
            return new Size(finalSize.Width, y + rowHeight);
        }
    }
}
