using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyColab.Models;

// MARK: - 网络协议结构

/// <summary>
/// Shared System.Text.Json options matching Swift <c>JSONDecoder()</c> / <c>JSONEncoder()</c> defaults:
/// exact (case-sensitive) keys from <c>[JsonPropertyName]</c>, nil optionals omitted when encoding.
/// </summary>
public static class ModelJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static T? Decode<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    public static T? Decode<T>(ReadOnlySpan<byte> utf8Json) => JsonSerializer.Deserialize<T>(utf8Json, Options);
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Decode returning null instead of throwing (Swift <c>try? JSONDecoder().decode(...)</c>).</summary>
    public static T? TryDecode<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Options); }
        catch { return null; }
    }
}

public sealed record LoadedModelSummary
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("mode")] public string? Mode { get; init; }
    [JsonPropertyName("vram_gb")] public double? VramGb { get; init; }
    [JsonPropertyName("context_length")] public int? ContextLength { get; init; }

    [JsonIgnore]
    public string LocalizedName
    {
        get
        {
            var spec = Catalogs.OPEN_MODEL_CATALOG.FirstOrDefault(s => s.Id == Id);
            if (spec is not null) return spec.Name;
            return LocalizeServerText(Name);
        }
    }
}

public sealed record ColabStatusResponse
{
    [JsonPropertyName("ok")] public bool? Ok { get; init; }
    [JsonPropertyName("cuda_available")] public bool? CudaAvailable { get; init; }
    [JsonPropertyName("gpu_name")] public string? GpuName { get; init; }
    [JsonPropertyName("vram_free_gb")] public double? VramFreeGb { get; init; }
    [JsonPropertyName("vram_total_gb")] public double? VramTotalGb { get; init; }
    [JsonPropertyName("vram_used_gb")] public double? VramUsedGb { get; init; }
    [JsonPropertyName("model_loaded")] public bool? ModelLoaded { get; init; }
    [JsonPropertyName("any_model_loaded")] public bool? AnyModelLoaded { get; init; }
    [JsonPropertyName("selected_model_id")] public string? SelectedModelId { get; init; }
    [JsonPropertyName("loaded_model_id")] public string? LoadedModelId { get; init; }
    [JsonPropertyName("loaded_model_ids")] public List<string>? LoadedModelIds { get; init; }
    [JsonPropertyName("loaded_models_detail")] public List<LoadedModelSummary>? LoadedModelsDetail { get; init; }
    [JsonPropertyName("current_model")] public string? CurrentModel { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("context_length")] public int? ContextLength { get; init; }
    [JsonPropertyName("loaded_context_length")] public int? LoadedContextLength { get; init; }

    [JsonPropertyName("files")] public List<ModelFileItem>? Files { get; init; }
    [JsonPropertyName("downloaded_count")] public int? DownloadedCount { get; init; }
    [JsonPropertyName("total_count")] public int? TotalCount { get; init; }
    [JsonPropertyName("all_downloaded")] public bool? AllDownloaded { get; init; }
    [JsonPropertyName("total_size_gb")] public double? TotalSizeGb { get; init; }
    [JsonPropertyName("drive_persisted")] public bool? DrivePersisted { get; init; }
    [JsonPropertyName("bf16_shards_count")] public int? Bf16ShardsCount { get; init; }
    [JsonPropertyName("is_downloading")] public bool? IsDownloading { get; init; }
    [JsonPropertyName("is_loading_vram")] public bool? IsLoadingVram { get; init; }
    [JsonPropertyName("task_progress")] public double? TaskProgress { get; init; }
    [JsonPropertyName("task_message")] public string? TaskMessage { get; init; }

    [JsonPropertyName("uptime_seconds")] public int? UptimeSeconds { get; init; }
    [JsonPropertyName("cu_burn_rate_per_hour")] public double? CuBurnRatePerHour { get; init; }
    [JsonPropertyName("session_cu_used")] public double? SessionCuUsed { get; init; }
    [JsonPropertyName("idle_timeout_minutes")] public int? IdleTimeoutMinutes { get; init; }
    [JsonPropertyName("idle_remaining_seconds")] public int? IdleRemainingSeconds { get; init; }
    [JsonPropertyName("api_key_enabled")] public bool? ApiKeyEnabled { get; init; }
    [JsonPropertyName("api_key_configured")] public bool? ApiKeyConfigured { get; init; }
    [JsonPropertyName("api_request_count")] public int? ApiRequestCount { get; init; }
    [JsonPropertyName("console_lines")] public List<string>? ConsoleLines { get; init; }

    [JsonPropertyName("ram_total_gb")] public double? RamTotalGb { get; init; }
    [JsonPropertyName("ram_used_gb")] public double? RamUsedGb { get; init; }
    [JsonPropertyName("ram_free_gb")] public double? RamFreeGb { get; init; }
    [JsonPropertyName("disk_total_gb")] public double? DiskTotalGb { get; init; }
    [JsonPropertyName("disk_used_gb")] public double? DiskUsedGb { get; init; }
    [JsonPropertyName("disk_free_gb")] public double? DiskFreeGb { get; init; }
    [JsonPropertyName("shm_total_gb")] public double? ShmTotalGb { get; init; }
    [JsonPropertyName("shm_used_gb")] public double? ShmUsedGb { get; init; }
    [JsonPropertyName("shm_free_gb")] public double? ShmFreeGb { get; init; }
    [JsonPropertyName("drive_total_gb")] public double? DriveTotalGb { get; init; }
    [JsonPropertyName("drive_used_gb")] public double? DriveUsedGb { get; init; }
    [JsonPropertyName("drive_free_gb")] public double? DriveFreeGb { get; init; }
    [JsonPropertyName("comfyui")] public ComfyRemoteStatus? Comfyui { get; init; }
    [JsonPropertyName("custom_models")] public List<CustomHFModelDTO>? CustomModels { get; init; }
    [JsonPropertyName("registered_model")] public CustomHFModelDTO? RegisteredModel { get; init; }
    [JsonPropertyName("action_ok")] public bool? ActionOk { get; init; }
    [JsonPropertyName("action_message")] public string? ActionMessage { get; init; }
}

public sealed record ComfyOutputMediaItem
{
    [JsonIgnore] public string Id => RelPath;
    [JsonPropertyName("rel_path")] public required string RelPath { get; init; }
    [JsonPropertyName("filename")] public required string Filename { get; init; }
    [JsonPropertyName("media_type")] public string? MediaType { get; init; }
    [JsonPropertyName("size_mb")] public double? SizeMb { get; init; }
    [JsonPropertyName("mtime")] public long? Mtime { get; init; }
}

public sealed record ComfyPresetPackItem
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("badge")] public required string Badge { get; init; }
    [JsonPropertyName("scenario")] public required string Scenario { get; init; }
    [JsonPropertyName("approx_size_gb")] public required double ApproxSizeGb { get; init; }
    [JsonPropertyName("workflows")] public required List<string> Workflows { get; init; }
    [JsonPropertyName("installed_models")] public required int InstalledModels { get; init; }
    [JsonPropertyName("total_models")] public required int TotalModels { get; init; }
    [JsonPropertyName("workflows_installed")] public required bool WorkflowsInstalled { get; init; }
    [JsonPropertyName("ready")] public required bool Ready { get; init; }

    [JsonIgnore]
    public string LocalizedTitle
    {
        get
        {
            if (!LanguageManager.Shared.IsEN) return Title;
            return Id switch
            {
                "wan22_i2v_flf2v" => "Wan2.2 14B · Image-to-Video + First/Last Frame Dual Workflows",
                "minimax_h3_i2v_flf2v" => "MiniMax-H3 Turbo · Image-to-Video+Audio + First/Last Frame",
                "ltx23_flf2v" => "LTX-2.3 (22B) · Cinematic Camera Motion First/Last Frame + Audio",
                "hunyuan15_i2v_1080p" => "HunyuanVideo 1.5 · Single-Image Direct 1080P Super-Res Workflow",
                _ => LocalizeServerText(Title),
            };
        }
    }

    [JsonIgnore]
    public string LocalizedBadge
    {
        get
        {
            if (!LanguageManager.Shared.IsEN) return Badge;
            return Id switch
            {
                "wan22_i2v_flf2v" => "Beginner Pick · 4-Step Fast",
                "minimax_h3_i2v_flf2v" => "Stereo Audio · 8-Step Turbo",
                "ltx23_flf2v" => "Cinematic Camera · Only 2 Weights",
                "hunyuan15_i2v_1080p" => "720P + 1080P Dual Output",
                _ => LocalizeServerText(Badge),
            };
        }
    }

    [JsonIgnore]
    public string LocalizedScenario
    {
        get
        {
            if (!LanguageManager.Shared.IsEN) return Scenario;
            return Id switch
            {
                "wan22_i2v_flf2v" => "Includes 'Single Image to Video' & 'First/Last Frame Smooth Transition' templates (~25s on G4, 100% built-in nodes, zero missing-node errors)",
                "minimax_h3_i2v_flf2v" => "Includes single-image and first/last-frame templates generating HD video with synchronized stereo SFX, dialogue & music in one pass",
                "ltx23_flf2v" => "Optimized for dramatic camera moves between start & end keyframes (e.g. aerial crane dive to low-angle close-up + synthesized soundtrack)",
                "hunyuan15_i2v_1080p" => "Latent super-resolution pipeline enabled by default — upload 1 image to render both 720P base video and 1920×1080 Full HD output",
                _ => LocalizeServerText(Scenario),
            };
        }
    }

    private static readonly Dictionary<string, string> WorkflowLabelMap = new()
    {
        ["01_Wan2.2_极速单图生视频(4步傻瓜版)"] = "01_Wan2.2_Fast_Single_I2V_4Step",
        ["02_Wan2.2_首尾帧丝滑过渡(双图4步极速)"] = "02_Wan2.2_FirstLastFrame_4Step",
        ["03_MiniMax-H3_极速图生有声视频(单图带立体声)"] = "03_MiniMax-H3_Fast_I2VA_Stereo",
        ["04_MiniMax-H3_首尾帧有声过渡(双图带立体声)"] = "04_MiniMax-H3_FirstLastFrame_Stereo",
        ["05_LTX-2.3_首尾帧电影运镜(双图极速带音效)"] = "05_LTX-2.3_Cinematic_FLF2V_Audio",
        ["06_Hunyuan1.5_单图直出1080P超清视频"] = "06_Hunyuan1.5_Direct_1080P_I2V",
    };

    public static string LocalizedWorkflowLabel(string rawName)
    {
        string stripped = rawName.Replace(".json", "", StringComparison.Ordinal);
        if (!LanguageManager.Shared.IsEN) return stripped;
        return WorkflowLabelMap.TryGetValue(stripped, out var en) ? en : stripped;
    }
}

public sealed record ComfyRemoteStatus
{
    [JsonPropertyName("is_running")] public bool? IsRunning { get; init; }
    [JsonPropertyName("is_starting")] public bool? IsStarting { get; init; }
    [JsonPropertyName("is_syncing_drive")] public bool? IsSyncingDrive { get; init; }
    [JsonPropertyName("is_downloading_weight")] public bool? IsDownloadingWeight { get; init; }
    [JsonPropertyName("installing_pack_id")] public string? InstallingPackId { get; init; }
    [JsonPropertyName("progress")] public double? Progress { get; init; }
    [JsonPropertyName("status_message")] public string? StatusMessage { get; init; }
    [JsonPropertyName("public_url")] public string? PublicUrl { get; init; }
    [JsonPropertyName("local_port")] public int? LocalPort { get; init; }
    [JsonPropertyName("queue_running")] public int? QueueRunning { get; init; }
    [JsonPropertyName("queue_pending")] public int? QueuePending { get; init; }
    [JsonPropertyName("persistence_mode")] public string? PersistenceMode { get; init; }
    [JsonPropertyName("persistence_label")] public string? PersistenceLabel { get; init; }
    [JsonPropertyName("drive_persisted")] public bool? DrivePersisted { get; init; }
    [JsonPropertyName("persist_root")] public string? PersistRoot { get; init; }
    [JsonPropertyName("custom_nodes_count")] public int? CustomNodesCount { get; init; }
    [JsonPropertyName("models_count")] public int? ModelsCount { get; init; }
    [JsonPropertyName("workflows_count")] public int? WorkflowsCount { get; init; }
    [JsonPropertyName("last_sync_ts")] public long? LastSyncTs { get; init; }
    [JsonPropertyName("recent_outputs")] public List<ComfyOutputMediaItem>? RecentOutputs { get; init; }
    [JsonPropertyName("preset_packs")] public List<ComfyPresetPackItem>? PresetPacks { get; init; }
}

public sealed record CompletedMediaItem
{
    [JsonPropertyName("index")] public int? Index { get; init; }
    [JsonPropertyName("seed")] public long? Seed { get; init; }
    [JsonPropertyName("filename")] public required string Filename { get; init; }
    [JsonPropertyName("video_url")] public required string VideoUrl { get; init; }
    [JsonPropertyName("media_type")] public string? MediaType { get; init; }
    [JsonPropertyName("elapsed")] public double? Elapsed { get; init; }
}

public sealed record LiveTelemetrySnapshot
{
    [JsonPropertyName("cuda_available")] public bool? CudaAvailable { get; init; }
    [JsonPropertyName("gpu_name")] public string? GpuName { get; init; }
    [JsonPropertyName("vram_free_gb")] public double? VramFreeGb { get; init; }
    [JsonPropertyName("vram_total_gb")] public double? VramTotalGb { get; init; }
    [JsonPropertyName("vram_used_gb")] public double? VramUsedGb { get; init; }
    [JsonPropertyName("cu_burn_rate_per_hour")] public double? CuBurnRatePerHour { get; init; }
    [JsonPropertyName("ram_total_gb")] public double? RamTotalGb { get; init; }
    [JsonPropertyName("ram_used_gb")] public double? RamUsedGb { get; init; }
    [JsonPropertyName("ram_free_gb")] public double? RamFreeGb { get; init; }
    [JsonPropertyName("disk_total_gb")] public double? DiskTotalGb { get; init; }
    [JsonPropertyName("disk_used_gb")] public double? DiskUsedGb { get; init; }
    [JsonPropertyName("disk_free_gb")] public double? DiskFreeGb { get; init; }
    [JsonPropertyName("shm_total_gb")] public double? ShmTotalGb { get; init; }
    [JsonPropertyName("shm_used_gb")] public double? ShmUsedGb { get; init; }
    [JsonPropertyName("shm_free_gb")] public double? ShmFreeGb { get; init; }
    [JsonPropertyName("drive_total_gb")] public double? DriveTotalGb { get; init; }
    [JsonPropertyName("drive_used_gb")] public double? DriveUsedGb { get; init; }
    [JsonPropertyName("drive_free_gb")] public double? DriveFreeGb { get; init; }
}

public sealed record ColabJobResponse
{
    [JsonPropertyName("job_id")] public required string JobId { get; init; }
    [JsonPropertyName("model_id")] public string? ModelId { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("progress")] public required double Progress { get; init; }
    [JsonPropertyName("message")] public required string Message { get; init; }
    [JsonPropertyName("seed")] public long? Seed { get; init; }
    [JsonPropertyName("elapsed")] public double? Elapsed { get; init; }
    [JsonPropertyName("filename")] public string? Filename { get; init; }
    [JsonPropertyName("video_url")] public string? VideoUrl { get; init; }
    [JsonPropertyName("media_type")] public string? MediaType { get; init; }
    [JsonPropertyName("text_output")] public string? TextOutput { get; init; }
    [JsonPropertyName("thinking_output")] public string? ThinkingOutput { get; init; }
    [JsonPropertyName("batch_count")] public int? BatchCount { get; init; }
    [JsonPropertyName("completed_count")] public int? CompletedCount { get; init; }
    [JsonPropertyName("completed_videos")] public List<CompletedMediaItem>? CompletedVideos { get; init; }
    [JsonPropertyName("console_lines")] public List<string>? ConsoleLines { get; init; }
    [JsonPropertyName("telemetry")] public LiveTelemetrySnapshot? Telemetry { get; init; }
}

public sealed record BridgeAuthStatusResponse
{
    [JsonPropertyName("ok")] public bool? Ok { get; init; }
    [JsonPropertyName("logged_in")] public bool? LoggedIn { get; init; }
    [JsonPropertyName("ws_connected")] public bool? WsConnected { get; init; }
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("paid_balance")] public double? PaidBalance { get; init; }
    [JsonPropertyName("burn_rate_hourly")] public double? BurnRateHourly { get; init; }
    [JsonPropertyName("active_assignments")] public int? ActiveAssignments { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("warning")] public string? Warning { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
}
