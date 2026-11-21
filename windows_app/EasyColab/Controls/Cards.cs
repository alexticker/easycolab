using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using WinColor = Windows.UI.Color;

namespace EasyColab.Controls;

/// <summary>
/// Port of Swift <c>SidebarCard(title:icon:badge:content:)</c>: 13 pt padding, 10 pt radius, sidebar card fill,
/// 0.8 pt card border, soft shadow (black 22 % dark / 4 % light, radius 3, y 1); header row = accent icon (12 pt semibold)
/// + bold 12.5 pt title + optional capsule badge (10.5 pt, secondary, primary@6 % fill); 11 pt gap to content.
/// Template: implicit style in Themes/StudioTheme.xaml.
/// </summary>
[ContentProperty(Name = nameof(Content))]
public sealed partial class SidebarCard : ContentControl
{
    private SoftShadow? _shadow;
    private FrameworkElement? _badge;

    public SidebarCard()
    {
        DefaultStyleKey = typeof(SidebarCard);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SidebarCard), new PropertyMetadata(string.Empty));

    /// <summary>Segoe Fluent Icons glyph (Swift: SF Symbol name), e.g. <c>Icon="&amp;#xE713;"</c>.</summary>
    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(SidebarCard), new PropertyMetadata(string.Empty));

    /// <summary>Optional badge text (Swift <c>badge: String? = nil</c>); hidden when null/empty.</summary>
    public string? Badge
    {
        get => (string?)GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    public static readonly DependencyProperty BadgeProperty = DependencyProperty.Register(
        nameof(Badge), typeof(string), typeof(SidebarCard),
        new PropertyMetadata(null, (d, _) => ((SidebarCard)d).UpdateBadge()));

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _badge = GetTemplateChild("BadgeCapsule") as FrameworkElement;
        UpdateBadge();
        if (GetTemplateChild("ShadowHost") is FrameworkElement host)
        {
            _shadow = new SoftShadow(host, ellipse: false, cornerRadius: 10, radius: 3, offsetY: 1,
                shapeFill: t => StudioColors.ResolveThemeColor("StudioSidebarCardFillBrush", t,
                    t == ElementTheme.Dark ? WinColor.FromArgb(0xFF, 0x28, 0x2B, 0x33) : WinColor.FromArgb(0xFF, 0xFB, 0xFC, 0xFF)),
                shadowColor: t => (Microsoft.UI.Colors.Black, t == ElementTheme.Dark ? 0.22f : 0.04f));
        }
    }

    private void UpdateBadge()
    {
        if (_badge is not null)
            _badge.Visibility = string.IsNullOrEmpty(Badge) ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>
/// Port of Swift <c>WorkspaceCard(title:subtitle:icon:trailing:content:)</c>: 16 pt padding, 11 pt radius, workspace card
/// fill, 0.8 pt border, soft shadow (black 25 % dark / 3.5 % light, radius 4, y 1.5); header = optional accent icon
/// (13 pt semibold) + title (headline 13 pt semibold) / optional caption subtitle + optional trailing view; 12 pt gap.
/// </summary>
[ContentProperty(Name = nameof(Content))]
public sealed partial class WorkspaceCard : ContentControl
{
    private SoftShadow? _shadow;
    private FrameworkElement? _icon;
    private FrameworkElement? _subtitle;
    private FrameworkElement? _trailing;

    public WorkspaceCard()
    {
        DefaultStyleKey = typeof(WorkspaceCard);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(WorkspaceCard), new PropertyMetadata(string.Empty));

    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(WorkspaceCard),
        new PropertyMetadata(null, (d, _) => ((WorkspaceCard)d).UpdateParts()));

    /// <summary>Optional Segoe Fluent Icons glyph (Swift: SF Symbol name).</summary>
    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(WorkspaceCard),
        new PropertyMetadata(null, (d, _) => ((WorkspaceCard)d).UpdateParts()));

    /// <summary>Optional trailing header content (Swift <c>trailing: AnyView?</c>).</summary>
    public object? Trailing
    {
        get => GetValue(TrailingProperty);
        set => SetValue(TrailingProperty, value);
    }

    public static readonly DependencyProperty TrailingProperty = DependencyProperty.Register(
        nameof(Trailing), typeof(object), typeof(WorkspaceCard),
        new PropertyMetadata(null, (d, _) => ((WorkspaceCard)d).UpdateParts()));

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _icon = GetTemplateChild("IconGlyph") as FrameworkElement;
        _subtitle = GetTemplateChild("SubtitleText") as FrameworkElement;
        _trailing = GetTemplateChild("TrailingPresenter") as FrameworkElement;
        UpdateParts();
        if (GetTemplateChild("ShadowHost") is FrameworkElement host)
        {
            _shadow = new SoftShadow(host, ellipse: false, cornerRadius: 11, radius: 4, offsetY: 1.5,
                shapeFill: t => StudioColors.ResolveThemeColor("StudioWorkspaceCardFillBrush", t,
                    t == ElementTheme.Dark ? WinColor.FromArgb(0xFF, 0x20, 0x22, 0x29) : WinColor.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                shadowColor: t => (Microsoft.UI.Colors.Black, t == ElementTheme.Dark ? 0.25f : 0.035f));
        }
    }

    private void UpdateParts()
    {
        if (_icon is not null)
            _icon.Visibility = string.IsNullOrEmpty(Icon) ? Visibility.Collapsed : Visibility.Visible;
        if (_subtitle is not null)
            _subtitle.Visibility = string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
        if (_trailing is not null)
            _trailing.Visibility = Trailing is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
