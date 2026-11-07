using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using MovieShelf.Core;
using Windows.UI;

namespace MovieShelf.App;

/// <summary>What the export dialog starts from. Pool is the current section (All, Movies or Series), the genre list counts it.</summary>
public sealed record ExportSource(List<Movie> All, List<Movie> Pool, List<Movie> View, List<Movie> Selected, List<string> ActiveLabels, string Group, string Sort, bool Reverse);

public sealed partial class MainWindow
{
    ExportSource ExportState()
    {
        var chosen = selecting ? Selected.Select(m => m.K).ToHashSet() : new HashSet<string>();
        return new ExportSource(all, pool, view, all.Where(m => chosen.Contains(m.K)).ToList(), ActiveLabels(), groupKey, sortKey, reverse);
    }

    /// <summary>Opens the export dialog on the library, the current results and the selection. Both Export buttons call this.</summary>
    public Task OpenExport() => Dialogs.Export(this, ExportState());

    static string Trim(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    static string FmtVotes(double v) => v >= 1e6 ? (v / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + "M"
        : v >= 1e3 ? Math.Round(v / 1e3, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "k" : Trim(v);

    // The filters in use, as the web app lists them above the results. The labels the web app translates are translated here too,
    // and they reach the Export info sheet as they are.
    List<string> ActiveLabels()
    {
        var l = Exporter.SearchTags(SearchBox.Text);
        if (person is { } p) l.Add(p.Name);
        var runtimeLabel = new Dictionary<string, string> { ["short"] = "< 60m", ["90"] = "60–90m", ["120"] = "90–120m", ["150"] = "2–2.5h", ["long"] = "> 2.5h" };
        IEnumerable<string> On(List<FilterOption> o, Func<FilterOption, string> text) => o.Where(x => x.IsChecked).Select(text);
        l.AddRange(On(status, o => Loc.T(o.Label)));
        l.AddRange(On(genre, o => o.Key));
        l.AddRange(On(decade, o => o.Key + "s"));
        l.AddRange(On(runtime, o => runtimeLabel.GetValueOrDefault(o.Key, o.Key)));
        l.AddRange(On(res, o => o.Key));
        l.AddRange(On(src, o => o.Key));
        l.AddRange(On(tech, o => Loc.T(o.Label)));
        l.AddRange(On(type, o => Loc.T(o.Label)));
        double yMin = double.IsNaN(YearMin.Value) ? 0 : YearMin.Value, yMax = double.IsNaN(YearMax.Value) ? 0 : YearMax.Value;
        if (yMin != 0 || yMax != 0) l.Add($"{(yMin != 0 ? Trim(yMin) : "…")}–{(yMax != 0 ? Trim(yMax) : "…")}");
        if (RatingMin.Value > 0) l.Add($"IMDb ≥ {Trim(RatingMin.Value)}");
        if (VotesMin.Value > 0) l.Add($"votes ≥ {FmtVotes(Math.Pow(10, VotesMin.Value))}");
        if (RtMin.Value > 0) l.Add($"Rotten Tomatoes ≥ {Trim(RtMin.Value)}%");
        if (McMin.Value > 0) l.Add($"Metacritic ≥ {Trim(McMin.Value)}");
        if (country.Length > 0) l.Add(country);
        if (language.Length > 0) l.Add(language);
        return l;
    }

    static IEnumerable<T> Under<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var x in Under<T>(c)) yield return x;
        }
    }

    static void Press(ButtonBase b)
    {
        if (FrameworkElementAutomationPeer.CreatePeerForElement(b) is IInvokeProvider p) p.Invoke();
    }

    // Drives the dialog through its controls for MOVIESHELF_SHOT: pictures in both languages, and one export from the Export button to a file.
    async Task ExportShots(string dir, string log)
    {
        void Log(string s) => File.AppendAllText(log, "export: " + s + "\n");
        async Task<(ContentDialog Dlg, Task Open, FrameworkElement Whole)> Show()
        {
            var dlg = Dialogs.BuildExport(this, ExportState());
            var open = dlg.ShowAsync().AsTask();
            await Task.Delay(1500);
            var whole = VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Select(p => p.Child).OfType<FrameworkElement>()
                .SelectMany(Under<FrameworkElement>).FirstOrDefault(e => e.Name == "BackgroundElement") ?? (FrameworkElement)dlg.Content;
            return (dlg, open, whole);
        }
        async Task Shoot(string name, FrameworkElement el)
        {
            await Task.Delay(400);
            await Shot(dir, name, el);
            Log(name + " ok");
        }
        string Sum(ContentDialog d) => Under<TextBlock>((FrameworkElement)d.Content).First(t => t.Name == "ExportSummary").Text;

        groupKey = "genre";
        SearchBox.Text = "year:>1990 -zombie";
        foreach (var g in genre.Take(1)) g.IsChecked = true;
        Apply();
        SetSelecting(true);
        Apply();
        await Task.Delay(800);
        for (int i = 0; i < 3 && i < ActiveList.Items.Count; i++) ActiveList.SelectedItems.Add(ActiveList.Items[i]);
        var (d1, o1, w1) = await Show();
        await Shoot("18-export-selected", w1);
        Log(Sum(d1));
        d1.Hide();
        await o1;
        SetSelecting(false);
        Apply();

        var (d2, o2, w2) = await Show();
        await Shoot("19-export-view", w2);
        var boxes = Under<TextBox>((FrameworkElement)d2.Content).ToList();
        var radios = Under<RadioButton>((FrameworkElement)d2.Content).ToList();
        var buttons = Under<Button>((FrameworkElement)d2.Content).ToList();
        var links = Under<HyperlinkButton>((FrameworkElement)d2.Content).ToList();

        // the longest gap between frames while "typing" one character per 40 ms into the search box, over the whole library
        radios.First(r => (string)r.Tag == "all").IsChecked = true;
        await Task.Delay(500);
        Log("whole library: " + Sum(d2));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double worst = 0, last = 0;
        void Frame(object? s, object e)
        {
            double t = sw.Elapsed.TotalMilliseconds;
            worst = Math.Max(worst, t - last);
            last = t;
        }
        CompositionTarget.Rendering += Frame;
        foreach (char c in "the matrix reloaded and revolutions")
        {
            boxes[0].Text += c;
            await Task.Delay(40);
        }
        CompositionTarget.Rendering -= Frame;
        await Task.Delay(600);
        Log($"typing: longest frame gap {worst:0} ms, then {Sum(d2)}");
        boxes[0].Text = "";
        boxes[1].Text = "1990";
        boxes[2].Text = "2000";
        boxes[3].Text = "7.5";
        await Task.Delay(600);
        Log("1990-2000 at 7.5: " + Sum(d2));
        Press(buttons.First(b => b.Content as string == Loc.T("Everything")));
        await Task.Delay(300);
        Log("Everything: " + Sum(d2));
        Press(buttons.First(b => b.Content as string == Loc.T("Clear")));
        await Task.Delay(300);
        Log($"Clear: {Sum(d2)}, export enabled {d2.IsPrimaryButtonEnabled}");
        Press(buttons.First(b => b.Content as string == Loc.T("Essentials")));
        await Task.Delay(300);
        Log("Essentials: " + Sum(d2));
        Press(links[0]);
        await Task.Delay(300);
        Log("Basic all/none: " + Sum(d2));
        Press(links[0]);
        await Task.Delay(300);
        Log("Basic all/none again: " + Sum(d2));
        radios.First(r => (string)r.Tag == "file").IsChecked = true;
        radios.First(r => (string)r.Tag == "csv").IsChecked = true;
        await Task.Delay(300);
        Log($"file rows csv: {Sum(d2)}, split enabled {Under<CheckBox>((FrameworkElement)d2.Content).First(c => c.Name == "ExportSplit").IsEnabled}");
        await Shoot("20-export-driven", w2);
        radios.First(r => (string)r.Tag == "xlsx").IsChecked = true;
        Under<CheckBox>((FrameworkElement)d2.Content).First(c => c.Name == "ExportSplit").IsChecked = true;
        boxes[1].Text = boxes[2].Text = boxes[3].Text = "";
        await Task.Delay(400);

        // the Export button itself, with the file picker replaced by a path
        Dialogs.ShotSavePath = Path.Combine(dir, "export-driven.xlsx");
        var go = Under<Button>(w2).First(b => b.Name == "PrimaryButton");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Press(go);
        await o2;
        Log($"Export button: dialog closed after {clock.ElapsedMilliseconds} ms, file {new FileInfo(Dialogs.ShotSavePath).Length / 1024} KB");
        Dialogs.ShotSavePath = null;

        // the choices come back
        var (d3, o3, w3) = await Show();
        Log("reopened: " + Sum(d3));
        await Shoot("21-export-remembered", w3);
        d3.Hide();
        await o3;

        Loc.IsFa = true;
        ApplyLanguage();
        var (d4, o4, w4) = await Show();
        await Shoot("22-export-fa", w4);
        d4.Hide();
        await o4;
        Loc.IsFa = false;
        ApplyLanguage();

        string theme = Chrome.Theme;
        Chrome.SetTheme("dark");
        var (d5, o5, w5) = await Show();
        await Shoot("23-export-dark", w5);
        d5.Hide();
        await o5;
        Chrome.SetTheme(theme);
        SavePrefs();
        Log("kept after SavePrefs: " + File.ReadAllText(prefsPath).Contains("\"export\""));
    }
}

public static partial class Dialogs
{
    /// <summary>Set by the MOVIESHELF_SHOT run: the path the file picker would have returned.</summary>
    internal static string? ShotSavePath;

    static string ExportPrefsPath => Path.Combine(App.Lib.DataDir, "native_ui.json");

    // Python keeps these in the browser's localStorage, which the native app cannot reach, so they live in native_ui.json in the same shape.
    static JsonNode? ReadExportPrefs()
    {
        try { return File.Exists(ExportPrefsPath) ? JsonNode.Parse(File.ReadAllText(ExportPrefsPath))?["export"] : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    static void WriteExportPrefs(IEnumerable<string> cols, string scope, string rows, string fmt, bool split)
    {
        try
        {
            var o = (File.Exists(ExportPrefsPath) ? JsonNode.Parse(File.ReadAllText(ExportPrefsPath)) as JsonObject : null) ?? new JsonObject();
            var c = new JsonArray();
            foreach (var k in cols) c.Add(k);
            o["export"] = new JsonObject { ["cols"] = c, ["opts"] = new JsonObject { ["scope"] = scope, ["rows"] = rows, ["fmt"] = fmt, ["split"] = split } };
            File.WriteAllText(ExportPrefsPath, o.ToJsonString());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }

    /// <summary>The main window rewrites native_ui.json when it closes; this carries the remembered export choices over.</summary>
    internal static void KeepExportPrefs(string path, JsonObject into)
    {
        try
        {
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path))?["export"] is { } e) into["export"] = e.DeepClone();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }

    // the app's theme can differ from the Windows one, and Application.Current.Resources follows Windows
    static Brush Line(byte alpha) => new SolidColorBrush(Chrome.IsDark ? Color.FromArgb(alpha, 255, 255, 255) : Color.FromArgb(alpha, 0, 0, 0));

    static TextBlock ExText(string text, double size = 14, bool semi = false, bool muted = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (semi) t.FontWeight = FontWeights.SemiBold;
        if (muted) t.Opacity = 0.7;
        return t;
    }

    static UIElement Labelled(string label, FrameworkElement control)
    {
        var s = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Top };
        s.Children.Add(ExText(Loc.T(label), 13, true));
        s.Children.Add(control);
        return s;
    }

    static Grid Row3(params UIElement[] cells)
    {
        var g = new Grid { ColumnSpacing = 10 };
        for (int i = 0; i < cells.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn((FrameworkElement)cells[i], i);
            g.Children.Add(cells[i]);
        }
        return g;
    }

    static ComboBox ExCombo(IEnumerable<(string Label, string Tag)> items)
    {
        var c = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (l, t) in items) c.Items.Add(new ComboBoxItem { Content = l, Tag = t });
        c.SelectedIndex = 0;
        return c;
    }

    static TextBox NumberEntry()
    {
        var t = new TextBox { PlaceholderText = Loc.T("Any") };
        t.BeforeTextChanging += (_, e) => e.Cancel = e.NewText.Any(c => !char.IsDigit(c) && c is not ('.' or '-' or '٫'));
        return t;
    }

    static double NumberOf(TextBox t)
    {
        string s = new string(t.Text.Select(c => char.IsDigit(c) ? (char)('0' + (int)char.GetNumericValue(c)) : c == '٫' ? '.' : c).ToArray());
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    static string Tag(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    /// <summary>A choice shown as a bordered row with a line of explanation, the look of the web dialog.</summary>
    static (RadioButton Radio, Border Card) Choice(string group, string value, string title, string sub, bool on)
    {
        var text = new StackPanel();
        text.Children.Add(ExText(Loc.T(title), 14, true));
        text.Children.Add(ExText(sub, 12, false, true));
        var r = new RadioButton { GroupName = group, Tag = value, IsChecked = on, Content = text, MinWidth = 0, MinHeight = 0, VerticalAlignment = VerticalAlignment.Center };
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 0, 8), Child = r };
        void Paint() => card.BorderBrush = Line(r.IsChecked == true ? (byte)0xE0 : (byte)0x24);
        Paint();
        r.Checked += (_, _) => Paint();
        r.Unchecked += (_, _) => Paint();
        card.Tapped += (_, _) => r.IsChecked = true;
        return (r, card);
    }

    static string GroupName(string group) => group switch
    {
        "decade" => "Decade", "year" => "Year", "genre" => "Genre", "rating" => "IMDb rating", "res" => "Quality", "type" => "Type", "letter" => "Title A–Z", _ => "Watched",
    };

    /// <summary>Builds the dialog; <see cref="Export"/> shows it, and the MOVIESHELF_SHOT run shows it to take its picture.</summary>
    internal static ContentDialog BuildExport(MainWindow main, ExportSource src)
    {
        var root = main.Content.XamlRoot;
        var dlg = NewDialog(root, "Export movies");
        dlg.Resources["ContentDialogMaxWidth"] = 1080.0;
        dlg.Resources["ContentDialogMaxHeight"] = 1400.0;
        dlg.PrimaryButtonText = Loc.T("Export");
        dlg.CloseButtonText = Loc.T("Cancel");
        dlg.SecondaryButtonText = Loc.T("Help");
        dlg.SecondaryButtonClick += (_, e) =>
        {
            e.Cancel = true;   // the dialog stays open beside the guide
            HelpWindow.Open("export");
        };
        // Enter in a text box must not start an export
        dlg.DefaultButton = ContentDialogButton.None;
        dlg.PrimaryButtonStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
        dlg.IsPrimaryButtonEnabled = false;
        if (main.Content is FrameworkElement fe) dlg.RequestedTheme = fe.RequestedTheme;

        var saved = ReadExportPrefs();
        var opts = saved?["opts"];
        string scopeNow = (string?)opts?["scope"] ?? "view", rowsNow = (string?)opts?["rows"] ?? "movie", fmtNow = (string?)opts?["fmt"] ?? "xlsx";
        bool splitNow = (bool?)opts?["split"] ?? false;
        if (src.Selected.Count > 0) scopeNow = "sel";
        else if (scopeNow == "sel") scopeNow = "view";
        var colsNow = saved?["cols"] is JsonArray ca
            ? ca.Select(x => (string?)x ?? "").ToHashSet()
            : Exporter.Columns.Where(c => c.Default).Select(c => c.Key).ToHashSet();
        bool filtered = src.View.Count != src.All.Count;

        // ---------- which movies ----------
        var left = new StackPanel { Spacing = 20 };
        var which = new StackPanel();
        which.Children.Add(new Border { Margin = new Thickness(0, 0, 0, 10), Child = ExText(Loc.T("Which movies"), 15, true) });
        var scopes = new List<RadioButton>();
        void AddScope(string value, string title, string sub)
        {
            var (r, card) = Choice("exscope", value, title, sub, scopeNow == value);
            scopes.Add(r);
            which.Children.Add(card);
        }
        string Fmt(string key, int n) => string.Format(Loc.T(key), n.ToString("N0"));
        if (src.Selected.Count > 0) AddScope("sel", "Selected movies", Fmt("{0} movies you selected", src.Selected.Count));
        AddScope("view", "Current results", Fmt(filtered ? "{0} movies, with your filters and search, in the order shown" : "{0} movies, no filters applied", src.View.Count));
        if (filtered && src.ActiveLabels.Count > 0)
        {
            var tags = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6, Margin = new Thickness(4, -2, 4, 10) };
            foreach (var l in src.ActiveLabels)
                tags.Children.Add(new Border
                {
                    Padding = new Thickness(8, 2, 8, 3), CornerRadius = new CornerRadius(10), Background = Line(0x10),
                    Child = new TextBlock { Text = l, FontSize = 12 },
                });
            which.Children.Add(tags);
        }
        AddScope("all", "Whole library", Fmt("{0} movies", src.All.Count));

        // ---------- narrow down further ----------
        var narrow = new StackPanel { Spacing = 12, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(0, 14, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Line(0x24) };
        narrow.Children.Add(ExText(Loc.T("Narrow down further"), 14, true));
        var query = new TextBox { TextReadingOrder = TextReadingOrder.DetectFromContent };
        var hint = new StackPanel { Spacing = 2 };
        hint.Children.Add(ExText(Loc.T("Same as the search box:"), 12, false, true));
        hint.Children.Add(new TextBlock { Text = "genre:horror year:>2000 -zombie", FontSize = 12, FontFamily = new FontFamily("Consolas"), Opacity = 0.7, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left });
        var querySet = new StackPanel { Spacing = 6 };
        querySet.Children.Add(ExText(Loc.T("Search"), 13, true));
        querySet.Children.Add(hint);
        querySet.Children.Add(query);
        narrow.Children.Add(querySet);
        TextBox yMin = NumberEntry(), yMax = NumberEntry(), rMin = NumberEntry();
        narrow.Children.Add(Row3(Labelled("Year from", yMin), Labelled("Year to", yMax), Labelled("IMDb rating at least", rMin)));
        var genres = src.Pool.SelectMany(m => m.Genres).GroupBy(g => g).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => ($"{g.Key} ({g.Count()})", g.Key));
        var genreBox = ExCombo(new[] { (Loc.T("Any"), "") }.Concat(genres));
        string[] resOrder = { "2160p", "1080p", "720p", "576p", "480p", "SD" };
        var resBox = ExCombo(new[] { (Loc.T("Any"), "") }.Concat(resOrder.Where(r => src.All.Any(m => m.Files.Any(f => f.Res == r))).Select(r => (r, r))));
        var statusBox = ExCombo(new[] { (Loc.T("All"), ""), (Loc.T("Favorites"), "fav"), (Loc.T("Watched"), "watched"), (Loc.T("Not watched"), "unwatched"), (Loc.T("Needs review"), "review") });
        narrow.Children.Add(Row3(Labelled("Genre", genreBox), Labelled("Quality", resBox), Labelled("Show", statusBox)));
        which.Children.Add(narrow);
        left.Children.Add(which);

        // ---------- layout and file type ----------
        var layout = new StackPanel();
        layout.Children.Add(new Border { Margin = new Thickness(0, 0, 0, 10), Child = ExText(Loc.T("Layout"), 15, true) });
        var (perMovie, movieCard) = Choice("exrows", "movie", "One row per movie", Loc.T("Files of the same movie are joined in one cell"), rowsNow != "file");
        var (perFile, fileCard) = Choice("exrows", "file", "One row per file", Loc.T("Movies with several files get several rows"), rowsNow == "file");
        layout.Children.Add(movieCard);
        layout.Children.Add(fileCard);
        var splitText = new StackPanel();
        splitText.Children.Add(ExText(Loc.T("One Excel sheet per group")));
        splitText.Children.Add(ExText(string.Format(Loc.T("Using your current grouping ({0})"), Loc.T(GroupName(src.Group))), 12, false, true));
        var split = new CheckBox { Name = "ExportSplit", Content = splitText, IsChecked = splitNow, Visibility = src.Group == "none" ? Visibility.Collapsed : Visibility.Visible };
        layout.Children.Add(split);
        left.Children.Add(layout);

        var fileType = new StackPanel();
        fileType.Children.Add(new Border { Margin = new Thickness(0, 0, 0, 6), Child = ExText(Loc.T("File type"), 15, true) });
        var xlsx = new RadioButton { GroupName = "exfmt", Tag = "xlsx", Content = Loc.T("Excel (.xlsx)"), IsChecked = fmtNow != "csv", MinWidth = 0 };
        var csv = new RadioButton { GroupName = "exfmt", Tag = "csv", Content = Loc.T("CSV"), IsChecked = fmtNow == "csv", MinWidth = 0 };
        var seg = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        seg.Children.Add(xlsx);
        seg.Children.Add(csv);
        fileType.Children.Add(seg);
        left.Children.Add(fileType);

        // ---------- columns ----------
        var right = new StackPanel();
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 10) };
        var nCols = new TextBlock { FontSize = 12, FontWeight = FontWeights.Medium, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
        legend.Children.Add(ExText(Loc.T("Columns"), 15, true));
        legend.Children.Add(new Border { Padding = new Thickness(8, 1, 8, 2), CornerRadius = new CornerRadius(10), Background = Line(0x10), Child = nCols });
        right.Children.Add(legend);
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 14) };
        right.Children.Add(presets);
        var boxes = new List<(CheckBox Box, Exporter.Column Col)>();
        Action colsChanged = () => { };
        foreach (var group in Exporter.Columns.Select(c => c.Group).Distinct())
        {
            var head = new Grid { BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Line(0x24), Padding = new Thickness(0, 0, 0, 2), Margin = new Thickness(0, 0, 0, 6) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = ExText(Loc.T(group), 13, true, true);
            title.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);
            var toggle = new HyperlinkButton { Content = Loc.T("All / none"), Padding = new Thickness(4, 0, 4, 0), FontSize = 12 };
            Grid.SetColumn(toggle, 1);
            head.Children.Add(toggle);
            right.Children.Add(head);
            var grid = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var inGroup = Exporter.Columns.Where(c => c.Group == group).ToList();
            var mine = new List<CheckBox>();
            for (int i = 0; i < inGroup.Count; i++)
            {
                if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var box = new CheckBox { Content = Loc.T(inGroup[i].Label), IsChecked = colsNow.Contains(inGroup[i].Key), MinWidth = 0, MinHeight = 30, Padding = new Thickness(6, 4, 0, 0) };
                Grid.SetRow(box, i / 2);
                Grid.SetColumn(box, i % 2);
                grid.Children.Add(box);
                boxes.Add((box, inGroup[i]));
                mine.Add(box);
                box.Click += (_, _) => colsChanged();
            }
            toggle.Click += (_, _) =>
            {
                bool on = mine.All(b => b.IsChecked == true);
                foreach (var b in mine) b.IsChecked = !on;
                colsChanged();
            };
            right.Children.Add(grid);
        }
        void Preset(string label, Func<Exporter.Column, bool> on)
        {
            var b = new Button { Content = Loc.T(label), Padding = new Thickness(11, 3, 11, 4) };
            b.Click += (_, _) =>
            {
                foreach (var (box, col) in boxes) box.IsChecked = on(col);
                colsChanged();
            };
            presets.Children.Add(b);
        }
        Preset("Essentials", c => c.Default);
        Preset("Everything", _ => true);
        Preset("Clear", _ => false);

        // ---------- the page ----------
        bool narrowWindow = root.Size.Width < 900;
        var columns = new Grid { ColumnSpacing = 32, RowSpacing = 20, Margin = new Thickness(0, 0, 16, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        columns.Children.Add(left);
        if (narrowWindow)
        {
            columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(right, 1);
        }
        else
        {
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(right, 1);
        }
        columns.Children.Add(right);

        var sum = new TextBlock { Name = "ExportSummary", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(sum, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var bar = new ProgressBar { Visibility = Visibility.Collapsed, Minimum = 0 };
        var page = new Grid { RowSpacing = 8, MinWidth = Math.Min(980, root.Size.Width - 96) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scroller = new ScrollViewer { Content = columns, MaxHeight = Math.Max(240, root.Size.Height - 260) };
        page.Children.Add(scroller);
        Grid.SetRow(sum, 1);
        page.Children.Add(sum);
        Grid.SetRow(bar, 2);
        page.Children.Add(bar);
        dlg.Content = page;

        // ---------- state ----------
        string Scope() => (string)scopes.First(r => r.IsChecked == true).Tag;
        bool FileRows() => perFile.IsChecked == true;
        string Format() => csv.IsChecked == true ? "csv" : "xlsx";
        List<string> Cols() => boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Col.Key).ToList();
        ExportFilter Filter() => new(query.Text.Trim(), NumberOf(yMin), NumberOf(yMax), NumberOf(rMin), Tag(genreBox), Tag(resBox), Tag(statusBox));
        List<Movie> Source(string scope) => scope == "sel" ? src.Selected : scope == "all" ? src.All : src.View;

        int version = 0, lastMovies = 0, lastRows = 0;
        CancellationTokenSource? job = null;

        void ShowSummary()
        {
            int n = boxes.Count(b => b.Box.IsChecked == true);
            nCols.Text = string.Format(Loc.T("{0} selected"), n.ToString("N0"));
            if (job != null) return;
            sum.Text = string.Format(Loc.T("{0} movies · {1} rows × {2} columns"), lastMovies.ToString("N0"), lastRows.ToString("N0"), n.ToString("N0"));
            dlg.IsPrimaryButtonEnabled = lastRows > 0 && n > 0;
        }

        // the counting runs off the UI thread and only the newest answer is shown, so typing never waits for it
        async void Recount()
        {
            int mine = ++version;
            var list = Source(Scope());
            var filter = Filter();
            bool each = FileRows();
            var (movies, rows) = await Task.Run(() =>
            {
                var found = Exporter.Narrow(list, filter);
                return (found.Count, Exporter.RowCount(found, each));
            });
            if (mine != version) return;
            lastMovies = movies;
            lastRows = rows;
            ShowSummary();
        }

        colsChanged = ShowSummary;
        var typing = main.DispatcherQueue.CreateTimer();
        typing.Interval = TimeSpan.FromMilliseconds(120);
        typing.IsRepeating = false;
        typing.Tick += (_, _) => Recount();
        foreach (var t in new[] { query, yMin, yMax, rMin })
            t.TextChanged += (_, _) =>
            {
                typing.Stop();
                typing.Start();
            };
        foreach (var c in new[] { genreBox, resBox, statusBox }) c.SelectionChanged += (_, _) => Recount();
        foreach (var r in scopes.Append(perMovie).Append(perFile)) r.Checked += (_, _) => Recount();
        void SyncSplit() => split.IsEnabled = csv.IsChecked != true;
        xlsx.Checked += (_, _) => SyncSplit();
        csv.Checked += (_, _) => SyncSplit();
        SyncSplit();
        ShowSummary();
        Recount();

        void Reset()
        {
            job = null;
            dlg.PrimaryButtonText = Loc.T("Export");
            bar.Visibility = Visibility.Collapsed;
            ShowSummary();
        }

        async void Go()
        {
            var cols = Cols();
            string scope = Scope(), fmt = Format();
            bool each = FileRows(), sheets = split.IsChecked == true && split.Visibility == Visibility.Visible;
            var filter = Filter();
            var token = new CancellationTokenSource();
            job = token;
            string temp = Path.Combine(Path.GetTempPath(), $"MovieShelf-export-{Guid.NewGuid():N}.tmp");
            WriteExportPrefs(cols, scope, each ? "file" : "movie", fmt, split.IsChecked == true);
            dlg.IsPrimaryButtonEnabled = false;
            dlg.PrimaryButtonText = Loc.T("Preparing…");
            bar.Visibility = Visibility.Visible;
            try
            {
                var list = await Task.Run(() => Exporter.Narrow(Source(scope), filter));
                var req = new ExportRequest
                {
                    Movies = list, Columns = cols, PerFile = each, Format = fmt,
                    Scope = scope == "all" ? "Whole library" : scope == "sel" ? "Selected movies" : "Current results",
                    Filters = (scope == "view" ? src.ActiveLabels : new List<string>()).Concat(Exporter.FilterLines(filter)).ToList(),
                };
                if (sheets && fmt == "xlsx") req.Sheets = Exporter.GroupList(list, src.Group, src.Sort, src.Reverse);
                int total = Exporter.RowCount(list, each);
                bar.Maximum = Math.Max(1, total);
                var progress = new Progress<int>(n =>
                {
                    bar.Value = n;
                    if (job != null) sum.Text = string.Format(Loc.T("Preparing… {0} of {1}"), n.ToString("N0"), total.ToString("N0"));
                });
                await Task.Run(() => Exporter.Build(temp, req, progress, token.Token));
                string name = Exporter.FileName(fmt, DateTime.Now);
                string? path = ShotSavePath ?? (await WinKit.SaveFile(main, Path.GetFileNameWithoutExtension(name), fmt == "xlsx" ? "Excel workbook" : "CSV file", "." + fmt))?.Path;
                if (token.IsCancellationRequested) return;
                if (path == null)
                {
                    Reset();
                    sum.Text = Loc.T("Cancelled");
                    return;
                }
                await Task.Run(() => File.Move(temp, path, true));
                job = null;
                main.ShowInfo(string.Format(Loc.T("Saved to {0}"), path));
                dlg.Hide();
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Reset();
                sum.Text = e.Message;
            }
            finally
            {
                try { File.Delete(temp); } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // the dialog stays open while the file is prepared and while Save As is up, and Cancel stops the work
        dlg.PrimaryButtonClick += (_, e) =>
        {
            e.Cancel = true;
            if (job == null) Go();
        };
        dlg.Closing += (_, _) =>
        {
            typing.Stop();
            version++;
            job?.Cancel();
        };
        return dlg;
    }

    /// <summary>The Export dialog: which movies, how they are laid out and which columns.</summary>
    public static async Task Export(MainWindow main, ExportSource src) => await BuildExport(main, src).ShowAsync();
}
