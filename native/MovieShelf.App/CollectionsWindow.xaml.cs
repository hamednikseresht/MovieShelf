using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieShelf.Core;

namespace MovieShelf.App;

public sealed class HaveVm
{
    public CollectionMovie M { get; init; } = null!;
    public bool CanOpen { get; init; }
    public string Text => (M.Title ?? M.K) + (M.Year is > 0 ? $" ({M.Year})" : "");
    public Visibility LinkVis => CanOpen ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlainVis => CanOpen ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class MissingVm
{
    public MissingPart P { get; init; } = null!;
    public string Text => (P.Title ?? "?") + (P.Year is > 0 ? $" ({P.Year})" : "");
}

public sealed class CollVm
{
    public CollectionInfo C { get; init; } = null!;
    public List<HaveVm> Have { get; init; } = new();
    public List<MissingVm> Missing { get; init; } = new();
    public string Name => C.Name;
    public string HaveTag => Loc.T("Have");
    public string MissingTag => Loc.T("Missing");
    public Visibility HaveBlockVis => Have.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MissingBlockVis => Missing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string Sub => C.Fetched
        ? string.Format(Loc.T("you have {0} of {1}"), C.Have.Count, C.Total)
        : string.Format(Loc.T("{0} in your library, parts not checked"), C.Have.Count);
    public Visibility AddVis => C.Missing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string AddLabel => string.Format(Loc.T("Add {0} missing to wishlist"), C.Missing.Count);
    public string ShowLabel => Loc.T("Show in library");
    public Visibility ShowVis { get; init; } = Visibility.Collapsed;
}

/// <summary>Franchises in the library and the parts of each that are missing. Needs a TMDB key for the part lists.</summary>
public sealed partial class CollectionsWindow : Window
{
    static CollectionsWindow? instance;

    /// <summary>Shows the collections window, or brings the open one to the front.
    /// <paramref name="openMovie"/> gets the movie key ("tt0000001") when the user clicks a movie they have;
    /// <paramref name="search"/> gets the search text that shows a franchise in the library, for "Show in library".</summary>
    public static void Open(Action<string>? openMovie = null, Action<string>? search = null)
    {
        bool wasOpen = instance != null;
        instance ??= new CollectionsWindow();
        if (openMovie != null) instance.openMovie = openMovie;
        if (search != null) instance.search = search;
        if (wasOpen) _ = instance.ReloadAsync();
        instance.Activate();
    }

    internal static CollectionsWindow? Instance => instance;

    readonly Library lib = App.Lib;
    Action<string>? openMovie, search;
    List<CollectionInfo> colls = new();
    string tab = "incomplete";
    bool hasKey, weStarted, wasRunning;
    readonly Action jobHandler;

    static readonly (string Key, string Label)[] TabDefs = { ("incomplete", "Missing movies"), ("complete", "Complete"), ("all", "All") };

    CollectionsWindow()
    {
        InitializeComponent();
        WinKit.Setup(this, AppTitleBar, "Collections", 980, 860, Relocalise, "collection");
        foreach (var (key, label) in TabDefs) Tabs.Items.Add(new SelectorBarItem { Text = Loc.T(label), Tag = key });
        Tabs.SelectedItem = Tabs.Items[0];
        Texts();

        var dq = DispatcherQueue;
        jobHandler = () => dq.TryEnqueue(UpdateJobUi);
        lib.JobChanged += jobHandler;
        Closed += (_, _) =>
        {
            lib.JobChanged -= jobHandler;
            instance = null;
        };
        UpdateJobUi();
        _ = ReloadAsync();
    }

    void Texts()
    {
        WinKit.Direction(Root);
        Loc.Apply(Root);
        NoKey.Title = Loc.T("Collections need a TMDB key");
        NoKey.Message = Loc.T("Add a free TMDB key in Settings, Online data. Then press Fetch collection details.");
    }

    void Relocalise()
    {
        Texts();
        Info.IsOpen = false;
        UpdateJobUi();
        Fill();
    }

    int generation;

    async Task ReloadAsync()
    {
        int gen = ++generation;
        try
        {
            var (list, key) = await Task.Run(() => (lib.Collections(), !string.IsNullOrEmpty(lib.LoadSettings().TmdbKey)));
            if (gen != generation) return;
            colls = list;
            hasKey = key;
            NoKey.IsOpen = !hasKey;
            FetchBtn.IsEnabled = hasKey && !lib.Job.Running;
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException or System.Text.Json.JsonException)
        {
            WinKit.Info(Info, e.Message, InfoBarSeverity.Error);
        }
        Ring.IsActive = false;
        Fill();
    }

    int Count(string key) => key switch
    {
        "incomplete" => colls.Count(c => c.Missing.Count > 0),
        "complete" => colls.Count(c => c.Fetched && c.Missing.Count == 0),
        _ => colls.Count,
    };

    void Fill()
    {
        for (int i = 0; i < TabDefs.Length; i++)
        {
            int n = Count(TabDefs[i].Key);
            ((SelectorBarItem)Tabs.Items[i]).Text = Loc.T(TabDefs[i].Label) + (n > 0 ? $"  {n:N0}" : "");
        }
        var rows = colls.Where(c => tab switch
        {
            "incomplete" => c.Missing.Count > 0,
            "complete" => c.Fetched && c.Missing.Count == 0,
            _ => true,
        }).Select(c => new CollVm
        {
            C = c,
            Have = c.Have.Select(m => new HaveVm { M = m, CanOpen = openMovie != null }).ToList(),
            Missing = c.Missing.Select(m => new MissingVm { P = m }).ToList(),
            ShowVis = search != null ? Visibility.Visible : Visibility.Collapsed,
        }).ToList();
        List.ItemsSource = rows;
        Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Loc.T(colls.Count == 0 ? "No collections known yet." : "Nothing in this list.");
        int unchecked_ = colls.Count(c => !c.Fetched);
        FetchNote.Text = colls.Count == 0 ? "" : unchecked_ > 0 ? string.Format(Loc.T("{0} of {1} collections not checked yet"), unchecked_.ToString("N0"), colls.Count.ToString("N0")) : Loc.T("All collections are checked.");
    }

    void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is SelectorBarItem i)
        {
            tab = (string)i.Tag;
            Fill();
        }
    }

    // ---------- fetch job ----------
    void UpdateJobUi()
    {
        var j = lib.Job;
        JobPanel.Visibility = j.Running ? Visibility.Visible : Visibility.Collapsed;
        if (j.Running)
        {
            JobText.Text = j.Stopping ? Loc.T("Stopping…") : U2.Stage(j.Stage) + (j.Total > 0 ? $"  {j.Done:N0} / {j.Total:N0}" : "");
            JobBar.IsIndeterminate = j.Total == 0;
            JobBar.Maximum = Math.Max(j.Total, 1);
            JobBar.Value = j.Done;
        }
        StopBtn.Visibility = j.Stoppable ? Visibility.Visible : Visibility.Collapsed;
        StopBtn.IsEnabled = j.Running && !j.Stopping && j.Stoppable;
        FetchBtn.IsEnabled = hasKey && !j.Running;
        if (wasRunning && !j.Running)
        {
            _ = ReloadAsync();
            if (weStarted)
            {
                if (j.Error != null) WinKit.Info(Info, j.Error, InfoBarSeverity.Error);
                else if (j.Stopped) WinKit.Info(Info, Loc.T("Stopped. What was checked is kept."), InfoBarSeverity.Informational);
                else WinKit.Info(Info, Loc.T("Collections checked."), InfoBarSeverity.Success);
            }
            weStarted = false;
        }
        wasRunning = j.Running;
    }

    void OnFetch(object sender, RoutedEventArgs e)
    {
        if (!hasKey) return;
        Info.IsOpen = false;
        try
        {
            // waits behind a running job, as in Python
            if (!lib.StartOrQueue(lib.FetchCollections))
            {
                WinKit.Info(Info, Loc.T("Queued: it runs after the current job."));
                return;
            }
            weStarted = true;
        }
        catch (InvalidOperationException ex)
        {
            WinKit.Info(Info, ex.Message, InfoBarSeverity.Error);
        }
        UpdateJobUi();
    }

    void OnStop(object sender, RoutedEventArgs e)
    {
        lib.Stop();
        UpdateJobUi();
    }

    // ---------- row buttons ----------
    void OnOpenMovie(object sender, RoutedEventArgs e)
    {
        if (((HyperlinkButton)sender).Tag is HaveVm h && openMovie != null) openMovie(h.M.K);
    }

    void OnShowInLibrary(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not CollVm v || search == null) return;
        search($"collection:\"{v.Name}\"");
        App.Main?.Activate();
    }

    async void OnAddMissing(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not CollVm v) return;
        var items = v.C.Missing.Where(m => !string.IsNullOrWhiteSpace(m.Title))
            .Select(m => new WishNew(m.Title!, m.Year, null, v.C.Name, "Collection")).ToList();
        try
        {
            int added = await Task.Run(() => lib.WishAdd(items));
            int dup = items.Count - added;
            string msg = added == 0 ? Loc.T("Already on the wishlist.")
                : dup > 0 ? string.Format(Loc.T("{0} added to the wishlist ({1} already there)"), added, dup)
                : string.Format(Loc.T("{0} added to the wishlist"), added);
            WinKit.Info(Info, msg, added > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
            WishlistWindow.NotifyChanged();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            WinKit.Info(Info, ex.Message, InfoBarSeverity.Error);
        }
    }
}
