using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MovieShelf.App;

/// <summary>A piece of help text in one style. <see cref="Link"/> is the href of an a tag.</summary>
sealed record HelpSpan(string Text, bool Bold, bool Italic, bool Code, string? Link);

sealed record HelpCell(List<HelpSpan> Spans, bool Head);

enum HelpKind { Subheading, Paragraph, Note, Item, Term, Definition, Table }

/// <summary>One block of a topic. A table keeps its text in <see cref="Rows"/>, everything else in <see cref="Spans"/>.</summary>
sealed class HelpBlock
{
    public HelpKind Kind;
    public int Depth;                   // list nesting; 0 outside lists
    public string Marker = "";          // the bullet or number of a list item
    public List<HelpSpan> Spans = new();
    public List<List<HelpCell>> Rows = new();
}

/// <summary>One section of en.html or fa.html. Its text is cut into blocks only when the topic is first shown.</summary>
sealed class HelpTopic
{
    string? text;
    List<HelpBlock>? blocks;

    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Html { get; init; }

    public List<HelpBlock> Blocks => blocks ??= HelpDoc.Parse(Html);

    /// <summary>The topic as plain text, for the search box.</summary>
    public string Text => text ??= Title + " " + HelpDoc.Plain(Html);

    public bool Matches(string q) => Text.Contains(q, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Reads the help files embedded in the app. They use section, h2, h3, p, ul, ol, li, dl, dt, dd, table, tr, th, td, b, i, code, kbd and div;
/// a tags are handled as well, so a topic can link to another one.</summary>
static class HelpDoc
{
    static readonly Regex Section = new(@"<section id=""([^""]+)"">\s*<h2>(.*?)</h2>(.*?)</section>", RegexOptions.Singleline);
    static readonly Regex Tag = new(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:\s[^>]*)?)>");
    static readonly Regex InlineTag = new(@"</?(?:b|i|code|kbd|a)(?:\s[^>]*)?>");
    static readonly Regex AnyTag = new(@"<[^>]+>");
    static readonly Regex Spaces = new(@"\s+");
    static readonly Regex Href = new(@"href=""([^""]*)""");
    static readonly Dictionary<string, List<HelpTopic>> cache = new();

    public static string Source(string lang)
    {
        using var st = typeof(HelpDoc).Assembly.GetManifestResourceStream($"help.{lang}.html")
            ?? throw new FileNotFoundException("The help could not be loaded.");
        using var rd = new StreamReader(st, Encoding.UTF8);
        return rd.ReadToEnd();
    }

    public static List<HelpTopic> Load(string lang)
    {
        lock (cache)
        {
            if (cache.TryGetValue(lang, out var ready)) return ready;
            var list = Section.Matches(Source(lang)).Select(m => new HelpTopic
            {
                Id = m.Groups[1].Value, Title = Plain(m.Groups[2].Value), Html = m.Groups[3].Value,
            }).ToList();
            return cache[lang] = list;
        }
    }

    public static string Plain(string html) =>
        Spaces.Replace(WebUtility.HtmlDecode(AnyTag.Replace(InlineTag.Replace(html, ""), " ")), " ").Trim();

    public static List<HelpBlock> Parse(string html)
    {
        var blocks = new List<HelpBlock>();
        var lists = new Stack<int[]>();     // one entry per open list: the last number given, -1 for bullets
        HelpBlock? table = null;
        List<HelpCell>? row = null;
        List<HelpSpan>? target = null;
        int bold = 0, italic = 0, code = 0;
        string? link = null;

        void Trim()
        {
            if (target == null) return;
            if (target.Count > 0) target[0] = target[0] with { Text = target[0].Text.TrimStart() };
            if (target.Count > 0) target[^1] = target[^1] with { Text = target[^1].Text.TrimEnd() };
            target.RemoveAll(s => s.Text.Length == 0);
            target = null;
        }

        void Open(HelpKind kind, string marker = "")
        {
            Trim();
            var b = new HelpBlock { Kind = kind, Depth = lists.Count, Marker = marker };
            blocks.Add(b);
            target = b.Spans;
        }

        void Text(string raw)
        {
            string text = Spaces.Replace(WebUtility.HtmlDecode(raw), " ");
            if (target == null)
            {
                if (text.Trim().Length == 0) return;
                Open(HelpKind.Paragraph);
            }
            target!.Add(new HelpSpan(text, bold > 0, italic > 0, code > 0, link));
        }

        int at = 0;
        foreach (Match m in Tag.Matches(html))
        {
            if (m.Index > at) Text(html[at..m.Index]);
            at = m.Index + m.Length;
            bool end = m.Groups[1].Length > 0;
            string name = m.Groups[2].Value.ToLowerInvariant();
            switch (name)
            {
                case "b": bold += end ? -1 : 1; break;
                case "i": italic += end ? -1 : 1; break;
                case "code": code += end ? -1 : 1; break;
                case "kbd": code += end ? -1 : 1; bold += end ? -1 : 1; break;
                case "a":
                    var href = Href.Match(m.Groups[3].Value);
                    link = !end && href.Success ? WebUtility.HtmlDecode(href.Groups[1].Value) : null;
                    break;
                case "h3": if (end) Trim(); else Open(HelpKind.Subheading); break;
                case "p": if (end) Trim(); else Open(HelpKind.Paragraph); break;
                case "div": if (end) Trim(); else Open(HelpKind.Note); break;
                case "dt": if (end) Trim(); else Open(HelpKind.Term); break;
                case "dd": if (end) Trim(); else Open(HelpKind.Definition); break;
                case "ul":
                case "ol":
                    Trim();
                    if (!end) lists.Push(new[] { name == "ol" ? 0 : -1 });
                    else if (lists.Count > 0) lists.Pop();
                    break;
                case "li":
                    if (end) { Trim(); break; }
                    var list = lists.Count > 0 ? lists.Peek() : new[] { -1 };
                    Open(HelpKind.Item, list[0] < 0 ? "•" : $"{++list[0]}.");
                    break;
                case "table":
                    Trim();
                    table = end ? null : new HelpBlock { Kind = HelpKind.Table };
                    if (table != null) blocks.Add(table);
                    break;
                case "tr":
                    Trim();
                    row = end ? null : new List<HelpCell>();
                    if (row != null) table?.Rows.Add(row);
                    break;
                case "td":
                case "th":
                    Trim();
                    if (end) break;
                    target = new List<HelpSpan>();
                    row?.Add(new HelpCell(target, name == "th"));
                    break;
            }
        }
        if (at < html.Length) Text(html[at..]);
        Trim();
        return blocks;
    }
}

/// <summary>The same text as a plain page for the default browser, to read beside the app or to print. WebView2 is not shipped, so the browser does this job.</summary>
static class HelpPage
{
    const string Css = """
        :root { color-scheme: light dark; --fg: #1f1f1f; --dim: #5c5c5c; --line: #dcdcdc; --soft: #f3f3f3; --note: #dbeafe; --link: #0a5fb4; }
        @media (prefers-color-scheme: dark) { :root { --fg: #f0f0f0; --dim: #b0b0b0; --line: #3d3d3d; --soft: #2a2a2a; --note: #1e3350; --link: #7cc0ff; } }
        * { box-sizing: border-box; }
        body { margin: 0; font: 15px/1.7 "Segoe UI", Tahoma, sans-serif; color: var(--fg); }
        header { display: flex; align-items: center; gap: 16px; padding: 14px 24px; border-bottom: 1px solid var(--line); }
        h1 { font-size: 20px; margin: 0; flex: 1; }
        .tools a, .tools button { font: inherit; padding: 4px 12px; border: 1px solid var(--line); border-radius: 6px; background: none; color: var(--fg); text-decoration: none; cursor: pointer; margin-inline-start: 8px; }
        .wrap { display: grid; grid-template-columns: 240px 1fr; gap: 32px; max-width: 1240px; margin: 0 auto; padding: 20px 24px; }
        nav.toc ol { list-style: none; margin: 0; padding: 0; position: sticky; top: 16px; }
        nav.toc a { display: block; padding: 5px 10px; border-radius: 6px; text-decoration: none; color: var(--link); }
        article { min-width: 0; }
        section { padding-bottom: 26px; margin-bottom: 26px; border-bottom: 1px solid var(--line); }
        section:last-of-type { border-bottom: 0; }
        h2 { font-size: 23px; margin: 0 0 12px; }
        h3 { font-size: 16px; margin: 22px 0 8px; }
        p, li, dd { max-width: 78ch; }
        table { width: 100%; border-collapse: collapse; margin: 10px 0 14px; font-size: 14px; }
        th, td { text-align: start; vertical-align: top; padding: 8px 10px; border-bottom: 1px solid var(--line); }
        th { color: var(--dim); font-weight: 600; background: var(--soft); }
        code, kbd { font: 13px/1.5 Consolas, monospace; background: var(--soft); border-radius: 4px; padding: 1px 6px; direction: ltr; unicode-bidi: isolate; }
        kbd { border: 1px solid var(--line); }
        .note { background: var(--note); border-radius: 8px; padding: 12px 14px; margin: 14px 0; max-width: 78ch; }
        dt { font-weight: 600; margin-top: 16px; }
        dd { margin: 4px 0 0; color: var(--dim); }
        @media (max-width: 800px) { .wrap { grid-template-columns: 1fr; } nav.toc ol { position: static; } }
        @media print {
            :root { color-scheme: light; --fg: #000; --dim: #333; --soft: #eee; --note: #eee; }
            header .tools, nav.toc { display: none; }
            .wrap { display: block; padding: 0; max-width: none; }
            h2, h3, dt { break-after: avoid; }
            tr, .note { break-inside: avoid; }
        }
        """;

    static string Name(string lang) => $"MovieShelf help {lang}.html";

    /// <summary>Writes both languages next to each other (each page links to the other) and returns the path of the page to open.</summary>
    public static string Write(string lang, string? topic)
    {
        string dir = Path.Combine(Path.GetTempPath(), "MovieShelf");
        Directory.CreateDirectory(dir);
        foreach (var l in new[] { "en", "fa" })
            File.WriteAllText(Path.Combine(dir, Name(l)), Html(l, l == lang ? topic : null), new UTF8Encoding(false));
        return Path.Combine(dir, Name(lang));
    }

    static string Html(string lang, string? topic)
    {
        bool fa = lang == "fa";
        string other = fa ? "en" : "fa";
        var toc = new StringBuilder();
        foreach (var t in HelpDoc.Load(lang))
            toc.Append($"<li><a href=\"#{t.Id}\">{WebUtility.HtmlEncode(t.Title)}</a></li>");
        // the shell cannot pass a #fragment with a file, so the page goes to the topic itself
        string jump = topic == null ? "" : $"<script>if (!location.hash) location.hash = '#{WebUtility.HtmlEncode(topic)}';</script>";
        return $"""
            <!doctype html>
            <html lang="{lang}" dir="{(fa ? "rtl" : "ltr")}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>MovieShelf: {Loc.TIn(fa, "Help")}</title>
            <style>{Css}</style>
            </head>
            <body>
            <header><h1>MovieShelf: {Loc.TIn(fa, "Help")}</h1>
            <div class="tools"><a href="{Uri.EscapeDataString(Name(other))}">{(fa ? "English" : "فارسی")}</a><button type="button" onclick="print()">{Loc.TIn(fa, "Print")}</button></div></header>
            <div class="wrap">
            <nav class="toc" aria-label="{Loc.TIn(fa, "Contents")}"><ol>{toc}</ol></nav>
            <article>{HelpDoc.Source(lang)}</article>
            </div>
            {jump}
            </body>
            </html>
            """;
    }
}

/// <summary>The guide: all topics in English and Persian with a contents list and a search box. <see cref="Open"/> takes the id of a section of the help files.</summary>
internal sealed class HelpWindow : Window
{
    static HelpWindow? instance;

    /// <summary>Opens the guide (or brings the open one forward), at <paramref name="topic"/> when given.</summary>
    public static void Open(string? topic = null)
    {
        if (instance == null)
        {
            instance = new HelpWindow(topic);
            instance.Activate();
            return;
        }
        if (topic != null) instance.Go(topic);
        instance.Activate();
    }

    static readonly FontFamily Mono = new("Consolas");
    static readonly Thickness CellPadding = new(10, 7, 10, 7);
    const string Ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'";

    // Styles from markup keep ThemeResource alive, so the blocks follow a theme change while the window is open.
    static Style BorderStyle(string background, string thickness = "0") => (Style)Microsoft.UI.Xaml.Markup.XamlReader.Load(
        $"<Style {Ns} TargetType='Border'><Setter Property='Background' Value='{{ThemeResource {background}}}'/>" +
        $"<Setter Property='BorderBrush' Value='{{ThemeResource DividerStrokeColorDefaultBrush}}'/><Setter Property='BorderThickness' Value='{thickness}'/></Style>");

    static readonly Lazy<Style> HeadCell = new(() => BorderStyle("CardBackgroundFillColorSecondaryBrush", "0,0,0,1"));
    static readonly Lazy<Style> BodyCell = new(() => BorderStyle("SubtleFillColorTransparentBrush", "0,0,0,1"));
    static readonly Lazy<Style> NoteBox = new(() => BorderStyle("SystemFillColorAttentionBackgroundBrush"));

    readonly Grid root = new();
    readonly TitleBar bar = new() { IsBackButtonVisible = false, IsPaneToggleButtonVisible = false };
    readonly TextBox search = new();
    readonly ListView toc = new() { SelectionMode = ListViewSelectionMode.Single };
    readonly ScrollViewer scroll = new() { HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly StackPanel doc = new() { Spacing = 8, Padding = new Thickness(28, 4, 28, 32), MaxWidth = 880, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button langBtn = new();
    readonly Button pageBtn = new();
    readonly InfoBar info = new() { IsOpen = false, IsClosable = true, Margin = new Thickness(16, 0, 16, 8) };
    readonly DispatcherQueueTimer searchTimer;

    List<HelpTopic> topics = new();
    string? currentId;
    string query = "";
    bool filling;
    FrameworkElement? firstHit;

    static string Lang => Loc.IsFa ? "fa" : "en";

    HelpWindow(string? topic)
    {
        currentId = topic;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(bar);
        Grid.SetRow(info, 1);
        root.Children.Add(info);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        var left = new Grid { Padding = new Thickness(12, 0, 8, 12) };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        search.Margin = new Thickness(4, 0, 4, 8);
        left.Children.Add(search);
        Grid.SetRow(toc, 1);
        left.Children.Add(toc);
        body.Children.Add(left);

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 28, 6) };
        tools.Children.Add(langBtn);
        tools.Children.Add(pageBtn);
        right.Children.Add(tools);
        scroll.Content = new Grid { Children = { doc } };   // a ScrollViewer misplaces a direct child that has a MaxWidth
        Grid.SetRow(scroll, 1);
        right.Children.Add(scroll);

        Content = root;
        WinKit.Setup(this, bar, "Help", 1120, 860, Relocalise);

        searchTimer = DispatcherQueue.CreateTimer();
        searchTimer.Interval = TimeSpan.FromMilliseconds(200);
        searchTimer.IsRepeating = false;
        searchTimer.Tick += (_, _) => ApplySearch();
        search.TextChanged += (_, _) => searchTimer.Start();
        toc.SelectionChanged += OnTocChanged;
        langBtn.Click += (_, _) => WinKit.SetLanguage(Loc.IsFa ? "en" : "fa");
        pageBtn.Click += OnOpenPage;
        var esc = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        esc.Invoked += OnEscape;
        root.KeyboardAccelerators.Add(esc);
        root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        Closed += (_, _) => instance = null;

        Texts();
        _ = LoadAsync();
    }

    void Texts()
    {
        WinKit.Direction(root);
        search.PlaceholderText = Loc.T("Search the help");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, Loc.T("Search the help"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toc, Loc.T("Contents"));
        langBtn.Content = Loc.IsFa ? "English" : "فارسی";
        pageBtn.Content = Loc.T("Open as a page");
    }

    void Relocalise()
    {
        Texts();
        info.IsOpen = false;
        _ = LoadAsync();
    }

    async Task LoadAsync()
    {
        string lang = Lang;
        List<HelpTopic> list;
        try { list = await Task.Run(() => HelpDoc.Load(lang)); }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            WinKit.Info(info, Loc.T("The help could not be loaded."), InfoBarSeverity.Error);
            return;
        }
        if (lang != Lang) return;      // the language changed again meanwhile
        topics = list;
        if (!list.Any(t => t.Id == currentId)) currentId = list.FirstOrDefault()?.Id;
        ApplySearch();
    }

    // ---------- contents and search ----------
    IEnumerable<HelpTopic> Shown => query.Length == 0 ? topics : topics.Where(t => t.Matches(query));

    void FillToc(List<HelpTopic> shown)
    {
        filling = true;
        toc.Items.Clear();
        foreach (var t in shown)
        {
            var item = new ListViewItem
            {
                Content = new TextBlock { Text = t.Title, TextWrapping = TextWrapping.Wrap, TextReadingOrder = TextReadingOrder.UseFlowDirection },
                Tag = t.Id,
            };
            toc.Items.Add(item);
            if (t.Id == currentId) toc.SelectedItem = item;
        }
        filling = false;
    }

    void ApplySearch()
    {
        searchTimer.Stop();
        query = search.Text.Trim();
        var shown = Shown.ToList();
        if (shown.Count > 0 && !shown.Any(t => t.Id == currentId)) currentId = shown[0].Id;
        FillToc(shown);
        var cur = shown.FirstOrDefault(t => t.Id == currentId);
        if (cur == null) ShowNothing();
        else Render(cur);
    }

    void OnTocChanged(object sender, SelectionChangedEventArgs e)
    {
        if (filling || toc.SelectedItem is not ListViewItem { Tag: string id } || id == currentId) return;
        var t = topics.FirstOrDefault(x => x.Id == id);
        if (t == null) return;
        currentId = id;
        Render(t);
    }

    /// <summary>Shows a topic. Asked for from elsewhere, a topic the search has hidden clears the search.</summary>
    public void Go(string id)
    {
        var t = topics.FirstOrDefault(x => x.Id == id);
        currentId = t != null || topics.Count == 0 ? id : currentId;    // before the topics are in, the id waits for them
        if (t == null) return;
        if (query.Length > 0 && !t.Matches(query))
        {
            search.Text = "";
            ApplySearch();
            return;
        }
        filling = true;
        toc.SelectedItem = toc.Items.OfType<ListViewItem>().FirstOrDefault(i => (string)i.Tag == id);
        filling = false;
        Render(t);
    }

    void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e)
    {
        e.Handled = true;
        if (search.Text.Length > 0) search.Text = "";
        else Close();
    }

    void Follow(string href)
    {
        if (href.StartsWith('#')) Go(href[1..]);
        else if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) U2.Launch(href);
    }

    async void OnOpenPage(object sender, RoutedEventArgs e)
    {
        string lang = Lang;
        string? topic = currentId;
        try
        {
            string path = await Task.Run(() => HelpPage.Write(lang, topic));
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            WinKit.Info(info, ex.Message, InfoBarSeverity.Error);
        }
    }

    // ---------- drawing a topic ----------
    void ShowNothing()
    {
        doc.Children.Clear();
        doc.Children.Add(new TextBlock { Text = Loc.T("Nothing found in the help."), Opacity = 0.75 });
    }

    void Render(HelpTopic t)
    {
        firstHit = null;
        doc.Children.Clear();
        doc.Children.Add(Heading(t.Title));
        foreach (var b in t.Blocks) doc.Children.Add(Block(b));
        scroll.ChangeView(null, 0, null, true);
        if (firstHit == null) return;
        var hit = firstHit;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            hit.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.1, AnimationDesired = false }));
    }

    FrameworkElement Heading(string title)
    {
        var tb = new TextBlock
        {
            Text = title, Style = (Style)Application.Current.Resources["TitleTextBlockStyle"], TextWrapping = TextWrapping.Wrap,
            TextReadingOrder = TextReadingOrder.UseFlowDirection, IsTextSelectionEnabled = true, Margin = new Thickness(0, 0, 0, 4),
        };
        Mark(tb, title);
        return tb;
    }

    FrameworkElement Block(HelpBlock b)
    {
        switch (b.Kind)
        {
            case HelpKind.Subheading:
                var h = Text(b.Spans);
                h.FontSize = 18;
                h.FontWeight = FontWeights.SemiBold;
                h.Margin = new Thickness(0, 12, 0, 0);
                return h;
            case HelpKind.Note:
                return new Border { Style = NoteBox.Value, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 11), Child = Text(b.Spans) };
            case HelpKind.Item:
                return Item(b);
            case HelpKind.Term:
                var term = Text(b.Spans);
                term.FontWeight = FontWeights.SemiBold;
                term.Margin = new Thickness(0, 8, 0, -4);
                return term;
            case HelpKind.Definition:
                var def = Text(b.Spans);
                def.Opacity = 0.85;
                return def;
            case HelpKind.Table:
                return Table(b);
            default:
                return Text(b.Spans);
        }
    }

    FrameworkElement Item(HelpBlock b)
    {
        var g = new Grid { ColumnSpacing = 8, Margin = new Thickness(6 + 20 * Math.Max(0, b.Depth - 1), 0, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new TextBlock { Text = b.Marker, TextReadingOrder = TextReadingOrder.UseFlowDirection });
        var body = Text(b.Spans);
        Grid.SetColumn(body, 1);
        g.Children.Add(body);
        return g;
    }

    FrameworkElement Table(HelpBlock b)
    {
        int cols = b.Rows.Count == 0 ? 0 : b.Rows.Max(r => r.Count);
        var g = new Grid { Margin = new Thickness(0, 4, 0, 8) };
        for (int c = 0; c < cols; c++)
        {
            // columns get room by what they hold, within limits, so a short first column is not squeezed nor a long one given everything
            int longest = b.Rows.Where(r => c < r.Count).Max(r => r[c].Spans.Sum(s => s.Text.Length));
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(longest, 14, 70), GridUnitType.Star) });
        }
        for (int r = 0; r < b.Rows.Count; r++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < b.Rows[r].Count; c++)
            {
                var cell = b.Rows[r][c];
                var tb = Text(cell.Spans);
                tb.FontSize = 13;
                if (cell.Head) tb.FontWeight = FontWeights.SemiBold;
                var box = new Border { Style = (cell.Head ? HeadCell : BodyCell).Value, Padding = CellPadding, Child = tb };
                Grid.SetRow(box, r);
                Grid.SetColumn(box, c);
                g.Children.Add(box);
            }
        }
        return g;
    }

    TextBlock Text(List<HelpSpan> spans)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, TextReadingOrder = TextReadingOrder.UseFlowDirection, IsTextSelectionEnabled = true };
        var plain = new StringBuilder();
        var code = new List<TextRange>();
        foreach (var s in spans)
        {
            // an embedding keeps a path or a search example whole inside right-to-left text
            string text = s.Code && Loc.IsFa ? "‪" + s.Text + "‬" : s.Text;
            var run = new Run { Text = text };
            if (s.Bold) run.FontWeight = FontWeights.SemiBold;
            if (s.Italic) run.FontStyle = Windows.UI.Text.FontStyle.Italic;
            if (s.Code)
            {
                run.FontFamily = Mono;
                code.Add(new TextRange { StartIndex = plain.Length, Length = text.Length });
            }
            plain.Append(text);
            if (s.Link is string href)
            {
                var link = new Hyperlink();
                link.Inlines.Add(run);
                link.Click += (_, _) => Follow(href);
                tb.Inlines.Add(link);
            }
            else tb.Inlines.Add(run);
        }
        if (code.Count > 0)
        {
            // a highlighter paints its own text colour, which does not follow the theme
            var chips = new TextHighlighter
            {
                Background = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)),
                Foreground = new SolidColorBrush(Chrome.IsDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black),
            };
            foreach (var r in code) chips.Ranges.Add(r);
            tb.TextHighlighters.Add(chips);
        }
        Mark(tb, plain.ToString());
        return tb;
    }

    /// <summary>Paints the search matches of a block and remembers the first, to scroll to it.</summary>
    void Mark(TextBlock tb, string plain)
    {
        if (query.Length == 0) return;
        TextHighlighter? marks = null;
        for (int i = plain.IndexOf(query, StringComparison.OrdinalIgnoreCase); i >= 0; i = plain.IndexOf(query, i + query.Length, StringComparison.OrdinalIgnoreCase))
        {
            marks ??= new TextHighlighter { Background = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xC8, 0x3D)), Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black) };
            marks.Ranges.Add(new TextRange { StartIndex = i, Length = query.Length });
        }
        if (marks == null) return;
        tb.TextHighlighters.Add(marks);
        firstHit ??= tb;
    }

    // ---------- for the screen checks ----------
    internal string CurrentTopic => currentId ?? "";
    internal int TopicsShown => toc.Items.Count;
    internal void Find(string text)
    {
        search.Text = text;
        ApplySearch();
    }
}
