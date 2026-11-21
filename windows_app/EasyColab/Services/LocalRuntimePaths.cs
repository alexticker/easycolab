using System.Diagnostics;

namespace EasyColab.Services;

/// <summary>
/// Windows port of macOS <c>LocalRuntimePaths</c> (ModelsAndCatalogs.swift) plus the Windows-specific
/// path helpers of the previous PySide6 client (models.py / viewmodel_services.py).
///
/// Release layout (dist\EasyColab\):
///   EasyColab.exe, colab_bridge.py, app.py, runtime_auth.py, EasyColab_Server.ipynb, VERSION ...
///   runtime\python.exe        (official Python embeddable distro, python3xx._pth includes ..\site-packages)
///   site-packages\            (google-colab-cli, google-auth, requests, websocket-client ...)
/// Dev layout: repo checkout; EasyColab.exe runs from windows_app\EasyColab\bin\...; the repo root is found by
/// walking up until a folder containing both app.py and colab_bridge.py is found.
/// </summary>
public static class LocalRuntimePaths
{
    /// <summary>Directory containing EasyColab.exe.</summary>
    public static string AppBaseDir => AppContext.BaseDirectory;

    private static readonly Lazy<string?> _repoRoot = new(() =>
    {
        static bool IsRepo(string dir) =>
            File.Exists(Path.Combine(dir, "app.py")) && File.Exists(Path.Combine(dir, "colab_bridge.py"))
            && Directory.Exists(Path.Combine(dir, "windows_app"));

        foreach (var start in new[] { AppBaseDir, Environment.CurrentDirectory })
        {
            var d = new DirectoryInfo(start);
            for (int i = 0; d is not null && i < 10; i++, d = d.Parent)
            {
                if (IsRepo(d.FullName)) return d.FullName;
            }
        }
        return null;
    });

    /// <summary>Repository root when running from a source checkout (dev hot-reload of bridge scripts), else null.</summary>
    public static string? RepoRootIfPresent => _repoRoot.Value;

    /// <summary>Prefer repo file (dev), fall back to the copy shipped next to EasyColab.exe.</summary>
    public static string ResolveBundledOrRepoFile(string filename)
    {
        if (RepoRootIfPresent is { } repo)
        {
            var p = Path.Combine(repo, filename);
            if (File.Exists(p)) return p;
        }
        var b = Path.Combine(AppBaseDir, filename);
        if (File.Exists(b)) return b;
        return filename;
    }

    /// <summary>%LOCALAPPDATA%\EasyColab (macOS: ~/Library/Application Support/EasyColab). Created on demand.</summary>
    public static string AppConfigDir
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
            var dir = Path.Combine(baseDir, "EasyColab");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Swift <c>appSupportDir</c> alias.</summary>
    public static string AppSupportDir => AppConfigDir;

    public static string ManagedVenvDir => Path.Combine(AppConfigDir, "venv");

    /// <summary>Local media output: %USERPROFILE%\Videos\EasyColab (macOS ~/Movies/EasyColab).</summary>
    public static string LocalSaveDir
    {
        get
        {
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(videos))
                videos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
            var dir = Path.Combine(videos, "EasyColab");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Current save dir followed by legacy product folders that still exist (Aevix, Colab_AI_Studio, MiniMax_H3_Videos).</summary>
    public static IReadOnlyList<string> LegacyMediaDirs
    {
        get
        {
            var save = LocalSaveDir;
            var list = new List<string> { save };
            var parent = Path.GetDirectoryName(save)!;
            foreach (var name in new[] { "Aevix", "Colab_AI_Studio", "MiniMax_H3_Videos" })
            {
                var d = Path.Combine(parent, name);
                if (Directory.Exists(d)) list.Add(d);
            }
            return list;
        }
    }

    /// <summary>Bundled embeddable runtime (release builds): &lt;exe dir&gt;\runtime\python.exe.</summary>
    public static string? FindEmbeddedPython()
    {
        var p = Path.Combine(AppBaseDir, "runtime", "python.exe");
        return File.Exists(p) ? p : null;
    }

    /// <summary>
    /// Returns a Python interpreter that can already import the bridge deps
    /// (<c>import colab_cli, google.auth, websocket</c>), or null.
    /// Order: embedded runtime → repo .venv → managed venv (%LOCALAPPDATA%\EasyColab\venv)
    /// → uv/pipx google-colab-cli tool venvs → system Python.
    /// </summary>
    public static string? FindReadyBridgePython()
    {
        if (FindEmbeddedPython() is { } emb) return emb; // shipped with all deps; skip probe for fast startup

        var candidates = new List<string>();
        if (RepoRootIfPresent is { } repo)
        {
            candidates.Add(Path.Combine(repo, ".venv", "Scripts", "python.exe"));
            candidates.Add(Path.Combine(repo, "venv", "Scripts", "python.exe"));
            candidates.Add(Path.Combine(repo, ".venv_win", "Scripts", "python.exe"));
        }
        candidates.Add(Path.Combine(ManagedVenvDir, "Scripts", "python.exe"));

        // Reuse existing uv / pipx google-colab-cli installations when present
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var uvToolDir = Environment.GetEnvironmentVariable("UV_TOOL_DIR");
        if (!string.IsNullOrWhiteSpace(uvToolDir))
            candidates.Add(Path.Combine(uvToolDir, "google-colab-cli", "Scripts", "python.exe"));
        if (!string.IsNullOrWhiteSpace(appData))
            candidates.Add(Path.Combine(appData, "uv", "tools", "google-colab-cli", "Scripts", "python.exe"));
        if (!string.IsNullOrWhiteSpace(localAppData))
            candidates.Add(Path.Combine(localAppData, "uv", "tools", "google-colab-cli", "Scripts", "python.exe"));
        if (!string.IsNullOrWhiteSpace(userProfile))
            candidates.Add(Path.Combine(userProfile, ".local", "pipx", "venvs", "google-colab-cli", "Scripts", "python.exe"));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var py in candidates)
        {
            if (string.IsNullOrWhiteSpace(py) || !seen.Add(py) || !File.Exists(py)) continue;
            if (ProbeImports(py)) return py;
        }
        return null;
    }

    private static bool ProbeImports(string python)
    {
        try
        {
            var psi = new ProcessStartInfo(python)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import colab_cli, google.auth, websocket");
            using var p = Process.Start(psi);
            if (p is null) return false;
            if (!p.WaitForExit(15000)) { try { p.Kill(true); } catch { } return false; }
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// System Python 3 used to create the managed venv on first run (dev / no embedded runtime).
    /// Order: <c>py -3</c> launcher → python.exe on PATH (ignoring the Microsoft Store WindowsApps stub).
    /// Returns the full command prefix (e.g. ["C:\Windows\py.exe", "-3"]) or null.
    /// </summary>
    public static string[]? FindSystemPython3()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string? Which(string exe, bool allowWindowsApps = false)
        {
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var full = Path.Combine(dir.Trim(), exe);
                    if (File.Exists(full) && (allowWindowsApps || !full.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))) return full;
                }
                catch { }
            }
            return null;
        }

        // Windows 11's official Python Install Manager places the real py.exe launcher inside WindowsApps.
        if (Which("py.exe", allowWindowsApps: true) is { } py) return new[] { py, "-3" };
        if (Which("python.exe") is { } python) return new[] { python };
        if (Which("python3.exe") is { } python3) return new[] { python3 };
        return null;
    }

    /// <summary>Open a file / folder in Explorer; <paramref name="reveal"/> selects the file in its folder.</summary>
    public static void OpenInExplorer(string path, bool reveal = false)
    {
        try
        {
            if (reveal && File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>Open a URL in the default browser (NSWorkspace.shared.open equivalent).</summary>
    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
