using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace EasyColab.Views.Dialogs;

public sealed partial class AddCustomHFModelDialog : UserControl
{
    public StudioViewModel ViewModel => StudioViewModel.Shared;

    private bool _wasVisible;
    private bool _subscribed;
    private bool _syncingFromVm;
    private string _trendingFilterText = string.Empty;

    public AddCustomHFModelDialog()
    {
        InitializeComponent();

        Subscribe();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) =>
        {
            UpdateCategoryPills();
            RebuildTrendingGrid();
        };

        SyncAllFromVm(initialSync: true);
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
        SyncAllFromVm(initialSync: false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unsubscribe();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UiDispatcher.Run(() =>
        {
            UpdateTrendingState();
            RebuildTrendingGrid();
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var prop = e.PropertyName;
        if (string.IsNullOrEmpty(prop))
        {
            SyncAllFromVm(initialSync: false);
            return;
        }

        switch (prop)
        {
            case nameof(StudioViewModel.ShowAddCustomModelSheet):
                SyncVisibilityFromVm(initialSync: false);
                break;
            case nameof(StudioViewModel.TrendingHFCategory):
                UpdateCategoryPills();
                break;
            case nameof(StudioViewModel.IsLoadingTrendingHF):
            case nameof(StudioViewModel.TrendingHFModels):
                UpdateTrendingState();
                RebuildTrendingGrid();
                break;
            case nameof(StudioViewModel.CustomRepoInput):
                SyncFormFieldsFromVm();
                RebuildTrendingGrid();
                UpdateActionButtons();
                break;
            case nameof(StudioViewModel.CustomIncludePatternInput):
            case nameof(StudioViewModel.CustomDisplayNameInput):
            case nameof(StudioViewModel.CustomModalityInput):
            case nameof(StudioViewModel.HfToken):
                SyncFormFieldsFromVm();
                break;
            case nameof(StudioViewModel.CustomModelErrorMessage):
                UpdateErrorBanner();
                break;
            case nameof(StudioViewModel.IsRegisteringCustomModel):
            case nameof(StudioViewModel.IsConnected):
                UpdateActionButtons();
                break;
        }
    }

    private void SyncAllFromVm(bool initialSync)
    {
        SyncVisibilityFromVm(initialSync);
        SyncFormFieldsFromVm();
        UpdateCategoryPills();
        UpdateTrendingState();
        RebuildTrendingGrid();
        UpdateErrorBanner();
        UpdateActionButtons();
    }

    private void SyncVisibilityFromVm(bool initialSync)
    {
        bool shouldShow = ViewModel.ShowAddCustomModelSheet;
        Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;

        if (shouldShow && (!_wasVisible || initialSync))
        {
            SyncFormFieldsFromVm();
            UpdateCategoryPills();
            UpdateTrendingState();
            RebuildTrendingGrid();
            UpdateErrorBanner();
            UpdateActionButtons();

            if (ViewModel.TrendingHFModels.Count == 0 && !ViewModel.IsLoadingTrendingHF)
            {
                _ = ViewModel.FetchTrendingHFModelsAsync();
            }
        }

        _wasVisible = shouldShow;
    }

    private void SyncFormFieldsFromVm()
    {
        _syncingFromVm = true;
        try
        {
            if (RepoInputBox.Text != ViewModel.CustomRepoInput)
                RepoInputBox.Text = ViewModel.CustomRepoInput ?? string.Empty;

            if (IncludePatternBox.Text != ViewModel.CustomIncludePatternInput)
                IncludePatternBox.Text = ViewModel.CustomIncludePatternInput ?? string.Empty;

            if (DisplayNameBox.Text != ViewModel.CustomDisplayNameInput)
                DisplayNameBox.Text = ViewModel.CustomDisplayNameInput ?? string.Empty;

            if (HfTokenBox.Password != ViewModel.HfToken)
                HfTokenBox.Password = ViewModel.HfToken ?? string.Empty;

            string mod = string.IsNullOrEmpty(ViewModel.CustomModalityInput) ? "auto" : ViewModel.CustomModalityInput;
            for (int i = 0; i < ModalityComboBox.Items.Count; i++)
            {
                if (ModalityComboBox.Items[i] is ComboBoxItem item && Equals(item.Tag, mod))
                {
                    if (ModalityComboBox.SelectedIndex != i)
                        ModalityComboBox.SelectedIndex = i;
                    break;
                }
            }
        }
        finally
        {
            _syncingFromVm = false;
        }
    }

    private void UpdateCategoryPills()
    {
        string active = ViewModel.TrendingHFCategory ?? "all";
        (string Id, Border Border, TextBlock Text)[] tabs =
        [
            ("all", TabAllBorder, TabAllText),
            ("gguf", TabGgufBorder, TabGgufText),
            ("llm", TabLlmBorder, TabLlmText),
            ("image_video", TabImageVideoBorder, TabImageVideoText),
        ];

        var accentBrush = ResolveThemeBrush("StudioSystemAccentBrush");
        var idleBg = ResolveThemeBrush("StudioPrimaryOpacity06Brush");
        var primaryBrush = ResolveThemeBrush("StudioTextPrimaryBrush");
        var whiteBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

        foreach (var (id, border, text) in tabs)
        {
            bool sel = string.Equals(active, id, StringComparison.Ordinal);
            border.Background = sel ? accentBrush : idleBg;
            text.Foreground = sel ? whiteBrush : primaryBrush;
            text.FontWeight = sel ? FontWeights.Bold : FontWeights.Medium;
        }
    }

    private void UpdateTrendingState()
    {
        bool loading = ViewModel.IsLoadingTrendingHF;
        bool hasItems = ViewModel.TrendingHFModels.Count > 0;

        RefreshSpinner.IsActive = loading;
        RefreshSpinner.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        RefreshIcon.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;

        if (!hasItems)
        {
            TrendingScrollViewer.Visibility = Visibility.Collapsed;
            TrendingEmptyPanel.Visibility = Visibility.Visible;
            TrendingEmptySpinner.IsActive = loading;
            TrendingEmptySpinner.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            TrendingEmptyText.Text = loading
                ? L("正在从 HuggingFace Hub 实时拉取模型及参数量/显存评估...", "Fetching models, parameter counts & Colab VRAM fit from HuggingFace Hub...")
                : L("未找到匹配模型，可点击「刷新」或更换搜索词，也可直接在下方输入 Repo ID。", "No matching models found. Click 'Refresh', try another keyword, or enter Repo ID below.");
        }
        else
        {
            TrendingEmptyPanel.Visibility = Visibility.Collapsed;
            TrendingEmptySpinner.IsActive = false;
            TrendingScrollViewer.Visibility = Visibility.Visible;
        }
    }

    private void RebuildTrendingGrid()
    {
        TrendingModelsGrid.Children.Clear();
        TrendingModelsGrid.RowDefinitions.Clear();

        var models = ViewModel.TrendingHFModels;
        if (models.Count == 0) return;

        int rowCount = (models.Count + 1) / 2;
        for (int r = 0; r < rowCount; r++)
        {
            TrendingModelsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var accentBrush = ResolveThemeBrush("StudioSystemAccentBrush");
        var accentFillBrush = ResolveThemeBrush("StudioAccentBlueFillBrush");
        var cardIdleFill = ResolveThemeBrush("StudioDialogCardFillBrush");
        var cardIdleBorder = ResolveThemeBrush("StudioCardBorderBrush");
        var primaryText = ResolveThemeBrush("StudioTextPrimaryBrush");
        var secondaryText = ResolveThemeBrush("StudioTextSecondaryBrush");
        var purpleFill = ResolveThemeBrush("StudioAccentPurpleFillBrush");
        var purpleBrush = ResolveThemeBrush("StudioAccentPurpleBrush");
        var orangeBrush = ResolveThemeBrush("StudioAccentOrangeBrush");
        var monoFont = Application.Current?.Resources.TryGetValue("StudioMonoFont", out var mf) == true && mf is FontFamily ff
            ? ff
            : new FontFamily("Cascadia Mono, Consolas, Courier New");

        string currentRepo = ViewModel.CustomRepoInput ?? string.Empty;

        for (int idx = 0; idx < models.Count; idx++)
        {
            var item = models[idx];
            bool isSel = string.Equals(currentRepo, item.RepoId, StringComparison.Ordinal);

            var cardBorder = new Border
            {
                Padding = new Thickness(10, 7, 10, 7),
                CornerRadius = new CornerRadius(8),
                Background = isSel ? accentFillBrush : cardIdleFill,
                BorderBrush = isSel ? accentBrush : cardIdleBorder,
                BorderThickness = new Thickness(isSel ? 1.2 : 1.0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            var cardStack = new StackPanel { Spacing = 4 };

            // Top row: [lock] name  [param_label] [tag] [heart likes]
            var topGrid = new Grid { ColumnSpacing = 4 };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (item.IsGated)
            {
                var lockIcon = new FontIcon
                {
                    Glyph = "\uE72E",
                    FontSize = 10,
                    Foreground = orangeBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                ToolTipService.SetToolTip(
                    lockIcon,
                    L("受限模型 (Gated Repo)：需填写 HF Token 并同意协议", "Gated repo: requires HF Token & license agreement"));
                nameRow.Children.Add(lockIcon);
            }

            var nameText = new TextBlock
            {
                Text = item.Name,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = primaryText,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            nameRow.Children.Add(nameText);
            Grid.SetColumn(nameRow, 0);
            topGrid.Children.Add(nameRow);

            var badgesPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (!string.IsNullOrEmpty(item.ParamLabel))
            {
                badgesPanel.Children.Add(new Border
                {
                    Padding = new Thickness(6, 1.5, 6, 1.5),
                    CornerRadius = new CornerRadius(6),
                    Background = purpleFill,
                    Child = new TextBlock
                    {
                        Text = item.ParamLabel,
                        FontSize = 9.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = purpleBrush,
                    },
                });
            }

            badgesPanel.Children.Add(new Border
            {
                Padding = new Thickness(5.5, 1.5, 5.5, 1.5),
                CornerRadius = new CornerRadius(6),
                Background = accentFillBrush,
                Child = new TextBlock
                {
                    Text = item.IsGguf ? "GGUF" : item.PipelineTag,
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = accentBrush,
                },
            });

            if (item.Likes > 0)
            {
                var likesPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 2.5,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                likesPanel.Children.Add(new FontIcon
                {
                    Glyph = "\uEB52",
                    FontSize = 8.5,
                    Foreground = secondaryText,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                likesPanel.Children.Add(new TextBlock
                {
                    Text = item.Likes.ToString(),
                    FontSize = 9.5,
                    FontWeight = FontWeights.Medium,
                    Foreground = secondaryText,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                badgesPanel.Children.Add(likesPanel);
            }

            Grid.SetColumn(badgesPanel, 1);
            topGrid.Children.Add(badgesPanel);
            cardStack.Children.Add(topGrid);

            // Middle row: repo_id
            var repoText = new TextBlock
            {
                Text = item.RepoId,
                FontFamily = monoFont,
                FontSize = 9.5,
                Foreground = secondaryText,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ToolTipService.SetToolTip(repoText, item.RepoId);
            cardStack.Children.Add(repoText);

            // Bottom row: deploy badge + Details button + selected check
            var bottomGrid = new Grid { ColumnSpacing = 5 };
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var deployBrush = item.DeployBadgeBrush;
            var deployRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center,
            };
            deployRow.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = deployBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            deployRow.Children.Add(new TextBlock
            {
                Text = item.LocalizedDeployLabel,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = deployBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(deployRow, 0);
            bottomGrid.Children.Add(deployRow);

            var rightActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var detailsBtn = new Button
            {
                Style = ResolvePlainButtonStyle(),
                Content = new Border
                {
                    Padding = new Thickness(7, 2.5, 7, 2.5),
                    CornerRadius = new CornerRadius(6),
                    Background = accentFillBrush,
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 3,
                        Children =
                        {
                            new FontIcon
                            {
                                Glyph = "\uE946",
                                FontSize = 9.5,
                                Foreground = accentBrush,
                                VerticalAlignment = VerticalAlignment.Center,
                            },
                            new TextBlock
                            {
                                Text = L("详情", "Details"),
                                FontSize = 9.5,
                                FontWeight = FontWeights.SemiBold,
                                Foreground = accentBrush,
                                VerticalAlignment = VerticalAlignment.Center,
                            },
                        },
                    },
                },
            };
            var captured = item;
            detailsBtn.Click += (_, _) =>
            {
                SelectTrendingModel(captured);
                _ = ViewModel.FetchHFModelDetailAsync(captured.RepoId);
            };
            rightActions.Children.Add(detailsBtn);

            if (isSel)
            {
                rightActions.Children.Add(new FontIcon
                {
                    Glyph = "\uE930",
                    FontSize = 11.5,
                    Foreground = accentBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            Grid.SetColumn(rightActions, 1);
            bottomGrid.Children.Add(rightActions);
            cardStack.Children.Add(bottomGrid);

            cardBorder.Child = cardStack;
            cardBorder.PointerPressed += (_, e) =>
            {
                if (!e.Handled)
                {
                    SelectTrendingModel(captured);
                    e.Handled = true;
                }
            };

            Grid.SetRow(cardBorder, idx / 2);
            Grid.SetColumn(cardBorder, idx % 2);
            TrendingModelsGrid.Children.Add(cardBorder);
        }
    }

    private void SelectTrendingModel(HFTrendingModelItem item)
    {
        ViewModel.CustomRepoInput = item.RepoId;
        ViewModel.CustomIncludePatternInput = item.SuggestedPattern;
        ViewModel.CustomDisplayNameInput = item.Name;
        ViewModel.CustomModalityInput = item.Modality;
    }

    private void UpdateErrorBanner()
    {
        string err = ViewModel.CustomModelErrorMessage ?? string.Empty;
        ErrorAlertBorder.Visibility = string.IsNullOrEmpty(err) ? Visibility.Collapsed : Visibility.Visible;
        ErrorAlertText.Text = err;
    }

    private void UpdateActionButtons()
    {
        bool hasRepo = !string.IsNullOrWhiteSpace(ViewModel.CustomRepoInput);
        bool registering = ViewModel.IsRegisteringCustomModel;
        bool connected = ViewModel.IsConnected;

        InspectRepoButton.IsEnabled = hasRepo;
        InspectRepoButton.Opacity = hasRepo ? 1.0 : 0.45;

        RegisterSpinner.IsActive = registering;
        RegisterSpinner.Visibility = registering ? Visibility.Visible : Visibility.Collapsed;
        AddModelIcon.Visibility = registering ? Visibility.Collapsed : Visibility.Visible;

        AddModelButton.IsEnabled = !registering && hasRepo;
        AddAndDownloadButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        AddAndDownloadButton.IsEnabled = !registering && hasRepo;
    }

    private void CloseDialog()
    {
        ViewModel.ShowAddCustomModelSheet = false;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => CloseDialog();

    private void OnScrimPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.ShowHFModelDetailOverlay) return;
        e.Handled = true;
        CloseDialog();
    }

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.ShowAddCustomModelSheet && !ViewModel.ShowHFModelDetailOverlay)
        {
            CloseDialog();
            args.Handled = true;
        }
    }

    private void OnCategoryTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string cat })
        {
            _ = ViewModel.FetchTrendingHFModelsAsync(
                category: cat,
                searchQuery: _trendingFilterText,
                forceRefresh: false);
        }
    }

    private void OnRefreshTrendingClick(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.FetchTrendingHFModelsAsync(
            category: ViewModel.TrendingHFCategory,
            searchQuery: _trendingFilterText,
            forceRefresh: true);
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _trendingFilterText = TrendingSearchBox.Text ?? string.Empty;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(_trendingFilterText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            _ = ViewModel.FetchTrendingHFModelsAsync(
                category: ViewModel.TrendingHFCategory,
                searchQuery: _trendingFilterText,
                forceRefresh: true);
        }
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e)
    {
        TrendingSearchBox.Text = string.Empty;
        _trendingFilterText = string.Empty;
        ClearSearchButton.Visibility = Visibility.Collapsed;
        _ = ViewModel.FetchTrendingHFModelsAsync(
            category: ViewModel.TrendingHFCategory,
            searchQuery: string.Empty,
            forceRefresh: false);
    }

    private void OnSearchOnlineClick(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.FetchTrendingHFModelsAsync(
            category: ViewModel.TrendingHFCategory,
            searchQuery: _trendingFilterText,
            forceRefresh: true);
    }

    private void OnInspectRepoClick(object sender, RoutedEventArgs e)
    {
        string clean = (ViewModel.CustomRepoInput ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(clean)) return;
        _ = ViewModel.FetchHFModelDetailAsync(clean);
    }

    private void OnRepoInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingFromVm) return;
        ViewModel.CustomRepoInput = RepoInputBox.Text ?? string.Empty;
    }

    private void OnIncludePatternTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingFromVm) return;
        ViewModel.CustomIncludePatternInput = IncludePatternBox.Text ?? string.Empty;
    }

    private void OnQuickPatternClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string pat })
        {
            ViewModel.CustomIncludePatternInput = pat;
        }
    }

    private void OnDisplayNameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingFromVm) return;
        ViewModel.CustomDisplayNameInput = DisplayNameBox.Text ?? string.Empty;
    }

    private void OnModalitySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFromVm) return;
        if (ModalityComboBox.SelectedItem is ComboBoxItem { Tag: string mod })
        {
            ViewModel.CustomModalityInput = mod;
        }
    }

    private void OnHfTokenPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingFromVm) return;
        ViewModel.HfToken = HfTokenBox.Password ?? string.Empty;
    }

    private void OnGetHfTokenClick(object sender, RoutedEventArgs e) =>
        LocalRuntimePaths.OpenUrl("https://huggingface.co/settings/tokens");

    private void OnOpenRepoLicenseClick(object sender, RoutedEventArgs e)
    {
        string clean = (ViewModel.CustomRepoInput ?? string.Empty).Trim();
        string normalized = ViewModel.NormalizeHFRepoId(clean);
        string url = normalized.Contains('/')
            ? $"https://huggingface.co/{normalized}"
            : "https://huggingface.co/models?sort=trending";
        LocalRuntimePaths.OpenUrl(url);
    }

    private void OnAddModelClick(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.RegisterCustomHFModelAsync(autoDownload: false);
    }

    private void OnAddAndDownloadClick(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.RegisterCustomHFModelAsync(autoDownload: true);
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
