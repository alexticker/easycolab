using System.Globalization;
using System.Text.Json.Serialization;

namespace EasyColab.Models;

// MARK: - 多模型与多模态数据结构

/// <summary>Port of Swift <c>enum ModelModality: String, Codable</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ModelModality>))]
public enum ModelModality
{
    [JsonStringEnumMemberName("video_audio")] VideoAudio,
    [JsonStringEnumMemberName("video")] Video,
    [JsonStringEnumMemberName("image")] Image,
    [JsonStringEnumMemberName("llm_vlm")] LlmVLM,
    [JsonStringEnumMemberName("comfyui")] ComfyUI,
}

/// <summary>Raw values (Swift <c>rawValue</c> / <c>init(rawValue:)</c>) for <see cref="ModelModality"/>.</summary>
public static class ModelModalityExtensions
{
    public static string RawValue(this ModelModality m) => m switch
    {
        ModelModality.VideoAudio => "video_audio",
        ModelModality.Video => "video",
        ModelModality.Image => "image",
        ModelModality.LlmVLM => "llm_vlm",
        ModelModality.ComfyUI => "comfyui",
        _ => "llm_vlm",
    };

    /// <summary>Swift <c>ModelModality(rawValue:)</c> — returns null for unknown raw values.</summary>
    public static ModelModality? FromRawValue(string? raw) => raw switch
    {
        "video_audio" => ModelModality.VideoAudio,
        "video" => ModelModality.Video,
        "image" => ModelModality.Image,
        "llm_vlm" => ModelModality.LlmVLM,
        "comfyui" => ModelModality.ComfyUI,
        _ => null,
    };
}

/// <summary>Port of Swift <c>TaskModeItem</c> (equality / hash by <see cref="Id"/> only).</summary>
public sealed record TaskModeItem
{
    public TaskModeItem() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public TaskModeItem(string id, string titleZh, string titleEn, string subtitleZh, string subtitleEn, string icon)
    {
        Id = id;
        TitleZh = titleZh;
        TitleEn = titleEn;
        SubtitleZh = subtitleZh;
        SubtitleEn = subtitleEn;
        Icon = icon;
    }

    public required string Id { get; init; }
    public required string TitleZh { get; init; }
    public required string TitleEn { get; init; }
    public required string SubtitleZh { get; init; }
    public required string SubtitleEn { get; init; }
    /// <summary>SF Symbol name (as in Swift); views map it to a Segoe Fluent glyph.</summary>
    public required string Icon { get; init; }

    public string Title => L(TitleZh, TitleEn);
    public string Subtitle => L(SubtitleZh, SubtitleEn);

    public bool Equals(TaskModeItem? other) => other is not null && other.Id == Id;
    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
}

/// <summary>Port of Swift <c>CustomHFModelDTO: Codable</c> (persisted under the custom-models settings key and sent by the server).</summary>
public sealed record CustomHFModelDTO
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("repo_id")] public required string RepoId { get; init; }
    [JsonPropertyName("include_pattern")] public string? IncludePattern { get; init; }
    [JsonPropertyName("modality")] public string? Modality { get; init; }
    [JsonPropertyName("pipeline_arch")] public string? PipelineArch { get; init; }
    [JsonPropertyName("category")] public string? Category { get; init; }
    [JsonPropertyName("approx_size_gb")] public double? ApproxSizeGb { get; init; }
    [JsonPropertyName("is_custom")] public bool? IsCustom { get; init; }
}

/// <summary>Port of Swift <c>OpenModelSpec</c> (equality / hash by <see cref="Id"/> only).</summary>
public sealed record OpenModelSpec
{
    public required string Id { get; init; }
    public required string NameZh { get; init; }
    public required string NameEn { get; init; }
    public required string RepoId { get; init; }
    public required string BadgeZh { get; init; }
    public required string BadgeEn { get; init; }
    public required string SummaryZh { get; init; }
    public required string SummaryEn { get; init; }
    public required double ApproxSizeGB { get; init; }
    /// <summary>SF Symbol name (as in Swift); views map it to a Segoe Fluent glyph.</summary>
    public required string Icon { get; init; }
    public required ModelModality Modality { get; init; }
    public required double DefaultSteps { get; init; }
    public required IReadOnlyList<TaskModeItem> Modes { get; init; }
    public required IReadOnlyList<ModelFileItem> DefaultFiles { get; init; }
    public string IncludePattern { get; init; } = "";
    public bool IsCustom { get; init; } = false;

    public string Name => L(NameZh, NameEn);
    public string Badge => L(BadgeZh, BadgeEn);
    public string Summary => L(SummaryZh, SummaryEn);

    public bool Equals(OpenModelSpec? other) => other is not null && other.Id == Id;
    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);

    public static OpenModelSpec FromCustomDTO(CustomHFModelDTO dto)
    {
        ModelModality mod = dto.Modality switch
        {
            "video_audio" => ModelModality.VideoAudio,
            "image" => ModelModality.Image,
            _ => ModelModality.LlmVLM,
        };
        double sizeGB = dto.ApproxSizeGb ?? 0.0;
        string arch = (dto.PipelineArch ?? "gguf").ToUpperInvariant();
        string sizeStr = sizeGB.ToString("F1", CultureInfo.InvariantCulture);
        string badgeZh = sizeGB > 0 ? $"自定义 {arch} · {sizeStr} GB" : $"自定义 HF · {arch}";
        string badgeEn = sizeGB > 0 ? $"Custom {arch} · {sizeStr} GB" : $"Custom HF · {arch}";
        string patDesc = !string.IsNullOrEmpty(dto.IncludePattern) ? $" ({dto.IncludePattern})" : "";
        string summaryZh = dto.Category ?? $"自定义 HuggingFace 仓库 {dto.RepoId}{patDesc}";
        string summaryEn = $"Custom HuggingFace repo {dto.RepoId}{patDesc}";
        List<TaskModeItem> modes;
        string iconName;
        double defSteps;
        switch (mod)
        {
            case ModelModality.VideoAudio:
            case ModelModality.Video:
                iconName = "film.stack";
                defSteps = 35;
                modes =
                [
                    new TaskModeItem(id: "t2va", titleZh: "文生音视频", titleEn: "Text to Video+Audio", subtitleZh: "输入镜头与声音描述，同步生成视频与立体声音轨", subtitleEn: "Generate HD video and synchronized stereo audio from prompts", icon: "film"),
                    new TaskModeItem(id: "fl2va", titleZh: "首尾帧生视频", titleEn: "First/Last Frame to Video", subtitleZh: "上传首帧或尾帧参考图，精准控制起止画面与运镜过渡", subtitleEn: "Upload start or end keyframes to lock composition and camera transitions", icon: "photo.on.rectangle.angled"),
                ];
                break;
            case ModelModality.Image:
                iconName = "sparkles.rectangle.stack";
                defSteps = 20;
                modes =
                [
                    new TaskModeItem(id: "t2i", titleZh: "高清文生图", titleEn: "HD Text-to-Image", subtitleZh: "自定义 HuggingFace 图像生成管线", subtitleEn: "Custom HuggingFace image generation pipeline", icon: "photo.artframe"),
                    new TaskModeItem(id: "i2i_edit", titleZh: "图生图重绘", titleEn: "Image-to-Image Restyle", subtitleZh: "上传参考底图并调节重绘幅度，保留构图生成全新细节", subtitleEn: "Upload a reference image and adjust denoising strength to restyle", icon: "wand.and.rays"),
                ];
                break;
            default: // .llmVLM, .comfyUI
                iconName = "shippingbox.fill";
                defSteps = 20;
                modes =
                [
                    new TaskModeItem(id: "chat", titleZh: "深度推理对话", titleEn: "Deep Reasoning Chat", subtitleZh: "自定义 HuggingFace 模型对话、长思维链推理与代码创作", subtitleEn: "Custom HuggingFace model chat, CoT reasoning & coding", icon: "bubble.left.and.bubble.right"),
                    new TaskModeItem(id: "vlm", titleZh: "图像视觉理解", titleEn: "Vision Understanding", subtitleZh: "拖入图片进行画面解析、OCR 识别或反推提示词", subtitleEn: "Drop images for scene analysis, OCR, or prompt reverse-engineering", icon: "eye"),
                    new TaskModeItem(id: "api", titleZh: "API 协议网关", titleEn: "API Protocol Gateway", subtitleZh: "提供 OpenAI (/v1) 与 Anthropic (/v1/messages) 双协议公网接口", subtitleEn: "Public dual-protocol gateway compatible with OpenAI (/v1) & Anthropic (/v1/messages)", icon: "network"),
                ];
                break;
        }
        return new OpenModelSpec
        {
            Id = dto.Id,
            NameZh = dto.Name,
            NameEn = dto.Name,
            RepoId = dto.RepoId,
            BadgeZh = badgeZh,
            BadgeEn = badgeEn,
            SummaryZh = summaryZh,
            SummaryEn = summaryEn,
            ApproxSizeGB = sizeGB,
            Icon = iconName,
            Modality = mod,
            DefaultSteps = defSteps,
            Modes = modes,
            DefaultFiles = [],
            IncludePattern = dto.IncludePattern ?? "",
            IsCustom = true,
        };
    }
}

/// <summary>Port of Swift <c>ModelFileItem: Codable</c> (<c>id</c> = <c>name</c>).</summary>
public sealed record ModelFileItem
{
    public ModelFileItem() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ModelFileItem(string name, string? desc, bool exists, double size_gb)
    {
        Name = name;
        Desc = desc;
        Exists = exists;
        SizeGb = size_gb;
    }

    [JsonIgnore] public string Id => Name;
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("desc")] public string? Desc { get; init; }
    [JsonPropertyName("exists")] public required bool Exists { get; init; }
    [JsonPropertyName("size_gb")] public required double SizeGb { get; init; }

    [JsonIgnore]
    public string? LocalizedDesc
    {
        get
        {
            if (string.IsNullOrEmpty(Desc)) return null;
            return LocalizeModelFileDesc(Desc);
        }
    }
}

/// <summary>Port of Swift <c>ResolutionOption</c> (equality / hash by id + width + height).</summary>
public sealed record ResolutionOption
{
    public ResolutionOption() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ResolutionOption(string id, string labelZh, string labelEn, int width, int height)
    {
        Id = id;
        LabelZh = labelZh;
        LabelEn = labelEn;
        Width = width;
        Height = height;
    }

    public required string Id { get; init; }
    public required string LabelZh { get; init; }
    public required string LabelEn { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    public string Label => L(LabelZh, LabelEn);

    public bool Equals(ResolutionOption? other) =>
        other is not null && other.Id == Id && other.Width == Width && other.Height == Height;

    public override int GetHashCode() => HashCode.Combine(Id, Width, Height);
}

/// <summary>Port of Swift <c>PresetTemplate</c>.</summary>
public sealed record PresetTemplate
{
    public required string Id { get; init; }
    public required ModelModality Category { get; init; }
    public required string NameZh { get; init; }
    public required string NameEn { get; init; }
    public required string VisualZh { get; init; }
    public required string VisualEn { get; init; }
    public required string Soundscape { get; init; }
    public required string Music { get; init; }

    public string Name => L(NameZh, NameEn);
    public string Visual => L(VisualZh, VisualEn);
}
