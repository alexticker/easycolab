using Microsoft.UI.Xaml.Markup;

namespace EasyColab.Localization;

/// <summary>
/// XAML markup extension for static bilingual strings:
/// <code>&lt;TextBlock Text="{loc:L Zh='连接', En='Connect'}" /&gt;</code>
/// (xmlns:loc="using:EasyColab.Localization").
/// Values are resolved when the element is created. When the language changes, the main window rebuilds its
/// root page (all state lives in the view model, so nothing is lost) — see MainWindow.RebuildRootPage().
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class L : MarkupExtension
{
    public string Zh { get; set; } = string.Empty;
    public string En { get; set; } = string.Empty;

    protected override object ProvideValue() => Loc.L(Zh, En);
}
