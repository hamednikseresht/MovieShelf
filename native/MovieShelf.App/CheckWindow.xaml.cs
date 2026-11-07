using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MovieShelf.Core;
using Windows.Storage.Pickers;

namespace MovieShelf.App;

/// <summary>One row of the list check result. Selection, the chosen candidate and the status can change after the row is shown.</summary>
public sealed class CheckRowVm : INotifyPropertyChanged
{
    public CheckItem It { get; }
    readonly List<int?> choiceTconsts = new();
    bool selected;
    int choiceIndex;
    string status;

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public CheckRowVm(CheckItem it)
    {
        It = it;
        status = it.Status;
        BuildChoices();
    }

    /// <summary>The radio labels; called again when the language changes, and the choice keeps its place.</summary>
    public void BuildChoices()
    {
        var it = It;
        var im = it.Imdb;
        bool first = ChoiceLabels.Count == 0;
        ChoiceLabels = new();           // a new list: the radio buttons do not watch this one
        choiceTconsts.Clear();
        if (it.Reason != null && (it.Cands?.Count > 0 || it.Tconst != null))
        {
            if (it.Tconst is int tc && im != null)
            {
                ChoiceLabels.Add(ChoiceText(im.Title, im.Year, im.Type, im.Votes, true));
                choiceTconsts.Add(tc);
            }
            foreach (var c in it.Cands ?? new())
            {
                if (c.Tconst == it.Tconst) continue;
                ChoiceLabels.Add(ChoiceText(c.Title, c.Year, c.Type, c.Votes, false));
                choiceTconsts.Add(c.Tconst);
            }
            ChoiceLabels.Add(Loc.T("None of these (add without IMDb data)"));
            choiceTconsts.Add(null);
            if (first) choiceIndex = it.Tconst != null && im != null ? 0 : ChoiceLabels.Count - 1;
        }
    }

    static string ChoiceText(string title, int? year, string? type, int? votes, bool best)
    {
        var bits = new List<string>();
        if (type != null && type != "movie") bits.Add(type);
        if (votes is > 0) bits.Add($"{U2.Votes(votes)} {Loc.T("votes")}");
        if (best) bits.Add(Loc.T("best guess"));
        return $"{title} ({(year?.ToString() ?? "?")})" + (bits.Count > 0 ? "  ·  " + string.Join(" · ", bits) : "");
    }

    // ----- identity
    public string Raw => It.Raw;
    public bool Addable => Status != "have";
    public Visibility AddableVis => U2.Vis(Addable);

    // ----- IMDb line
    public Visibility ImdbVis => U2.Vis(It.Imdb != null);
    public string NoImdbText => Loc.T("No IMDb match");
    public string WhichText => Loc.T("Which movie is it?");
    public Visibility NoImdbVis => U2.Vis(It.Imdb == null);
    public string ImdbTitle => It.Imdb == null ? "" : It.Imdb.Title + (It.Imdb.Year != null ? $" ({It.Imdb.Year})" : "");
    public string ImdbMeta => It.Imdb == null ? "" : string.Join(" · ", new[]
    {
        It.Imdb.Orig, It.Imdb.Rating != null ? $"IMDb {It.Imdb.Rating:0.0}" : null, It.Imdb.Votes > 0 ? $"{U2.Votes(It.Imdb.Votes)} {Loc.T("votes")}" : null,
        It.Imdb.Type != null && It.Imdb.Type != "movie" ? It.Imdb.Type : null,
    }.Where(x => !string.IsNullOrEmpty(x)));

    // ----- file facts
    public string QualityMeta => string.Join("  ·  ", new[]
    {
        It.Quality == "?" ? null : It.Quality, U2.Size(It.Size), It.Copies > 1 ? U2.Fmt("{0}× in this list", It.Copies) : null,
    }.Where(x => !string.IsNullOrEmpty(x)));
    public Visibility QualityVis => U2.Vis(QualityMeta.Length > 0);
    public string PathText => It.Path ?? "";
    public string ShortPath => U2.ShortPath(PathText);
    public Visibility PathVis => U2.Vis(!string.IsNullOrEmpty(It.Path));

    // ----- why unsure
    public string Reason => Loc.T(It.Reason ?? "");
    public Visibility ReasonVis => U2.Vis(It.Reason != null);
    public List<string> ChoiceLabels { get; private set; } = new();
    public Visibility ChoiceVis => U2.Vis(ChoiceLabels.Count > 0);
    public bool Touched { get; private set; }

    /// <summary>The radio selected by the user. A negative index (the control clearing itself while it is recycled) is ignored.</summary>
    public int ChoiceIndex
    {
        get => choiceIndex;
        set
        {
            if (value < 0 || value == choiceIndex) return;
            choiceIndex = value;
            Touched = true;
        }
    }

    /// <summary>The IMDb id picked in the radio list, null for "None of these".</summary>
    public int? ChosenTconst => choiceIndex >= 0 && choiceIndex < choiceTconsts.Count ? choiceTconsts[choiceIndex] : null;

    // ----- library comparison
    public Visibility LibVis => U2.Vis(It.Lib is { Count: > 0 });
    public string LibHead => Loc.T("In your library") + (string.IsNullOrEmpty(It.LibTitle) ? "" : " " + Loc.T("as") + " " + It.LibTitle) + ":";
    public string LibFiles => string.Join("\n", (It.Lib ?? new()).Select(f => $"{(f.Quality.Length > 0 ? f.Quality : "?")}   {f.Raw}"));
    public Visibility TwinsVis => U2.Vis(It.Twins is { Count: > 0 });
    public string TwinsText => Loc.T("Also in this list:") + " " + string.Join(", ", It.Twins ?? new());

    // ----- state that changes
    public bool Selected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            Changed(nameof(Selected));
        }
    }

    public string Status
    {
        get => status;
        set
        {
            status = value;
            It.Status = value;
            foreach (var n in new[] { nameof(Status), nameof(StatusLabel), nameof(StatusTip), nameof(BadgeBg), nameof(BadgeFg), nameof(AddableVis) }) Changed(n);
        }
    }

    public string StatusLabel => Loc.T(CheckResult.StatusText.TryGetValue(status, out var s) ? s.Label : status);
    public string StatusTip => CheckResult.StatusText.TryGetValue(status, out var s) ? Loc.T(s.Tip) : "";
    public Brush BadgeBg => U2.Badge(status).Bg;
    public Brush BadgeFg => U2.Badge(status).Fg;
}

/// <summary>A library movie that is not in the checked list.</summary>
public sealed class CheckMissingVm
{
    public CheckMissing M { get; }
    public CheckMissingVm(CheckMissing m) => M = m;
    public string TitleText => M.Title == null ? "" : M.Title + (M.Year != null ? $" ({M.Year})" : "");
    public Visibility TitleVis => U2.Vis(M.Title != null);
    public string FilesText => string.Join("\n", M.Files);
    public string Label => Loc.T("Only in library");
    public Brush BadgeBg => U2.Badge("missing").Bg;
    public Brush BadgeFg => U2.Badge("missing").Fg;
}

/// <summary>"Check a list": compare a list file, a folder or pasted names with the library, then add what is wanted.</summary>
public sealed partial class CheckWindow : Window
{
    static CheckWindow? current;
    static OnlineClient? onlineClient;
    static CheckState? dismissed;   // the check put aside with "Check another": it does not come back when the window opens again

    readonly Library lib = App.Lib;
    Action? wishlistChanged;
    List<CheckRowVm> rows = new();
    CheckResult? result;
    CheckState? handled;        // the check whose result is on screen (or that was dismissed with "Check another")
    string tab = "all";
    bool filling;

    static readonly (string Key, string Label)[] TabDefs =
    {
        ("all", "All"), ("new", "New"), ("better", "Better quality"), ("version", "Other version"), ("worse", "Lower quality"),
        ("duplicate", "Already have"), ("have", "Same file"), ("unmatched", "Not found"), ("unsure", "Needs a check"),
        ("twins", "Twice in this list"), ("missing", "Only in library"),
    };

    /// <summary>Opens the window (or brings the open one forward). It shows a running check or the last result, else the source chooser.
    /// Names that are added are matched at once by a queued update, so <paramref name="runUpdate"/> is no longer called; <paramref name="wishlistChanged"/> runs after wishlist entries were added.</summary>
    public static void Open(Action? runUpdate = null, Action? wishlistChanged = null)
    {
        if (current != null)
        {
            current.wishlistChanged = wishlistChanged;
            current.Activate();
            return;
        }
        current = new CheckWindow(wishlistChanged);
        current.Activate();
    }

    CheckWindow(Action? wishlistChanged)
    {
        this.wishlistChanged = wishlistChanged;
        InitializeComponent();
        U2.Setup(this, AppTitleBar, "List check", 1040, 860, Relocalise, "check");
        foreach (var (key, label) in TabDefs) Tabs.Items.Add(new SelectorBarItem { Text = Loc.T(label), Tag = key });
        Root.FlowDirection = U2.Flow;
        Loc.Apply(Root);
        JobHost.Child = WinKit.JobStrip(this);

        // The offline index misses some names; with the setting "search IMDb online" on, the check asks IMDb's suggestion search for them.
        // The check runs on a background thread, so blocking on the async call is fine here.
        if (lib.CheckOnlineFind == null)
        {
            onlineClient ??= new OnlineClient();
            lib.CheckOnlineFind = p => ImdbOnline.FindAsync(onlineClient, p.Title, p.AltTitle, p.Year).GetAwaiter().GetResult()?.Tconst;
        }

        lib.CheckChanged += OnLibCheckChanged;
        Closed += (_, _) =>
        {
            lib.CheckChanged -= OnLibCheckChanged;
            current = null;
        };
        var st = lib.Check;
        if (st.Running) ShowProgress(st);
        else if (st.Result != null && st != dismissed) ShowResult(st);
        else ShowPick();
    }

    void Relocalise()
    {
        Root.FlowDirection = U2.Flow;
        Loc.Apply(Root);
        Msg.IsOpen = false;
        var st = lib.Check;
        if (st.Running) ShowProgress(st);
        if (result == null) return;
        ResultHead();
        foreach (var r in rows) r.BuildChoices();
        BuildTabs();
        FillList();
    }

    // ---------- state changes ----------
    void OnLibCheckChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        var st = lib.Check;
        if (st.Running) ShowProgress(st);
        else if (st.Error != null && st != handled)
        {
            handled = st;
            ShowPick();
            Message(InfoBarSeverity.Error, Loc.T("Could not check that list."), Loc.T(st.Error) + "\n" + Loc.T("Tip: pick a .txt / .csv file, or a folder that contains video files."));
        }
        else if (st.Result != null && st != handled) ShowResult(st);
    });

    void Show(UIElement view)
    {
        PickView.Visibility = view == PickView ? Visibility.Visible : Visibility.Collapsed;
        ProgressView.Visibility = view == ProgressView ? Visibility.Visible : Visibility.Collapsed;
        ResultView.Visibility = view == ResultView ? Visibility.Visible : Visibility.Collapsed;
    }

    void Message(InfoBarSeverity sev, string title, string text = "", string? actionText = null, Action? action = null)
    {
        Msg.Severity = sev;
        Msg.Title = title;
        Msg.Message = text;
        Msg.ActionButton = null;
        if (actionText != null && action != null)
        {
            var b = new Button { Content = actionText };
            b.Click += (_, _) => action();
            Msg.ActionButton = b;
        }
        Msg.IsOpen = true;
    }

    void ShowPick()
    {
        Show(PickView);
    }

    void ShowProgress(CheckState st)
    {
        Show(ProgressView);
        Msg.IsOpen = false;
        Bar.IsIndeterminate = st.Total <= 0;
        Bar.Maximum = Math.Max(1, st.Total);
        Bar.Value = st.Done;
        StageText.Text = U2.Stage(st.Stage) + (st.Total > 0 ? $"  {st.Done:N0} / {st.Total:N0}" : "");
    }

    void ShowResult(CheckState st)
    {
        handled = st;
        result = st.Result!;
        rows = result.Items.Select(i => new CheckRowVm(i) { Selected = i.Wanted }).ToList();
        tab = "all";
        ResultHead();
        Show(ResultView);
        Msg.IsOpen = false;
        BuildTabs();
        FillList();
    }

    void ResultHead()
    {
        ResultTitle.Text = U2.Fmt(result!.Entries == 1 ? "List check: {0} entry" : "List check: {0} entries", result.Entries.ToString("N0"));
        ResultSource.Text = result.Source + "  ·  " + result.At;
    }

    // ---------- start a check ----------
    void Start(CheckSource src)
    {
        Msg.IsOpen = false;
        try
        {
            if (!lib.StartCheck(src)) Message(InfoBarSeverity.Warning, Loc.T("A check is already running."));
        }
        catch (InvalidOperationException e)
        {
            Message(InfoBarSeverity.Error, Loc.T("Could not check that list."), Loc.T(e.Message));
        }
    }

    async void OnPickFile(object sender, RoutedEventArgs e)
    {
        try
        {
            var p = new FileOpenPicker();
            p.FileTypeFilter.Add(".txt");
            p.FileTypeFilter.Add(".csv");
            p.FileTypeFilter.Add(".tsv");
            U2.InitPicker(p, this);
            var f = await p.PickSingleFileAsync();
            if (f != null) Start(new CheckSource(Path: f.Path));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Message(InfoBarSeverity.Error, ex.Message);
        }
    }

    async void OnPickFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var p = new FolderPicker();
            p.FileTypeFilter.Add("*");
            U2.InitPicker(p, this);
            var f = await p.PickSingleFolderAsync();
            if (f != null) Start(new CheckSource(Path: f.Path));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Message(InfoBarSeverity.Error, ex.Message);
        }
    }

    void OnCheckPath(object sender, RoutedEventArgs e)
    {
        string p = PathBox.Text.Trim().Trim('"');
        if (p.Length == 0) { Message(InfoBarSeverity.Warning, Loc.T("Type or choose a list file or a folder first")); return; }
        Start(new CheckSource(Path: p));
    }

    void OnPathKey(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) OnCheckPath(sender, e);
    }

    void OnCheckPaste(object sender, RoutedEventArgs e)
    {
        string t = PasteBox.Text;
        if (t.Trim().Length == 0) { Message(InfoBarSeverity.Warning, Loc.T("Paste some names first")); return; }
        Start(new CheckSource(Text: t, Name: "pasted list"));
    }

    void OnCheckAnother(object sender, RoutedEventArgs e)
    {
        dismissed = handled;
        rows = new();
        result = null;
        ItemList.ItemsSource = null;
        MissingList.ItemsSource = null;
        Msg.IsOpen = false;
        PathBox.Text = "";
        PasteBox.Text = "";
        ShowPick();
    }

    // ---------- tabs and list ----------
    int Count(string key) => key switch
    {
        "all" => rows.Count,
        "missing" => result?.MissingCount ?? 0,
        "unsure" => rows.Count(r => r.It.Reason != null),
        "twins" => rows.Count(r => r.It.Twins != null),
        _ => rows.Count(r => r.Status == key),
    };

    void BuildTabs()
    {
        string keep = tab;
        filling = true;
        Tabs.Items.Clear();
        foreach (var (key, label) in TabDefs)
        {
            int n = Count(key);
            if (key != "all" && n == 0) continue;
            Tabs.Items.Add(new SelectorBarItem { Text = Loc.T(label) + $"  {n:N0}", Tag = key });
        }
        var sel = Tabs.Items.Cast<SelectorBarItem>().FirstOrDefault(i => (string)i.Tag == keep) ?? (SelectorBarItem)Tabs.Items[0];
        tab = (string)sel.Tag;
        Tabs.SelectedItem = sel;
        filling = false;
        BuildTiles();
    }

    void BuildTiles()
    {
        Tiles.Children.Clear();
        int have = Count("worse") + Count("duplicate") + Count("have");
        Tiles.Children.Add(U2.Tile(Count("new").ToString("N0"), Loc.T("new movies")));
        Tiles.Children.Add(U2.Tile(Count("better").ToString("N0"), Loc.T("better than your copy")));
        Tiles.Children.Add(U2.Tile(Count("version").ToString("N0"), Loc.T("other audio version")));
        Tiles.Children.Add(U2.Tile(have.ToString("N0"), Loc.T("already have (same or better)")));
        Tiles.Children.Add(U2.Tile($"{Count("unmatched"):N0} / {Count("unsure"):N0}", Loc.T("not found / need a check")));
    }

    void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (filling || sender.SelectedItem is not SelectorBarItem i) return;
        tab = (string)i.Tag;
        FillList();
    }

    IEnumerable<CheckRowVm> ShownRows() => tab switch
    {
        "all" => rows,
        "unsure" => rows.Where(r => r.It.Reason != null),
        "twins" => rows.Where(r => r.It.Twins != null),
        _ => rows.Where(r => r.Status == tab),
    };

    void FillList()
    {
        bool missing = tab == "missing";
        ItemList.Visibility = missing ? Visibility.Collapsed : Visibility.Visible;
        MissingList.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
        SelectBar.Visibility = missing ? Visibility.Collapsed : Visibility.Visible;
        int n;
        if (missing)
        {
            var list = (result?.Missing ?? new()).Select(m => new CheckMissingVm(m)).ToList();
            MissingList.ItemsSource = list;
            n = list.Count;
        }
        else
        {
            var list = ShownRows().ToList();
            ItemList.ItemsSource = list;
            n = list.Count;
        }
        EmptyText.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCounts();
    }

    void UpdateCounts()
    {
        int sel = rows.Count(r => r.Selected && r.Addable);
        AddBtn.Content = U2.Fmt("Add {0} selected to library", sel);
        AddBtn.IsEnabled = sel > 0;
        WishBtn.IsEnabled = rows.Any(r => r.Selected && r.It.Wanted);
        SelCount.Text = U2.Fmt("{0} selected", sel);
    }

    void OnRowCheck(object sender, RoutedEventArgs e) => UpdateCounts();

    void SetShown(bool on)
    {
        foreach (var r in ShownRows()) r.Selected = on && r.Addable;
        UpdateCounts();
    }

    void OnSelectAll(object sender, RoutedEventArgs e) => SetShown(true);
    void OnSelectNone(object sender, RoutedEventArgs e) => SetShown(false);

    void OnOpenImdb(object sender, RoutedEventArgs e)
    {
        if (((HyperlinkButton)sender).Tag is CheckRowVm r && r.It.Tconst is int tc) U2.Launch(U2.ImdbTitleUrl(tc));
    }

    void OnOpenMissing(object sender, RoutedEventArgs e)
    {
        if (((HyperlinkButton)sender).Tag is CheckMissingVm m) U2.Launch(U2.ImdbTitleUrl(m.M.Tconst));
    }

    // ---------- actions ----------
    async void OnAdd(object sender, RoutedEventArgs e)
    {
        var chosen = rows.Where(r => r.Selected && r.Addable).ToList();
        if (chosen.Count == 0) return;
        // a radio choice counts only when the user touched it; otherwise the plain name goes in and the matcher decides
        var items = chosen.Select(r => r.Touched ? new AddItem(r.Raw, true, r.ChosenTconst) : new AddItem(r.Raw)).ToList();
        AddBtn.IsEnabled = false;
        AddBtn.Content = Loc.T("Adding…");
        try
        {
            int added = await Task.Run(() => lib.AddFromCheck(items, update: true));
            foreach (var r in chosen)
            {
                r.Status = "have";
                r.Selected = false;
            }
            BuildTabs();
            FillList();
            if (added > 0) Message(InfoBarSeverity.Success, U2.Fmt("{0} added to your library", added), Loc.T("They are being matched now; progress shows below."));
            else Message(InfoBarSeverity.Informational, Loc.T("Those names were already in your list."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
        {
            Message(InfoBarSeverity.Error, Loc.T("Could not add the names."), ex.Message);
            UpdateCounts();
        }
    }

    async void OnWish(object sender, RoutedEventArgs e)
    {
        var chosen = rows.Where(r => r.Selected).Select(r => r.It).ToList();
        var wish = CheckResult.WishlistItems(chosen);
        if (wish.Count == 0)
        {
            Message(InfoBarSeverity.Warning, Loc.T("Only new, better-quality and other-version movies can go to the wishlist."));
            return;
        }
        try
        {
            var items = wish.Select(w => new WishNew(w.Title, w.Year, w.Tconst, w.Note, w.Source)).ToList();
            int added = await Task.Run(() => lib.WishAdd(items));
            Message(InfoBarSeverity.Success, added > 0 ? U2.Fmt("{0} added to the wishlist", added) : Loc.T("Those are already on the wishlist."));
            if (added > 0)
            {
                wishlistChanged?.Invoke();
                WishlistWindow.NotifyChanged();
            }
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            Message(InfoBarSeverity.Error, Loc.T("Could not add to the wishlist."), ex.Message);
        }
    }

    async void OnExport(object sender, RoutedEventArgs e)
    {
        if (result == null) return;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = "MovieShelf list check" };
            picker.FileTypeChoices.Add(Loc.T("CSV file"), new List<string> { ".csv" });
            U2.InitPicker(picker, this);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            var report = result;
            var data = await Task.Run(report.ToCsv);
            await U2.WriteAsync(file.Path, data);
            Message(InfoBarSeverity.Success, Loc.T("Saved") + ": " + file.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Message(InfoBarSeverity.Error, Loc.T("Could not save the report."), ex.Message);
        }
    }
}
