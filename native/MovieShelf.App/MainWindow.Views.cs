using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MovieShelf.Core;

namespace MovieShelf.App;

// Saved views (a search with its filters, sort and grouping), the statistics picks and the other windows' entry points.
public sealed partial class MainWindow
{
    bool restoring;
    List<SavedView> views = new();
    bool viewsLoaded;

    async Task LoadViews()
    {
        views = await Task.Run(() =>
        {
            try { return lib.ListViews(); }
            catch (Microsoft.Data.Sqlite.SqliteException) { return new List<SavedView>(); }
        });
        RenderViews();
    }

    void RenderViews()
    {
        if (!viewsLoaded)
        {
            viewsLoaded = true;
            _ = LoadViews();
            return;
        }
        ViewsList.Children.Clear();
        if (views.Count == 0)
        {
            ViewsList.Children.Add(new TextBlock { Text = Loc.T("Search and filter, then save the combination here."), TextWrapping = TextWrapping.Wrap, FontSize = 12, Style = (Style)Root.Resources["Muted"] });
            return;
        }
        foreach (var v in views)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var open = new Button
            {
                Content = new TextBlock { Text = v.Name, TextTrimming = TextTrimming.CharacterEllipsis }, Background = null, BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            ToolTipService.SetToolTip(open, Loc.T("Apply this view"));
            open.Click += (_, _) => RestoreView(v);
            row.Children.Add(open);
            var del = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 10 }, Background = null, BorderThickness = new Thickness(0), Padding = new Thickness(6) };
            ToolTipService.SetToolTip(del, string.Format(Loc.T("Delete the view {0}"), v.Name));
            del.Click += async (_, _) => await DeleteView(v);
            Grid.SetColumn(del, 1);
            row.Children.Add(del);
            ViewsList.Children.Add(row);
        }
    }

    /// <summary>The view's filters replace the current ones, as applyView in app.js does; the section stays unless the view is an old native one that names it.</summary>
    void RestoreView(SavedView v)
    {
        var s = LibraryFilter.ReadView(v.State);
        F.Clear();
        F.Merge(s.Filter.ToJson());
        SetSearchText(F.Q);
        restoring = true;
        if (s.Sort != null && SortBox.Items.Cast<ComboBoxItem>().Any(i => (string)i.Tag == s.Sort)) sortKey = s.Sort;
        reverse = s.Reverse;
        groupKey = GroupBox.Items.Cast<ComboBoxItem>().Any(i => (string)i.Tag == s.Group) ? s.Group! : "none";
        if (s.Kind is "all" or "movies" or "series") kind = s.Kind;
        SelectTag(SortBox, sortKey);
        SelectTag(GroupBox, groupKey);
        ReverseBtn.IsChecked = reverse;
        KindBar.SelectedItem = kind == "movies" ? KindMovies : kind == "series" ? KindSeries : KindAll;
        restoring = false;
        SyncFilterControls();
        UpdateSortHeader();
        BuildFacets();
        Apply();
        Toast(string.Format(Loc.T("View: {0}"), v.Name), "info");
    }

    async Task DeleteView(SavedView v)
    {
        if (!await Dialogs.Confirm(Root.XamlRoot, string.Format(Loc.T("Delete the view “{0}”?"), v.Name), "Only the saved view goes; your movies are not touched.", "Delete", danger: true)) return;
        await Task.Run(() => lib.DeleteView(v.Id));
        await LoadViews();
    }

    void OnViewSave(object sender, RoutedEventArgs e)
    {
        ViewSaveRow.Visibility = ViewSaveRow.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        ViewName.Focus(FocusState.Programmatic);
    }

    async void OnViewOk(object sender, RoutedEventArgs e) => await SaveCurrentView();

    async void OnViewNameKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) await SaveCurrentView();
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            ViewSaveRow.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    async Task SaveCurrentView()
    {
        string name = ViewName.Text.Trim();
        if (name.Length == 0)
        {
            ViewName.Focus(FocusState.Programmatic);
            return;
        }
        var state = F.ToView(sortKey, reverse, groupKey);
        try
        {
            await Task.Run(() => lib.SaveView(name, state));
        }
        catch (Exception x) when (x is ArgumentException or Microsoft.Data.Sqlite.SqliteException)
        {
            Toast(x.Message, "err");
            return;
        }
        ViewName.Text = "";
        ViewSaveRow.Visibility = Visibility.Collapsed;
        ShowInfo(string.Format(Loc.T("Saved view “{0}”"), name));
        await LoadViews();
    }

    // ---------- other windows ----------
    void OnWishlist(object sender, RoutedEventArgs e) => WishlistWindow.Open();

    void OnCollections(object sender, RoutedEventArgs e) => CollectionsWindow.Open(key =>
    {
        var m = all.Find(x => x.K == key);
        if (m != null) ShowDetail(m);
    }, ShowSearch);

    void OnStats(object sender, RoutedEventArgs e) => StatsWindow.Open(all, PickFromStats);

    void OnCheckList(object sender, RoutedEventArgs e) => CheckWindow.Open(() => RunUpdate(), RefreshBadges);

    /// <summary>Load list: bring movie names in from a list file, a folder of lists or a folder of movies. The rescan is queued by the load itself.</summary>
    public async void OnLoadList(object sender, RoutedEventArgs e)
    {
        try
        {
            var r = await Dialogs.LoadList(this, Content.XamlRoot);
            if (r == null) return;
            Toast(string.Format(Loc.T(r.Moved > 0 ? "{0} names loaded. {1} old list files put aside in “previous lists”. Updating…" : "{0} names loaded. Updating…"), r.Names.ToString("N0"), r.Moved), "info");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException) { Toast(ex.Message, "err"); }
    }

    void OnRenamePlan(object sender, RoutedEventArgs e) => RenamePlanWindow.Open();

    void OnBackups(object sender, RoutedEventArgs e) => OpenBackups();

    public void OpenBackups() => BackupsWindow.Open(() => DispatcherQueue.TryEnqueue(async () => await LoadAsync()), () => RunUpdate());

    /// <summary>Shows only what this search finds, like a click on a collection's name.</summary>
    public void ShowSearch(string query)
    {
        F.Clear();
        F.Q = query;
        SetSearchText(query);
        SyncFilterControls();
        BuildFacets();
        Apply();
        Activate();
    }

    /// <summary>A click on a statistics bar. A person opens the profile; the other bars show exactly those movies.</summary>
    void PickFromStats(string what, string key, string label)
    {
        if (what == "person")
        {
            OpenPerson(int.Parse(key), label);
            return;
        }
        if (what == "country") F.Country = label;
        else
        {
            F.Clear();
            SetSearchText("");
            if (what == "decade") F.Decade.Add(key);
            if (what == "genre") F.Genre.Add(label);
            if (what == "res") F.Res.Add(label);
        }
        SyncFilterControls();
        BuildFacets();
        Apply();
    }
}
