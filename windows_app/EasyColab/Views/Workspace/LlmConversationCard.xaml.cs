using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace EasyColab.Views.Workspace;

/// <summary>
/// Port of Swift <c>llmConversationCard(palette:)</c> in MainStudioView+Workspace.swift.
/// Streams reasoning (collapsible chain-of-thought) and assistant replies live by observing
/// <see cref="StudioViewModel.ChatHistory"/> and each <see cref="ChatTurnItem.PropertyChanged"/>.
/// </summary>
public sealed partial class LlmConversationCard : UserControl
{
    public StudioViewModel ViewModel => StudioViewModel.Shared;

    private readonly Dictionary<Guid, TurnRowVisual> _turnRows = new();
    private bool _subscribed;

    public LlmConversationCard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += OnActualThemeChanged;
        SyncHeaderAndButtons();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        foreach (var row in _turnRows.Values)
            row.RefreshTheme(ActualTheme);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ViewModel.ChatHistory.CollectionChanged += OnChatHistoryChanged;
        }
        SyncHeaderAndButtons();
        RebuildTurns();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            _subscribed = false;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.ChatHistory.CollectionChanged -= OnChatHistoryChanged;
        }
        DetachAllTurns();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(StudioViewModel.SelectedModelId):
            case nameof(StudioViewModel.ActiveModelSpec):
            case nameof(StudioViewModel.ContextLengthK):
                SyncHeaderAndButtons();
                foreach (var row in _turnRows.Values)
                    row.UpdateModelName(ViewModel.ActiveModelSpec.Name);
                break;

            case nameof(StudioViewModel.IsGenerating):
                SyncHeaderAndButtons();
                foreach (var row in _turnRows.Values)
                    row.UpdateGeneratingState(ViewModel.IsGenerating);
                break;
        }
    }

    private void OnChatHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (ChatTurnItem item in e.NewItems)
            {
                AddTurnVisual(item);
            }
            SyncEmptyAndButtons();
            ScrollToBottom(force: true);
        }
        else
        {
            RebuildTurns();
        }
    }

    private void SyncHeaderAndButtons()
    {
        var modelName = ViewModel.ActiveModelSpec.Name;
        RootCard.Title = L(
            $"{modelName} 深度推理与视觉对话输出",
            $"{modelName} Deep Reasoning & Vision Chat");
        RootCard.Subtitle = L(
            $"实时流式展示模型内部思考链与最终回答（当前上下文窗口 {ViewModel.ContextLengthK}K），支持一键复制为视频/生图提示词",
            $"Streams internal chain-of-thought and final response in real time ({ViewModel.ContextLengthK}K context window)");
        EmptyHintText.Text = L(
            $"加载 {modelName} 后，在上方输入问题或拖入图片即可开启流式深度推理",
            $"Load {modelName}, then enter a prompt or drop images above to stream reasoning & answers");
        SyncEmptyAndButtons();
    }

    private void SyncEmptyAndButtons()
    {
        bool empty = ViewModel.ChatHistory.Count == 0;
        EmptyStatePanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ChatScrollViewer.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ClearChatButton.IsEnabled = !empty && !ViewModel.IsGenerating;
    }

    private void RebuildTurns()
    {
        DetachAllTurns();
        TurnsHost.Children.Clear();
        foreach (var turn in ViewModel.ChatHistory)
        {
            AddTurnVisual(turn);
        }
        SyncEmptyAndButtons();
        if (ViewModel.ChatHistory.Count > 0)
        {
            ScrollToBottom(force: true);
        }
    }

    private void DetachAllTurns()
    {
        foreach (var row in _turnRows.Values)
        {
            row.Dispose();
        }
        _turnRows.Clear();
    }

    private void AddTurnVisual(ChatTurnItem turn)
    {
        if (_turnRows.ContainsKey(turn.Id)) return;
        var row = new TurnRowVisual(
            turn,
            ViewModel.ActiveModelSpec.Name,
            ViewModel.IsGenerating,
            ActualTheme,
            onStreamUpdate: () => ScrollToBottom(force: false),
            onInteract: () => ViewModel.RecordLocalInteraction());
        _turnRows[turn.Id] = row;
        TurnsHost.Children.Add(row.Root);
    }

    private void ScrollToBottom(bool force)
    {
        if (ChatScrollViewer.Visibility != Visibility.Visible) return;
        bool nearBottom = force || (ChatScrollViewer.ScrollableHeight - ChatScrollViewer.VerticalOffset <= 64);
        if (!nearBottom) return;

        DispatcherQueue?.TryEnqueue(() =>
        {
            ChatScrollViewer.UpdateLayout();
            ChatScrollViewer.ChangeView(null, ChatScrollViewer.ScrollableHeight, null, disableAnimation: !force);
        });
    }

    private void ApiGatewayButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RecordLocalInteraction();
        ViewModel.TaskModeId = "api";
    }

    private void ClearChatButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RecordLocalInteraction();
        ViewModel.ChatHistory.Clear();
    }

    /// <summary>
    /// Stateful visual row for a single <see cref="ChatTurnItem"/> so streaming updates mutate
    /// TextBlocks in-place without resetting the thinking disclosure state or breaking text selection.
    /// </summary>
    private sealed class TurnRowVisual : IDisposable
    {
        private readonly ChatTurnItem _turn;
        private readonly bool _isUser;
        private readonly Action _onStreamUpdate;
        private readonly Action _onInteract;

        private readonly FontIcon _roleIcon;
        private readonly TextBlock _senderLabel;
        private readonly Border _imageBadge;
        private readonly TextBlock _imageBadgeText;
        private readonly Button _copyButton;
        private readonly Border _thinkingBox;
        private readonly FontIcon _thinkingChevron;
        private readonly TextBlock _thinkingHeader;
        private readonly TextBlock _thinkingBody;
        private readonly TextBlock _contentText;
        private readonly StackPanel _generatingRow;
        private readonly TextBlock _generatingText;

        private bool _thinkingExpanded;
        private bool _isGenerating;
        private int _copyFeedbackVersion;

        public Border Root { get; }

        public TurnRowVisual(
            ChatTurnItem turn,
            string modelName,
            bool isGenerating,
            ElementTheme theme,
            Action onStreamUpdate,
            Action onInteract)
        {
            _turn = turn;
            _isUser = string.Equals(turn.Role, "user", StringComparison.Ordinal);
            _isGenerating = isGenerating;
            _onStreamUpdate = onStreamUpdate;
            _onInteract = onInteract;

            var stack = new StackPanel { Spacing = 8 };

            // Header row
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftHeader = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
            };

            _roleIcon = new FontIcon
            {
                Glyph = _isUser ? "\uE77B" : "\uE734",
                FontSize = 11.5,
            };
            leftHeader.Children.Add(_roleIcon);

            _senderLabel = new TextBlock
            {
                Text = _isUser ? L("提问", "You") : modelName,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            leftHeader.Children.Add(_senderLabel);

            _imageBadgeText = new TextBlock
            {
                Text = L("附带视觉图像", "Image Attached"),
                FontSize = 10,
            };
            _imageBadge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = turn.HasImage ? Visibility.Visible : Visibility.Collapsed,
                Child = _imageBadgeText,
            };
            leftHeader.Children.Add(_imageBadge);
            header.Children.Add(leftHeader);

            _copyButton = new Button
            {
                Content = L("复制回复", "Copy Reply"),
                FontSize = 11,
                Padding = new Thickness(8, 2, 8, 2),
                MinHeight = 0,
                MinWidth = 0,
                Visibility = Visibility.Collapsed,
            };
            _copyButton.Click += OnCopyClick;
            Grid.SetColumn(_copyButton, 1);
            header.Children.Add(_copyButton);

            stack.Children.Add(header);

            // Collapsible Chain-of-Thought block (Swift DisclosureGroup)
            _thinkingChevron = new FontIcon
            {
                Glyph = "\uE76C",
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _thinkingHeader = new TextBlock
            {
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var toggleContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
            };
            toggleContent.Children.Add(_thinkingChevron);
            toggleContent.Children.Add(_thinkingHeader);

            var toggleBtn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = toggleContent,
            };
            if (Application.Current.Resources.TryGetValue("StudioPlainButtonStyle", out var plainStyle) && plainStyle is Style s)
            {
                toggleBtn.Style = s;
            }
            toggleBtn.Click += OnToggleThinking;

            _thinkingBody = new TextBlock
            {
                FontSize = 12,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed,
            };

            var thinkingStack = new StackPanel();
            thinkingStack.Children.Add(toggleBtn);
            thinkingStack.Children.Add(_thinkingBody);

            _thinkingBox = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Child = thinkingStack,
                Visibility = Visibility.Collapsed,
            };
            stack.Children.Add(_thinkingBox);

            // Main content
            _contentText = new TextBlock
            {
                FontSize = 13.5,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            stack.Children.Add(_contentText);

            // Streaming placeholder when assistant has no content yet
            _generatingRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Visibility = Visibility.Collapsed,
            };
            _generatingRow.Children.Add(new ProgressRing
            {
                Width = 14,
                Height = 14,
                IsActive = true,
            });
            _generatingText = new TextBlock
            {
                Text = L("正在思考并组织回答...", "Thinking and composing response..."),
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _generatingRow.Children.Add(_generatingText);
            stack.Children.Add(_generatingRow);

            Root = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12),
                BorderThickness = new Thickness(0.7),
                Child = stack,
            };

            RefreshTheme(theme);
            _turn.PropertyChanged += OnTurnPropertyChanged;
            RefreshContent();
        }

        public void RefreshTheme(ElementTheme theme)
        {
            var textPrimary = ThemeBrush(theme, "StudioTextPrimaryBrush");
            var textSecondary = ThemeBrush(theme, "StudioTextSecondaryBrush");
            var accent = ThemeBrush(theme, "StudioSystemAccentBrush");
            var subtleFill = ThemeBrush(theme, "StudioPrimaryOpacity06Brush");
            var border = ThemeBrush(theme, "StudioCardBorderBrush");
            var cardFill = ThemeBrush(theme, _isUser ? "StudioInputFillBrush" : "StudioWorkspaceCardFillBrush");

            var roleBrush = _isUser ? textSecondary : accent;
            _roleIcon.Foreground = roleBrush;
            _senderLabel.Foreground = roleBrush;
            _imageBadge.Background = subtleFill;
            _imageBadgeText.Foreground = textSecondary;
            _thinkingChevron.Foreground = textSecondary;
            _thinkingHeader.Foreground = textSecondary;
            _thinkingBody.Foreground = textSecondary;
            _thinkingBox.Background = subtleFill;
            _contentText.Foreground = textPrimary;
            _generatingText.Foreground = textSecondary;
            Root.BorderBrush = border;
            Root.Background = cardFill;
        }

        private static SolidColorBrush ThemeBrush(ElementTheme theme, string key)
        {
            var color = EasyColab.Controls.StudioColors.ResolveThemeColor(
                key, theme, Windows.UI.Color.FromArgb(0xFF, 0x94, 0xA3, 0xB8));
            return new SolidColorBrush(color);
        }

        public void UpdateModelName(string modelName)
        {
            if (!string.Equals(_turn.Role, "user", StringComparison.Ordinal))
            {
                _senderLabel.Text = modelName;
            }
        }

        public void UpdateGeneratingState(bool isGenerating)
        {
            _isGenerating = isGenerating;
            RefreshContent();
        }

        private void OnTurnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RefreshContent();
            _onStreamUpdate();
        }

        private void RefreshContent()
        {
            bool isAssistant = string.Equals(_turn.Role, "assistant", StringComparison.Ordinal);
            bool hasThinking = !string.IsNullOrEmpty(_turn.Thinking);
            bool hasContent = !string.IsNullOrEmpty(_turn.Content);

            _imageBadge.Visibility = _turn.HasImage ? Visibility.Visible : Visibility.Collapsed;
            _copyButton.Visibility = (isAssistant && hasContent) ? Visibility.Visible : Visibility.Collapsed;

            _thinkingBox.Visibility = hasThinking ? Visibility.Visible : Visibility.Collapsed;
            if (hasThinking)
            {
                int count = GraphemeCount(_turn.Thinking);
                _thinkingHeader.Text = L(
                    $"深度思考过程 ({count} 字)",
                    $"Chain of Thought ({count} chars)");
                _thinkingBody.Text = _turn.Thinking;
                _thinkingBody.Visibility = _thinkingExpanded ? Visibility.Visible : Visibility.Collapsed;
                _thinkingChevron.Glyph = _thinkingExpanded ? "\uE70D" : "\uE76C";
            }

            if (hasContent)
            {
                _contentText.Text = _turn.Content;
                _contentText.Visibility = Visibility.Visible;
                _generatingRow.Visibility = Visibility.Collapsed;
            }
            else if (isAssistant && _isGenerating)
            {
                _contentText.Visibility = Visibility.Collapsed;
                _generatingRow.Visibility = Visibility.Visible;
            }
            else
            {
                _contentText.Visibility = Visibility.Collapsed;
                _generatingRow.Visibility = Visibility.Collapsed;
            }
        }

        private void OnToggleThinking(object sender, RoutedEventArgs e)
        {
            _thinkingExpanded = !_thinkingExpanded;
            _thinkingBody.Visibility = _thinkingExpanded ? Visibility.Visible : Visibility.Collapsed;
            _thinkingChevron.Glyph = _thinkingExpanded ? "\uE70D" : "\uE76C";
            _onInteract();
        }

        private async void OnCopyClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_turn.Content)) return;
            _onInteract();
            try
            {
                var dp = new DataPackage();
                dp.SetText(_turn.Content);
                Clipboard.SetContent(dp);
                var ver = ++_copyFeedbackVersion;
                _copyButton.Content = L("已复制", "Copied");
                await Task.Delay(1400);
                if (ver == _copyFeedbackVersion)
                {
                    _copyButton.Content = L("复制回复", "Copy Reply");
                }
            }
            catch { }
        }

        public void Dispose()
        {
            _turn.PropertyChanged -= OnTurnPropertyChanged;
        }
    }
}
