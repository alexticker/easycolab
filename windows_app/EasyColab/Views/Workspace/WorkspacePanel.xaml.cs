using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace EasyColab.Views.Workspace;

/// <summary>
/// Port of the central workspace panel (<c>workspacePanel</c>, <c>modeSwitcherHeader</c>,
/// <c>referenceImageSection</c>, <c>promptComposerSection</c>, <c>generateButtonTitle</c>,
/// <c>generateActionSection</c>) from macOS <c>MainStudioView+Workspace.swift</c>.
/// Embeds <see cref="ComfyUIWorkspaceView"/>, <see cref="ApiGatewayView"/>,
/// <see cref="LlmConversationCard"/>, <see cref="MediaPreviewCard"/>, and <see cref="MediaHistoryCard"/>.
/// </summary>
public sealed partial class WorkspacePanel : UserControl
{
    public StudioViewModel ViewModel => StudioViewModel.Shared;

    private readonly List<ModeCardVisual> _modeCards = new();
    private string? _builtModesModelId;
    private bool _showNegativePrompt;
    private bool _subscribed;

    public WorkspacePanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
        SyncAllSections();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            _subscribed = false;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateModeCardSelectionStyles();
        UpdateVramStatusPill();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(StudioViewModel.SelectedModelId):
            case nameof(StudioViewModel.ActiveModelSpec):
            case nameof(StudioViewModel.CustomModels):
                SyncAllSections();
                break;

            case nameof(StudioViewModel.TaskModeId):
                UpdateModeCardSelectionStyles();
                SyncActiveWorkspaceBranch();
                SyncReferenceImageSection();
                SyncPromptComposerSection();
                SyncGenerateActionSection();
                break;

            case nameof(StudioViewModel.LoadedModelId):
            case nameof(StudioViewModel.LoadedModelIds):
            case nameof(StudioViewModel.ModelLoaded):
            case nameof(StudioViewModel.IsSelectedModelLoadedInVRAM):
                UpdateVramStatusPill();
                SyncGenerateActionSection();
                break;

            case nameof(StudioViewModel.IsGenerating):
            case nameof(StudioViewModel.ProgressValue):
            case nameof(StudioViewModel.CurrentJobElapsed):
            case nameof(StudioViewModel.BatchCount):
                SyncGenerateActionSection();
                break;
        }
    }

    private void SyncAllSections()
    {
        SyncModeSwitcherHeader();
        SyncActiveWorkspaceBranch();
        SyncReferenceImageSection();
        SyncPromptComposerSection();
        SyncGenerateActionSection();
    }

    // ── 1. Mode Switcher Header ──

    private void SyncModeSwitcherHeader()
    {
        var spec = ViewModel.ActiveModelSpec;
        ModelHeaderIcon.Glyph = WorkspaceIconMap.GlyphFor(spec.Icon);
        ModelHeaderNameText.Text = spec.Name;
        ModelHeaderRepoText.Text = spec.RepoId;
        ModelHeaderSummaryText.Text = spec.Summary;

        UpdateVramStatusPill();

        if (!string.Equals(_builtModesModelId, spec.Id, StringComparison.Ordinal) || _modeCards.Count != spec.Modes.Count)
        {
            RebuildModeCards(spec);
        }
        UpdateModeCardSelectionStyles();
    }

    private void UpdateVramStatusPill()
    {
        var theme = ActualTheme;
        int loadedCount = ViewModel.LoadedModelIds.Count;
        if (loadedCount > 1)
        {
            CoResidentPill.Visibility = Visibility.Visible;
            CoResidentText.Text = L($"{loadedCount} 模型显存共驻", $"{loadedCount} Models in VRAM");
        }
        else
        {
            CoResidentPill.Visibility = Visibility.Collapsed;
        }

        bool loaded = ViewModel.IsSelectedModelLoadedInVRAM;
        var greenBrush = new SolidColorBrush(EasyColab.Controls.StudioColors.SystemGreen(theme));
        var secondaryBrush = new SolidColorBrush(EasyColab.Controls.StudioColors.ResolveThemeColor(
            "StudioTextSecondaryBrush", theme, Windows.UI.Color.FromArgb(0xFF, 0x94, 0xA3, 0xB8)));
        var idleDotBrush = new SolidColorBrush(EasyColab.Controls.StudioColors.ResolveThemeColor(
            "StudioSecondaryOpacity50Brush", theme, Windows.UI.Color.FromArgb(0x46, 0xFF, 0xFF, 0xFF)));

        VramStatusDot.Color = loaded ? greenBrush : idleDotBrush;
        VramStatusText.Text = loaded
            ? L("显存已就绪", "Ready in VRAM")
            : L("待加载至显存", "Not Loaded in VRAM");
        VramStatusText.Foreground = loaded ? greenBrush : secondaryBrush;
    }

    private void RebuildModeCards(OpenModelSpec spec)
    {
        _builtModesModelId = spec.Id;
        _modeCards.Clear();
        ModeCardsGrid.Children.Clear();
        ModeCardsGrid.ColumnDefinitions.Clear();

        var plainStyle = Application.Current.Resources.TryGetValue("StudioPlainButtonStyle", out var ps) && ps is Style s
            ? s
            : null;

        for (int i = 0; i < spec.Modes.Count; i++)
        {
            var mode = spec.Modes[i];
            ModeCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var innerGrid = new Grid { ColumnSpacing = 12 };
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconGlyph = new FontIcon
            {
                Glyph = WorkspaceIconMap.GlyphFor(mode.Icon),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var iconBox = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Center,
                Child = iconGlyph,
            };
            innerGrid.Children.Add(iconBox);

            var textCol = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var titleText = new TextBlock
            {
                Text = mode.Title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var activeBadgeText = new TextBlock
            {
                Text = L("当前模式", "Active"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
            };
            var activeBadge = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 1.5, 6, 1.5),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Child = activeBadgeText,
            };
            titleRow.Children.Add(titleText);
            titleRow.Children.Add(activeBadge);

            var subtitleText = new TextBlock
            {
                Text = mode.Subtitle,
                FontSize = 11.5,
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ToolTipService.SetToolTip(subtitleText, mode.Subtitle);

            textCol.Children.Add(titleRow);
            textCol.Children.Add(subtitleText);
            Grid.SetColumn(textCol, 1);
            innerGrid.Children.Add(textCol);

            var radioIcon = new FontIcon
            {
                Glyph = "\uECCA",
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(radioIcon, 2);
            innerGrid.Children.Add(radioIcon);

            var cardBorder = new Border
            {
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(10),
                Child = innerGrid,
            };

            string modeId = mode.Id;
            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = cardBorder,
            };
            if (plainStyle is not null) btn.Style = plainStyle;
            btn.Click += (_, _) =>
            {
                ViewModel.RecordLocalInteraction();
                ViewModel.TaskModeId = modeId;
            };

            Grid.SetColumn(btn, i);
            ModeCardsGrid.Children.Add(btn);
            _modeCards.Add(new ModeCardVisual(
                modeId, cardBorder, iconBox, iconGlyph, titleText, activeBadge, activeBadgeText, subtitleText, radioIcon));
        }
    }

    private void UpdateModeCardSelectionStyles()
    {
        var theme = ActualTheme;
        bool isDark = theme == ElementTheme.Dark;
        var accent = EasyColab.Controls.StudioColors.SystemAccent(theme);
        var accentBrush = new SolidColorBrush(accent);
        var accentSelectedFill = new SolidColorBrush(EasyColab.Controls.StudioColors.WithOpacity(accent, isDark ? 0.16 : 0.07));
        var accentBadgeFill = new SolidColorBrush(EasyColab.Controls.StudioColors.WithOpacity(accent, 0.14));
        var whiteBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

        var inputFillBrush = new SolidColorBrush(EasyColab.Controls.StudioColors.ResolveThemeColor(
            "StudioInputFillBrush", theme, Windows.UI.Color.FromArgb(0xFF, 0x17, 0x19, 0x1E)));
        var borderBrush = new SolidColorBrush(EasyColab.Controls.StudioColors.ResolveThemeColor(
            "StudioCardBorderBrush", theme, Windows.UI.Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF)));
        var subtleFillBrush = new SolidColorBrush(EasyColab.Controls.StudioColors.ResolveThemeColor(
            "StudioPrimaryOpacity06Brush", theme, Windows.UI.Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)));
        var primaryTextBrush = new SolidColorBrush(EasyColab.Controls.StudioColors.ResolveThemeColor(
            "StudioTextPrimaryBrush", theme, Windows.UI.Color.FromArgb(0xFF, 0xF1, 0xF5, 0xF9)));
        var secondaryTextBrush = new SolidColorBrush(EasyColab.Controls.StudioColors.ResolveThemeColor(
            "StudioTextSecondaryBrush", theme, Windows.UI.Color.FromArgb(0xFF, 0x94, 0xA3, 0xB8)));
        var idleRadioBrush = new SolidColorBrush(EasyColab.Controls.StudioColors.ResolveThemeColor(
            "StudioSecondaryOpacity45Brush", theme, Windows.UI.Color.FromArgb(0x3F, 0xFF, 0xFF, 0xFF)));

        var activeModeId = ViewModel.TaskModeId;
        foreach (var card in _modeCards)
        {
            bool selected = string.Equals(card.ModeId, activeModeId, StringComparison.Ordinal);

            card.CardBorder.Background = selected ? accentSelectedFill : inputFillBrush;
            card.CardBorder.BorderBrush = selected
                ? new SolidColorBrush(EasyColab.Controls.StudioColors.WithOpacity(accent, isDark ? 0.65 : 0.55))
                : borderBrush;
            card.CardBorder.BorderThickness = new Thickness(selected ? 1.25 : 0.8);

            card.IconBox.Background = selected ? accentBrush : subtleFillBrush;
            card.IconGlyph.Foreground = selected ? whiteBrush : secondaryTextBrush;

            card.TitleText.Foreground = selected ? primaryTextBrush : secondaryTextBrush;
            card.ActiveBadge.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            card.ActiveBadge.Background = accentBadgeFill;
            card.ActiveBadgeText.Foreground = accentBrush;
            card.SubtitleText.Foreground = secondaryTextBrush;

            card.RadioIcon.Glyph = selected ? "\uECCB" : "\uECCA";
            card.RadioIcon.Foreground = selected ? accentBrush : idleRadioBrush;
        }
    }

    // ── Active Workspace Branch (ComfyUI / API Gateway / Standard Studio + LLM or Media) ──

    private void SyncActiveWorkspaceBranch()
    {
        var mod = ViewModel.ActiveModelSpec.Modality;
        bool isComfy = mod == ModelModality.ComfyUI;
        bool isApi = !isComfy && string.Equals(ViewModel.TaskModeId, "api", StringComparison.Ordinal);
        bool isStandard = !isComfy && !isApi;
        bool isLlm = isStandard && mod == ModelModality.LlmVLM;
        bool showMedia = isStandard && !isLlm;

        ComfyUIView.Visibility = isComfy ? Visibility.Visible : Visibility.Collapsed;
        ApiGatewayViewHost.Visibility = isApi ? Visibility.Visible : Visibility.Collapsed;
        StandardStudioPanel.Visibility = isStandard ? Visibility.Visible : Visibility.Collapsed;
        LlmConversationHost.Visibility = isLlm ? Visibility.Visible : Visibility.Collapsed;
        MediaSplitHost.Visibility = showMedia ? Visibility.Visible : Visibility.Collapsed;

        if (!showMedia)
        {
            MediaPreviewControl.PausePlayback();
        }
    }

    // ── 2. Reference Image Section (exact match to Swift lines 1114-1199) ──

    private void SyncReferenceImageSection()
    {
        switch (ViewModel.TaskModeId)
        {
            case "fl2va":
                ReferenceImageCard.Visibility = Visibility.Visible;
                ReferenceImageCard.Title = L("首尾关键帧参考图", "Start & End Keyframe Images");
                ReferenceImageCard.Subtitle = L(
                    "至少拖入首帧或尾帧之一，精准锁定起止画面构图",
                    "Drop at least a start frame or end frame to anchor camera motion & composition");
                ReferenceImageCard.Icon = "\uEB9F";
                FirstFrameWell.Title = L("起始首帧 (Index 0)", "Start Frame (Index 0)");
                FirstFrameWell.Subtitle = L("拖入图片或点击选择", "Drop an image or click to browse");
                LastFrameWell.Title = L("结束尾帧 (可选 · Index -1)", "End Frame (Optional · Index -1)");
                LastFrameWell.Subtitle = L("拖入图片或点击选择", "Drop an image or click to browse");
                SetReferenceWellColumns(showSecondWell: true);
                break;

            case "i2v":
                ReferenceImageCard.Visibility = Visibility.Visible;
                ReferenceImageCard.Title = L("起始首帧参考图", "Start Keyframe Image");
                ReferenceImageCard.Subtitle = L(
                    "模型将以该图片作为视频第 1 帧向后延展动态画面",
                    "The model animates forward starting from this image as frame 0");
                ReferenceImageCard.Icon = "\uE91B";
                FirstFrameWell.Title = L("首帧原图", "Start Frame Image");
                FirstFrameWell.Subtitle = L("拖入本地图片或点击浏览", "Drop a local image or click to browse");
                SetReferenceWellColumns(showSecondWell: false);
                break;

            case "i2i_edit":
                bool isDualRef = ViewModel.SelectedModelId == "qwen_image_21" || ViewModel.IsMiniMaxH3ImageModel;
                ReferenceImageCard.Visibility = Visibility.Visible;
                ReferenceImageCard.Title = isDualRef
                    ? L("指令修图参考原图", "Instruction Image-Editing References")
                    : L("图生图重绘参考底图", "Image-to-Image Reference");
                ReferenceImageCard.Subtitle = ViewModel.IsMiniMaxH3ImageModel
                    ? L(
                        "支持上传 1~2 张参考图（可在提示词中用 <Picture 1> / <Picture 2> 指代）进行精准修图或融合",
                        "Upload 1–2 reference images (refer via <Picture 1> / <Picture 2> in prompt) for editing or fusion")
                    : (ViewModel.SelectedModelId == "qwen_image_21"
                        ? L(
                            "支持上传 1~2 张参考图进行局部编辑、换背景或多主体融合",
                            "Upload 1–2 reference images for local edits, background swaps, or multi-subject fusion")
                        : L(
                            "上传参考原图并在左侧调节重绘幅度",
                            "Upload a reference image and tune Denoising Strength in the left sidebar"));
                ReferenceImageCard.Icon = "\uE70F";
                FirstFrameWell.Title = L("主参考图 1 (必选)", "Primary Reference 1 (Required)");
                FirstFrameWell.Subtitle = L("拖入待编辑原图", "Drop source image to edit");
                LastFrameWell.Title = L("融合参考图 2 (可选)", "Fusion Reference 2 (Optional)");
                LastFrameWell.Subtitle = L("用于多图主体融合编辑", "For multi-image subject fusion");
                SetReferenceWellColumns(showSecondWell: isDualRef);
                break;

            case "vlm":
                ReferenceImageCard.Visibility = Visibility.Visible;
                ReferenceImageCard.Title = L("待分析视觉图像", "Vision Reference Images");
                ReferenceImageCard.Subtitle = L(
                    $"支持拖入 1~2 张截图、摄影或分镜画稿交由 {ViewModel.ActiveModelSpec.Name} 解析",
                    $"Drop 1–2 screenshots, photos, or storyboard sketches for {ViewModel.ActiveModelSpec.Name} to analyze");
                ReferenceImageCard.Icon = "\uE7B3";
                FirstFrameWell.Title = L("输入图像 1", "Input Image 1");
                FirstFrameWell.Subtitle = L("拖入图片进行解析或反推提示词", "Drop an image to analyze or reverse-engineer prompts");
                LastFrameWell.Title = L("对比图像 2 (可选)", "Comparison Image 2 (Optional)");
                LastFrameWell.Subtitle = L("可同时传入第二张图像对比分析", "Optionally attach a second image for side-by-side comparison");
                SetReferenceWellColumns(showSecondWell: true);
                break;

            default:
                ReferenceImageCard.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void SetReferenceWellColumns(bool showSecondWell)
    {
        LastFrameWell.Visibility = showSecondWell ? Visibility.Visible : Visibility.Collapsed;
        SecondWellColumn.Width = showSecondWell ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ReferenceWellsGrid.ColumnSpacing = showSecondWell ? 12 : 0;
    }

    // ── 3. Prompt Composer Section (exact match to Swift lines 1201-1319) ──

    private void SyncPromptComposerSection()
    {
        var mod = ViewModel.ActiveModelSpec.Modality;
        bool isI2IEdit = string.Equals(ViewModel.TaskModeId, "i2i_edit", StringComparison.Ordinal);

        PromptComposerCard.Title = mod switch
        {
            ModelModality.LlmVLM => L("问题与指令输入", "Message & Instruction Input"),
            ModelModality.Image => isI2IEdit
                ? L("修图指令与画面描述", "Editing Instruction & Scene Prompt")
                : L("图像画面提示词", "Image Prompt"),
            _ => L("镜头与多模态提示词", "Camera & Multimodal Prompts"),
        };

        VisualPromptLabel.Text = mod == ModelModality.LlmVLM
            ? L("对话内容 / 视觉提问", "Chat Message / Vision Question")
            : (isI2IEdit
                ? L("编辑指令或目标画面描述", "Edit Instruction or Target Scene")
                : L("主体、构图、光影与风格描述", "Subject, Composition, Lighting & Style"));

        VisualPromptBox.PlaceholderText = mod switch
        {
            ModelModality.LlmVLM => L(
                "输入你想提问、推理编码或反推提示词的内容...",
                "Enter a question, coding task, or vision prompt..."),
            ModelModality.Image => isI2IEdit
                ? L(
                    "描述你想修改的细节（例如：把背景换成夕阳海滩，保留人物光影）...",
                    "Describe your edit (e.g. replace background with a sunset beach while preserving lighting)...")
                : L(
                    "描述主体、构图、镜头景别、光影色调与文字排版...",
                    "Describe subject, composition, lighting, lens style, and typography..."),
            _ => L(
                "描述画面主体运动、镜头推拉摇移、光影氛围与电影质感...",
                "Describe subject motion, camera movement, lighting, and cinematic style..."),
        };

        VisualPromptBox.Height = mod == ModelModality.LlmVLM ? 84 : 92;

        bool isVideoAudio = mod == ModelModality.VideoAudio;
        bool showDirectNegative = (mod == ModelModality.Image && !ViewModel.IsMiniMaxH3ImageModel) || mod == ModelModality.Video;

        AudioPromptsRow.Visibility = isVideoAudio ? Visibility.Visible : Visibility.Collapsed;
        VideoAudioNegativeSection.Visibility = isVideoAudio ? Visibility.Visible : Visibility.Collapsed;
        DirectNegativeSection.Visibility = showDirectNegative ? Visibility.Visible : Visibility.Collapsed;

        VideoAudioNegativeBody.Visibility = _showNegativePrompt ? Visibility.Visible : Visibility.Collapsed;
        NegativeChevronIcon.Glyph = _showNegativePrompt ? "\uE70D" : "\uE76C";
    }

    private void PresetTemplatesFlyout_Opening(object? sender, object e)
    {
        PresetTemplatesFlyout.Items.Clear();
        var mod = ViewModel.ActiveModelSpec.Modality;
        var matching = PRESET_TEMPLATES
            .Where(t => t.Category == mod || (mod == ModelModality.Video && t.Category == ModelModality.VideoAudio))
            .ToList();
        var items = matching.Count > 0 ? matching : PRESET_TEMPLATES.ToList();

        foreach (var tpl in items)
        {
            var captured = tpl;
            var menuItem = new MenuFlyoutItem { Text = captured.Name };
            menuItem.Click += (_, _) =>
            {
                ViewModel.RecordLocalInteraction();
                ViewModel.VisualPrompt = captured.Visual;
                if (!string.IsNullOrEmpty(captured.Soundscape))
                    ViewModel.SoundscapePrompt = captured.Soundscape;
                if (!string.IsNullOrEmpty(captured.Music))
                    ViewModel.MusicPrompt = captured.Music;
            };
            PresetTemplatesFlyout.Items.Add(menuItem);
        }
    }

    private void ClearPromptsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RecordLocalInteraction();
        ViewModel.VisualPrompt = string.Empty;
        if (ViewModel.ActiveModelSpec.Modality == ModelModality.VideoAudio)
        {
            ViewModel.SoundscapePrompt = string.Empty;
            ViewModel.MusicPrompt = string.Empty;
        }
        ViewModel.NegativePrompt = string.Empty;
    }

    private void ToggleNegativePromptButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RecordLocalInteraction();
        _showNegativePrompt = !_showNegativePrompt;
        VideoAudioNegativeBody.Visibility = _showNegativePrompt ? Visibility.Visible : Visibility.Collapsed;
        NegativeChevronIcon.Glyph = _showNegativePrompt ? "\uE70D" : "\uE76C";
    }

    private void PromptBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is Control c && c.FocusState != FocusState.Unfocused)
        {
            ViewModel.RecordLocalInteraction();
        }
    }

    private void PromptBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        if (!ctrlState.HasFlag(CoreVirtualKeyStates.Down)) return;

        e.Handled = true;
        if (!ViewModel.IsGenerating)
        {
            ViewModel.RecordLocalInteraction();
            _ = ViewModel.StartGenerationAsync();
        }
    }

    private void FrameWell_ImagePathChanged(EasyColab.Controls.FrameDropWell sender, string? args)
    {
        ViewModel.RecordLocalInteraction();
    }

    // ── 4. Generate Action Section (exact match to Swift lines 1321-1426) ──

    private string BuildGenerateButtonTitle()
    {
        var spec = ViewModel.ActiveModelSpec;
        var mod = spec.Modality;

        if (ViewModel.IsGenerating)
        {
            string liveTime = ViewModel.CurrentJobElapsed > 0.05
                ? L(
                    $" · 已耗时 {FormatDuration(ViewModel.CurrentJobElapsed, compact: true)}",
                    $" · {FormatDuration(ViewModel.CurrentJobElapsed, compact: true)}")
                : string.Empty;

            if (mod == ModelModality.LlmVLM)
            {
                return L(
                    $"{spec.Name} 正在推理输出{liveTime}...",
                    $"{spec.Name} generating{liveTime}...");
            }

            return ViewModel.BatchCount > 1
                ? L(
                    $"正在生成（共 {ViewModel.BatchCount} 项{liveTime}）...",
                    $"Generating ({ViewModel.BatchCount} items{liveTime})...")
                : L(
                    $"正在云端 GPU 生成{liveTime}...",
                    $"Generating on Cloud GPU{liveTime}...");
        }

        if (!ViewModel.IsSelectedModelLoadedInVRAM)
        {
            return L(
                $"开始运行（请先在左侧将 {spec.Name} 加载到显存）",
                $"Run (Load {spec.Name} to VRAM on the left first)");
        }

        return mod switch
        {
            ModelModality.LlmVLM => L(
                "发送并开始深度推理 (Ctrl + Enter)",
                "Send & Start Reasoning (Ctrl + Enter)"),
            ModelModality.Image => ViewModel.BatchCount > 1
                ? L($"生成 {ViewModel.BatchCount} 张高清图像", $"Generate {ViewModel.BatchCount} HD Images")
                : L("开始生成图像", "Generate Image"),
            ModelModality.VideoAudio => ViewModel.BatchCount > 1
                ? L($"连续生成 {ViewModel.BatchCount} 条全模态音视频", $"Generate {ViewModel.BatchCount} Videos with Audio")
                : L("开始生成音视频", "Generate Video + Audio"),
            _ => ViewModel.BatchCount > 1
                ? L($"连续生成 {ViewModel.BatchCount} 条高清视频", $"Generate {ViewModel.BatchCount} HD Videos")
                : L("开始生成视频", "Generate Video"),
        };
    }

    private void SyncGenerateActionSection()
    {
        bool generating = ViewModel.IsGenerating;
        GenerateButton.IsEnabled = !generating;
        GenerateSpinner.IsActive = generating;
        GenerateSpinner.Visibility = generating ? Visibility.Visible : Visibility.Collapsed;
        GenerateButtonIcon.Visibility = generating ? Visibility.Collapsed : Visibility.Visible;
        GenerateButtonIcon.Glyph = ViewModel.ActiveModelSpec.Modality == ModelModality.LlmVLM ? "\uE724" : "\uE768";
        GenerateButtonText.Text = BuildGenerateButtonTitle();

        CancelGenerationButton.Visibility = generating ? Visibility.Visible : Visibility.Collapsed;
        ProgressRow.Visibility = generating ? Visibility.Visible : Visibility.Collapsed;
        if (generating)
        {
            int pct = (int)Math.Round(Math.Clamp(ViewModel.ProgressValue, 0.0, 1.0) * 100.0);
            GenerationProgressText.Text = $"{pct}%";
        }
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsGenerating) return;
        if (ViewModel.ActiveModelSpec.Modality == ModelModality.ComfyUI ||
            string.Equals(ViewModel.TaskModeId, "api", StringComparison.Ordinal))
        {
            return;
        }
        ViewModel.RecordLocalInteraction();
        _ = ViewModel.StartGenerationAsync();
    }

    private void CancelGenerationButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RecordLocalInteraction();
        _ = ViewModel.CancelCurrentGenerationAsync();
    }

    private sealed record ModeCardVisual(
        string ModeId,
        Border CardBorder,
        Border IconBox,
        FontIcon IconGlyph,
        TextBlock TitleText,
        Border ActiveBadge,
        TextBlock ActiveBadgeText,
        TextBlock SubtitleText,
        FontIcon RadioIcon);
}
