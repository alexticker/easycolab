using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace EasyColab.Views.Dialogs;

public sealed partial class ColabGuideDialog : UserControl
{
    private const string SeenGuideKeySwift = "hasSeenOnboardingGuide_v1";
    private const string SeenGuideKeyV1 = "hasSeenEasyColabGuideV1";

    private sealed record GuideStepMeta(
        int Id,
        string BadgeZh,
        string BadgeEn,
        string TitleZh,
        string TitleEn,
        string SubtitleZh,
        string SubtitleEn)
    {
        public string Title => L(TitleZh, TitleEn);
        public string Subtitle => L(SubtitleZh, SubtitleEn);
    }

    private static readonly GuideStepMeta[] Steps =
    [
        new(
            0,
            "第 1 步", "Step 1",
            "连接云端 96GB 显卡", "Connect Cloud 96GB GPU",
            "绑定账号 · 一键唤醒 G4 实例", "Bind Google · 1-Click Wake G4 VM"),
        new(
            1,
            "第 2 步", "Step 2",
            "下载与加载开源模型", "Download & Load Open Models",
            "云盘永久保存 · 大内存热备共驻", "Drive Persistence · Co-Resident VRAM"),
        new(
            2,
            "第 3 步", "Step 3",
            "四大模式快速上手", "4 Creative Studio Modes",
            "有声视频 · ComfyUI · 对话 · 生图", "Video+Audio · ComfyUI · Chat · Image"),
        new(
            3,
            "第 4 步", "Step 4",
            "作品保存与省额度秘籍", "Output Saving & CU Tips",
            "自动回传本地 · 防忘关机自动释放", "Auto Local Sync · Idle Auto-Release"),
    ];

    private static int? _preservedStepAcrossLangRebuild;

    public StudioViewModel ViewModel => StudioViewModel.Shared;

    private int _currentStep;
    private bool _wasVisible;
    private bool _subscribed;

    public ColabGuideDialog()
    {
        InitializeComponent();

        PopulateMultilineDetails();
        Subscribe();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => UpdateStepUI();

        SyncVisibilityFromVm(initialSync: true);
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        LanguageManager.Shared.LanguageChanged += OnLanguageChanged;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        LanguageManager.Shared.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Subscribe();
        PopulateMultilineDetails();
        SyncVisibilityFromVm(initialSync: false);
        RefreshEnvironmentStatus();
        UpdateStepUI();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unsubscribe();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UiDispatcher.Run(() =>
        {
            PopulateMultilineDetails();
            RefreshEnvironmentStatus();
            UpdateStepUI();
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var prop = e.PropertyName;
        if (string.IsNullOrEmpty(prop) || prop == nameof(StudioViewModel.ShowColabGuideSheet))
        {
            SyncVisibilityFromVm(initialSync: false);
        }

        if (string.IsNullOrEmpty(prop)
            || prop == nameof(StudioViewModel.CliLoggedIn)
            || prop == nameof(StudioViewModel.IsConnected)
            || prop == nameof(StudioViewModel.IsWakingColab)
            || prop == nameof(StudioViewModel.DisplayGpuName)
            || prop == nameof(StudioViewModel.GpuName))
        {
            RefreshEnvironmentStatus();
        }
    }

    private void SyncVisibilityFromVm(bool initialSync)
    {
        bool shouldShow = ViewModel.ShowColabGuideSheet;
        Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;

        if (shouldShow && (!_wasVisible || initialSync))
        {
            int targetStep = _preservedStepAcrossLangRebuild ?? ViewModel.GuideInitialStep;
            _preservedStepAcrossLangRebuild = null;
            _currentStep = Math.Clamp(targetStep, 0, Steps.Length - 1);
            MarkGuideSeen();
            RefreshEnvironmentStatus();
            UpdateStepUI();
        }

        _wasVisible = shouldShow;
    }

    private void PopulateMultilineDetails()
    {
        Step1Card1Detail.Text = L(
            "在主界面左侧「Colab 算力实例」卡片点击「绑定」，在弹出的浏览器页面登录开通了 Colab Pro / AI Ultra 的 Google 账号并粘贴授权码。绑定后会自动同步真实剩余 CU 计算单元与 Google Drive 云盘。",
            "In the left 'Colab Compute' card, click 'Bind', sign in with a Google account subscribed to Colab Pro / Pro+ / AI Ultra in your browser, and paste the authorization code. Your real CU balance and Google Drive will sync automatically.");

        Step1Card2Title.Text = L(
            "选择 G4 机型并点击「唤醒实例并部署」（免开网页）",
            "Select G4 & Click 'Wake & Deploy' (Browserless)");

        Step1Card2Detail.Text = L(
            "默认已为你选中性价比最高的 G4 机型（NVIDIA RTX PRO 6000 · 96GB 超大显存 + 176GB 系统内存）。点击蓝色「唤醒实例并部署」按钮后，客户端会自动在云端拉起实例、挂载你的 Google Drive 云盘并完成自动连接，全程约 1～2 分钟。",
            "G4 (NVIDIA RTX PRO 6000 · 96GB VRAM + 176GB System RAM) is pre-selected for maximum price-performance. Click the blue 'Wake & Deploy' button to provision a cloud runtime, mount Google Drive, and connect automatically in ~1–2 minutes.");

        Step1Card3Detail.Text = L(
            "若你希望在浏览器里查看原生 Notebook，也可以把同步生成的 EasyColab_Server.ipynb 上传至 Colab 网页运行，再把生成的 https://xxxx.gradio.live 地址粘贴到左侧服务地址栏点击「连接」。",
            "Prefer running a notebook in your browser? Upload EasyColab_Server.ipynb to Google Colab, run the cell, then paste the generated https://xxxx.gradio.live URL into the Server URL field on the left and click 'Connect'.");

        Step2Card1Title.Text = L(
            "先点「下载权重」：一次存入 Google Drive，永久免重下",
            "Step 1 — 'Download Weights': Save Once to Google Drive Forever");

        Step2Card1Detail.Text = L(
            "在左侧「开源模型中心」选中你想用的模型。第一次使用时点击「下载权重」，服务端会以 8 线程极速将模型直接下载进你的 Google Drive 云盘。以后每次重新唤醒新机器，都会秒级识别云盘已有权重，无需重复下载。",
            "Pick a model in 'Open Model Center' on the left. Click 'Download Weights' once — the server downloads shards at 8-thread speed directly into your Google Drive. Future runtimes detect existing Drive weights instantly without re-downloading.");

        Step2Card2Title.Text = L(
            "再点「加载到显存」：96GB 显存 + 176GB 内存热备加速",
            "Step 2 — 'Load to VRAM': 96GB VRAM + 176GB RAM Hot-Standby");

        Step2Card2Detail.Text = L(
            "权重就绪后点击「加载到显存」即可开始生成。在 G4 机型上：\n• MiniMax-H3 原生 BF16 会自动激活「双主干内存热备」，同提示词换种子 0ms 瞬回，换新提示词解析仅需约 10 秒。\n• 支持多模型同时常驻显存（例如同时驻留 Qwen3.8-GGUF 对话与生图/视频模型），也可随时在卡片上点「卸载」单独释放。",
            "Once weights are ready, click 'Load to VRAM':\n• On G4, MiniMax-H3 Native BF16 activates dual-backbone RAM hot-standby — 0ms cache hit when changing seeds and ~10s encoding on new prompts.\n• Multiple models can co-reside in VRAM simultaneously (e.g. Qwen3.8-GGUF chat alongside image/video models) and be unloaded individually anytime.");

        Step3CardADetail.Text = L(
            "支持「文生有声视频」「首帧图生视频」「首尾帧生视频」三种模式。可分别填写画面描述、环境音效与背景配乐（也可点右上角「示例模板」一键填入），下方提供 4s / 5s / 8s / 10s / 12s / 15s 快捷时长按钮，直出带 32kHz 立体声的 MP4 成片。",
            "Supports Text-to-Video+Audio, Image-to-Video, and First/Last Frame modes. Describe visual scenes, soundscapes/dialogue, and music (or insert a preset from 'Templates'), pick 4s–15s duration chips, and render MP4s with 32kHz stereo audio.");

        Step3CardBDetail.Text = L(
            "不会连节点也能直接用：切到 ComfyUI -> 点击「一键部署并启动」-> 在下方「预置图生视频 / 首尾帧运镜工作流包」任选万相 Wan2.2 / MiniMax-H3 / LTX-2.3 / 混元 1.5 点「一键配齐」-> 点击打开免费公网画布 -> 左上角「工作流」菜单打开中文模板 -> 上传首尾图片点「运行」即可！所有插件、模型与工作流均自动持久化到云盘。",
            "No node wiring required: switch to ComfyUI -> click 'Start ComfyUI' -> pick Wan2.2 / MiniMax-H3 / LTX-2.3 / Hunyuan 1.5 under Preset Workflow Packs and click '1-Click Install' -> open the fullscreen canvas -> select a template from Workflows -> upload 1–2 images and hit Run! All custom nodes, models, and workflows persist to Google Drive.");

        Step3CardCDetail.Text = L(
            "推荐日常选择 GGUF 量化版（仅占约 21GB 显存，生成高达 100 tok/s，支持最高 256K 上下文与拖入图片反推提示词）。切换到「API 协议网关」标签还可一键复制兼容 OpenAI 与 Anthropic 的公网接口，直接接入 Claude Code 或 Cursor。",
            "Recommended for daily use (~21GB VRAM, up to 100 tok/s, up to 256K context, and drag-and-drop image analysis). Switch to the 'API Gateway' mode card to copy OpenAI `/v1` and Anthropic `/v1/messages` endpoints for Claude Code or Cursor.");

        Step4Card2Title.Text = L(
            "开启自动休眠 + 用完点「释放全部实例」立省 CU 额度",
            "Idle Auto-Sleep + 'Release All Instances' Saves Compute Units");

        Step4Card2Detail.Text = L(
            "Colab 按 GPU 在线时长消耗计算单元（CU）：\n• 左侧「算力额度与自动休眠」默认开启 30 分钟闲置看门狗，长时间离开会自动销毁虚拟机防止忘关机。\n• 当天创作结束时，建议直接点击左侧红色的「释放全部实例」按钮，云端 GPU 立即销毁、计费瞬间归零！",
            "Colab bills Compute Units (CU) while a GPU VM is online:\n• 'Compute Quota & Auto-Sleep' enables a 30-minute idle watchdog by default to auto-terminate forgotten runtimes.\n• When done for the day, click the red 'Release All Instances' button on the left to destroy cloud VMs and stop CU burn immediately!");

        Step4Card3Detail.Text = L(
            "ComfyUI 使用的 *.trycloudflare.com 随机公网链接由 Cloudflare 官方免费 Quick Tunnel 提供，0 元免注册、全程 HTTPS 加密，且每次释放 Colab 实例后自动销毁失效，安全省心。",
            "ComfyUI's random `*.trycloudflare.com` URL is powered by Cloudflare's official free Quick Tunnel — zero cost, no signup, end-to-end HTTPS encryption, and automatically invalidated whenever the Colab VM is released.");

        FooterHintText.Text = L(
            "随时可点击左上角「新手引导」或顶栏「使用指南」再次打开本页",
            "Reopen anytime via 'Guide' at top-left or 'User Guide' in the top bar");
    }

    private void RefreshEnvironmentStatus()
    {
        bool loggedIn = ViewModel.CliLoggedIn;
        bool connected = ViewModel.IsConnected;
        bool waking = ViewModel.IsWakingColab;

        AccountStatusDot.Color = ResolveThemeBrush(loggedIn ? "StudioSystemGreenBrush" : "StudioSystemOrangeBrush");
        string accText = loggedIn
            ? L("Google 账号：已绑定", "Google Account: Connected")
            : L("Google 账号：待绑定", "Google Account: Unbound");
        AccountStatusText.Text = accText;
        ToolTipService.SetToolTip(AccountStatusText, accText);

        GpuStatusDot.Color = ResolveThemeBrush(connected ? "StudioSystemGreenBrush" : "StudioSecondaryOpacity50Brush");
        string gpuText = connected
            ? L($"云端算力：{ViewModel.DisplayGpuName}", $"Cloud GPU: {ViewModel.DisplayGpuName}")
            : L("云端算力：尚未唤醒连接", "Cloud GPU: Not Connected");
        GpuStatusText.Text = gpuText;
        ToolTipService.SetToolTip(GpuStatusText, gpuText);

        BindNowButton.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        AlreadyBoundBadge.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        WakeG4NowButton.Visibility = (loggedIn && !connected && !waking)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateStepUI()
    {
        var meta = Steps[_currentStep];
        StepBadgeText.Text = $"STEP 0{_currentStep + 1} / 04";
        StepTitleText.Text = meta.Title;
        StepSubtitleText.Text = meta.Subtitle;

        bool isEn = LanguageManager.Shared.IsEN;
        LangToggleLabel.Text = isEn ? "中文" : "EN";
        ToolTipService.SetToolTip(
            LangToggleButton,
            isEn ? "切换界面为简体中文" : "Switch UI to English");

        Step0Panel.Visibility = _currentStep == 0 ? Visibility.Visible : Visibility.Collapsed;
        Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        StepScrollViewer.ChangeView(null, 0, null, disableAnimation: true);

        PrevStepButton.Visibility = _currentStep > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextStepButton.Content = _currentStep < Steps.Length - 1
            ? L("下一步", "Next")
            : L("开始创作", "Start Creating");

        Button[] navButtons = [NavBtn0, NavBtn1, NavBtn2, NavBtn3];
        FontIcon[] navIcons = [NavIcon0, NavIcon1, NavIcon2, NavIcon3];
        TextBlock[] navTitles = [NavTitle0, NavTitle1, NavTitle2, NavTitle3];

        var accentBrush = ResolveThemeBrush("StudioSystemAccentBrush");
        var accentFillBrush = ResolveThemeBrush("StudioAccentBlueFillBrush");
        var secondaryBrush = ResolveThemeBrush("StudioTextSecondaryBrush");
        var primaryBrush = ResolveThemeBrush("StudioTextPrimaryBrush");
        var transparentBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        var accentBorderColor = accentBrush is SolidColorBrush scb
            ? Windows.UI.Color.FromArgb(115, scb.Color.R, scb.Color.G, scb.Color.B)
            : Windows.UI.Color.FromArgb(115, 0x0A, 0x84, 0xFF);
        var accentBorderBrush = new SolidColorBrush(accentBorderColor);

        for (int i = 0; i < navButtons.Length; i++)
        {
            bool selected = i == _currentStep;
            navButtons[i].Background = selected ? accentFillBrush : transparentBrush;
            navButtons[i].BorderBrush = selected ? accentBorderBrush : transparentBrush;
            navIcons[i].Foreground = selected ? accentBrush : secondaryBrush;
            navTitles[i].FontWeight = selected ? FontWeights.Bold : FontWeights.SemiBold;
            navTitles[i].Foreground = primaryBrush;
            navTitles[i].Opacity = selected ? 1.0 : 0.84;
        }
    }

    private static void MarkGuideSeen()
    {
        SettingsStore.Shared.Set(SeenGuideKeySwift, true);
        SettingsStore.Shared.Set(SeenGuideKeyV1, true);
    }

    private void CompleteAndClose()
    {
        MarkGuideSeen();
        ViewModel.ShowColabGuideSheet = false;
    }

    private void JumpToModel(string modelId)
    {
        var spec = OPEN_MODEL_CATALOG.FirstOrDefault(m => m.Id == modelId);
        if (spec is not null)
        {
            ViewModel.SwitchSelectedModel(spec);
        }
        CompleteAndClose();
    }

    private static void RevealServerNotebookInExplorer()
    {
        string notebookPath = LocalRuntimePaths.ResolveBundledOrRepoFile("EasyColab_Server.ipynb");
        if (File.Exists(notebookPath))
        {
            LocalRuntimePaths.OpenInExplorer(notebookPath, reveal: true);
        }
    }

    private void OnNavStepClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string s } && int.TryParse(s, out int idx))
        {
            _currentStep = Math.Clamp(idx, 0, Steps.Length - 1);
            UpdateStepUI();
        }
    }

    private void OnPreviousStepClick(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 0)
        {
            _currentStep--;
            UpdateStepUI();
        }
    }

    private void OnNextOrFinishClick(object sender, RoutedEventArgs e)
    {
        if (_currentStep < Steps.Length - 1)
        {
            _currentStep++;
            UpdateStepUI();
        }
        else
        {
            CompleteAndClose();
        }
    }

    private void OnToggleLanguageClick(object sender, RoutedEventArgs e)
    {
        _preservedStepAcrossLangRebuild = _currentStep;
        ViewModel.ToggleLanguage();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CompleteAndClose();

    private void OnScrimPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        CompleteAndClose();
    }

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.ShowColabGuideSheet
            && !ViewModel.ShowHFModelDetailOverlay
            && !ViewModel.ShowAddCustomModelSheet)
        {
            CompleteAndClose();
            args.Handled = true;
        }
    }

    private void OnBindGoogleNowClick(object sender, RoutedEventArgs e)
    {
        CompleteAndClose();
        _ = ViewModel.StartGoogleOAuthLoginAsync();
    }

    private void OnWakeG4NowClick(object sender, RoutedEventArgs e)
    {
        ViewModel.WakeGPUChoice = "G4";
        CompleteAndClose();
        _ = ViewModel.WakeColabHeadlessAsync();
    }

    private void OnRevealNotebookClick(object sender, RoutedEventArgs e) => RevealServerNotebookInExplorer();

    private void OnOpenColabWebClick(object sender, RoutedEventArgs e) =>
        LocalRuntimePaths.OpenUrl("https://colab.research.google.com/");

    private void OnJumpToModelClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string modelId })
        {
            JumpToModel(modelId);
        }
    }

    private void OnOpenLocalOutputDirClick(object sender, RoutedEventArgs e) =>
        LocalRuntimePaths.OpenInExplorer(ViewModel.LocalSaveDir);

    private Brush ResolveThemeBrush(string key)
    {
        string themeKey = ActualTheme == ElementTheme.Light ? "Light" : "Dark";
        var root = Application.Current?.Resources;
        if (root is not null && FindThemeResource(root, themeKey, key) is Brush themed)
            return themed;
        if (root is not null && root.TryGetValue(key, out var fallback) && fallback is Brush fb)
            return fb;
        return new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA));
    }

    private static object? FindThemeResource(ResourceDictionary dict, string themeKey, string key)
    {
        if (dict.ThemeDictionaries.TryGetValue(themeKey, out var td)
            && td is ResourceDictionary themed
            && themed.TryGetValue(key, out var val))
        {
            return val;
        }

        foreach (var merged in dict.MergedDictionaries)
        {
            if (FindThemeResource(merged, themeKey, key) is { } found)
                return found;
        }

        return null;
    }
}
