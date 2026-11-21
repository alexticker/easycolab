using System.ComponentModel;
using EasyColab.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using WinColor = Windows.UI.Color;

namespace EasyColab.Views.Workspace;

public sealed partial class ApiGatewayView : UserControl
{
    public StudioViewModel ViewModel => StudioViewModel.Shared;

    private bool _isSubscribed;
    private bool _syncingApiKey;
    private bool _revealApiKey = true;
    private int _openaiSnippetTab;    // 0 = Python SDK, 1 = Cursor, 2 = curl
    private int _anthropicSnippetTab; // 0 = Claude Code, 1 = curl

    public ApiGatewayView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        SyncAll();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
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
            ActualThemeChanged -= OnActualThemeChanged;
            _isSubscribed = false;
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        SyncAuthCard();
        SyncSegmentedTabVisuals();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(StudioViewModel.ApiRequestCount):
            case nameof(StudioViewModel.IsConnected):
            case nameof(StudioViewModel.IsQwenLoadedInVRAM):
            case nameof(StudioViewModel.LoadedModelIds):
            case nameof(StudioViewModel.SelectedModelId):
            case nameof(StudioViewModel.ApiAccessKey):
            case nameof(StudioViewModel.RemoteApiKeySynced):
            case nameof(StudioViewModel.ColabServerURL):
            case nameof(StudioViewModel.NormalizedBaseURL):
                SyncAll();
                break;
        }
    }

    // ── Computed Protocol Values (1:1 port of MainStudioView+Workspace.swift) ──

    private string BaseHost =>
        string.IsNullOrEmpty(ViewModel.NormalizedBaseURL)
            ? "https://xxxx.gradio.live"
            : ViewModel.NormalizedBaseURL;

    private string OpenAiBase => $"{BaseHost}/v1";

    private string AnthropicBase => BaseHost;

    private string ActualKey =>
        ViewModel.ApiKeyEnabled ? (ViewModel.ApiAccessKey ?? string.Empty) : "sk-no-key-required";

    private string SnippetDisplayKey
    {
        get
        {
            string key = ActualKey;
            if (_revealApiKey || string.IsNullOrEmpty(key) || key == "sk-no-key-required")
                return key;
            if (key.Length <= 8)
                return new string('•', key.Length);
            return $"{key[..Math.Min(9, key.Length)]}••••••••{key[^4..]}";
        }
    }

    private string ActiveLLMRepo
    {
        get
        {
            var vm = ViewModel;
            if (vm.SelectedModelId == "qwen38_27b_gguf") return "unsloth/Qwen3.8-27B-GGUF";
            if (vm.SelectedModelId == "qwen38_27b") return "Qwen/Qwen3.8-27B";
            var loaded = vm.LoadedModelIds;
            if (loaded is not null && loaded.Contains("qwen38_27b_gguf") && !loaded.Contains("qwen38_27b"))
                return "unsloth/Qwen3.8-27B-GGUF";
            return "Qwen/Qwen3.8-27B";
        }
    }

    private string ActiveLLMDisplayName =>
        ActiveLLMRepo.Contains("GGUF", StringComparison.Ordinal) ? "Qwen3.8-27B-GGUF" : "Qwen3.8-27B";

    private string BuildOpenAiCurl(string key)
    {
        string prompt = L("用三句话解释量子纠缠，并展示思考过程", "Explain quantum entanglement in 3 sentences with reasoning");
        return $$"""
            curl {{OpenAiBase}}/chat/completions \
              -H "Content-Type: application/json" \
              -H "Authorization: Bearer {{key}}" \
              -d '{
                "model": "{{ActiveLLMRepo}}",
                "stream": true,
                "messages": [{"role": "user", "content": "{{prompt}}"}]
              }'
            """;
    }

    private string BuildOpenAiPython(string key)
    {
        string prompt = L("帮我设计一个高并发网关架构", "Design a high-concurrency API gateway architecture");
        return $$"""
            from openai import OpenAI

            client = OpenAI(base_url="{{OpenAiBase}}", api_key="{{key}}")
            stream = client.chat.completions.create(
                model="{{ActiveLLMRepo}}",
                messages=[{"role": "user", "content": "{{prompt}}"}],
                stream=True,
            )
            for chunk in stream:
                delta = chunk.choices[0].delta
                if getattr(delta, "reasoning_content", None):
                    print(delta.reasoning_content, end="", flush=True)
                if delta.content:
                    print(delta.content, end="", flush=True)
            """;
    }

    private string BuildCursorConfig(string key)
    {
        string comment = L(
            "Cursor -> Settings -> Models -> OpenAI API Key 配置",
            "Cursor -> Settings -> Models -> OpenAI API Key");
        return $"""
            # {comment}
            Override OpenAI Base URL: {OpenAiBase}
            OpenAI API Key:           {key}
            Custom Model Name:        {ActiveLLMRepo}
            """;
    }

    private string BuildAnthropicCurl(string key)
    {
        string prompt = L("分析 Rust 所有权机制的核心优势", "Analyze the core advantages of Rust's ownership model");
        return $$"""
            curl {{AnthropicBase}}/v1/messages \
              -H "Content-Type: application/json" \
              -H "x-api-key: {{key}}" \
              -H "anthropic-version: 2023-06-01" \
              -d '{
                "model": "{{ActiveLLMRepo}}",
                "max_tokens": 2048,
                "stream": true,
                "messages": [{"role": "user", "content": "{{prompt}}"}]
              }'
            """;
    }

    private string BuildClaudeCodeEnv(string key) =>
        $"""
        export ANTHROPIC_BASE_URL="{AnthropicBase}"
        export ANTHROPIC_API_KEY="{key}"
        export ANTHROPIC_MODEL="{ActiveLLMRepo}"
        """;

    // ── UI Synchronization ──

    private void SyncAll()
    {
        SyncAuthCard();
        SyncProtocolCards();
        SyncSegmentedTabVisuals();
    }

    private void SyncAuthCard()
    {
        var vm = ViewModel;

        ApiRequestCountText.Text = L(
            $"累计 API 请求: {vm.ApiRequestCount} 次",
            $"Total API Requests: {vm.ApiRequestCount}");

        string llmName = ActiveLLMDisplayName;
        if (!vm.IsQwenLoadedInVRAM)
        {
            VramStatusTipText.Text = L(
                $"提示：请先在左侧点击「加载到显存」装载 {llmName}（可与其他模型同时共驻显存）",
                $"Tip: Click 'Load to VRAM' on the left to load {llmName} first (co-resides with other models)");
            VramStatusTipText.Foreground = ResolveThemeBrush("StudioSystemOrangeBrush");
            VramStatusTipText.Visibility = Visibility.Visible;
        }
        else if (vm.LoadedModelIds is not null && vm.LoadedModelIds.Count > 1)
        {
            VramStatusTipText.Text = L(
                $"多模型共驻已激活：外部 API 可随时调用 {llmName}，与视频/生图模型零切换共存",
                $"Multi-model VRAM active: external clients can call {llmName} alongside video/image models with zero reload");
            VramStatusTipText.Foreground = ResolveThemeBrush("StudioSystemGreenBrush");
            VramStatusTipText.Visibility = Visibility.Visible;
        }
        else
        {
            VramStatusTipText.Visibility = Visibility.Collapsed;
        }

        ApiKeyRow.Visibility = vm.ApiKeyEnabled ? Visibility.Visible : Visibility.Collapsed;
        SyncApiKeyButton.IsEnabled = vm.IsConnected;

        // Sync key editors without fighting cursor position
        _syncingApiKey = true;
        try
        {
            string key = vm.ApiAccessKey ?? string.Empty;
            if (ApiKeyTextBox.Text != key)
                ApiKeyTextBox.Text = key;
            if (ApiKeyPasswordBox.Password != key)
                ApiKeyPasswordBox.Password = key;
        }
        finally
        {
            _syncingApiKey = false;
        }

        ApiKeyTextBox.Visibility = _revealApiKey ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyPasswordBox.Visibility = _revealApiKey ? Visibility.Collapsed : Visibility.Visible;
        ToggleRevealKeyIcon.Glyph = _revealApiKey ? "\uED1A" : "\uE7B3";
        ToolTipService.SetToolTip(ToggleRevealKeyButton, _revealApiKey
            ? L("隐藏密钥", "Hide Key")
            : L("显示密钥", "Reveal Key"));
    }

    private void SyncProtocolCards()
    {
        string openaiBase = OpenAiBase;
        string anthropicBase = AnthropicBase;
        string repo = ActiveLLMRepo;
        string llmName = ActiveLLMDisplayName;
        string displayKey = SnippetDisplayKey;

        // OpenAI endpoints
        OpenAiBaseUrlText.Text = openaiBase;
        ToolTipService.SetToolTip(OpenAiBaseUrlText, openaiBase);

        string chatUrl = $"{openaiBase}/chat/completions";
        OpenAiChatUrlText.Text = chatUrl;
        ToolTipService.SetToolTip(OpenAiChatUrlText, chatUrl);

        OpenAiModelIdText.Text = repo;
        ToolTipService.SetToolTip(OpenAiModelIdText, repo);

        // OpenAI active snippet
        OpenAiSnippetTitleText.Text = _openaiSnippetTab switch
        {
            0 => L("Python OpenAI SDK 流式示例", "Python OpenAI SDK Streaming Example"),
            1 => L("Cursor 编辑器接入配置", "Cursor IDE Custom Model Setup"),
            _ => "OpenAI Chat Completions curl",
        };
        OpenAiSnippetCodeText.Text = _openaiSnippetTab switch
        {
            0 => BuildOpenAiPython(displayKey),
            1 => BuildCursorConfig(displayKey),
            _ => BuildOpenAiCurl(displayKey),
        };

        // Anthropic endpoints
        AnthropicBaseUrlText.Text = anthropicBase;
        ToolTipService.SetToolTip(AnthropicBaseUrlText, anthropicBase);

        string messagesUrl = $"{anthropicBase}/v1/messages";
        AnthropicMessagesUrlText.Text = messagesUrl;
        ToolTipService.SetToolTip(AnthropicMessagesUrlText, messagesUrl);

        string authHeader = L("x-api-key 或 Authorization: Bearer", "x-api-key or Authorization: Bearer");
        AnthropicAuthHeaderText.Text = authHeader;
        ToolTipService.SetToolTip(AnthropicAuthHeaderText, authHeader);

        AnthropicDescriptionText.Text = L(
            $"完整支持 Anthropic SSE 事件流（thinking_delta 思考块、text_delta 文本块、tool_use 工具块）及 /v1/messages/count_tokens 接口，任意模型名均可自动路由至 {llmName}。",
            $"Full Anthropic SSE streaming (`thinking_delta`, `text_delta`, `tool_use`) plus `/v1/messages/count_tokens`; any model alias routes automatically to {llmName}.");

        // Anthropic active snippet
        AnthropicSnippetTitleText.Text = _anthropicSnippetTab == 0
            ? L("Claude Code / 终端环境变量接入", "Claude Code / Shell Environment Variables")
            : "Anthropic /v1/messages curl";
        AnthropicSnippetCodeText.Text = _anthropicSnippetTab == 0
            ? BuildClaudeCodeEnv(displayKey)
            : BuildAnthropicCurl(displayKey);
    }

    private void SyncSegmentedTabVisuals()
    {
        var activeBg = ResolveThemeBrush("StudioWorkspaceCardFillBrush");
        var activeBorder = ResolveThemeBrush("StudioCardBorderBrush");
        var activeFg = ResolveThemeBrush("StudioTextPrimaryBrush");
        var inactiveBg = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var inactiveFg = ResolveThemeBrush("StudioTextSecondaryBrush");

        void StyleTab(Button btn, bool active)
        {
            btn.Background = active ? activeBg : inactiveBg;
            btn.Foreground = active ? activeFg : inactiveFg;
            btn.BorderBrush = active ? activeBorder : inactiveBg;
            btn.BorderThickness = active ? new Thickness(0.7) : new Thickness(0);
        }

        StyleTab(OpenAiTabPythonBtn, _openaiSnippetTab == 0);
        StyleTab(OpenAiTabCursorBtn, _openaiSnippetTab == 1);
        StyleTab(OpenAiTabCurlBtn, _openaiSnippetTab == 2);

        StyleTab(AnthropicTabClaudeCodeBtn, _anthropicSnippetTab == 0);
        StyleTab(AnthropicTabCurlBtn, _anthropicSnippetTab == 1);
    }

    // ── Event Handlers ──

    private void OnApiKeyTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingApiKey) return;
        ViewModel.ApiAccessKey = ApiKeyTextBox.Text;
    }

    private void OnApiKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingApiKey) return;
        ViewModel.ApiAccessKey = ApiKeyPasswordBox.Password;
    }

    private async void OnApiKeyInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.IsConnected)
        {
            e.Handled = true;
            await ViewModel.SyncApiKeyToColabAsync();
        }
    }

    private void OnToggleRevealKeyClick(object sender, RoutedEventArgs e)
    {
        _revealApiKey = !_revealApiKey;
        SyncAuthCard();
        SyncProtocolCards();
    }

    private async void OnSyncApiKeyClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.SyncApiKeyToColabAsync();
        ViewModel.StatusMessage = L(
            "API 密钥已同步更新至 Colab 云端网关。",
            "API Key synced to Colab cloud gateway.");
    }

    private void OnRegenerateApiKeyClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RegenerateApiKey();
        ViewModel.StatusMessage = L(
            "已生成新的随机 API Key。",
            "Generated a new random API Key.");
    }

    private void OnCopyApiKeyClick(object sender, RoutedEventArgs e)
    {
        CopyToPasteboard(
            ViewModel.ApiAccessKey,
            L("已复制 API Key 到剪贴板。", "Copied API Key to clipboard."));
    }

    // OpenAI endpoint copy handlers
    private void OnCopyOpenAiBaseUrlClick(object sender, RoutedEventArgs e) =>
        CopyToPasteboard(OpenAiBase, L("已复制 OpenAI Base URL", "Copied OpenAI Base URL"));

    private void OnCopyOpenAiChatUrlClick(object sender, RoutedEventArgs e) =>
        CopyToPasteboard($"{OpenAiBase}/chat/completions", L("已复制 Chat Completions 地址", "Copied Chat Completions URL"));

    private void OnCopyOpenAiModelIdClick(object sender, RoutedEventArgs e) =>
        CopyToPasteboard(ActiveLLMRepo, L("已复制模型名称", "Copied Model ID"));

    // OpenAI tab handlers
    private void OnOpenAiTabPythonClick(object sender, RoutedEventArgs e)
    {
        _openaiSnippetTab = 0;
        SyncProtocolCards();
        SyncSegmentedTabVisuals();
    }

    private void OnOpenAiTabCursorClick(object sender, RoutedEventArgs e)
    {
        _openaiSnippetTab = 1;
        SyncProtocolCards();
        SyncSegmentedTabVisuals();
    }

    private void OnOpenAiTabCurlClick(object sender, RoutedEventArgs e)
    {
        _openaiSnippetTab = 2;
        SyncProtocolCards();
        SyncSegmentedTabVisuals();
    }

    // OpenAI snippet copy handlers (always copy with actual key)
    private void OnCopyOpenAiPythonClick(object sender, RoutedEventArgs e) =>
        CopyToPasteboard(BuildOpenAiPython(ActualKey), L("已复制 OpenAI Python 调用代码。", "Copied OpenAI Python snippet."));

    private void OnCopyOpenAiCursorClick(object sender, RoutedEventArgs e) =>
        CopyToPasteboard(BuildCursorConfig(ActualKey), L("已复制 Cursor 接入配置。", "Copied Cursor configuration."));

    private void OnCopyOpenAiCurlClick(object sender, RoutedEventArgs e) =>
        CopyToPasteboard(BuildOpenAiCurl(ActualKey), L("已复制 OpenAI curl 测试命令。", "Copied OpenAI curl command."));

    // Anthropic endpoint copy handlers
    private void OnCopyAnthropicBaseUrlClick(object sender, RoutedEventArgs e) =>
        CopyToPasteboard(AnthropicBase, L("已复制 Anthropic Base URL", "Copied Anthropic Base URL"));

    private void OnCopyAnthropicMessagesUrlClick(object sender, RoutedEventArgs e) =>
        CopyToPasteboard($"{AnthropicBase}/v1/messages", L("已复制 Anthropic Messages 地址", "Copied Anthropic Messages URL"));

    private void OnCopyAnthropicAuthHeaderClick(object sender, RoutedEventArgs e) =>
        CopyToPasteboard(
            L("x-api-key 或 Authorization: Bearer", "x-api-key or Authorization: Bearer"),
            L("已复制请求头说明", "Copied auth header format"));

    // Anthropic tab handlers
    private void OnAnthropicTabClaudeCodeClick(object sender, RoutedEventArgs e)
    {
        _anthropicSnippetTab = 0;
        SyncProtocolCards();
        SyncSegmentedTabVisuals();
    }

    private void OnAnthropicTabCurlClick(object sender, RoutedEventArgs e)
    {
        _anthropicSnippetTab = 1;
        SyncProtocolCards();
        SyncSegmentedTabVisuals();
    }

    // Anthropic snippet copy handlers (always copy with actual key)
    private void OnCopyClaudeCodeEnvClick(object sender, RoutedEventArgs e) =>
        CopyToPasteboard(BuildClaudeCodeEnv(ActualKey), L("已复制 ANTHROPIC_BASE_URL 环境变量配置。", "Copied ANTHROPIC_BASE_URL environment variables."));

    private void OnCopyAnthropicCurlClick(object sender, RoutedEventArgs e) =>
        CopyToPasteboard(BuildAnthropicCurl(ActualKey), L("已复制 Anthropic /v1/messages curl 命令。", "Copied Anthropic /v1/messages curl command."));

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
}
