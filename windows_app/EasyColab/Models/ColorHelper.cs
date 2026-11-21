using System.Globalization;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace EasyColab.Models;

/// <summary>
/// Colour helpers for model data (Swift <c>Color</c> values stored in models are mapped to <see cref="Color"/>).
/// SwiftUI system colours are mapped to their macOS (light appearance) sRGB values so they read well on both themes.
/// </summary>
public static class ColorHelper
{
    /// <summary>SwiftUI <c>.green</c> (systemGreen).</summary>
    public static readonly Color Green = Color.FromArgb(0xFF, 0x34, 0xC7, 0x59);

    /// <summary>SwiftUI <c>.orange</c> (systemOrange).</summary>
    public static readonly Color Orange = Color.FromArgb(0xFF, 0xFF, 0x95, 0x00);

    /// <summary>SwiftUI <c>.red</c> (systemRed).</summary>
    public static readonly Color Red = Color.FromArgb(0xFF, 0xFF, 0x3B, 0x30);

    /// <summary>SwiftUI <c>.blue</c> (systemBlue).</summary>
    public static readonly Color Blue = Color.FromArgb(0xFF, 0x00, 0x7A, 0xFF);

    /// <summary>SwiftUI <c>.purple</c> (systemPurple).</summary>
    public static readonly Color Purple = Color.FromArgb(0xFF, 0xAF, 0x52, 0xDE);

    /// <summary>SwiftUI <c>.secondary</c> (secondary label grey, readable on light and dark backgrounds).</summary>
    public static readonly Color Secondary = Color.FromArgb(0xFF, 0x8E, 0x8E, 0x93);

    /// <summary>Swift <c>Color(red: 0.85, green: 0.62, blue: 0.05)</c> — "a100" deploy gold.</summary>
    public static readonly Color A100Gold = FromRgb(0.85, 0.62, 0.05);

    /// <summary>Equivalent of SwiftUI <c>Color(red:green:blue:opacity:)</c> with 0…1 components.</summary>
    public static Color FromRgb(double red, double green, double blue, double opacity = 1.0)
    {
        static byte C(double v) => (byte)Math.Clamp((int)Math.Round(v * 255.0, MidpointRounding.AwayFromZero), 0, 255);
        return Color.FromArgb(C(opacity), C(red), C(green), C(blue));
    }

    /// <summary>Parse <c>#RRGGBB</c> or <c>#AARRGGBB</c> (leading '#' optional). Returns <see cref="Secondary"/> on failure.</summary>
    public static Color FromHex(string hex)
    {
        var s = (hex ?? string.Empty).Trim().TrimStart('#');
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return Secondary;
        return Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>Format as <c>#RRGGBB</c> (or <c>#AARRGGBB</c> when not fully opaque).</summary>
    public static string ToHex(Color c) =>
        c.A == 0xFF ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>SwiftUI <c>.opacity(x)</c>: multiplies the alpha channel.</summary>
    public static Color WithOpacity(Color c, double opacity) =>
        Color.FromArgb((byte)Math.Clamp((int)Math.Round(c.A * opacity, MidpointRounding.AwayFromZero), 0, 255), c.R, c.G, c.B);

    /// <summary>Create a brush (must be called on the UI thread).</summary>
    public static SolidColorBrush Brush(Color c) => new(c);

    /// <summary>Create a brush with an opacity multiplier (must be called on the UI thread).</summary>
    public static SolidColorBrush Brush(Color c, double opacity) => new(WithOpacity(c, opacity));

    /// <summary>
    /// Shared mapping used by <c>deployBadgeColor</c> / <c>fitColor</c> in Swift:
    /// great/good → green, a100 → gold, heavy → orange, too_large → red, otherwise secondary.
    /// </summary>
    public static Color DeployStatusColor(string? status)
    {
        switch (status ?? "unknown")
        {
            case "great":
            case "good":
                return Green;
            case "a100":
                return A100Gold;
            case "heavy":
                return Orange;
            case "too_large":
                return Red;
            default:
                return Secondary;
        }
    }
}
