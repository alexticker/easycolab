using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Windows.UI.Text;

namespace EasyColab.Controls;

/// <summary>
/// Port of SwiftUI <c>HFMarkdownExcerptView</c> (LocalizationAndText.swift): renders a cleaned HuggingFace README
/// excerpt as compact blocks — headings (bold 11.5), a bold short title line, bullets, block quotes with an accent bar
/// and paragraphs (10.5, secondary) — with inline bold / italic / code / links. All text is selectable.
/// </summary>
public sealed partial class HFMarkdownExcerptView : UserControl
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(HFMarkdownExcerptView),
        new PropertyMetadata(string.Empty, (d, _) => ((HFMarkdownExcerptView)d).Rebuild()));

    /// <summary>Raw README markdown (Swift <c>let markdown: String</c>).</summary>
    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private readonly StackPanel _stack;

    public HFMarkdownExcerptView()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        _stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Content = _stack;
        ActualThemeChanged += (_, _) => Rebuild();
        Rebuild();
    }

    private IReadOnlyList<string> Blocks =>
        SplitNewlines(CleanHFReadmeExcerpt(Markdown ?? string.Empty))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

    private void Rebuild()
    {
        _stack.Children.Clear();
        var primary = ThemeBrush("StudioTextPrimaryBrush");
        var secondary = ThemeBrush("StudioTextSecondaryBrush");
        var blocks = Blocks;
        for (int idx = 0; idx < blocks.Count; idx++)
        {
            string line = blocks[idx];
            if (line.StartsWith('#'))
            {
                string titleText = line.TrimStart('#', ' ');
                var rtb = MakeText(titleText, 11.5, FontWeights.Bold, primary);
                rtb.Margin = new Thickness(0, idx == 0 ? 0 : 3, 0, 0);
                _stack.Children.Add(rtb);
            }
            else if (idx == 0 && GraphemeCount(line) <= 60 && !line.EndsWith('.') && !line.EndsWith('。') && !line.StartsWith("**", StringComparison.Ordinal))
            {
                _stack.Children.Add(MakeText(line, 11.5, FontWeights.Bold, primary));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal) || line.StartsWith("+ ", StringComparison.Ordinal))
            {
                string itemText = line[2..].Trim(' ', '\t');
                var grid = new Grid { ColumnSpacing = 5 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var bullet = new TextBlock
                {
                    Text = "•",
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = secondary,
                    VerticalAlignment = VerticalAlignment.Top,
                };
                var body = MakeText(itemText, 10.5, FontWeights.Normal, secondary);
                Grid.SetColumn(bullet, 0);
                Grid.SetColumn(body, 1);
                grid.Children.Add(bullet);
                grid.Children.Add(body);
                _stack.Children.Add(grid);
            }
            else if (line.StartsWith('>'))
            {
                string quoteText = line.TrimStart('>', ' ');
                var grid = new Grid { ColumnSpacing = 6 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var bar = new Rectangle
                {
                    Width = 2.5,
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Fill = new SolidColorBrush(AccentColor(0.45)),
                };
                var body = MakeText(quoteText, 10.5, FontWeights.Normal, secondary);
                Grid.SetColumn(bar, 0);
                Grid.SetColumn(body, 1);
                grid.Children.Add(bar);
                grid.Children.Add(body);
                _stack.Children.Add(grid);
            }
            else
            {
                _stack.Children.Add(MakeText(line, 10.5, FontWeights.Normal, secondary));
            }
        }
    }

    private RichTextBlock MakeText(string markdownLine, double fontSize, FontWeight weight, Brush foreground)
    {
        var rtb = new RichTextBlock
        {
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var para = new Paragraph();
        var runs = RenderHFInlineMarkdown(markdownLine);
        int i = 0;
        while (i < runs.Count)
        {
            var r = runs[i];
            if (r.Link is { } link && Uri.TryCreate(link, UriKind.Absolute, out var uri))
            {
                var hl = new Hyperlink { NavigateUri = uri };
                ToolTipService.SetToolTip(hl, link);
                while (i < runs.Count && runs[i].Link == link)
                {
                    hl.Inlines.Add(MakeRun(runs[i]));
                    i++;
                }
                para.Inlines.Add(hl);
                continue;
            }
            para.Inlines.Add(MakeRun(r));
            i++;
        }
        rtb.Blocks.Add(para);
        return rtb;
    }

    private static Run MakeRun(HFInlineRun r)
    {
        var run = new Run { Text = r.Text };
        if (r.Bold) run.FontWeight = FontWeights.Bold;
        if (r.Italic) run.FontStyle = FontStyle.Italic;
        if (r.Code)
        {
            run.FontFamily = AppResource("StudioMonoFont") as FontFamily ?? new FontFamily("Cascadia Mono, Consolas");
        }
        if (r.Strikethrough) run.TextDecorations = TextDecorations.Strikethrough;
        return run;
    }

    private static object? AppResource(string key)
    {
        var res = Application.Current?.Resources;
        if (res is null) return null;
        return res.TryGetValue(key, out var v) ? v : null;
    }

    private Color AccentColor(double opacity)
    {
        var c = AppResource("SystemAccentColor") is Color ac ? ac : Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);
        return Color.FromArgb((byte)Math.Round(c.A * opacity), c.R, c.G, c.B);
    }

    /// <summary>Resolve a theme-dictionary brush for this element's <see cref="FrameworkElement.ActualTheme"/>.</summary>
    private Brush ThemeBrush(string key)
    {
        string theme = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        var root = Application.Current?.Resources;
        if (root is not null && FindThemed(root, theme, key) is Brush b) return b;
        if (AppResource(key) is Brush fallback) return fallback;
        return new SolidColorBrush(theme == "Dark" ? Color.FromArgb(0xFF, 0xF1, 0xF5, 0xF9) : Color.FromArgb(0xFF, 0x0F, 0x17, 0x2A));
    }

    private static object? FindThemed(ResourceDictionary dict, string theme, string key)
    {
        if (dict.ThemeDictionaries.TryGetValue(theme, out var td) && td is ResourceDictionary themed && themed.TryGetValue(key, out var v))
            return v;
        foreach (var merged in dict.MergedDictionaries)
        {
            if (FindThemed(merged, theme, key) is { } found) return found;
        }
        return null;
    }
}
