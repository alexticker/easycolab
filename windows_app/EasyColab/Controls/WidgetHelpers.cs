using System.Globalization;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using WinColor = Windows.UI.Color;

namespace EasyColab.Controls;

/// <summary>
/// Port of the free function <c>fmt(_:_:)</c> from TelemetryAndWidgets.swift
/// (<c>String(format: "%.\(digits)f", v)</c>, always invariant culture like Swift's POSIX formatting).
/// Usable from x:Bind: <c>{x:Bind controls:WidgetFormat.Fmt(ViewModel.RamUsedGB, 1), Mode=OneWay}</c>.
/// </summary>
public static class WidgetFormat
{
    public static string Fmt(double v, int digits = 2) =>
        v.ToString("F" + Math.Max(0, digits).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary><c>String(format: "%.1f", v)</c> (private <c>fmt1</c> helper of the chart cards).</summary>
    public static string Fmt1(double v) => Fmt(v, 1);

    /// <summary><c>String(format: "%.0f%%", fraction * 100)</c>.</summary>
    public static string Percent0(double fraction) => Fmt(fraction * 100.0, 0) + "%";
}

/// <summary>Port of Swift <c>extension Comparable { func clamped(to:) }</c>.</summary>
public static class ComparableExtensions
{
    /// <summary><c>value.clamped(to: lower...upper)</c> → <c>value.Clamped(lower, upper)</c>.</summary>
    public static T Clamped<T>(this T value, T lower, T upper) where T : IComparable<T>
    {
        if (value.CompareTo(lower) < 0) return lower;
        if (value.CompareTo(upper) > 0) return upper;
        return value;
    }
}

/// <summary>
/// SwiftUI colours used by the widgets, resolved per element theme (SwiftUI system colours are appearance-dependent).
/// </summary>
public static class StudioColors
{
    /// <summary>SwiftUI <c>Color.red</c>.</summary>
    public static WinColor SystemRed(ElementTheme theme) =>
        theme == ElementTheme.Dark ? WinColor.FromArgb(0xFF, 0xFF, 0x45, 0x3A) : WinColor.FromArgb(0xFF, 0xFF, 0x3B, 0x30);

    /// <summary>SwiftUI <c>Color.orange</c>.</summary>
    public static WinColor SystemOrange(ElementTheme theme) =>
        theme == ElementTheme.Dark ? WinColor.FromArgb(0xFF, 0xFF, 0x9F, 0x0A) : WinColor.FromArgb(0xFF, 0xFF, 0x95, 0x00);

    /// <summary>SwiftUI <c>Color.green</c>.</summary>
    public static WinColor SystemGreen(ElementTheme theme) =>
        theme == ElementTheme.Dark ? WinColor.FromArgb(0xFF, 0x30, 0xD1, 0x58) : WinColor.FromArgb(0xFF, 0x34, 0xC7, 0x59);

    /// <summary>Windows 11 Fluent 2 system accent colour (follows user's OS accent colour).</summary>
    public static WinColor SystemAccent(ElementTheme theme)
    {
        try
        {
            if (Application.Current?.Resources is { } res)
            {
                string key = theme == ElementTheme.Dark ? "SystemAccentColorLight2" : "SystemAccentColorDark1";
                if (res.TryGetValue(key, out var val) && val is WinColor c) return c;
                if (res.TryGetValue("SystemAccentColor", out var baseVal) && baseVal is WinColor bc) return bc;
            }
        }
        catch { }
        return theme == ElementTheme.Dark ? WinColor.FromArgb(0xFF, 0x60, 0xCD, 0xFF) : WinColor.FromArgb(0xFF, 0x00, 0x67, 0xC0);
    }

    /// <summary>Swift telemetry colours used by the main view (<c>Color(red:green:blue:)</c>).</summary>
    public static readonly WinColor VramBlue = FromRgb(0.33, 0.53, 0.98);
    public static readonly WinColor RamGreen = FromRgb(0.10, 0.76, 0.53);
    public static readonly WinColor DiskCyan = FromRgb(0.05, 0.73, 0.88);

    public static WinColor FromRgb(double r, double g, double b, double opacity = 1.0)
    {
        static byte C(double v) => (byte)Math.Clamp((int)Math.Round(v * 255.0, MidpointRounding.AwayFromZero), 0, 255);
        return WinColor.FromArgb(C(opacity), C(r), C(g), C(b));
    }

    /// <summary>SwiftUI <c>.opacity(x)</c>.</summary>
    public static WinColor WithOpacity(WinColor c, double opacity) =>
        WinColor.FromArgb((byte)Math.Clamp((int)Math.Round(c.A * opacity, MidpointRounding.AwayFromZero), 0, 255), c.R, c.G, c.B);

    /// <summary>
    /// Resolve a theme-dictionary <see cref="SolidColorBrush"/> (e.g. <c>StudioWorkspaceCardFillBrush</c>) for a given element
    /// theme. Needed where a palette colour is combined with an extra opacity in code (<c>palette.x.opacity(0.94)</c>).
    /// </summary>
    public static WinColor ResolveThemeColor(string key, ElementTheme theme, WinColor fallback)
    {
        try
        {
            var themeKey = theme == ElementTheme.Dark ? "Dark" : "Light";
            if (Application.Current?.Resources is { } root && TryFind(root, key, themeKey, out var c)) return c;
        }
        catch { }
        return fallback;
    }

    private static bool TryFind(ResourceDictionary dict, string key, string themeKey, out WinColor color)
    {
        if (dict.ThemeDictionaries.TryGetValue(themeKey, out var td) && td is ResourceDictionary themed
            && themed.TryGetValue(key, out var v) && v is SolidColorBrush b)
        {
            color = WithOpacity(b.Color, b.Opacity);
            return true;
        }
        foreach (var merged in dict.MergedDictionaries)
        {
            if (TryFind(merged, key, themeKey, out color)) return true;
        }
        color = default;
        return false;
    }
}

/// <summary>
/// SwiftUI <c>.shadow(color:radius:x:y:)</c> under a rounded rectangle / circle, implemented with a composition
/// <see cref="SpriteVisual"/> + <see cref="DropShadow"/> (avoiding <c>LayerVisual</c> which ignores parent DPI scale
/// in Windows App SDK 2.x).
/// </summary>
internal sealed class SoftShadow
{
    private readonly FrameworkElement _host;
    private readonly bool _ellipse;
    private readonly float _inset;
    private readonly Func<ElementTheme, (WinColor color, float opacity)> _shadowColor;
    private readonly SpriteVisual _sprite;
    private readonly DropShadow _shadow;

    /// <param name="host">Element (same size as the shape) placed directly behind the visible content.</param>
    /// <param name="radius">SwiftUI shadow radius (pt).</param>
    public SoftShadow(
        FrameworkElement host,
        bool ellipse,
        double cornerRadius,
        double radius,
        double offsetY,
        Func<ElementTheme, WinColor> shapeFill,
        Func<ElementTheme, (WinColor color, float opacity)> shadowColor)
    {
        _ = shapeFill;
        _host = host;
        _ellipse = ellipse;
        // Inset slightly so a rectangular SpriteVisual shadow never pokes past rounded corners.
        _inset = ellipse ? 1f : (float)Math.Clamp(cornerRadius * 0.25, 1.0, 3.0);
        _shadowColor = shadowColor;

        var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _sprite = compositor.CreateSpriteVisual();
        _shadow = compositor.CreateDropShadow();
        // SwiftUI radius ≈ Gaussian sigma; composition BlurRadius behaves like CSS blur (≈ 2σ).
        _shadow.BlurRadius = (float)(radius * 2.2);
        _shadow.Offset = new Vector3(0, (float)offsetY, 0);
        _sprite.Shadow = _shadow;
        ElementCompositionPreview.SetElementChildVisual(host, _sprite);

        host.SizeChanged += (_, _) => UpdateSize();
        host.ActualThemeChanged += (_, _) => UpdateColors();
        UpdateSize();
        UpdateColors();
    }

    public void UpdateColors()
    {
        var theme = _host.ActualTheme;
        var (c, o) = _shadowColor(theme);
        _shadow.Color = c;
        _shadow.Opacity = o;
    }

    private void UpdateSize()
    {
        float w = Math.Max(0f, (float)_host.ActualWidth - _inset * 2f);
        float h = Math.Max(0f, (float)_host.ActualHeight - _inset * 2f);
        _sprite.Size = new Vector2(w, h);
        _sprite.Offset = new Vector3(_inset, _inset, 0);
        if (_ellipse)
        {
            _shadow.BlurRadius = Math.Max(_shadow.BlurRadius, 6f);
        }
    }
}
