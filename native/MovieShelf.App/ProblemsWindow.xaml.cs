using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using MovieShelf.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace MovieShelf.App;

/// <summary>The sizes and short facts the rows of the Problems window show, formatted as the web page does.</summary>
internal static class PrFmt
{
    public static string Size(long? bytes)
    {
        if (bytes is not long n || n <= 0) return "";
        string s = n >= 1_000_000_000 ? (n / 1e9).ToString(n >= 10_000_000_000 ? "0.0" : "0.00") + " GB" : Math.Max(1, (long)Math.Round(n / 1e6, MidpointRounding.AwayFromZero)) + " MB";
        return Loc.IsFa ? "⁦" + s + "⁩" : s;
    }

    /// <summary>"IMDb 8.1 · 1.2M votes".</summary>
    public static string Meta(double? rating, int? votes) => string.Join(" · ", new[]
    {
        rating is > 0 ? "IMDb " + rating.Value.ToString("0.0") : null,
        votes is > 0 ? string.Format(Loc.T("{0} votes"), U2.Votes(votes)) : null,
    }.Where(x => x != null));

    /// <summary>Keeps a name or number in its own direction inside right-to-left text.</summary>
    public static string Isolate(string s) => Loc.IsFa ? "⁨" + s + "⁩" : s;

    /// <summary>The matcher's reason in the current language. The dash and colon forms are both keys: the Python app words them with a dash.</summary>
    public static string Reason(string reason)
    {
        var m = Regex.Match(reason, @"^Year in the name \((\d+)\) differs from IMDb \((\d+)\)$");
        return m.Success ? string.Format(Loc.T("Year in the name ({0}) differs from IMDb ({1})"), m.Groups[1].Value, m.Groups[2].Value) : Loc.T(reason);
    }

    public static string TypeLabel(string? type) => type switch
    {
        null or "" => "",
        "movie" => Loc.T("Movie"), "tvMovie" => Loc.T("TV movie"), "video" => Loc.T("Video"), "short" => Loc.T("Short"), "tvShort" => Loc.T("TV short"),
        "tvSpecial" => Loc.T("TV special"), "tvMiniSeries" => Loc.T("Mini-series"), "tvSeries" => Loc.T("TV series"), _ => type,
    };
}

public sealed class CandVm
{
    public Candidate2 C { get; init; } = null!;
    public MatchVm Owner { get; init; } = null!;
    public string Text => C.Title + (C.Year != null ? $" {C.Year}" : "");
    public string Detail => string.Join(" · ", new[] { PrFmt.TypeLabel(C.Type), C.Orig, PrFmt.Meta(C.Rating, C.Votes), $"tt{C.Tconst:0000000}" }.Where(x => !string.IsNullOrEmpty(x)));
    public string ViewLabel => Loc.T("View");
    public string UseLabel => Loc.T("Use this");
}

public sealed class MatchVm : INotifyPropertyChanged
{
    List<CandVm>? cands;
    string? done;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MatchProblem P { get; private set; }

    public MatchVm(MatchProblem p, string? done)
    {
        P = p;
        this.done = done;
    }

    void Changed(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>The misspelling search came back with more candidates: only the list of this row changes, so the scroll position stays.</summary>
    public void Update(MatchProblem p)
    {
        P = p;
        cands = null;
        Changed(nameof(Cands), nameof(CandsVis), nameof(CandsLabel));
    }

    public string Raw => P.Raw;
    public Uri GoogleUri => GoogleLink.For(P.Current?.Title ?? P.Raw, P.Current?.Year?.ToString());
    public string GoogleTip => GoogleLink.Tip;
    public List<IssuePathVm> PathLinks => P.Paths.Select(x => new IssuePathVm { Path = x }).ToList();
    public Visibility PathsVis => U2.Vis(P.Paths.Count > 0);
    public string Reason => PrFmt.Reason(P.Reason);
    public string NowLabel => Loc.T("Now matched to:");
    public string CurrentTitle => P.Current?.Title ?? "";
    public string CurrentMeta => P.Current == null ? "" : string.Join(" ", new[] { P.Current.Year?.ToString(), PrFmt.Meta(P.Current.Rating, P.Current.Votes) }.Where(x => !string.IsNullOrEmpty(x)));
    public Visibility CurrentVis => U2.Vis(P.Current != null);
    public string LooksRight => Loc.T("Looks right");
    public string CandsLabel => Loc.T(P.Current != null ? "Or maybe:" : "Possible matches:");
    public Visibility CandsVis => U2.Vis(P.Cands.Count > 0);
    public List<CandVm> Cands => cands ??= P.Cands.Select(c => new CandVm { C = c, Owner = this }).ToList();
    public string SearchLabel => Loc.T("Search IMDb…");
    public string NotImdbLabel => Loc.T("Not on IMDb");
    public string RemoveLabel => Loc.T("Remove from list");
    public string ConfLabel => Loc.T(P.Conf switch
    {
        "online" => "Online match", "noyear" => "No year in name", "review" => "Ambiguous", "fuzzy" => "Fuzzy match", "none" => "Not found on IMDb", _ => P.Conf,
    });
    public Brush ConfBg => U2.Badge(P.Conf == "none" ? "unmatched" : "better").Bg;
    public Brush ConfFg => U2.Badge(P.Conf == "none" ? "unmatched" : "better").Fg;

    /// <summary>The row stays in the list with this message and without its buttons, as the web page leaves it.</summary>
    public string? Done
    {
        get => done;
        set
        {
            done = value;
            Changed(nameof(DoneText), nameof(DoneVis), nameof(OpenVis));
        }
    }

    public string DoneText => done ?? "";
    public Visibility DoneVis => U2.Vis(done != null);
    public Visibility OpenVis => U2.Vis(done == null);
}

/// <summary>The magnifier next to a title: the title and year searched on Google.</summary>
internal static class GoogleLink
{
    public static Uri For(string? title, string? year) =>
        new("https://www.google.com/search?q=" + Uri.EscapeDataString(string.Join(" ", new[] { title, year, "movie" }.Where(x => !string.IsNullOrWhiteSpace(x)))));
    public static string Tip => Loc.T("Search Google");
}

public sealed class IssuePathVm
{
    public string Path { get; init; } = "";
    public string Short => U2.ShortPath(Path, 90);
    public string ShowLabel => Loc.T("Show in folder");
}

public sealed class IssueFileVm
{
    public IssueFile F { get; init; } = null!;
    public IssueVm Owner { get; init; } = null!;
    public string Raw => F.Raw;
    public string Quality => F.Quality;
    public string SizeText => PrFmt.Size(F.Size);
    public Visibility SizeVis => U2.Vis(F.Size is > 0);
    public string VerdictText => Loc.T(F.Verdict switch { "keep" => "Keep", "lower" => "Lower quality", "same" => "Same quality", "unsure" => "Cannot compare", _ => "" });
    public Visibility VerdictVis => U2.Vis(F.Verdict != null);
    string Tone => F.Verdict switch { "keep" => "new", "lower" => "worse", "same" => "unmatched", _ => "" };
    public Brush VerdictBg => U2.Badge(Tone).Bg;
    public Brush VerdictFg => U2.Badge(Tone).Fg;
    public string RemoveLabel => Loc.T("Remove from list");
    public Visibility RemoveVis => U2.Vis(Owner.R.Files.Count > 1);
    public string FixLabel => Owner.FixLabel;
    public Visibility FixVis => U2.Vis(Owner.CanFix && Owner.R.Files.Count > 1);
    public List<IssuePathVm> Paths => F.Paths.Select(p => new IssuePathVm { Path = p }).ToList();
    public Visibility PathsVis => U2.Vis(F.Paths.Count > 0);
}

public sealed class JunkVm
{
    public IssueJunk J { get; init; } = null!;
    public string Status => J.NFiles == 0 ? Loc.T("Empty folder") : string.Format(Loc.T(J.NFiles == 1 ? "{0} file, no video" : "{0} files, no video"), J.NFiles);
    public string SizeText => PrFmt.Size(J.Size);
    public Visibility SizeVis => U2.Vis(J.Size > 0);
    public string Path => J.Path;
    public string OpenLabel => Loc.T("Open folder");
    public string Names => string.Join(", ", J.Names);
    public Visibility NamesVis => U2.Vis(J.Names.Count > 0);
    public Brush BadBg => U2.Badge("unmatched").Bg;
    public Brush BadFg => U2.Badge("unmatched").Fg;
}

public sealed class IssueVm : INotifyPropertyChanged
{
    readonly string area, sub;
    readonly bool canOpen;
    bool used;
    List<IssueFileVm>? files;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IssueVm(IssueRow r, string area, string sub, bool canOpen)
    {
        R = r;
        this.area = area;
        this.sub = sub;
        this.canOpen = canOpen;
    }

    public IssueRow R { get; }
    public Uri GoogleUri => GoogleLink.For(R.Title, R.Year?.ToString());
    public string GoogleTip => GoogleLink.Tip;
    public string Title => R.Title;
    public string Year => R.Year?.ToString() ?? "";
    public string Meta => PrFmt.Meta(R.Rating, R.Votes);
    public Visibility ImdbVis => U2.Vis(R.Tconst != null);
    public string ImdbTip => Loc.T("Open on IMDb");
    public string Note => string.Format(Loc.T(R.NoteFormat), R.NoteArgs.Select(PrFmt.Isolate).ToArray<object>());
    public Visibility AlertVis => U2.Vis(R.Sub != "versions");
    public Brush NoteBrush => U2.Res(R.Sub == "versions" ? "TextFillColorSecondaryBrush" : "SystemFillColorCautionBrush");
    public List<JunkVm> Junk => R.Junk.Select(j => new JunkVm { J = j }).ToList();
    public Visibility JunkVis => U2.Vis(R.Junk.Count > 0);
    public List<IssueFileVm> Files => files ??= R.Files.Select(f => new IssueFileVm { F = f, Owner = this }).ToList();

    public string SeriesLabel => Loc.T(R.Sub == "series" ? "Not a series" : "This is a series");
    public Visibility SeriesVis => U2.Vis(area == "dupes");
    public string RemoveLabel => Loc.T(R.Files.Count > 1 ? "Remove all from list" : "Remove from list");
    public string PosterLabel => Loc.T("Add a poster…");
    public Visibility PosterVis => U2.Vis(R.Tconst != null && sub == "noposter");
    public string OpenLabel => Loc.T("Open movie");
    public Visibility OpenVis => U2.Vis(canOpen);
    public string CopyLabel => Loc.T(R.Sub == "copies" ? "Copy extra paths" : R.RemovalsHavePaths ? "Copy paths to remove" : "Copy names to remove");
    public Visibility CopyVis => U2.Vis(R.HasCopiesToRemove || R.Sub == "copies");
    public string WishLabel => Loc.T("Add to wishlist");
    public Visibility WishVis => U2.Vis(area == "quality" && sub == "lowq");
    public string FixLabel => Loc.T(area == "dupes" ? "Not the same movie? Fix match" : "Wrong movie? Fix match");
    /// <summary>Fix match is offered when the match may be wrong. With several files the user picks which one is wrong (the app cannot know), so each file has its own button.</summary>
    public bool CanFix => (area == "other" && sub is "namediff" or "yeardiff" or "runtime") || (area == "dupes" && R.Sub is not ("versions" or "copies"));
    public Visibility FixVis => U2.Vis(CanFix && R.Files.Count <= 1);
    public string IgnoreLabel => Loc.T(R.Ignored ? "Restore" : "Ignore");
    public double RowOpacity => R.Ignored || used ? 0.55 : 1;

    /// <summary>The match of this row was changed: the row dims until the list is built again.</summary>
    public void MarkUsed()
    {
        used = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowOpacity)));
    }
}

/// <summary>Everything that needs a look: matching, duplicates, quality, other problems. Tab by tab the
/// same controls as the Problems dialog of the web page (app.js probList). Row data is only worked out for the rows on screen.</summary>
public sealed partial class ProblemsWindow : Window
{
    static readonly (string Key, string Label)[] AreaDefs = { ("match", "Matching"), ("dupes", "Duplicates"), ("quality", "Quality"), ("other", "Other") };
    static readonly (string Key, string Label)[] MatchTabs =
    {
        ("all", "All"), ("none", "Not found"), ("review", "Ambiguous"), ("fuzzy", "Spelling differs"), ("noyear", "No year"), ("online", "Online match"),
    };
    static readonly Dictionary<string, (string Key, string Label)[]> SubDefs = new()
    {
        ["dupes"] = new[]
        {
            ("all", "All"), ("same", "Same quality"), ("lower", "Lower copy"), ("versions", "Dubbed + original"), ("mixed", "Different movies?"), ("copies", "Same file twice"), ("series", "Series"),
        },
        ["quality"] = new[] { ("lowq", "Low quality"), ("unknownq", "Unknown quality"), ("realq", "Name vs picture"), ("dubonly", "Dubbed only") },
        ["other"] = new[]
        {
            ("novideo", "No video file"), ("namediff", "Name differs"), ("yeardiff", "Year differs"), ("runtime", "Length differs"), ("fatitle", "Persian title"), ("nonfilm", "TV, not a film"), ("noposter", "No poster"),
        },
    };
    static readonly Dictionary<string, string> Help = new()
    {
        ["all"] = "Movies you have more than once. Each copy is marked Keep, Lower quality or Same quality. The app never deletes files: copy the names of the extra copies and remove them yourself, or ignore the movie if you want to keep both. If the copies are really different movies (Vol. 1 and Vol. 2, for example), use Fix match on the extra one.",
        ["same"] = "Two or more copies of the same quality, so one is redundant. Often the same movie listed under two different names.",
        ["lower"] = "A better copy is already in your library next to a lower-quality one. The lower one can usually go.",
        ["versions"] = "One dubbed and one original-audio copy. Not redundant, so nothing to remove; ignore them once you have seen them.",
        ["mixed"] = "Files matched to one IMDb title whose names differ by a number (Vol. 1 / Vol. 2, Star Wars 4 / 6) or that look like bonus material. These are probably different movies: use Fix match on the wrong one.",
        ["series"] = "Episodes of a TV series (file names with S01E02, or IMDb says it is a series). They are not copies and are not counted as problems. If one of them is a movie after all, press Not a series.",
        ["copies"] = "The same file name found in more than one movie folder (needs your movie folders in Settings). Keep one, remove the others.",
        ["lowq"] = "Movies where your best copy is SD, 480p or 576p. The most popular come first: the best candidates for an upgrade.",
        ["unknownq"] = "No resolution appears in the file name, so quality cannot be compared or checked.",
        ["dubonly"] = "Movies where every copy is Farsi-dubbed. Useful if you also want the original audio.",
        ["yeardiff"] = "The year in the file name is two or more years from IMDb’s year. The match may be a different movie.",
        ["namediff"] = "The name of the file is not the real name of the film on IMDb: a typo, a missing word, a word that is not part of the name, or another language. The most different names come first. Fix the file name, or use Fix match if it is the wrong film. Files already in Matching (unsure matches) are not repeated here.",
        ["nonfilm"] = "IMDb lists these as TV specials or TV shorts, not films. TV series and mini-series are not problems: they are in the Series section of the main list.",
        ["realq"] = "The file name says one resolution but ffprobe sees a smaller picture: a mislabelled or upscaled file. Needs ffprobe (Settings → General → Movie folders).",
        ["runtime"] = "The length of the file is far from IMDb’s runtime: a cut-short download, a different cut, or the wrong movie. Needs ffprobe.",
        ["fatitle"] = "The Persian title from TMDB is written in Latin letters. Open the movie and use Edit next to Persian to type the right one; your edit is kept.",
        ["noposter"] = "No poster was found for these. Retry downloads the missing ones from IMDb, or use Add a poster to choose an image file, or paste an image address.",
        ["novideo"] = "The folder of this title (in your movie folders) has no video file: it is empty, or holds only junk such as .nfo, .jpg or .url files, or only a sample. Remove the title from your list. This hides it in the library; your list files and folders are not changed, and it stays out of every update. Needs your movie folders in Settings.",
    };
    const string MatchHelp = "These names could not be matched for sure: a different name on IMDb, a wrong year, or several movies with the same title. Confirm the current match, pick another, or search.";

    // the page keeps the open tab between openings of the dialog
    static string area = "match";
    static readonly Dictionary<string, string> subs = new() { ["dupes"] = "all", ["quality"] = "lowq", ["other"] = "namediff" };

    readonly Library lib = App.Lib;
    readonly Action<string>? openMovie;
    readonly Action? changed;
    readonly Dictionary<int, string> doneRows = new();
    readonly Dictionary<int, MatchVm> matchVms = new();
    readonly List<ToggleButton> chips = new();
    readonly DispatcherQueueTimer toastTimer;
    List<MatchProblem> matches = new();
    IssueReport report = new();
    ObservableCollection<IssueVm> issueRows = new();
    string matchTab = "all";
    string? chipsFor;
    bool showIgnored, hasFolders, recheck, recheckDone, loaded;
    int generation;

    /// <summary>Opens the window.</summary>
    /// <param name="openMovie">Shows a movie in the main window, by its key (<see cref="Movie.K"/>); without it the Open movie button is hidden.</param>
    /// <param name="changed">Runs after the list of movies changed here (a row removed, a match changed, a poster added), so the main window can reload.</param>
    public ProblemsWindow(Action<string>? openMovie = null, Action? changed = null)
    {
        this.openMovie = openMovie;
        this.changed = changed;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Chrome.Attach(this, Relocalise);
        var ask = new Button { Content = "?", Padding = new Thickness(11, 3, 11, 4), Margin = new Thickness(0, 0, 8, 0) };
        ToolTipService.SetToolTip(ask, Loc.T("Help"));
        ask.Click += (_, _) => HelpWindow.Open("problems");
        AppTitleBar.RightHeader = ask;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 880));
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "MovieShelf.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        toastTimer = DispatcherQueue.CreateTimer();
        toastTimer.Interval = TimeSpan.FromSeconds(6);
        toastTimer.Tick += (_, _) => { toastTimer.Stop(); Toast.IsOpen = false; };
        foreach (var (key, _) in AreaDefs) Areas.Items.Add(new SelectorBarItem { Tag = key });
        lib.JobChanged += OnJobChanged;
        Closed += (_, _) => lib.JobChanged -= OnJobChanged;
        IssueList.ItemsSource = issueRows;
        Relocalise();
        _ = ReloadAsync();
    }

    /// <summary>Puts all text of the window in the current language and the layout in its direction.</summary>
    void Relocalise()
    {
        Root.FlowDirection = U2.Flow;
        Title = AppTitleBar.Title = Loc.T("Problems");
        CheckAgainBtn.Content = Loc.T(recheck ? "Checking…" : "Check again");
        ToolTipService.SetToolTip(CheckAgainBtn, Loc.T("Read your list files and movie folders again and rebuild this list"));
        ExportBtn.Content = Loc.T("Export list (CSV)");
        PostersText.Text = Loc.T("Retry missing posters");
        LoadingText.Text = Loc.T("Looking for problems…");
        NoFolders.Text = Loc.T("Your movie folders are not set, so only your list is checked. Set them in Settings → General to also find folders with no movie and movies that are in no list.");
        chipsFor = null;
        foreach (var vm in matchVms.Values) vm.Update(vm.P);
        if (loaded) Fill();
    }

    async Task ReloadAsync()
    {
        int gen = ++generation;
        LoadingBox.Visibility = Visibility.Visible;
        Ring.IsActive = true;
        Empty.Visibility = Visibility.Collapsed;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (m, r, dirs) = await Task.Run(() => (lib.MatchProblems(fuzzy: false), Issues.Build(lib), lib.LoadSettings().MediaDirs.Count > 0));
            Timing = $"first {sw.Elapsed.TotalSeconds:0.0}s";
            matches = m;
            report = r;
            hasFolders = dirs;
            matchVms.Clear();
            foreach (var p in matches) matchVms[p.FileId] = new MatchVm(p, doneRows.GetValueOrDefault(p.FileId));
            if (area == "match" && matches.Count == 0 && report.OpenCount("novideo") > 0)   // nothing to match: show what needs removing
            {
                area = "other";
                subs["other"] = "novideo";
            }
            loaded = true;
        }
        catch (Exception e) when (IsExpected(e))
        {
            Error = e.ToString();
            Say(e.Message, InfoBarSeverity.Error);
        }
        Ring.IsActive = false;
        LoadingBox.Visibility = Visibility.Collapsed;
        var want = Areas.Items.First(i => (string)i.Tag == area);
        if (Areas.SelectedItem != want) Areas.SelectedItem = want;  // its event fills the list
        else Fill();
        await AddSpellingSuggestionsAsync(gen);
    }

    /// <summary>The misspelling search is slow, so the list shows first and the extra suggestions follow.</summary>
    async Task AddSpellingSuggestionsAsync(int gen)
    {
        if (matches.Count == 0) return;
        Fuzzy = true;
        try
        {
            var more = await Task.Run(() => lib.MatchProblems(fuzzy: true));
            if (gen != generation) return; // something changed meanwhile; that reload brings its own
            var still = matches.Select(p => p.FileId).ToHashSet();
            matches = more.Where(p => still.Contains(p.FileId)).ToList();
            foreach (var p in matches)
                if (matchVms.TryGetValue(p.FileId, out var vm)) vm.Update(p);
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            Error = e.ToString();
        }
        finally
        {
            if (gen == generation) Fuzzy = false;
        }
    }

    /// <summary>The issues again after a change; the matching rows keep their candidates, so this is quick.</summary>
    async Task RefreshIssuesAsync()
    {
        report = await Task.Run(() => Issues.Build(lib));
        Fill(keepScroll: true);
    }

    public bool Busy => Ring.IsActive || Fuzzy;
    public bool Fuzzy { get; private set; }
    public string? Error { get; private set; }
    public string? Timing { get; private set; }

    static bool IsExpected(Exception e) => e is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException
        or ArgumentException or NotImplementedException or Win32Exception or System.Runtime.InteropServices.COMException or System.Net.Http.HttpRequestException;

    string Sub => area == "match" ? matchTab : subs[area];
    string Kind => area == "dupes" ? "dupes" : subs.GetValueOrDefault(area, "");

    int AreaCount(string a) => a == "match" ? matches.Count : report.AreaCount(a);

    // ---------- filling the window ----------
    void Fill(bool keepScroll = false)
    {
        for (int i = 0; i < AreaDefs.Length; i++)
        {
            var item = (SelectorBarItem)Areas.Items[i];
            item.Text = $"{Loc.T(AreaDefs[i].Label)}  {AreaCount(AreaDefs[i].Key):N0}";
        }
        FillChips();
        bool isMatch = area == "match", isIssue = !isMatch;
        var list = isMatch ? MatchList : IssueList;
        double? offset = keepScroll ? Scroller(list)?.VerticalOffset : null;
        MatchList.Visibility = U2.Vis(isMatch);
        IssueList.Visibility = U2.Vis(isIssue);
        NoFolders.Visibility = U2.Vis(!hasFolders && loaded);
        ExportBtn.Visibility = Visibility.Visible;
        ShowIgnored.Visibility = Visibility.Collapsed;
        WastedText.Visibility = Visibility.Collapsed;
        RemoveAllBtn.Visibility = PostersBtn.Visibility = Visibility.Collapsed;
        string sub = Sub, empty;
        int shown;
        if (isMatch)
        {
            HelpText.Text = Loc.T(MatchHelp);
            FootText.Text = Loc.T("Fixes are saved right away and never overwritten by updates.");
            var rows = matches.Where(p => matchTab == "all" || p.Conf == matchTab).Select(p => matchVms[p.FileId]).ToList();
            MatchList.ItemsSource = rows;
            shown = rows.Count;
            empty = "Nothing to check. Every title is matched.";
        }
        else
        {
            HelpText.Text = Loc.T(Help[sub]);
            FootText.Text = Loc.T(sub == "novideo" ? "Removing only hides a title in the library. Your list files and folders are not changed."
                : "Nothing here changes your files. Ignored items stay ignored until their files change.");
            var all = report.Rows(area, sub);
            int hidden = all.Count(r => r.Ignored);
            var rows = showIgnored ? all : all.Where(r => !r.Ignored).ToList();
            issueRows = new ObservableCollection<IssueVm>(rows.Select(r => new IssueVm(r, area, sub, openMovie != null)));
            IssueList.ItemsSource = issueRows;
            shown = rows.Count;
            empty = all.Count > 0 ? "Everything here is ignored." : "Nothing here. All good.";
            UpdateIgnoredBox(hidden);
            ShowWasted();
            if (sub == "novideo" && rows.Count > 0)
            {
                RemoveAllBtn.Content = string.Format(Loc.T("Remove all {0} from list"), rows.Count.ToString("N0"));
                RemoveAllBtn.Visibility = Visibility.Visible;
            }
            if (sub == "noposter" && rows.Count > 0) PostersBtn.Visibility = Visibility.Visible;
        }
        BulkRow.Visibility = U2.Vis(RemoveAllBtn.Visibility == Visibility.Visible || PostersBtn.Visibility == Visibility.Visible);
        Empty.Visibility = U2.Vis(shown == 0 && loaded);
        EmptyText.Text = Loc.T(empty);
        if (offset is double top)
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => Scroller(list)?.ChangeView(null, top, null, true));
    }

    static ScrollViewer? Scroller(DependencyObject o)
    {
        if (o is ScrollViewer s) return s;
        for (int i = 0, n = VisualTreeHelper.GetChildrenCount(o); i < n; i++)
            if (Scroller(VisualTreeHelper.GetChild(o, i)) is { } found) return found;
        return null;
    }

    void FillChips()
    {
        string key = area == "match" ? "match:" + string.Join(',', MatchTabs.Where(t => ChipCount(t.Key) > 0).Select(t => t.Key)) : area;
        if (chipsFor != key)
        {
            chipsFor = key;
            Chips.Children.Clear();
            chips.Clear();
            var defs = area == "match" ? MatchTabs.Where(t => t.Key == "all" || ChipCount(t.Key) > 0).ToArray() : SubDefs[area];
            if (area == "match" && defs.All(d => d.Item1 != matchTab)) matchTab = "all";
            foreach (var (k, label) in defs)
            {
                var b = new ToggleButton { Tag = (k, label), FontSize = 12, Padding = new Thickness(10, 3, 10, 3) };
                b.Click += OnChip;
                chips.Add(b);
                Chips.Children.Add(b);
            }
        }
        Chips.Visibility = U2.Vis(chips.Count > 0);
        foreach (var b in chips)
        {
            var (k, label) = ((string, string))b.Tag;
            b.IsChecked = k == Sub;
            b.Content = $"{Loc.T(label)}  {ChipCount(k):N0}";
        }
    }

    int ChipCount(string key) => area == "match" ? (key == "all" ? matches.Count : matches.Count(p => p.Conf == key)) : report.SubCount(area, key);

    void UpdateIgnoredBox(int hidden)
    {
        ShowIgnored.Visibility = U2.Vis(hidden > 0 || showIgnored);
        ShowIgnored.Content = string.Format(Loc.T("Show ignored ({0})"), hidden);
        ShowIgnored.IsChecked = showIgnored;
    }

    void ShowWasted()
    {
        long wasted = report.Kinds.TryGetValue("dupes", out var d) ? d.Where(r => !r.Ignored).Sum(r => r.Wasted) : 0;
        WastedText.Visibility = U2.Vis(area == "dupes" && wasted > 0);
        WastedText.Inlines.Clear();
        var parts = Loc.T("{0} of disk space is taken by redundant copies (from your movie folders).").Split("{0}");
        WastedText.Inlines.Add(new Run { Text = parts[0] });
        WastedText.Inlines.Add(new Run { Text = PrFmt.Size(wasted), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        WastedText.Inlines.Add(new Run { Text = parts[1] });
    }

    /// <summary>Opens a tab; only for the screenshots of the debug run.</summary>
    public void ShowTab(string a, string? sub = null, bool ignored = false)
    {
        if (sub != null && subs.ContainsKey(a)) subs[a] = sub;
        area = a;
        showIgnored = ignored;
        var want = Areas.Items.First(i => (string)i.Tag == a);
        if (Areas.SelectedItem != want) Areas.SelectedItem = want;
        else Fill();
    }

    void OnAreaChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem i) return;
        area = (string)i.Tag;
        if (loaded) Fill();
    }

    void OnChip(object sender, RoutedEventArgs e)
    {
        var (k, _) = ((string, string))((ToggleButton)sender).Tag;
        if (area == "match") matchTab = k;
        else subs[area] = k;
        Fill();
    }

    void OnShowIgnored(object sender, RoutedEventArgs e)
    {
        showIgnored = ShowIgnored.IsChecked == true;
        Fill();
    }

    /// <summary>A short message that goes away by itself, like the toasts of the web page.</summary>
    void Say(string text, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        WinKit.Info(Toast, text, severity);
        toastTimer.Stop();
        toastTimer.Start();
    }

    /// <summary>Runs a button's work; what can go wrong shows as a message instead of ending the app.</summary>
    async Task Guard(Func<Task> work)
    {
        try { await work(); }
        catch (Exception e) when (IsExpected(e))
        {
            Say(Loc.T(e.Message), InfoBarSeverity.Error);
        }
    }

    // ---------- Matching ----------
    void MarkDone(MatchVm m, string message)
    {
        doneRows[m.P.FileId] = Loc.T(message);
        m.Done = doneRows[m.P.FileId];
    }

    async void OnUseCandidate(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not CandVm c) return;
        await Guard(async () =>
        {
            await Task.Run(() => lib.SetMatchAndRefresh(c.Owner.P.FileId, c.C.Tconst));
            MarkDone(c.Owner, "Fixed");
            changed?.Invoke();
        });
    }

    async void OnAccept(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not MatchVm m) return;
        await Guard(async () =>
        {
            await Task.Run(() => lib.Accept(m.P.FileId));
            MarkDone(m, "Confirmed");
        });
    }

    async void OnSearch(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not MatchVm m) return;
        await Guard(async () =>
        {
            if (await Dialogs.FixMatch(Root.XamlRoot, lib, m.P.FileId, m.P.Raw) is not int chosen) return;
            MarkDone(m, chosen == 0 ? "Marked as not on IMDb" : "Fixed");
            changed?.Invoke();
        });
    }

    async void OnNotImdb(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not MatchVm m) return;
        await Guard(async () =>
        {
            if (!await WinKit.Confirm(Root.XamlRoot, "Mark as not on IMDb?", "This file keeps no IMDb data (no poster, cast or ratings). You can fix it again later.", "Mark as not on IMDb")) return;
            await Task.Run(() => lib.SetMatch(m.P.FileId, null));
            MarkDone(m, "Marked as not on IMDb");
            changed?.Invoke();
        });
    }

    void OnCurrentImdb(object sender, RoutedEventArgs e)
    {
        if (((HyperlinkButton)sender).Tag is MatchVm { P.Current: { } c }) U2.Launch(U2.ImdbTitleUrl(c.Tconst));
    }

    void OnCandImdb(object sender, RoutedEventArgs e)
    {
        if (((HyperlinkButton)sender).Tag is CandVm c) U2.Launch(U2.ImdbTitleUrl(c.C.Tconst));
    }

    // ---------- removing from the list ----------
    /// <summary>Asks first, as the web page does. Returns true when the entries were removed.</summary>
    async Task Remove(IReadOnlyList<int> ids)
    {
        if (ids.Count == 0) return;
        string title = ids.Count == 1 ? Loc.T("Remove this title from your list?") : string.Format(Loc.T("Remove {0} titles from your list?"), ids.Count.ToString("N0"));
        if (!await WinKit.Confirm(Root.XamlRoot, title, "They are hidden from the library and no update brings them back. Your list files and your movie files are not changed.", "Remove from list")) return;
        int removed = await Task.Run(() => lib.RemoveFromList(ids));
        Say(string.Format(Loc.T("{0} removed from your list"), removed.ToString("N0")), InfoBarSeverity.Success);
        var gone = ids.ToHashSet();
        matches.RemoveAll(p => gone.Contains(p.FileId));
        await RefreshIssuesAsync();
        changed?.Invoke();
    }

    async void OnRemoveMatch(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is MatchVm m) await Guard(() => Remove(new[] { m.P.FileId }));
    }

    async void OnRemoveRow(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is IssueVm v) await Guard(() => Remove(v.R.RemoveIds));
    }

    async void OnRemoveFile(object sender, RoutedEventArgs e)
    {
        if (((HyperlinkButton)sender).Tag is IssueFileVm f) await Guard(() => Remove(new[] { f.F.Id }));
    }

    async void OnRemoveAll(object sender, RoutedEventArgs e) =>
        await Guard(() => Remove(issueRows.Where(v => v.R.JunkIds != null).SelectMany(v => v.R.JunkIds!).ToList()));

    // ---------- row actions ----------
    async void OnIgnore(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not IssueVm v) return;
        await Guard(async () =>
        {
            bool undo = v.R.Ignored;
            await Task.Run(() => lib.Dismiss(v.R.IKey, undo));
            v.R.Ignored = !undo;
            int at = issueRows.IndexOf(v);
            if (at >= 0)
            {
                // only this row changes, so the list stays where it is
                if (showIgnored) issueRows[at] = new IssueVm(v.R, area, Sub, openMovie != null);
                else issueRows.RemoveAt(at);
            }
            FillChips();
            for (int i = 0; i < AreaDefs.Length; i++)
                ((SelectorBarItem)Areas.Items[i]).Text = $"{Loc.T(AreaDefs[i].Label)}  {AreaCount(AreaDefs[i].Key):N0}";
            UpdateIgnoredBox(report.Rows(area, Sub).Count(r => r.Ignored));
            ShowWasted();
            if (issueRows.Count == 0)
            {
                Empty.Visibility = Visibility.Visible;
                EmptyText.Text = Loc.T("Everything here is ignored.");
            }
            Say(Loc.T(undo ? "Restored" : "Ignored. Show it again with \"Show ignored\"."));
        });
    }

    async void OnSeries(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not IssueVm v) return;
        await Guard(async () =>
        {
            bool mark = v.R.Sub != "series";
            await Task.Run(() =>
            {
                lib.Dismiss($"{(mark ? "series" : "noseries")}:{v.R.K}");
                lib.Dismiss($"{(mark ? "noseries" : "series")}:{v.R.K}", undo: true);
            });
            await RefreshIssuesAsync();
            Say(Loc.T(mark ? "Marked as a series: it is not a duplicate any more" : "Not a series: it is checked for duplicates again"));
        });
    }

    void OnOpenMovie(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is IssueVm v) openMovie?.Invoke(v.R.K);
    }

    void OnIssueImdb(object sender, RoutedEventArgs e)
    {
        if (((HyperlinkButton)sender).Tag is IssueVm { R.Tconst: int tc }) U2.Launch(U2.ImdbTitleUrl(tc));
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not IssueVm v) return;
        var names = v.R.CopyList();
        try
        {
            var data = new DataPackage();
            data.SetText(string.Join("\n", names));
            Clipboard.SetContent(data);
            Say(string.Format(Loc.T(names.Count == 1 ? "Copied {0} file name" : "Copied {0} file names"), names.Count), InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            Say(Loc.T("Could not copy"), InfoBarSeverity.Error);
        }
    }

    async void OnWish(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not IssueVm v) return;
        await Guard(async () =>
        {
            int added = await Task.Run(() => lib.WishAdd(new[] { new WishNew(v.R.Title, v.R.Year, v.R.Tconst, v.R.Note, "Upgrade") }));
            if (added > 0) Say(string.Format(Loc.T("{0} added to the wishlist"), added), InfoBarSeverity.Success);
            else Say(Loc.T("Already on the wishlist"));
        });
    }

    async void OnFixIssue(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not IssueVm v) return;
        await Guard(async () =>
        {
            int id = v.R.FixFileId(area == "dupes");
            if (await Dialogs.FixMatch(Root.XamlRoot, lib, id, v.R.Files.First(f => f.Id == id).Raw) == null) return;
            v.MarkUsed();
            Say(Loc.T("Match updated"), InfoBarSeverity.Success);
            changed?.Invoke();
        });
    }

    async void OnFixFile(object sender, RoutedEventArgs e)
    {
        if (((HyperlinkButton)sender).Tag is not IssueFileVm f) return;
        await Guard(async () =>
        {
            if (await Dialogs.FixMatch(Root.XamlRoot, lib, f.F.Id, f.Raw) == null) return;
            f.Owner.MarkUsed();
            Say(Loc.T("Match updated"), InfoBarSeverity.Success);
            changed?.Invoke();
        });
    }

    void OnReveal(object sender, RoutedEventArgs e)
    {
        if (((HyperlinkButton)sender).Tag is not string path) return;
        try { lib.Reveal(path); }
        catch (Exception ex) when (IsExpected(ex)) { Say(Loc.T(ex.Message), InfoBarSeverity.Warning); }
    }

    void OnRetryPosters(object sender, RoutedEventArgs e)
    {
        lib.StartOrQueue(() => lib.FetchPosters(retry: true));
        Say(Loc.T("Looking for the missing posters…"));
    }

    /// <summary>The poster box of the web page as a dialog: an image address or a file path, or a file picked here.</summary>
    async void OnAddPoster(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not IssueVm { R.Tconst: int tc }) return;
        await Guard(async () =>
        {
            var dlg = WinKit.Dialog(Root.XamlRoot, "Add a poster…");
            var source = new TextBox { PlaceholderText = Loc.T("Image address (https://…) or a file path"), FlowDirection = FlowDirection.LeftToRight, MinWidth = 440 };
            var pick = new Button { Content = Loc.T("Choose an image file…") };
            string hint = Loc.T("JPEG, PNG, WebP or GIF, up to 15 MB. A poster you add is kept: updates never replace it.");
            var msg = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, FontSize = 12, MaxWidth = 460, Foreground = U2.Res("TextFillColorSecondaryBrush") };
            void Note(string text, bool bad)
            {
                msg.Text = text;
                msg.Foreground = U2.Res(bad ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush");
            }
            pick.Click += async (_, _) =>
            {
                var picker = new FileOpenPicker();
                foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" }) picker.FileTypeFilter.Add(ext);
                WinKit.Pick(picker, this);
                var file = await picker.PickSingleFileAsync();
                if (file != null) source.Text = file.Path;
            };
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(source);
            panel.Children.Add(pick);
            panel.Children.Add(msg);
            dlg.Content = panel;
            dlg.PrimaryButtonText = Loc.T("Use");
            dlg.CloseButtonText = Loc.T("Cancel");
            dlg.DefaultButton = ContentDialogButton.Primary;
            bool saved = false;
            dlg.PrimaryButtonClick += async (d, args) =>
            {
                args.Cancel = true;     // the dialog closes itself when the poster is in
                string v = source.Text.Trim();
                if (v.Length == 0)
                {
                    Note(Loc.T("Paste an image address or a file path first."), true);
                    return;
                }
                if (!v.Contains("://") && File.Exists(v) && new FileInfo(v).Length > 15_000_000)
                {
                    Note(Loc.T("The image is larger than 15 MB."), true);
                    return;
                }
                var wait = args.GetDeferral();
                Note(Loc.T("Saving…"), false);
                try
                {
                    await Task.Run(() => lib.SetPosterFrom(tc, v));
                    saved = true;
                    d.Hide();
                }
                catch (Exception ex) when (IsExpected(ex))
                {
                    Note(Loc.T(ex.Message), true);
                }
                finally
                {
                    wait.Complete();
                }
            };
            await dlg.ShowAsync();
            if (!saved) return;
            Say(Loc.T("Poster saved"), InfoBarSeverity.Success);
            await RefreshIssuesAsync();
            changed?.Invoke();
        });
    }

    // ---------- bottom bar ----------
    async void OnCheckAgain(object sender, RoutedEventArgs e)
    {
        // a long job that is running (a people scan, for example) can be stopped first; what it already finished is kept
        bool now = false;
        if (lib.Job.Running)
        {
            if (!await Dialogs.Confirm(Content.XamlRoot, "Another job is running", "An update is still running, so the check would wait for it. Stop it and check your lists and movie folders now? What it already finished is kept.", "Stop it and check now")) return;
            now = true;
            lib.Stop();
        }
        recheckDone = false;
        bool started = lib.StartOrQueue(() =>
        {
            lib.Rescan();
            recheckDone = true;
        });
        Say(Loc.T(started || now ? "Checking your lists and movie folders again…" : "Queued: runs after the current job"));
        recheck = true;
        CheckAgainBtn.IsEnabled = false;
        CheckAgainBtn.Content = Loc.T("Checking…");
    }

    void OnJobChanged() => DispatcherQueue.TryEnqueue(async () =>
    {
        var j = lib.Job;
        if (!recheck || j.Running || !(recheckDone || j.Error != null || j.Stopped)) return;
        recheck = false;
        CheckAgainBtn.IsEnabled = true;
        CheckAgainBtn.Content = Loc.T("Check again");
        if (j.Error != null) Say(j.Error, InfoBarSeverity.Error);
        doneRows.Clear();
        await ReloadAsync();
        changed?.Invoke();
    });

    async void OnExportCsv(object sender, RoutedEventArgs e)
    {
        await Guard(async () =>
        {
            string name = area == "match" ? "MovieShelf problems" : $"MovieShelf problems {Kind}";
            string text = area == "match" ? Issues.ToCsv(matches) : Issues.ToCsv(report, Kind, area == "dupes" ? Sub : "all");
            var file = await WinKit.SaveFile(this, name, "CSV (.csv)", ".csv");
            if (file == null) return;
            await Task.Run(() => File.WriteAllText(file.Path, text, new UTF8Encoding(false)));
            Say(Loc.T("Saved"), InfoBarSeverity.Success);
        });
    }
}
