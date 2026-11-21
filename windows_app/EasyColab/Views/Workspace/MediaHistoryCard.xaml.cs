using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EasyColab.Views.Workspace;

/// <summary>
/// Port of Swift <c>mediaHistoryCard</c> in MainStudioView+Workspace.swift.
/// Displays <see cref="StudioViewModel.LocalMediaItems"/> with selection highlight, timestamp,
/// elapsed badge, click-to-select, and Explorer reveal context menu / action.
/// </summary>
public sealed partial class MediaHistoryCard : UserControl
{
    public StudioViewModel ViewModel => StudioViewModel.Shared;

    private readonly List<HistoryRowVisual> _rows = new();
    private bool _subscribed;

    public MediaHistoryCard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ViewModel.LocalMediaItems.CollectionChanged += OnLocalMediaItemsChanged;
        }
        RebuildList();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            _subscribed = false;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.LocalMediaItems.CollectionChanged -= OnLocalMediaItemsChanged;
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateSelectionHighlights();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(StudioViewModel.CurrentMediaURL):
                UpdateSelectionHighlights();
                break;

            case nameof(StudioViewModel.MediaElapsedMap):
                RebuildList();
                break;
        }
    }

    private void OnLocalMediaItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildList();
    }

    private void RebuildList()
    {
        int count = ViewModel.LocalMediaItems.Count;
        RootCard.Subtitle = L($"{count} 个媒体文件", $"{count} media files");

        bool empty = count == 0;
        EmptyStateText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        HistoryScrollViewer.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        ItemsHost.Children.Clear();
        _rows.Clear();
        if (empty) return;

        var monoFont = Application.Current.Resources.TryGetValue("StudioMonoFont", out var mf) && mf is FontFamily ff
            ? ff
            : new FontFamily("Cascadia Mono, Consolas, Courier New");
        var plainStyle = Application.Current.Resources.TryGetValue("StudioPlainButtonStyle", out var ps) && ps is Style st
            ? st
            : null;

        bool isEn = LanguageManager.Shared.IsEN;
        var culture = CultureInfo.GetCultureInfo(isEn ? "en-US" : "zh-CN");

        foreach (var item in ViewModel.LocalMediaItems)
        {
            var row = BuildRow(item, culture, monoFont, plainStyle, isEn);
            _rows.Add(row);
            ItemsHost.Children.Add(row.Container);
        }

        UpdateSelectionHighlights();
    }

    private HistoryRowVisual BuildRow(
        LocalMediaItem item,
        CultureInfo culture,
        FontFamily monoFont,
        Style? plainStyle,
        bool isEn)
    {
        var grid = new Grid { ColumnSpacing = 7 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = item.IsImage ? "\uE91B" : "\uE8B2",
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(icon);

        var textCol = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var filenameText = new TextBlock
        {
            Text = item.Filename,
            FontSize = 11.5,
            FontWeight = FontWeights.Medium,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var dateStr = isEn
            ? item.CreatedAt.ToString("MMM d, h:mm tt", culture)
            : item.CreatedAt.ToString("M月d日 HH:mm", culture);
        var dateText = new TextBlock
        {
            Text = dateStr,
            FontSize = 10,
            MaxLines = 1,
        };
        textCol.Children.Add(filenameText);
        textCol.Children.Add(dateText);
        Grid.SetColumn(textCol, 1);
        grid.Children.Add(textCol);

        Border? elapsedPill = null;
        TextBlock? elapsedText = null;
        double? el = item.ElapsedSeconds ?? ViewModel.ElapsedForMedia(item.Url);
        if (el is > 0)
        {
            elapsedText = new TextBlock
            {
                Text = FormatDuration(el.Value, compact: true),
                FontFamily = monoFont,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
            };
            elapsedPill = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(5, 2, 5, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = elapsedText,
            };
            Grid.SetColumn(elapsedPill, 2);
            grid.Children.Add(elapsedPill);
        }

        var rowBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Child = grid,
        };

        // Right-click context menu for Reveal in Explorer & Open
        var flyout = new MenuFlyout();
        var revealItem = new MenuFlyoutItem
        {
            Text = L("在资源管理器中显示", "Show in Explorer"),
            Icon = new FontIcon { Glyph = "\uE8B7" },
        };
        revealItem.Click += (_, _) =>
        {
            ViewModel.RecordLocalInteraction();
            LocalRuntimePaths.OpenInExplorer(item.Url, reveal: true);
        };
        var openItem = new MenuFlyoutItem
        {
            Text = L("用默认程序打开", "Open with Default App"),
            Icon = new FontIcon { Glyph = "\uE8A7" },
        };
        openItem.Click += (_, _) =>
        {
            ViewModel.RecordLocalInteraction();
            LocalRuntimePaths.OpenInExplorer(item.Url, reveal: false);
        };
        flyout.Items.Add(revealItem);
        flyout.Items.Add(openItem);

        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = rowBorder,
            ContextFlyout = flyout,
        };
        if (plainStyle is not null) btn.Style = plainStyle;
        ToolTipService.SetToolTip(btn, item.Filename);
        btn.Click += (_, _) =>
        {
            ViewModel.RecordLocalInteraction();
            ViewModel.SelectMedia(item.Url);
        };

        return new HistoryRowVisual(item.Url, btn, rowBorder, icon, filenameText, dateText, elapsedPill, elapsedText);
    }

    private void UpdateSelectionHighlights()
    {
        var theme = ActualTheme;
        var accent = EasyColab.Controls.StudioColors.SystemAccent(theme);
        var accentBrush = new SolidColorBrush(accent);
        var selectedBg = new SolidColorBrush(EasyColab.Controls.StudioColors.WithOpacity(accent, 0.15));
        var selectedBadgeBg = new SolidColorBrush(EasyColab.Controls.StudioColors.WithOpacity(accent, 0.16));
        var clearBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

        var primaryBrush = new SolidColorBrush(EasyColab.Controls.StudioColors.ResolveThemeColor(
            "StudioTextPrimaryBrush", theme, Windows.UI.Color.FromArgb(0xFF, 0xF1, 0xF5, 0xF9)));
        var secondaryBrush = new SolidColorBrush(EasyColab.Controls.StudioColors.ResolveThemeColor(
            "StudioTextSecondaryBrush", theme, Windows.UI.Color.FromArgb(0xFF, 0x94, 0xA3, 0xB8)));
        var subtleBadgeBg = new SolidColorBrush(EasyColab.Controls.StudioColors.ResolveThemeColor(
            "StudioPrimaryOpacity06Brush", theme, Windows.UI.Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)));

        var currentUrl = ViewModel.CurrentMediaURL;
        foreach (var row in _rows)
        {
            bool selected = !string.IsNullOrEmpty(currentUrl) &&
                            string.Equals(currentUrl, row.Url, StringComparison.OrdinalIgnoreCase);

            row.RowBorder.Background = selected ? selectedBg : clearBrush;
            row.Icon.Foreground = selected ? accentBrush : secondaryBrush;
            row.FilenameText.Foreground = primaryBrush;
            row.DateText.Foreground = secondaryBrush;

            if (row.ElapsedPill is not null && row.ElapsedText is not null)
            {
                row.ElapsedPill.Background = selected ? selectedBadgeBg : subtleBadgeBg;
                row.ElapsedText.Foreground = selected ? accentBrush : secondaryBrush;
            }
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RecordLocalInteraction();
        ViewModel.RefreshLocalMedia();
    }

    private sealed record HistoryRowVisual(
        string Url,
        Button Container,
        Border RowBorder,
        FontIcon Icon,
        TextBlock FilenameText,
        TextBlock DateText,
        Border? ElapsedPill,
        TextBlock? ElapsedText);
}
