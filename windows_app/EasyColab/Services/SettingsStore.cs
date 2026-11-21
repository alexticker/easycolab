using System.Text.Json;
using System.Text.Json.Nodes;

namespace EasyColab.Services;

/// <summary>
/// UserDefaults-equivalent persistent key/value store.
/// File: <c>%LOCALAPPDATA%\EasyColab\settings.json</c> — the SAME file and key names used by the previous
/// PySide6 Windows client and the macOS UserDefaults keys, so existing users keep their settings.
/// Thread-safe; writes are flushed asynchronously (debounced) and synchronously on <see cref="Flush"/>.
/// </summary>
public sealed class SettingsStore
{
    public static SettingsStore Shared { get; } = new();

    private readonly object _gate = new();
    private readonly JsonObject _data;
    private readonly string _path;
    private Timer? _flushTimer;

    private SettingsStore()
    {
        _path = Path.Combine(LocalRuntimePaths.AppConfigDir, "settings.json");
        _data = Load(_path);
    }

    private static JsonObject Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var node = JsonNode.Parse(File.ReadAllText(path));
                if (node is JsonObject obj) return obj;
            }
        }
        catch
        {
            // Corrupt file: start fresh (matches previous client behaviour).
        }
        return new JsonObject();
    }

    /// <summary>Equivalent of <c>UserDefaults.object(forKey:) != nil</c>.</summary>
    public bool Contains(string key)
    {
        lock (_gate) return _data.ContainsKey(key) && _data[key] is not null;
    }

    public string? GetString(string key)
    {
        lock (_gate)
        {
            var n = _data[key];
            if (n is JsonValue v && v.TryGetValue<string>(out var s)) return s;
            return n?.ToString();
        }
    }

    public double GetDouble(string key, double fallback = 0)
    {
        lock (_gate)
        {
            if (_data[key] is JsonValue v)
            {
                if (v.TryGetValue<double>(out var d)) return d;
                if (v.TryGetValue<long>(out var l)) return l;
                if (v.TryGetValue<bool>(out var b)) return b ? 1 : 0;
                if (v.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p)) return p;
            }
            return fallback;
        }
    }

    public int GetInt(string key, int fallback = 0) => Contains(key) ? (int)Math.Round(GetDouble(key, fallback)) : fallback;

    public bool GetBool(string key, bool fallback = false)
    {
        lock (_gate)
        {
            if (_data[key] is JsonValue v)
            {
                if (v.TryGetValue<bool>(out var b)) return b;
                if (v.TryGetValue<double>(out var d)) return d != 0;
                if (v.TryGetValue<string>(out var s)) return s is "1" or "true" or "True";
            }
            return fallback;
        }
    }

    /// <summary>Deserialize an arbitrary JSON value (arrays, dictionaries, DTOs).</summary>
    public T? Get<T>(string key, JsonSerializerOptions? options = null)
    {
        lock (_gate)
        {
            var n = _data[key];
            if (n is null) return default;
            try { return n.Deserialize<T>(options ?? JsonDefaults.Options); }
            catch { return default; }
        }
    }

    public void Set<T>(string key, T value, JsonSerializerOptions? options = null)
    {
        lock (_gate)
        {
            _data[key] = value is null ? null : JsonSerializer.SerializeToNode(value, options ?? JsonDefaults.Options);
            ScheduleFlush();
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            _data.Remove(key);
            ScheduleFlush();
        }
    }

    private void ScheduleFlush()
    {
        _flushTimer ??= new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        _flushTimer.Change(300, Timeout.Infinite);
    }

    public void Flush()
    {
        string json;
        lock (_gate) json = _data.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            // Best effort; next write retries.
        }
    }
}

public static class JsonDefaults
{
    /// <summary>Lenient options used for settings and bridge / server JSON (snake_case keys are mapped explicitly via [JsonPropertyName]).</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
