using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MovieShelf.Core;

namespace MovieShelf.App;

public sealed partial class MainWindow : Window
{
    readonly Library lib = App.Lib;
    List<Movie> all = new(), pool = new(), view = new();
    readonly Dictionary<Movie, MovieItem> itemOf = new();
    List<MovieItem> shown = new();     // the rows on screen, in display order
    string kind = "all", sortKey = "rating", groupKey = "none";
    bool reverse, loading, ready, reloadAgain, reloadKeep;
    int seriesCount, loadedVersion;
    string? reopenKey;
    Movie? current;
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer searchTimer, prefsTimer, reloadTimer;
    readonly List<FilterOption> status = new(), genre = new(), decade = new(), res = new(), src = new(), type = new(), runtime = new(), tech = new();
    readonly string prefsPath;
    readonly bool prefsReadOnly, debugRun;
    readonly TaskCompletionSource rootLoaded = new();

    /// <summary>The search and the filters. The controls show it and change it; nothing else holds filter state.</summary>
    readonly LibraryFilter F = new();

    (int Id, string Name)? person => F.Person;
    string country => F.Country;
    string language => F.Lang;

    static readonly (string Key, string Label)[] StatusDefs =
    {
        ("fav", "Favorites"), ("watched", "Watched"), ("unwatched", "Unwatched"), ("review", "Needs review"),
        ("dupes", "Multiple files"), ("noposter", "No poster"), ("top", "Top rated 8+"),
    };

    static readonly (string Key, string Label)[] RuntimeDefs =
    {
        ("short", "< 60m"), ("90", "60–90m"), ("120", "90–120m"), ("150", "2–2.5h"), ("long", "> 2.5h"),
    };

    static readonly (string Key, string Label)[] TechDefs =
    {
        ("fa", "Farsi dubbed"), ("dual", "Dual audio"), ("sub", "Softsub"), ("x265", "x265/HEVC"), ("b10", "10-bit"), ("hdr", "HDR"),
        ("51", "5.1+ audio"), ("ed", "Special edition"),
    };

    static readonly string[] ResOrder = { "2160p", "1080p", "720p", "576p", "480p", "SD" };

    internal static readonly Dictionary<string, string> TypeLabel = new()
    {
        ["movie"] = "Movie", ["tvMovie"] = "TV movie", ["video"] = "Video", ["short"] = "Short", ["tvShort"] = "TV short",
        ["tvSpecial"] = "TV special", ["tvMiniSeries"] = "Mini-series", ["tvSeries"] = "TV series", ["unknown"] = "Unmatched",
    };

    public MainWindow()
    {
        InitializeComponent();
        debugRun = (Environment.GetEnvironmentVariable("MOVIESHELF_SHOT") is { Length: > 0 } && Environment.GetEnvironmentVariable("MOVIESHELF_SHOT_ONLY") is not ("welcome" or "startup"))
            || Environment.GetEnvironmentVariable("MOVIESHELF_PERF") is { Length: > 0 };
        string? ownPrefs = Environment.GetEnvironmentVariable("MOVIESHELF_PREFS");
        prefsPath = ownPrefs is { Length: > 0 } ? ownPrefs : Path.Combine(lib.DataDir, "native_ui.json");
        prefsReadOnly = debugRun && ownPrefs is not { Length: > 0 };   // a debug run does not leave its filters behind
        Title = "MovieShelf";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Chrome.Attach(this, ApplyLanguage);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1500, 950));
        if (AppWindow.Presenter is OverlappedPresenter window)
        {
            window.PreferredMinimumWidth = 900;
            window.PreferredMinimumHeight = 600;
        }
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "MovieShelf.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);

        StatusList.ItemsSource = status;
        GenreList.ItemsSource = genre;
        DecadeList.ItemsSource = decade;
        ResList.ItemsSource = res;
        SrcList.ItemsSource = src;
        TypeList.ItemsSource = type;
        RuntimeList.ItemsSource = runtime;
        TechList.ItemsSource = tech;
        Root.Loaded += (_, _) =>
        {
            UpdatePosterWidth();
            Root.XamlRoot.Changed += (_, _) => UpdatePosterWidth();
            WatchScroll(ActiveList);
            rootLoaded.TrySetResult();
        };
        Root.ActualThemeChanged += (_, _) => OnThemeChanged();
        AddShortcuts();

        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        searchTimer = queue.CreateTimer();
        searchTimer.Interval = TimeSpan.FromMilliseconds(120);
        searchTimer.IsRepeating = false;
        searchTimer.Tick += (_, _) =>
        {
            F.Q = SearchBox.Text;
            Apply();
        };
        prefsTimer = queue.CreateTimer();
        prefsTimer.Interval = TimeSpan.FromSeconds(1);
        prefsTimer.IsRepeating = false;
        prefsTimer.Tick += (_, _) => WritePrefsLater();
        reloadTimer = queue.CreateTimer();
        reloadTimer.Interval = TimeSpan.FromSeconds(20);   // during a long job the list follows at most this often, so scrolling is not disturbed
        reloadTimer.Tick += async (_, _) =>
        {
            if (lib.Version != loadedVersion) await LoadAsync();
        };

        restoring = true;
        LoadPrefs();
        SelectTag(SortBox, sortKey);
        SelectTag(GroupBox, groupKey);
        ReverseBtn.IsChecked = reverse;
        SyncFilterControls();
        restoring = false;
        foreach (var s in new[] { SecViews, SecLibrary, SecGenre, SecYear, SecScores, SecRuntime, SecQuality, SecType, RegionExpander })
        {
            if (sections.TryGetValue((string)s.Tag, out bool open)) s.IsExpanded = open;
            s.Expanding += (e, _) => RememberSection(e, true);
            s.Collapsed += (e, _) => RememberSection(e, false);
        }

        FilterOption.Toggled += () => Apply();
        lib.JobChanged += OnJobChanged;
        lib.DataChanged += () => DispatcherQueue.TryEnqueue(async () => await LoadAsync());
        Closed += (_, _) => SavePrefs();
        WishlistWindow.Changed += RefreshBadges;

        UpdateThemeIcon();
        ApplyLanguage();
        _ = StartAsync();
        if (Environment.GetEnvironmentVariable("MOVIESHELF_SHOT") is { Length: > 0 } shotDir) _ = DebugShots(shotDir);
        if (Environment.GetEnvironmentVariable("MOVIESHELF_PERF") is { Length: > 0 } perfFile) _ = DebugPerf(perfFile);
    }

    void UpdatePosterWidth() => MovieItem.PosterWidth = (int)Math.Ceiling(MovieItem.CardWidth * Root.XamlRoot.RasterizationScale);

    static void SelectTag(ComboBox box, string tag) =>
        box.SelectedIndex = Math.Max(0, box.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == tag));

    // ---------- preferences ----------
    readonly Dictionary<string, bool> sections = new();
    List<string> recentSearches = new();
    string? dismissedError;

    void LoadPrefs()
    {
        try
        {
            if (!File.Exists(prefsPath)) return;
            var o = JsonNode.Parse(File.ReadAllText(prefsPath));
            kind = (string?)o?["kind"] is "movies" or "series" ? (string)o!["kind"]! : "all";
            reverse = (bool?)o?["reverse"] ?? false;
            string sort = (string?)o?["sort"] ?? "rating";
            if (sort == "year")   // older saved choice: oldest first
            {
                sort = "year-desc";
                reverse = true;
            }
            sortKey = SortBox.Items.Cast<ComboBoxItem>().Any(i => (string)i.Tag == sort) ? sort : "rating";
            string group = (string?)o?["group"] ?? "none";
            groupKey = GroupBox.Items.Cast<ComboBoxItem>().Any(i => (string)i.Tag == group) ? group : "none";
            Chrome.SetTheme((string?)o?["theme"] ?? "system");
            SetPosterSizeNow((string?)o?["psize"] ?? "m");
            bool list = (bool?)o?["list"] ?? false;
            GridBtn.IsChecked = !list;
            ListBtn.IsChecked = list;
            MovieGrid.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
            ListHost.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            KindBar.SelectedItem = kind == "movies" ? KindMovies : kind == "series" ? KindSeries : KindAll;
            F.Merge(o?["filters"]);
            if (o?["recent"] is JsonArray rec) recentSearches = rec.Select(x => (string?)x ?? "").Where(x => x.Length > 0).ToList();
            if (o?["sections"] is JsonObject secs) foreach (var (k, v) in secs) sections[k] = (bool?)v ?? false;
            dismissedError = (string?)o?["dismissedError"];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException) { }
    }

    JsonObject PrefsNow()
    {
        var secs = new JsonObject();
        foreach (var (k, v) in sections) secs[k] = v;
        return new JsonObject
        {
            ["kind"] = kind, ["sort"] = sortKey, ["group"] = groupKey, ["theme"] = Theme, ["reverse"] = reverse, ["list"] = ListBtn.IsChecked == true,
            ["psize"] = PosterSize, ["filters"] = F.ToJson(withQuery: false), ["sections"] = secs, ["dismissedError"] = dismissedError,
            ["recent"] = new JsonArray(recentSearches.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
        };
    }

    void WritePrefs(JsonObject o)
    {
        if (prefsReadOnly) return;
        try
        {
            Dialogs.KeepExportPrefs(prefsPath, o);
            File.WriteAllText(prefsPath, o.ToJsonString());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    void SavePrefs() => WritePrefs(PrefsNow());

    void WritePrefsLater()
    {
        var o = PrefsNow();
        _ = Task.Run(() => WritePrefs(o));
    }

    void SavePrefsSoon()
    {
        if (!ready) return;
        prefsTimer.Stop();
        prefsTimer.Start();
    }

    void RememberSection(Expander e, bool open)
    {
        sections[(string)e.Tag] = open;
        SavePrefsSoon();
    }

    // ---------- language ----------
    public void ApplyLanguage()
    {
        Root.FlowDirection = Loc.IsFa ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        MovieGrid.FlowDirection = FlowDirection.LeftToRight;
        Loc.Apply(Root);
        AppTitleBar.Title = "MovieShelf";
        // what Loc.Apply does not reach
        Loc.Apply(AppTitleBar.Content as DependencyObject);
        Loc.Apply(AppTitleBar.RightHeader as DependencyObject);
        SelectBtn.Label = Loc.T("Select");
        YearMin.Header = Loc.T("From");
        YearMax.Header = Loc.T("To");
        CountryBox.Header = Loc.T("Country");
        LangBox.Header = Loc.T("Language");
        LangBtn.Content = Loc.IsFa ? "EN" : "FA";
        ToolTipService.SetToolTip(LangBtn, Loc.T("Language: English / فارسی"));
        ViewSaveBtn.Content = Loc.T("Save the current view…");
        foreach (var box in new[] { SortBox, GroupBox }) RefreshCombo(box);
        UpdateRangeLabels();
        UpdateSortHeader();
        RenderViews();
        UpdateJobUi();
        if (!ready) return;
        BuildFacets();
        Render(keep: true);
        RenderActive();
        UpdateBulkCount();
        if (DetailSplit.IsPaneOpen && current != null) ShowDetail(current);
    }

    // a combo box shows a copy of its selected item, so a new text needs it picked again
    void RefreshCombo(ComboBox box)
    {
        bool was = restoring;
        restoring = true;
        int i = box.SelectedIndex;
        box.SelectedIndex = -1;
        box.SelectedIndex = i;
        restoring = was;
    }

    // ---------- data ----------
    // the filter panel is the heaviest part of the first layout, and the posters are what the first frame is for
    void ShowFiltersAfterFirstFrame()
    {
        void Show(object? s, object e)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= Show;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FilterBody.Visibility = Visibility.Visible);
        }
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += Show;
    }

    async Task StartAsync()
    {
        await LoadAsync(keep: false);
        ShowFiltersAfterFirstFrame();
        await rootLoaded.Task;
        if (debugRun) return;
        // run.py: today's backup, then the update that picks up new lines in the lists. The very first start asks how to begin instead.
        await Task.Run(lib.RunStartupJobs);
        if (lib.FirstRun) RunUpdate();
    }

    /// <summary>Reads the library again. <paramref name="keep"/> keeps the scroll position, as every reload but the first does.</summary>
    public async Task LoadAsync(bool keep = true)
    {
        if (loading)
        {
            reloadAgain = true;
            reloadKeep = keep;
            return;
        }
        loading = true;
        LoadRing.IsActive = all.Count == 0;
        try
        {
            do
            {
                reloadAgain = false;
                int version = lib.Version;
                var list = await Task.Run(lib.LoadMovies);
                all = list;
                loadedVersion = version;
                itemOf.Clear();
                seriesCount = 0;
                foreach (var m in all)
                {
                    itemOf[m] = new MovieItem(m);
                    if (m.IsSeries) seriesCount++;
                }
                if (current != null)
                {
                    current = all.Find(m => m.K == current.K);
                    if (current == null) CloseDetailPane();
                }
                BuildFacets();
                ready = true;
                Apply(keep);
                if (reopenKey != null)
                {
                    var again = all.Find(m => m.K == reopenKey);
                    reopenKey = null;
                    if (again != null) ShowDetail(again);
                }
                keep = reloadKeep;
            } while (reloadAgain);
            RefreshBadges();
        }
        catch (Exception e)
        {
            Toast(Loc.T("Could not read the library:") + " " + e.Message, "err");
        }
        finally
        {
            loading = false;
            LoadRing.IsActive = false;
        }
    }

    static void Bump<T>(Dictionary<T, int> counts, T key) where T : notnull => CollectionsMarshal.GetValueRefOrAddDefault(counts, key, out _)++;

    // the counts of every group in one walk over the movies
    void BuildFacets()
    {
        pool = kind == "all" ? all : all.FindAll(m => (kind == "series") == m.IsSeries);
        UpdateKindCounts();

        int fav = 0, watched = 0, review = 0, dupes = 0, noPoster = 0, top = 0;
        Dictionary<string, int> genres = new(), srcs = new(), types = new(), countries = new(), langs = new();
        var decades = new Dictionary<int, int>();
        var resCount = new int[ResOrder.Length];
        var techCount = new int[TechDefs.Length];
        var techTests = TechDefs.Select(d => LibraryFilter.TechTests[d.Key]).ToArray();
        var movieSrc = new List<string>();
        foreach (var m in pool)
        {
            if (m.Fav) fav++;
            if (m.Watched) watched++;
            if (m.NeedsReview) review++;
            if (m.Files.Count > 1) dupes++;
            if (!m.HasPoster) noPoster++;
            if (m.Rating >= 8) top++;
            foreach (var g in m.Genres) Bump(genres, g);
            foreach (var c in m.Countries) Bump(countries, c);
            foreach (var l in m.Langs) Bump(langs, l);
            if (m.Decade is int d) Bump(decades, d);
            Bump(types, m.Type ?? "unknown");
            int seenRes = 0, seenTech = 0;
            movieSrc.Clear();
            foreach (var f in m.Files)
            {
                if (Array.IndexOf(ResOrder, f.Res) is >= 0 and var r) seenRes |= 1 << r;
                for (int t = 0; t < techTests.Length; t++)
                    if ((seenTech >> t & 1) == 0 && techTests[t](f)) seenTech |= 1 << t;
                if (f.Src is { Length: > 0 } s && !movieSrc.Contains(s)) movieSrc.Add(s);
            }
            foreach (var s in movieSrc) Bump(srcs, s);
            for (int r = 0; r < resCount.Length; r++) if ((seenRes >> r & 1) != 0) resCount[r]++;
            for (int t = 0; t < techCount.Length; t++) if ((seenTech >> t & 1) != 0) techCount[t]++;
        }

        int Status(string key) => key switch
        {
            "fav" => fav, "watched" => watched, "unwatched" => pool.Count - watched, "review" => review,
            "dupes" => dupes, "noposter" => noPoster, _ => top,
        };
        Fill(status, StatusList, F.Status, StatusDefs.Select(d => (d.Key, d.Label, (int?)Status(d.Key))));
        Fill(genre, GenreList, F.Genre, genres.OrderByDescending(g => g.Value).Select(g => (g.Key, g.Key, (int?)g.Value)));
        Fill(decade, DecadeList, F.Decade, decades.OrderBy(g => g.Key).Select(g => (g.Key.ToString(), g.Key + "s", (int?)g.Value)));
        Fill(res, ResList, F.Res, ResOrder.Select((r, i) => (r, r, (int?)resCount[i])).Where(x => x.Item3 > 0));
        Fill(src, SrcList, F.Src, srcs.OrderByDescending(g => g.Value).Select(g => (g.Key, g.Key, (int?)g.Value)));
        Fill(type, TypeList, F.Type, types.OrderByDescending(g => g.Value).Select(g => (g.Key, TypeLabel.GetValueOrDefault(g.Key, "Unmatched"), (int?)g.Value)));
        Fill(runtime, RuntimeList, F.Runtime, RuntimeDefs.Select(d => (d.Key, d.Label, (int?)null)));
        Fill(tech, TechList, F.Tech, TechDefs.Select((d, i) => (d.Key, d.Label, (int?)techCount[i])));
        FillRegion(CountryBox, countries, F.Country);
        FillRegion(LangBox, langs, F.Lang);
        RegionExpander.Visibility = countries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // the same choices keep their controls and only get new numbers, so the panel does not jump
    static void Fill(List<FilterOption> target, ItemsControl host, HashSet<string> set, IEnumerable<(string Key, string Label, int? Count)> opts)
    {
        var list = opts.ToList();
        bool same = target.Count == list.Count;
        for (int i = 0; same && i < list.Count; i++) same = target[i].Key == list[i].Key;
        if (same)
        {
            for (int i = 0; i < list.Count; i++) target[i].Update(list[i].Label, list[i].Count);
            return;
        }
        target.Clear();
        target.AddRange(list.Select(o => new FilterOption(set, o.Key, o.Label, o.Count)));
        host.ItemsSource = null;
        host.ItemsSource = target;
    }

    bool fillingRegion;
    readonly Dictionary<ComboBox, string> regionShown = new();

    void FillRegion(ComboBox box, Dictionary<string, int> counts, string selected)
    {
        var items = counts.OrderByDescending(g => g.Value).ToList();
        string shownNow = Loc.T("Any") + "\n" + selected + "\n" + string.Join('\n', items.Select(i => $"{i.Key}\t{i.Value}"));
        if (regionShown.GetValueOrDefault(box) == shownNow) return;
        regionShown[box] = shownNow;
        fillingRegion = true;
        box.Items.Clear();
        box.Items.Add(new ComboBoxItem { Content = Loc.T("Any"), Tag = "" });
        foreach (var (k, n) in items) box.Items.Add(new ComboBoxItem { Content = $"{k} ({n:N0})", Tag = k });
        SelectTag(box, selected);
        fillingRegion = false;
    }

    void UpdateKindCounts()
    {
        KindAll.Text = $"{Loc.T("All")}  {all.Count:N0}";
        KindMovies.Text = $"{Loc.T("Movies")}  {all.Count - seriesCount:N0}";
        KindSeries.Text = $"{Loc.T("Series")}  {seriesCount:N0}";
    }

    void UpdateCount()
    {
        string noun = kind == "series" ? "series" : view.Count == 1 ? "movie" : "movies";
        CountText.Text = $"{view.Count:N0} {Loc.T(noun)}" + (view.Count != pool.Count ? $" {Loc.T("of")} {pool.Count:N0}" : "");
    }

    // ---------- the results ----------
    IEnumerable<Movie> Sorted(IEnumerable<Movie> r)
    {
        Func<Movie, string> tk = m => m.TKey;
        IOrderedEnumerable<Movie> o = sortKey switch
        {
            "year-desc" => r.OrderByDescending(m => m.Year ?? 0).ThenBy(tk, StringComparer.Ordinal),
            "rating" => r.OrderByDescending(m => m.Rating ?? 0).ThenByDescending(m => m.Votes),
            "votes" => r.OrderByDescending(m => m.Votes),
            "rt" => r.OrderByDescending(m => m.Rt ?? -1).ThenByDescending(m => m.Votes),
            "mc" => r.OrderByDescending(m => m.Mc ?? -1).ThenByDescending(m => m.Votes),
            "runtime" => r.OrderByDescending(m => m.Runtime ?? 0),
            "added" => r.OrderByDescending(m => m.Files[0].Id),
            "myrating" => r.OrderByDescending(m => m.MyRating ?? 0).ThenByDescending(m => m.Rating ?? 0),
            _ => r.OrderBy(tk, StringComparer.Ordinal),
        };
        return o;
    }

    List<Movie> Results()
    {
        var q = SearchQuery.Parse(F.Q);
        var pass = F.Compile();
        var hits = new List<Movie>();
        foreach (var m in pool)
            if (pass.Test(m) && q.Test(m)) hits.Add(m);
        var r = Sorted(hits).ToList();
        if (reverse) r.Reverse();
        if (q.Plain.Length > 0)
        {
            // titles that match the query float to the top
            int Rank(Movie m)
            {
                var names = new[] { m.Title, m.En, m.Orig, m.FaTitle }.Concat(m.Akas).Select(Movie.Fold).ToList();
                if (names.Any(n => n.StartsWith(q.Plain, StringComparison.Ordinal))) return 0;
                return names.Take(4).Any(n => n.Contains(q.Plain, StringComparison.Ordinal)) ? 1 : 2;
            }
            r = r.OrderBy(Rank).ToList();
        }
        return r;
    }

    /// <summary>Filters, sorts and shows the results. Without <paramref name="keep"/> the list goes back to the top.</summary>
    void Apply(bool keep = false)
    {
        if (restoring || !ready) return;
        SavePrefsSoon();
        view = Results();
        Render(keep);
        RenderActive();
    }

    int renderCount;

    void Render(bool keep)
    {
        renderCount++;
        bool isList = ListBtn.IsChecked == true;
        var list = ActiveList;
        var scroll = ScrollOf(list);
        double top = keep ? scroll?.VerticalOffset ?? 0 : 0;
        var rows = new List<MovieItem>(view.Count);
        foreach (var m in view) rows.Add(itemOf[m]);
        object? source = rows;
        shown = rows;
        if (groupKey != "none")
        {
            var groups = Groups(rows);
            shown = groups.SelectMany(g => g).ToList();
            source = new Microsoft.UI.Xaml.Data.CollectionViewSource { IsSourceGrouped = true, Source = groups }.View;
        }
        syncingSel = true;
        MovieGrid.ItemsSource = isList ? null : source;
        MovieList.ItemsSource = isList ? source : null;
        RestoreSelection();
        syncingSel = false;
        RenderEmpty();
        UpdateCount();
        if (scroll == null) return;
        if (top > 0) list.UpdateLayout();
        scroll.ChangeView(null, top, null, true);
    }

    void RenderEmpty()
    {
        EmptyPanel.Visibility = ready && view.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (EmptyPanel.Visibility == Visibility.Collapsed) return;
        EmptyButtons.Children.Clear();
        void Action(string text, Action click, bool accent = false)
        {
            var b = new Button { Content = Loc.T(text) };
            if (accent) b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            b.Click += (_, _) => click();
            EmptyButtons.Children.Add(b);
        }
        var j = lib.Job;
        if (all.Count == 0 && !j.Running && j.Error?.Contains("No .txt", StringComparison.Ordinal) == true)
        {
            EmptyTitle.Text = Loc.T("No movies yet");
            EmptyText.Text = Loc.T("Give MovieShelf your movie folder and it finds the movies by itself, or load a list of movie names (a .txt or .csv file, one name per line).");
            Action("Choose the movie folder", () => _ = Dialogs.Settings(this), accent: true);
            Action("Load list", () => OnLoadList(this, new RoutedEventArgs()));
            Action("How to prepare a list", () => HelpWindow.Open("welcome"));
        }
        else if (all.Count == 0)
        {
            EmptyTitle.Text = Loc.T("Building your library…");
            EmptyText.Text = Loc.T("The first run downloads IMDb data (about 1.9 GB) and matches your list. This takes around 10 minutes; progress shows at the top.");
        }
        else if (pool.Count == 0 && kind == "series")
        {
            EmptyTitle.Text = Loc.T("No series in your library yet.");
            EmptyText.Text = Loc.T("A series shows up here when a name in your list matches an IMDb TV series or mini-series, for example Breaking.Bad.2008.");
        }
        else
        {
            EmptyTitle.Text = Loc.T(kind == "series" ? "No series match these filters." : "No movies match these filters.");
            EmptyText.Text = "";
            Action("Reset filters", ResetFilters);
        }
        EmptyText.Visibility = EmptyText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- scrolling ----------
    readonly Dictionary<ListViewBase, ScrollViewer> scrollers = new();

    ScrollViewer? ScrollOf(ListViewBase list)
    {
        if (scrollers.TryGetValue(list, out var s)) return s;
        s = Under<ScrollViewer>(list).FirstOrDefault();
        if (s != null) scrollers[list] = s;
        return s;
    }

    readonly HashSet<ListViewBase> watchedScroll = new();

    void WatchScroll(ListViewBase list)
    {
        if (ScrollOf(list) is { } s && watchedScroll.Add(list)) s.ViewChanged += (_, _) => UpdateToTop();
    }

    void UpdateToTop()
    {
        var want = (ScrollOf(ActiveList)?.VerticalOffset ?? 0) >= 1500 ? Visibility.Visible : Visibility.Collapsed;
        if (ToTopBtn.Visibility != want) ToTopBtn.Visibility = want;
    }

    void OnToTop(object sender, RoutedEventArgs e)
    {
        ScrollOf(ActiveList)?.ChangeView(null, 0, null);
        SearchBox.Focus(FocusState.Programmatic);
    }

    // ---------- toolbar and filter events ----------
    void OnPaneToggleRequested(TitleBar sender, object args) => FilterSplit.IsPaneOpen = !FilterSplit.IsPaneOpen;

    void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (restoring) return;
        int? lo = double.IsNaN(YearMin.Value) ? null : (int)YearMin.Value, hi = double.IsNaN(YearMax.Value) ? null : (int)YearMax.Value;
        if (lo == F.YMin && hi == F.YMax) return;
        F.YMin = lo;
        F.YMax = hi;
        Apply();
    }

    void OnSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (restoring || McLabel == null || McMin == null) return;
        if (F.RMin == RatingMin.Value && F.VMin == VotesMin.Value && F.RtMin == RtMin.Value && F.McMin == McMin.Value) return;
        F.RMin = RatingMin.Value;
        F.VMin = VotesMin.Value;
        F.RtMin = RtMin.Value;
        F.McMin = McMin.Value;
        UpdateRangeLabels();
        Apply();
    }

    void UpdateRangeLabels()
    {
        string any = Loc.T("any");
        RatingLabel.Text = Loc.T("IMDb rating") + ": " + (F.RMin > 0 ? $"≥ {F.RMin:0.0}" : any);
        VotesLabel.Text = Loc.T("Votes") + ": " + (F.VMin > 0 ? "≥ " + FmtVotes(Math.Pow(10, F.VMin)) : any);
        RtLabel.Text = Loc.T("Rotten Tomatoes") + ": " + (F.RtMin > 0 ? $"≥ {F.RtMin:0}%" : any);
        McLabel.Text = Loc.T("Metacritic") + ": " + (F.McMin > 0 ? $"≥ {F.McMin:0}" : any);
    }

    void OnRegionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (fillingRegion || restoring) return;
        string c = (CountryBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "", l = (LangBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        if (c == F.Country && l == F.Lang) return;
        F.Country = c;
        F.Lang = l;
        Apply();
    }

    /// <summary>Makes the controls show <see cref="F"/>, without a pass over the results for each one.</summary>
    void SyncFilterControls()
    {
        bool was = restoring;
        restoring = true;
        YearMin.Value = F.YMin ?? double.NaN;
        YearMax.Value = F.YMax ?? double.NaN;
        RatingMin.Value = F.RMin;
        VotesMin.Value = F.VMin;
        RtMin.Value = F.RtMin;
        McMin.Value = F.McMin;
        restoring = was;
        UpdateRangeLabels();
    }

    void OnResetFilters(object sender, RoutedEventArgs e) => ResetFilters();

    void ResetFilters()
    {
        F.Clear();
        SetSearchText("");
        SyncFilterControls();
        BuildFacets();
        Apply();
    }

    void OnKindChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem item || (string)item.Tag == kind) return;
        kind = (string)item.Tag;
        BuildFacets();
        Apply();
    }

    void OnSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (restoring || SortBox.SelectedItem is not ComboBoxItem i || (string)i.Tag == sortKey) return;
        sortKey = (string)i.Tag;
        reverse = false;
        ReverseBtn.IsChecked = false;
        UpdateSortHeader();
        Apply();
    }

    static readonly (string Key, string Label)[] SortHeads =
    {
        ("title", "Title"), ("year-desc", "Year"), ("rating", "IMDb"), ("votes", "Votes"), ("rt", "RT"), ("mc", "MC"), ("runtime", "Min"),
    };

    void UpdateSortHeader()
    {
        foreach (var (b, (key, label)) in new[] { HeadTitle, HeadYear, HeadImdb, HeadVotes, HeadRt, HeadMc, HeadMin }.Zip(SortHeads))
            b.Content = Loc.T(label) + (key != sortKey ? "" : (sortKey == "title") != reverse ? " ▲" : " ▼");
    }

    // the table headers sort: a second click on the same column turns the order around
    void OnHeaderSort(object sender, RoutedEventArgs e)
    {
        string key = (string)((FrameworkElement)sender).Tag;
        if (key == sortKey) reverse = !reverse;
        else
        {
            sortKey = key;
            reverse = false;
            restoring = true;
            SelectTag(SortBox, key);
            restoring = false;
        }
        ReverseBtn.IsChecked = reverse;
        UpdateSortHeader();
        Apply();
    }

    void OnReverseClick(object sender, RoutedEventArgs e)
    {
        reverse = ReverseBtn.IsChecked == true;
        UpdateSortHeader();
        Apply();
    }

    void OnGroupChanged(object sender, SelectionChangedEventArgs e)
    {
        if (restoring || GroupBox.SelectedItem is not ComboBoxItem i || (string)i.Tag == groupKey) return;
        groupKey = (string)i.Tag;
        Apply();
    }

    // each grouper gives a heading, the order of the heading and a text that breaks ties
    (string Label, double Order, string Text) GroupOf(Movie m)
    {
        switch (groupKey)
        {
            case "decade": return m.Decade is int d ? (d + "s", -d, "") : (Loc.T("Unknown year"), 1e9, "");
            case "year": return m.Year is int y ? (y.ToString(), -y, "") : (Loc.T("Unknown year"), 1e9, "");
            case "genre": return m.Genres.Length > 0 ? (m.Genres[0], 0, m.Genres[0]) : (Loc.T("No genre"), 1e9, "zzzzzz");
            case "rating":
                if (m.Rating is not double r || r == 0) return (Loc.T("Unrated"), 99, "");
                int b = Math.Min(Math.Max((int)Math.Floor(r), 5), 9);
                return b >= 9 ? ("9.0 " + Loc.T("and up"), -9, "") : b <= 5 ? (Loc.T("Below") + " 6.0", -5, "") : ($"{b}.0 – {b}.9", -b, "");
            case "res": return m.Res != null ? (m.Res == "2160p" ? "4K (2160p)" : m.Res, Array.IndexOf(ResOrder, m.Res), "") : (Loc.T("Unknown quality"), 99, "");
            case "type":
                string? label = m.Type != null ? TypeLabel.GetValueOrDefault(m.Type) : null;
                return (Loc.T(label ?? "Unmatched"), 0, label ?? "zzzzzz");
            case "letter":
                char c = char.ToUpperInvariant(m.TKey.Length > 0 ? m.TKey[0] : '#');
                return c is >= 'A' and <= 'Z' ? (c.ToString(), 0, c.ToString()) : ("#", 0, "0");
            case "seen": return m.Watched ? (Loc.T("Watched"), 1, "") : (Loc.T("Not watched yet"), 0, "");
        }
        return ("", 0, "");
    }

    readonly HashSet<string> collapsed = new();    // 'group:label' of the closed group headings

    List<MovieGroup> Groups(List<MovieItem> rows)
    {
        var map = new Dictionary<string, (double Order, string Text, List<MovieItem> Items)>();
        foreach (var it in rows)
        {
            var (label, order, text) = GroupOf(it.M);
            if (!map.TryGetValue(label, out var e)) map[label] = e = (order, text, new List<MovieItem>());
            e.Items.Add(it);
        }
        var gs = map.OrderBy(kv => kv.Value.Order).ThenBy(kv => kv.Value.Text, StringComparer.Ordinal).ToList();
        if (reverse && ((sortKey == "year-desc" && groupKey is "decade" or "year") || (sortKey == "title" && groupKey == "letter"))) gs.Reverse();
        bool Tail(KeyValuePair<string, (double Order, string Text, List<MovieItem> Items)> e) => e.Value.Order == 1e9 || e.Value.Order == 99 || e.Value.Text == "zzzzzz";
        return gs.Where(e => !Tail(e)).Concat(gs.Where(Tail)).Select(e =>
        {
            bool closed = collapsed.Contains(groupKey + ":" + e.Key);
            return new MovieGroup(e.Key, closed ? Enumerable.Empty<MovieItem>() : e.Value.Items, e.Value.Items.Count, closed);
        }).ToList();
    }

    void OnGroupHeader(object sender, RoutedEventArgs e)
    {
        string key = groupKey + ":" + (string)((FrameworkElement)sender).Tag;
        if (!collapsed.Add(key)) collapsed.Remove(key);
        Render(keep: true);
    }

    // inside the sideways scroller the table would grow to its longest title; it takes the width on offer and scrolls only below its minimum
    void OnListSized(object sender, SizeChangedEventArgs e) => ListGrid.Width = Math.Max(ListGrid.MinWidth, e.NewSize.Width);

    void OnViewGrid(object sender, RoutedEventArgs e) => SetList(false);
    void OnViewList(object sender, RoutedEventArgs e) => SetList(true);

    void SetList(bool list)
    {
        GridBtn.IsChecked = !list;
        ListBtn.IsChecked = list;
        MovieGrid.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
        ListHost.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
        SavePrefsSoon();
        Render(keep: false);
        ActiveList.UpdateLayout();
        WatchScroll(ActiveList);
        UpdateToTop();
    }

    // ---------- select several movies and change them together ----------
    bool selecting, syncingSel;
    readonly HashSet<string> sel = new();    // the selection outlives the list on screen: it is kept by movie, not by row
    MovieItem? selLast;

    ListViewBase ActiveList => ListBtn.IsChecked == true ? MovieList : MovieGrid;

    void OnSelectToggled(object sender, RoutedEventArgs e) => SetSelecting(SelectBtn.IsChecked == true);

    void SetSelecting(bool on)
    {
        selecting = on;
        SelectBtn.IsChecked = on;
        if (on && BulkRateMenu.Items.Count == 0)
            for (int i = 10; i >= 1; i--)
            {
                int v = i;
                var mi = new MenuFlyoutItem { Text = $"{i}/10" };
                mi.Click += (_, _) => BulkSet(string.Format(Loc.T("Rated {0}/10"), v), rating: v);
                BulkRateMenu.Items.Add(mi);
            }
        BulkBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) sel.Clear();
        selLast = null;
        syncingSel = true;
        foreach (var l in new ListViewBase[] { MovieGrid, MovieList })
        {
            l.SelectionMode = on ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
            l.IsItemClickEnabled = !on;
        }
        syncingSel = false;
        UpdateBulkCount();
    }

    IEnumerable<Movie> Selected => all.Where(m => sel.Contains(m.K));

    void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncingSel || !selecting) return;
        foreach (var i in e.AddedItems.OfType<MovieItem>()) sel.Add(i.M.K);
        foreach (var i in e.RemovedItems.OfType<MovieItem>()) sel.Remove(i.M.K);
        if (e.AddedItems.Count + e.RemovedItems.Count == 1)
        {
            var hit = (e.AddedItems.Count == 1 ? e.AddedItems[0] : e.RemovedItems[0]) as MovieItem;
            if (ShiftDown && selLast != null && hit != null) SelectBetween(selLast, hit);
            selLast = hit;
        }
        UpdateBulkCount();
    }

    static bool ShiftDown => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>Shift-click: everything between the last click and this one.</summary>
    void SelectBetween(MovieItem from, MovieItem to)
    {
        int a = shown.IndexOf(from), b = shown.IndexOf(to);
        if (a < 0 || b < 0) return;
        (a, b) = (Math.Min(a, b), Math.Max(a, b));
        for (int i = a; i <= b; i++) sel.Add(shown[i].M.K);
        syncingSel = true;
        ActiveList.SelectRange(new Microsoft.UI.Xaml.Data.ItemIndexRange(a, (uint)(b - a + 1)));
        syncingSel = false;
    }

    // the list forgets its selection with its items; this gives it back, joined into ranges so a big selection stays quick
    void RestoreSelection()
    {
        if (!selecting || sel.Count == 0) return;
        var list = ActiveList;
        int start = -1;
        for (int i = 0; i <= shown.Count; i++)
        {
            bool on = i < shown.Count && sel.Contains(shown[i].M.K);
            if (on && start < 0) start = i;
            else if (!on && start >= 0)
            {
                list.SelectRange(new Microsoft.UI.Xaml.Data.ItemIndexRange(start, (uint)(i - start)));
                start = -1;
            }
        }
    }

    void UpdateBulkCount()
    {
        int n = sel.Count;
        BulkCount.Text = n > 0 ? $"{n:N0} {Loc.T("selected")}" : Loc.T("Click movies to select them");
        foreach (var c in BulkActions.Children)
            if (c is Control b && (c is not Button { Tag: "done" }) && c is not HyperlinkButton) b.IsEnabled = n > 0;
    }

    void BulkSet(string message, bool? fav = null, bool? watched = null, int? rating = null)
    {
        var chosen = Selected.ToList();
        if (chosen.Count == 0) return;
        var keys = chosen.Select(m => m.K).ToList();
        SaveUserData(() => lib.SetUser(keys, fav, watched, rating));
        foreach (var m in chosen)
        {
            if (fav != null) m.Fav = fav.Value;
            if (watched != null) MarkWatched(m, watched.Value);
            if (rating != null) m.MyRating = rating;
            itemOf.GetValueOrDefault(m)?.Refresh();
        }
        AfterUserChange();
        ShowInfo($"{message}: {chosen.Count:N0} {Loc.T(chosen.Count == 1 ? "movie" : "movies")}");
    }

    static void MarkWatched(Movie m, bool on)
    {
        m.Watched = on;
        m.WatchedAt = on ? m.WatchedAt ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : null;
    }

    Task saving = Task.CompletedTask;

    // one write at a time, in the order the clicks came
    void SaveUserData(Action write)
    {
        saving = saving.ContinueWith(_ =>
        {
            try { write(); }
            catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
            {
                DispatcherQueue.TryEnqueue(() => Toast(e.Message, "err"));
            }
        }, TaskScheduler.Default);
    }

    /// <summary>The cards already show the change. The counts follow; the list only changes when the change takes a movie in or out of it, and then it keeps its place.</summary>
    void AfterUserChange()
    {
        BuildFacets();
        var next = Results();
        if (groupKey != "seen" && next.SequenceEqual(view)) return;
        view = next;
        Render(keep: true);
    }

    async void OnBulk(object sender, RoutedEventArgs e)
    {
        switch ((string)((FrameworkElement)sender).Tag)
        {
            case "fav": BulkSet(Loc.T("Added to favorites"), fav: true); break;
            case "unfav": BulkSet(Loc.T("Removed from favorites"), fav: false); break;
            case "seen": BulkSet(Loc.T("Marked watched"), watched: true); break;
            case "unseen": BulkSet(Loc.T("Marked not watched"), watched: false); break;
            case "all":
                foreach (var m in view) sel.Add(m.K);
                syncingSel = true;
                ActiveList.SelectAll();
                syncingSel = false;
                UpdateBulkCount();
                break;
            case "none":
                sel.Clear();
                syncingSel = true;
                ActiveList.SelectedItems.Clear();
                syncingSel = false;
                UpdateBulkCount();
                break;
            case "done": SetSelecting(false); break;
            case "export": await OpenExport(); break;
            case "wish": await AddToWishlist(Selected.ToList()); break;
            case "remove":
                if (await RemoveFiles(Selected.SelectMany(m => m.Files).Select(f => f.Id).ToList()))
                {
                    sel.Clear();
                    UpdateBulkCount();
                }
                break;
        }
    }

    async Task AddToWishlist(List<Movie> movies)
    {
        var wanted = movies.Select(m => new WishNew(m.Display, m.Year, m.Tconst, null, "Selection")).ToList();
        int n = await Task.Run(() => lib.WishAdd(wanted));
        WishlistWindow.NotifyChanged();
        int dup = wanted.Count - n;
        if (n == 0) Toast(Loc.T("Already on the wishlist"), "info");
        else ShowInfo(string.Format(Loc.T("{0} added to the wishlist"), n.ToString("N0")) + (dup > 0 ? " " + string.Format(Loc.T("({0} already there)"), dup) : ""));
    }

    /// <summary>"Remove from list" for one file, a movie or a selection. True when the user said yes.</summary>
    async Task<bool> RemoveFiles(List<int> ids, Action? after = null)
    {
        if (ids.Count == 0) return false;
        string title = ids.Count == 1 ? Loc.T("Remove this title from your list?") : string.Format(Loc.T("Remove {0} titles from your list?"), ids.Count.ToString("N0"));
        if (!await Dialogs.Confirm(Root.XamlRoot, title, "They are hidden from the library and no update brings them back. Your list files and your movie files are not changed.", "Remove from list", danger: true)) return false;
        int n = await Task.Run(() => lib.RemoveFromList(ids));
        ShowInfo(string.Format(Loc.T("{0} removed from your list"), n.ToString("N0")));
        after?.Invoke();
        return true;
    }

    // ---------- watch next ----------
    async void OnWatchNext(object sender, RoutedEventArgs e) => await Dialogs.WatchNext(this, () => all, () => view, ShowDetail, WatchedFromPick);

    void WatchedFromPick(Movie m)
    {
        MarkWatched(m, true);
        SaveUserData(() => lib.SetUser(new[] { m.K }, watched: true));
        itemOf.GetValueOrDefault(m)?.Refresh();
        ShowInfo(string.Format(Loc.T("Marked watched: {0}"), m.Display));
        AfterUserChange();
    }

    // ---------- commands ----------
    void OnUpdate(object sender, RoutedEventArgs e) => RunUpdate();

    void OnProblems(object sender, RoutedEventArgs e)
    {
        var w = new ProblemsWindow(key =>
        {
            var m = all.Find(x => x.K == key);
            if (m != null) ShowDetail(m);
            Activate();
        }, () =>
        {
            RefreshBadges();
            _ = LoadAsync();
        });
        w.Activate();
    }

    async void OnExport(object sender, RoutedEventArgs e) => await OpenExport();

    async void OnSettings(object sender, RoutedEventArgs e) => await Dialogs.Settings(this);

    void OnHelp(object sender, RoutedEventArgs e) => HelpWindow.Open();
}
