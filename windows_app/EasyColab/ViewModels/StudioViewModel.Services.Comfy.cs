using System.Net;
using System.Text.Json;

namespace EasyColab.ViewModels;

// MARK: - StudioViewModel 扩展：云端 ComfyUI 工作台、预置工作流包与成片回传

public sealed partial class StudioViewModel
{
    public void OpenComfyUIInBrowser()
    {
        if (string.IsNullOrWhiteSpace(ComfyPublicURL)) return;
        if (!Uri.TryCreate(ComfyPublicURL, UriKind.Absolute, out var uri)) return;
        LocalRuntimePaths.OpenUrl(uri.AbsoluteUri);
    }

    public async Task StartRemoteComfyUIAsync(bool forceRestart = false, bool autoOpenBrowser = true)
    {
        string baseUrl = NormalizedBaseURL;
        if (!IsConnected || string.IsNullOrEmpty(baseUrl))
        {
            StatusMessage = L(
                "请先唤醒或连接云端 G4 实例后再启动 ComfyUI。",
                "Please wake or connect to a cloud G4 instance before starting ComfyUI."
            );
            return;
        }

        ComfyStarting = true;
        ComfyProgress = 0.04;
        string tip = forceRestart
            ? L("正在重启云端 ComfyUI 并重新加载自定义节点...", "Restarting cloud ComfyUI & reloading custom nodes...")
            : L("正在部署云端 ComfyUI 并挂载 Google Drive 持久化软链...", "Deploying cloud ComfyUI & mounting Google Drive persistence symlinks...");
        ComfyStatusMessage = tip;
        StatusMessage = tip;

        try
        {
            string body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["unload_existing"] = ComfyUnloadOthersOnStart,
                ["force_restart"] = forceRestart,
            }, ModelJson.Options);

            var (_, respBody) = await SendAdminRequestAsync(
                $"{baseUrl}/minimax_api/comfyui/start",
                HttpMethod.Post,
                body,
                timeoutSeconds: 30);
            var decoded = ModelJson.Decode<ColabStatusResponse>(respBody);
            if (decoded is not null)
            {
                ApplyStatusResponse(decoded);
            }

            // 持续轮询直到启动完成并拿到 trycloudflare 公网画布地址
            for (int i = 0; i < 180; i++)
            {
                await Task.Delay(1500);
                await CheckColabStatusAsync(silent: true);
                StatusMessage = ComfyStatusMessage;
                if (ComfyRunning && !string.IsNullOrEmpty(ComfyPublicURL) && !ComfyStarting)
                {
                    StatusMessage = L(
                        "ComfyUI 全屏节点工作台已就绪！正在为您在浏览器中打开画布...",
                        "ComfyUI fullscreen node canvas is ready! Opening in your browser..."
                    );
                    if (autoOpenBrowser)
                    {
                        OpenComfyUIInBrowser();
                    }
                    return;
                }
                if (!ComfyStarting && !ComfyRunning)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            ComfyStarting = false;
            StatusMessage = L(
                $"启动 ComfyUI 请求失败: {ex.Message}",
                $"Failed to start ComfyUI: {ex.Message}"
            );
        }
    }

    public async Task StopRemoteComfyUIAsync()
    {
        string baseUrl = NormalizedBaseURL;
        if (!IsConnected || string.IsNullOrEmpty(baseUrl)) return;

        StatusMessage = L(
            "正在同步自定义节点与工作流到 Google Drive 并停止 ComfyUI...",
            "Syncing custom nodes & workflows to Google Drive and stopping ComfyUI..."
        );
        try
        {
            var (_, respBody) = await SendAdminRequestAsync(
                $"{baseUrl}/minimax_api/comfyui/stop",
                HttpMethod.Post,
                jsonBody: null,
                timeoutSeconds: 35);
            var decoded = ModelJson.Decode<ColabStatusResponse>(respBody);
            if (decoded is not null)
            {
                ApplyStatusResponse(decoded);
            }
            StatusMessage = ComfyStatusMessage;
        }
        catch (Exception ex)
        {
            StatusMessage = L(
                $"停止 ComfyUI 失败: {ex.Message}",
                $"Failed to stop ComfyUI: {ex.Message}"
            );
        }
    }

    public async Task FreeRemoteComfyUIVRAMAsync()
    {
        string baseUrl = NormalizedBaseURL;
        if (!IsConnected || string.IsNullOrEmpty(baseUrl)) return;

        try
        {
            var (_, respBody) = await SendAdminRequestAsync(
                $"{baseUrl}/minimax_api/comfyui/free_vram",
                HttpMethod.Post,
                jsonBody: null,
                timeoutSeconds: 20);
            var decoded = ModelJson.Decode<ColabStatusResponse>(respBody);
            if (decoded is not null)
            {
                ApplyStatusResponse(decoded);
                if (!string.IsNullOrEmpty(decoded.ActionMessage))
                {
                    StatusMessage = decoded.ActionMessage;
                }
            }
        }
        catch { }
    }

    public async Task SyncRemoteComfyUIToDriveAsync()
    {
        string baseUrl = NormalizedBaseURL;
        if (!IsConnected || string.IsNullOrEmpty(baseUrl)) return;

        ComfySyncingDrive = true;
        StatusMessage = L(
            "正在将 ComfyUI 自定义节点、模型与工作流同步至 Google Drive...",
            "Syncing ComfyUI custom nodes, models & workflows to Google Drive..."
        );
        try
        {
            var (_, respBody) = await SendAdminRequestAsync(
                $"{baseUrl}/minimax_api/comfyui/sync_drive",
                HttpMethod.Post,
                jsonBody: null,
                timeoutSeconds: 90);
            var decoded = ModelJson.Decode<ColabStatusResponse>(respBody);
            if (decoded is not null)
            {
                ApplyStatusResponse(decoded);
                if (!string.IsNullOrEmpty(decoded.ActionMessage))
                {
                    StatusMessage = decoded.ActionMessage;
                }
            }
        }
        catch (Exception ex)
        {
            ComfySyncingDrive = false;
            StatusMessage = L(
                $"同步至 Google Drive 失败: {ex.Message}",
                $"Failed to sync to Google Drive: {ex.Message}"
            );
        }
    }

    public async Task StartComfyWeightDownloadAsync()
    {
        string src = (ComfyWeightSourceInput ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(src))
        {
            StatusMessage = L(
                "请先粘贴 HuggingFace 模型直链、下载 URL 或 仓库 ID",
                "Please paste a Hugging Face direct link, download URL, or repo ID first"
            );
            return;
        }
        string baseUrl = NormalizedBaseURL;
        if (!IsConnected || string.IsNullOrEmpty(baseUrl))
        {
            StatusMessage = L("请先连接 Colab 实例后再下载权重", "Please connect to a Colab instance before downloading weights");
            return;
        }

        ComfyDownloadingWeight = true;
        ComfyProgress = 0.05;
        StatusMessage = L(
            $"正在通过云端 8 线程通道下载权重至 ComfyUI/models/{ComfyWeightSubfolder}...",
            $"Downloading weights via 8-thread cloud channel to ComfyUI/models/{ComfyWeightSubfolder}..."
        );
        try
        {
            string body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["url_or_repo"] = src,
                ["filename"] = (ComfyWeightFilenameInput ?? string.Empty).Trim(),
                ["subfolder"] = ComfyWeightSubfolder,
                ["hf_token"] = (HfToken ?? string.Empty).Trim(),
            }, ModelJson.Options);

            var (_, respBody) = await SendAdminRequestAsync(
                $"{baseUrl}/minimax_api/comfyui/download_weight",
                HttpMethod.Post,
                body,
                timeoutSeconds: 25);
            var decoded = ModelJson.Decode<ColabStatusResponse>(respBody);
            if (decoded is not null)
            {
                ApplyStatusResponse(decoded);
            }

            while (ComfyDownloadingWeight)
            {
                await Task.Delay(1500);
                await CheckColabStatusAsync(silent: true);
                StatusMessage = ComfyStatusMessage;
            }
        }
        catch (Exception ex)
        {
            ComfyDownloadingWeight = false;
            StatusMessage = L(
                $"提交权重下载失败: {ex.Message}",
                $"Failed to start weight download: {ex.Message}"
            );
        }
    }

    public async Task InstallComfyPresetPackAsync(ComfyPresetPackItem pack, bool openWhenReady = true)
    {
        string baseUrl = NormalizedBaseURL;
        if (!IsConnected || string.IsNullOrEmpty(baseUrl))
        {
            StatusMessage = L(
                "请先在左侧唤醒或连接云端 G4 实例后再一键配齐工作流包。",
                "Please wake or connect to a cloud G4 instance on the left before installing a workflow pack."
            );
            return;
        }

        ComfyDownloadingWeight = true;
        ComfyInstallingPackId = pack.Id;
        ComfyProgress = 0.04;
        string tip = L(
            $"正在为您一键配齐「{pack.LocalizedTitle}」模型权重与全中文工作流...",
            $"Installing models & workflow templates for \"{pack.LocalizedTitle}\"..."
        );
        ComfyStatusMessage = tip;
        StatusMessage = tip;

        try
        {
            string body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["pack_id"] = pack.Id,
                ["auto_start"] = true,
                ["hf_token"] = (HfToken ?? string.Empty).Trim(),
            }, ModelJson.Options);

            var (statusCode, respBody) = await SendAdminRequestAsync(
                $"{baseUrl}/minimax_api/comfyui/install_pack",
                HttpMethod.Post,
                body,
                timeoutSeconds: 30);

            if (statusCode == HttpStatusCode.NotFound)
            {
                ComfyDownloadingWeight = false;
                ComfyInstallingPackId = "";
                string upgradeTip = L(
                    "当前云端实例仍在运行旧版服务，请点左侧「唤醒实例并部署」热更新服务端（不会释放现有 G4 实例与画布），随后即可一键配齐工作流！",
                    "The cloud instance is running an older server version. Click 'Wake & Deploy' on the left to hot-update app.py without releasing your G4 VM, then install the pack!"
                );
                ComfyStatusMessage = upgradeTip;
                StatusMessage = upgradeTip;
                return;
            }

            var decoded = ModelJson.Decode<ColabStatusResponse>(respBody);
            if (decoded is not null)
            {
                ApplyStatusResponse(decoded);
            }

            for (int i = 0; i < 600; i++)
            {
                await Task.Delay(1500);
                await CheckColabStatusAsync(silent: true);
                StatusMessage = ComfyStatusMessage;
                if (!ComfyDownloadingWeight && string.IsNullOrEmpty(ComfyInstallingPackId) && !ComfyStarting)
                {
                    break;
                }
            }

            if (openWhenReady && ComfyRunning && !string.IsNullOrEmpty(ComfyPublicURL))
            {
                OpenComfyUIInBrowser();
            }
        }
        catch (Exception ex)
        {
            ComfyDownloadingWeight = false;
            ComfyInstallingPackId = "";
            StatusMessage = L(
                $"一键配齐预置工作流包失败: {ex.Message}",
                $"Failed to install preset workflow pack: {ex.Message}"
            );
        }
    }

    public async Task DownloadComfyOutputToMacAsync(ComfyOutputMediaItem item)
    {
        string baseUrl = NormalizedBaseURL;
        if (string.IsNullOrEmpty(baseUrl)) return;

        string encodedRel = string.Join(
            "/",
            item.RelPath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        string url = $"{baseUrl}/minimax_api/comfyui/output_media/{encodedRel}";

        ComfyPullingOutputRelPath = item.RelPath;
        StatusMessage = L(
            $"正在从 ComfyUI 云端输出目录拉取 {item.Filename} 到本机...",
            $"Pulling {item.Filename} from ComfyUI cloud output folder to local PC..."
        );
        try
        {
            using var req = AdminRequest(url, HttpMethod.Get);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            using var resp = await AdminHttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                StatusMessage = L(
                    "拉取成片失败: 云端文件不存在或尚未写入完成",
                    "Failed to pull output: remote file does not exist or is still being written"
                );
                return;
            }

            string safeName = "comfy_" + Path.GetFileName(item.Filename);
            string saveDir = LocalRuntimePaths.LocalSaveDir;
            Directory.CreateDirectory(saveDir);
            string dstPath = Path.Combine(saveDir, safeName);
            string tmpPath = dstPath + $".{Guid.NewGuid():N}.tmp";

            try
            {
                await using (var remoteStream = await resp.Content.ReadAsStreamAsync(cts.Token))
                await using (var fileStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await remoteStream.CopyToAsync(fileStream, cts.Token);
                }
                File.Move(tmpPath, dstPath, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                throw;
            }

            RefreshLocalMedia();
            SelectMedia(dstPath);
            StatusMessage = L(
                $"已将 ComfyUI 成片保存至本地目录: {Path.GetFileName(dstPath)}",
                $"Saved ComfyUI output to local folder: {Path.GetFileName(dstPath)}"
            );
        }
        catch (Exception ex)
        {
            StatusMessage = L(
                $"下载 ComfyUI 成片失败: {ex.Message}",
                $"Failed to download ComfyUI output: {ex.Message}"
            );
        }
        finally
        {
            ComfyPullingOutputRelPath = null;
        }
    }
}
