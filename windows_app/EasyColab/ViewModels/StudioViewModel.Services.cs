using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace EasyColab.ViewModels;

// MARK: - StudioViewModel 扩展：Google OAuth 桥接、免网页唤醒、保活守护进程、本地媒体库与状态同步

internal sealed class KeepalivePipeLineBuffer
{
    private readonly Lock _lock = new();
    private readonly List<byte> _pending = [];

    public List<string> AppendAndExtractLines(ReadOnlySpan<byte> chunk)
    {
        lock (_lock)
        {
            foreach (byte b in chunk)
            {
                _pending.Add(b);
            }

            var lines = new List<string>();
            while (true)
            {
                int nlIdx = _pending.IndexOf((byte)'\n');
                if (nlIdx < 0) break;

                byte[] lineBytes = new byte[nlIdx];
                _pending.CopyTo(0, lineBytes, 0, nlIdx);
                _pending.RemoveRange(0, nlIdx + 1);

                string lineStr = Encoding.UTF8.GetString(lineBytes).Trim();
                if (!string.IsNullOrEmpty(lineStr))
                {
                    lines.Add(lineStr);
                }
            }
            return lines;
        }
    }

    public string? FlushRemainingLine()
    {
        lock (_lock)
        {
            if (_pending.Count == 0) return null;
            byte[] rem = _pending.ToArray();
            _pending.Clear();
            string lineStr = Encoding.UTF8.GetString(rem).Trim();
            return string.IsNullOrEmpty(lineStr) ? null : lineStr;
        }
    }

    public List<string> AppendAndExtractPayloads(ReadOnlySpan<byte> chunk)
    {
        const string prefix = "KEEPALIVE_JSON:";
        var lines = AppendAndExtractLines(chunk);
        var payloads = new List<string>();
        foreach (string lineStr in lines)
        {
            if (lineStr.StartsWith(prefix, StringComparison.Ordinal))
            {
                payloads.Add(lineStr[prefix.Length..]);
            }
        }
        return payloads;
    }
}

public sealed partial class StudioViewModel
{
    private bool _wakeObtainedUrlThisRun;
    private bool _isStartingKeepaliveDaemon;
    private CancellationTokenSource? _keepaliveReadCts;

    // MARK: - 内部通用辅助方法

    private void AppendConsoleLineInternal(string line)
    {
        string clean = StripEmoji(line);
        LiveConsoleLines.Add(clean);
        OnPropertyChanged(nameof(LiveConsoleLines));
    }

    private void ReplaceConsoleLinesInternal(IEnumerable<string> lines)
    {
        LiveConsoleLines.Clear();
        foreach (string line in lines)
        {
            LiveConsoleLines.Add(StripEmoji(line));
        }
        OnPropertyChanged(nameof(LiveConsoleLines));
    }

    private static DateTime NowMatchingKind(DateTime reference) =>
        reference.Kind == DateTimeKind.Local ? DateTime.Now : DateTime.UtcNow;

    private static ProcessStartInfo CreateUtf8ProcessStartInfo(
        string fileName,
        IEnumerable<string> arguments,
        bool redirectStdin = false,
        IDictionary<string, string>? extraEnv = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = redirectStdin,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        if (extraEnv is not null)
        {
            foreach (var kv in extraEnv)
            {
                psi.Environment[kv.Key] = kv.Value;
            }
        }
        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }
        return psi;
    }

    private ProcessStartInfo CreateBridgeProcessStartInfo(
        string pyBin,
        IEnumerable<string> bridgeArgs,
        bool redirectStdin = false,
        IDictionary<string, string>? extraEnv = null)
    {
        var allArgs = new List<string> { "-X", "utf8", BridgeScript };
        allArgs.AddRange(bridgeArgs);
        return CreateUtf8ProcessStartInfo(pyBin, allArgs, redirectStdin, extraEnv);
    }

    private bool IsKeepaliveProcessRunning()
    {
        lock (KeepaliveProcessLock)
        {
            if (KeepaliveProcess is null) return false;
            try
            {
                return !KeepaliveProcess.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    private async Task<(HttpStatusCode StatusCode, string Body)> SendAdminRequestAsync(
        string url,
        HttpMethod? method = null,
        string? jsonBody = null,
        double timeoutSeconds = 30.0,
        CancellationToken cancellationToken = default)
    {
        using var req = AdminRequest(url, method, jsonBody);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var resp = await AdminHttpClient.SendAsync(req, cts.Token);
        string body = await resp.Content.ReadAsStringAsync(cts.Token);
        return (resp.StatusCode, body);
    }

    // MARK: - 本地 colab_bridge.py 交互与零配置环境自举

    public async Task<string?> EnsureBridgePythonBinAsync(bool installIfMissing)
    {
        if (!string.IsNullOrEmpty(CachedBridgePythonBin) && File.Exists(CachedBridgePythonBin))
        {
            return CachedBridgePythonBin;
        }

        string? existing = await Task.Run(LocalRuntimePaths.FindReadyBridgePython);
        if (!string.IsNullOrEmpty(existing))
        {
            CachedBridgePythonBin = existing;
            return existing;
        }
        if (!installIfMissing) return null;

        string[]? sysPy = LocalRuntimePaths.FindSystemPython3();
        if (sysPy is null || sysPy.Length == 0)
        {
            StatusMessage = L(
                "未检测到 Windows Python 3 环境，请先安装 Python 3 (python.org) 或使用内置运行时版本。",
                "Windows Python 3 not found. Please install Python 3 (python.org) or use the bundled runtime build."
            );
            return null;
        }

        string venvDir = LocalRuntimePaths.ManagedVenvDir;
        string venvPy = Path.Combine(venvDir, "Scripts", "python.exe");
        string reqFile = LocalRuntimePaths.ResolveBundledOrRepoFile("requirements.txt");
        bool hasReqFile = File.Exists(reqFile);

        StatusMessage = L(
            "首次使用正在自动初始化本地 Colab 连接依赖环境 (约 15~30 秒)...",
            "Initializing local Colab bridge Python dependencies for first-time setup (~15–30s)..."
        );
        AppendConsoleLineInternal(L(
            $"[本地初始化] 正在于 {venvDir} 安装 google-colab-cli 依赖...",
            $"[Local Init] Installing google-colab-cli dependencies in {venvDir}..."
        ));

        bool ok = await Task.Run(() =>
        {
            static bool RunSync(ProcessStartInfo psi)
            {
                try
                {
                    using var p = Process.Start(psi);
                    if (p is null) return false;
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var errTask = p.StandardError.ReadToEndAsync();
                    p.WaitForExit();
                    try { Task.WaitAll(outTask, errTask); } catch { }
                    return p.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            }

            if (!File.Exists(venvPy))
            {
                var mkArgs = new List<string>();
                for (int i = 1; i < sysPy.Length; i++) mkArgs.Add(sysPy[i]);
                mkArgs.Add("-m");
                mkArgs.Add("venv");
                mkArgs.Add(venvDir);
                var mkPsi = CreateUtf8ProcessStartInfo(sysPy[0], mkArgs);
                if (!RunSync(mkPsi)) return false;
            }

            string[] pipArgs = hasReqFile
                ? ["-m", "pip", "install", "-q", "--upgrade", "pip", "-r", reqFile]
                : [
                    "-m", "pip", "install", "-q", "--upgrade", "pip",
                    "google-colab-cli>=0.7.5",
                    "google-auth>=2.28.0",
                    "google-auth-oauthlib>=1.2.0",
                    "requests>=2.31.0",
                    "requests-oauthlib>=1.3.1",
                    "websocket-client>=1.7.0",
                ];
            var pipPsi = CreateUtf8ProcessStartInfo(venvPy, pipArgs);
            return RunSync(pipPsi);
        });

        if (ok && File.Exists(venvPy))
        {
            CachedBridgePythonBin = venvPy;
            AppendConsoleLineInternal(L(
                "[本地初始化] Colab 本地连接组件已就绪。",
                "[Local Init] Local Colab bridge environment is ready."
            ));
            return venvPy;
        }
        else
        {
            StatusMessage = L(
                "自动安装本地连接依赖失败，请检查网络或执行 pip install -r requirements.txt。",
                "Failed to install local bridge dependencies. Check your network or run pip install -r requirements.txt."
            );
            return null;
        }
    }

    public static string ExtractJSONPayload(string rawOutput)
    {
        string trimmed = (rawOutput ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed)) return "";
        string[] lines = SplitNewlines(trimmed);
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string candidate = lines[i].Trim();
            if (candidate.StartsWith('{') && candidate.EndsWith('}'))
            {
                return candidate;
            }
        }
        return trimmed;
    }

    public async Task<string> RunBridgeCommandAsync(IReadOnlyList<string> args, bool installIfMissing = false)
    {
        string? pyBin = await EnsureBridgePythonBinAsync(installIfMissing);
        if (string.IsNullOrEmpty(pyBin))
        {
            return "";
        }

        var psi = CreateBridgeProcessStartInfo(pyBin, args);
        return await Task.Run(() =>
        {
            try
            {
                using var proc = Process.Start(psi);
                if (proc is null) return "";
                var errDrain = proc.StandardError.ReadToEndAsync();
                string stdout = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                try { errDrain.Wait(1000); } catch { }
                return ExtractJSONPayload(stdout);
            }
            catch
            {
                return "";
            }
        });
    }

    public async Task RefreshOfficialColabAccountUsageAsync()
    {
        if (_isReleasingColab) return;

        int releaseGeneration = _colabReleaseGeneration;
        string output = await RunBridgeCommandAsync(["auth_status"], installIfMissing: false);
        if (!IsCurrentColabReleaseGeneration(releaseGeneration)) return;

        var decoded = ModelJson.TryDecode<BridgeAuthStatusResponse>(output);
        if (decoded is null) return;

        CliLoggedIn = decoded.LoggedIn ?? false;
        if (!string.IsNullOrEmpty(decoded.Email))
        {
            AccountEmail = decoded.Email;
        }
        else if (!CliLoggedIn)
        {
            AccountEmail = "";
        }
        if (decoded.PaidBalance is { } bal)
        {
            OfficialCUBalance = bal;
            TotalBudgetCU = bal;
        }
        if (decoded.BurnRateHourly is { } rate)
        {
            OfficialBurnRate = rate;
        }
        if (decoded.ActiveAssignments is { } count)
        {
            ActiveVMCount = count;
            if (count == 0 && !IsConnected && (LoadedModelsDetail.Count > 0 || VramTotalGB > 0))
            {
                ResetStaleRemoteRuntimeState(L(
                    "云端 Colab 实例当前未运行，已自动清理历史显存状态；点击左侧「唤醒实例并部署」即可启动。",
                    "No active cloud Colab runtime detected; cleared stale VRAM state. Click 'Wake & Deploy' on the left to launch."
                ));
            }
        }
    }

    public async Task StartGoogleOAuthLoginAsync(bool switchingAccount = false)
    {
        string output = await RunBridgeCommandAsync(["auth_url"], installIfMissing: true);
        string? authUrlStr = null;
        if (!string.IsNullOrEmpty(output))
        {
            try
            {
                using var doc = JsonDocument.Parse(output);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("auth_url", out var urlProp)
                    && urlProp.ValueKind == JsonValueKind.String)
                {
                    authUrlStr = urlProp.GetString();
                }
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(authUrlStr)
            || !Uri.TryCreate(authUrlStr, UriKind.Absolute, out var uri))
        {
            if (!StatusMessage.Contains("初始化", StringComparison.Ordinal)
                && !StatusMessage.Contains("Initializ", StringComparison.Ordinal)
                && !StatusMessage.Contains("Python", StringComparison.Ordinal))
            {
                StatusMessage = L("获取 Google 授权链接失败", "Failed to obtain Google OAuth authorization URL");
            }
            return;
        }

        OauthCodeText = "";
        ShowOAuthCodeInput = true;
        LocalRuntimePaths.OpenUrl(uri.AbsoluteUri);
        StatusMessage = switchingAccount
            ? L(
                "已在浏览器打开 Google 账号选择页，请选择新账号授权并粘贴授权码（点取消可保留原账号）。",
                "Opened Google account chooser in browser. Authorize a new account and paste the code (Cancel keeps current account)."
            )
            : L(
                "已在浏览器打开 Google 授权页，请允许访问后粘贴授权码。",
                "Opened Google authorization page in browser. Grant access and paste the auth code."
            );
    }

    public async Task LogoutGoogleAccountAsync()
    {
        StopActiveWakeProcess();
        StopKeepaliveDaemon();
        _ = await RunBridgeCommandAsync(["auth_logout"], installIfMissing: false);
        CliLoggedIn = false;
        AccountEmail = "";
        ShowOAuthCodeInput = false;
        OauthCodeText = "";
        OfficialCUBalance = null;
        OfficialBurnRate = null;
        ActiveVMCount = 0;
        ColabServerURL = "";
        ResetStaleRemoteRuntimeState(L(
            "已退出当前 Google 账号绑定；可点击左侧「绑定」登录其他账号。",
            "Signed out of Google account. Click 'Bind' on the left to sign in with another account."
        ));
    }

    public async Task SubmitGoogleOAuthCodeAsync()
    {
        string code = (OauthCodeText ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(code)) return;

        string prevEmail = AccountEmail;
        bool wasLoggedIn = CliLoggedIn;
        StatusMessage = L("正在验证授权码并同步账户余额...", "Verifying authorization code & syncing CU balance...");
        string output = await RunBridgeCommandAsync(["auth_code", code], installIfMissing: true);
        var decoded = ModelJson.TryDecode<BridgeAuthStatusResponse>(output);
        if (decoded is null)
        {
            StatusMessage = L("授权验证失败，请重试。", "Authorization verification failed. Please try again.");
            return;
        }

        if (decoded.LoggedIn == true)
        {
            string newEmail = decoded.Email ?? "";
            if (wasLoggedIn && (prevEmail != newEmail || !string.IsNullOrEmpty(ColabServerURL)))
            {
                ColabServerURL = "";
                ResetStaleRemoteRuntimeState("");
            }
            CliLoggedIn = true;
            AccountEmail = newEmail;
            ShowOAuthCodeInput = false;
            OauthCodeText = "";
            OfficialCUBalance = decoded.PaidBalance;
            OfficialBurnRate = decoded.BurnRateHourly;
            ActiveVMCount = decoded.ActiveAssignments ?? 0;
            if (decoded.PaidBalance is { } bal)
            {
                TotalBudgetCU = bal;
            }
            string emailTag = string.IsNullOrEmpty(newEmail) ? "" : $" ({DisplayAccountLabel})";
            string balStr = CFormatFixed(decoded.PaidBalance ?? 0.0, 2);
            StatusMessage = L(
                $"Google 账号绑定成功{emailTag}！可用额度: {balStr} CU",
                $"Google account bound{emailTag}! Available balance: {balStr} CU"
            );
        }
        else
        {
            StatusMessage = L(
                $"授权码无效: {decoded.Error ?? "请重新获取"}",
                $"Invalid authorization code: {decoded.Error ?? "please generate a new code"}"
            );
        }
    }

    // MARK: - 免网页唤醒与实时日志流

    public void StopActiveWakeProcess()
    {
        Process? proc;
        lock (ActiveWakeProcessLock)
        {
            proc = ActiveWakeProcess;
            ActiveWakeProcess = null;
        }
        if (proc is not null)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch { }
            try { proc.Dispose(); } catch { }
        }
    }

    public void HandleWakeOutputLine(string line, string gpu, string password)
    {
        if (line.StartsWith("WAKE_URL:", StringComparison.Ordinal))
        {
            string url = line["WAKE_URL:".Length..].Trim();
            ColabServerURL = url;
            AdminPassword = password;
            _wakeObtainedUrlThisRun = true;
            AppendConsoleLineInternal(L(
                $"成功获取云端隧道地址: {MaskedURLDisplay(url)}",
                $"Obtained public tunnel URL: {MaskedURLDisplay(url)}"
            ));
            StatusMessage = L(
                $"云端 {gpu} 实例已唤醒就绪，正在同步模型状态...",
                $"Cloud {gpu} instance is ready. Syncing model status..."
            );
        }
        else if (line.StartsWith("WAKE_ERROR:", StringComparison.Ordinal))
        {
            string errBody = line["WAKE_ERROR:".Length..].Trim();
            string displayErr = string.IsNullOrEmpty(errBody) ? line : errBody;
            AppendConsoleLineInternal(displayErr);
            StatusMessage = L($"唤醒失败: {displayErr}", $"Wake failed: {displayErr}");
        }
        else if (line.StartsWith("WAKE_LOG:", StringComparison.Ordinal))
        {
            string clean = line["WAKE_LOG:".Length..].Trim();
            if (!string.IsNullOrEmpty(clean))
            {
                AppendConsoleLineInternal(clean);
                StatusMessage = clean;
            }
        }
        else if (!line.StartsWith("[colab]", StringComparison.Ordinal)
                 && !line.Contains("Assignment request failed", StringComparison.Ordinal)
                 && !line.Contains("urllib3", StringComparison.Ordinal))
        {
            AppendConsoleLineInternal(line);
        }
    }

    public async Task WakeColabHeadlessAsync()
    {
        if (!CliLoggedIn)
        {
            StatusMessage = L(
                "请先绑定 Google 账号后再使用免网页一键唤醒。",
                "Please bind your Google account before using headless 1-click wake."
            );
            return;
        }

        StopActiveWakeProcess();
        StopKeepaliveDaemon();
        _wakeObtainedUrlThisRun = false;
        IsWakingColab = true;
        StatusMessage = L(
            $"正在通过 Google Colab API 后台分配 {WakeGPUChoice} 实例并部署多模型引擎...",
            $"Provisioning {WakeGPUChoice} runtime via Google Colab API & deploying multi-model engine..."
        );
        ReplaceConsoleLinesInternal([L(
            $"[Colab-CLI] 正在启动免网页一键唤醒流程 ({WakeGPUChoice} GPU)...",
            $"[Colab-CLI] Starting headless 1-click wake ({WakeGPUChoice} GPU)..."
        )]);

        string? pyBin = await EnsureBridgePythonBinAsync(installIfMissing: true);
        if (string.IsNullOrEmpty(pyBin))
        {
            IsWakingColab = false;
            return;
        }

        string gpu = WakeGPUChoice;
        string password = Guid.NewGuid().ToString("D").ToUpperInvariant() + Guid.NewGuid().ToString("D").ToUpperInvariant();
        int idleMin = IdleTimeoutMinutes;
        string appPy = AppPyPath;

        var extraEnv = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["EASYCOLAB_ADMIN_PASSWORD"] = password,
            ["EASYCOLAB_IDLE_TIMEOUT_MINUTES"] = idleMin.ToString(CultureInfo.InvariantCulture),
        };
        var psi = CreateBridgeProcessStartInfo(pyBin, ["wake", "--gpu", gpu, "--app-py", appPy], extraEnv: extraEnv);

        Process? proc = null;
        try
        {
            proc = Process.Start(psi);
            if (proc is null)
            {
                StatusMessage = L("启动唤醒进程失败: 无法创建子进程", "Failed to launch wake process: unable to start child process");
                IsWakingColab = false;
                return;
            }
            lock (ActiveWakeProcessLock)
            {
                ActiveWakeProcess = proc;
            }

            async Task PumpStreamAsync(Stream stream)
            {
                var lineBuffer = new KeepalivePipeLineBuffer();
                byte[] buf = new byte[4096];
                try
                {
                    while (true)
                    {
                        int n = await stream.ReadAsync(buf);
                        if (n <= 0) break;
                        var lines = lineBuffer.AppendAndExtractLines(buf.AsSpan(0, n));
                        if (lines.Count > 0)
                        {
                            await UiDispatcher.RunAsync(() =>
                            {
                                foreach (string line in lines)
                                {
                                    HandleWakeOutputLine(line, gpu, password);
                                }
                            });
                        }
                    }
                    if (lineBuffer.FlushRemainingLine() is { } rem)
                    {
                        await UiDispatcher.RunAsync(() => HandleWakeOutputLine(rem, gpu, password));
                    }
                }
                catch { }
            }

            var stdoutTask = Task.Run(() => PumpStreamAsync(proc.StandardOutput.BaseStream));
            var stderrTask = Task.Run(() => PumpStreamAsync(proc.StandardError.BaseStream));

            await Task.Run(() =>
            {
                try { proc.WaitForExit(); } catch { }
            });
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch (Exception ex)
        {
            StatusMessage = L(
                $"启动唤醒进程失败: {ex.Message}",
                $"Failed to launch wake process: {ex.Message}"
            );
        }
        finally
        {
            if (proc is not null)
            {
                lock (ActiveWakeProcessLock)
                {
                    if (ReferenceEquals(ActiveWakeProcess, proc))
                    {
                        ActiveWakeProcess = null;
                    }
                }
                try { proc.Dispose(); } catch { }
            }
        }

        IsWakingColab = false;
        await RefreshOfficialColabAccountUsageAsync();
        if (_wakeObtainedUrlThisRun && !string.IsNullOrEmpty(ColabServerURL))
        {
            await CheckColabStatusAsync();
            await EnsureKeepaliveDaemonRunningAsync();
        }
    }

    // MARK: - 常驻外部 Runtime Proxy 保活守护进程

    public void StopKeepaliveDaemon()
    {
        try { _keepaliveReadCts?.Cancel(); } catch { }
        try { _keepaliveReadCts?.Dispose(); } catch { }
        _keepaliveReadCts = null;

        Process? proc;
        lock (KeepaliveProcessLock)
        {
            proc = KeepaliveProcess;
            KeepaliveProcess = null;
        }
        if (proc is not null)
        {
            try { proc.StandardInput.Close(); } catch { }
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch { }
            try { proc.Dispose(); } catch { }
        }

        UiDispatcher.Run(() => KeepaliveProxyWSConnected = false);
    }

    public void HandleKeepaliveDaemonPayloadLine(string jsonLine)
    {
        var decoded = ModelJson.TryDecode<BridgeAuthStatusResponse>(jsonLine);
        if (decoded is null) return;

        if (decoded.LoggedIn is { } logged)
        {
            CliLoggedIn = logged;
        }
        if (decoded.WsConnected is { } wsConn)
        {
            KeepaliveProxyWSConnected = wsConn;
        }
        if (decoded.PaidBalance is { } bal)
        {
            OfficialCUBalance = bal;
            TotalBudgetCU = bal;
        }
        if (decoded.BurnRateHourly is { } rate)
        {
            OfficialBurnRate = rate;
        }
        if (decoded.ActiveAssignments is { } count)
        {
            ActiveVMCount = count;
        }
    }

    public async Task EnsureKeepaliveDaemonRunningAsync()
    {
        if (!CliLoggedIn || !IsConnected || IsWakingColab || IgnoreColabRuntimeStatus) return;
        if (IsKeepaliveProcessRunning()) return;
        if (_isStartingKeepaliveDaemon) return;

        _isStartingKeepaliveDaemon = true;
        int releaseGeneration = _colabReleaseGeneration;
        try
        {
            string? pyBin = await EnsureBridgePythonBinAsync(installIfMissing: false);
            if (string.IsNullOrEmpty(pyBin)) return;
            if (!CliLoggedIn || !IsConnected || IsWakingColab || !IsCurrentColabReleaseGeneration(releaseGeneration)) return;
            if (IsKeepaliveProcessRunning()) return;

            StopKeepaliveDaemon();

            var psi = CreateBridgeProcessStartInfo(pyBin, ["keepalive_daemon"], redirectStdin: true);
            Process? proc;
            try
            {
                proc = Process.Start(psi);
            }
            catch
            {
                return;
            }
            if (proc is null) return;

            var readCts = new CancellationTokenSource();
            _keepaliveReadCts = readCts;
            lock (KeepaliveProcessLock)
            {
                KeepaliveProcess = proc;
            }

            CancellationToken ct = readCts.Token;
            var lineBuffer = new KeepalivePipeLineBuffer();

            _ = Task.Run(async () =>
            {
                try
                {
                    using var errReader = proc.StandardError;
                    await errReader.ReadToEndAsync(ct);
                }
                catch { }
            });

            _ = Task.Run(async () =>
            {
                byte[] buf = new byte[4096];
                try
                {
                    Stream stdout = proc.StandardOutput.BaseStream;
                    while (!ct.IsCancellationRequested)
                    {
                        int n = await stdout.ReadAsync(buf, ct);
                        if (n <= 0) break;
                        var payloads = lineBuffer.AppendAndExtractPayloads(buf.AsSpan(0, n));
                        if (payloads.Count > 0)
                        {
                            UiDispatcher.Run(() =>
                            {
                                if (ct.IsCancellationRequested) return;
                                foreach (string payload in payloads)
                                {
                                    HandleKeepaliveDaemonPayloadLine(payload);
                                }
                            });
                        }
                    }
                }
                catch { }
                finally
                {
                    UiDispatcher.Run(() =>
                    {
                        bool cleared = false;
                        lock (KeepaliveProcessLock)
                        {
                            if (ReferenceEquals(KeepaliveProcess, proc))
                            {
                                KeepaliveProcess = null;
                                cleared = true;
                            }
                        }
                        if (cleared)
                        {
                            KeepaliveProxyWSConnected = false;
                            try { proc.StandardInput.Close(); } catch { }
                            try { proc.Dispose(); } catch { }
                        }
                    });
                }
            });
        }
        finally
        {
            _isStartingKeepaliveDaemon = false;
        }
    }

    public async Task KeepaliveColabRuntimeAsync()
    {
        if (!CliLoggedIn || IsWakingColab || IgnoreColabRuntimeStatus) return;
        int releaseGeneration = _colabReleaseGeneration;
        if (IsConnected)
        {
            await EnsureKeepaliveDaemonRunningAsync();
            if (!IsCurrentColabReleaseGeneration(releaseGeneration) || IsKeepaliveProcessRunning())
            {
                return;
            }
        }
        string output = await RunBridgeCommandAsync(["keepalive"]);
        if (!IsCurrentColabReleaseGeneration(releaseGeneration)) return;

        var decoded = ModelJson.TryDecode<BridgeAuthStatusResponse>(output);
        if (decoded is null) return;

        if (decoded.PaidBalance is { } bal)
        {
            OfficialCUBalance = bal;
            TotalBudgetCU = bal;
        }
        if (decoded.BurnRateHourly is { } rate)
        {
            OfficialBurnRate = rate;
        }
        if (decoded.ActiveAssignments is { } count)
        {
            ActiveVMCount = count;
        }
    }

    public void StartBackgroundTelemetryLoop()
    {
        try { TelemetryLoopCts?.Cancel(); } catch { }
        try { TelemetryLoopCts?.Dispose(); } catch { }
        var cts = new CancellationTokenSource();
        TelemetryLoopCts = cts;
        _ = RunBackgroundTelemetryLoopAsync(cts.Token);
    }

    private async Task RunBackgroundTelemetryLoopAsync(CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrEmpty(NormalizedBaseURL))
            {
                await CheckColabStatusAsync(silent: true);
            }
            int tick = 0;
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2500, ct);
                tick += 1;
                if (IsConnected)
                {
                    // 生成任务进行中由 pollColabJob 附带 telemetry 实时更新图表，仅每 15s 低频同步一次全局状态，避免争抢 Gradio 隧道
                    if (!IsGenerating || tick % 6 == 0)
                    {
                        await CheckColabStatusAsync(silent: true);
                    }
                    if (CliLoggedIn && !IsWakingColab)
                    {
                        await EnsureKeepaliveDaemonRunningAsync();
                    }
                }
                else
                {
                    bool hasKeepalive;
                    lock (KeepaliveProcessLock) { hasKeepalive = KeepaliveProcess is not null; }
                    if (hasKeepalive)
                    {
                        StopKeepaliveDaemon();
                    }
                    if (!string.IsNullOrEmpty(NormalizedBaseURL) && tick % 4 == 0)
                    {
                        await CheckColabStatusAsync(silent: true);
                    }
                }

                // 未连接云端实例时每 ~40s 低频刷新一次官方 CU 余额与活跃分配数；已连接时由常驻 keepalive_daemon 实时回传
                if (tick % 16 == 0 && CliLoggedIn && !IsWakingColab && !IsConnected)
                {
                    await RefreshOfficialColabAccountUsageAsync();
                }
                else if (tick % 24 == 0 && CliLoggedIn && !IsWakingColab && IsConnected && !IsKeepaliveProcessRunning())
                {
                    await KeepaliveColabRuntimeAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch
        {
            // Guard against unexpected telemetry exception
        }
    }

    // MARK: - 本地历史媒体库（支持 MP4 视频与 PNG/JPG 图片，兼容旧目录）

    private static readonly HashSet<string> ValidMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".png", ".jpg", ".jpeg", ".webp",
    };

    public void RefreshLocalMedia()
    {
        var allFiles = new List<string>();
        foreach (string dir in LocalRuntimePaths.LegacyMediaDirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (string file in Directory.EnumerateFiles(dir))
                {
                    string fname = Path.GetFileName(file);
                    if (string.IsNullOrEmpty(fname) || fname.StartsWith('.')) continue;
                    try
                    {
                        var attrs = File.GetAttributes(file);
                        if ((attrs & FileAttributes.Hidden) != 0) continue;
                    }
                    catch { }
                    allFiles.Add(file);
                }
            }
            catch { }
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<LocalMediaItem>();
        foreach (string path in allFiles)
        {
            string ext = Path.GetExtension(path);
            if (!ValidMediaExtensions.Contains(ext)) continue;
            string fname = Path.GetFileName(path);
            if (!seenNames.Add(fname)) continue;

            DateTime createdAt;
            try { createdAt = File.GetCreationTime(path); }
            catch { createdAt = DateTime.MinValue; }

            double? elapsed = MediaElapsedMap.TryGetValue(fname, out double el) ? el : null;
            items.Add(new LocalMediaItem
            {
                Url = path,
                Filename = fname,
                IsImage = !ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase),
                CreatedAt = createdAt,
                ElapsedSeconds = elapsed,
            });
        }

        items.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        LocalMediaItems = [.. items];

        if (CurrentMediaURL is null && items.Count > 0)
        {
            SelectMedia(items[0].Url);
        }
    }

    public void SelectMedia(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            CurrentMediaURL = null;
            return;
        }
        bool samePath = string.Equals(CurrentMediaURL, filePath, StringComparison.OrdinalIgnoreCase);
        CurrentMediaURL = filePath;
        if (samePath)
        {
            OnPropertyChanged(nameof(CurrentMediaURL));
        }
    }

    // MARK: - 远端状态响应应用

    public void ApplyStatusResponse(ColabStatusResponse decoded)
    {
        if (IgnoreColabRuntimeStatus) return;

        IsConnected = true;
        GpuName = decoded.GpuName ?? "Colab GPU";
        VramFreeGB = decoded.VramFreeGb ?? 0.0;
        VramTotalGB = decoded.VramTotalGb ?? 0.0;
        VramUsedGB = decoded.VramUsedGb ?? 0.0;
        RamTotalGB = decoded.RamTotalGb ?? RamTotalGB;
        RamUsedGB = decoded.RamUsedGb ?? RamUsedGB;
        RamFreeGB = decoded.RamFreeGb ?? RamFreeGB;
        DiskTotalGB = decoded.DiskTotalGb ?? DiskTotalGB;
        DiskUsedGB = decoded.DiskUsedGb ?? DiskUsedGB;
        DiskFreeGB = decoded.DiskFreeGb ?? DiskFreeGB;
        ShmTotalGB = decoded.ShmTotalGb ?? ShmTotalGB;
        ShmUsedGB = decoded.ShmUsedGb ?? ShmUsedGB;
        ShmFreeGB = decoded.ShmFreeGb ?? ShmFreeGB;
        DriveTotalGB = decoded.DriveTotalGb ?? DriveTotalGB;
        DriveUsedGB = decoded.DriveUsedGb ?? DriveUsedGB;
        DriveFreeGB = decoded.DriveFreeGb ?? DriveFreeGB;
        RecordTelemetrySample();

        if (decoded.CustomModels is { } remoteCustom)
        {
            MergeRemoteCustomModels(remoteCustom);
        }

        LoadedModelId = decoded.LoadedModelId;
        if (decoded.LoadedModelIds is { } ids)
        {
            LoadedModelIds = [.. ids];
        }
        else if (!string.IsNullOrEmpty(decoded.LoadedModelId) && decoded.ModelLoaded == true)
        {
            LoadedModelIds = [decoded.LoadedModelId];
        }
        else if (decoded.AnyModelLoaded == false)
        {
            LoadedModelIds = [];
        }

        if (decoded.LoadedModelsDetail is { } details)
        {
            LoadedModelsDetail =
            [
                .. details.Select(item => item with
                {
                    Name = StripEmoji(item.Name),
                    Mode = item.Mode is not null ? StripEmoji(item.Mode) : null,
                }),
            ];
        }
        else if (decoded.AnyModelLoaded == false)
        {
            LoadedModelsDetail = [];
        }

        ModelLoaded = LoadedModelIds.Contains(SelectedModelId) || (decoded.ModelLoaded ?? false);
        CurrentModelName = decoded.CurrentModel ?? L("未加载", "Not Loaded");
        LoadedContextLengthTokens = decoded.LoadedContextLength ?? 0;

        if (decoded.ContextLength is { } remoteCtx)
        {
            if (remoteCtx == ContextLengthTokens)
            {
                RemoteContextSynced = true;
            }
            else if (!RemoteContextSynced && (NowMatchingKind(LastContextSyncAttemptAt) - LastContextSyncAttemptAt).TotalSeconds >= 8.0)
            {
                LastContextSyncAttemptAt = NowMatchingKind(LastContextSyncAttemptAt);
                _ = SyncContextLengthToColabAsync();
            }
        }

        if (decoded.Files is { Count: > 0 } files)
        {
            ModelFiles = [.. files];
        }
        DownloadedCount = decoded.DownloadedCount ?? DownloadedCount;
        TotalFileCount = decoded.TotalCount ?? ActiveModelSpec.DefaultFiles.Count;
        AllDownloaded = decoded.AllDownloaded ?? (DownloadedCount == TotalFileCount && TotalFileCount > 0);
        TotalDiskSizeGB = decoded.TotalSizeGb ?? TotalDiskSizeGB;
        DrivePersisted = decoded.DrivePersisted ?? false;
        Bf16ShardsCount = decoded.Bf16ShardsCount ?? 0;
        IsDownloadingWeights = decoded.IsDownloading ?? false;
        IsLoadingModel = decoded.IsLoadingVram ?? false;

        if (CliLoggedIn && !IsWakingColab && !IsKeepaliveProcessRunning())
        {
            _ = EnsureKeepaliveDaemonRunningAsync();
        }
        if (decoded.TaskProgress is { } p)
        {
            ModelTaskProgress = p;
        }

        UptimeSeconds = decoded.UptimeSeconds ?? UptimeSeconds;
        CuBurnRatePerHour = decoded.CuBurnRatePerHour ?? CuBurnRatePerHour;
        SessionCUUsed = decoded.SessionCuUsed ?? SessionCUUsed;
        IdleRemainingSeconds = decoded.IdleRemainingSeconds ?? IdleRemainingSeconds;

        if (decoded.IdleTimeoutMinutes is { } remoteIdle)
        {
            if (remoteIdle == IdleTimeoutMinutes)
            {
                RemoteIdleSynced = true;
            }
            else if ((NowMatchingKind(LastIdleSyncAttemptAt) - LastIdleSyncAttemptAt).TotalSeconds >= 8.0)
            {
                RemoteIdleSynced = false;
                LastIdleSyncAttemptAt = NowMatchingKind(LastIdleSyncAttemptAt);
                int targetIdle = IdleTimeoutMinutes;
                _ = UpdateIdleTimeoutConfigAsync(targetIdle);
            }
        }

        ApiRequestCount = decoded.ApiRequestCount ?? ApiRequestCount;
        if (decoded.ApiKeyConfigured == false && !RemoteApiKeySynced)
        {
            RemoteApiKeySynced = true;
            _ = SyncApiKeyToColabAsync(initializeOnly: true);
        }

        if (decoded.ConsoleLines is { Count: > 0 } lines)
        {
            ReplaceConsoleLinesInternal(lines);
        }

        if (decoded.Comfyui is { } cf)
        {
            ComfyRunning = cf.IsRunning ?? false;
            ComfyStarting = cf.IsStarting ?? false;
            ComfySyncingDrive = cf.IsSyncingDrive ?? false;
            ComfyDownloadingWeight = cf.IsDownloadingWeight ?? false;
            ComfyInstallingPackId = cf.InstallingPackId ?? "";
            ComfyProgress = cf.Progress ?? 0.0;
            if (!string.IsNullOrEmpty(cf.StatusMessage))
            {
                ComfyStatusMessage = cf.StatusMessage;
            }
            ComfyPublicURL = cf.PublicUrl ?? "";
            ComfyQueueRunning = cf.QueueRunning ?? 0;
            ComfyQueuePending = cf.QueuePending ?? 0;
            if (!string.IsNullOrEmpty(cf.PersistenceLabel))
            {
                ComfyPersistenceLabel = cf.PersistenceLabel;
            }
            ComfyDrivePersisted = cf.DrivePersisted ?? DrivePersisted;
            if (!string.IsNullOrEmpty(cf.PersistRoot))
            {
                ComfyPersistRoot = cf.PersistRoot;
            }
            ComfyCustomNodesCount = cf.CustomNodesCount ?? 0;
            ComfyModelsCount = cf.ModelsCount ?? 0;
            ComfyWorkflowsCount = cf.WorkflowsCount ?? 0;
            if (cf.RecentOutputs is { } outs)
            {
                ComfyRecentOutputs = [.. outs];
            }
            if (cf.PresetPacks is { Count: > 0 } packs)
            {
                ComfyPresetPacks = [.. packs];
            }
        }
    }

    // MARK: - API Key 与上下文长度远端同步

    public void RegenerateApiKey()
    {
        ApiAccessKey = "sk-colab-" + Guid.NewGuid().ToString("N")[..20].ToLowerInvariant();
        _ = SyncApiKeyToColabAsync();
    }

    public async Task SyncApiKeyToColabAsync(bool initializeOnly = false)
    {
        string baseUrl = NormalizedBaseURL;
        if (!IsConnected || string.IsNullOrEmpty(baseUrl)) return;
        string payloadKey = (ApiAccessKey ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(payloadKey)) return;

        try
        {
            string body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["api_key"] = payloadKey,
                ["initialize_only"] = initializeOnly,
            }, ModelJson.Options);
            var (_, respBody) = await SendAdminRequestAsync($"{baseUrl}/minimax_api/config_api_key", HttpMethod.Post, body, timeoutSeconds: 20);
            var decoded = ModelJson.Decode<ColabStatusResponse>(respBody);
            if (decoded is not null)
            {
                RemoteApiKeySynced = true;
                ApplyStatusResponse(decoded);
            }
        }
        catch { }
    }

    public async Task SyncContextLengthToColabAsync(bool announce = false)
    {
        string baseUrl = NormalizedBaseURL;
        if (!IsConnected || string.IsNullOrEmpty(baseUrl)) return;
        int targetTokens = ContextLengthTokens;

        try
        {
            string body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["context_length"] = targetTokens,
            }, ModelJson.Options);
            var (_, respBody) = await SendAdminRequestAsync($"{baseUrl}/minimax_api/config_context", HttpMethod.Post, body, timeoutSeconds: 20);
            var decoded = ModelJson.Decode<ColabStatusResponse>(respBody);
            if (decoded is not null)
            {
                RemoteContextSynced = true;
                ApplyStatusResponse(decoded);
                if (announce && !string.IsNullOrEmpty(decoded.TaskMessage))
                {
                    StatusMessage = decoded.TaskMessage;
                }
            }
        }
        catch { }
    }
}
