using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EasyColab.ViewModels;

// MARK: - 同源管理鉴权重定向策略 (SameOriginAdminRedirect)

/// <summary>
/// Port of Swift <c>SameOriginAdminRedirect</c>: validates HTTPS same-origin requests and strips/blocks
/// cross-origin redirects so admin credentials are never leaked off the connected Colab tunnel origin.
/// </summary>
public static class SameOriginAdminRedirect
{
    public static bool Permits(Uri? target, Uri? origin)
    {
        if (target is null || origin is null || !target.IsAbsoluteUri || !origin.IsAbsoluteUri)
            return false;
        if (string.IsNullOrEmpty(origin.Host))
            return false;
        if (!string.Equals(target.Scheme, "https", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(origin.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(target.UserInfo) || !string.IsNullOrEmpty(origin.UserInfo))
            return false;

        int targetPort = target.IsDefaultPort ? 443 : target.Port;
        int originPort = origin.IsDefaultPort ? 443 : origin.Port;
        return string.Equals(target.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
            && targetPort == originPort;
    }
}

/// <summary>
/// <see cref="DelegatingHandler"/> enforcing <see cref="SameOriginAdminRedirect"/> with
/// <c>AllowAutoRedirect = false</c> on the inner <see cref="SocketsHttpHandler"/>.
/// Cross-origin redirects are NOT followed (matching Swift's <c>completionHandler(nil)</c>);
/// same-origin HTTPS redirects preserve the original <c>Authorization</c> header.
/// </summary>
internal sealed class SameOriginAdminRedirectHandler : DelegatingHandler
{
    private const int MaxRedirects = 5;

    public SameOriginAdminRedirectHandler()
        : base(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        })
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var originalUri = request.RequestUri;
        var originalAuth = request.Headers.Authorization;

        // Capture buffered body only when the request has content that may need replaying on 307/308.
        byte[]? bufferedBody = null;
        MediaTypeHeaderValue? bodyContentType = null;
        if (request.Content is StringContent or ByteArrayContent)
        {
            bufferedBody = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            bodyContentType = request.Content.Headers.ContentType;
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var currentUri = originalUri;
        var currentMethod = request.Method;

        for (int hop = 0; hop < MaxRedirects; hop++)
        {
            if (!IsRedirectStatus(response.StatusCode) || response.Headers.Location is not { } loc || currentUri is null)
                return response;

            var nextUri = loc.IsAbsoluteUri ? loc : new Uri(currentUri, loc);
            if (!SameOriginAdminRedirect.Permits(nextUri, originalUri))
            {
                // Cross-origin redirect refused: do not follow, never leak credentials.
                return response;
            }

            var nextMethod = currentMethod;
            bool dropBody = false;
            if (response.StatusCode == HttpStatusCode.SeeOther ||
                ((response.StatusCode == HttpStatusCode.MovedPermanently || response.StatusCode == HttpStatusCode.Found)
                 && currentMethod == HttpMethod.Post))
            {
                nextMethod = HttpMethod.Get;
                dropBody = true;
            }

            var redirected = new HttpRequestMessage(nextMethod, nextUri);
            foreach (var header in request.Headers)
            {
                if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                    continue;
                redirected.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            if (originalAuth is not null)
            {
                redirected.Headers.Authorization = originalAuth;
            }
            if (!dropBody && bufferedBody is not null)
            {
                var content = new ByteArrayContent(bufferedBody);
                if (bodyContentType is not null)
                    content.Headers.ContentType = bodyContentType;
                redirected.Content = content;
            }

            response.Dispose();
            response = await base.SendAsync(redirected, cancellationToken).ConfigureAwait(false);
            currentUri = nextUri;
            currentMethod = nextMethod;
        }

        return response;
    }

    private static bool IsRedirectStatus(HttpStatusCode code) => code is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Found or
        HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;
}

// MARK: - 核心视图模型 (StudioViewModel)

public sealed partial class StudioViewModel : ObservableObject
{
    private static readonly Lazy<StudioViewModel> _shared = new(() => new StudioViewModel());
    public static StudioViewModel Shared => _shared.Value;

    // Shared HTTP clients (ephemeral, no cookie persistence; infinite base timeout so per-call CTS governs timeouts).
    internal static readonly HttpClient AdminHttpClient = new(new SameOriginAdminRedirectHandler())
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    internal static readonly HttpClient PublicHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private static readonly string[] ValidWakeGPUs = ["G4", "H100", "A100", "L4", "T4"];
    private static readonly int[] ValidIdleMinutes = [0, 15, 30, 60];
    private static readonly string[] ValidReasoningEfforts = ["low", "medium", "high", "xhigh"];

    // 默认初始文案（支持中英文双向无损切换）
    public const string DefaultGpuNameZh = "未连接 Colab 服务端";
    public const string DefaultGpuNameEn = "Colab Server Disconnected";
    public const string DefaultModelNameZh = "未加载";
    public const string DefaultModelNameEn = "Not Loaded";
    public const string DefaultComfyStatusZh = "尚未启动云端 ComfyUI 工作台";
    public const string DefaultComfyStatusEn = "Cloud ComfyUI Studio Not Started";
    public const string DefaultComfyPersistZh = "Google Drive 自动软链持久化";
    public const string DefaultComfyPersistEn = "Google Drive Auto-Symlink Persistence";
    public const string DefaultReadyStatusZh = "就绪：默认已选 G4 (RTX PRO 6000 · 96GB)，可在左侧一键唤醒实例并部署模型。";
    public const string DefaultReadyStatusEn = "Ready: G4 (RTX PRO 6000 · 96GB) selected by default. Wake & deploy from the left sidebar.";

    public static readonly string[] DefaultWelcomeLinesZh =
    [
        "欢迎使用 EasyColab 多模态云端算力工作台",
        "默认搭载 G4 (RTX PRO 6000 · 96GB)，支持一键部署 ComfyUI 节点工作台 (云盘软链持久化)、MiniMax-H3、Qwen3.8-27B (UD-Q4_K_M GGUF 100 tok/s / BF16)、Qwen-Image-2.1 与 Z-Image-Turbo。",
    ];

    public static readonly string[] DefaultWelcomeLinesEn =
    [
        "Welcome to EasyColab Multimodal Cloud GPU Studio",
        "Powered by G4 (RTX PRO 6000 · 96GB) by default — 1-click deploy ComfyUI Node Studio (Drive symlink persistence), MiniMax-H3, Qwen3.8-27B (UD-Q4_K_M GGUF 100 tok/s / BF16), Qwen-Image-2.1 & Z-Image-Turbo.",
    ];

    public const string ClearedConsoleLineZh = "已清空历史输出，正在监听云端实时日志...";
    public const string ClearedConsoleLineEn = "Cleared log history. Listening for live cloud logs...";
    public const string CustomModelsDefaultsKey = "easycolab.macos.custom_models.v1";
    public const int MaxTelemetrySamples = 48;
    public const int MaxLocalConsoleLines = 400;

    // ── Internal Runtime & Subprocess State (shared with StudioViewModel.Services*.cs) ──

    internal string? CachedBridgePythonBin;
    internal string BridgeScript => LocalRuntimePaths.ResolveBundledOrRepoFile("colab_bridge.py");
    internal string AppPyPath => LocalRuntimePaths.ResolveBundledOrRepoFile("app.py");

    internal Process? ActiveWakeProcess;
    internal readonly object ActiveWakeProcessLock = new();

    internal Process? KeepaliveProcess;
    internal readonly object KeepaliveProcessLock = new();

    internal CancellationTokenSource? TelemetryLoopCts;
    internal CancellationTokenSource? PollingCts;
    internal CancellationTokenSource? GenerationTimerCts;
    internal DateTime? GenerationStartedAt;
    internal DateTime? ServerConnectedAt;
    internal Guid ActiveJobPollToken;
    internal Guid ActiveGenerationToken = Guid.NewGuid();

    internal int ConsecutiveStatusFailures;
    internal DateTime LastTelemetrySampleDate = DateTime.MinValue;
    internal bool RemoteIdleSynced;
    internal bool RemoteContextSynced;
    internal DateTime LastIdleSyncAttemptAt = DateTime.MinValue;
    internal DateTime LastContextSyncAttemptAt = DateTime.MinValue;
    internal DateTime LastLocalInteractionAt = DateTime.UtcNow;
    internal DateTime LastRemoteTouchSentAt = DateTime.MinValue;

    private readonly bool _initialized;
    private bool _holdingActivityAssertion;
    private bool _isSanitizingProperty;

    // ── Win32 Sleep Prevention (Windows equivalent of macOS ProcessInfo.beginActivity) ──

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    // ── Media Selection Notification Event (optional convenience for WinUI MediaPlayerElement) ──

    public event EventHandler<string?>? MediaSelected;

    internal void RaiseMediaSelected(string? path) => MediaSelected?.Invoke(this, path);

    // ── Server Connection & Credentials ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayAccountLabel))]
    public partial bool MaskServerURL { get; set; }

    partial void OnMaskServerURLChanged(bool value)
    {
        if (!_initialized) return;
        SettingsStore.Shared.Set("maskServerURL", value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NormalizedBaseURL))]
    public partial string ColabServerURL { get; set; }

    partial void OnColabServerURLChanged(string oldValue, string newValue)
    {
        if (!_initialized) return;
        if (NormalizeServerURL(oldValue ?? string.Empty) != NormalizeServerURL(newValue))
        {
            AdminPassword = SavedAdminPassword(newValue);
            RemoteApiKeySynced = false;
            RemoteIdleSynced = false;
            RemoteContextSynced = false;
            LastIdleSyncAttemptAt = DateTime.MinValue;
            LastContextSyncAttemptAt = DateTime.MinValue;
        }
        SettingsStore.Shared.Set("colabServerURL", newValue);
    }

    [ObservableProperty]
    public partial string AdminPassword { get; set; }

    partial void OnAdminPasswordChanged(string value)
    {
        if (!_initialized) return;
        var key = NormalizedBaseURL;
        if (string.IsNullOrEmpty(key) || value == SavedAdminPassword(key)) return;
        var passwords = SettingsStore.Shared.Get<Dictionary<string, string>>("adminPasswordsByURL")
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(value))
            passwords.Remove(key);
        else
            passwords[key] = value;
        if (ColabServerURL != key)
            passwords.Remove(ColabServerURL);
        SettingsStore.Shared.Set("adminPasswordsByURL", passwords);
    }

    [ObservableProperty]
    public partial string HfToken { get; set; }

    partial void OnHfTokenChanged(string value)
    {
        if (!_initialized) return;
        SettingsStore.Shared.Set("hfToken", value);
    }

    // ── Colab CLI 唤醒与真实账本状态 ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayAccountLabel))]
    public partial bool CliLoggedIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayAccountLabel))]
    public partial string AccountEmail { get; set; }

    [ObservableProperty]
    public partial bool ShowOAuthCodeInput { get; set; }

    [ObservableProperty]
    public partial string OauthCodeText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayRemainingCU))]
    public partial double? OfficialCUBalance { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayBurnRate))]
    public partial double? OfficialBurnRate { get; set; }

    [ObservableProperty]
    public partial int ActiveVMCount { get; set; }

    [ObservableProperty]
    public partial string WakeGPUChoice { get; set; }

    partial void OnWakeGPUChoiceChanged(string value)
    {
        if (!_initialized) return;
        SettingsStore.Shared.Set("wakeGPUChoiceV2", value);
    }

    [ObservableProperty]
    public partial bool IsWakingColab { get; set; }

    partial void OnIsWakingColabChanged(bool value)
    {
        if (!_initialized) return;
        SyncRuntimeActivityAssertion();
    }

    // ── 远端 GPU 与多模型驻留状态 ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayBurnRate))]
    public partial bool IsConnected { get; set; }

    partial void OnIsConnectedChanged(bool value)
    {
        if (!_initialized) return;
        ServerConnectedAt = value ? (ServerConnectedAt ?? DateTime.UtcNow) : null;
        SyncRuntimeActivityAssertion();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayGpuName))]
    public partial string GpuName { get; set; }

    partial void OnGpuNameChanged(string value)
    {
        if (_isSanitizingProperty) return;
        var clean = StripEmoji(value);
        if (clean != value)
        {
            _isSanitizingProperty = true;
            try { GpuName = clean; }
            finally { _isSanitizingProperty = false; }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVramWarning), nameof(IsVramCritical))]
    public partial double VramFreeGB { get; set; }

    [ObservableProperty]
    public partial double VramTotalGB { get; set; }

    [ObservableProperty]
    public partial double VramUsedGB { get; set; }

    [ObservableProperty]
    public partial double RamTotalGB { get; set; }

    [ObservableProperty]
    public partial double RamUsedGB { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRamWarning), nameof(IsRamCritical))]
    public partial double RamFreeGB { get; set; }

    [ObservableProperty]
    public partial double DiskTotalGB { get; set; }

    [ObservableProperty]
    public partial double DiskUsedGB { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiskWarning), nameof(IsDiskCritical))]
    public partial double DiskFreeGB { get; set; }

    [ObservableProperty]
    public partial double ShmTotalGB { get; set; }

    [ObservableProperty]
    public partial double ShmUsedGB { get; set; }

    [ObservableProperty]
    public partial double ShmFreeGB { get; set; }

    [ObservableProperty]
    public partial double DriveTotalGB { get; set; }

    [ObservableProperty]
    public partial double DriveUsedGB { get; set; }

    [ObservableProperty]
    public partial double DriveFreeGB { get; set; }

    [ObservableProperty]
    public partial List<double> VramHistoryPct { get; set; }

    [ObservableProperty]
    public partial List<double> RamHistoryPct { get; set; }

    [ObservableProperty]
    public partial List<double> DiskHistoryPct { get; set; }

    [ObservableProperty]
    public partial bool CombinedTelemetryChartMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ActiveModelSpec),
        nameof(ActiveModeItem),
        nameof(IsSelectedModelLoadedInVRAM),
        nameof(IsMiniMaxH3ImageModel),
        nameof(ActiveImageResolutions))]
    public partial string SelectedModelId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(AllAvailableModels),
        nameof(ActiveModelSpec),
        nameof(ActiveModeItem),
        nameof(IsMiniMaxH3ImageModel),
        nameof(ActiveImageResolutions),
        nameof(IsQwenLoadedInVRAM))]
    public partial List<OpenModelSpec> CustomModels { get; set; }

    [ObservableProperty]
    public partial bool ShowAddCustomModelSheet { get; set; }

    [ObservableProperty]
    public partial string CustomRepoInput { get; set; }

    [ObservableProperty]
    public partial string CustomIncludePatternInput { get; set; }

    [ObservableProperty]
    public partial string CustomDisplayNameInput { get; set; }

    [ObservableProperty]
    public partial string CustomModalityInput { get; set; }

    [ObservableProperty]
    public partial bool IsRegisteringCustomModel { get; set; }

    [ObservableProperty]
    public partial string CustomModelErrorMessage { get; set; }

    [ObservableProperty]
    public partial List<HFTrendingModelItem> TrendingHFModels { get; set; }

    [ObservableProperty]
    public partial string TrendingHFCategory { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingTrendingHF { get; set; }

    [ObservableProperty]
    public partial HFModelDetailInfo? ActiveHFModelDetail { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingHFModelDetail { get; set; }

    [ObservableProperty]
    public partial bool ShowHFModelDetailOverlay { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedModelLoadedInVRAM), nameof(IsQwenLoadedInVRAM))]
    public partial string? LoadedModelId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedModelLoadedInVRAM), nameof(IsQwenLoadedInVRAM))]
    public partial List<string> LoadedModelIds { get; set; }

    [ObservableProperty]
    public partial List<LoadedModelSummary> LoadedModelsDetail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedModelLoadedInVRAM), nameof(IsQwenLoadedInVRAM))]
    public partial bool ModelLoaded { get; set; }

    [ObservableProperty]
    public partial bool IsUnloadingModel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayCurrentModelName))]
    public partial string CurrentModelName { get; set; }

    partial void OnCurrentModelNameChanged(string value)
    {
        if (_isSanitizingProperty) return;
        var clean = StripEmoji(value);
        if (clean != value)
        {
            _isSanitizingProperty = true;
            try { CurrentModelName = clean; }
            finally { _isSanitizingProperty = false; }
        }
    }

    // ── 算力额度账本 ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayRemainingCU))]
    public partial double TotalBudgetCU { get; set; }

    partial void OnTotalBudgetCUChanged(double value)
    {
        if (!_initialized) return;
        SettingsStore.Shared.Set("totalBudgetCU", value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedUptime))]
    public partial int UptimeSeconds { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayBurnRate))]
    public partial double CuBurnRatePerHour { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayRemainingCU))]
    public partial double SessionCUUsed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedIdleRemaining))]
    public partial int IdleTimeoutMinutes { get; set; }

    partial void OnIdleTimeoutMinutesChanged(int value)
    {
        if (!_initialized) return;
        SettingsStore.Shared.Set("idleTimeoutMinutes", value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedIdleRemaining))]
    public partial int IdleRemainingSeconds { get; set; }

    [ObservableProperty]
    public partial bool DrivePersisted { get; set; }

    [ObservableProperty]
    public partial int Bf16ShardsCount { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<string> LiveConsoleLines { get; set; }

    partial void OnLiveConsoleLinesChanged(ObservableCollection<string> value)
    {
        if (value is null) return;
        for (int i = 0; i < value.Count; i++)
        {
            var cleaned = StripEmoji(value[i]);
            if (cleaned != value[i])
                value[i] = cleaned;
        }
    }

    // ── 当前选中模型的权重明细 ──

    [ObservableProperty]
    public partial List<ModelFileItem> ModelFiles { get; set; }

    [ObservableProperty]
    public partial int DownloadedCount { get; set; }

    [ObservableProperty]
    public partial int TotalFileCount { get; set; }

    [ObservableProperty]
    public partial bool AllDownloaded { get; set; }

    [ObservableProperty]
    public partial double TotalDiskSizeGB { get; set; }

    [ObservableProperty]
    public partial bool IsDownloadingWeights { get; set; }

    partial void OnIsDownloadingWeightsChanged(bool value)
    {
        if (!_initialized) return;
        SyncRuntimeActivityAssertion();
    }

    [ObservableProperty]
    public partial bool IsLoadingModel { get; set; }

    partial void OnIsLoadingModelChanged(bool value)
    {
        if (!_initialized) return;
        SyncRuntimeActivityAssertion();
    }

    // ── ComfyUI 云端工作台与 Google Drive 软链持久化状态 ──

    [ObservableProperty]
    public partial bool ComfyRunning { get; set; }

    [ObservableProperty]
    public partial bool ComfyStarting { get; set; }

    [ObservableProperty]
    public partial bool ComfySyncingDrive { get; set; }

    [ObservableProperty]
    public partial bool ComfyDownloadingWeight { get; set; }

    [ObservableProperty]
    public partial string ComfyInstallingPackId { get; set; }

    [ObservableProperty]
    public partial List<ComfyPresetPackItem> ComfyPresetPacks { get; set; }

    [ObservableProperty]
    public partial double ComfyProgress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayComfyStatusMessage))]
    public partial string ComfyStatusMessage { get; set; }

    partial void OnComfyStatusMessageChanged(string value)
    {
        if (_isSanitizingProperty) return;
        var clean = StripEmoji(value);
        if (clean != value)
        {
            _isSanitizingProperty = true;
            try { ComfyStatusMessage = clean; }
            finally { _isSanitizingProperty = false; }
        }
    }

    [ObservableProperty]
    public partial string ComfyPublicURL { get; set; }

    [ObservableProperty]
    public partial int ComfyQueueRunning { get; set; }

    [ObservableProperty]
    public partial int ComfyQueuePending { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayComfyPersistenceLabel))]
    public partial string ComfyPersistenceLabel { get; set; }

    [ObservableProperty]
    public partial bool ComfyDrivePersisted { get; set; }

    [ObservableProperty]
    public partial string ComfyPersistRoot { get; set; }

    [ObservableProperty]
    public partial int ComfyCustomNodesCount { get; set; }

    [ObservableProperty]
    public partial int ComfyModelsCount { get; set; }

    [ObservableProperty]
    public partial int ComfyWorkflowsCount { get; set; }

    [ObservableProperty]
    public partial List<ComfyOutputMediaItem> ComfyRecentOutputs { get; set; }

    [ObservableProperty]
    public partial bool ComfyUnloadOthersOnStart { get; set; }

    [ObservableProperty]
    public partial string ComfyWeightSourceInput { get; set; }

    [ObservableProperty]
    public partial string ComfyWeightFilenameInput { get; set; }

    [ObservableProperty]
    public partial string ComfyWeightSubfolder { get; set; }

    [ObservableProperty]
    public partial string? ComfyPullingOutputRelPath { get; set; }

    [ObservableProperty]
    public partial double ModelTaskProgress { get; set; }

    [ObservableProperty]
    public partial string SelectedPrecision { get; set; }

    [ObservableProperty]
    public partial string SelectedOffload { get; set; }

    // ── 任务模式与生成参数 ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveModeItem))]
    public partial string TaskModeId { get; set; }

    [ObservableProperty]
    public partial string VisualPrompt { get; set; }

    [ObservableProperty]
    public partial string NegativePrompt { get; set; }

    [ObservableProperty]
    public partial string SoundscapePrompt { get; set; }

    [ObservableProperty]
    public partial string MusicPrompt { get; set; }

    /// <summary>Windows adaptation of Swift <c>firstFrameImage: NSImage?</c> (local image file path).</summary>
    [ObservableProperty]
    public partial string? FirstFrameImage { get; set; }

    /// <summary>Windows adaptation of Swift <c>lastFrameImage: NSImage?</c> (local image file path).</summary>
    [ObservableProperty]
    public partial string? LastFrameImage { get; set; }

    [ObservableProperty]
    public partial ResolutionOption SelectedVideoResolution { get; set; }

    [ObservableProperty]
    public partial ResolutionOption SelectedImageResolution { get; set; }

    [ObservableProperty]
    public partial double NumFrames { get; set; }

    [ObservableProperty]
    public partial double InferenceSteps { get; set; }

    [ObservableProperty]
    public partial double Fps { get; set; }

    [ObservableProperty]
    public partial long Seed { get; set; }

    [ObservableProperty]
    public partial bool RandomizeSeed { get; set; }

    [ObservableProperty]
    public partial int BatchCount { get; set; }

    [ObservableProperty]
    public partial double DenoisingStrength { get; set; }

    // ── Qwen3.8-27B / GGUF 对话与双协议 API 网关专属参数（支持最高 256K 上下文）──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContextLengthTokens))]
    public partial int ContextLengthK { get; set; }

    partial void OnContextLengthKChanged(int value)
    {
        int clamped = Math.Clamp(value, 4, 256);
        if (value != clamped)
        {
            ContextLengthK = clamped;
            return;
        }
        if (!_initialized) return;
        SettingsStore.Shared.Set("qwenContextLengthK", clamped);
    }

    [ObservableProperty]
    public partial int LoadedContextLengthTokens { get; set; }

    [ObservableProperty]
    public partial bool EnableThinking { get; set; }

    partial void OnEnableThinkingChanged(bool value)
    {
        if (!_initialized) return;
        // 自动对齐 Unsloth Qwen3.8 官方推荐混合思考采样表
        if (value)
        {
            Temperature = 1.0;
            TopP = 0.95;
            PresencePenalty = 0.0;
        }
        else
        {
            Temperature = 0.7;
            TopP = 0.80;
            PresencePenalty = 1.5;
        }
    }

    [ObservableProperty]
    public partial string ReasoningEffort { get; set; }

    partial void OnReasoningEffortChanged(string value)
    {
        if (!_initialized) return;
        SettingsStore.Shared.Set("reasoningEffort", value);
    }

    [ObservableProperty]
    public partial double MaxNewTokens { get; set; }

    [ObservableProperty]
    public partial double Temperature { get; set; }

    [ObservableProperty]
    public partial double TopP { get; set; }

    [ObservableProperty]
    public partial double PresencePenalty { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<ChatTurnItem> ChatHistory { get; set; }

    public bool ApiKeyEnabled => true;

    [ObservableProperty]
    public partial string ApiAccessKey { get; set; }

    partial void OnApiAccessKeyChanged(string value)
    {
        if (!_initialized) return;
        SettingsStore.Shared.Set("apiAccessKey", value);
    }

    [ObservableProperty]
    public partial int ApiRequestCount { get; set; }

    [ObservableProperty]
    public partial bool RemoteApiKeySynced { get; set; }

    // ── 任务进度与生成耗时状态 ──

    [ObservableProperty]
    public partial bool IsGenerating { get; set; }

    partial void OnIsGeneratingChanged(bool value)
    {
        if (!_initialized) return;
        SyncRuntimeActivityAssertion();
    }

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial double CurrentJobElapsed { get; set; }

    [ObservableProperty]
    public partial double? LastCompletedElapsed { get; set; }

    [ObservableProperty]
    public partial Dictionary<string, double> MediaElapsedMap { get; set; }

    partial void OnMediaElapsedMapChanged(Dictionary<string, double> value)
    {
        if (!_initialized) return;
        SettingsStore.Shared.Set("mediaElapsedSecondsMap", value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatusMessage))]
    public partial string StatusMessage { get; set; }

    partial void OnStatusMessageChanged(string value)
    {
        if (_isSanitizingProperty) return;
        var clean = StripEmoji(value);
        if (clean != value)
        {
            _isSanitizingProperty = true;
            try { StatusMessage = clean; }
            finally { _isSanitizingProperty = false; }
        }
    }

    // ── 多媒体预览与本地历史记录 ──

    /// <summary>Absolute local path of the currently selected media file (video or image).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrentMediaVideo), nameof(IsCurrentMediaImage))]
    public partial string? CurrentMediaURL { get; set; }

    partial void OnCurrentMediaURLChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            CurrentPreviewImage = null;
        }
        else
        {
            var ext = Path.GetExtension(value);
            bool isImage = ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
            CurrentPreviewImage = isImage ? value : null;
        }
        if (_initialized)
        {
            RaiseMediaSelected(value);
        }
    }

    /// <summary>Windows adaptation of Swift <c>currentPreviewImage: NSImage?</c>: non-null local image path when selected media is an image.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrentMediaVideo), nameof(IsCurrentMediaImage))]
    public partial string? CurrentPreviewImage { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<LocalMediaItem> LocalMediaItems { get; set; }

    [ObservableProperty]
    public partial bool ShowColabGuideSheet { get; set; }

    [ObservableProperty]
    public partial int GuideInitialStep { get; set; }

    [ObservableProperty]
    public partial bool ShowDisconnectConfirm { get; set; }

    [ObservableProperty]
    public partial bool KeepaliveProxyWSConnected { get; set; }

    // ── Computed Properties ──

    public string NormalizedBaseURL => NormalizeServerURL(ColabServerURL);

    internal string? NormalizedServerBaseURL()
    {
        var s = NormalizedBaseURL;
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public string DisplayAccountLabel
    {
        get
        {
            if (!CliLoggedIn) return L("未绑定", "Unbound");
            var raw = AccountEmail.Trim();
            if (string.IsNullOrEmpty(raw)) return L("已连接", "Connected");
            int atIdx = raw.IndexOf('@');
            if (MaskServerURL && atIdx >= 0)
            {
                var prefix = raw[..Math.Min(2, atIdx)];
                var domain = raw[atIdx..];
                return $"{prefix}•••{domain}";
            }
            return raw;
        }
    }

    public int ContextLengthTokens => Math.Clamp(ContextLengthK, 4, 256) * 1024;

    public string DisplayGpuName => LocalizeServerText(GpuName);
    public string DisplayCurrentModelName => LocalizeServerText(CurrentModelName);
    public string DisplayComfyStatusMessage => LocalizeServerText(ComfyStatusMessage);
    public string DisplayComfyPersistenceLabel => LocalizeServerText(ComfyPersistenceLabel);
    public string DisplayStatusMessage => LocalizeServerText(StatusMessage);

    public bool IsCurrentMediaImage => !string.IsNullOrEmpty(CurrentPreviewImage);
    public bool IsCurrentMediaVideo => !string.IsNullOrEmpty(CurrentMediaURL) && string.IsNullOrEmpty(CurrentPreviewImage);

    // Telemetry warning / critical thresholds (exact match to Swift MainStudioView.swift)
    public bool IsVramWarning => VramFreeGB < 8.0;
    public bool IsVramCritical => VramFreeGB < 3.0;
    public bool IsRamWarning => RamFreeGB < 10.0;
    public bool IsRamCritical => RamFreeGB < 5.0;
    public bool IsDiskWarning => DiskFreeGB < 20.0;
    public bool IsDiskCritical => DiskFreeGB < 10.0;

    public string LocalSaveDir => LocalRuntimePaths.LocalSaveDir;

    /// <summary>Legacy media folders (excluding <see cref="LocalSaveDir"/>, matching Swift <c>legacySaveDirs</c>).</summary>
    public IReadOnlyList<string> LegacySaveDirs
    {
        get
        {
            var save = LocalSaveDir;
            var parent = Path.GetDirectoryName(save) ?? save;
            return
            [
                Path.Combine(parent, "Aevix"),
                Path.Combine(parent, "Colab_AI_Studio"),
                Path.Combine(parent, "MiniMax_H3_Videos"),
            ];
        }
    }

    public IReadOnlyList<OpenModelSpec> AllAvailableModels
    {
        get
        {
            var list = new List<OpenModelSpec>(OPEN_MODEL_CATALOG.Count + CustomModels.Count);
            list.AddRange(OPEN_MODEL_CATALOG);
            list.AddRange(CustomModels);
            return list;
        }
    }

    public OpenModelSpec ActiveModelSpec =>
        AllAvailableModels.FirstOrDefault(m => m.Id == SelectedModelId) ?? OPEN_MODEL_CATALOG[0];

    public TaskModeItem ActiveModeItem =>
        ActiveModelSpec.Modes.FirstOrDefault(m => m.Id == TaskModeId) ?? ActiveModelSpec.Modes[0];

    public bool IsSelectedModelLoadedInVRAM =>
        LoadedModelIds.Contains(SelectedModelId) || (ModelLoaded && LoadedModelId == SelectedModelId);

    public bool IsMiniMaxH3ImageModel =>
        SelectedModelId == "minimax_h3_image" ||
        ActiveModelSpec.RepoId.Equals("minimaxai/minimax-h3-image", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ResolutionOption> ActiveImageResolutions =>
        IsMiniMaxH3ImageModel ? MINIMAX_H3_IMAGE_RESOLUTIONS : IMAGE_RESOLUTIONS;

    public bool IsQwenLoadedInVRAM =>
        LoadedModelIds.Contains("qwen38_27b")
        || LoadedModelIds.Contains("qwen38_27b_gguf")
        || (ModelLoaded && (LoadedModelId == "qwen38_27b" || LoadedModelId == "qwen38_27b_gguf"))
        || CustomModels.Any(m => m.Modality == ModelModality.LlmVLM && LoadedModelIds.Contains(m.Id));

    public string FormattedUptime
    {
        get
        {
            int h = UptimeSeconds / 3600;
            int m = (UptimeSeconds % 3600) / 60;
            int s = UptimeSeconds % 60;
            return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}", h, m, s);
        }
    }

    public string FormattedIdleRemaining
    {
        get
        {
            if (IdleTimeoutMinutes <= 0 || IdleRemainingSeconds < 0)
                return L("已关闭自动休眠", "Auto-sleep disabled");
            int m = IdleRemainingSeconds / 60;
            int s = IdleRemainingSeconds % 60;
            string sPad = s.ToString("D2", CultureInfo.InvariantCulture);
            return L($"{m}分{sPad}秒后自动释放实例", $"Auto-release in {m}m {sPad}s");
        }
    }

    public double DisplayRemainingCU
    {
        get
        {
            if (OfficialCUBalance is { } official) return official;
            return Math.Max(0.0, TotalBudgetCU - SessionCUUsed);
        }
    }

    public double DisplayBurnRate
    {
        get
        {
            if (OfficialBurnRate is { } rate && (rate > 0 || !IsConnected)) return rate;
            return CuBurnRatePerHour;
        }
    }

    // ── Constructor & Lifecycle ──

    public StudioViewModel()
    {
        var store = SettingsStore.Shared;

        MaskServerURL = store.GetBool("maskServerURL", false);
        var savedUrl = store.GetString("colabServerURL") ?? string.Empty;
        ColabServerURL = savedUrl;
        AdminPassword = SavedAdminPassword(savedUrl);
        HfToken = store.GetString("hfToken") ?? string.Empty;

        CliLoggedIn = false;
        AccountEmail = string.Empty;
        ShowOAuthCodeInput = false;
        OauthCodeText = string.Empty;
        OfficialCUBalance = null;
        OfficialBurnRate = null;
        ActiveVMCount = 0;

        var savedGpu = store.GetString("wakeGPUChoiceV2") ?? "G4";
        WakeGPUChoice = ValidWakeGPUs.Contains(savedGpu) ? savedGpu : "G4";
        IsWakingColab = false;

        IsConnected = false;
        GpuName = L(DefaultGpuNameZh, DefaultGpuNameEn);
        VramHistoryPct = [];
        RamHistoryPct = [];
        DiskHistoryPct = [];
        CombinedTelemetryChartMode = false;

        SelectedModelId = "minimax_h3";
        CustomModels = [];
        ShowAddCustomModelSheet = false;
        CustomRepoInput = string.Empty;
        CustomIncludePatternInput = string.Empty;
        CustomDisplayNameInput = string.Empty;
        CustomModalityInput = "auto";
        IsRegisteringCustomModel = false;
        CustomModelErrorMessage = string.Empty;
        TrendingHFModels = [];
        TrendingHFCategory = "all";
        IsLoadingTrendingHF = false;
        ActiveHFModelDetail = null;
        IsLoadingHFModelDetail = false;
        ShowHFModelDetailOverlay = false;

        LoadedModelId = null;
        LoadedModelIds = [];
        LoadedModelsDetail = [];
        ModelLoaded = false;
        IsUnloadingModel = false;
        CurrentModelName = L(DefaultModelNameZh, DefaultModelNameEn);

        var savedBudget = store.GetDouble("totalBudgetCU", 0);
        TotalBudgetCU = savedBudget > 0 ? savedBudget : 2000.0;
        UptimeSeconds = 0;
        CuBurnRatePerHour = 0.0;
        SessionCUUsed = 0.0;

        if (!store.Contains("idleTimeoutMinutes"))
        {
            IdleTimeoutMinutes = 30;
        }
        else
        {
            var savedIdle = store.GetInt("idleTimeoutMinutes", 30);
            IdleTimeoutMinutes = ValidIdleMinutes.Contains(savedIdle) ? savedIdle : 30;
        }
        IdleRemainingSeconds = 1800;
        DrivePersisted = false;
        Bf16ShardsCount = 0;

        var welcome = LanguageManager.Shared.IsEN ? DefaultWelcomeLinesEn : DefaultWelcomeLinesZh;
        LiveConsoleLines = new ObservableCollection<string>(welcome);

        ModelFiles = OPEN_MODEL_CATALOG[0].DefaultFiles.ToList();
        DownloadedCount = 0;
        TotalFileCount = OPEN_MODEL_CATALOG[0].DefaultFiles.Count;
        AllDownloaded = false;
        TotalDiskSizeGB = 0.0;
        IsDownloadingWeights = false;
        IsLoadingModel = false;

        ComfyRunning = false;
        ComfyStarting = false;
        ComfySyncingDrive = false;
        ComfyDownloadingWeight = false;
        ComfyInstallingPackId = string.Empty;
        ComfyPresetPacks = DEFAULT_COMFY_PRESET_PACKS.ToList();
        ComfyProgress = 0.0;
        ComfyStatusMessage = L(DefaultComfyStatusZh, DefaultComfyStatusEn);
        ComfyPublicURL = string.Empty;
        ComfyQueueRunning = 0;
        ComfyQueuePending = 0;
        ComfyPersistenceLabel = L(DefaultComfyPersistZh, DefaultComfyPersistEn);
        ComfyDrivePersisted = false;
        ComfyPersistRoot = "/content/drive/MyDrive/EasyColab_ComfyUI";
        ComfyCustomNodesCount = 0;
        ComfyModelsCount = 0;
        ComfyWorkflowsCount = 0;
        ComfyRecentOutputs = [];
        ComfyUnloadOthersOnStart = false;
        ComfyWeightSourceInput = string.Empty;
        ComfyWeightFilenameInput = string.Empty;
        ComfyWeightSubfolder = "diffusion_models";
        ComfyPullingOutputRelPath = null;
        ModelTaskProgress = 0.0;

        SelectedPrecision = "bf16";
        SelectedOffload = "disk";

        TaskModeId = "t2va";
        VisualPrompt = PRESET_TEMPLATES[0].Visual;
        NegativePrompt = string.Empty;
        SoundscapePrompt = PRESET_TEMPLATES[0].Soundscape;
        MusicPrompt = PRESET_TEMPLATES[0].Music;
        FirstFrameImage = null;
        LastFrameImage = null;
        SelectedVideoResolution = VIDEO_RESOLUTIONS[0];
        SelectedImageResolution = IMAGE_RESOLUTIONS[0];
        NumFrames = 124;
        InferenceSteps = 35;
        Fps = 24;
        Seed = 42;
        RandomizeSeed = true;
        BatchCount = 1;
        DenoisingStrength = 0.70;

        var savedCtx = store.GetInt("qwenContextLengthK", 0);
        ContextLengthK = (savedCtx >= 4 && savedCtx <= 256) ? savedCtx : 32;
        LoadedContextLengthTokens = 0;

        EnableThinking = true;
        var savedEffort = store.GetString("reasoningEffort") ?? "medium";
        ReasoningEffort = ValidReasoningEfforts.Contains(savedEffort) ? savedEffort : "medium";
        MaxNewTokens = 16384;
        Temperature = 1.0;
        TopP = 0.95;
        PresencePenalty = 0.0;
        ChatHistory = new ObservableCollection<ChatTurnItem>();

        var savedKey = store.GetString("apiAccessKey");
        if (!string.IsNullOrEmpty(savedKey))
        {
            ApiAccessKey = savedKey;
        }
        else
        {
            var generated = "sk-colab-" + Guid.NewGuid().ToString("N")[..20].ToLowerInvariant();
            store.Set("apiAccessKey", generated);
            ApiAccessKey = generated;
        }
        ApiRequestCount = 0;
        RemoteApiKeySynced = false;

        IsGenerating = false;
        ProgressValue = 0.0;
        CurrentJobElapsed = 0.0;
        LastCompletedElapsed = null;
        MediaElapsedMap = store.Get<Dictionary<string, double>>("mediaElapsedSecondsMap")
            ?? new Dictionary<string, double>(StringComparer.Ordinal);
        StatusMessage = L(DefaultReadyStatusZh, DefaultReadyStatusEn);

        CurrentMediaURL = null;
        CurrentPreviewImage = null;
        LocalMediaItems = new ObservableCollection<LocalMediaItem>();
        ShowColabGuideSheet = false;
        GuideInitialStep = 0;
        ShowDisconnectConfirm = false;
        KeepaliveProxyWSConnected = false;

        _initialized = true;

        LanguageManager.Shared.LanguageChanged += OnLanguageChanged;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        LoadPersistedCustomModels();
        RefreshLocalMedia();
        StartBackgroundTelemetryLoop();
        _ = RefreshOfficialColabAccountUsageAsync();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UiDispatcher.Run(() =>
        {
            SyncLocalizedDynamicTexts();
            OnPropertyChanged(string.Empty);
        });
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        Shutdown();
    }

    /// <summary>Record local user input (pointer / keyboard) to keep the remote idle watchdog alive.</summary>
    public void RecordLocalInteraction()
    {
        LastLocalInteractionAt = DateTime.UtcNow;
    }

    /// <summary>Cleanly stops child processes, timers, sleep assertion, and flushes settings on window close / exit.</summary>
    public void Shutdown()
    {
        try
        {
            TelemetryLoopCts?.Cancel();
            PollingCts?.Cancel();
            StopLiveGenerationTimer();
            StopActiveWakeProcess();
            StopKeepaliveDaemon();
            if (_holdingActivityAssertion)
            {
                SetThreadExecutionState(ES_CONTINUOUS);
                _holdingActivityAssertion = false;
            }
            SettingsStore.Shared.Flush();
        }
        catch
        {
            // Best-effort cleanup on shutdown.
        }
    }

    // ── URL Normalization, Masking & Admin HTTP Helpers ──

    public string MaskedURLDisplay(string rawURL)
    {
        var trimmed = (rawURL ?? string.Empty).Trim();
        if (!MaskServerURL || string.IsNullOrEmpty(trimmed)) return trimmed;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Scheme) && !string.IsNullOrEmpty(u.Host))
        {
            var parts = u.Host.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var suffix = parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : u.Host;
            return $"{u.Scheme}://••••••••••••.{suffix}";
        }
        return "https://••••••••••••";
    }

    public static string NormalizeServerURL(string rawURL)
    {
        var raw = (rawURL ?? string.Empty).Trim();
        while (raw.EndsWith('/'))
            raw = raw[..^1];
        return raw;
    }

    private static string SavedAdminPassword(string address)
    {
        var normalized = NormalizeServerURL(address);
        if (string.IsNullOrEmpty(normalized)) return string.Empty;
        var passwords = SettingsStore.Shared.Get<Dictionary<string, string>>("adminPasswordsByURL");
        if (passwords is null) return string.Empty;
        if (passwords.TryGetValue(normalized, out var p) && p is not null) return p;
        if (passwords.TryGetValue(address, out var raw) && raw is not null) return raw;
        return string.Empty;
    }

    internal HttpRequestMessage AdminRequest(string url, HttpMethod? method = null, string? jsonBody = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Invalid admin request URL: {url}");
        return AdminRequest(uri, method, jsonBody);
    }

    internal HttpRequestMessage AdminRequest(Uri uri, HttpMethod? method = null, string? jsonBody = null)
    {
        Uri.TryCreate(NormalizedBaseURL, UriKind.Absolute, out var origin);
        if (!SameOriginAdminRedirect.Permits(uri, origin))
            throw new InvalidOperationException("Admin request target does not match the connected HTTPS origin.");
        if (string.IsNullOrEmpty(AdminPassword))
            throw new InvalidOperationException(L("请先输入管理密码或使用一键唤醒部署。", "Authentication required: enter management password or use Wake & Deploy."));

        var req = new HttpRequestMessage(method ?? HttpMethod.Get, uri);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"easycolab:{AdminPassword}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (jsonBody is not null)
        {
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }
        return req;
    }

    internal static async Task<HttpResponseMessage> SendAdminAsync(
        HttpRequestMessage request,
        TimeSpan timeout,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        return await AdminHttpClient.SendAsync(request, completionOption, cts.Token);
    }

    // ── Language & Onboarding Helpers ──

    public void ToggleLanguage()
    {
        LanguageManager.Shared.Toggle();
    }

    public void SyncLocalizedDynamicTexts()
    {
        bool isEN = LanguageManager.Shared.IsEN;
        if (GpuName == DefaultGpuNameZh || GpuName == DefaultGpuNameEn)
            GpuName = isEN ? DefaultGpuNameEn : DefaultGpuNameZh;
        if (CurrentModelName == DefaultModelNameZh || CurrentModelName == DefaultModelNameEn)
            CurrentModelName = isEN ? DefaultModelNameEn : DefaultModelNameZh;
        if (ComfyStatusMessage == DefaultComfyStatusZh || ComfyStatusMessage == DefaultComfyStatusEn)
            ComfyStatusMessage = isEN ? DefaultComfyStatusEn : DefaultComfyStatusZh;
        if (ComfyPersistenceLabel == DefaultComfyPersistZh || ComfyPersistenceLabel == DefaultComfyPersistEn)
            ComfyPersistenceLabel = isEN ? DefaultComfyPersistEn : DefaultComfyPersistZh;
        if (StatusMessage == DefaultReadyStatusZh || StatusMessage == DefaultReadyStatusEn)
            StatusMessage = isEN ? DefaultReadyStatusEn : DefaultReadyStatusZh;

        if (LiveConsoleLines.SequenceEqual(DefaultWelcomeLinesZh) || LiveConsoleLines.SequenceEqual(DefaultWelcomeLinesEn))
        {
            ReplaceConsoleLines(isEN ? DefaultWelcomeLinesEn : DefaultWelcomeLinesZh);
        }
        else if (LiveConsoleLines.Count == 1 &&
                 (LiveConsoleLines[0] == ClearedConsoleLineZh || LiveConsoleLines[0] == ClearedConsoleLineEn))
        {
            ReplaceConsoleLines([isEN ? ClearedConsoleLineEn : ClearedConsoleLineZh]);
        }

        foreach (var tpl in PRESET_TEMPLATES)
        {
            if (tpl.VisualZh != tpl.VisualEn && (VisualPrompt == tpl.VisualZh || VisualPrompt == tpl.VisualEn))
            {
                VisualPrompt = tpl.Visual;
                break;
            }
        }
    }

    public void OpenOnboardingGuide(int step = 0)
    {
        GuideInitialStep = Math.Clamp(step, 0, 3);
        ShowColabGuideSheet = true;
    }

    // ── Console Logging Helpers ──

    public void AppendConsoleLine(string line)
    {
        var clean = StripEmoji(line);
        if (string.IsNullOrEmpty(clean)) return;
        UiDispatcher.Run(() =>
        {
            LiveConsoleLines.Add(clean);
            while (LiveConsoleLines.Count > MaxLocalConsoleLines)
            {
                LiveConsoleLines.RemoveAt(0);
            }
        });
    }

    public void Log(string line) => AppendConsoleLine(line);

    public void ReplaceConsoleLines(IEnumerable<string> lines)
    {
        var cleaned = lines.Select(StripEmoji).ToList();
        UiDispatcher.Run(() =>
        {
            if (LiveConsoleLines.Count == cleaned.Count && LiveConsoleLines.SequenceEqual(cleaned))
                return;
            LiveConsoleLines.Clear();
            foreach (var item in cleaned)
            {
                LiveConsoleLines.Add(item);
            }
        });
    }

    public void ClearConsoleLines()
    {
        ReplaceConsoleLines([L(ClearedConsoleLineZh, ClearedConsoleLineEn)]);
    }

    public void StripConsoleProgressBars()
    {
        UiDispatcher.Run(() =>
        {
            int sepIdx = -1;
            for (int i = 0; i < LiveConsoleLines.Count; i++)
            {
                if (LiveConsoleLines[i].Contains("实时下载/采样进度流", StringComparison.Ordinal))
                {
                    sepIdx = i;
                    break;
                }
            }
            if (sepIdx >= 0)
            {
                while (LiveConsoleLines.Count > sepIdx)
                {
                    LiveConsoleLines.RemoveAt(LiveConsoleLines.Count - 1);
                }
            }
        });
    }

    // ── Windows Sleep Prevention & Telemetry ──

    internal void SyncRuntimeActivityAssertion()
    {
        bool shouldHold = IsConnected || IsWakingColab || IsGenerating || IsDownloadingWeights || IsLoadingModel;
        try
        {
            if (shouldHold && !_holdingActivityAssertion)
            {
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
                _holdingActivityAssertion = true;
            }
            else if (!shouldHold && _holdingActivityAssertion)
            {
                SetThreadExecutionState(ES_CONTINUOUS);
                _holdingActivityAssertion = false;
            }
        }
        catch
        {
            // Ignore in non-Win32/restricted environments.
        }
    }

    /// <summary>Alias matching Swift <c>syncMacOSRuntimeActivityAssertion()</c> for mechanical cross-file calls.</summary>
    internal void SyncMacOSRuntimeActivityAssertion() => SyncRuntimeActivityAssertion();

    public void RecordTelemetrySample(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - LastTelemetrySampleDate).TotalSeconds < 0.85)
            return;
        LastTelemetrySampleDate = now;

        if (VramTotalGB > 0)
            VramHistoryPct = AppendTelemetryPct(VramHistoryPct, VramUsedGB / VramTotalGB);
        if (RamTotalGB > 0)
            RamHistoryPct = AppendTelemetryPct(RamHistoryPct, RamUsedGB / RamTotalGB);
        if (DiskTotalGB > 0)
            DiskHistoryPct = AppendTelemetryPct(DiskHistoryPct, DiskUsedGB / DiskTotalGB);
    }

    private static List<double> AppendTelemetryPct(List<double> current, double rawPct)
    {
        double pct = Math.Clamp(rawPct, 0.0, 1.0);
        if (current.Count == 0)
            return Enumerable.Repeat(pct, 14).ToList();

        var next = new List<double>(Math.Min(current.Count + 1, MaxTelemetrySamples));
        int skip = Math.Max(0, current.Count + 1 - MaxTelemetrySamples);
        for (int i = skip; i < current.Count; i++)
            next.Add(current[i]);
        next.Add(pct);
        return next;
    }

    public void ApplyLiveTelemetrySnapshot(LiveTelemetrySnapshot snap)
    {
        if (IgnoreColabRuntimeStatus) return;

        if (!string.IsNullOrEmpty(snap.GpuName)) GpuName = snap.GpuName;
        if (snap.VramFreeGb is { } vf) VramFreeGB = vf;
        if (snap.VramTotalGb is { } vt) VramTotalGB = vt;
        if (snap.VramUsedGb is { } vu) VramUsedGB = vu;
        if (snap.CuBurnRatePerHour is { } cr) CuBurnRatePerHour = cr;
        if (snap.RamTotalGb is { } rt && rt > 0) RamTotalGB = rt;
        if (snap.RamUsedGb is { } ru) RamUsedGB = ru;
        if (snap.RamFreeGb is { } rf) RamFreeGB = rf;
        if (snap.DiskTotalGb is { } dt && dt > 0) DiskTotalGB = dt;
        if (snap.DiskUsedGb is { } du) DiskUsedGB = du;
        if (snap.DiskFreeGb is { } df) DiskFreeGB = df;
        if (snap.ShmTotalGb is { } st) ShmTotalGB = st;
        if (snap.ShmUsedGb is { } su) ShmUsedGB = su;
        if (snap.ShmFreeGb is { } sf) ShmFreeGB = sf;
        if (snap.DriveTotalGb is { } gt && gt > 0) DriveTotalGB = gt;
        if (snap.DriveUsedGb is { } gu) DriveUsedGB = gu;
        if (snap.DriveFreeGb is { } gf) DriveFreeGB = gf;
        RecordTelemetrySample();
    }

    public double? ElapsedForMedia(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var name = Path.GetFileName(path);
        return MediaElapsedMap.TryGetValue(name, out var val) ? val : null;
    }

    public void SetMediaElapsed(string filename, double elapsedSeconds)
    {
        if (string.IsNullOrEmpty(filename) || elapsedSeconds <= 0) return;
        MediaElapsedMap[filename] = elapsedSeconds;
        SettingsStore.Shared.Set("mediaElapsedSecondsMap", MediaElapsedMap);
        OnPropertyChanged(nameof(MediaElapsedMap));
    }

    // ── Generation Elapsed Timer ──

    public void StartLiveGenerationTimer()
    {
        StopLiveGenerationTimer();
        var cts = new CancellationTokenSource();
        GenerationTimerCts = cts;
        GenerationStartedAt = DateTime.UtcNow;
        CurrentJobElapsed = 0.0;

        _ = RunGenerationTimerAsync(cts.Token);
    }

    private async Task RunGenerationTimerAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(200, ct);
                if (ct.IsCancellationRequested || !IsGenerating || GenerationStartedAt is not { } started)
                    break;
                double localElapsed = (DateTime.UtcNow - started).TotalSeconds;
                if (localElapsed > CurrentJobElapsed)
                {
                    CurrentJobElapsed = localElapsed;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
    }

    public void StopLiveGenerationTimer()
    {
        try { GenerationTimerCts?.Cancel(); } catch { }
        GenerationTimerCts?.Dispose();
        GenerationTimerCts = null;
        GenerationStartedAt = null;
    }

    // ── Remote State Reset & Local Record Clearing ──

    public void ResetStaleRemoteRuntimeState(string reason)
    {
        bool hadActiveState = IsConnected
            || LoadedModelsDetail.Count > 0
            || LoadedModelIds.Count > 0
            || ModelLoaded
            || IsGenerating
            || IsLoadingModel
            || IsDownloadingWeights
            || VramTotalGB > 0;

        StopKeepaliveDaemon();
        StopLiveGenerationTimer();
        IsConnected = false;
        ModelLoaded = false;
        LoadedModelId = null;
        LoadedModelIds = [];
        LoadedModelsDetail = [];
        IsGenerating = false;
        IsLoadingModel = false;
        IsDownloadingWeights = false;
        IsUnloadingModel = false;
        ComfyRunning = false;
        ComfyStarting = false;
        ComfySyncingDrive = false;
        ComfyDownloadingWeight = false;
        ComfyInstallingPackId = string.Empty;
        ComfyPublicURL = string.Empty;
        ComfyQueueRunning = 0;
        ComfyQueuePending = 0;
        ComfyProgress = 0.0;
        ComfyStatusMessage = string.Empty;
        ProgressValue = 0.0;
        ModelTaskProgress = 0.0;
        VramUsedGB = 0.0;
        VramFreeGB = 0.0;
        VramTotalGB = 0.0;
        RamTotalGB = 0.0;
        RamUsedGB = 0.0;
        RamFreeGB = 0.0;
        DiskTotalGB = 0.0;
        DiskUsedGB = 0.0;
        DiskFreeGB = 0.0;
        ShmTotalGB = 0.0;
        ShmUsedGB = 0.0;
        ShmFreeGB = 0.0;
        DriveTotalGB = 0.0;
        DriveUsedGB = 0.0;
        DriveFreeGB = 0.0;
        VramHistoryPct = [];
        RamHistoryPct = [];
        DiskHistoryPct = [];
        LastTelemetrySampleDate = DateTime.MinValue;
        UptimeSeconds = 0;
        CuBurnRatePerHour = 0.0;
        ConsecutiveStatusFailures = 0;
        LoadedContextLengthTokens = 0;
        RemoteContextSynced = false;
        RemoteIdleSynced = false;
        RemoteApiKeySynced = false;
        LastIdleSyncAttemptAt = DateTime.MinValue;
        LastContextSyncAttemptAt = DateTime.MinValue;
        SyncRuntimeActivityAssertion();
        GpuName = L(DefaultGpuNameZh, DefaultGpuNameEn);

        if (hadActiveState && !string.IsNullOrEmpty(reason))
        {
            StatusMessage = reason;
        }
    }

    public void ClearLocalLoadedModelRecord(string modelId)
    {
        if (modelId == "all")
        {
            LoadedModelIds = [];
            LoadedModelsDetail = [];
            LoadedModelId = null;
            ModelLoaded = false;
            LoadedContextLengthTokens = 0;
        }
        else
        {
            LoadedModelIds = LoadedModelIds.Where(id => id != modelId).ToList();
            LoadedModelsDetail = LoadedModelsDetail.Where(d => d.Id != modelId).ToList();
            if (LoadedModelId == modelId)
            {
                LoadedModelId = LoadedModelIds.FirstOrDefault();
            }
            ModelLoaded = LoadedModelIds.Contains(SelectedModelId);
            if (!IsQwenLoadedInVRAM)
            {
                LoadedContextLengthTokens = 0;
            }
        }
    }

    // ── Image Encoding Helper (Windows WIC JPEG 0.92) ──

    public Task<string?> EncodeImageToBase64Async(string? path) =>
        EasyColab.Controls.ImageFileHelpers.EncodeImageFileToJpegBase64Async(path, 0.92);
}
