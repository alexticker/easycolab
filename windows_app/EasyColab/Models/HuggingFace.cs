using System.Text.Json.Serialization;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace EasyColab.Models;

public sealed record HFTrendingModelItem
{
    [JsonIgnore] public string Id => RepoId;
    [JsonPropertyName("repo_id")] public required string RepoId { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("pipeline_tag")] public required string PipelineTag { get; init; }
    [JsonPropertyName("modality")] public required string Modality { get; init; }
    [JsonPropertyName("is_gguf")] public required bool IsGguf { get; init; }
    [JsonPropertyName("is_gated")] public required bool IsGated { get; init; }
    [JsonPropertyName("suggested_pattern")] public required string SuggestedPattern { get; init; }
    [JsonPropertyName("likes")] public required long Likes { get; init; }
    [JsonPropertyName("downloads")] public required long Downloads { get; init; }
    [JsonPropertyName("param_count")] public long? ParamCount { get; init; } = null;
    [JsonPropertyName("param_label")] public string? ParamLabel { get; init; } = null;
    [JsonPropertyName("est_vram_gb")] public double? EstVramGb { get; init; } = null;
    [JsonPropertyName("deploy_status")] public string? DeployStatus { get; init; } = null;
    [JsonPropertyName("deploy_label_zh")] public string? DeployLabelZh { get; init; } = null;
    [JsonPropertyName("deploy_label_en")] public string? DeployLabelEn { get; init; } = null;

    [JsonIgnore]
    public string LocalizedDeployLabel
    {
        get
        {
            string rawZh = !string.IsNullOrEmpty(DeployLabelZh) ? DeployLabelZh : "显存待探测";
            string rawEn = !string.IsNullOrEmpty(DeployLabelEn) ? DeployLabelEn : "VRAM TBD";
            return StripEmojiCharacters(L(rawZh, rawEn));
        }
    }

    [JsonIgnore] public Color DeployBadgeColor => ColorHelper.DeployStatusColor(DeployStatus ?? "unknown");
    [JsonIgnore] public string DeployBadgeColorHex => ColorHelper.ToHex(DeployBadgeColor);
    /// <summary>UI-thread only: solid brush of <see cref="DeployBadgeColor"/>.</summary>
    [JsonIgnore] public SolidColorBrush DeployBadgeBrush => ColorHelper.Brush(DeployBadgeColor);
    /// <summary>UI-thread only: <c>deployBadgeColor.opacity(0.14)</c> capsule fill.</summary>
    [JsonIgnore] public SolidColorBrush DeployBadgeFillBrush => ColorHelper.Brush(DeployBadgeColor, 0.14);
}

public sealed record HFTrendingAPIResponse
{
    [JsonPropertyName("ok")] public bool? Ok { get; init; }
    [JsonPropertyName("category")] public string? Category { get; init; }
    [JsonPropertyName("models")] public List<HFTrendingModelItem>? Models { get; init; }
}

public sealed record HFModelWeightFileItem
{
    [JsonIgnore] public string Id => Filename;
    [JsonPropertyName("filename")] public required string Filename { get; init; }
    [JsonPropertyName("size_gb")] public required double SizeGb { get; init; }
    [JsonPropertyName("fit_status")] public required string FitStatus { get; init; }
    [JsonPropertyName("fit_zh")] public required string FitZh { get; init; }
    [JsonPropertyName("fit_en")] public required string FitEn { get; init; }

    [JsonIgnore] public string LocalizedFit => StripEmojiCharacters(L(FitZh, FitEn));

    [JsonIgnore] public Color FitColor => ColorHelper.DeployStatusColor(FitStatus);
    [JsonIgnore] public string FitColorHex => ColorHelper.ToHex(FitColor);
    /// <summary>UI-thread only: solid brush of <see cref="FitColor"/>.</summary>
    [JsonIgnore] public SolidColorBrush FitBrush => ColorHelper.Brush(FitColor);
}

public sealed record HFModelDetailInfo
{
    [JsonIgnore] public string Id => RepoId;
    [JsonPropertyName("ok")] public bool? Ok { get; init; }
    [JsonPropertyName("repo_id")] public required string RepoId { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("author")] public required string Author { get; init; }
    [JsonPropertyName("pipeline_tag")] public required string PipelineTag { get; init; }
    [JsonPropertyName("modality")] public required string Modality { get; init; }
    [JsonPropertyName("is_gguf")] public required bool IsGguf { get; init; }
    [JsonPropertyName("is_gated")] public required bool IsGated { get; init; }
    [JsonPropertyName("license")] public required string License { get; init; }
    [JsonPropertyName("base_model")] public required string BaseModel { get; init; }
    [JsonPropertyName("architecture")] public required string Architecture { get; init; }
    [JsonPropertyName("context_length")] public required int ContextLength { get; init; }
    [JsonPropertyName("languages")] public required List<string> Languages { get; init; }
    [JsonPropertyName("likes")] public required long Likes { get; init; }
    [JsonPropertyName("downloads")] public required long Downloads { get; init; }
    [JsonPropertyName("last_modified")] public required string LastModified { get; init; }
    [JsonPropertyName("used_storage_gb")] public required double UsedStorageGb { get; init; }
    [JsonPropertyName("param_count")] public required long ParamCount { get; init; }
    [JsonPropertyName("param_label")] public required string ParamLabel { get; init; }
    [JsonPropertyName("est_vram_gb")] public required double EstVramGb { get; init; }
    [JsonPropertyName("deploy_status")] public required string DeployStatus { get; init; }
    [JsonPropertyName("deploy_label_zh")] public required string DeployLabelZh { get; init; }
    [JsonPropertyName("deploy_label_en")] public required string DeployLabelEn { get; init; }
    [JsonPropertyName("weight_files")] public required List<HFModelWeightFileItem> WeightFiles { get; init; }
    [JsonPropertyName("readme_excerpt")] public required string ReadmeExcerpt { get; init; }

    [JsonIgnore]
    public string LocalizedDeployLabel =>
        StripEmojiCharacters(L(
            string.IsNullOrEmpty(DeployLabelZh) ? "显存待探测" : DeployLabelZh,
            string.IsNullOrEmpty(DeployLabelEn) ? "VRAM TBD" : DeployLabelEn));

    [JsonIgnore] public Color DeployBadgeColor => ColorHelper.DeployStatusColor(DeployStatus);
    [JsonIgnore] public string DeployBadgeColorHex => ColorHelper.ToHex(DeployBadgeColor);
    /// <summary>UI-thread only: solid brush of <see cref="DeployBadgeColor"/>.</summary>
    [JsonIgnore] public SolidColorBrush DeployBadgeBrush => ColorHelper.Brush(DeployBadgeColor);
    /// <summary>UI-thread only: <c>deployBadgeColor.opacity(0.14)</c> capsule fill.</summary>
    [JsonIgnore] public SolidColorBrush DeployBadgeFillBrush => ColorHelper.Brush(DeployBadgeColor, 0.14);
}
