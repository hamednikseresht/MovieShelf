using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MovieShelf.Core;

namespace MovieShelf.App;

/// <summary>One TMDB credit, with the library movie it matches (if any) and the job it counts under.</summary>
sealed class CreditVm
{
    public TmdbCredit C { get; init; } = null!;
    public Movie? Mine { get; init; }
    public string Role { get; init; } = "";
}

/// <summary>A library movie the person worked on: the jobs, and what a job adds (the character, the exact crew job).</summary>
sealed class MineVm
{
    public Movie M { get; init; } = null!;
    public List<string> Roles { get; } = new();
    public Dictionary<string, string> Notes { get; } = new();

    public void Add(string role, string? note)
    {
        if (!Roles.Contains(role)) Roles.Add(role);
        if (!string.IsNullOrEmpty(note)) Notes.TryAdd(role, note);
    }
}

/// <summary>A person: photo, facts, biography, the movies of the library they worked on, and their TMDB filmography.</summary>
public sealed partial class PersonWindow : Window
{
    static readonly Dictionary<int, PersonWindow> open = new();
    static readonly object cacheLock = new();
    static (int Version, List<Movie> Movies)? movieCache;

    static readonly string[] RoleOrder = { "Actor", "Director", "Writer", "Producer", "Composer", "Cinematographer", "Editor" };
    const int PageSize = 60;
    const int CardPage = 14;

    readonly Library lib = App.Lib;
    readonly int nconst;
    readonly string name;
    readonly Action<int, string> filterLibrary;
    readonly Action<int> openMovie;
    readonly Func<bool, Task<PersonInfo>> loader;

    PersonInfo? info;
    List<MineVm> mine = new();
    List<CreditVm> credits = new();
    string role = "all";
    string sort = "new";
    int shown = PageSize;
    int cardsShown = CardPage;
    bool filling;
    int generation;

    /// <summary>Opens the profile of a person (or brings the open one forward). <paramref name="filterLibrary"/> is called for "Show in library",
    /// <paramref name="openMovie"/> with the IMDb number of a library movie the user clicked.</summary>
    public static void Open(int nconst, string name, Action<int, string> filterLibrary, Action<int> openMovie)
    {
        if (open.TryGetValue(nconst, out var w))
        {
            w.Activate();
            return;
        }
        w = new PersonWindow(nconst, name, filterLibrary, openMovie, null);
        open[nconst] = w;
        w.Activate();
    }

    /// <param name="loader">Where the profile comes from; null means Library.PersonProfileAsync. Tests pass a canned profile.</param>
    internal PersonWindow(int nconst, string name, Action<int, string> filterLibrary, Action<int> openMovie, Func<bool, Task<PersonInfo>>? loader)
    {
        this.nconst = nconst;
        this.name = name;
        this.filterLibrary = filterLibrary;
        this.openMovie = openMovie;
        this.loader = loader ?? (refresh => Task.Run(() => lib.PersonProfileAsync(nconst, refresh)));
        InitializeComponent();
        U2.Setup(this, AppTitleBar, "Person", 1000, 860, Relocalise);
        Texts();
        NameText.Text = name;
        Initials.Text = InitialsOf(name);
        Title = name;
        AppTitleBar.Title = name;
        SortBox.SelectedIndex = 0;
        Closed += (_, _) => open.Remove(nconst);
        _ = LoadAsync(false);
    }

    void Texts()
    {
        Root.FlowDirection = U2.Flow;
        Loc.Apply(Root);
        NoKey.Title = Loc.T("Add a TMDB key in Settings for photo and biography");
        NoKey.Message = Loc.T("The photo, the biography and the full filmography come from TMDB. A free key is enough.");
        FilterBtn.Content = Loc.T("Show in library");
    }

    void Relocalise()
    {
        Texts();
        Title = name;
        AppTitleBar.Title = name;
        NameText.Text = name;
        // a closed ComboBox keeps showing the text it had until the selection changes
        int picked = SortBox.SelectedIndex;
        SortBox.SelectedIndex = -1;
        SortBox.SelectedIndex = picked;
        if (info != null) Build();
    }

    static string InitialsOf(string n) => string.Concat(n.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    // ---------- loading ----------
    async Task LoadAsync(bool refresh)
    {
        int gen = ++generation;
        Busy.Visibility = Visibility.Visible;
        Err.IsOpen = false;
        RefreshBtn.IsEnabled = false;
        try
        {
            var infoTask = loader(refresh);
            var moviesTask = Task.Run(() => Movies(lib));
            var i = await infoTask;
            var movies = await moviesTask;
            var (m, c) = await Task.Run(() => Collect(movies, i));
            if (gen != generation) return;
            info = i;
            mine = m;
            credits = c;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or Microsoft.Data.Sqlite.SqliteException or InvalidOperationException
                                      or TimeoutException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (gen != generation) return;
            Busy.Visibility = Visibility.Collapsed;
            RefreshBtn.IsEnabled = true;
            Err.Severity = InfoBarSeverity.Error;
            Err.Title = Loc.T("Could not load the profile.");
            Err.Message = e.Message;
            Err.IsOpen = true;
            return;
        }
        Busy.Visibility = Visibility.Collapsed;
        RefreshBtn.IsEnabled = true;
        role = "all";
        shown = PageSize;
        cardsShown = CardPage;
        Build();
    }

    /// <summary>The library, loaded once and reused until the library changes. Runs on a worker thread.</summary>
    static List<Movie> Movies(Library lib)
    {
        lock (cacheLock)
        {
            if (movieCache is { } c && c.Version == lib.Version) return c.Movies;
            var l = lib.LoadMovies();
            movieCache = (lib.Version, l);
            return l;
        }
    }

    /// <summary>The job name of an IMDb or TMDB crew credit, as the web app groups them.</summary>
    static string RoleOfJob(string? job)
    {
        string j = (job ?? "").ToLowerInvariant().Replace('_', ' ');
        if (j.Contains("producer")) return "Producer";
        if (j.Contains("composer")) return "Composer";
        if (j.Contains("photography") || j.Contains("cinematograph")) return "Cinematographer";
        if (j.Contains("editor")) return "Editor";
        if (j.StartsWith("casting")) return "Casting director";
        if (j.StartsWith("archive")) return "Archive footage";
        return j.Length > 0 ? Cap(j) : "Other";
    }

    /// <summary>The person's library movies by job, from the cast and crew of each movie and from the TMDB credits that match one. Runs on a worker thread:
    /// the crew lists are parsed on first use.</summary>
    (List<MineVm>, List<CreditVm>) Collect(List<Movie> movies, PersonInfo i)
    {
        var byMovie = new Dictionary<Movie, MineVm>();
        MineVm Get(Movie m) => byMovie.TryGetValue(m, out var v) ? v : byMovie[m] = new MineVm { M = m };
        foreach (var m in movies)
        {
            if (m.Tconst == null || !m.HasPerson(nconst)) continue;
            var v = Get(m);
            if (m.Dir.Any(p => p.Id == nconst)) v.Add("Director", null);
            if (m.Wri.Any(p => p.Id == nconst)) v.Add("Writer", null);
            foreach (var p in m.Cast.Where(p => p.Id == nconst)) v.Add("Actor", p.Role);
            foreach (var p in m.Crew.Where(p => p.Id == nconst))
            {
                string job = RoleOfJob(p.Role);
                v.Add(job, !string.IsNullOrEmpty(p.Role) && job != Cap(p.Role) ? Cap(p.Role) : null);
            }
        }
        var list = Match(i.Profile?.Credits ?? new(), movies);
        foreach (var c in list)
            if (c.Mine != null) Get(c.Mine).Add(c.Role, c.C.Detail);
        return (byMovie.Values.OrderByDescending(x => x.M.Year ?? 0).ToList(), list);
    }

    /// <summary>Pairs each TMDB credit with the library movie of the same title and year (one year of slack), as the web app does.</summary>
    static List<CreditVm> Match(List<TmdbCredit> list, List<Movie> movies)
    {
        var idx = new Dictionary<string, List<Movie>>();
        foreach (var m in movies)
        {
            if (m.Tconst == null) continue;
            foreach (var t in new[] { m.En, m.Title, m.Orig }.Where(x => !string.IsNullOrEmpty(x)).Select(x => Movie.Fold(x)).Distinct())
            {
                if (!idx.TryGetValue(t, out var l)) idx[t] = l = new();
                l.Add(m);
            }
        }
        return list.Where(c => !string.IsNullOrEmpty(c.Title)).Select(c =>
        {
            Movie? hit = null;
            if (idx.TryGetValue(Movie.Fold(c.Title), out var l))
                hit = l.FirstOrDefault(m => c.Year == null || m.Year == null || Math.Abs(m.Year.Value - c.Year.Value) <= 1);
            return new CreditVm { C = c, Mine = hit, Role = Array.IndexOf(RoleOrder, c.Role) >= 0 ? c.Role : RoleOfJob(c.Role) };
        }).ToList();
    }

    // ---------- build the page ----------
    void Build()
    {
        var i = info!;
        var pr = i.Profile;
        string shownName = !string.IsNullOrEmpty(i.Name) ? i.Name! : name;
        NameText.Text = shownName;
        Initials.Text = InitialsOf(shownName);
        Title = shownName;
        AppTitleBar.Title = shownName;

        // photo
        if (i.Photo && File.Exists(lib.PersonPhotoPath(nconst)))
        {
            Photo.Source = new BitmapImage { DecodePixelWidth = 300, UriSource = new Uri(lib.PersonPhotoPath(nconst)) };
            Initials.Visibility = Visibility.Collapsed;
        }
        else
        {
            Photo.Source = null;
            Initials.Visibility = Visibility.Visible;
        }

        // professions
        Chips.Children.Clear();
        var profs = i.Prof.Count > 0 ? i.Prof : !string.IsNullOrEmpty(pr?.Department) ? new List<string> { pr!.Department! } : new();
        foreach (var p in profs)
            Chips.Children.Add(new Border
            {
                Child = new TextBlock { Text = Loc.T(Cap(p)), FontSize = 12 }, Padding = new Thickness(8, 2, 8, 3), CornerRadius = new CornerRadius(10),
                Background = U2.Res("CardBackgroundFillColorSecondaryBrush"), BorderBrush = U2.Res("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(1),
            });
        Chips.Visibility = profs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // facts
        Facts.Children.Clear();
        string? born = !string.IsNullOrEmpty(pr?.Birthday) ? pr!.Birthday : i.Birth?.ToString();
        string? died = !string.IsNullOrEmpty(pr?.Deathday) ? pr!.Deathday : i.Death?.ToString();
        int? by = Year(born), dy = Year(died);
        if (born != null)
        {
            string age = by != null ? (died != null && dy != null ? $" ({U2.Fmt("died at {0}", dy - by)})" : $" ({U2.Fmt("{0} years old", DateTime.Now.Year - by)})") : "";
            AddFact("Born", Iso(born) + (string.IsNullOrEmpty(pr?.Place) ? "" : " · " + pr!.Place) + (died == null ? age : ""));
        }
        else if (!string.IsNullOrEmpty(pr?.Place)) AddFact("Born in", pr!.Place!);
        if (died != null) AddFact("Died", Iso(died) + (by != null && dy != null ? $" ({U2.Fmt("aged {0}", dy - by)})" : ""));
        if (pr?.Aka is { Count: > 0 }) AddFact("Also known as", string.Join(" · ", pr.Aka.Take(8)));
        AddFact("In your library", U2.Fmt(mine.Count == 1 ? "{0} movie" : "{0} movies", mine.Count.ToString("N0")));

        // buttons
        FilterBtn.Visibility = mine.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FilterBtn.Content = Loc.T("Show in library");
        TmdbBtn.Visibility = pr?.TmdbId != null ? Visibility.Visible : Visibility.Collapsed;
        SiteBtn.Visibility = Uri.TryCreate(pr?.Homepage, UriKind.Absolute, out var site) && site.Scheme is "http" or "https" ? Visibility.Visible : Visibility.Collapsed;
        RefreshBtn.Visibility = i.Tmdb ? Visibility.Visible : Visibility.Collapsed;

        // notices
        NoKey.IsOpen = !i.Tmdb;
        if (!string.IsNullOrEmpty(i.Error))
        {
            Err.Severity = InfoBarSeverity.Error;
            Err.Title = Loc.T("Could not reach TMDB.");
            Err.Message = i.Error;
            Err.IsOpen = true;
        }
        else if (i.Tmdb && pr == null)
        {
            Err.Severity = InfoBarSeverity.Informational;
            Err.Title = Loc.T("TMDB has no profile for this person.");
            Err.Message = "";
            Err.IsOpen = true;
        }
        else Err.IsOpen = false;

        // biography
        string bio = pr?.Biography?.Trim() ?? "";
        BioBox.Visibility = bio.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        BioText.Text = bio;
        BioText.MaxLines = 6;
        BioMore.Visibility = bio.Length > 420 ? Visibility.Visible : Visibility.Collapsed;
        BioMore.Content = Loc.T("Read more");

        BuildTabs();
        FillCards();
        FillCredits();
    }

    /// <summary>Keeps a date such as 1970-07-30 in reading order inside right-to-left text.</summary>
    static string Iso(string s) => "⁦" + s + "⁩";

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].Replace('_', ' ');
    static int? Year(string? date) => date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), out int y) ? y : null;

    void AddFact(string label, string value)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        t.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = Loc.T(label) + "  ", Foreground = U2.Res("TextFillColorSecondaryBrush") });
        t.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = value });
        Facts.Children.Add(t);
    }

    void OnBioMore(object sender, RoutedEventArgs e)
    {
        bool open = BioText.MaxLines == 0;
        BioText.MaxLines = open ? 6 : 0;
        BioMore.Content = Loc.T(open ? "Read more" : "Show less");
    }

    // ---------- role tabs ----------
    int RoleCount(string key) => mine.Count(m => m.Roles.Contains(key)) + credits.Count(c => c.Mine == null && c.Role == key);

    void BuildTabs()
    {
        var keys = credits.Select(c => c.Role).Concat(mine.SelectMany(m => m.Roles)).Where(r => !string.IsNullOrEmpty(r)).Distinct()
            .OrderBy(r => Array.IndexOf(RoleOrder, r) is var ix and >= 0 ? ix : 99).ThenBy(r => r, StringComparer.CurrentCultureIgnoreCase).ToList();
        filling = true;
        Tabs.Items.Clear();
        if (keys.Count > 1)
        {
            Tabs.Items.Add(new SelectorBarItem { Text = Loc.T("All jobs") + $"  {keys.Sum(RoleCount):N0}", Tag = "all" });
            foreach (var k in keys) Tabs.Items.Add(new SelectorBarItem { Text = Loc.T(k) + $"  {RoleCount(k):N0}", Tag = k });
            var sel = Tabs.Items.Cast<SelectorBarItem>().FirstOrDefault(t => (string)t.Tag == role) ?? (SelectorBarItem)Tabs.Items[0];
            role = (string)sel.Tag;
            Tabs.SelectedItem = sel;
            TabsBox.Visibility = Visibility.Visible;
        }
        else
        {
            role = "all";
            TabsBox.Visibility = Visibility.Collapsed;
        }
        filling = false;
    }

    void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (filling || sender.SelectedItem is not SelectorBarItem t) return;
        role = (string)t.Tag;
        shown = PageSize;
        cardsShown = CardPage;
        FillCards();
        FillCredits();
    }

    void OnSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortBox.SelectedItem is not ComboBoxItem it) return;
        sort = (string)it.Tag;
        shown = PageSize;
        if (info != null) FillCredits();
    }

    // ---------- the person's movies in the library ----------
    void FillCards()
    {
        Cards.Children.Clear();
        var list = mine.Where(x => role == "all" || x.Roles.Contains(role)).ToList();
        LibBox.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LibHead.Text = Loc.T("In your library") + $"  ({list.Count:N0})";
        CardsMore.Visibility = list.Count > cardsShown ? Visibility.Visible : Visibility.Collapsed;
        CardsMore.Content = U2.Fmt("Show all {0}", list.Count.ToString("N0"));
        foreach (var x in list.Take(cardsShown))
        {
            var m = x.M;
            var poster = new Grid { Width = 120, Height = 180, CornerRadius = new CornerRadius(6), Background = U2.Res("CardBackgroundFillColorSecondaryBrush") };
            if (m.HasPoster && File.Exists(lib.PosterPath(m)))
                poster.Children.Add(new Image { Stretch = Stretch.UniformToFill, Source = new BitmapImage { DecodePixelWidth = 240, UriSource = new Uri(lib.PosterPath(m)) } });
            else
                poster.Children.Add(new TextBlock
                {
                    Text = m.Display, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8), Foreground = U2.Res("TextFillColorSecondaryBrush"), FlowDirection = FlowDirection.LeftToRight,
                });
            var sp = new StackPanel { Width = 120, Spacing = 2 };
            sp.Children.Add(poster);
            sp.Children.Add(new TextBlock { Text = m.Display, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, MaxLines = 2, MinHeight = 40, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left });
            string sub = string.Join(" · ", new[] { m.Year?.ToString(), role == "all" ? string.Join("/", x.Roles.Select(Loc.T)) : x.Notes.GetValueOrDefault(role) }.Where(t => !string.IsNullOrEmpty(t)));
            sp.Children.Add(new TextBlock { Text = sub, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = U2.Res("TextFillColorSecondaryBrush") });
            var b = new Button { Content = sp, Padding = new Thickness(0), Background = null, BorderThickness = new Thickness(0), Tag = m.Tconst, HorizontalContentAlignment = HorizontalAlignment.Left };
            ToolTipService.SetToolTip(b, m.Display);
            b.Click += OnCardClick;
            Cards.Children.Add(b);
        }
    }

    void OnMoreCards(object sender, RoutedEventArgs e)
    {
        cardsShown = int.MaxValue;
        FillCards();
    }

    void OnCardClick(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is int tc) OpenMovie(tc);
    }

    void OpenMovie(int tconst)
    {
        openMovie(tconst);
        App.Main?.Activate();
    }

    // ---------- filmography ----------
    IEnumerable<CreditVm> Sorted(IEnumerable<CreditVm> src) => sort switch
    {
        "old" => src.OrderBy(c => c.C.Year ?? int.MaxValue).ThenByDescending(c => c.C.Votes),
        "votes" => src.OrderByDescending(c => c.C.Votes).ThenByDescending(c => c.C.Year ?? 0),
        _ => src.OrderByDescending(c => c.C.Year ?? int.MaxValue).ThenByDescending(c => c.C.Votes),   // films without a year (announced) first
    };

    void FillCredits()
    {
        Credits.Children.Clear();
        var all = Sorted(credits.Where(c => role == "all" || c.Role == role)).ToList();
        FilmBox.Visibility = credits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        int inLib = all.Count(c => c.Mine != null);
        FilmHead.Text = Loc.T("Filmography") + $"  ({all.Count:N0})" + (inLib > 0 ? "  ·  " + U2.Fmt("{0} in your library", inLib) : "");
        foreach (var c in all.Take(shown)) Credits.Children.Add(CreditRow(c));
        MoreBtn.Visibility = all.Count > shown ? Visibility.Visible : Visibility.Collapsed;
        MoreBtn.Content = U2.Fmt("Show more ({0} left)", all.Count - shown);
    }

    void OnMoreCredits(object sender, RoutedEventArgs e)
    {
        shown += PageSize * 2;
        FillCredits();
    }

    UIElement CreditRow(CreditVm c)
    {
        var g = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 5, 0, 5), BorderBrush = U2.Res("DividerStrokeColorDefaultBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = c.C.Year?.ToString() ?? "–", Foreground = U2.Res("TextFillColorTertiaryBrush"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) });

        var mid = new StackPanel { Spacing = 1 };
        if (c.Mine?.Tconst is int tc)
        {
            var link = new HyperlinkButton { Content = c.C.Title, Padding = new Thickness(0), Tag = tc, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
            ToolTipService.SetToolTip(link, Loc.T("Open in your library"));
            link.Click += (_, _) => OpenMovie(tc);
            mid.Children.Add(link);
        }
        else
            mid.Children.Add(new TextBlock { Text = c.C.Title, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 0) });
        string sub = string.Join(" · ", new[] { role == "all" ? Loc.T(c.Role) : null, c.C.Detail, c.C.Votes > 0 ? $"{U2.Votes(c.C.Votes)} {Loc.T("votes")}" : null }.Where(x => !string.IsNullOrEmpty(x)));
        if (sub.Length > 0) mid.Children.Add(new TextBlock { Text = sub, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = U2.Res("TextFillColorSecondaryBrush") });
        Grid.SetColumn(mid, 1);
        g.Children.Add(mid);

        // at the end of the row: a quiet marker for a movie you have, the wishlist for one you do not
        var mark = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) };
        if (c.Mine != null)
        {
            mark.Children.Add(new FontIcon { Glyph = "", FontSize = 12, Foreground = U2.Res("SystemFillColorSuccessBrush"), VerticalAlignment = VerticalAlignment.Center });
            mark.Children.Add(new TextBlock { Text = Loc.T("In your library"), FontSize = 12, Foreground = U2.Res("SystemFillColorSuccessBrush") });
        }
        else
        {
            var wish = new Button { Content = Loc.T("Wishlist"), FontSize = 12, Padding = new Thickness(8, 2, 8, 3) };
            ToolTipService.SetToolTip(wish, Loc.T("not in your library"));
            wish.Click += (_, _) => _ = AddToWishlist(c, wish);
            mark.Children.Add(wish);
        }
        Grid.SetColumn(mark, 2);
        g.Children.Add(mark);
        return g;
    }

    async Task AddToWishlist(CreditVm c, Button b)
    {
        b.IsEnabled = false;
        string who = info?.Name is { Length: > 0 } n ? n : name;
        var item = new WishNew(c.C.Title!, c.C.Year, null, $"{who}: {c.Role}", "Person");
        try
        {
            int added = await Task.Run(() => lib.WishAdd(new[] { item }));
            b.Content = Loc.T(added > 0 ? "On the wishlist" : "Already on the wishlist.");
            if (added > 0) WishlistWindow.NotifyChanged();
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            b.IsEnabled = true;
            Err.Severity = InfoBarSeverity.Error;
            Err.Title = Loc.T("Could not add to the wishlist.");
            Err.Message = e.Message;
            Err.IsOpen = true;
        }
    }

    // ---------- buttons ----------
    void OnShowInLibrary(object sender, RoutedEventArgs e)
    {
        filterLibrary(nconst, info?.Name is { Length: > 0 } n ? n : name);
        App.Main?.Activate();
    }

    void OnOpenImdb(object sender, RoutedEventArgs e) => U2.Launch($"https://www.imdb.com/name/nm{nconst:0000000}/");
    void OnOpenTmdb(object sender, RoutedEventArgs e) { if (info?.Profile?.TmdbId is int id) U2.Launch($"https://www.themoviedb.org/person/{id}"); }
    void OnOpenSite(object sender, RoutedEventArgs e) { if (info?.Profile?.Homepage is string h) U2.Launch(h); }
    void OnRefresh(object sender, RoutedEventArgs e) => _ = LoadAsync(true);
}
