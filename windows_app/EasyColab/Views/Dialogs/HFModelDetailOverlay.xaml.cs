using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace EasyColab.Views.Dialogs;

public sealed partial class HFModelDetailOverlay : UserControl
{
    public StudioViewModel ViewModel => StudioViewModel.Shared;

    private bool _subscribed;

    public HFModelDetailOverlay()
    {
        InitializeComponent();

        Subscribe();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => RenderCurrentState();

        RenderCurrentState();
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        LanguageManager.Shared.LanguageChanged += OnLanguageChanged;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        LanguageManager.Shared.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Subscribe();
        RenderCurrentState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unsubscribe();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UiDispatcher.Run(RenderCurrentState);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var prop = e.PropertyName;
        if (string.IsNullOrEmpty(prop)
            || prop == nameof(StudioViewModel.ShowHFModelDetailOverlay)
            || prop == nameof(StudioViewModel.IsLoadingHFModelDetail)
            || prop == nameof(StudioViewModel.ActiveHFModelDetail)
            || prop == nameof(StudioViewModel.CustomIncludePatternInput))
        {
            RenderCurrentState();
        }
    }

    private void RenderCurrentState()
    {
        bool show = ViewModel.ShowHFModelDetailOverlay;
        Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        if (!show)
        {
            LoadingSpinner.IsActive = false;
            return;
        }

        if (ViewModel.IsLoadingHFModelDetail)
        {
            LoadingPanel.Visibility = Visibility.Visible;
            LoadingSpinner.IsActive = true;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            DetailRootGrid.Visibility = Visibility.Collapsed;
            return;
        }

        LoadingPanel.Visibility = Visibility.Collapsed;
        LoadingSpinner.IsActive = false;

        if (ViewModel.ActiveHFModelDetail is not { } d)
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
            DetailRootGrid.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyStatePanel.Visibility = Visibility.Collapsed;
        DetailRootGrid.Visibility = Visibility.Visible;

        // Header
        HeaderGatedLockIcon.Visibility = d.IsGated ? Visibility.Visible : Visibility.Collapsed;
        HeaderModelNameText.Text = d.Name;
        ToolTipService.SetToolTip(HeaderModelNameText, d.Name);

        bool hasParam = !string.IsNullOrEmpty(d.ParamLabel);
        HeaderParamBadge.Visibility = hasParam ? Visibility.Visible : Visibility.Collapsed;
        HeaderParamText.Text = d.ParamLabel;

        var deployBrush = d.DeployBadgeBrush;
        HeaderDeployBadge.Background = d.DeployBadgeFillBrush;
        HeaderDeployDot.Fill = deployBrush;
        HeaderDeployText.Foreground = deployBrush;
        HeaderDeployText.Text = d.LocalizedDeployLabel;

        HeaderRepoIdText.Text = d.RepoId;
        HeaderLikesText.Text = d.Likes.ToString();
        HeaderDownloadsText.Text = d.Downloads.ToString();
        HeaderUpdatedText.Text = $"{L("更新于", "Updated")} {(string.IsNullOrEmpty(d.LastModified) ? "-" : d.LastModified)}";

        // 4 Stat Boxes
        string paramStr = string.IsNullOrEmpty(d.ParamLabel) ? L("未标注", "N/A") : d.ParamLabel;
        string storageStr = d.UsedStorageGb > 0 ? $"{CFormatFixed(d.UsedStorageGb, 1)} GB" : "-";
        StatParamsText.Text = $"{paramStr} ({storageStr})";
        ToolTipService.SetToolTip(StatParamsText, StatParamsText.Text);

        string archBase = string.IsNullOrEmpty(d.Architecture) ? d.PipelineTag : d.Architecture;
        string ctxSuffix = d.ContextLength > 0 ? $" · {d.ContextLength / 1024}K" : string.Empty;
        StatArchText.Text = $"{archBase}{ctxSuffix}";
        ToolTipService.SetToolTip(StatArchText, StatArchText.Text);

        StatLicenseLockIcon.Visibility = d.IsGated ? Visibility.Visible : Visibility.Collapsed;
        StatLicenseText.Text = StripEmojiCharacters(d.License);
        ToolTipService.SetToolTip(StatLicenseText, StatLicenseText.Text);

        string baseLabel;
        if (string.IsNullOrEmpty(d.BaseModel))
        {
            baseLabel = L("原生基座", "Base");
        }
        else
        {
            var parts = d.BaseModel.Split('/', StringSplitOptions.RemoveEmptyEntries);
            baseLabel = parts.Length > 0 ? parts[^1] : d.BaseModel;
        }
        StatBaseModelText.Text = baseLabel;
        ToolTipService.SetToolTip(StatBaseModelText, string.IsNullOrEmpty(d.BaseModel) ? baseLabel : d.BaseModel);

        // Weight & Quant Shards List
        int wfCount = d.WeightFiles.Count;
        WeightSectionTitleText.Text = L(
            $"权重与量化分片清单 (点击「选用此文件」自动填入过滤规则 · 共 {wfCount} 个文件)",
            $"Weight & Quant Shards (Click 'Select' to auto-fill Include Pattern · {wfCount} files)");

        if (wfCount == 0)
        {
            NoWeightsText.Visibility = Visibility.Visible;
            WeightFilesContainer.Visibility = Visibility.Collapsed;
            WeightFilesStack.Children.Clear();
        }
        else
        {
            NoWeightsText.Visibility = Visibility.Collapsed;
            WeightFilesContainer.Visibility = Visibility.Visible;
            RebuildWeightFilesList(d);
        }

        // Model Card README Excerpt
        bool hasReadme = !string.IsNullOrEmpty(d.ReadmeExcerpt);
        ReadmeSectionPanel.Visibility = hasReadme ? Visibility.Visible : Visibility.Collapsed;
        if (hasReadme)
        {
            ReadmeViewer.Markdown = d.ReadmeExcerpt;
        }
    }

    private void RebuildWeightFilesList(HFModelDetailInfo detail)
    {
        WeightFilesStack.Children.Clear();

        var rowFill = ResolveThemeBrush("StudioWorkspaceCardFillBrush");
        var primaryBrush = ResolveThemeBrush("StudioTextPrimaryBrush");
        var secondaryBrush = ResolveThemeBrush("StudioTextSecondaryBrush");
        var accentBrush = ResolveThemeBrush("StudioSystemAccentBrush");
        var whiteBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        var plainStyle = ResolvePlainButtonStyle();
        var monoFont = Application.Current?.Resources.TryGetValue("StudioMonoFont", out var mf) == true && mf is FontFamily ff
            ? ff
            : new FontFamily("Cascadia Mono, Consolas, Courier New");

        string currentPat = ViewModel.CustomIncludePatternInput ?? string.Empty;

        foreach (var wf in detail.WeightFiles)
        {
            bool isCurPat = string.Equals(currentPat, wf.Filename, StringComparison.Ordinal);

            var rowGrid = new Grid { ColumnSpacing = 10 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var fileNameText = new TextBlock
            {
                Text = wf.Filename,
                FontFamily = monoFont,
                FontSize = 10.5,
                Foreground = primaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ToolTipService.SetToolTip(fileNameText, wf.Filename);
            Grid.SetColumn(fileNameText, 0);
            rowGrid.Children.Add(fileNameText);

            if (wf.SizeGb > 0)
            {
                var sizeText = new TextBlock
                {
                    Text = $"{CFormatFixed(wf.SizeGb, 2)} GB",
                    FontFamily = monoFont,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = secondaryBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(sizeText, 1);
                rowGrid.Children.Add(sizeText);
            }

            var fitBrush = wf.FitBrush;
            var fitPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
            };
            fitPanel.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = fitBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            fitPanel.Children.Add(new TextBlock
            {
                Text = wf.LocalizedFit,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = fitBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(fitPanel, 2);
            rowGrid.Children.Add(fitPanel);

            var selectBtn = new Button
            {
                Style = plainStyle,
                Content = new Border
                {
                    Padding = new Thickness(9, 3.5, 9, 3.5),
                    CornerRadius = new CornerRadius(6),
                    Background = accentBrush,
                    Child = new TextBlock
                    {
                        Text = isCurPat ? L("已选用", "Selected") : L("选用此文件", "Select File"),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = whiteBrush,
                    },
                },
            };
            var capturedFile = wf;
            selectBtn.Click += (_, _) =>
            {
                ViewModel.CustomRepoInput = detail.RepoId;
                ViewModel.CustomIncludePatternInput = capturedFile.Filename;
                ViewModel.CustomDisplayNameInput = detail.Name;
                ViewModel.CustomModalityInput = detail.Modality;
                ViewModel.ShowHFModelDetailOverlay = false;
            };
            Grid.SetColumn(selectBtn, 3);
            rowGrid.Children.Add(selectBtn);

            WeightFilesStack.Children.Add(new Border
            {
                Padding = new Thickness(9, 6, 9, 6),
                CornerRadius = new CornerRadius(6),
                Background = rowFill,
                Child = rowGrid,
            });
        }
    }

    private void CloseOverlay()
    {
        ViewModel.ShowHFModelDetailOverlay = false;
    }

    private void OnCloseOverlayClick(object sender, RoutedEventArgs e) => CloseOverlay();

    private void OnScrimPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        CloseOverlay();
    }

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.ShowHFModelDetailOverlay)
        {
            CloseOverlay();
            args.Handled = true;
        }
    }

    private void OnOpenOnHfClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveHFModelDetail is { } d && !string.IsNullOrEmpty(d.RepoId))
        {
            LocalRuntimePaths.OpenUrl($"https://huggingface.co/{d.RepoId}");
        }
    }

    private void OnUseModelClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveHFModelDetail is not { } d)
        {
            CloseOverlay();
            return;
        }

        ViewModel.CustomRepoInput = d.RepoId;
        if (string.IsNullOrEmpty(ViewModel.CustomDisplayNameInput))
        {
            ViewModel.CustomDisplayNameInput = d.Name;
        }
        ViewModel.CustomModalityInput = d.Modality;
        ViewModel.ShowHFModelDetailOverlay = false;
    }

    private Style? ResolvePlainButtonStyle()
    {
        if (Application.Current?.Resources.TryGetValue("StudioPlainButtonStyle", out var s) == true && s is Style style)
            return style;
        return null;
    }

    private Brush ResolveThemeBrush(string key)
    {
        string themeKey = ActualTheme == ElementTheme.Light ? "Light" : "Dark";
        var root = Application.Current?.Resources;
        if (root is not null && FindThemeResource(root, themeKey, key) is Brush themed)
            return themed;
        if (root is not null && root.TryGetValue(key, out var fallback) && fallback is Brush fb)
            return fb;
        return new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA));
    }

    private static object? FindThemeResource(ResourceDictionary dict, string themeKey, string key)
    {
        if (dict.ThemeDictionaries.TryGetValue(themeKey, out var td)
            && td is ResourceDictionary themed
            && themed.TryGetValue(key, out var val))
        {
            return val;
        }

        foreach (var merged in dict.MergedDictionaries)
        {
            if (FindThemeResource(merged, themeKey, key) is { } found)
                return found;
        }

        return null;
    }
}
