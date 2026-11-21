using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EasyColab.ViewModels;

// MARK: - StudioViewModel 扩展：自定义 HuggingFace 模型仓库注册、热榜与详情页

public sealed partial class StudioViewModel
{
    private const string CustomModelsStorageKey = "easycolab.macos.custom_models.v1";
    private const string LegacyCustomModelsStorageKey = "customModels";

    private static readonly HttpClient HfDirectHttpClient = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static readonly string[] HfHosts = ["https://huggingface.co", "https://hf-mirror.com"];

    private static readonly string[] HfUrlPrefixes =
    [
        "https://huggingface.co/",
        "http://huggingface.co/",
        "https://hf.co/",
        "http://hf.co/",
        "huggingface.co/",
        "hf.co/",
    ];

    private static readonly Regex SlugSanitizeRegex = new("[^a-z0-9]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private void ReplaceCustomModelsInternal(IEnumerable<OpenModelSpec> items)
    {
        CustomModels.Clear();
        foreach (var spec in items)
        {
            CustomModels.Add(spec);
        }
        OnPropertyChanged(nameof(CustomModels));
        OnPropertyChanged(nameof(AllAvailableModels));
    }

    private void UpsertCustomModelInternal(OpenModelSpec spec)
    {
        int idx = -1;
        for (int i = 0; i < CustomModels.Count; i++)
        {
            if (CustomModels[i].Id == spec.Id)
            {
                idx = i;
                break;
            }
        }
        if (idx >= 0)
        {
            CustomModels[idx] = spec;
        }
        else
        {
            CustomModels.Add(spec);
        }
        OnPropertyChanged(nameof(CustomModels));
        OnPropertyChanged(nameof(AllAvailableModels));
    }

    private void RemoveCustomModelsWhereInternal(Predicate<OpenModelSpec> match)
    {
        bool removed = false;
        for (int i = CustomModels.Count - 1; i >= 0; i--)
        {
            if (match(CustomModels[i]))
            {
                CustomModels.RemoveAt(i);
                removed = true;
            }
        }
        if (removed)
        {
            OnPropertyChanged(nameof(CustomModels));
            OnPropertyChanged(nameof(AllAvailableModels));
        }
    }

    public void LoadPersistedCustomModels()
    {
        var dtos = SettingsStore.Shared.Get<List<CustomHFModelDTO>>(CustomModelsStorageKey, ModelJson.Options);
        bool fromLegacy = false;
        if (dtos is null)
        {
            dtos = SettingsStore.Shared.Get<List<CustomHFModelDTO>>(LegacyCustomModelsStorageKey, ModelJson.Options);
            fromLegacy = dtos is not null;
        }
        if (dtos is null) return;

        var filtered = dtos
            .Where(d => !string.Equals(d.RepoId, "minimaxai/minimax-h3-image", StringComparison.OrdinalIgnoreCase))
            .ToList();
        ReplaceCustomModelsInternal(filtered.Select(OpenModelSpec.FromCustomDTO));
        if (filtered.Count != dtos.Count || fromLegacy)
        {
            SavePersistedCustomModels();
        }
    }

    public void SavePersistedCustomModels()
    {
        var dtos = CustomModels.Select(spec =>
        {
            string modStr = spec.Modality switch
            {
                ModelModality.VideoAudio or ModelModality.Video => "video_audio",
                ModelModality.Image => "image",
                _ => "llm_vlm",
            };
            return new CustomHFModelDTO
            {
                Id = spec.Id,
                Name = spec.NameZh,
                RepoId = spec.RepoId,
                IncludePattern = spec.IncludePattern,
                Modality = modStr,
                PipelineArch = null,
                Category = spec.SummaryZh,
                ApproxSizeGb = spec.ApproxSizeGB,
                IsCustom = true,
            };
        }).ToList();

        SettingsStore.Shared.Set(CustomModelsStorageKey, dtos, ModelJson.Options);
    }

    public void MergeRemoteCustomModels(IReadOnlyList<CustomHFModelDTO> remoteDTOs)
    {
        var validRemote = remoteDTOs
            .Where(d => !string.Equals(d.RepoId, "minimaxai/minimax-h3-image", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var existingById = new Dictionary<string, OpenModelSpec>(StringComparer.Ordinal);
        var currentIds = new List<string>(CustomModels.Count);
        foreach (var item in CustomModels)
        {
            currentIds.Add(item.Id);
            if (!string.Equals(item.RepoId, "minimaxai/minimax-h3-image", StringComparison.OrdinalIgnoreCase))
            {
                existingById[item.Id] = item;
            }
        }

        var remoteIdSet = new HashSet<string>(StringComparer.Ordinal);
        var orderedIds = new List<string>(validRemote.Count + currentIds.Count);
        foreach (var dto in validRemote)
        {
            existingById[dto.Id] = OpenModelSpec.FromCustomDTO(dto);
            if (remoteIdSet.Add(dto.Id))
            {
                orderedIds.Add(dto.Id);
            }
        }
        foreach (string id in currentIds)
        {
            if (!remoteIdSet.Contains(id))
            {
                orderedIds.Add(id);
            }
        }

        var merged = new List<OpenModelSpec>(orderedIds.Count);
        foreach (string id in orderedIds)
        {
            if (existingById.TryGetValue(id, out var spec))
            {
                merged.Add(spec);
            }
        }
        ReplaceCustomModelsInternal(merged);
        SavePersistedCustomModels();
    }

    public string NormalizeHFRepoId(string raw)
    {
        string s = (raw ?? string.Empty).Trim();
        foreach (string prefix in HfUrlPrefixes)
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                s = s[prefix.Length..];
                break;
            }
        }
        string[] parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return $"{parts[0]}/{parts[1]}";
        }
        return s;
    }

    private static string GetJsonString(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(key, out var prop)
        && prop.ValueKind == JsonValueKind.String
            ? (prop.GetString() ?? string.Empty)
            : string.Empty;

    private static long GetJsonInt64(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(key, out var prop)) return 0;
        if (prop.ValueKind == JsonValueKind.Number)
        {
            if (prop.TryGetInt64(out long l)) return l;
            return (long)prop.GetDouble();
        }
        return 0;
    }

    private static double GetJsonDouble(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(key, out var prop)) return 0.0;
        if (prop.ValueKind == JsonValueKind.Number) return prop.GetDouble();
        return 0.0;
    }

    private static bool ParseIsGated(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty("gated", out var gatedVal))
            return false;

        if (gatedVal.ValueKind == JsonValueKind.True) return true;
        if (gatedVal.ValueKind == JsonValueKind.False) return false;
        if (gatedVal.ValueKind == JsonValueKind.String)
        {
            string sl = (gatedVal.GetString() ?? string.Empty).ToLowerInvariant();
            return !string.IsNullOrEmpty(sl) && sl != "false" && sl != "0" && sl != "none";
        }
        return false;
    }

    private static List<string> ParseLowerStringArray(JsonElement obj, string key)
    {
        var result = new List<string>();
        if (obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(key, out var arr)
            && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && el.GetString() is { } s)
                {
                    result.Add(s.ToLowerInvariant());
                }
            }
        }
        return result;
    }

    private static async Task<string?> FetchHfDirectStringAsync(string url, string hfToken, double timeoutSeconds)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "EasyColab-Windows/2.0");
            if (!string.IsNullOrEmpty(hfToken))
            {
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {hfToken}");
            }
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var resp = await HfDirectHttpClient.SendAsync(req, cts.Token);
            if (resp.StatusCode != HttpStatusCode.OK) return null;
            return await resp.Content.ReadAsStringAsync(cts.Token);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]?> FetchHfDirectBytesAsync(string url, string hfToken, double timeoutSeconds)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "EasyColab-Windows/2.0");
            if (!string.IsNullOrEmpty(hfToken))
            {
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {hfToken}");
            }
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var resp = await HfDirectHttpClient.SendAsync(req, cts.Token);
            if (resp.StatusCode != HttpStatusCode.OK) return null;
            return await resp.Content.ReadAsByteArrayAsync(cts.Token);
        }
        catch
        {
            return null;
        }
    }

    public async Task FetchTrendingHFModelsAsync(string? category = null, string searchQuery = "", bool forceRefresh = false)
    {
        string cat = (category ?? TrendingHFCategory).Trim();
        string sq = (searchQuery ?? string.Empty).Trim();
        if (TrendingHFCategory != cat)
        {
            TrendingHFCategory = cat;
        }
        IsLoadingTrendingHF = true;
        try
        {
            string baseUrl = NormalizedBaseURL;
            if (IsConnected && !string.IsNullOrEmpty(baseUrl))
            {
                try
                {
                    string qs =
                        $"category={Uri.EscapeDataString(cat)}"
                        + "&limit=12"
                        + $"&hf_token={Uri.EscapeDataString(HfToken ?? string.Empty)}"
                        + $"&force_refresh={(forceRefresh ? "true" : "false")}"
                        + $"&search_query={Uri.EscapeDataString(sq)}";
                    var (code, body) = await SendAdminRequestAsync(
                        $"{baseUrl}/minimax_api/hf_trending?{qs}",
                        HttpMethod.Get,
                        timeoutSeconds: 12);
                    if (code == HttpStatusCode.OK)
                    {
                        var dec = ModelJson.TryDecode<HFTrendingAPIResponse>(body);
                        if (dec?.Models is { Count: > 0 } models)
                        {
                            TrendingHFModels = [.. models];
                            return;
                        }
                    }
                }
                catch { }
            }

            const string expandQS = "&expand[]=safetensors&expand[]=gguf&expand[]=likes&expand[]=downloads&expand[]=pipeline_tag&expand[]=tags&expand[]=gated";
            string encodedSQ = Uri.EscapeDataString(sq);
            string searchQS = string.IsNullOrEmpty(encodedSQ) ? "" : $"&search={encodedSQ}";

            string[] queryPaths = cat switch
            {
                "gguf" => [$"/api/models?sort=trendingScore&direction=-1&filter=gguf&limit=20{searchQS}{expandQS}"],
                "llm" => string.IsNullOrEmpty(sq)
                    ? [
                        $"/api/models?sort=trendingScore&direction=-1&pipeline_tag=text-generation&limit=18{expandQS}",
                        $"/api/models?sort=trendingScore&direction=-1&pipeline_tag=image-text-to-text&limit=10{expandQS}",
                    ]
                    : [$"/api/models?sort=trendingScore&direction=-1&pipeline_tag=text-generation&limit=20{searchQS}{expandQS}"],
                "image_video" => [
                    $"/api/models?sort=trendingScore&direction=-1&pipeline_tag=text-to-image&limit=12{searchQS}{expandQS}",
                    $"/api/models?sort=trendingScore&direction=-1&pipeline_tag=image-to-video&limit=10{searchQS}{expandQS}",
                ],
                _ => [$"/api/models?sort=trendingScore&direction=-1&limit=22{searchQS}{expandQS}"],
            };

            string tok = (HfToken ?? string.Empty).Trim();
            var parsed = new List<HFTrendingModelItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (string qpath in queryPaths)
            {
                string? rawJson = null;
                foreach (string host in HfHosts)
                {
                    string? fetched = await FetchHfDirectStringAsync($"{host}{qpath}", tok, timeoutSeconds: 10);
                    if (!string.IsNullOrEmpty(fetched))
                    {
                        rawJson = fetched;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(rawJson)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(rawJson);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                    foreach (var obj in doc.RootElement.EnumerateArray())
                    {
                        if (obj.ValueKind != JsonValueKind.Object) continue;
                        string rid = GetJsonString(obj, "id");
                        if (string.IsNullOrEmpty(rid)) rid = GetJsonString(obj, "modelId");
                        rid = rid.Trim();
                        if (!rid.Contains('/') || !seen.Add(rid)) continue;

                        string ridLow = rid.ToLowerInvariant();
                        string ptag = GetJsonString(obj, "pipeline_tag").ToLowerInvariant();
                        var tags = ParseLowerStringArray(obj, "tags");
                        bool isGGUF = ridLow.Contains("gguf", StringComparison.Ordinal) || tags.Contains("gguf");
                        bool isGated = ParseIsGated(obj);

                        if (string.IsNullOrEmpty(sq) && cat == "all" && !isGGUF)
                        {
                            if (UNSUPPORTED_HF_PIPELINE_TAGS.Contains(ptag)) continue;
                            if (!string.IsNullOrEmpty(ptag) && !SUPPORTED_TRENDING_PIPELINE_TAGS.Contains(ptag)) continue;
                            if (string.IsNullOrEmpty(ptag)
                                && !tags.Contains("transformers")
                                && !tags.Contains("diffusers")
                                && !tags.Contains("text-generation"))
                            {
                                continue;
                            }
                        }

                        string modality;
                        if (ptag.Contains("video", StringComparison.Ordinal)
                            || tags.Contains("text-to-video")
                            || tags.Contains("image-to-video"))
                        {
                            modality = "video_audio";
                        }
                        else if (ptag.Contains("text-to-image", StringComparison.Ordinal)
                                 || ptag.Contains("image-to-image", StringComparison.Ordinal)
                                 || (tags.Contains("diffusers") && !isGGUF))
                        {
                            modality = "image";
                        }
                        else
                        {
                            modality = "llm_vlm";
                        }

                        string[] ridParts = rid.Split('/');
                        string shortName = ridParts.Length > 0 ? ridParts[^1] : rid;
                        var sizing = AnalyzeHFModelSizing(obj, rid, isGGUF);

                        parsed.Add(new HFTrendingModelItem
                        {
                            RepoId = rid,
                            Name = shortName,
                            PipelineTag = string.IsNullOrEmpty(ptag) ? (isGGUF ? "gguf" : "text-generation") : ptag,
                            Modality = modality,
                            IsGguf = isGGUF,
                            IsGated = isGated,
                            SuggestedPattern = isGGUF ? "*Q4_K_M*.gguf" : "",
                            Likes = GetJsonInt64(obj, "likes"),
                            Downloads = GetJsonInt64(obj, "downloads"),
                            ParamCount = sizing.ParamCount,
                            ParamLabel = sizing.ParamLabel,
                            EstVramGb = sizing.EstVramGb,
                            DeployStatus = sizing.DeployStatus,
                            DeployLabelZh = sizing.DeployZh,
                            DeployLabelEn = sizing.DeployEn,
                        });
                        if (parsed.Count >= 12) break;
                    }
                }
                catch { }

                if (parsed.Count >= 12) break;
            }

            if (parsed.Count > 0 || !string.IsNullOrEmpty(sq))
            {
                TrendingHFModels = [.. parsed];
            }
        }
        finally
        {
            IsLoadingTrendingHF = false;
        }
    }

    public async Task FetchHFModelDetailAsync(string repoId)
    {
        string cleanId = NormalizeHFRepoId(repoId);
        if (!cleanId.Contains('/')) return;

        ShowHFModelDetailOverlay = true;
        IsLoadingHFModelDetail = true;
        try
        {
            string baseUrl = NormalizedBaseURL;
            string tok = (HfToken ?? string.Empty).Trim();
            if (IsConnected && !string.IsNullOrEmpty(baseUrl))
            {
                try
                {
                    string qs = $"repo_id={Uri.EscapeDataString(cleanId)}&hf_token={Uri.EscapeDataString(tok)}";
                    var (code, body) = await SendAdminRequestAsync(
                        $"{baseUrl}/minimax_api/hf_model_detail?{qs}",
                        HttpMethod.Get,
                        timeoutSeconds: 14);
                    if (code == HttpStatusCode.OK)
                    {
                        var dec = ModelJson.TryDecode<HFModelDetailInfo>(body);
                        if (dec is not null && dec.Ok == true)
                        {
                            ActiveHFModelDetail = dec with { ReadmeExcerpt = CleanHFReadmeExcerpt(dec.ReadmeExcerpt) };
                            return;
                        }
                    }
                }
                catch { }
            }

            // Offline / direct HuggingFace API fallback
            string? metaJson = null;
            foreach (string host in HfHosts)
            {
                string? fetched = await FetchHfDirectStringAsync($"{host}/api/models/{cleanId}?blobs=true", tok, timeoutSeconds: 12);
                if (!string.IsNullOrEmpty(fetched))
                {
                    metaJson = fetched;
                    break;
                }
            }
            if (string.IsNullOrEmpty(metaJson)) return;

            JsonDocument? metaDoc = null;
            try
            {
                metaDoc = JsonDocument.Parse(metaJson);
                if (metaDoc.RootElement.ValueKind != JsonValueKind.Object) return;
                JsonElement meta = metaDoc.RootElement;

                string rid = GetJsonString(meta, "id");
                if (string.IsNullOrEmpty(rid)) rid = GetJsonString(meta, "modelId");
                if (string.IsNullOrEmpty(rid)) rid = cleanId;
                rid = rid.Trim();

                string ridLow = rid.ToLowerInvariant();
                string ptag = GetJsonString(meta, "pipeline_tag").ToLowerInvariant();
                var tags = ParseLowerStringArray(meta, "tags");
                bool hasGgufDict = meta.TryGetProperty("gguf", out var ggufDict)
                    && ggufDict.ValueKind == JsonValueKind.Object
                    && ggufDict.EnumerateObject().Any();
                bool isGGUF = ridLow.Contains("gguf", StringComparison.Ordinal) || tags.Contains("gguf") || hasGgufDict;
                bool isGated = ParseIsGated(meta);

                string modality;
                if (ptag.Contains("video", StringComparison.Ordinal)
                    || tags.Contains("text-to-video")
                    || tags.Contains("image-to-video"))
                {
                    modality = "video_audio";
                }
                else if (ptag.Contains("text-to-image", StringComparison.Ordinal)
                         || ptag.Contains("image-to-image", StringComparison.Ordinal)
                         || (tags.Contains("diffusers") && !isGGUF))
                {
                    modality = "image";
                }
                else
                {
                    modality = "llm_vlm";
                }

                bool hasCardData = meta.TryGetProperty("cardData", out var cardData) && cardData.ValueKind == JsonValueKind.Object;
                bool hasConfigData = meta.TryGetProperty("config", out var configData) && configData.ValueKind == JsonValueKind.Object;

                string lic = "";
                if (hasCardData && cardData.TryGetProperty("license", out var licEl))
                {
                    if (licEl.ValueKind == JsonValueKind.String)
                    {
                        lic = licEl.GetString() ?? "";
                    }
                    else if (licEl.ValueKind == JsonValueKind.Array)
                    {
                        var lics = new List<string>();
                        foreach (var el in licEl.EnumerateArray())
                        {
                            if (el.ValueKind == JsonValueKind.String && el.GetString() is { } s) lics.Add(s);
                        }
                        lic = string.Join(", ", lics);
                    }
                }
                if (string.IsNullOrEmpty(lic)) lic = L("未标注 (见模型页)", "Unspecified");

                string baseModel = "";
                if (hasCardData && cardData.TryGetProperty("base_model", out var bmEl))
                {
                    if (bmEl.ValueKind == JsonValueKind.String)
                    {
                        baseModel = bmEl.GetString() ?? "";
                    }
                    else if (bmEl.ValueKind == JsonValueKind.Array)
                    {
                        var bms = new List<string>();
                        foreach (var el in bmEl.EnumerateArray())
                        {
                            if (el.ValueKind == JsonValueKind.String && el.GetString() is { } s)
                            {
                                bms.Add(s);
                                if (bms.Count >= 2) break;
                            }
                        }
                        baseModel = string.Join(", ", bms);
                    }
                }

                string archStr = "";
                if (hasConfigData
                    && configData.TryGetProperty("architectures", out var archsEl)
                    && archsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in archsEl.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(el.GetString()))
                        {
                            archStr = el.GetString()!;
                            break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(archStr) && hasGgufDict)
                {
                    archStr = GetJsonString(ggufDict, "architecture");
                }
                if (string.IsNullOrEmpty(archStr) && hasConfigData)
                {
                    archStr = GetJsonString(configData, "model_type");
                }

                int ctxLen = hasGgufDict ? (int)GetJsonInt64(ggufDict, "context_length") : 0;

                var langs = new List<string>();
                if (hasCardData && cardData.TryGetProperty("language", out var langEl))
                {
                    if (langEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in langEl.EnumerateArray())
                        {
                            if (el.ValueKind == JsonValueKind.String && el.GetString() is { } s)
                            {
                                langs.Add(s);
                                if (langs.Count >= 8) break;
                            }
                        }
                    }
                    else if (langEl.ValueKind == JsonValueKind.String && langEl.GetString() is { } s)
                    {
                        langs.Add(s);
                    }
                }

                double usedStorageBytes = GetJsonDouble(meta, "usedStorage");
                double usedStorageGb = usedStorageBytes > 0
                    ? Math.Round((usedStorageBytes / 1_073_741_824.0) * 100.0, MidpointRounding.AwayFromZero) / 100.0
                    : 0.0;

                var weightFiles = new List<HFModelWeightFileItem>();
                double totalWeightFilesGb = 0.0;
                if (meta.TryGetProperty("siblings", out var sibsEl) && sibsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sib in sibsEl.EnumerateArray())
                    {
                        if (sib.ValueKind != JsonValueKind.Object) continue;
                        string fname = GetJsonString(sib, "rfilename").Trim();
                        string fl = fname.ToLowerInvariant();
                        if (!fl.EndsWith(".gguf", StringComparison.Ordinal)
                            && !fl.EndsWith(".safetensors", StringComparison.Ordinal)
                            && !fl.EndsWith(".bin", StringComparison.Ordinal)
                            && !fl.EndsWith(".pth", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        double fBytes = GetJsonDouble(sib, "size");
                        if (fBytes <= 0 && sib.TryGetProperty("lfs", out var lfsEl) && lfsEl.ValueKind == JsonValueKind.Object)
                        {
                            fBytes = GetJsonDouble(lfsEl, "size");
                        }
                        double sizeGb = fBytes > 0
                            ? Math.Round((fBytes / 1_073_741_824.0) * 100.0, MidpointRounding.AwayFromZero) / 100.0
                            : 0.0;
                        totalWeightFilesGb += sizeGb;
                        double vramNeed = sizeGb > 0
                            ? Math.Round((sizeGb * 1.12 + 1.0) * 10.0, MidpointRounding.AwayFromZero) / 10.0
                            : 0.0;

                        string fStatus, fZh, fEn;
                        if (vramNeed <= 0)
                        {
                            fStatus = "unknown"; fZh = "体积待测"; fEn = "Size TBD";
                        }
                        else if (vramNeed <= 14.5)
                        {
                            fStatus = "great"; fZh = "T4(15G)可跑"; fEn = "T4 15G Ready";
                        }
                        else if (vramNeed <= 22.5)
                        {
                            fStatus = "good"; fZh = "L4(24G)可跑"; fEn = "L4 24G Ready";
                        }
                        else if (vramNeed <= 39.0)
                        {
                            fStatus = "a100"; fZh = "需A100(40G)"; fEn = "A100 40G";
                        }
                        else if (vramNeed <= 78.0)
                        {
                            fStatus = "heavy"; fZh = "需A100(80G)"; fEn = "A100 80G";
                        }
                        else
                        {
                            fStatus = "too_large"; fZh = "超单卡显存"; fEn = ">80G VRAM";
                        }

                        weightFiles.Add(new HFModelWeightFileItem
                        {
                            Filename = fname,
                            SizeGb = sizeGb,
                            FitStatus = fStatus,
                            FitZh = fZh,
                            FitEn = fEn,
                        });
                        if (weightFiles.Count >= 36) break;
                    }
                }

                double fallbackGb = (!isGGUF && totalWeightFilesGb > 0) ? totalWeightFilesGb : usedStorageGb;
                var sizing = AnalyzeHFModelSizing(meta, rid, isGGUF, fallbackStorageGb: fallbackGb);

                string readmeExcerpt = "";
                foreach (string host in HfHosts)
                {
                    byte[]? rBytes = await FetchHfDirectBytesAsync($"{host}/{rid}/raw/main/README.md", tok, timeoutSeconds: 6);
                    if (rBytes is { Length: > 0 })
                    {
                        int len = Math.Min(rBytes.Length, 32768);
                        string rawMd = Encoding.UTF8.GetString(rBytes, 0, len);
                        readmeExcerpt = CleanHFReadmeExcerpt(rawMd);
                        if (!string.IsNullOrEmpty(readmeExcerpt)) break;
                    }
                }

                string[] ridParts = rid.Split('/');
                string shortName = ridParts.Length > 0 ? ridParts[^1] : rid;
                string ownerName = GetJsonString(meta, "author");
                if (string.IsNullOrEmpty(ownerName))
                {
                    ownerName = ridParts.Length > 0 ? ridParts[0] : "";
                }
                string lastModRaw = GetJsonString(meta, "lastModified");
                string lastMod = lastModRaw.Length > 10 ? lastModRaw[..10] : lastModRaw;

                ActiveHFModelDetail = new HFModelDetailInfo
                {
                    Ok = true,
                    RepoId = rid,
                    Name = shortName,
                    Author = ownerName,
                    PipelineTag = string.IsNullOrEmpty(ptag) ? (isGGUF ? "gguf" : "text-generation") : ptag,
                    Modality = modality,
                    IsGguf = isGGUF,
                    IsGated = isGated,
                    License = lic,
                    BaseModel = baseModel,
                    Architecture = archStr,
                    ContextLength = ctxLen,
                    Languages = langs,
                    Likes = GetJsonInt64(meta, "likes"),
                    Downloads = GetJsonInt64(meta, "downloads"),
                    LastModified = lastMod,
                    UsedStorageGb = usedStorageGb,
                    ParamCount = sizing.ParamCount,
                    ParamLabel = sizing.ParamLabel,
                    EstVramGb = sizing.EstVramGb,
                    DeployStatus = sizing.DeployStatus,
                    DeployLabelZh = sizing.DeployZh,
                    DeployLabelEn = sizing.DeployEn,
                    WeightFiles = weightFiles,
                    ReadmeExcerpt = readmeExcerpt,
                };
            }
            catch { }
            finally
            {
                metaDoc?.Dispose();
            }
        }
        finally
        {
            IsLoadingHFModelDetail = false;
        }
    }

    public async Task RegisterCustomHFModelAsync(bool autoDownload = false)
    {
        string repo = NormalizeHFRepoId(CustomRepoInput);
        string[] repoParts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!repo.Contains('/') || repoParts.Length != 2)
        {
            CustomModelErrorMessage = L(
                "请输入合法的 HuggingFace 仓库地址（格式：作者/仓库名，如 unsloth/Qwen3.8-27B-GGUF）",
                "Please enter a valid HuggingFace Repo ID (format: owner/repo, e.g. unsloth/Qwen3.8-27B-GGUF)"
            );
            return;
        }

        CustomModelErrorMessage = "";
        IsRegisteringCustomModel = true;
        try
        {
            string inc = (CustomIncludePatternInput ?? string.Empty).Trim();
            string customName = (CustomDisplayNameInput ?? string.Empty).Trim();
            string modPref = CustomModalityInput;

            if (string.Equals(repo, "minimaxai/minimax-h3-image", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(inc))
            {
                var builtinSpec = OPEN_MODEL_CATALOG.FirstOrDefault(m => m.Id == "minimax_h3_image");
                if (builtinSpec is not null)
                {
                    RemoveCustomModelsWhereInternal(m =>
                        string.Equals(m.RepoId, "minimaxai/minimax-h3-image", StringComparison.OrdinalIgnoreCase));
                    SavePersistedCustomModels();
                    SwitchSelectedModel(builtinSpec);
                    ShowAddCustomModelSheet = false;
                    CustomRepoInput = "";
                    CustomIncludePatternInput = "";
                    CustomDisplayNameInput = "";
                    StatusMessage = L(
                        "已切换至内置专属引擎「MiniMax-H3-Image (33B · 4K)」（支持 6 步原生 4K ModularPipeline 与编码器零拷贝共享）！",
                        "Switched to built-in dedicated engine 'MiniMax-H3-Image (33B · 4K)' (6-step native 4K ModularPipeline + shared encoder)!"
                    );
                    if (autoDownload && IsConnected)
                    {
                        await StartRemoteDownloadWeightsAsync();
                    }
                    return;
                }
            }

            string baseUrl = NormalizedBaseURL;
            if (IsConnected && !string.IsNullOrEmpty(baseUrl))
            {
                try
                {
                    string body = JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        ["repo_id"] = repo,
                        ["include_pattern"] = inc,
                        ["custom_name"] = customName,
                        ["display_name"] = customName,
                        ["modality"] = modPref,
                        ["modality_hint"] = modPref,
                        ["hf_token"] = (HfToken ?? string.Empty).Trim(),
                    }, ModelJson.Options);

                    var (_, respBody) = await SendAdminRequestAsync(
                        $"{baseUrl}/minimax_api/custom_model/register",
                        HttpMethod.Post,
                        body,
                        timeoutSeconds: 35);
                    var decoded = ModelJson.Decode<ColabStatusResponse>(respBody);
                    if (decoded is not null)
                    {
                        if (decoded.ActionOk == false)
                        {
                            string srvMsg = decoded.ActionMessage ?? "";
                            if (!srvMsg.Contains("all_rfilenames", StringComparison.Ordinal))
                            {
                                CustomModelErrorMessage = string.IsNullOrEmpty(srvMsg)
                                    ? L("无法解析该 HuggingFace 仓库", "Failed to resolve HuggingFace repository")
                                    : srvMsg;
                                return;
                            }
                        }
                        else
                        {
                            ApplyStatusResponse(decoded);
                            if (decoded.RegisteredModel is { } reg)
                            {
                                var regSpec = OpenModelSpec.FromCustomDTO(reg);
                                UpsertCustomModelInternal(regSpec);
                                SavePersistedCustomModels();
                                SwitchSelectedModel(regSpec);
                            }
                            if (!string.IsNullOrEmpty(decoded.ActionMessage))
                            {
                                StatusMessage = decoded.ActionMessage;
                            }
                            ShowAddCustomModelSheet = false;
                            CustomRepoInput = "";
                            CustomIncludePatternInput = "";
                            CustomDisplayNameInput = "";
                            if (autoDownload)
                            {
                                await StartRemoteDownloadWeightsAsync();
                            }
                            return;
                        }
                    }
                }
                catch
                {
                    // 若远端旧版或网络波动，回退为本地先注册
                }
            }

            string slugSource = (repo + (string.IsNullOrEmpty(inc) ? "" : $"_{inc}")).ToLowerInvariant();
            string slugBase = SlugSanitizeRegex.Replace(slugSource, "_").Trim('_');
            string modelId = "custom_" + (slugBase.Length > 40 ? slugBase[..40] : slugBase);

            string resolvedMod;
            if (modPref == "auto")
            {
                string lower = (repo + " " + inc).ToLowerInvariant();
                if (lower.Contains("flux", StringComparison.Ordinal)
                    || lower.Contains("image", StringComparison.Ordinal)
                    || lower.Contains("sdxl", StringComparison.Ordinal)
                    || lower.Contains("z-image", StringComparison.Ordinal))
                {
                    resolvedMod = "image";
                }
                else if (lower.Contains("video", StringComparison.Ordinal)
                         || lower.Contains("wan", StringComparison.Ordinal)
                         || lower.Contains("ltx", StringComparison.Ordinal)
                         || lower.Contains("minimax", StringComparison.Ordinal)
                         || lower.Contains("hunyuan", StringComparison.Ordinal))
                {
                    resolvedMod = "video_audio";
                }
                else
                {
                    resolvedMod = "llm_vlm";
                }
            }
            else
            {
                resolvedMod = modPref;
            }

            string repoShort = repoParts[^1];
            string finalName = string.IsNullOrEmpty(customName)
                ? (string.IsNullOrEmpty(inc) ? repoShort : $"{repoShort} ({inc})")
                : customName;
            string archGuess = (repo.Contains("gguf", StringComparison.OrdinalIgnoreCase)
                                || inc.Contains(".gguf", StringComparison.OrdinalIgnoreCase))
                ? "gguf"
                : (resolvedMod == "image" ? "diffusers_image" : "transformers");

            var dto = new CustomHFModelDTO
            {
                Id = modelId,
                Name = finalName,
                RepoId = repo,
                IncludePattern = inc,
                Modality = resolvedMod,
                PipelineArch = archGuess,
                Category = $"自定义 HuggingFace 仓库 ({repo})",
                ApproxSizeGb = 0.0,
                IsCustom = true,
            };
            var spec = OpenModelSpec.FromCustomDTO(dto);
            UpsertCustomModelInternal(spec);
            SavePersistedCustomModels();
            SwitchSelectedModel(spec);
            ShowAddCustomModelSheet = false;
            CustomRepoInput = "";
            CustomIncludePatternInput = "";
            CustomDisplayNameInput = "";
            StatusMessage = L(
                $"已添加自定义模型「{finalName}」({repo})，连接 Colab 后即可并发拉取至 Google Drive。",
                $"Added custom model '{finalName}' ({repo}). Connect to Colab to download weights to Google Drive."
            );
        }
        finally
        {
            IsRegisteringCustomModel = false;
        }
    }

    public async Task RemoveCustomHFModelAsync(OpenModelSpec spec)
    {
        if (!spec.IsCustom) return;
        RemoveCustomModelsWhereInternal(m => m.Id == spec.Id);
        SavePersistedCustomModels();
        if (SelectedModelId == spec.Id)
        {
            SwitchSelectedModel(OPEN_MODEL_CATALOG[0]);
        }

        string baseUrl = NormalizedBaseURL;
        if (!IsConnected || string.IsNullOrEmpty(baseUrl)) return;

        try
        {
            string body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["model_id"] = spec.Id,
                ["delete_weights"] = false,
            }, ModelJson.Options);
            var (_, respBody) = await SendAdminRequestAsync(
                $"{baseUrl}/minimax_api/custom_model/remove",
                HttpMethod.Post,
                body,
                timeoutSeconds: 20);
            var decoded = ModelJson.Decode<ColabStatusResponse>(respBody);
            if (decoded is not null)
            {
                ApplyStatusResponse(decoded);
            }
        }
        catch { }
    }
}
