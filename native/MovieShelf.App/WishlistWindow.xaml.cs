using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieShelf.Core;

namespace MovieShelf.App;

/// <summary>A wishlist entry as the list shows it.</summary>
public sealed class WishVm
{
    public WishItem W { get; init; } = null!;
    public FlowDirection TitleFlow => WinKit.DirOf(W.Title);
    public string Title => W.Title + (W.Year is > 0 ? $" ({W.Year})" : "");
    bool Found => W.Status == WishStatus.Found;
    public Visibility WantedVis => Found ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FoundVis => Found ? Visibility.Visible : Visibility.Collapsed;
    public string WantedLabel => Loc.T("Wanted");
    public string FoundLabel => Loc.T("Found");
    public double Dim => Found ? 0.7 : 1;
    public string ToggleLabel => Loc.T(Found ? "Wanted again" : "Mark found");
    public string ImdbLabel => Loc.T("Open on IMDb");
    public string RemoveLabel => Loc.T("Remove");
    public Visibility ImdbVis => W.Tconst is > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string Sub
    {
        get
        {
            string added = W.Added is { Length: > 0 } a ? string.Format(Loc.T("Added {0}"), "‪" + a[..Math.Min(10, a.Length)] + "‬") : "";
            return string.Join(" · ", new[] { LocSource(W.Source), W.Note, added }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
    }

    public Visibility SubVis => Sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    // The sources the app itself writes are translated; anything else is shown as it was stored.
    static string? LocSource(string? s) => s == null ? null : Loc.T(s);
}

/// <summary>The wishlist: movies to get or replace. One window at a time; <see cref="Changed"/> tells the main window to refresh its badge.</summary>
public sealed partial class WishlistWindow : Window
{
    static WishlistWindow? instance;

    /// <summary>Raised on the UI thread after the wishlist was changed from any window.</summary>
    public static event Action? Changed;

    /// <summary>Tells everyone that the wishlist changed outside this window (for example Collections added entries).</summary>
    public static void NotifyChanged()
    {
        if (instance != null) _ = instance.ReloadAsync();
        Changed?.Invoke();
    }

    internal static WishlistWindow? Instance => instance;

    /// <summary>Shows the wishlist window, or brings the open one to the front.</summary>
    public static void Open()
    {
        if (instance == null) instance = new WishlistWindow();
        instance.Activate();
    }

    readonly Library lib = App.Lib;
    List<WishItem> items = new();
    string tab = WishStatus.Wanted;

    static readonly (string Key, string Label)[] TabDefs = { (WishStatus.Wanted, "Wanted"), (WishStatus.Found, "Found"), ("all", "All") };

    WishlistWindow()
    {
        InitializeComponent();
        WinKit.Setup(this, AppTitleBar, "Wishlist", 860, 800, Relocalise, "collection");
        foreach (var (key, label) in TabDefs) Tabs.Items.Add(new SelectorBarItem { Text = Loc.T(label), Tag = key });
        Tabs.SelectedItem = Tabs.Items[0];
        WinKit.Direction(Root);
        Loc.Apply(Root);
        AppTitleBar.Title = Loc.T("Wishlist");
        Closed += (_, _) => instance = null;
        _ = ReloadAsync();
    }

    void Relocalise()
    {
        WinKit.Direction(Root);
        Loc.Apply(Root);
        Info.IsOpen = false;
        Fill();
    }

    int generation;

    async Task ReloadAsync()
    {
        int gen = ++generation;
        try
        {
            var list = await Task.Run(lib.WishlistItems);
            if (gen != generation) return;
            items = list;
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            WinKit.Info(Info, e.Message, InfoBarSeverity.Error);
        }
        Ring.IsActive = false;
        Fill();
    }

    int Count(string key) => key == "all" ? items.Count : items.Count(w => w.Status == key);

    void Fill()
    {
        for (int i = 0; i < TabDefs.Length; i++)
        {
            int n = Count(TabDefs[i].Key);
            ((SelectorBarItem)Tabs.Items[i]).Text = Loc.T(TabDefs[i].Label) + (n > 0 ? $"  {n:N0}" : "");
        }
        var rows = items.Where(w => tab == "all" || w.Status == tab).Select(w => new WishVm { W = w }).ToList();
        List.ItemsSource = rows;
        Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Loc.T(items.Count == 0 ? "The wishlist is empty." : "Nothing here.");
        Summary.Text = string.Format(Loc.T("{0} wanted, {1} found"), Count(WishStatus.Wanted).ToString("N0"), Count(WishStatus.Found).ToString("N0"));
        bool anyWanted = Count(WishStatus.Wanted) > 0;
        CopyBtn.IsEnabled = SaveTxtBtn.IsEnabled = anyWanted;
        SaveCsvBtn.IsEnabled = items.Count > 0;
    }

    void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is SelectorBarItem i)
        {
            tab = (string)i.Tag;
            Fill();
        }
    }

    // ---------- add ----------
    static readonly Regex TitleYear = new(@"^(.*?)(?:\s*\((\d{4})\))?\s*$", RegexOptions.Compiled);

    void OnNewKey(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) OnAdd(sender, e);
    }

    async void OnAdd(object sender, RoutedEventArgs e)
    {
        var m = TitleYear.Match(NewTitle.Text.Trim());
        string title = m.Groups[1].Value.Trim();
        if (title.Length == 0)
        {
            NewTitle.Focus(FocusState.Programmatic);
            return;
        }
        int? year = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : null;
        try
        {
            int added = await Task.Run(() => lib.WishAdd(new[] { new WishNew(title, year, null, null, "Added by hand") }));
            WinKit.Info(Info, Loc.T(added > 0 ? "Added to the wishlist." : "Already on the wishlist."), added > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
            NewTitle.Text = "";
            Tabs.SelectedItem = Tabs.Items[0];
            tab = WishStatus.Wanted;
            await ReloadAsync();
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            WinKit.Info(Info, ex.Message, InfoBarSeverity.Error);
        }
    }

    // ---------- row buttons ----------
    async Task Change(Action work)
    {
        try
        {
            await Task.Run(work);
            await ReloadAsync();
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            WinKit.Info(Info, ex.Message, InfoBarSeverity.Error);
        }
    }

    async void OnToggle(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not WishVm v) return;
        string to = v.W.Status == WishStatus.Found ? WishStatus.Wanted : WishStatus.Found;
        await Change(() => lib.WishSet(v.W.Id, status: to));
    }

    async void OnRemove(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not WishVm v) return;
        await Change(() => lib.WishSet(v.W.Id, delete: true));
    }

    void OnImdb(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is WishVm { W.Tconst: int tc }) WinKit.OpenUrl(WinKit.ImdbUrl(tc));
    }

    // ---------- copy and save ----------
    void OnCopy(object sender, RoutedEventArgs e)
    {
        var wanted = items.Where(w => w.Status == WishStatus.Wanted).ToList();
        if (wanted.Count == 0)
        {
            WinKit.Info(Info, Loc.T("Nothing to copy."));
            return;
        }
        try
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(Csv.WishlistText(wanted));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            WinKit.Info(Info, string.Format(Loc.T("Copied {0} titles"), wanted.Count.ToString("N0")), InfoBarSeverity.Success);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            WinKit.Info(Info, ex.Message, InfoBarSeverity.Error);
        }
    }

    async void OnSaveText(object sender, RoutedEventArgs e) => await Save("MovieShelf wishlist", "Text file", ".txt", lib.WriteWishlistText);

    async void OnSaveCsv(object sender, RoutedEventArgs e) => await Save("MovieShelf wishlist", "CSV file", ".csv", lib.WriteWishlistCsv);

    async Task Save(string name, string label, string ext, Action<string> write)
    {
        try
        {
            var file = await WinKit.SaveFile(this, name, label, ext);
            if (file == null) return;
            string path = file.Path;
            await Task.Run(() => write(path));
            WinKit.Info(Info, $"{Loc.T("Saved")}: {path}", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or System.Runtime.InteropServices.COMException)
        {
            WinKit.Info(Info, ex.Message, InfoBarSeverity.Error);
        }
    }
}
