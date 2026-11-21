using System.Net;
using System.Text.Json;

namespace EasyColab.ViewModels;

// MARK: - 模型切换、状态轮询、权重管理、任务推理与媒体下载 (StudioViewModel.Generation)

public sealed partial class StudioViewModel
{
    public void SwitchSelectedModel(OpenModelSpec spec)
    {
        if (SelectedModelId == spec.Id) return;

        SelectedModelId = spec.Id;
        TaskModeId = spec.Modes[0].Id;
        InferenceSteps = spec.DefaultSteps;
        SelectedPrecision = "bf16";
        ModelFiles = spec.DefaultFiles.ToList();
        DownloadedCount = 0;
        TotalFileCount = spec.DefaultFiles.Count;
        AllDownloaded = false;
        TotalDiskSizeGB = 0.0;
        ModelLoaded = LoadedModelIds.Contains(spec.Id) || (LoadedModelId == spec.Id);

        if (spec.Modality == ModelModality.Image)
        {
            var targetResList = (spec.Id == "minimax_h3_image" ||
                                 spec.RepoId.Equals("minimaxai/minimax-h3-image", StringComparison.OrdinalIgnoreCase))
                ? MINIMAX_H3_IMAGE_RESOLUTIONS
                : IMAGE_RESOLUTIONS;
            if (!targetResList.Contains(SelectedImageResolution) && targetResList.Count > 0)
            {
                SelectedImageResolution = targetResList[0];
            }
        }

        // 自动填入匹配该模态的首条示例提示词（若当前框还是另一模态的默认模板）
        var sample = PRESET_TEMPLATES.FirstOrDefault(t =>
            t.Category == spec.Modality || (spec.Modality == ModelModality.Video && t.Category == ModelModality.VideoAudio));
        if (sample is not null)
        {
            VisualPrompt = sample.Visual;
            SoundscapePrompt = sample.Soundscape;
            MusicPrompt = sample.Music;
        }

        if (spec.Modality == ModelModality.ComfyUI)
        {
            if (IsConnected)
            {
                StatusMessage = ComfyRunning && !string.IsNullOrEmpty(ComfyPublicURL)
                    ? L(
                        "ComfyUI 云端节点工作台正在运行，可一键在浏览器打开全屏画布。",
                        "ComfyUI Cloud Node Studio is running. Open the fullscreen canvas in your browser.")
                    : L(
                        "已切换至 ComfyUI 节点工作台，点击「一键拉起云端 ComfyUI」即可挂载 Google Drive 软链并开通画布。",
                        "Switched to ComfyUI Node Studio. Click 'Deploy & Open Cloud ComfyUI' to mount Google Drive symlinks and launch the canvas.");
            }
            else
            {
                StatusMessage = L(
                    "已切换至 ComfyUI 节点工作台，唤醒并连接 G4 实例后即可一键部署并持久化到 Google Drive。",
                    "Switched to ComfyUI Node Studio. Wake & connect a G4 instance to deploy and persist to Google Drive.");
            }
            return;
        }

        if (IsConnected)
        {
            _ = NotifyRemoteSelectModelAsync(spec.Id);
        }
        else
        {
            StatusMessage = L(
                $"已切换至 {spec.Name} ({spec.RepoId})，连接 Colab 后即可一键下载并加载至显存。",
                $"Switched to {spec.Name} ({spec.RepoId}). Connect to Colab to download & load into VRAM.");
        }
    }

    public async Task NotifyRemoteSelectModelAsync(string modelId)
    {
        var baseUrl = NormalizedBaseURL;
        if (string.IsNullOrEmpty(baseUrl)) return;
        var url = $"{baseUrl}/minimax_api/select_model";
        try
        {
            var body = JsonSerializer.Serialize(new Dictionary<string, object?> { ["model_id"] = modelId }, ModelJson.Options);
            using var req = AdminRequest(url, HttpMethod.Post, body);
            using var resp = await SendAdminAsync(req, TimeSpan.FromSeconds(25));
            var json = await resp.Content.ReadAsStringAsync();
            var decoded = ModelJson.Decode<ColabStatusResponse>(json);
            if (decoded is null) return;

            ApplyStatusResponse(decoded);
            if (IsSelectedModelLoadedInVRAM)
            {
                StatusMessage = L(
                    $"{ActiveModelSpec.Name} 已驻留 GPU 显存，可直接开始推理！",
                    $"{ActiveModelSpec.Name} is resident in GPU VRAM and ready for inference!");
            }
            else if (AllDownloaded)
            {
                StatusMessage = L(
                    $"{ActiveModelSpec.Name} 权重已在磁盘就绪，请点击左侧「加载到显存」。",
                    $"{ActiveModelSpec.Name} weights are ready on disk. Click 'Load to VRAM' on the left.");
            }
            else
            {
                StatusMessage = L(
                    $"已切换至 {ActiveModelSpec.Name} | 权重进度 {DownloadedCount}/{TotalFileCount}，请先点击「下载权重」。",
                    $"Switched to {ActiveModelSpec.Name} | Weights {DownloadedCount}/{TotalFileCount}. Click 'Download Weights' first.");
            }
        }
        catch
        {
            // Swallow background select errors (matches Swift `catch {}`).
        }
    }

    public async Task CheckColabStatusAsync(bool silent = false)
    {
        var baseUrl = NormalizedBaseURL;
        var now = DateTime.UtcNow;
        bool shouldTouchActivity = !silent || (
            (now - LastLocalInteractionAt).TotalSeconds < 30.0
            && (now - LastRemoteTouchSentAt).TotalSeconds >= 20.0
        );
        string touchSuffix = shouldTouchActivity ? "&touch=1" : string.Empty;
        string urlStr = $"{baseUrl}/minimax_api/status?model_id={Uri.EscapeDataString(SelectedModelId)}{touchSuffix}";

        if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(urlStr, UriKind.Absolute, out var uri))
        {
            if (!silent)
            {
                StatusMessage = L(
                    "请先填入服务地址，或点击左侧「唤醒实例并部署」。",
                    "Please enter a Server URL, or click 'Wake & Deploy' on the left.");
            }
            IsConnected = false;
            return;
        }

        if (shouldTouchActivity)
        {
            LastRemoteTouchSentAt = now;
        }

        if (!silent)
        {
            StatusMessage = L(
                $"正在同步 Colab GPU 显存与 {ActiveModelSpec.Name} 权重状态...",
                $"Syncing Colab GPU VRAM & {ActiveModelSpec.Name} weight status...");
        }

        try
        {
            using var req = AdminRequest(uri);
            var timeout = TimeSpan.FromSeconds(IsGenerating ? 28 : (silent ? 20 : 22));
            using var resp = await SendAdminAsync(req, timeout);
            int statusCode = (int)resp.StatusCode;

            if (statusCode == 404)
            {
                // 生成视频高负载期间单次 404 可能为 Gradio FRP 隧道瞬断，需连续多次确认才判定进程已停止
                ConsecutiveStatusFailures += IsGenerating ? 1 : 2;
                if (!silent || ConsecutiveStatusFailures >= 6)
                {
                    ConsecutiveStatusFailures = 0;
                    ResetStaleRemoteRuntimeState(
                        L(
                            "云端 Colab 推理服务已停止（可能因显存溢出或实例释放），已自动重置本地显存状态；请点击左侧「唤醒实例并部署」重新启动。",
                            "Cloud Colab server has stopped (OOM or runtime released). Local VRAM state reset; click 'Wake & Deploy' on the left to restart."));
                }
                return;
            }

            if (statusCode != 200)
            {
                ConsecutiveStatusFailures += 1;
                int failureLimit = IsGenerating ? 8 : 5;
                if (!silent || ConsecutiveStatusFailures >= failureLimit)
                {
                    ResetStaleRemoteRuntimeState(
                        L(
                            $"无法连接至 Colab 服务端 (HTTP {statusCode})，实例可能已关闭或网络中断。",
                            $"Unable to reach Colab server (HTTP {statusCode}). Runtime may be stopped or network disconnected."));
                }
                return;
            }

            ConsecutiveStatusFailures = 0;
            var json = await resp.Content.ReadAsStringAsync();
            var decoded = ModelJson.Decode<ColabStatusResponse>(json)
                ?? throw new InvalidDataException("Empty ColabStatusResponse JSON.");
            ApplyStatusResponse(decoded);

            if (!silent)
            {
                if (IsSelectedModelLoadedInVRAM)
                {
                    StatusMessage = L(
                        $"已连接 {GpuName} | {ActiveModelSpec.Name} 已驻留显存，可立即开始生成！",
                        $"Connected to {GpuName} | {ActiveModelSpec.Name} is in VRAM and ready to generate!");
                }
                else if (AllDownloaded)
                {
                    var diskStr = CFormatFixed(TotalDiskSizeGB, 1);
                    StatusMessage = L(
                        $"已连接 {GpuName} | {ActiveModelSpec.Name} 权重已就绪 ({diskStr} GB)，请点击「加载到显存」。",
                        $"Connected to {GpuName} | {ActiveModelSpec.Name} weights ready ({diskStr} GB). Click 'Load to VRAM'.");
                }
                else
                {
                    StatusMessage = L(
                        $"已连接 {GpuName} | {ActiveModelSpec.Name} 权重进度 {DownloadedCount}/{TotalFileCount}，请先点击「下载权重」。",
                        $"Connected to {GpuName} | {ActiveModelSpec.Name} weights {DownloadedCount}/{TotalFileCount}. Click 'Download Weights' first.");
                }
            }
        }
        catch (Exception ex)
        {
            ConsecutiveStatusFailures += 1;
            int failureLimit = IsGenerating ? 8 : 5;
            if (!silent)
            {
                ResetStaleRemoteRuntimeState(L(
                    $"连接失败: {ex.Message}",
                    $"Connection failed: {ex.Message}"));
            }
            else if (ConsecutiveStatusFailures >= failureLimit && IsConnected)
            {
                ResetStaleRemoteRuntimeState(L(
                    "与 Colab 服务端连接中断，已重置本地常驻状态；可点击「连接」重试或「唤醒实例并部署」。",
                    "Disconnected from Colab server; local VRAM state reset. Click 'Connect' to retry or 'Wake & Deploy'."));
            }
        }
    }

    public async Task UpdateIdleTimeoutConfigAsync(int minutes)
    {
        IdleTimeoutMinutes = minutes;
        RemoteIdleSynced = false;
        LastIdleSyncAttemptAt = DateTime.UtcNow;

        var baseUrl = NormalizedBaseURL;
        if (!IsConnected || string.IsNullOrEmpty(baseUrl)) return;
        var url = $"{baseUrl}/minimax_api/config_idle";

        try
        {
            var body = JsonSerializer.Serialize(new Dictionary<string, object?> { ["idle_timeout_minutes"] = minutes }, ModelJson.Options);
            using var req = AdminRequest(url, HttpMethod.Post, body);
            using var resp = await SendAdminAsync(req, TimeSpan.FromSeconds(20));
            var json = await resp.Content.ReadAsStringAsync();
            var decoded = ModelJson.Decode<ColabStatusResponse>(json);
            if (decoded is null) return;
            if (decoded.IdleTimeoutMinutes == minutes)
            {
                RemoteIdleSynced = true;
            }
            ApplyStatusResponse(decoded);
        }
        catch
        {
            // Ignore transient sync errors; telemetry loop retries after 8s.
        }
    }

    public async Task StartRemoteDownloadWeightsAsync()
    {
        var baseUrl = NormalizedBaseURL;
        var urlStr = $"{baseUrl}/minimax_api/download_model";
        if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(urlStr, UriKind.Absolute, out var uri))
        {
            StatusMessage = L("请先连接有效的 Colab 服务端地址。", "Please connect to a valid Colab Server URL first.");
            return;
        }

        string targetModel = SelectedModelId;
        IsDownloadingWeights = true;
        ModelTaskProgress = 0.05;
        StatusMessage = L(
            $"已通知 Colab 启动 8 线程并发下载 {ActiveModelSpec.Name}...",
            $"Started 8-thread parallel weight download on Colab for {ActiveModelSpec.Name}...");

        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["model_id"] = targetModel,
                ["hf_token"] = HfToken,
                ["repo_id"] = ActiveModelSpec.IsCustom ? ActiveModelSpec.RepoId : string.Empty,
                ["include_pattern"] = ActiveModelSpec.IsCustom ? ActiveModelSpec.IncludePattern : string.Empty,
                ["custom_name"] = ActiveModelSpec.IsCustom ? ActiveModelSpec.NameZh : string.Empty,
            };
            using var req = AdminRequest(uri, HttpMethod.Post, JsonSerializer.Serialize(payload, ModelJson.Options));
            using var resp = await SendAdminAsync(req, TimeSpan.FromSeconds(30));
            var json = await resp.Content.ReadAsStringAsync();
            var decoded = ModelJson.Decode<ColabStatusResponse>(json);
            if (decoded is not null)
            {
                ApplyStatusResponse(decoded);
            }

            while (IsDownloadingWeights)
            {
                await Task.Delay(1500);
                var statusUrl = $"{baseUrl}/minimax_api/status?model_id={Uri.EscapeDataString(targetModel)}";
                try
                {
                    using var sReq = AdminRequest(statusUrl);
                    using var sResp = await SendAdminAsync(sReq, TimeSpan.FromSeconds(20));
                    var sJson = await sResp.Content.ReadAsStringAsync();
                    var sDecoded = ModelJson.TryDecode<ColabStatusResponse>(sJson);
                    if (sDecoded is not null)
                    {
                        ApplyStatusResponse(sDecoded);
                        if (!string.IsNullOrEmpty(sDecoded.TaskMessage))
                            StatusMessage = sDecoded.TaskMessage;
                        if (!(sDecoded.IsDownloading ?? false))
                        {
                            IsDownloadingWeights = false;
                            break;
                        }
                    }
                }
                catch
                {
                    // Transient poll error: keep polling until state resolves or connection resets.
                }
            }
        }
        catch (Exception ex)
        {
            IsDownloadingWeights = false;
            StatusMessage = L(
                $"下载权重请求失败: {ex.Message}",
                $"Weight download request failed: {ex.Message}");
        }
    }

    public async Task LoadRemoteModelToVRAMAsync()
    {
        var baseUrl = NormalizedBaseURL;
        var urlStr = $"{baseUrl}/minimax_api/load_model";
        if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(urlStr, UriKind.Absolute, out var uri))
        {
            StatusMessage = L("请先填入有效的 Colab 服务端地址。", "Please enter a valid Colab Server URL first.");
            return;
        }

        if (!AllDownloaded)
        {
            StatusMessage = L(
                $"{ActiveModelSpec.Name} 权重尚未下载完整 ({DownloadedCount}/{TotalFileCount})，请先点击「下载权重」。",
                $"{ActiveModelSpec.Name} weights are incomplete ({DownloadedCount}/{TotalFileCount}). Click 'Download Weights' first.");
            return;
        }

        string targetModel = SelectedModelId;
        IsLoadingModel = true;
        ModelTaskProgress = 0.15;
        if (ActiveModelSpec.Modality == ModelModality.LlmVLM)
        {
            StatusMessage = L(
                $"正在将 {ActiveModelSpec.Name} 装载进 Colab GPU 显存 (上下文 {ContextLengthK}K)...",
                $"Loading {ActiveModelSpec.Name} into Colab GPU VRAM (Context {ContextLengthK}K)...");
        }
        else
        {
            StatusMessage = L(
                $"正在将 {ActiveModelSpec.Name} 装载进 Colab GPU 显存...",
                $"Loading {ActiveModelSpec.Name} into Colab GPU VRAM...");
        }

        try
        {
            var body = new Dictionary<string, object?>
            {
                ["model_id"] = targetModel,
                ["precision_mode"] = "bf16",
                ["offload_mode"] = SelectedOffload,
                ["context_length"] = ContextLengthTokens,
                ["hf_token"] = HfToken,
                ["repo_id"] = ActiveModelSpec.IsCustom ? ActiveModelSpec.RepoId : string.Empty,
                ["include_pattern"] = ActiveModelSpec.IsCustom ? ActiveModelSpec.IncludePattern : string.Empty,
                ["custom_name"] = ActiveModelSpec.IsCustom ? ActiveModelSpec.NameZh : string.Empty,
            };
            using var req = AdminRequest(uri, HttpMethod.Post, JsonSerializer.Serialize(body, ModelJson.Options));
            using var resp = await SendAdminAsync(req, TimeSpan.FromSeconds(30));
            var json = await resp.Content.ReadAsStringAsync();
            var decoded = ModelJson.Decode<ColabStatusResponse>(json);
            if (decoded is not null)
            {
                ApplyStatusResponse(decoded);
            }

            while (IsLoadingModel)
            {
                await Task.Delay(1500);
                var statusUrl = $"{baseUrl}/minimax_api/status?model_id={Uri.EscapeDataString(targetModel)}";
                try
                {
                    using var sReq = AdminRequest(statusUrl);
                    using var sResp = await SendAdminAsync(sReq, TimeSpan.FromSeconds(20));
                    var sJson = await sResp.Content.ReadAsStringAsync();
                    var sDecoded = ModelJson.TryDecode<ColabStatusResponse>(sJson);
                    if (sDecoded is not null)
                    {
                        ApplyStatusResponse(sDecoded);
                        if (!string.IsNullOrEmpty(sDecoded.TaskMessage))
                            StatusMessage = sDecoded.TaskMessage;
                        if (!(sDecoded.IsLoadingVram ?? false))
                        {
                            IsLoadingModel = false;
                            break;
                        }
                    }
                }
                catch
                {
                    // Transient poll error.
                }
            }
        }
        catch (Exception ex)
        {
            IsLoadingModel = false;
            StatusMessage = L(
                $"加载模型请求失败: {ex.Message}",
                $"Load model request failed: {ex.Message}");
        }
    }

    public async Task UnloadRemoteModelFromVRAMAsync(string modelId)
    {
        string targetLabel = modelId == "all"
            ? L("全部常驻模型", "all resident models")
            : (AllAvailableModels.FirstOrDefault(m => m.Id == modelId)?.Name ?? modelId);

        if (modelId == "all" || modelId == SelectedModelId)
        {
            IsGenerating = false;
            ProgressValue = 0.0;
        }

        var baseUrl = NormalizedBaseURL;
        var urlStr = $"{baseUrl}/minimax_api/unload_model";
        if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(urlStr, UriKind.Absolute, out var uri))
        {
            ClearLocalLoadedModelRecord(modelId);
            StatusMessage = L(
                $"已清理本地 {targetLabel} 常驻记录。",
                $"Cleared local VRAM residency record for {targetLabel}.");
            return;
        }

        IsUnloadingModel = true;
        StatusMessage = L(
            $"正在中断关联任务并从 GPU 显存卸载 {targetLabel}...",
            $"Interrupting related tasks and unloading {targetLabel} from GPU VRAM...");

        try
        {
            var body = JsonSerializer.Serialize(new Dictionary<string, object?> { ["model_id"] = modelId }, ModelJson.Options);
            using var req = AdminRequest(uri, HttpMethod.Post, body);
            using var resp = await SendAdminAsync(req, TimeSpan.FromSeconds(20));
            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var json = await resp.Content.ReadAsStringAsync();
                var decoded = ModelJson.Decode<ColabStatusResponse>(json);
                if (decoded is not null)
                {
                    ApplyStatusResponse(decoded);
                    if (!string.IsNullOrEmpty(decoded.TaskMessage))
                    {
                        StatusMessage = decoded.TaskMessage;
                    }
                    else
                    {
                        var vramStr = CFormatFixed(VramFreeGB, 1);
                        StatusMessage = L(
                            $"已从显存卸载 {targetLabel}，当前可用显存 {vramStr} GB。",
                            $"Unloaded {targetLabel} from VRAM. Free VRAM: {vramStr} GB.");
                    }
                }
            }
            else
            {
                // 远端已离线或 404，直接清理本地残留状态，绝不卡死按钮
                ResetStaleRemoteRuntimeState(L(
                    $"远端 Colab 服务已停止，已清理本地 {targetLabel} 显存记录。",
                    $"Remote Colab server stopped; cleared local VRAM record for {targetLabel}."));
            }
        }
        catch (Exception ex)
        {
            ClearLocalLoadedModelRecord(modelId);
            if (!IsConnected)
            {
                ResetStaleRemoteRuntimeState(L(
                    $"远端实例未连接，已移除本地 {targetLabel} 常驻记录。",
                    $"Remote instance disconnected; removed local VRAM record for {targetLabel}."));
            }
            else
            {
                StatusMessage = L(
                    $"远端响应超时，已先移除本地 {targetLabel} 常驻状态 ({ex.Message})。",
                    $"Remote timed out; cleared local VRAM record for {targetLabel} ({ex.Message}).");
            }
        }
        finally
        {
            IsUnloadingModel = false;
        }
    }

    public async Task CancelCurrentGenerationAsync()
    {
        ActiveGenerationToken = Guid.NewGuid();
        ActiveJobPollToken = ActiveGenerationToken;
        CancelActivePolling();
        StopLiveGenerationTimer();
        IsGenerating = false;
        ProgressValue = 0.0;
        CurrentJobElapsed = 0.0;
        StripConsoleProgressBars();
        StatusMessage = L(
            "已发送停止指令，正在中断云端 GPU 推理任务...",
            "Stop signal sent. Aborting cloud GPU inference...");

        var baseUrl = NormalizedBaseURL;
        var urlStr = $"{baseUrl}/minimax_api/cancel";
        if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(urlStr, UriKind.Absolute, out var uri)) return;

        try
        {
            using var req = AdminRequest(uri, HttpMethod.Post);
            using var resp = await SendAdminAsync(req, TimeSpan.FromSeconds(12));
            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var json = await resp.Content.ReadAsStringAsync();
                var decoded = ModelJson.TryDecode<ColabStatusResponse>(json);
                if (decoded is not null)
                    ApplyStatusResponse(decoded);
            }
            StripConsoleProgressBars();
            StatusMessage = L(
                "已停止当前推理任务，GPU 已恢复就绪。",
                "Stopped current inference task. GPU is ready.");
        }
        catch (Exception ex)
        {
            StatusMessage = L(
                $"本地已停止；远端连接异常: {ex.Message}",
                $"Stopped locally; remote connection error: {ex.Message}");
        }
    }

    public async Task StartGenerationAsync()
    {
        var promptTrimmed = (VisualPrompt ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(promptTrimmed))
        {
            StatusMessage = ActiveModelSpec.Modality == ModelModality.LlmVLM
                ? L("请输入对话问题或视觉指令！", "Please enter a question or vision prompt!")
                : L("请输入画面描述提示词！", "Please enter a visual scene prompt!");
            return;
        }

        if (TaskModeId == "fl2va" && string.IsNullOrEmpty(FirstFrameImage) && string.IsNullOrEmpty(LastFrameImage))
        {
            StatusMessage = L(
                "在「首尾帧生视频」模式下，请至少提供一张首帧或尾帧图片。",
                "In First/Last Frame mode, please provide at least a start frame or end frame image.");
            return;
        }
        if (TaskModeId == "i2v" && string.IsNullOrEmpty(FirstFrameImage))
        {
            StatusMessage = L(
                "在「首帧图生视频」模式下，请提供一张首帧图片。",
                "In Image-to-Video mode, please provide a start frame image.");
            return;
        }
        if (TaskModeId == "i2i_edit" && string.IsNullOrEmpty(FirstFrameImage) && string.IsNullOrEmpty(LastFrameImage))
        {
            StatusMessage = L(
                "在「图像编辑/重绘」模式下，请至少拖入一张参考原图。",
                "In Image Edit / Restyle mode, please drop at least one reference image.");
            return;
        }

        await CheckColabStatusAsync(silent: true);
        if (!IsConnected)
        {
            StatusMessage = L(
                "尚未连接 Colab 服务端，请先唤醒或连接现有实例。",
                "Not connected to Colab server. Please wake & deploy or connect to an active runtime first.");
            return;
        }
        if (!IsSelectedModelLoadedInVRAM)
        {
            if (!AllDownloaded)
            {
                StatusMessage = L(
                    $"请先在左侧点击「下载权重」，再点击「加载到显存」装载 {ActiveModelSpec.Name}。",
                    $"Click 'Download Weights' first, then 'Load to VRAM' to load {ActiveModelSpec.Name}.");
            }
            else
            {
                StatusMessage = L(
                    $"{ActiveModelSpec.Name} 尚未装载至显存，请先点击左侧「加载到显存」。",
                    $"{ActiveModelSpec.Name} is not in VRAM yet. Click 'Load to VRAM' on the left first.");
            }
            return;
        }

        var baseUrl = NormalizedBaseURL;
        var submitUrlStr = $"{baseUrl}/minimax_api/generate";
        if (!Uri.TryCreate(submitUrlStr, UriKind.Absolute, out var submitUri)) return;

        var myToken = Guid.NewGuid();
        ActiveGenerationToken = myToken;
        ActiveJobPollToken = myToken;
        IsGenerating = true;
        ProgressValue = 0.0;
        CurrentJobElapsed = 0.0;
        StripConsoleProgressBars();
        StartLiveGenerationTimer();
        StatusMessage = L(
            $"正在向 Colab 提交 {ActiveModelSpec.Name} 推理任务 (0%)...",
            $"Submitting {ActiveModelSpec.Name} inference job to Colab (0%)...");

        var resOpt = ActiveModelSpec.Modality == ModelModality.Image ? SelectedImageResolution : SelectedVideoResolution;

        List<Dictionary<string, string>> chatPayload = [];
        int? assistantRowIndex = null;
        if (ActiveModelSpec.Modality == ModelModality.LlmVLM)
        {
            bool hasImage = TaskModeId == "vlm" && (!string.IsNullOrEmpty(FirstFrameImage) || !string.IsNullOrEmpty(LastFrameImage));
            var userTurn = new ChatTurnItem(role: "user", content: promptTrimmed, hasImage: hasImage);
            ChatHistory.Add(userTurn);
            chatPayload = ChatHistory
                .Select(t => new Dictionary<string, string> { ["role"] = t.Role, ["content"] = t.Content })
                .ToList();
            var placeholderAssistant = new ChatTurnItem(role: "assistant", content: string.Empty, thinking: string.Empty);
            ChatHistory.Add(placeholderAssistant);
            assistantRowIndex = ChatHistory.Count - 1;
        }

        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["model_id"] = SelectedModelId,
                ["task_mode"] = TaskModeId,
                ["visual_prompt"] = promptTrimmed,
                ["negative_prompt"] = NegativePrompt,
                ["soundscape_prompt"] = SoundscapePrompt,
                ["music_prompt"] = MusicPrompt,
                ["width"] = resOpt.Width,
                ["height"] = resOpt.Height,
                ["num_frames"] = (int)NumFrames,
                ["num_inference_steps"] = (int)InferenceSteps,
                ["fps"] = (int)Fps,
                ["seed"] = Seed,
                ["randomize_seed"] = RandomizeSeed,
                ["batch_count"] = ActiveModelSpec.Modality == ModelModality.LlmVLM ? 1 : BatchCount,
                ["denoising_strength"] = DenoisingStrength,
                ["enable_thinking"] = EnableThinking,
                ["max_new_tokens"] = (int)MaxNewTokens,
                ["context_length"] = ContextLengthTokens,
                ["temperature"] = Temperature,
                ["top_p"] = TopP,
                ["top_k"] = 20,
                ["min_p"] = 0.0,
                ["presence_penalty"] = PresencePenalty,
                ["repeat_penalty"] = 1.0,
                ["reasoning_effort"] = ReasoningEffort,
            };

            if (chatPayload.Count > 0)
            {
                payload["chat_messages"] = chatPayload;
            }

            bool needsImage = TaskModeId is "fl2va" or "i2v" or "i2i_edit" or "vlm";
            if (needsImage)
            {
                if (!string.IsNullOrEmpty(FirstFrameImage))
                {
                    var b64 = await EncodeImageToBase64Async(FirstFrameImage);
                    if (!string.IsNullOrEmpty(b64))
                        payload["first_frame_base64"] = b64;
                }
                if (!string.IsNullOrEmpty(LastFrameImage))
                {
                    var b64 = await EncodeImageToBase64Async(LastFrameImage);
                    if (!string.IsNullOrEmpty(b64))
                        payload["last_frame_base64"] = b64;
                }
            }

            using var req = AdminRequest(submitUri, HttpMethod.Post, JsonSerializer.Serialize(payload, ModelJson.Options));
            using var resp = await SendAdminAsync(req, TimeSpan.FromSeconds(30));
            if (ActiveGenerationToken != myToken) return;

            var json = await resp.Content.ReadAsStringAsync();
            var job = ModelJson.Decode<ColabJobResponse>(json)
                ?? throw new InvalidDataException("Empty ColabJobResponse JSON.");

            if (job.Status == "failed")
            {
                StopLiveGenerationTimer();
                IsGenerating = false;
                ProgressValue = 0.0;
                StatusMessage = job.Message;
                if (assistantRowIndex is { } idx && idx < ChatHistory.Count)
                {
                    ChatHistory[idx].Content = L(
                        $"推理失败：{job.Message}",
                        $"Inference failed: {LocalizeServerText(job.Message)}");
                }
                return;
            }

            await PollColabJobAsync(baseUrl, job.JobId, assistantRowIndex, myToken);
        }
        catch (Exception ex)
        {
            if (ActiveGenerationToken != myToken) return;
            StopLiveGenerationTimer();
            IsGenerating = false;
            ProgressValue = 0.0;
            StatusMessage = L(
                $"提交任务失败: {ex.Message}",
                $"Failed to submit job: {ex.Message}");
        }
    }

    public async Task PollColabJobAsync(string baseURL, string jobId, int? assistantRowIndex, Guid token)
    {
        var pollUrlStr = $"{baseURL}/minimax_api/job/{Uri.EscapeDataString(jobId)}";
        if (!Uri.TryCreate(pollUrlStr, UriKind.Absolute, out var pollUri))
        {
            if (ActiveGenerationToken == token)
            {
                StopLiveGenerationTimer();
                IsGenerating = false;
            }
            return;
        }

        CancelActivePolling();
        var pollCts = new CancellationTokenSource();
        PollingCts = pollCts;
        var ct = pollCts.Token;

        var downloadedFilenames = new HashSet<string>(StringComparer.Ordinal);
        int consecutivePollErrors = 0;
        int consecutiveNotFound = 0;
        int pollIntervalMs = assistantRowIndex is not null ? 600 : 1000;

        try
        {
            while (IsGenerating && ActiveGenerationToken == token && !ct.IsCancellationRequested)
            {
                try { await Task.Delay(pollIntervalMs, ct); }
                catch (OperationCanceledException) { break; }

                if (!IsGenerating || ActiveGenerationToken != token || ct.IsCancellationRequested)
                    break;

                try
                {
                    using var req = AdminRequest(pollUri);
                    using var resp = await SendAdminAsync(req, TimeSpan.FromSeconds(30), cancellationToken: ct);
                    if (!IsGenerating || ActiveGenerationToken != token) break;

                    int code = (int)resp.StatusCode;
                    if (code == 404)
                    {
                        consecutiveNotFound += 1;
                        if (consecutiveNotFound >= 5)
                        {
                            ResetStaleRemoteRuntimeState(L(
                                "云端推理进程已中断（可能因显存溢出或实例释放），请重新唤醒实例。",
                                "Cloud inference process interrupted (OOM or runtime released). Please wake & deploy again."));
                            break;
                        }
                        continue;
                    }
                    consecutiveNotFound = 0;

                    if (code != 200)
                    {
                        consecutivePollErrors += 1;
                        if (consecutivePollErrors >= 12)
                        {
                            StopLiveGenerationTimer();
                            IsGenerating = false;
                            StatusMessage = L(
                                $"云端任务响应异常 (HTTP {code})，已自动解除等待。",
                                $"Cloud job returned HTTP {code}; unlocked controls automatically.");
                            break;
                        }
                        continue;
                    }

                    consecutivePollErrors = 0;
                    var json = await resp.Content.ReadAsStringAsync(ct);
                    var job = ModelJson.Decode<ColabJobResponse>(json)
                        ?? throw new InvalidDataException("Empty ColabJobResponse JSON.");

                    if (job.Telemetry is { } tl)
                    {
                        ApplyLiveTelemetrySnapshot(tl);
                    }

                    // 在文件彻底生成并下载回本地预览前，进度条最高锁定在 98%，杜绝“显示 100% 但视频尚未出来”
                    if (job.Status != "completed")
                    {
                        ProgressValue = Math.Clamp(job.Progress, 0.0, 0.98);
                    }
                    StatusMessage = job.Message;
                    if (job.Elapsed is { } remoteEl && remoteEl >= 0)
                    {
                        CurrentJobElapsed = Math.Max(CurrentJobElapsed, remoteEl);
                    }
                    if (job.Seed is { } s)
                    {
                        Seed = s;
                    }
                    if (job.ConsoleLines is { Count: > 0 } lines)
                    {
                        ReplaceConsoleLines(lines);
                    }

                    // Qwen3.8-27B 实时流式更新思考链与回复正文
                    if (assistantRowIndex is { } idx && idx < ChatHistory.Count)
                    {
                        if (job.ThinkingOutput is { } thinkTxt)
                            ChatHistory[idx].Thinking = thinkTxt;
                        if (job.TextOutput is { } ansTxt)
                            ChatHistory[idx].Content = ansTxt;
                    }

                    // 视频或图像批量边生边回传预览
                    if (job.CompletedVideos is { Count: > 0 } completedItems)
                    {
                        foreach (var item in completedItems)
                        {
                            if (downloadedFilenames.Contains(item.Filename)) continue;
                            if (ActiveGenerationToken != token) break;

                            var downloadUrlStr = $"{baseURL}{item.VideoUrl}";
                            if (Uri.TryCreate(downloadUrlStr, UriKind.Absolute, out _))
                            {
                                downloadedFilenames.Add(item.Filename);
                                if (job.Status == "completed" && (job.BatchCount ?? 1) <= 1)
                                {
                                    ProgressValue = 0.99;
                                    StatusMessage = L(
                                        "云端渲染已完成，正在高速回传结果文件至本地预览 (99%)...",
                                        "Cloud rendering complete. Transferring output to local preview (99%)...");
                                }
                                await DownloadMediaToLocalAsync(
                                    remoteURL: downloadUrlStr,
                                    filename: item.Filename,
                                    elapsedSeconds: item.Elapsed ?? job.Elapsed,
                                    keepGeneratingProgress: job.Status != "completed");
                            }
                        }
                    }

                    if (ActiveGenerationToken != token) break;

                    if (job.Status == "completed")
                    {
                        double finalElapsed = job.Elapsed ?? CurrentJobElapsed;
                        if (finalElapsed > 0)
                        {
                            LastCompletedElapsed = finalElapsed;
                            CurrentJobElapsed = finalElapsed;
                        }

                        if (!string.IsNullOrEmpty(job.Filename)
                            && !downloadedFilenames.Contains(job.Filename)
                            && !string.IsNullOrEmpty(job.VideoUrl))
                        {
                            var downloadUrlStr = $"{baseURL}{job.VideoUrl}";
                            if (Uri.TryCreate(downloadUrlStr, UriKind.Absolute, out _))
                            {
                                downloadedFilenames.Add(job.Filename);
                                ProgressValue = 0.99;
                                StatusMessage = L(
                                    "云端渲染已完成，正在高速回传结果文件至本地预览 (99%)...",
                                    "Cloud rendering complete. Transferring output to local preview (99%)...");
                                await DownloadMediaToLocalAsync(
                                    remoteURL: downloadUrlStr,
                                    filename: job.Filename,
                                    elapsedSeconds: finalElapsed);
                            }
                        }

                        if (ActiveGenerationToken != token) break;

                        if (assistantRowIndex is null && (job.BatchCount ?? 1) > 1 && finalElapsed > 0)
                        {
                            int cnt = job.BatchCount ?? 1;
                            StatusMessage = L(
                                $"批量生成 {cnt} 项全部完成 · 总耗时 {FormatDuration(finalElapsed)}（均值 {FormatDuration(finalElapsed / cnt)}/项）",
                                $"Batch of {cnt} completed · Total {FormatDuration(finalElapsed)} (avg {FormatDuration(finalElapsed / cnt)}/item)");
                        }
                        StopLiveGenerationTimer();
                        IsGenerating = false;
                        ProgressValue = 1.0;
                        await CheckColabStatusAsync(silent: true);
                        break;
                    }
                    else if (job.Status == "failed")
                    {
                        if (assistantRowIndex is { } failIdx && failIdx < ChatHistory.Count && string.IsNullOrEmpty(ChatHistory[failIdx].Content))
                        {
                            ChatHistory[failIdx].Content = LocalizeServerText(job.Message);
                        }
                        StopLiveGenerationTimer();
                        IsGenerating = false;
                        ProgressValue = 0.0;
                        StripConsoleProgressBars();
                        break;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (ActiveGenerationToken != token) break;
                    consecutivePollErrors += 1;
                    if (consecutivePollErrors >= 12)
                    {
                        StopLiveGenerationTimer();
                        IsGenerating = false;
                        StatusMessage = L(
                            $"与云端任务失去连接，已自动解除锁定: {ex.Message}",
                            $"Lost connection to cloud job; unlocked controls automatically: {ex.Message}");
                        await CheckColabStatusAsync(silent: true);
                        break;
                    }
                }
            }
        }
        finally
        {
            if (ReferenceEquals(PollingCts, pollCts))
            {
                PollingCts = null;
            }
            pollCts.Dispose();
        }
    }

    private void CancelActivePolling()
    {
        var cts = PollingCts;
        PollingCts = null;
        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
        }
    }

    public async Task DownloadMediaToLocalAsync(
        string remoteURL,
        string filename,
        double? elapsedSeconds = null,
        bool keepGeneratingProgress = false)
    {
        string safeName = Path.GetFileName(filename);
        if (string.IsNullOrEmpty(safeName))
            safeName = $"output_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.mp4";

        string destDir = LocalSaveDir;
        Directory.CreateDirectory(destDir);
        string destPath = Path.Combine(destDir, safeName);
        string tmpPath = Path.Combine(destDir, $".{safeName}.{Guid.NewGuid():N}.tmp");

        try
        {
            using var req = AdminRequest(remoteURL);
            using var resp = await SendAdminAsync(req, TimeSpan.FromMinutes(10), HttpCompletionOption.ResponseHeadersRead);
            if (resp.StatusCode != HttpStatusCode.OK)
                throw new HttpRequestException($"Unexpected status code {(int)resp.StatusCode}");

            await using (var remoteStream = await resp.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await remoteStream.CopyToAsync(fileStream);
            }

            File.Move(tmpPath, destPath, overwrite: true);

            if (elapsedSeconds is { } el && el > 0)
            {
                SetMediaElapsed(safeName, el);
            }
            RefreshLocalMedia();
            SelectMedia(destPath);

            if (!keepGeneratingProgress)
            {
                ProgressValue = 1.0;
                bool isMP4 = Path.GetExtension(destPath).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
                string kindZh = isMP4 ? "视频" : "图像";
                string kindEn = isMP4 ? "Video" : "Image";
                if (elapsedSeconds is { } el2 && el2 > 0)
                {
                    StatusMessage = L(
                        $"{kindZh}生成完成 · 耗时 {FormatDuration(el2)} · 已保存：{safeName}",
                        $"{kindEn} generated in {FormatDuration(el2)} · Saved: {safeName}");
                }
                else
                {
                    StatusMessage = L(
                        $"结果已保存至本地并自动预览：{safeName}",
                        $"Saved locally and opened in preview: {safeName}");
                }
            }
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            StatusMessage = L(
                $"云端生成已完成，但下载至本地失败: {ex.Message}",
                $"Cloud generation finished, but local download failed: {ex.Message}");
        }
    }

    public async Task DisconnectRemoteColabAsync()
    {
        StopActiveWakeProcess();
        StopKeepaliveDaemon();
        StatusMessage = L(
            "正在释放云端 Colab 实例并同步账户计费状态...",
            "Terminating cloud Colab instances & syncing billing status...");

        var baseUrl = NormalizedBaseURL;
        if (!string.IsNullOrEmpty(baseUrl) && Uri.TryCreate($"{baseUrl}/minimax_api/disconnect", UriKind.Absolute, out var disconnectUri))
        {
            try
            {
                using var req = AdminRequest(disconnectUri, HttpMethod.Post);
                using var _ = await SendAdminAsync(req, TimeSpan.FromSeconds(8));
            }
            catch
            {
                // Best-effort remote disconnect.
            }
        }

        if (CliLoggedIn)
        {
            var outJson = await RunBridgeCommandAsync(["stop_all"]);
            var decoded = ModelJson.TryDecode<BridgeAuthStatusResponse>(outJson);
            if (decoded is not null)
            {
                OfficialCUBalance = decoded.PaidBalance ?? OfficialCUBalance;
                OfficialBurnRate = decoded.BurnRateHourly ?? 0.0;
                ActiveVMCount = decoded.ActiveAssignments ?? 0;
                StatusMessage = decoded.Message ?? L(
                    "已彻底释放全部云端实例，计费速率归零。",
                    "All cloud Colab instances terminated. Billing rate is now 0 CU/h.");
            }
        }
        else
        {
            StatusMessage = L(
                "已发送释放指令，Colab 实例已停止计费。",
                "Release command sent. Colab runtime billing stopped.");
        }

        StopKeepaliveDaemon();
        StopLiveGenerationTimer();
        IsConnected = false;
        ModelLoaded = false;
        LoadedModelId = null;
        LoadedModelIds = [];
        LoadedModelsDetail = [];
        LoadedContextLengthTokens = 0;
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
        RemoteContextSynced = false;
        RemoteIdleSynced = false;
        RemoteApiKeySynced = false;
        LastIdleSyncAttemptAt = DateTime.MinValue;
        LastContextSyncAttemptAt = DateTime.MinValue;
        UptimeSeconds = 0;
        CuBurnRatePerHour = 0.0;
        SyncRuntimeActivityAssertion();
    }
}
