using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EasyColab.Localization;

/// <summary>
/// One styled inline run produced by <see cref="TextHelpers.RenderHFInlineMarkdown"/>
/// (C# replacement for the Swift <c>AttributedString</c> result).
/// </summary>
public sealed record HFInlineRun(
    string Text,
    bool Bold = false,
    bool Italic = false,
    bool Code = false,
    bool Strikethrough = false,
    string? Link = null);

/// <summary>
/// Port of all non-View helpers of LocalizationAndText.swift. Imported with
/// <c>global using static EasyColab.Localization.TextHelpers;</c> so call sites read exactly like Swift
/// (<c>LocalizeServerText(x)</c>, <c>StripEmoji(x)</c>, <c>UNSUPPORTED_HF_PIPELINE_TAGS.Contains(tag)</c> ...).
/// </summary>
public static class TextHelpers
{
    // MARK: - C printf-compatible fixed formatting

    /// <summary>
    /// Equivalent of Swift <c>String(format: "%.Nf", v)</c>: invariant culture, round-half-even on exact binary ties
    /// (the .NET "F" format rounds exact ties away from zero, printf does not).
    /// </summary>
    public static string CFormatFixed(double v, int digits)
    {
        if (double.IsNaN(v)) return "nan";
        if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
        string fmt = "F" + digits.ToString(CultureInfo.InvariantCulture);
        double t = v * Math.Pow(2, digits + 1);
        if (Math.Abs(t) < 9e15 && t == Math.Floor(t) && Math.Abs(t % 2) == 1)
        {
            double p10 = Math.Pow(10, digits);
            double r = Math.Round(v * p10, MidpointRounding.ToEven) / p10;
            return r.ToString(fmt, CultureInfo.InvariantCulture);
        }
        return v.ToString(fmt, CultureInfo.InvariantCulture);
    }

    // MARK: - Localisation of server / model texts

    public static string LocalizeModelFileDesc(string raw)
    {
        if (!LanguageManager.Shared.IsEN || string.IsNullOrEmpty(raw)) return raw;
        var s = raw;
        s = s.Replace(" [RAM内存盘]", " [RAM Disk]", StringComparison.Ordinal);
        s = s.Replace("DiT 音视频主干 BF16", "DiT Audio-Video Backbone BF16", StringComparison.Ordinal);
        s = s.Replace("多模态编码器 BF16", "Multimodal Encoder BF16", StringComparison.Ordinal);
        s = s.Replace("3D 视频 VAE BF16", "3D Video VAE BF16", StringComparison.Ordinal);
        s = s.Replace("32kHz 立体声 VAE BF16", "32kHz Stereo Audio VAE BF16", StringComparison.Ordinal);
        s = s.Replace("权重分片", "Weight Shard", StringComparison.Ordinal);
        s = s.Replace("Unsloth UD-Q4_K_M 主模型", "Unsloth UD-Q4_K_M Main Model", StringComparison.Ordinal);
        s = s.Replace("多模态视觉投影器", "Multimodal Vision Projector", StringComparison.Ordinal);
        s = s.Replace("Unsloth MTP 多词元投机解码头", "Unsloth MTP Speculative Decoding Head", StringComparison.Ordinal);
        s = s.Replace("编码器", "Encoder", StringComparison.Ordinal);
        s = s.Replace("图像 VAE", "Image VAE", StringComparison.Ordinal);
        return s;
    }

    private static readonly Dictionary<string, string> ServerTextExactMap = new(StringComparer.Ordinal)
    {
        ["未加载"] = "Not Loaded",
        ["未连接 Colab 服务端"] = "Colab Server Disconnected",
        ["尚未启动云端 ComfyUI 工作台"] = "Cloud ComfyUI Studio Not Started",
        ["尚未启动（支持一键拉起 ComfyUI + Google Drive 自动软链持久化）"] = "Not started (1-click ComfyUI + Google Drive auto-symlink persistence ready)",
        ["Google Drive 自动软链持久化"] = "Google Drive Auto-Symlink Persistence",
        ["Google Drive 实时软链挂载"] = "Google Drive Live Symlink Mount",
        ["Google Drive 云盘自动持久化"] = "Google Drive Auto-Persistence",
        ["实例本地高速存储"] = "Instance Local NVMe Storage",
        ["就绪：默认已选 G4 (RTX PRO 6000 · 96GB)，可在左侧一键唤醒实例并部署模型。"] = "Ready: G4 (RTX PRO 6000 · 96GB) selected by default. Wake & deploy from the left sidebar.",
        ["已彻底释放全部云端实例，计费速率归零。"] = "All cloud Colab instances terminated. Billing rate is now 0 CU/h.",
        ["正在初始化 ComfyUI 环境与 Google Drive 持久化目录..."] = "Initializing ComfyUI environment & Google Drive persistence folders...",
        ["正在释放现有常驻模型显存，为 ComfyUI 腾出完整 96GB 空间..."] = "Releasing resident models from VRAM to free full 96GB for ComfyUI...",
        ["正在从 Google Drive 恢复历史持久化的 ComfyUI 节点、模型与工作流..."] = "Restoring persisted ComfyUI nodes, models & workflows from Google Drive...",
        ["正在克隆官方最新 ComfyUI 核心引擎..."] = "Cloning latest official ComfyUI core engine...",
        ["正在安装 ComfyUI 核心依赖（保留当前 G4 Blackwell CUDA PyTorch）..."] = "Installing ComfyUI core dependencies (preserving G4 Blackwell CUDA PyTorch)...",
        ["正在恢复已持久化的自定义节点并配置 ComfyUI-Manager..."] = "Restoring persisted custom nodes & configuring ComfyUI-Manager...",
        ["正在检查并安装已保存自定义节点的 Python 依赖包..."] = "Checking & installing Python packages for saved custom nodes...",
        ["正在拉起 ComfyUI 节点推理引擎 (端口 8188)..."] = "Starting ComfyUI node inference engine (Port 8188)...",
        ["ComfyUI 内核已就绪，正在建立 Cloudflare 高速全屏画布隧道..."] = "ComfyUI engine ready. Opening Cloudflare Quick Tunnel for fullscreen canvas...",
        ["ComfyUI 已停止（节点、模型与工作流已持久化保存至 Google Drive）"] = "ComfyUI stopped (nodes, models & workflows persisted to Google Drive)",
        ["正在先从 Google Drive 检查已持久化的历史模型权重..."] = "Checking Google Drive for previously persisted model weights...",
        ["正在将配齐的模型与中文工作流持久化同步至 Google Drive..."] = "Syncing installed models & workflow templates to Google Drive...",
        ["正在预装并校验 llama.cpp CUDA 高速推理引擎..."] = "Installing & verifying llama.cpp CUDA inference engine...",
        ["正在映射 MiniMax-H3 BF16 多模态编码器权重 (14 分片)..."] = "Mapping MiniMax-H3 BF16 multimodal encoder weights (14 shards)...",
        ["正在映射 MiniMax-H3 BF16 33B DiT 主干权重 (13 分片)..."] = "Mapping MiniMax-H3 BF16 33B DiT backbone weights (13 shards)...",
        ["正在映射 3D 视频 VAE 与 32kHz 立体声 VAE 权重..."] = "Mapping 3D Video VAE & 32kHz Stereo Audio VAE weights...",
        ["正在构建 MiniMax-H3 BF16 原生 GPU 计算图与 Processor..."] = "Building MiniMax-H3 native BF16 GPU computation graph & processor...",
        ["正在映射 Qwen-Image-2.1 权重并构建 GPU 管线..."] = "Mapping Qwen-Image-2.1 weights & building GPU pipeline...",
        ["正在映射 Z-Image-Turbo 权重并构建 8 步极速生图管线..."] = "Mapping Z-Image-Turbo weights & building 8-step turbo pipeline...",
        ["正在预热 Qwen3.8-27B-UD-Q4_K_M.gguf 与 mmproj 视觉权重..."] = "Warming up Qwen3.8-27B-UD-Q4_K_M.gguf & mmproj vision weights...",
        ["正在自动补齐 Unsloth MTP 投机解码加速头 (1.31 GB)..."] = "Downloading Unsloth MTP speculative decoding head (1.31 GB)...",
        ["正在上传最新多模型开源服务端代码 (app.py)..."] = "Uploading latest multi-model server engine (app.py)...",
        ["正在云端配置 Google Drive 持久化、多模型引擎并启动隧道 (约需 40~90 秒)..."] = "Configuring Google Drive persistence, multi-model engine & public tunnel (~40–90s)...",
        ["正在克隆 DiffSynth-Studio..."] = "Cloning DiffSynth-Studio...",
        ["容器仍在初始化，继续尝试连接内核..."] = "Container still initializing, retrying kernel connection...",
        ["旧实例已失效或正在销毁，正在自动清理并重新分配新实例..."] = "Previous runtime expired; cleaning up and provisioning a fresh GPU instance...",
        ["已手动停止当前推理任务"] = "Inference cancelled by user",
        ["首尾帧模式下请至少提供首帧或尾帧图片"] = "Please provide at least a start frame or end frame image in First/Last Frame mode",
        ["图像编辑/重绘模式下请至少拖入一张输入图片"] = "Please drop at least one reference image in Image Edit / Restyle mode",
    };

    private static readonly (string Zh, string En)[] ServerTextReplacements =
    [
        ("已无缝接管在线 ComfyUI 画布", "Connected to active ComfyUI canvas"),
        ("ComfyUI 进程已退出", "ComfyUI process exited"),
        ("，可点击重新启动", "; click to restart"),
        ("正在建立 ", "Linking "),
        (" 目录软链", " symlinks"),
        ("正在准备配齐「", "Preparing preset pack \""),
        ("正在生成「", "Generating workflow templates for \""),
        ("」全中文三步傻瓜注释工作流...", "\"..."),
        ("」（共 ", "\" ("),
        (" 个官方权重）...", " official weights)..."),
        ("正在 8 线程极速下载 ", "8-thread fast downloading "),
        ("正在 8 线程并发下载 ", "8-thread downloading "),
        ("正在下载 ", "Downloading "),
        (" 至 models/", " to models/"),
        (" 已就绪 (", " ready ("),
        (")，跳过下载", "), skipping"),
        ("正在为 ", "Starting 8-stream parallel download for "),
        (" 启动 8 路并发 HTTP Range 极速下载通道...", "..."),
        ("正在获取 ", "Fetching tokenizer & processor configs for "),
        (" 的分词器与 Processor 配置文件...", "..."),
        ("正在评估显存余量并装载 ", "Checking VRAM & loading "),
        (" 至 GPU...", " into GPU..."),
        ("[显存常驻加速] 正在将子模块 ", "[VRAM Pinning] Warming up submodule "),
        (" 预热锁入 GPU 显存...", " in GPU VRAM..."),
        (" 正在编码多模态上下文并启动推理...", " encoding multimodal context & starting inference..."),
        (" 推理完成！耗时 ", " inference complete! Elapsed "),
        (" 正在使用多模态编码器解析提示词", " encoding prompts with multimodal encoder"),
        ("DiT 采样已完成，正在执行", "DiT sampling finished, running "),
        ("3D 视频 VAE 解码", "3D Video VAE decoding"),
        ("视频帧解码完成，正在执行 32kHz 立体声音轨解码", "Video frames decoded, decoding 32kHz stereo audio"),
        (" 正在执行去噪采样第 ", " denoising step "),
        (" 步 (", " ("),
        ("VAE 解码完成，正在合成 32kHz 立体声音轨并封装 MP4", "VAE decoded, muxing 32kHz stereo audio into MP4"),
        ("正在写入无损 PNG 图像文件", "Writing lossless PNG image"),
        ("正在等待上一任务释放 GPU 显存锁...", "Waiting for previous job to release GPU lock..."),
        ("生成完成！耗时 ", "Generation complete! Elapsed "),
        ("生成出错: ", "Generation error: "),
        // Server text keeps "Mac 客户端"; the English output refers to this Windows app.
        ("尚未加载至 Colab GPU 显存，请先在 Mac 客户端点击「加载到显存」。", "is not loaded in Colab GPU VRAM. Click 'Load to VRAM' in the Windows app first."),
        ("尚未加载到 GPU 显存！请先在左侧点击「加载到显存」（显存充裕时可与现有模型同时常驻）。", "is not loaded in GPU VRAM! Click 'Load to VRAM' on the left first (can co-reside with loaded models)."),
        ("尚未加载至 GPU 显存！请先在左侧点击「加载到显存」。", "is not loaded in GPU VRAM! Click 'Load to VRAM' on the left first."),
        ("尚未加载到显存", "is not loaded in VRAM"),
        ("当前已常驻：", "currently resident: "),
        ("请点击左侧「加载到显存」，显存充裕时可同时常驻无需卸载现有模型。", "Click 'Load to VRAM' on the left (can co-reside when VRAM permits)."),
        ("当前已分配实例型号为 ", "Current assigned instance is "),
        ("，正在释放并切换为您选择的 ", "; releasing and switching to "),
        ("正在检查已有实例 ", "Checking existing runtime "),
        (" 连通性...", " connectivity..."),
        ("已通过 Google OAuth 自动挂载 Google Drive", "Mounted Google Drive automatically via Google OAuth"),
        ("启动超时，日志尾部: ", "Startup timed out, log tail: "),
        ("Google Drive 实时软链挂载", "Google Drive Live Symlink"),
        ("Google Drive 云盘自动持久化", "Google Drive Auto-Persist"),
        ("实例本地高速存储", "Local NVMe Storage"),
    ];

    public static string LocalizeServerText(string text)
    {
        if (!LanguageManager.Shared.IsEN || string.IsNullOrEmpty(text)) return text;
        if (ServerTextExactMap.TryGetValue(text, out var exact)) return exact;

        var s = LocalizeModelFileDesc(text);
        foreach (var (zhSub, enSub) in ServerTextReplacements)
        {
            if (s.Contains(zhSub, StringComparison.Ordinal))
            {
                s = s.Replace(zhSub, enSub, StringComparison.Ordinal);
            }
        }
        return s;
    }

    public static string LocalizeConsoleLine(string line)
    {
        if (!LanguageManager.Shared.IsEN || string.IsNullOrEmpty(line)) return line;
        if (line.Contains("实时下载/采样进度流", StringComparison.Ordinal))
        {
            return line.Replace("实时下载/采样进度流", "Live Download / Sampling Progress", StringComparison.Ordinal);
        }
        return LocalizeServerText(line);
    }

    public static string StripEmojiCharacters(string text) => StripEmoji(text);

    // MARK: - HuggingFace README excerpt

    private static readonly char[] NewlineChars = ['\n', '\u000B', '\u000C', '\r', '\u0085', '\u2028', '\u2029'];

    /// <summary>Swift <c>components(separatedBy: .newlines)</c>.</summary>
    public static string[] SplitNewlines(string s) => s.Split(NewlineChars);

    /// <summary>Swift <c>String.count</c> (extended grapheme clusters).</summary>
    public static int GraphemeCount(string s) => new StringInfo(s).LengthInTextElements;

    private const RegexOptions Rx = RegexOptions.CultureInvariant;
    private static readonly Regex HtmlCommentRegex = new(@"<!--[\s\S]*?-->", Rx);
    private static readonly Regex AnchorImageRegex = new(@"<a[^>]*>\s*<img[^>]*>\s*</a>", Rx | RegexOptions.IgnoreCase);
    private static readonly Regex ImgTagRegex = new(@"<img[^>]*>", Rx | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlHeadingRegex = new(@"<h[1-4][^>]*>(.*?)</h[1-4]>", Rx | RegexOptions.IgnoreCase);
    private static readonly Regex AnyTagRegex = new(@"<[^>]+>", Rx);
    private static readonly Regex LinkedBadgeRegex = new(@"\[!\[[^\]]*\]\([^\)]*\)\]\([^\)]*\)", Rx);
    private static readonly Regex MdImageRegex = new(@"!\[[^\]]*\]\([^\)]*\)", Rx);
    private static readonly Regex TrailingBrokenLinkRegex = new(@"\[([^\]]+)\]\([^\)\n]*$", Rx);
    private static readonly Regex MdLinkRegex = new(@"\[([^\]]+)\]\(([^\)]+)\)", Rx);

    public static string CleanHFReadmeExcerpt(string rawMd)
    {
        var body = rawMd ?? string.Empty;
        if (body.StartsWith("---", StringComparison.Ordinal))
        {
            int rEnd = body.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (rEnd >= 0)
            {
                body = body[(rEnd + 4)..];
            }
        }
        // Remove HTML comments and multi-line anchor/image badge blocks
        body = HtmlCommentRegex.Replace(body, "");
        body = AnchorImageRegex.Replace(body, "");
        body = ImgTagRegex.Replace(body, "");
        body = HtmlHeadingRegex.Replace(body, "\n### $1\n");
        body = AnyTagRegex.Replace(body, "");
        body = LinkedBadgeRegex.Replace(body, "");
        body = MdImageRegex.Replace(body, "");
        body = body.Replace("&nbsp;", " ", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal)
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal);

        var cleanLines = new List<string>();
        int totalChars = 0;
        foreach (var ln in SplitNewlines(body))
        {
            var s = ln.Trim();
            if (s.Length == 0 || s.StartsWith("```", StringComparison.Ordinal) || s.StartsWith("|---", StringComparison.Ordinal) || s.StartsWith("| ---", StringComparison.Ordinal))
            {
                continue;
            }
            cleanLines.Add(s);
            totalChars += GraphemeCount(s) + 1;
            if (totalChars >= 1400) break;
        }
        var joined = string.Join("\n", cleanLines);
        joined = TrailingBrokenLinkRegex.Replace(joined, "$1");
        return joined;
    }

    // MARK: - Inline markdown (AttributedString(markdown:, .inlineOnlyPreservingWhitespace) equivalent)

    /// <summary>
    /// Port of Swift <c>renderHFInlineMarkdown(_:)</c>. Returns styled runs (bold / italic / code / strikethrough / link)
    /// instead of an <c>AttributedString</c>; <c>Controls.HFMarkdownExcerptView</c> turns them into RichTextBlock inlines.
    /// </summary>
    public static IReadOnlyList<HFInlineRun> RenderHFInlineMarkdown(string raw)
    {
        var cleaned = (raw ?? string.Empty).Trim();
        cleaned = TrailingBrokenLinkRegex.Replace(cleaned, "$1");
        int oddBold = CountOccurrences(cleaned, "**");
        if (oddBold % 2 != 0)
        {
            int last = cleaned.LastIndexOf("**", StringComparison.Ordinal);
            if (last >= 0) cleaned = cleaned.Remove(last, 2);
        }
        try
        {
            var runs = new List<HFInlineRun>();
            new InlineMarkdownParser(cleaned).Parse(0, cleaned.Length, default, runs);
            return MergeRuns(runs);
        }
        catch
        {
            var plain = cleaned;
            plain = MdLinkRegex.Replace(plain, "$1");
            plain = plain.Replace("**", "", StringComparison.Ordinal);
            plain = plain.Replace("__", "", StringComparison.Ordinal);
            plain = plain.Replace("`", "", StringComparison.Ordinal);
            return [new HFInlineRun(plain)];
        }
    }

    /// <summary>Plain text of <see cref="RenderHFInlineMarkdown"/> (Swift <c>String(attr.characters)</c>).</summary>
    public static string RenderHFInlineMarkdownPlain(string raw) =>
        string.Concat(RenderHFInlineMarkdown(raw).Select(r => r.Text));

    private static int CountOccurrences(string s, string sub)
    {
        int count = 0, idx = 0;
        while ((idx = s.IndexOf(sub, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += sub.Length;
        }
        return count;
    }

    private static List<HFInlineRun> MergeRuns(List<HFInlineRun> runs)
    {
        var merged = new List<HFInlineRun>(runs.Count);
        foreach (var r in runs)
        {
            if (r.Text.Length == 0) continue;
            if (merged.Count > 0)
            {
                var prev = merged[^1];
                if (prev.Bold == r.Bold && prev.Italic == r.Italic && prev.Code == r.Code
                    && prev.Strikethrough == r.Strikethrough && prev.Link == r.Link)
                {
                    merged[^1] = prev with { Text = prev.Text + r.Text };
                    continue;
                }
            }
            merged.Add(r);
        }
        return merged;
    }

    private readonly record struct InlineStyle(bool Bold, bool Italic, bool Strike, string? Link);

    /// <summary>Small CommonMark-style inline parser (emphasis, strong, code spans, links, autolinks, escapes, entities).</summary>
    private sealed class InlineMarkdownParser(string s)
    {
        private static readonly Regex AutolinkRegex = new(@"\G<((?:https?|ftp|mailto):[^\s<>]*|[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,})>", RegexOptions.CultureInvariant);

        private static bool IsAsciiPunct(char c) => c < 128 && char.IsAsciiLetterOrDigit(c) == false && !char.IsWhiteSpace(c) && !char.IsControl(c);
        private static bool IsPunct(char c) => char.IsPunctuation(c) || char.IsSymbol(c);

        public void Parse(int start, int end, InlineStyle st, List<HFInlineRun> output)
        {
            var text = new StringBuilder();
            void Flush()
            {
                if (text.Length == 0) return;
                output.Add(new HFInlineRun(WebUtility.HtmlDecode(text.ToString()), st.Bold, st.Italic, false, st.Strike, st.Link));
                text.Clear();
            }

            int i = start;
            while (i < end)
            {
                char c = s[i];

                // Backslash escapes
                if (c == '\\' && i + 1 < end && IsAsciiPunct(s[i + 1]))
                {
                    // keep escaped char literal (avoid entity decoding of an escaped '&')
                    Flush();
                    output.Add(new HFInlineRun(s[i + 1].ToString(), st.Bold, st.Italic, false, st.Strike, st.Link));
                    i += 2;
                    continue;
                }

                // Code spans
                if (c == '`')
                {
                    int n = RunLength(i, end, '`');
                    int close = FindBacktickClose(i + n, end, n);
                    if (close >= 0)
                    {
                        Flush();
                        var code = s.Substring(i + n, close - (i + n)).Replace('\n', ' ');
                        if (code.Length >= 2 && code[0] == ' ' && code[^1] == ' ' && code.Trim(' ').Length > 0)
                            code = code[1..^1];
                        output.Add(new HFInlineRun(code, st.Bold, st.Italic, true, st.Strike, st.Link));
                        i = close + n;
                        continue;
                    }
                    text.Append(s, i, n);
                    i += n;
                    continue;
                }

                // Images ![alt](url) → alt text; links [text](url)
                if (c == '[' || (c == '!' && i + 1 < end && s[i + 1] == '['))
                {
                    bool isImage = c == '!';
                    int open = isImage ? i + 1 : i;
                    if (TryParseLink(open, end, out int textEnd, out string url, out int after))
                    {
                        Flush();
                        // images render their alt text; links nested inside a link keep the outer target
                        var inner = isImage ? st : st with { Link = st.Link ?? url };
                        Parse(open + 1, textEnd, inner, output);
                        i = after;
                        continue;
                    }
                    if (isImage)
                    {
                        text.Append('!');
                        i++;
                        continue;
                    }
                }

                // Autolinks <https://...>
                if (c == '<')
                {
                    var m = AutolinkRegex.Match(s, i);
                    if (m.Success && m.Index + m.Length <= end)
                    {
                        Flush();
                        string target = m.Groups[1].Value;
                        string href = target.Contains('@') && !target.Contains(':') ? "mailto:" + target : target;
                        output.Add(new HFInlineRun(target, st.Bold, st.Italic, false, st.Strike, st.Link ?? href));
                        i += m.Length;
                        continue;
                    }
                }

                // Strikethrough ~~text~~
                if (c == '~' && i + 1 < end && s[i + 1] == '~')
                {
                    int close = s.IndexOf("~~", i + 2, StringComparison.Ordinal);
                    if (close > i + 2 && close + 2 <= end && !char.IsWhiteSpace(s[i + 2]) && !char.IsWhiteSpace(s[close - 1]))
                    {
                        Flush();
                        Parse(i + 2, close, st with { Strike = true }, output);
                        i = close + 2;
                        continue;
                    }
                    text.Append("~~");
                    i += 2;
                    continue;
                }

                // Emphasis / strong
                if (c == '*' || c == '_')
                {
                    int n = RunLength(i, end, c);
                    if (CanOpen(i, n, end, c))
                    {
                        if (n >= 3)
                        {
                            int close3 = FindEmphasisClose(i + 3, end, c, 3);
                            if (close3 >= 0)
                            {
                                Flush();
                                if (n > 3) text.Append(c, n - 3);
                                Flush();
                                Parse(i + n, close3, st with { Bold = true, Italic = true }, output);
                                i = close3 + 3;
                                continue;
                            }
                        }
                        if (n >= 2)
                        {
                            int close2 = FindEmphasisClose(i + n, end, c, 2);
                            if (close2 >= 0)
                            {
                                Flush();
                                if (n > 2) text.Append(c, n - 2);
                                Flush();
                                Parse(i + n, close2, st with { Bold = true }, output);
                                i = close2 + 2;
                                continue;
                            }
                        }
                        {
                            int close1 = FindEmphasisClose(i + n, end, c, 1);
                            if (close1 >= 0)
                            {
                                Flush();
                                if (n > 1) text.Append(c, n - 1);
                                Flush();
                                Parse(i + n, close1, st with { Italic = true }, output);
                                i = close1 + 1;
                                continue;
                            }
                        }
                    }
                    text.Append(c, n);
                    i += n;
                    continue;
                }

                text.Append(c);
                i++;
            }
            Flush();
        }

        private int RunLength(int i, int end, char c)
        {
            int n = 0;
            while (i + n < end && s[i + n] == c) n++;
            return n;
        }

        private int FindBacktickClose(int from, int end, int n)
        {
            int j = from;
            while (j < end)
            {
                if (s[j] == '`')
                {
                    int m = RunLength(j, end, '`');
                    if (m == n) return j;
                    j += m;
                }
                else j++;
            }
            return -1;
        }

        private bool CanOpen(int i, int n, int end, char c)
        {
            int after = i + n;
            if (after >= end) return false;
            char next = s[after];
            if (char.IsWhiteSpace(next)) return false;
            char prev = i > 0 ? s[i - 1] : ' ';
            bool leftFlanking = !IsPunct(next) || char.IsWhiteSpace(prev) || IsPunct(prev);
            if (!leftFlanking) return false;
            if (c == '_' && char.IsLetterOrDigit(prev)) return false;
            return true;
        }

        private bool CanClose(int j, int len, int end, char c)
        {
            if (j == 0) return false;
            char prev = s[j - 1];
            if (char.IsWhiteSpace(prev)) return false;
            int after = j + len;
            char next = after < end ? s[after] : ' ';
            bool rightFlanking = !IsPunct(prev) || char.IsWhiteSpace(next) || IsPunct(next);
            if (!rightFlanking) return false;
            if (c == '_' && char.IsLetterOrDigit(next)) return false;
            return true;
        }

        /// <summary>Find a closing delimiter run of exactly <paramref name="len"/> (or longer, consumed partially) chars.</summary>
        private int FindEmphasisClose(int from, int end, char c, int len)
        {
            int j = from;
            while (j < end)
            {
                char ch = s[j];
                if (ch == '\\') { j += 2; continue; }
                if (ch == '`')
                {
                    int n = RunLength(j, end, '`');
                    int close = FindBacktickClose(j + n, end, n);
                    j = close >= 0 ? close + n : j + n;
                    continue;
                }
                if (ch == c)
                {
                    int m = RunLength(j, end, c);
                    if (j > from)
                    {
                        // for single emphasis skip nested strong pairs; otherwise accept runs that can close
                        if (len == 1 && m == 2)
                        {
                            int inner = FindEmphasisClose(j + 2, end, c, 2);
                            if (inner >= 0) { j = inner + 2; continue; }
                        }
                        if (m >= len && CanClose(j, m, end, c))
                        {
                            return j + (m - len);
                        }
                    }
                    j += m;
                    continue;
                }
                j++;
            }
            return -1;
        }

        private bool TryParseLink(int open, int end, out int textEnd, out string url, out int after)
        {
            textEnd = -1; url = ""; after = -1;
            if (open >= end || s[open] != '[') return false;
            int depth = 0;
            int j = open;
            for (; j < end; j++)
            {
                char ch = s[j];
                if (ch == '\\') { j++; continue; }
                if (ch == '[') depth++;
                else if (ch == ']')
                {
                    depth--;
                    if (depth == 0) break;
                }
            }
            if (j >= end || depth != 0) return false;
            int close = j;
            if (close + 1 >= end || s[close + 1] != '(') return false;
            int k = close + 2;
            int pdepth = 1;
            for (; k < end; k++)
            {
                char ch = s[k];
                if (ch == '\\') { k++; continue; }
                if (ch == '(') pdepth++;
                else if (ch == ')')
                {
                    pdepth--;
                    if (pdepth == 0) break;
                }
                else if (ch == '\n') return false;
            }
            if (k >= end || pdepth != 0) return false;
            var dest = s.Substring(close + 2, k - (close + 2)).Trim();
            if (dest.StartsWith('<'))
            {
                int gt = dest.IndexOf('>');
                dest = gt > 0 ? dest[1..gt] : dest[1..];
            }
            else
            {
                int sp = dest.IndexOfAny([' ', '\t']);
                if (sp >= 0) dest = dest[..sp]; // drop optional "title"
            }
            textEnd = close;
            url = dest;
            after = k + 1;
            return true;
        }
    }

    // MARK: - HuggingFace pipeline tags & sizing

    public static readonly IReadOnlySet<string> UNSUPPORTED_HF_PIPELINE_TAGS = new HashSet<string>(StringComparer.Ordinal)
    {
        "image-to-3d", "text-to-3d", "image-segmentation", "object-detection",
        "depth-estimation", "mask-generation", "keypoint-detection", "robotics",
        "reinforcement-learning", "tabular-classification", "tabular-regression",
        "time-series-forecasting", "audio-classification", "automatic-speech-recognition",
        "text-to-speech", "audio-to-audio", "feature-extraction", "sentence-similarity",
        "token-classification", "text-classification", "zero-shot-image-classification",
        "zero-shot-classification", "fill-mask", "question-answering",
        "table-question-answering", "video-classification",
    };

    public static readonly IReadOnlySet<string> SUPPORTED_TRENDING_PIPELINE_TAGS = new HashSet<string>(StringComparer.Ordinal)
    {
        "text-generation", "image-text-to-text", "visual-question-answering",
        "any-to-any", "text-to-image", "image-to-image", "text-to-video", "image-to-video",
    };

    private static readonly Regex ParamSizeFromRepoRegex = new(@"(?:^|[-_/])(\d+(?:\.\d+)?)\s*([bBmMtT])(?:[-_/\b]|$)", RegexOptions.CultureInvariant);

    /// <summary>Overload for <see cref="JsonNode"/> metadata objects (HF API JSON).</summary>
    public static (long ParamCount, string ParamLabel, double EstVramGb, string DeployStatus, string DeployZh, string DeployEn)
        AnalyzeHFModelSizing(JsonNode? obj, string repoId, bool isGGUF, double fallbackStorageGb = 0.0)
    {
        var el = obj is null ? default : JsonSerializer.SerializeToElement(obj);
        return AnalyzeHFModelSizing(el, repoId, isGGUF, fallbackStorageGb);
    }

    /// <summary>Port of Swift <c>analyzeHFModelSizing(_:repoId:isGGUF:fallbackStorageGb:)</c>; <paramref name="obj"/> is the HF model JSON object.</summary>
    public static (long ParamCount, string ParamLabel, double EstVramGb, string DeployStatus, string DeployZh, string DeployEn)
        AnalyzeHFModelSizing(JsonElement obj, string repoId, bool isGGUF, double fallbackStorageGb = 0.0)
    {
        static JsonElement? Obj(JsonElement parent, string key) =>
            parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

        static long? Int64Of(JsonElement? parent, string key)
        {
            if (parent is not { } p || !p.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number) return null;
            if (v.TryGetInt64(out var l)) return l;
            double d = v.GetDouble();
            if (d >= long.MaxValue) return long.MaxValue;
            if (d <= long.MinValue) return long.MinValue;
            return (long)d;
        }

        string ptag = "";
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("pipeline_tag", out var pt) && pt.ValueKind == JsonValueKind.String)
        {
            ptag = (pt.GetString() ?? "").Trim().ToLowerInvariant();
        }
        var st = Obj(obj, "safetensors");
        var gg = Obj(obj, "gguf");
        long totalParams = 0;
        if (Int64Of(st, "total") is { } stTotal)
        {
            totalParams = stTotal;
        }
        else if (Int64Of(gg, "total") is { } ggTotal)
        {
            totalParams = ggTotal;
        }
        if (totalParams <= 0)
        {
            var match = ParamSizeFromRepoRegex.Match(repoId);
            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                string unit = match.Groups[2].Value.ToUpperInvariant();
                if (unit == "T") totalParams = (long)(val * 1_000_000_000_000);
                else if (unit == "B") totalParams = (long)(val * 1_000_000_000);
                else if (unit == "M") totalParams = (long)(val * 1_000_000);
            }
        }

        string paramLabel;
        if (totalParams >= 1_000_000_000_000)
        {
            paramLabel = CFormatFixed(totalParams / 1_000_000_000_000.0, 1) + "T";
        }
        else if (totalParams >= 1_000_000_000)
        {
            double b = totalParams / 1_000_000_000.0;
            if (b >= 20)
            {
                paramLabel = CFormatFixed(b, 0) + "B";
            }
            else
            {
                string s = CFormatFixed(b, 1) + "B";
                paramLabel = s.Replace(".0B", "B", StringComparison.Ordinal);
            }
        }
        else if (totalParams >= 1_000_000)
        {
            paramLabel = CFormatFixed(totalParams / 1_000_000.0, 0) + "M";
        }
        else
        {
            paramLabel = "";
        }

        static double R1(double x) => Math.Round(x * 10, MidpointRounding.AwayFromZero) / 10;

        double paramsB = totalParams > 0 ? (totalParams / 1_000_000_000.0) : 0.0;
        double estVramGb = 0.0;
        JsonElement pmap = default;
        bool hasPmap = st is { } stEl && stEl.TryGetProperty("parameters", out pmap)
            && pmap.ValueKind == JsonValueKind.Object && pmap.EnumerateObject().Any();
        if (isGGUF)
        {
            estVramGb = paramsB > 0 ? R1(paramsB * 0.62 + 1.2) : 0.0;
        }
        else if (hasPmap)
        {
            double bytesTotal = 0.0;
            foreach (var prop in pmap.EnumerateObject())
            {
                double cnt = prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : 0.0;
                string dt = prop.Name.ToUpperInvariant();
                if (dt.Contains("32", StringComparison.Ordinal)) bytesTotal += cnt * 4.0;
                else if (dt.Contains("16", StringComparison.Ordinal)) bytesTotal += cnt * 2.0;
                else if (dt.Contains('8')) bytesTotal += cnt * 1.0;
                else if (dt.Contains('4')) bytesTotal += cnt * 0.55;
                else bytesTotal += cnt * 2.0;
            }
            estVramGb = R1((bytesTotal / 1_000_000_000.0) * 1.12 + 1.2);
        }
        else if (paramsB > 0)
        {
            estVramGb = R1(paramsB * 2.1 + 1.2);
        }
        else if (fallbackStorageGb > 0)
        {
            estVramGb = R1(fallbackStorageGb * 1.12 + 1.0);
        }

        string status, zh, en;
        long vramInt = Math.Max(1, (long)Math.Round(estVramGb, MidpointRounding.AwayFromZero));
        if (!isGGUF && UNSUPPORTED_HF_PIPELINE_TAGS.Contains(ptag))
        {
            status = "too_large";
            zh = $"非内置管线 ({ptag} · 需专用SDK)";
            en = $"Custom Pipeline ({ptag})";
        }
        else if (estVramGb <= 0)
        {
            status = "unknown";
            zh = "显存待探测";
            en = "VRAM TBD";
        }
        else if (estVramGb <= 14.2)
        {
            status = "great";
            zh = $"适合 · T4(15G)可跑 (~{vramInt}G)";
            en = $"Fit: T4 15G (~{vramInt}G)";
        }
        else if (estVramGb <= 22.5)
        {
            status = "good";
            zh = $"适合 · 需L4(24G) (~{vramInt}G)";
            en = $"Fit: L4 24G (~{vramInt}G)";
        }
        else if (estVramGb <= 39.0)
        {
            status = "a100";
            zh = $"可部署 · 需A100(40G) (~{vramInt}G)";
            en = $"Fit: A100 40G (~{vramInt}G)";
        }
        else if (estVramGb <= 78.0)
        {
            status = "heavy";
            zh = $"需A100(80G) (~{vramInt}G)";
            en = $"Needs A100 80G (~{vramInt}G)";
        }
        else if (isGGUF && paramsB <= 240.0)
        {
            status = "heavy";
            zh = $"需A100(80G)+CPU卸载 (~{vramInt}G)";
            en = $"A100 80G + Offload (~{vramInt}G)";
        }
        else
        {
            status = "too_large";
            zh = $"超单卡上限 (~{vramInt}G · 建议搜GGUF)";
            en = $"Too Large (~{vramInt}G · Try GGUF)";
        }
        return (totalParams, paramLabel, estVramGb, status, zh, en);
    }

    // MARK: - 文本清理：过滤服务端消息中的 emoji

    // Unicode `Emoji` property ranges above U+238C (Swift: `p.isEmoji && v > 0x238C`), plus the two
    // `Emoji_Presentation` code points below it (U+231A..U+231B). Source: Unicode emoji-data.txt (15.1).
    private static readonly (int Lo, int Hi)[] EmojiRanges =
    [
        (0x231A, 0x231B), (0x23CF, 0x23CF), (0x23E9, 0x23F3), (0x23F8, 0x23FA), (0x24C2, 0x24C2),
        (0x25AA, 0x25AB), (0x25B6, 0x25B6), (0x25C0, 0x25C0), (0x25FB, 0x25FE),
        (0x2600, 0x2604), (0x260E, 0x260E), (0x2611, 0x2611), (0x2614, 0x2615), (0x2618, 0x2618),
        (0x261D, 0x261D), (0x2620, 0x2620), (0x2622, 0x2623), (0x2626, 0x2626), (0x262A, 0x262A),
        (0x262E, 0x262F), (0x2638, 0x263A), (0x2640, 0x2640), (0x2642, 0x2642), (0x2648, 0x2653),
        (0x265F, 0x2660), (0x2663, 0x2663), (0x2665, 0x2666), (0x2668, 0x2668), (0x267B, 0x267B),
        (0x267E, 0x267F), (0x2692, 0x2697), (0x2699, 0x2699), (0x269B, 0x269C), (0x26A0, 0x26A1),
        (0x26A7, 0x26A7), (0x26AA, 0x26AB), (0x26B0, 0x26B1), (0x26BD, 0x26BE), (0x26C4, 0x26C5),
        (0x26C8, 0x26C8), (0x26CE, 0x26CF), (0x26D1, 0x26D1), (0x26D3, 0x26D4), (0x26E9, 0x26EA),
        (0x26F0, 0x26F5), (0x26F7, 0x26FA), (0x26FD, 0x26FD), (0x2702, 0x2702), (0x2705, 0x2705),
        (0x2708, 0x270D), (0x270F, 0x270F), (0x2712, 0x2712), (0x2714, 0x2714), (0x2716, 0x2716),
        (0x271D, 0x271D), (0x2721, 0x2721), (0x2728, 0x2728), (0x2733, 0x2734), (0x2744, 0x2744),
        (0x2747, 0x2747), (0x274C, 0x274C), (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757),
        (0x2763, 0x2764), (0x2795, 0x2797), (0x27A1, 0x27A1), (0x27B0, 0x27B0), (0x27BF, 0x27BF),
        (0x2934, 0x2935), (0x2B05, 0x2B07), (0x2B1B, 0x2B1C), (0x2B50, 0x2B50), (0x2B55, 0x2B55),
        (0x3030, 0x3030), (0x303D, 0x303D), (0x3297, 0x3297), (0x3299, 0x3299),
        (0x1F004, 0x1F004), (0x1F0CF, 0x1F0CF), (0x1F170, 0x1F171), (0x1F17E, 0x1F17F), (0x1F18E, 0x1F18E),
        (0x1F191, 0x1F19A), (0x1F1E6, 0x1F1FF), (0x1F201, 0x1F202), (0x1F21A, 0x1F21A), (0x1F22F, 0x1F22F),
        (0x1F232, 0x1F23A), (0x1F250, 0x1F251), (0x1F300, 0x1F321), (0x1F324, 0x1F393), (0x1F396, 0x1F397),
        (0x1F399, 0x1F39B), (0x1F39E, 0x1F3F0), (0x1F3F3, 0x1F3F5), (0x1F3F7, 0x1F4FD), (0x1F4FF, 0x1F53D),
        (0x1F549, 0x1F54E), (0x1F550, 0x1F567), (0x1F56F, 0x1F570), (0x1F573, 0x1F57A), (0x1F587, 0x1F587),
        (0x1F58A, 0x1F58D), (0x1F590, 0x1F590), (0x1F595, 0x1F596), (0x1F5A4, 0x1F5A5), (0x1F5A8, 0x1F5A8),
        (0x1F5B1, 0x1F5B2), (0x1F5BC, 0x1F5BC), (0x1F5C2, 0x1F5C4), (0x1F5D1, 0x1F5D3), (0x1F5DC, 0x1F5DE),
        (0x1F5E1, 0x1F5E1), (0x1F5E3, 0x1F5E3), (0x1F5E8, 0x1F5E8), (0x1F5EF, 0x1F5EF), (0x1F5F3, 0x1F5F3),
        (0x1F5FA, 0x1F64F), (0x1F680, 0x1F6C5), (0x1F6CB, 0x1F6D2), (0x1F6D5, 0x1F6D7), (0x1F6DC, 0x1F6E5),
        (0x1F6E9, 0x1F6E9), (0x1F6EB, 0x1F6EC), (0x1F6F0, 0x1F6F0), (0x1F6F3, 0x1F6FC), (0x1F7E0, 0x1F7EB),
        (0x1F7F0, 0x1F7F0), (0x1F90C, 0x1F93A), (0x1F93C, 0x1F945), (0x1F947, 0x1F9FF), (0x1FA70, 0x1FA7C),
        (0x1FA80, 0x1FA88), (0x1FA90, 0x1FABD), (0x1FABF, 0x1FAC5), (0x1FACE, 0x1FADB), (0x1FAE0, 0x1FAE8),
        (0x1FAF0, 0x1FAF8),
    ];

    private static bool IsEmojiScalar(int v)
    {
        if (v == 0xFE0F || v == 0x200D || v == 0x20E3) return true;
        if (v >= 0x1F1E6 && v <= 0x1F1FF) return true;
        if (v < 0x231A) return false;
        int lo = 0, hi = EmojiRanges.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            var r = EmojiRanges[mid];
            if (v < r.Lo) hi = mid - 1;
            else if (v > r.Hi) lo = mid + 1;
            else return true;
        }
        return false;
    }

    /// <summary>Swift <c>CharacterSet.whitespaces</c> (Unicode Zs + tab), excluding newlines.</summary>
    private static bool IsSwiftWhitespace(char c) =>
        c == '\t' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;

    public static string StripEmoji(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (!IsEmojiScalar(rune.Value)) sb.Append(rune.ToString());
        }
        var result = sb.ToString();
        while (result.Contains("  ", StringComparison.Ordinal)) result = result.Replace("  ", " ", StringComparison.Ordinal);
        int start = 0, end = result.Length;
        while (start < end && IsSwiftWhitespace(result[start])) start++;
        while (end > start && IsSwiftWhitespace(result[end - 1])) end--;
        return result[start..end];
    }
}
