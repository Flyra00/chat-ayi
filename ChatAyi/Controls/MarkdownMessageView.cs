using System.Text;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace ChatAyi.Controls;

// Lightweight Markdown renderer for chat bubbles.
// Supports: headings (#..######), blockquotes (>), unordered/ordered lists, fenced code blocks (```), bold (**), italic (*), inline code (`).
public sealed class MarkdownMessageView : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(MarkdownMessageView),
        string.Empty,
        propertyChanged: (b, _, __) => ((MarkdownMessageView)b).OnTextChanged());

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private readonly VerticalStackLayout _root = new()
    {
        Spacing = 6
    };

    private readonly Label _plain = new();
    private CancellationTokenSource _debounceCts;
    private string _lastRendered = string.Empty;
    private string _pendingText = string.Empty;

    public MarkdownMessageView()
    {
        _plain.Style = GetStyle("ChatMessageText");
        _plain.LineBreakMode = LineBreakMode.WordWrap;
        _root.Add(_plain);
        Content = _root;
    }

    private void OnTextChanged()
    {
        var text = (Text ?? string.Empty).Replace("\r\n", "\n");
        _pendingText = text;  // Track latest pending

        // Always keep UI responsive while streaming by showing plain text immediately.
        _plain.Text = text;

        // If we already rendered this exact content, do nothing.
        if (string.Equals(_lastRendered, text, StringComparison.Ordinal))
            return;

        // Avoid heavy markdown layout for very large messages.
        if (text.Length > 30000)
        {
            _lastRendered = text;  // Prevent repeated attempts
            return;
        }

        // Debounce markdown rendering - increased to 800ms to allow fast streaming to complete.
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var ct = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(800, ct);  // Increased from 350ms
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var snapshot = (Text ?? string.Empty).Replace("\r\n", "\n");
            if (ct.IsCancellationRequested) return;
            if (snapshot.Length == 0) return;
            if (snapshot.Length > 30000) return;

            // Only attempt markdown if there's a sign of markdown syntax.
            if (!LooksLikeMarkdown(snapshot))
            {
                _lastRendered = snapshot;
                return;
            }

            List<Block> blocks;
            try
            {
                blocks = ParseBlocks(snapshot);
                if (blocks.Count > 100)  // Lowered from 250 for better performance
                    return;
            }
            catch
            {
                return;
            }

            if (ct.IsCancellationRequested) return;

            // Verify pending text still matches - skip if new content arrived
            if (!string.Equals(_pendingText, snapshot, StringComparison.Ordinal))
                return;

            Dispatcher?.Dispatch(() =>
            {
                if (ct.IsCancellationRequested) return;
                var current = (Text ?? string.Empty).Replace("\r\n", "\n");
                if (!string.Equals(current, snapshot, StringComparison.Ordinal))
                    return;

                _root.Clear();
                foreach (var b in blocks)
                    _root.Add(RenderBlock(b));

                _lastRendered = snapshot;
            });
        });
    }

    private sealed record Block(BlockKind Kind, string Text, int Level = 0, string Marker = "");

    private enum BlockKind
    {
        Paragraph,
        Heading,
        Quote,
        UnorderedListItem,
        OrderedListItem,
        CodeBlock,
        HorizontalRule,
        Table,
    }

    private static List<Block> ParseBlocks(string input)
    {
        var blocks = new List<Block>();
        var lines = input.Split('\n');

        var paragraph = new StringBuilder();
        var code = new StringBuilder();
        var inCode = false;

        void FlushParagraph()
        {
            if (paragraph.Length == 0) return;
            blocks.Add(new Block(BlockKind.Paragraph, paragraph.ToString().Trim()));
            paragraph.Clear();
        }

        void FlushCode()
        {
            blocks.Add(new Block(BlockKind.CodeBlock, code.ToString().TrimEnd()));
            code.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = raw?.TrimEnd() ?? string.Empty;

            var trimmed = line.Trim();

            // Horizontal rule: --- / *** / ___ (3+)
            if (IsHorizontalRule(trimmed))
            {
                FlushParagraph();
                blocks.Add(new Block(BlockKind.HorizontalRule, string.Empty));
                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (!inCode)
                {
                    FlushParagraph();
                    inCode = true;
                    continue;
                }

                inCode = false;
                FlushCode();
                continue;
            }

            if (inCode)
            {
                code.AppendLine(raw ?? string.Empty);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                continue;
            }

            // Markdown table: header row with | then separator row (---) then body rows.
            // Capture until blank line.
            if (LooksLikeTableRow(trimmed) && i + 1 < lines.Length)
            {
                var next = (lines[i + 1] ?? string.Empty).Trim();
                if (LooksLikeTableSeparator(next))
                {
                    FlushParagraph();
                    var table = new StringBuilder();
                    table.AppendLine(trimmed);
                    table.AppendLine(next);
                    i += 2;
                    for (; i < lines.Length; i++)
                    {
                        var t = (lines[i] ?? string.Empty).TrimEnd();
                        if (string.IsNullOrWhiteSpace(t))
                            break;
                        if (!LooksLikeTableRow(t.Trim()))
                            break;
                        table.AppendLine(t.Trim());
                    }

                    // The for-loop will i++ once more.
                    i--;
                    blocks.Add(new Block(BlockKind.Table, table.ToString().TrimEnd()));
                    continue;
                }
            }

            // Headings: #..###### + space
            var level = 0;
            while (level < line.Length && level < 6 && line[level] == '#') level++;
            if (level > 0 && level < line.Length && line[level] == ' ')
            {
                FlushParagraph();
                blocks.Add(new Block(BlockKind.Heading, line.Substring(level + 1).Trim(), level));
                continue;
            }

            // Quote: > text
            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushParagraph();
                blocks.Add(new Block(BlockKind.Quote, line.Substring(2).Trim()));
                continue;
            }

            // Unordered list: -/*/+ + space
            if (line.Length > 2 && (line[0] == '-' || line[0] == '*' || line[0] == '+') && line[1] == ' ')
            {
                FlushParagraph();
                blocks.Add(new Block(BlockKind.UnorderedListItem, line.Substring(2).Trim(), Marker: "•"));
                continue;
            }

            // Ordered list: 1. text
            var dot = line.IndexOf('.');
            if (dot > 0 && dot < 6)
            {
                var numPart = line.Substring(0, dot);
                if (int.TryParse(numPart, out _))
                {
                    var after = dot + 1 < line.Length && line[dot + 1] == ' ' ? line.Substring(dot + 2) : line.Substring(dot + 1);
                    FlushParagraph();
                    blocks.Add(new Block(BlockKind.OrderedListItem, after.Trim(), Marker: numPart + "."));
                    continue;
                }
            }

            // Paragraph: keep joining lines until a blank or a new block.
            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(line.Trim());
        }

        FlushParagraph();
        if (inCode)
            FlushCode();

        return blocks;
    }

    private static bool IsHorizontalRule(string trimmed)
    {
        if (trimmed.Length < 3) return false;
        static bool AllOf(string s, char ch)
        {
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == ' ') continue;
                if (c != ch) return false;
            }
            return true;
        }

        return AllOf(trimmed, '-') || AllOf(trimmed, '*') || AllOf(trimmed, '_');
    }

    private static bool LooksLikeTableRow(string trimmed)
    {
        // Simple heuristic: at least one pipe and at least 2 columns.
        if (!trimmed.Contains('|')) return false;
        var count = 0;
        for (var i = 0; i < trimmed.Length; i++)
            if (trimmed[i] == '|') count++;
        return count >= 2;
    }

    private static bool LooksLikeTableSeparator(string trimmed)
    {
        // Typical: | --- | --- | or ---|---
        if (!trimmed.Contains('-') || !trimmed.Contains('|')) return false;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c == '|' || c == '-' || c == ':' || c == ' ') continue;
            return false;
        }
        return true;
    }

    private static bool LooksLikeMarkdown(string s)
    {
        // Quick heuristic: headings, lists, quotes, or fenced code.
        if (s.IndexOf("```", StringComparison.Ordinal) >= 0) return true;
        if (s.IndexOf("\n#", StringComparison.Ordinal) >= 0 || s.StartsWith("#", StringComparison.Ordinal)) return true;
        if (s.IndexOf("\n- ", StringComparison.Ordinal) >= 0 || s.IndexOf("\n* ", StringComparison.Ordinal) >= 0) return true;
        if (s.IndexOf("\n> ", StringComparison.Ordinal) >= 0) return true;
        if (s.IndexOf("**", StringComparison.Ordinal) >= 0) return true;
        if (s.IndexOf('`') >= 0) return true;
        return false;
    }

    private View RenderBlock(Block b)
    {
        return b.Kind switch
        {
            BlockKind.Heading => CreateHeading(b.Text, b.Level),
            BlockKind.Quote => CreateQuote(b.Text),
            BlockKind.UnorderedListItem => CreateListItem(b.Marker, b.Text),
            BlockKind.OrderedListItem => CreateListItem(b.Marker, b.Text),
            BlockKind.CodeBlock => CreateCodeBlock(b.Text),
            BlockKind.HorizontalRule => CreateHorizontalRule(),
            BlockKind.Table => CreateTable(b.Text),
            _ => CreateParagraph(b.Text),
        };
    }

    private Label CreateParagraph(string text)
    {
        return new Label
        {
            Style = GetStyle("ChatMessageText"),
            LineBreakMode = LineBreakMode.WordWrap,
            FormattedText = ParseInline(text)
        };
    }

    private Label CreateHeading(string text, int level)
    {
        var size = level switch
        {
            1 => 18,
            2 => 16,
            3 => 15,
            _ => 14
        };

        return new Label
        {
            FontFamily = "OpenSansSemibold",
            FontSize = size,
            TextColor = GetColor("Gray100", Colors.White),
            LineHeight = 1.2,
            LineBreakMode = LineBreakMode.WordWrap,
            FormattedText = ParseInline(text)
        };
    }

    private View CreateQuote(string text)
    {
        var stripe = new BoxView
        {
            WidthRequest = 3,
            BackgroundColor = GetColor("NvidiaGreen", Colors.Green),
            CornerRadius = 2,
            VerticalOptions = LayoutOptions.Fill
        };

        var label = new Label
        {
            FontFamily = "OpenSansRegular",
            FontSize = 13,
            TextColor = GetColor("Gray300", Colors.LightGray),
            LineHeight = 1.3,
            LineBreakMode = LineBreakMode.WordWrap,
            FormattedText = ParseInline(text)
        };

        var body = new Border
        {
            Background = GetBrush("ChatSurface2Brush"),
            Stroke = GetBrush("ChatStrokeBrush"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(10, 8),
            HorizontalOptions = LayoutOptions.Fill
        };
        body.Content = label;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = 3 },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 10
        };
        grid.Add(stripe);
        grid.Add(body, 1);
        return grid;
    }

    private View CreateListItem(string marker, string text)
    {
        var m = new Label
        {
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            TextColor = GetColor("NvidiaGreen", Colors.Green),
            Text = marker,
            VerticalTextAlignment = TextAlignment.Start
        };

        var t = new Label
        {
            Style = GetStyle("ChatMessageText"),
            LineBreakMode = LineBreakMode.WordWrap,
            FormattedText = ParseInline(text)
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = 26 },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 6
        };
        grid.Add(m);
        grid.Add(t, 1);
        return grid;
    }

    private View CreateCodeBlock(string code)
    {
        var label = new Label
        {
            FontFamily = "Consolas",
            FontSize = 12.5,
            TextColor = GetColor("Gray100", Colors.White),
            LineHeight = 1.2,
            Text = code,
            LineBreakMode = LineBreakMode.WordWrap,
            HorizontalOptions = LayoutOptions.Start
        };

        var border = new Border
        {
            Background = GetBrush("ChatSurface2Brush"),
            Stroke = GetBrush("ChatStrokeBrush"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 4),
            HorizontalOptions = LayoutOptions.Start
        };
        border.Content = label;
        return border;
    }

    private View CreateHorizontalRule()
    {
        return new BoxView
        {
            HeightRequest = 1,
            BackgroundColor = GetColor("ChatStroke", Colors.Gray),
            Margin = new Thickness(0, 6)
        };
    }

    private View CreateTable(string raw)
    {
        var lines = (raw ?? string.Empty).Split('\n').Select(x => (x ?? string.Empty).Trim()).Where(x => x.Length > 0).ToList();
        if (lines.Count < 2) return CreateParagraph(raw);

        // Drop the separator line (2nd line)
        var header = SplitTableRow(lines[0]);
        var rows = new List<List<string>>();
        for (var i = 2; i < lines.Count; i++)
            rows.Add(SplitTableRow(lines[i]));

        var cols = Math.Max(header.Count, rows.Count == 0 ? 0 : rows.Max(r => r.Count));
        if (cols < 2) return CreateParagraph(raw);

        var grid = new Grid
        {
            ColumnSpacing = 10,
            RowSpacing = 6,
            Padding = new Thickness(2, 0)
        };

        for (var c = 0; c < cols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < rows.Count; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var c = 0; c < cols; c++)
        {
            var cell = c < header.Count ? header[c] : string.Empty;
            var lbl = new Label
            {
                FontFamily = "OpenSansSemibold",
                FontSize = 13,
                TextColor = GetColor("Gray200", Colors.LightGray),
                LineBreakMode = LineBreakMode.WordWrap,
                FormattedText = ParseInline(cell)
            };
            grid.Add(lbl, c, 0);
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (var c = 0; c < cols; c++)
            {
                var cell = c < row.Count ? row[c] : string.Empty;
                var lbl = new Label
                {
                    Style = GetStyle("ChatMessageText"),
                    FontSize = 13,
                    LineBreakMode = LineBreakMode.WordWrap,
                    FormattedText = ParseInline(cell)
                };
                grid.Add(lbl, c, r + 1);
            }
        }

        var border = new Border
        {
            Background = GetBrush("ChatSurface2Brush"),
            Stroke = GetBrush("ChatStrokeBrush"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 4)
        };
        border.Content = grid;
        return border;
    }

    private static List<string> SplitTableRow(string line)
    {
        var t = (line ?? string.Empty).Trim();
        if (t.StartsWith("|", StringComparison.Ordinal)) t = t.Substring(1);
        if (t.EndsWith("|", StringComparison.Ordinal)) t = t.Substring(0, t.Length - 1);

        return t.Split('|')
            .Select(x => (x ?? string.Empty).Trim())
            .ToList();
    }

    private FormattedString ParseInline(string text)
    {
        var fs = new FormattedString();
        if (string.IsNullOrEmpty(text))
            return fs;

        var i = 0;
        while (i < text.Length)
        {
            // Inline code: `code`
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    var inner = text.Substring(i + 1, end - i - 1);
                    fs.Spans.Add(new Span
                    {
                        Text = inner,
                        FontFamily = "Consolas",
                        TextColor = GetColor("Gray200", Colors.LightGray)
                    });
                    i = end + 1;
                    continue;
                }
            }

            // Bold: **text**
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i)
                {
                    var inner = text.Substring(i + 2, end - i - 2);
                    fs.Spans.Add(new Span { Text = inner, FontAttributes = FontAttributes.Bold });
                    i = end + 2;
                    continue;
                }
            }

            // Italic: *text*
            if (text[i] == '*')
            {
                var end = text.IndexOf('*', i + 1);
                if (end > i)
                {
                    var inner = text.Substring(i + 1, end - i - 1);
                    fs.Spans.Add(new Span { Text = inner, FontAttributes = FontAttributes.Italic });
                    i = end + 1;
                    continue;
                }
            }

            // Plain chunk until next token
            var next = NextInlineToken(text, i);
            var chunk = next <= i ? text.Substring(i, 1) : text.Substring(i, next - i);
            fs.Spans.Add(new Span { Text = chunk });
            i = next <= i ? i + 1 : next;
        }

        return fs;
    }

    private static int NextInlineToken(string s, int start)
    {
        var best = -1;

        void Consider(int idx)
        {
            if (idx < 0) return;
            if (best < 0 || idx < best) best = idx;
        }

        Consider(s.IndexOf('`', start));
        Consider(s.IndexOf("**", start, StringComparison.Ordinal));
        Consider(s.IndexOf('*', start));

        return best < 0 ? s.Length : best;
    }

    private static Style GetStyle(string key)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(key, out var v) == true)
                return v as Style;
        }
        catch { }
        return null;
    }

    private static Brush GetBrush(string key)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Brush b)
                return b;
        }
        catch { }
        return new SolidColorBrush(Colors.Transparent);
    }

    private static Color GetColor(string key, Color fallback)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(key, out var v) == true)
            {
                if (v is Color c) return c;
                if (v is SolidColorBrush sb) return sb.Color;
            }
        }
        catch { }
        return fallback;
    }
}
