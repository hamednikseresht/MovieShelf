using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieShelf.Core;

namespace MovieShelf.App;

public static partial class Dialogs
{
    /// <summary>Settings, at a tab when given: general, appearance, online, maintenance or log ("View log" opens the last).</summary>
    public static Task Settings(MainWindow main, string? tab = null)
    {
        SettingsWindow.Open(main, tab);
        return Task.CompletedTask;
    }
}

/// <summary>Every setting and maintenance action of the Python app, on its tabs. A change is saved as it is made, as in Python. The window is modal: it opens in the middle
/// of the main window, which stays blurred behind it, and a running job can be followed on its own job strip. It shows at once; settings, numbers and sizes are read off
/// the UI thread and fill in afterwards.</summary>
internal sealed class SettingsWindow : Window
{
    const int Width = 920, Height = 860, OwnerIndex = -8;   // GWLP_HWNDPARENT

    static SettingsWindow? instance;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    public static void Open(MainWindow main, string? tab)
    {
        if (instance == null)
        {
            instance = new SettingsWindow(main, tab);
            instance.ShowOverMain();
        }
        else if (tab != null) instance.ShowTab(tab);
        instance.Activate();
    }

    void ShowOverMain()
    {
        SetWindowLongPtr(WinRT.Interop.WindowNative.GetWindowHandle(this), OwnerIndex, WinRT.Interop.WindowNative.GetWindowHandle(main));
        if (AppWindow.Presenter is OverlappedPresenter p) p.IsMinimizable = p.IsMaximizable = p.IsResizable = false;
        double scale = (main.Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1;
        var work = DisplayArea.GetFromWindowId(main.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int w = Math.Min((int)(Width * scale), work.Width), h = Math.Min((int)(Height * scale), work.Height);
        var m = main.AppWindow;
        int x = Math.Clamp(m.Position.X + (m.Size.Width - w) / 2, work.X, work.X + work.Width - w);
        int y = Math.Clamp(m.Position.Y + (m.Size.Height - h) / 2, work.Y, work.Y + work.Height - h);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));
        main.SetModal(true);
    }

    static readonly (string Key, string Label)[] TabDefs =
    {
        ("general", "General"), ("appearance", "Appearance"), ("online", "Online data"), ("maintenance", "Maintenance"), ("log", "Activity log"),
    };

    readonly Library lib = App.Lib;
    readonly MainWindow main;
    readonly Grid root = new();
    readonly TitleBar bar = new() { IsBackButtonVisible = false, IsPaneToggleButtonVisible = false };
    readonly SelectorBar tabs = new();
    readonly ScrollViewer scroll = new() { HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly InfoBar info = new() { IsOpen = false, IsClosable = true, Margin = new Thickness(16, 4, 16, 8) };
    readonly DispatcherQueueTimer infoTimer;
    readonly Dictionary<string, FrameworkElement> panes = new();
    TextBlock? log;
    Settings? s;
    string tab;
    bool filling;
    List<string>? logShown;

    SettingsWindow(MainWindow main, string? tab)
    {
        this.main = main;
        this.tab = TabDefs.Any(t => t.Key == tab) ? tab! : "general";
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(bar);
        var tabHost = new ScrollViewer
        {
            Content = tabs, Padding = new Thickness(16, 0, 16, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled,
        };
        Grid.SetRow(tabHost, 1);
        root.Children.Add(tabHost);
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        Grid.SetRow(info, 3);
        root.Children.Add(info);
        var job = WinKit.JobStrip(this);
        Grid.SetRow(job, 4);
        root.Children.Add(job);
        Content = root;
        WinKit.Setup(this, bar, "Settings", Width, Height, Relocalise, "settings");
        WinKit.Direction(root);

        infoTimer = DispatcherQueue.CreateTimer();
        infoTimer.Interval = TimeSpan.FromSeconds(6);
        infoTimer.IsRepeating = false;
        infoTimer.Tick += (_, _) => info.IsOpen = false;
        filling = true;
        foreach (var (key, label) in TabDefs)
        {
            var item = new SelectorBarItem { Text = Loc.T(label), Tag = key };
            tabs.Items.Add(item);
            if (key == this.tab) tabs.SelectedItem = item;
        }
        filling = false;
        tabs.SelectionChanged += OnTabChanged;
        lib.JobChanged += OnJob;
        Closed += (_, _) =>
        {
            lib.JobChanged -= OnJob;
            instance = null;
            main.SetModal(false);
            main.Activate();
        };
        _ = LoadAsync();
    }

    async Task LoadAsync()
    {
        try { s = await Task.Run(lib.LoadSettings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Say(e.Message, InfoBarSeverity.Error);
            return;
        }
        ShowPane();
    }

    void Relocalise()
    {
        WinKit.Direction(root);
        filling = true;
        foreach (var item in tabs.Items.Cast<SelectorBarItem>()) item.Text = Loc.T(TabDefs.First(t => t.Key == (string)item.Tag).Label);
        filling = false;
        panes.Clear();
        info.IsOpen = false;
        if (s != null) ShowPane();
    }

    void ShowTab(string key)
    {
        if (!TabDefs.Any(t => t.Key == key)) return;
        tabs.SelectedItem = tabs.Items.First(i => (string)i.Tag == key);
    }

    void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (filling || sender.SelectedItem is not SelectorBarItem { Tag: string key }) return;
        tab = key;
        if (s != null) ShowPane();
    }

    /// <summary>A pane is built the first time it is shown, so the window opens without doing the work of the others.</summary>
    void ShowPane()
    {
        if (!panes.TryGetValue(tab, out var pane))
        {
            pane = tab switch
            {
                "appearance" => Appearance(),
                "online" => Online(),
                "maintenance" => Maintenance(),
                "log" => LogPane(),
                _ => General(),
            };
            pane = new Grid { Children = { pane } };   // a ScrollViewer misplaces a direct child that has a MaxWidth
            panes[tab] = pane;
        }
        scroll.Content = pane;
        scroll.ChangeView(null, 0, null, true);
        if (tab == "log") ShowLog();
    }

    // ---------- small pieces ----------
    static StackPanel Pane() => new() { Spacing = 10, Padding = new Thickness(24, 8, 24, 24), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Stretch };

    static TextBlock Heading(string text) => new() { Text = Loc.T(text), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], FontSize = 17, Margin = new Thickness(0, 12, 0, 0) };

    static TextBlock Label(string text) => new() { Text = Loc.T(text), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };

    static TextBlock Note(string text = "") => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75 };

    static Button Btn(string text, RoutedEventHandler click, bool accent = false)
    {
        var b = new Button { Content = Loc.T(text) };
        if (accent) b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        b.Click += click;
        return b;
    }

    static WrapPanel Row(params UIElement[] items)
    {
        var w = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        foreach (var i in items) w.Children.Add(i);
        return w;
    }

    /// <summary>A box that fills the row with a button at its end.</summary>
    static Grid Line(FrameworkElement box, FrameworkElement button)
    {
        var g = new Grid { ColumnSpacing = 8 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(box);
        Grid.SetColumn(button, 1);
        g.Children.Add(button);
        return g;
    }

    static void Add(Panel p, params UIElement[] els)
    {
        foreach (var e in els) p.Children.Add(e);
    }

    static string Fmt(string template, params object[] args) => string.Format(Loc.T(template), args);

    /// <summary>Keeps a path in reading order inside right-to-left text.</summary>
    static string Ltr(string path) => Loc.IsFa ? "\u202A" + path + "\u202C" : path;

    /// <summary>The short message Python shows as a toast. Errors stay until closed.</summary>
    void Say(string text, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        infoTimer.Stop();
        WinKit.Info(info, text, severity);
        if (severity != InfoBarSeverity.Error) infoTimer.Start();
    }

    async Task Change(Action<Settings> change, string? saved = "Setting saved")
    {
        if (s != null) change(s);
        try
        {
            await WinKit.SaveSettings(change);
            if (saved != null) Say(Loc.T(saved));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Say(e.Message, InfoBarSeverity.Error);
        }
    }

    CheckBox Toggle(string text, bool on, Action<Settings, bool> set)
    {
        var c = new CheckBox { Content = Loc.T(text), IsChecked = on };
        c.Click += (_, _) => { bool v = c.IsChecked == true; _ = Change(x => set(x, v)); };
        return c;
    }

    /// <summary>The usual way a job starts: at once, or queued behind the running one.</summary>
    void Act(string label, Func<bool> start)
    {
        try
        {
            bool started = start();
            Say(Fmt(started ? "{0}: started" : "{0}: queued, runs after the current job", Loc.T(label)));
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { Say(e.Message, InfoBarSeverity.Error); }
    }

    // ---------- general ----------
    FrameworkElement General()
    {
        var p = Pane();

        // library numbers, filled in once they are counted
        var tiles = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        string[] captions = { "list entries", "matched to IMDb", "unique titles", "posters", "with cast & crew", "with RT / Metacritic" };
        void Tiles(params int[] n)
        {
            tiles.Children.Clear();
            for (int i = 0; i < captions.Length; i++) tiles.Children.Add(U2.Tile(n.Length > i ? n[i].ToString("N0") : "…", Loc.T(captions[i])));
        }
        Tiles();
        var ratings = Note();
        var indexInfo = Note();
        Add(p, Heading("Library"), tiles);

        // Load list: the one way to bring movie names in
        var loadList = Btn("Load list…", async (_, _) =>
        {
            try
            {
                var r = await Dialogs.LoadList(this, Content.XamlRoot);
                if (r != null)
                    Say(Fmt(r.Moved > 0 ? "{0} names loaded. {1} old list files put aside in “previous lists”. Updating…" : "{0} names loaded. Updating…", r.Names.ToString("N0"), r.Moved));
            }
            catch (Exception e) when (e is InvalidOperationException or IOException) { Say(e.Message, InfoBarSeverity.Error); }
        });
        Add(p, Heading("Load list"),
            Note(Fmt("A .txt or .csv list, a folder of lists or a folder of movies. You see what is repeated or already in your library first, then choose to add to the current list or start a new one. Lists are kept in {0}.", Ltr(s!.ListDir))),
            Row(loadList));

        // list folder
        var folder = new TextBox { Text = s.ListDir, FlowDirection = FlowDirection.LeftToRight };
        void SaveFolder()
        {
            string d = folder.Text.Trim().Trim('"');
            if (d.Length > 0 && d != s!.ListDir) _ = Change(x => x.ListDir = d);
        }
        folder.LostFocus += (_, _) => SaveFolder();
        var browse = Btn("Browse", async (_, _) =>
        {
            try
            {
                if (await WinKit.PickFolder(this) is not string d) return;
                folder.Text = d;
                SaveFolder();
            }
            catch (System.Runtime.InteropServices.COMException e) { Say(e.Message, InfoBarSeverity.Error); }
        });
        Add(p, Label("List folder"), Line(folder, browse));

        // movie folders
        var media = new TextBox
        {
            Text = string.Join("\r", s.MediaDirs), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 84, MaxHeight = 160, PlaceholderText = @"D:\Movies", FlowDirection = FlowDirection.LeftToRight,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(media, ScrollBarVisibility.Auto);
        List<string> Dirs() => media.Text.Split('\r', '\n').Select(x => x.Trim().Trim('"')).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var addDir = Btn("Add a folder…", async (_, _) =>
        {
            try
            {
                if (await WinKit.PickFolder(this) is string d) { media.Text = media.Text.Trim().Length > 0 ? media.Text.TrimEnd() + "\r" + d : d; SaveDirs(); }
            }
            catch (System.Runtime.InteropServices.COMException e) { Say(e.Message, InfoBarSeverity.Error); }
        });
        // the box saves itself when you leave it; a changed set of folders is read at once
        async void SaveDirs()
        {
            var dirs = Dirs();
            if (dirs.SequenceEqual(s!.MediaDirs, StringComparer.OrdinalIgnoreCase)) return;
            await Change(x => x.MediaDirs = dirs, null);
            lib.StartRescan();
            Say(Loc.T(dirs.Count > 0 ? "Reading your movie folders…" : "Movie folders cleared"));
        }
        media.LostFocus += (_, _) => SaveDirs();
        Add(p, Heading("Movie folders"),
            Note(Loc.T("One folder per line. The app goes through them by itself, finds every movie and lists it; it saves and reads them when you leave the box. Movies only in a folder go away when their video does.")),
            media, Row(addDir));

        // ffprobe
        var probe = Toggle("Read audio languages, subtitles, length and real picture size with ffprobe", s.ProbeMedia, (x, v) => x.ProbeMedia = v);
        var ffinfo = Note();
        var probeNow = Btn("Read files now", (_, _) =>
        {
            lib.StartOrQueue(lib.ProbeMedia);
            Say(Loc.T("Reading the files with ffprobe…"));
        });
        probeNow.IsEnabled = false;
        var ffpath = new TextBox { Text = s.FfprobePath, FlowDirection = FlowDirection.LeftToRight };
        async Task ShowProbe()
        {
            try
            {
                var st = await Task.Run(lib.ProbeStatus);
                ffinfo.Text = st.Ffprobe != null
                    ? Fmt("ffprobe found. {0} of {1} files read", st.Read.ToString("N0"), st.Files.ToString("N0")) + (st.Failed > 0 ? ", " + Fmt("{0} unreadable", st.Failed.ToString("N0")) : "") + ". " + Loc.T("Only new or changed files are read on later updates.")
                    : Loc.T("ffprobe not found. Install ffmpeg (winget install Gyan.FFmpeg), then restart the app.");
                probeNow.IsEnabled = st.Ffprobe != null && st.Files > 0;
            }
            catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException) { ffinfo.Text = e.Message; }
        }
        ffpath.LostFocus += async (_, _) =>
        {
            string v = ffpath.Text.Trim().Trim('"');
            if (v == s!.FfprobePath) return;
            await Change(x => x.FfprobePath = v, null);
            await ShowProbe();
        };
        Add(p, probe, ffinfo, Label("ffprobe location (leave empty to find it automatically)"), ffpath, Row(probeNow));

        // automatic updates
        Add(p, Heading("Automatic updates"), Note(Loc.T("What the app does on its own in the background.")),
            Toggle("Download posters from IMDb", s.OnlinePosters, (x, v) => x.OnlinePosters = v),
            Toggle("Search IMDb online for titles not found offline", s.OnlineFallback, (x, v) => x.OnlineFallback = v),
            Toggle("Keep IMDb ratings fresh (weekly) and critic scores (monthly)", s.AutoRefresh, (x, v) => x.AutoRefresh = v),
            ratings, indexInfo);

        async Task Numbers()
        {
            try
            {
                // duplicates are counted first and outside the lock: they read through their own connection and take a while on a big library
                var (st, indexSize) = await Task.Run(() =>
                {
                    lib.OpenDuplicates();
                    LibraryStats x;
                    lock (lib.Lock) x = lib.GetLibraryStats();
                    return (x, File.Exists(lib.IndexPath) ? new FileInfo(lib.IndexPath).Length : 0);
                });
                Tiles(st.Files, st.Matched, st.Titles, st.Posters, st.Enriched, st.Scores);
                ratings.Text = st.RatingsAgeDays is not double d ? Loc.T("IMDb ratings are not downloaded yet.")
                    : d < 1 ? Loc.T("IMDb ratings are up to date today.") : Fmt("IMDb ratings are {0} days old.", Math.Round(d));
                indexInfo.Text = Fmt("The IMDb data (about 2 GB) is downloaded to {0}; the search index built from it is imdb_index.db in the folder above it.", Ltr(st.ImdbDir))
                    + (indexSize > 0 ? " " + Fmt("The index is {0}.", WinKit.Size(indexSize)) : "");
            }
            catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException) { ratings.Text = e.Message; }
        }
        _ = Numbers();
        _ = ShowProbe();
        return p;
    }

    // ---------- appearance ----------
    FrameworkElement Appearance()
    {
        var p = Pane();
        var theme = new ComboBox { Header = Loc.T("Theme"), MinWidth = 240 };
        foreach (var (tag, label) in new[] { ("light", "Light"), ("dark", "Dark"), ("system", "System") })
            theme.Items.Add(new ComboBoxItem { Content = Loc.T(label), Tag = tag });
        theme.SelectedIndex = Math.Max(0, new[] { "light", "dark", "system" }.ToList().IndexOf(Chrome.Theme));
        // the switch rebuilds this pane, so it waits until the box has finished its own event
        theme.SelectionChanged += (_, _) =>
        {
            if (theme.SelectedItem is ComboBoxItem { Tag: string t }) DispatcherQueue.TryEnqueue(() => main.SetTheme(t));
        };
        var lang = new ComboBox { Header = Loc.T("Language / زبان"), MinWidth = 240 };
        lang.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
        lang.Items.Add(new ComboBoxItem { Content = "فارسی", Tag = "fa" });
        lang.SelectedIndex = Loc.IsFa ? 1 : 0;
        lang.SelectionChanged += (_, _) =>
        {
            if (lang.SelectedItem is ComboBoxItem { Tag: string l }) DispatcherQueue.TryEnqueue(async () => await main.SetLanguage(l == "fa"));
        };
        var size = new ComboBox { Header = Loc.T("Poster size"), MinWidth = 240 };
        foreach (var (label, key) in new[] { ("Small", "s"), ("Medium", "m"), ("Large", "l") })
            size.Items.Add(new ComboBoxItem { Content = Loc.T(label), Tag = key });
        size.SelectedIndex = main.PosterSize switch { "s" => 0, "l" => 2, _ => 1 };
        size.SelectionChanged += (_, _) =>
        {
            if (size.SelectedItem is ComboBoxItem { Tag: string k }) main.SetPosterSize(k);
        };
        Add(p, Heading("Appearance"), Note(Loc.T("Saved on this computer.")), theme, Note(Loc.T("Light, dark, or follow Windows.")),
            lang, Note(Loc.T("Menus and Help can be shown in English or Persian (فارسی).")), size);
        return p;
    }

    // ---------- online data ----------
    FrameworkElement Online()
    {
        var p = Pane();
        Grid Key(PasswordBox box, Func<string, Task<KeyTest>> test, TextBlock msg, Action<Settings, string> set, string okText)
        {
            var b = new Button { Content = Loc.T("Test & save"), VerticalAlignment = VerticalAlignment.Top };
            b.Click += async (_, _) =>
            {
                string key = box.Password.Trim();
                if (key.Length == 0) return;
                b.IsEnabled = false;
                msg.Text = Loc.T("Testing…");
                try
                {
                    var r = await test(key);
                    if (r.Ok)
                    {
                        await Change(x => set(x, key), "Key saved");
                        panes.Remove("maintenance");    // it shows whether a TMDB key is set
                        msg.Text = Loc.T(okText);
                        box.Password = "";
                        box.PlaceholderText = Loc.T("Key saved – paste a new one to replace");
                    }
                    else msg.Text = Loc.T("Key rejected") + (r.Message != "" ? ": " + r.Message : "");
                }
                catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException) { msg.Text = e.Message; }
                b.IsEnabled = true;
            };
            return Line(box, b);
        }
        var tmdb = new PasswordBox { PlaceholderText = Loc.T(s!.TmdbKey != "" ? "Key saved – paste a new one to replace" : "API key (v3) or Read Access Token (v4)") };
        var omdb = new PasswordBox { PlaceholderText = Loc.T(s.OmdbKey != "" ? "Key saved – paste a new one to replace" : "OMDb key") };
        var tmdbMsg = Note();
        var omdbMsg = Note();
        var tmdbRow = Key(tmdb, k => lib.TestTmdbKeyAsync(k), tmdbMsg, (x, k) => x.TmdbKey = k, "Key works and is saved. Click “Scan list & update” to fetch details.");
        var omdbRow = Key(omdb, k => lib.TestOmdbKeyAsync(k), omdbMsg, (x, k) => x.OmdbKey = k, "Key works and is saved. Click “Update RT & Metacritic now”.");
        Add(p, Heading("Online data"), Note(Loc.T("Both keys are optional and free. Without them the app still works from the IMDb data.")),
            Label("TMDB API key"), Note(Loc.T("Adds plot, tagline, countries, languages, Persian titles, cast photos and backdrops.")), tmdbRow,
            Note(Loc.T("Get one at themoviedb.org → Settings → API.")), tmdbMsg,
            Label("OMDb API key"), Note(Loc.T("Fills Rotten Tomatoes and Metacritic scores that Wikidata does not have.")), omdbRow,
            Note(Loc.T("Get one at omdbapi.com/apikey.aspx (free: 1,000 titles a day; the rest continue on the next update).")), omdbMsg);
        return p;
    }

    // ---------- maintenance ----------
    FrameworkElement Maintenance()
    {
        var p = Pane();
        Add(p, Heading("Update"), Note(Loc.T("“Scan list & update” adds new lines from your list files. Manual matches are never overwritten.")),
            Row(Btn("Scan list & update", (_, _) => Act("Scan list & update", () => lib.StartFullUpdate()), true),
                Btn("Update IMDb ratings", (_, _) => Act("Update IMDb ratings", () => lib.StartOrQueue(lib.RefreshRatings))),
                Btn("Update RT & Metacritic", (_, _) => Act("Update RT & Metacritic", () => lib.StartOrQueue(() => lib.FetchScores(true), key: "scores"))),
                Btn("Retry missing posters", (_, _) => Act("Retry missing posters", () => lib.StartOrQueue(() => lib.FetchPosters(true), key: "posters")))),
            Note(Loc.T("“Update IMDb ratings” downloads only the ratings file (about 8 MB).")));

        Add(p, Heading("Export"), Note(Loc.T("Pick the movies and the columns, then save an Excel or CSV file.")),
            Row(Btn("Export to Excel…", async (_, _) =>
            {
                main.Activate();
                try { await main.OpenExport(); }
                catch (System.Runtime.InteropServices.COMException e) { Say(e.Message, InfoBarSeverity.Error); }   // a dialog is already open there
            })));

        // people: the counts take a moment, so the choices show their numbers once they are known
        var peopleInfo = Note();
        var noKey = new InfoBar { Severity = InfoBarSeverity.Warning, IsClosable = false, IsOpen = s!.TmdbKey == "", Message = Loc.T("Add a free TMDB key in Online data first.") };
        var scope = new ComboBox { Header = Loc.T("Whom to scan"), HorizontalAlignment = HorizontalAlignment.Stretch };
        (string Tag, string Label)[] scopes = { ("directors", "Directors and writers"), ("main", "Directors, writers and the first five actors"), ("all", "Everyone with a credit (slow: tens of thousands)") };
        foreach (var (tag, label) in scopes) scope.Items.Add(new ComboBoxItem { Content = Loc.T(label), Tag = tag });
        scope.SelectedIndex = Math.Max(0, Array.FindIndex(scopes, x => x.Tag == s.PeopleScope));
        scope.SelectionChanged += (_, _) =>
        {
            if (!filling && scope.SelectedItem is ComboBoxItem { Tag: string t }) _ = Change(x => x.PeopleScope = t, null);
        };
        var scan = Btn("Scan people now", async (_, _) =>
        {
            string sc = scope.SelectedItem is ComboBoxItem { Tag: string t } ? t : "main";
            await Change(x => x.PeopleScope = sc, null);
            bool started = lib.StartOrQueue(() => lib.FetchPeople(sc), key: ("people", sc));
            Say(Loc.T(started ? "Scanning people… progress shows below." : "Queued: it runs after the current job."));
        });
        scan.IsEnabled = s.TmdbKey != "";
        scan.VerticalAlignment = VerticalAlignment.Bottom;
        Add(p, Heading("People"), peopleInfo, noKey, Line(scope, scan),
            Toggle("Scan people automatically after each update", s.PeopleAuto, (x, v) => x.PeopleAuto = v));
        async Task People()
        {
            string line = "Photos, biographies and full filmographies of directors, writers and actors come from TMDB. Profiles are fetched when you open them; scanning fetches them in advance, so they open at once and work offline.";
            peopleInfo.Text = Loc.T(line);
            try
            {
                var st = await Task.Run(lib.PeopleStatus);
                peopleInfo.Text = Loc.T(line) + " " + Fmt("{0} profiles fetched so far ({1} with a photo).", st.Fetched.ToString("N0"), st.Photos.ToString("N0"));
                filling = true;
                foreach (var it in scope.Items.Cast<ComboBoxItem>())
                {
                    string tag = (string)it.Tag;
                    string label = scopes.First(x => x.Tag == tag).Label;
                    it.Content = Loc.T(label) + " " + Fmt("({0} to fetch)", st.Todo.GetValueOrDefault(tag).ToString("N0"));
                }
                filling = false;
            }
            catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException) { peopleInfo.Text = e.Message; }
        }
        _ = People();

        Add(p, Heading("Rename plan"), Note(Loc.T("Writes a plan to rename your movie files, subtitles and folders by a pattern you choose. Nothing is renamed until you run the plan.")),
            Row(Btn("Rename plan…", (_, _) => RenamePlanWindow.Open())));

        Add(p, Heading("Full backup"),
            Note(Loc.T("Everything in one .zip file: the whole library, all settings and API keys, your list files, the posters and, if you want, the IMDb data. Use it for a complete safety copy or to move to another PC.")),
            Row(Btn("Save full backup…", (_, _) => Backups("full")), Btn("Load full backup…", (_, _) => Backups("full"))));
        Add(p, Heading("Quick backup (your own data only)"),
            Note(Loc.T("Your favorites, watched flags, ratings, notes, manual matches, ignored problems, saved views and wishlist. A copy is also saved automatically once a day in the data folder (the last 14 are kept).")),
            Row(Btn("Export backup…", (_, _) => Backups("quick")), Btn("Import backup…", (_, _) => Backups("quick"))));

        Add(p, Heading("Rebuild"), Note(Loc.T("Only needed if matches look wrong or the IMDb data is damaged.")),
            Row(Btn("Re-match everything", async (_, _) =>
                {
                    if (!await WinKit.Confirm(root.XamlRoot, "Re-match everything?", "Every automatically matched file is matched again. Matches you set yourself are kept. This takes a few minutes.", "Re-match")) return;
                    // not queued, as in Python
                    if (lib.StartRematch()) Say(Fmt("{0}: started", Loc.T("Re-match everything")));
                    else Say(Loc.T("Another job is running. Wait until it is done."), InfoBarSeverity.Warning);
                }),
                Btn("Re-download IMDb data", async (_, _) =>
                {
                    if (!await WinKit.Confirm(root.XamlRoot, "Download all IMDb data again?", "About 1.9 GB is downloaded and the index is rebuilt (10–20 minutes). Your matches and notes are kept.", "Download")) return;
                    Act("Re-download IMDb data", () => lib.StartFullUpdate(true));
                })),
            Note(Loc.T("“Re-download IMDb data” refreshes everything (about 1.9 GB).")));
        return p;
    }

    void Backups(string which)
    {
        main.OpenBackups();
        BackupsWindow.Instance?.ShowTab(which);
    }

    // ---------- activity log ----------
    FrameworkElement LogPane()
    {
        var p = Pane();
        log = new TextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12, FlowDirection = FlowDirection.LeftToRight };
        Add(p, Heading("Activity log"), Note(Loc.T("What the last updates did.")), log);
        return p;
    }

    void ShowLog()
    {
        var lines = lib.Job.Log;
        if (log == null || (ReferenceEquals(lines, logShown) && log.Text.Length > 0)) return;
        logShown = lines;
        log.Text = lines.Count > 0 ? string.Join("\n", lines) : Loc.T("No activity yet.");
    }

    void OnJob() => DispatcherQueue.TryEnqueue(() => { if (tab == "log") ShowLog(); });
}
