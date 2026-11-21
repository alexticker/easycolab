using System.ComponentModel;
using EasyColab.Services;

namespace EasyColab.Localization;

/// <summary>Port of macOS <c>AppLanguage</c>.</summary>
public enum AppLanguage
{
    Zh,
    En,
}

/// <summary>
/// Port of macOS <c>LanguageManager</c> (LocalizationAndText.swift). Default language is Simplified Chinese.
/// Persisted under the settings key <c>appLanguage</c> ("zh" / "en"), shared with previous Windows releases.
/// </summary>
public sealed class LanguageManager : INotifyPropertyChanged
{
    public static LanguageManager Shared { get; } = new();

    private AppLanguage _lang;

    private LanguageManager()
    {
        _lang = SettingsStore.Shared.GetString("appLanguage") == "en" ? AppLanguage.En : AppLanguage.Zh;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised (on the UI thread) after the language actually changes.</summary>
    public event EventHandler? LanguageChanged;

    public AppLanguage Lang
    {
        get => _lang;
        set
        {
            if (_lang == value) return;
            _lang = value;
            SettingsStore.Shared.Set("appLanguage", value == AppLanguage.En ? "en" : "zh");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lang)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEN)));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsEN => _lang == AppLanguage.En;

    public void Toggle() => Lang = _lang == AppLanguage.Zh ? AppLanguage.En : AppLanguage.Zh;
}

/// <summary>
/// Global <c>L(zh, en)</c> helper, identical semantics to the Swift <c>L(_:_:)</c> function.
/// Available everywhere via <c>global using static EasyColab.Localization.Loc;</c>.
/// </summary>
public static class Loc
{
    public static string L(string zh, string en) => LanguageManager.Shared.IsEN ? en : zh;
}
