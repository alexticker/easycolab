using System.Globalization;

namespace EasyColab.Models;

/// <summary>
/// Global catalogs / functions from ModelsAndCatalogs.swift. Imported with
/// <c>global using static EasyColab.Models.Catalogs;</c> so they can be referenced unqualified exactly like in Swift
/// (<c>OPEN_MODEL_CATALOG</c>, <c>VIDEO_RESOLUTIONS</c>, <c>FormatDuration(...)</c> ...).
/// </summary>
public static class Catalogs
{
    private static IReadOnlyList<ModelFileItem> Shards(string format, int count, Func<int, string> desc)
    {
        var list = new List<ModelFileItem>(count);
        for (int i = 1; i <= count; i++)
        {
            list.Add(new ModelFileItem(name: string.Format(CultureInfo.InvariantCulture, format, i), desc: desc(i), exists: false, size_gb: 0));
        }
        return list;
    }

    private static ModelFileItem F(string name, string desc) => new(name: name, desc: desc, exists: false, size_gb: 0);

    public static readonly IReadOnlyList<OpenModelSpec> OPEN_MODEL_CATALOG =
    [
        new OpenModelSpec
        {
            Id = "minimax_h3",
            NameZh = "MiniMax-H3 (33B)",
            NameEn = "MiniMax-H3 (33B)",
            RepoId = "MiniMaxAI/MiniMax-H3",
            BadgeZh = "BF16 原生 · 134 GB",
            BadgeEn = "Native BF16 · 134 GB",
            SummaryZh = "全模态 BF16 原生满血精度同步生成高清视频与 32kHz 立体声音效、人声对白及配乐",
            SummaryEn = "Full-modality native BF16 generation of HD video with synchronized 32kHz stereo SFX, dialogue & music",
            ApproxSizeGB = 134.1,
            Icon = "film.stack",
            Modality = ModelModality.VideoAudio,
            DefaultSteps = 35,
            Modes =
            [
                new TaskModeItem(
                    id: "t2va",
                    titleZh: "文生音视频",
                    titleEn: "Text to Video+Audio",
                    subtitleZh: "输入镜头与声音描述，同步生成视频与立体声音轨",
                    subtitleEn: "Generate HD video and synchronized stereo audio from scene & sound prompts",
                    icon: "film"),
                new TaskModeItem(
                    id: "fl2va",
                    titleZh: "首尾帧生视频",
                    titleEn: "First/Last Frame to Video",
                    subtitleZh: "上传首帧或尾帧参考图，精准控制起止画面与运镜过渡",
                    subtitleEn: "Upload start or end keyframes to lock composition and camera transitions",
                    icon: "photo.on.rectangle.angled"),
            ],
            DefaultFiles =
            [
                .. Shards("FL2VA/transformer/model-{0:D5}-of-00013.safetensors", 13, i => $"DiT 音视频主干 BF16 {i}/13"),
                .. Shards("FL2VA/text_encoder/model-{0:D5}-of-00014.safetensors", 14, i => $"多模态编码器 BF16 {i}/14"),
                F("FL2VA/video_vae/source/model.safetensors", "3D 视频 VAE BF16 (9.7 GB)"),
                F("FL2VA/audio_vae/model.safetensors", "32kHz 立体声 VAE BF16 (0.6 GB)"),
            ],
        },
        new OpenModelSpec
        {
            Id = "minimax_h3_image",
            NameZh = "MiniMax-H3-Image (33B · 4K)",
            NameEn = "MiniMax-H3-Image (33B · 4K)",
            RepoId = "MiniMaxAI/MiniMax-H3-Image",
            BadgeZh = "6步原生4K · 135 GB (共享省67G)",
            BadgeEn = "6-Step Native 4K · 135 GB",
            SummaryZh = "MiniMax 33B 6步蒸馏原生 4K (16MP) 商业摄影、双语海报排版与多参考图编辑旗舰（与 MiniMax-H3 零拷贝共享 66.7GB 编码器）",
            SummaryEn = "MiniMax 33B 6-step distilled native 4K (16MP) photorealistic image generation & multi-reference instruction editing (shares 66.7GB encoder with MiniMax-H3)",
            ApproxSizeGB = 135.3,
            Icon = "camera.aperture",
            Modality = ModelModality.Image,
            DefaultSteps = 6,
            Modes =
            [
                new TaskModeItem(
                    id: "t2i",
                    titleZh: "6 步原生 4K 文生图",
                    titleEn: "6-Step Native 4K Text-to-Image",
                    subtitleZh: "6 步 PDD 蒸馏直出 4MP~16MP (4K) 超清商业大片与高密度中英文海报排版",
                    subtitleEn: "6-step PDD distilled native 4MP–16MP (4K) commercial photography & dense bilingual poster typography",
                    icon: "camera.metering.matrix"),
                new TaskModeItem(
                    id: "i2i_edit",
                    titleZh: "多参考图指令修图",
                    titleEn: "Multi-Image Instruction Edit",
                    subtitleZh: "上传 1~2 张参考图（可用 <Picture 1> / <Picture 2> 指代）进行精准修图与主体融合",
                    subtitleEn: "Upload 1–2 reference images (refer via <Picture 1> / <Picture 2>) for precise editing & subject fusion",
                    icon: "wand.and.stars"),
            ],
            DefaultFiles =
            [
                .. Shards("transformer_diffusers/diffusion_pytorch_model-{0:D5}-of-00012.safetensors", 12, i => $"33B 图像 DiT 主干 {i}/12"),
                .. Shards("text_encoder/model-{0:D5}-of-00014.safetensors", 14, i => $"32B 多模态编码器 {i}/14 (共享)"),
                F("vae_diffusers/diffusion_pytorch_model.safetensors", "4K 16MP 图像 VAE (2.3 GB)"),
            ],
        },
        new OpenModelSpec
        {
            Id = "comfyui_studio",
            NameZh = "ComfyUI 节点工作台",
            NameEn = "ComfyUI Node Studio",
            RepoId = "comfyanonymous/ComfyUI",
            BadgeZh = "节点视频创作 · 云盘软链持久化",
            BadgeEn = "Node Video Studio · Drive Symlink Persist",
            SummaryZh = "一键拉起云端 ComfyUI + Manager 全屏节点画布，自定义节点、视频权重与工作流自动软链持久化至 Google Drive",
            SummaryEn = "1-click cloud ComfyUI + Manager fullscreen node canvas with Google Drive symlink persistence for custom nodes, weights & workflows",
            ApproxSizeGB = 0.0,
            Icon = "point.3.connected.trianglepath.dotted",
            Modality = ModelModality.ComfyUI,
            DefaultSteps = 25,
            Modes =
            [
                new TaskModeItem(
                    id: "comfy_canvas",
                    titleZh: "全屏节点画布",
                    titleEn: "Fullscreen Node Canvas",
                    subtitleZh: "一键部署并跳转浏览器全屏节点画布，复用 G4 保活、折线监控与自动休眠",
                    subtitleEn: "Deploy & open fullscreen canvas in browser with G4 keepalive, live telemetry & auto-sleep",
                    icon: "macwindow.badge.plus"),
                new TaskModeItem(
                    id: "comfy_assets",
                    titleZh: "极速下权重与回传成片",
                    titleEn: "Fast Weights & Pull Outputs",
                    // Windows: "回传 Mac" → "回传本机" (as the previous Windows client did)
                    subtitleZh: "8 线程极速下载视频模型至云盘 models 目录，并将 output 成片一键回传本机",
                    subtitleEn: "8-thread fast model download to Drive models folder & 1-click output pull to local PC",
                    icon: "externaldrive.badge.icloud"),
            ],
            DefaultFiles = [],
        },
        new OpenModelSpec
        {
            Id = "qwen38_27b",
            NameZh = "Qwen3.8-27B",
            NameEn = "Qwen3.8-27B",
            RepoId = "Qwen/Qwen3.8-27B",
            BadgeZh = "BF16 原生 · 51.8 GB",
            BadgeEn = "Native BF16 · 51.8 GB",
            SummaryZh = "Hugging Face 榜首开源多模态大模型（BF16 原生满血精度，支持最高 256K 上下文，需大显存实例）",
            SummaryEn = "Top Hugging Face open multimodal LLM (full native BF16 precision, up to 256K context, high-VRAM GPU)",
            ApproxSizeGB = 51.8,
            Icon = "brain",
            Modality = ModelModality.LlmVLM,
            DefaultSteps = 20,
            Modes =
            [
                new TaskModeItem(
                    id: "chat",
                    titleZh: "深度推理对话",
                    titleEn: "Deep Reasoning Chat",
                    subtitleZh: "支持长思维链推理、代码架构、复杂逻辑与分镜脚本创作",
                    subtitleEn: "Long chain-of-thought reasoning, software architecture, logic & storyboard scripting",
                    icon: "bubble.left.and.bubble.right"),
                new TaskModeItem(
                    id: "vlm",
                    titleZh: "图像视觉理解",
                    titleEn: "Vision Understanding",
                    subtitleZh: "拖入图片进行画面解析、OCR 识别或反推专业生图/生视频提示词",
                    subtitleEn: "Drop images for scene analysis, OCR, or reverse-engineering image/video prompts",
                    icon: "eye"),
                new TaskModeItem(
                    id: "api",
                    titleZh: "API 协议网关",
                    titleEn: "API Protocol Gateway",
                    subtitleZh: "提供 OpenAI (/v1) 与 Anthropic (/v1/messages) 双协议公网接口",
                    subtitleEn: "Public dual-protocol gateway compatible with OpenAI (/v1) & Anthropic (/v1/messages)",
                    icon: "network"),
            ],
            DefaultFiles = Shards("model-{0:D5}-of-00018.safetensors", 18, i => $"权重分片 {i}/18"),
        },
        new OpenModelSpec
        {
            Id = "qwen38_27b_gguf",
            NameZh = "Qwen3.8-27B-GGUF",
            NameEn = "Qwen3.8-27B-GGUF",
            RepoId = "unsloth/Qwen3.8-27B-GGUF",
            BadgeZh = "UD-Q4_K_M + MTP · 18.7 GB",
            BadgeEn = "UD-Q4_K_M + MTP · 18.7 GB",
            SummaryZh = "Unsloth 动态 4-bit 量化 + 原生 MTP 多词元投机解码加速，支持最高 256K 上下文与多模态视觉理解",
            SummaryEn = "Unsloth dynamic 4-bit quantization + native MTP speculative decoding (~100 tok/s), up to 256K context & vision",
            ApproxSizeGB = 18.7,
            Icon = "cpu",
            Modality = ModelModality.LlmVLM,
            DefaultSteps = 20,
            Modes =
            [
                new TaskModeItem(
                    id: "chat",
                    titleZh: "深度推理对话",
                    titleEn: "Deep Reasoning Chat",
                    subtitleZh: "UD-Q4_K_M + MTP 投机解码极速推理，支持长思维链、代码架构与超长上下文分析",
                    subtitleEn: "Ultra-fast UD-Q4_K_M + MTP speculative decoding for CoT reasoning, coding & long-context analysis",
                    icon: "bubble.left.and.bubble.right"),
                new TaskModeItem(
                    id: "vlm",
                    titleZh: "图像视觉理解",
                    titleEn: "Vision Understanding",
                    subtitleZh: "内置 mmproj-F16 视觉投影器，支持图片解析、OCR 与生图/生视频提示词反推",
                    subtitleEn: "Built-in mmproj-F16 vision projector for image analysis, OCR & prompt reverse-engineering",
                    icon: "eye"),
                new TaskModeItem(
                    id: "api",
                    titleZh: "API 协议网关",
                    titleEn: "API Protocol Gateway",
                    subtitleZh: "提供 OpenAI (/v1) 与 Anthropic (/v1/messages) 双协议公网接口",
                    subtitleEn: "Public dual-protocol gateway compatible with OpenAI (/v1) & Anthropic (/v1/messages)",
                    icon: "network"),
            ],
            DefaultFiles =
            [
                F("Qwen3.8-27B-UD-Q4_K_M.gguf", "Unsloth UD-Q4_K_M 主模型 (16.5 GB)"),
                F("mmproj-F16.gguf", "多模态视觉投影器 (0.93 GB)"),
                F("MTP/mtp-Qwen3.8-27B-Q4_0.gguf", "Unsloth MTP 多词元投机解码头 (1.31 GB)"),
            ],
        },
        new OpenModelSpec
        {
            Id = "qwen_image_21",
            NameZh = "Qwen-Image-2.1",
            NameEn = "Qwen-Image-2.1",
            RepoId = "Qwen/Qwen-Image-2.1",
            BadgeZh = "文生图与修图 · 30.9 GB",
            BadgeEn = "Text-to-Image & Edit · 30.9 GB",
            SummaryZh = "通义千问新一代旗舰图像生成与自然语言多图指令编辑模型",
            SummaryEn = "Qwen flagship image generation & natural-language multi-image instruction editing model",
            ApproxSizeGB = 30.9,
            Icon = "paintbrush.pointed",
            Modality = ModelModality.Image,
            DefaultSteps = 40,
            Modes =
            [
                new TaskModeItem(
                    id: "t2i",
                    titleZh: "高清文生图",
                    titleEn: "HD Text-to-Image",
                    subtitleZh: "复杂构图、精准中英文排版与高质感摄影/插画生成",
                    subtitleEn: "Complex composition, accurate bilingual typography & high-fidelity photography/illustration",
                    icon: "photo.artframe"),
                new TaskModeItem(
                    id: "i2i_edit",
                    titleZh: "多图指令修图",
                    titleEn: "Multi-Image Instruction Edit",
                    subtitleZh: "上传 1~2 张原图，用自然语言指令改局部、换背景或融合主体",
                    subtitleEn: "Upload 1–2 source images to edit regions, swap backgrounds, or fuse subjects via natural language",
                    icon: "wand.and.rays"),
            ],
            DefaultFiles =
            [
                F("transformer/diffusion_pytorch_model-00001-of-00002.safetensors", "DiT 1/2"),
                F("transformer/diffusion_pytorch_model-00002-of-00002.safetensors", "DiT 2/2"),
                F("text_encoder/model-00001-of-00004.safetensors", "编码器 1/4"),
                F("text_encoder/model-00002-of-00004.safetensors", "编码器 2/4"),
                F("text_encoder/model-00003-of-00004.safetensors", "编码器 3/4"),
                F("text_encoder/model-00004-of-00004.safetensors", "编码器 4/4"),
                F("vae/diffusion_pytorch_model.safetensors", "图像 VAE"),
            ],
        },
        new OpenModelSpec
        {
            Id = "z_image_turbo",
            NameZh = "Z-Image-Turbo",
            NameEn = "Z-Image-Turbo",
            RepoId = "Tongyi-MAI/Z-Image-Turbo",
            BadgeZh = "8步极速生图 · 30.6 GB",
            BadgeEn = "8-Step Turbo Image · 30.6 GB",
            SummaryZh = "Hugging Face 高人气 8 步蒸馏极速写实摄影与中英文海报生图模型",
            SummaryEn = "Popular Hugging Face 8-step distilled model for ultra-fast photorealism & bilingual poster design",
            ApproxSizeGB = 30.6,
            Icon = "bolt.square",
            Modality = ModelModality.Image,
            DefaultSteps = 8,
            Modes =
            [
                new TaskModeItem(
                    id: "t2i",
                    titleZh: "8 步极速文生图",
                    titleEn: "8-Step Fast Text-to-Image",
                    subtitleZh: "仅需 8 步采样，秒级输出商业级人像摄影与中英文排版画面",
                    subtitleEn: "Commercial-grade portrait photography & bilingual poster typography in just 8 steps",
                    icon: "bolt"),
                new TaskModeItem(
                    id: "i2i_edit",
                    titleZh: "图生图风格重绘",
                    titleEn: "Image-to-Image Restyle",
                    subtitleZh: "上传参考底图并调节重绘幅度，保留构图生成全新细节",
                    subtitleEn: "Upload a reference image and adjust denoising strength to restyle while preserving layout",
                    icon: "square.on.square"),
            ],
            DefaultFiles =
            [
                F("transformer/diffusion_pytorch_model-00001-of-00003.safetensors", "DiT 1/3"),
                F("transformer/diffusion_pytorch_model-00002-of-00003.safetensors", "DiT 2/3"),
                F("transformer/diffusion_pytorch_model-00003-of-00003.safetensors", "DiT 3/3"),
                F("text_encoder/model-00001-of-00003.safetensors", "编码器 1/3"),
                F("text_encoder/model-00002-of-00003.safetensors", "编码器 2/3"),
                F("text_encoder/model-00003-of-00003.safetensors", "编码器 3/3"),
                F("vae/diffusion_pytorch_model.safetensors", "图像 VAE"),
            ],
        },
    ];

    public static readonly IReadOnlyList<ResolutionOption> VIDEO_RESOLUTIONS =
    [
        new ResolutionOption(id: "v_832_480", labelZh: "832 × 480 (16:9 横屏标准 · 推荐)", labelEn: "832 × 480 (16:9 Landscape · Recommended)", width: 832, height: 480),
        new ResolutionOption(id: "v_480_832", labelZh: "480 × 832 (9:16 竖屏短视频)", labelEn: "480 × 832 (9:16 Vertical Short)", width: 480, height: 832),
        new ResolutionOption(id: "v_1280_720", labelZh: "1280 × 720 (16:9 720P 高清)", labelEn: "1280 × 720 (16:9 720P HD)", width: 1280, height: 720),
        new ResolutionOption(id: "v_720_1280", labelZh: "720 × 1280 (9:16 720P 竖屏)", labelEn: "720 × 1280 (9:16 720P Vertical)", width: 720, height: 1280),
        new ResolutionOption(id: "v_640_640", labelZh: "640 × 640 (1:1 方形)", labelEn: "640 × 640 (1:1 Square)", width: 640, height: 640),
    ];

    public static readonly IReadOnlyList<ResolutionOption> IMAGE_RESOLUTIONS =
    [
        new ResolutionOption(id: "i_1024_1024", labelZh: "1024 × 1024 (1:1 超清方图 · 推荐)", labelEn: "1024 × 1024 (1:1 Square HD · Recommended)", width: 1024, height: 1024),
        new ResolutionOption(id: "i_1344_768", labelZh: "1344 × 768 (16:9 电影横幅)", labelEn: "1344 × 768 (16:9 Cinematic Widescreen)", width: 1344, height: 768),
        new ResolutionOption(id: "i_768_1344", labelZh: "768 × 1344 (9:16 竖版海报)", labelEn: "768 × 1344 (9:16 Vertical Poster)", width: 768, height: 1344),
        new ResolutionOption(id: "i_1536_1024", labelZh: "1536 × 1024 (3:2 单反摄影)", labelEn: "1536 × 1024 (3:2 DSLR Photo)", width: 1536, height: 1024),
    ];

    public static readonly IReadOnlyList<ResolutionOption> MINIMAX_H3_IMAGE_RESOLUTIONS =
    [
        new ResolutionOption(id: "mh3i_2048_2048", labelZh: "2048 × 2048 (1:1 4MP 极速超清 · 推荐)", labelEn: "2048 × 2048 (1:1 4MP Ultra HD · Recommended)", width: 2048, height: 2048),
        new ResolutionOption(id: "mh3i_4096_4096", labelZh: "4096 × 4096 (1:1 16MP 原生 4K 巨幅)", labelEn: "4096 × 4096 (1:1 16MP Native 4K Square)", width: 4096, height: 4096),
        new ResolutionOption(id: "mh3i_5472_3072", labelZh: "5472 × 3072 (16:9 16MP 电影超宽 4K)", labelEn: "5472 × 3072 (16:9 16MP Cinematic 4K)", width: 5472, height: 3072),
        new ResolutionOption(id: "mh3i_3072_5472", labelZh: "3072 × 5472 (9:16 16MP 竖版巨幅 4K)", labelEn: "3072 × 5472 (9:16 16MP Vertical Poster 4K)", width: 3072, height: 5472),
        new ResolutionOption(id: "mh3i_4736_3552", labelZh: "4736 × 3552 (4:3 16MP 商业摄影 4K)", labelEn: "4736 × 3552 (4:3 16MP Commercial 4K)", width: 4736, height: 3552),
        new ResolutionOption(id: "mh3i_3552_4736", labelZh: "3552 × 4736 (3:4 16MP 杂志人像 4K)", labelEn: "3552 × 4736 (3:4 16MP Editorial Portrait 4K)", width: 3552, height: 4736),
    ];

    public static readonly IReadOnlyList<PresetTemplate> PRESET_TEMPLATES =
    [
        new PresetTemplate
        {
            Id = "tpl_shinkai_train",
            Category = ModelModality.VideoAudio,
            NameZh = "[动漫] 新海诚风 · 盛夏樱花铁道口",
            NameEn = "[Anime] Makoto Shinkai · Cherry Blossom Railway",
            VisualZh = "Masterpiece Makoto Shinkai anime style, 2D cel-shaded animation. A teenage girl in a Japanese sailor school uniform standing at a sunlit railway crossing in late spring. A green commuter train speeds past in front of her, creating a strong gust of wind that blows her long black hair and a flurry of pink cherry blossom petals across the frame. Dramatic lens flare, glowing cumulous clouds in a deep blue sky, vibrant cinematic lighting, smooth 24fps keyframe animation.",
            VisualEn = "Masterpiece Makoto Shinkai anime style, 2D cel-shaded animation. A teenage girl in a Japanese sailor school uniform standing at a sunlit railway crossing in late spring. A green commuter train speeds past in front of her, creating a strong gust of wind that blows her long black hair and a flurry of pink cherry blossom petals across the frame. Dramatic lens flare, glowing cumulous clouds in a deep blue sky, vibrant cinematic lighting, smooth 24fps keyframe animation.",
            Soundscape = "Metallic clacking of train wheels on railway tracks, rhythmic dinging of the railroad crossing warning bell, warm spring wind rustling through leaves, a soft gasp from the girl.",
            Music = "Emotional and nostalgic solo piano melody joined by warm acoustic guitar and soaring cinematic strings.",
        },
        new PresetTemplate
        {
            Id = "tpl_ufotable_duel",
            Category = ModelModality.VideoAudio,
            NameZh = "[动漫] 飞碟社风 · 苍蓝雷光剑术对决",
            NameEn = "[Anime] Ufotable Sakuga · Azure Lightning Sword Duel",
            VisualZh = "High-budget ufotable anime action sakuga, dynamic fast-paced camera tracking. A silver-haired anime swordsman in a dark haori coat dashes across a shattered stone temple under a blood-red full moon, swinging a katana wreathed in crackling blue lightning and swirling water dragon effects. Sparks and glowing embers fly toward the camera in slow-motion impact frames, intense anime shading, dramatic motion blur.",
            VisualEn = "High-budget ufotable anime action sakuga, dynamic fast-paced camera tracking. A silver-haired anime swordsman in a dark haori coat dashes across a shattered stone temple under a blood-red full moon, swinging a katana wreathed in crackling blue lightning and swirling water dragon effects. Sparks and glowing embers fly toward the camera in slow-motion impact frames, intense anime shading, dramatic motion blur.",
            Soundscape = "Sharp metallic sword unsheathing and clashing clang, roaring thunder and crackling electric arcs, heavy stone debris crumbling, fierce battle cry.",
            Music = "Epic dark orchestral battle music with fast taiko drums, intense choir, and distorted electric guitar riffs.",
        },
        new PresetTemplate
        {
            Id = "tpl_iceland_fpv",
            Category = ModelModality.VideoAudio,
            NameZh = "[写实] 冰岛巨型瀑布 FPV 穿越机航拍",
            NameEn = "[Realism] Iceland Waterfall FPV Drone Dive",
            VisualZh = "FPV drone shot diving down a majestic Icelandic waterfall surrounded by lush green moss and basalt columns under golden hour sunlight, mist rising into a rainbow.",
            VisualEn = "FPV drone shot diving down a majestic Icelandic waterfall surrounded by lush green moss and basalt columns under golden hour sunlight, mist rising into a rainbow.",
            Soundscape = "Roaring waterfall rushing water, wind whooshing past the camera, distant seabirds calling.",
            Music = "Epic cinematic acoustic guitar and soaring cello.",
        },
        new PresetTemplate
        {
            Id = "tpl_cyber_poster",
            Category = ModelModality.Image,
            NameZh = "[生图] 东方赛博霓虹商业海报（含中文排版）",
            NameEn = "[Image] Cyberpunk Neon Bund Poster (Chinese Title)",
            VisualZh = "超清商业电影海报摄影，雨夜霓虹闪烁的未来上海外滩街道，一位身着机能风银色夹克的少女回眸凝视镜头，景深虚化光斑，顶部醒目发光现代中文字体排版写着“未来纪元 2026”，极致光影细节，8K画质。",
            VisualEn = "Ultra-HD commercial sci-fi movie poster photography, rain-slicked cyberpunk Shanghai Bund street glowing with neon reflections at night, a young woman in a futuristic silver techwear jacket looking back at the camera, shallow depth of field bokeh flares, bold glowing modern title typography at the top reading \"FUTURE ERA 2026\" (未来纪元 2026), intricate lighting details, 8K.",
            Soundscape = "",
            Music = "",
        },
        new PresetTemplate
        {
            Id = "tpl_film_cafe",
            Category = ModelModality.Image,
            NameZh = "[生图] 胶片质感午后咖啡馆肖像",
            NameEn = "[Image] Hasselblad Film Afternoon Tokyo Cafe",
            VisualZh = "Hasselblad medium format film photography, soft golden afternoon sunlight streaming through wooden blinds onto a cozy minimalist Tokyo cafe table, a ceramic pour-over coffee dripper with rising steam beside an open sketchbook, natural film grain, ultra-detailed textures.",
            VisualEn = "Hasselblad medium format film photography, soft golden afternoon sunlight streaming through wooden blinds onto a cozy minimalist Tokyo cafe table, a ceramic pour-over coffee dripper with rising steam beside an open sketchbook, natural film grain, ultra-detailed textures.",
            Soundscape = "",
            Music = "",
        },
        new PresetTemplate
        {
            Id = "tpl_llm_storyboard",
            Category = ModelModality.LlmVLM,
            NameZh = "[推理] 为 AI 视频导演生成 3 组新海诚风分镜提示词",
            NameEn = "[Reasoning] Generate 3 Shinkai-Style AI Video Storyboard Prompts",
            VisualZh = "请帮我构思 3 组极具画面感的新海诚风格动画短片分镜，每组包含：1) 英文 Visual & Camera 镜头提示词；2) Soundscape 环境音描述；3) Background Music 配乐描述。",
            VisualEn = "Please design 3 vivid Makoto Shinkai-style anime short film storyboard shots. For each shot, provide: 1) Visual & Camera prompt; 2) Soundscape & SFX prompt; 3) Background Music prompt.",
            Soundscape = "",
            Music = "",
        },
        new PresetTemplate
        {
            Id = "tpl_vlm_reverse",
            Category = ModelModality.LlmVLM,
            NameZh = "[视觉] 深度解析图片构图并反推生图/生视频 Prompt",
            NameEn = "[Vision] Analyze Image Composition & Reverse-Engineer Prompt",
            VisualZh = "请仔细观察这张图片的主体、光影色调、镜头景别与艺术风格，先做简要中文分析，然后反推出一段高质量的英文生图/生视频 Prompt。",
            VisualEn = "Carefully analyze the subject, lighting, color palette, camera framing, and artistic style of this image. First give a concise visual breakdown, then write a high-quality English image/video generation prompt.",
            Soundscape = "",
            Music = "",
        },
    ];

    public static readonly IReadOnlyList<ComfyPresetPackItem> DEFAULT_COMFY_PRESET_PACKS =
    [
        new ComfyPresetPackItem
        {
            Id = "wan22_i2v_flf2v",
            Title = "万相 Wan2.2 14B · 图生视频 + 首尾帧双工作流",
            Badge = "新手首选 · 4步极速",
            Scenario = "含「单图动起来」与「首尾双图丝滑过渡」2 套中文画布，G4 约 25 秒出片，纯官方内置节点零红框报错",
            ApproxSizeGb = 35.4,
            Workflows =
            [
                "01_Wan2.2_极速单图生视频(4步傻瓜版).json",
                "02_Wan2.2_首尾帧丝滑过渡(双图4步极速).json",
            ],
            InstalledModels = 0,
            TotalModels = 6,
            WorkflowsInstalled = false,
            Ready = false,
        },
        new ComfyPresetPackItem
        {
            Id = "minimax_h3_i2v_flf2v",
            Title = "MiniMax-H3 Turbo · 图生有声视频 + 首尾帧双工作流",
            Badge = "自带立体声音效 · 8步加速",
            Scenario = "含「单图生有声视频」与「首尾双图有声过渡」2 套极简画布，一次生成高清动态画面 + 同步环境音效/配乐/对白",
            ApproxSizeGb = 39.1,
            Workflows =
            [
                "03_MiniMax-H3_极速图生有声视频(单图带立体声).json",
                "04_MiniMax-H3_首尾帧有声过渡(双图带立体声).json",
            ],
            InstalledModels = 0,
            TotalModels = 5,
            WorkflowsInstalled = false,
            Ready = false,
        },
        new ComfyPresetPackItem
        {
            Id = "ltx23_flf2v",
            Title = "LTX-2.3 (22B) · 首尾帧电影级运镜有声工作流",
            Badge = "电影推拉摇移 · 仅 2 个权重",
            Scenario = "专为首尾关键帧大幅度镜头运动优化（如高空俯拍平滑推镜至低角度特写 + 自动合成配乐音轨）",
            ApproxSizeGb = 36.3,
            Workflows =
            [
                "05_LTX-2.3_首尾帧电影运镜(双图极速带音效).json",
            ],
            InstalledModels = 0,
            TotalModels = 2,
            WorkflowsInstalled = false,
            Ready = false,
        },
        new ComfyPresetPackItem
        {
            Id = "hunyuan15_i2v_1080p",
            Title = "混元 HunyuanVideo 1.5 · 单图直出 1080P 超分工作流",
            Badge = "720P + 1080P 双路超清",
            Scenario = "已默认激活潜空间超分管线，传 1 张图一键自动渲染 720P 原片与 1920×1080 超高清成片",
            ApproxSizeGb = 43.5,
            Workflows =
            [
                "06_Hunyuan1.5_单图直出1080P超清视频.json",
            ],
            InstalledModels = 0,
            TotalModels = 7,
            WorkflowsInstalled = false,
            Ready = false,
        },
    ];

    /// <summary>Port of Swift <c>formatDuration(_:compact:)</c>.</summary>
    public static string FormatDuration(double sec, bool compact = false)
    {
        var inv = CultureInfo.InvariantCulture;
        double s = Math.Max(0.0, sec);
        if (s < 60.0)
        {
            if (compact) return string.Format(inv, "{0:F1}s", s);
            return LanguageManager.Shared.IsEN
                ? string.Format(inv, "{0:F1} s", s)
                : string.Format(inv, "{0:F1} 秒", s);
        }
        int mins = (int)s / 60;
        double rem = s - mins * 60;
        if (compact) return string.Format(inv, "{0}m{1:F0}s", mins, rem);
        return LanguageManager.Shared.IsEN
            ? string.Format(inv, "{0}m {1:F1}s", mins, rem)
            : string.Format(inv, "{0}分{1:F1}秒", mins, rem);
    }
}
