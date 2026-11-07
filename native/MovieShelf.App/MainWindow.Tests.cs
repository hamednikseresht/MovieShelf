using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MovieShelf.Core;

namespace MovieShelf.App;

// Set MOVIESHELF_SHOT_ONLY=main (with MOVIESHELF_SHOT) to drive the main window through its controls and write what happened to checks.log.
// prefs1 and prefs2 are two runs that test filters kept across a restart; keys sends real key presses; welcome and startup test the first seconds of the app.
// It changes marks on the data folder it runs on and takes them back.
public sealed partial class MainWindow
{
    string checkLog = "";
    int failed;

    void Note(string text) => File.AppendAllText(checkLog, text + "\n");

    void Check(string name, bool ok, string detail = "")
    {
        if (!ok) failed++;
        Note($"{(ok ? "ok  " : "FAIL")} {name}{(detail.Length > 0 ? "  [" + detail + "]" : "")}");
    }

    FrameworkElement DialogBox() => VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Select(p => p.Child).OfType<FrameworkElement>()
        .SelectMany(Under<FrameworkElement>).First(e => e.Name == "BackgroundElement");

    ContentDialog? OpenDialog() => VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Select(p => p.Child).OfType<ContentDialog>().FirstOrDefault();

    Button DialogButton(string name) => VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Select(p => p.Child).OfType<FrameworkElement>()
        .SelectMany(Under<Button>).First(b => b.Name == name);

    double GridOffset => ScrollOf(MovieGrid)?.VerticalOffset ?? -1;

    async Task ScrollGrid(double y)
    {
        ScrollOf(MovieGrid)!.ChangeView(null, y, null, true);
        await Task.Delay(500);
    }

    static string Texts(DependencyObject root) => string.Join(" | ", Under<TextBlock>(root).Select(t => t.Text).Where(t => t.Length > 0));

    async Task DebugChecks(string dir, string only)
    {
        checkLog = Path.Combine(dir, "checks.log");
        if (only == "prefs1") await CheckPrefsWrite();
        else if (only == "prefs2") CheckPrefsRead();
        else if (only == "keys") await CheckKeys();
        else
        {
            Root.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
            await Step("marks", () => CheckMarks(dir));
            await Step("selection", () => CheckSelection(dir));
            await Step("reload", CheckReload);
            await Step("views", () => CheckViews(dir));
            await Step("list", () => CheckList(dir));
            await Step("groups", () => CheckGroups(dir));
            await Step("search", () => CheckSearch(dir));
            await Step("filters", () => CheckFilters(dir));
            await Step("detail", () => CheckDetail(dir));
            await Step("fix", () => CheckFix(dir));
            await Step("poster", () => CheckPoster(dir));
            await Step("remove", () => CheckRemove(dir));
            await Step("watch next", () => CheckWatchNext(dir));
            await Step("job", () => CheckJobs(dir));
            await Step("top bar", () => CheckTopBar(dir));
            await Step("language", () => CheckLanguage(dir));
            await Step("poster size", () => CheckPosterSize(dir));
            await Step("empty", () => CheckEmpty(dir));
        }
        Note(failed == 0 ? "all checks passed" : $"{failed} checks FAILED");
    }

    async Task Step(string name, Func<Task> run)
    {
        Note("--- " + name);
        try
        {
            await run();
        }
        catch (Exception e)
        {
            failed++;
            Note("FAIL exception: " + e);
        }
    }

    // ---------- UD1: a mark does not touch the list ----------
    async Task CheckMarks(string dir)
    {
        ResetFilters();
        await Task.Delay(800);
        await ScrollGrid(4000);
        double y = GridOffset;
        var source = MovieGrid.ItemsSource;
        var rendered = renderCount;
        var m = view[60];
        var item = itemOf[m];
        ShowDetail(m);
        await Task.Delay(1500);
        await Shot(dir, "m-detail", Root);
        y = GridOffset;
        bool fav = m.Fav;
        var changed = new List<string>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");
        FavToggle.IsChecked = !fav;
        OnFavClick(FavToggle, new RoutedEventArgs());
        await Task.Delay(400);
        Check("favorite: list not replaced", ReferenceEquals(source, MovieGrid.ItemsSource) && rendered == renderCount, $"renders {rendered}->{renderCount}");
        Check("favorite: scroll kept", Math.Abs(GridOffset - y) < 1, $"{y} -> {GridOffset}");
        Check("favorite: card told", changed.Contains(nameof(MovieItem.FavVis)));
        Check("favorite: movie changed", m.Fav != fav);
        await saving;
        var again = await Task.Run(lib.LoadMovies);
        Check("favorite: saved", again.Find(x => x.K == m.K)!.Fav != fav);
        FavToggle.IsChecked = fav;
        OnFavClick(FavToggle, new RoutedEventArgs());

        bool seen = m.Watched;
        WatchedToggle.IsChecked = !seen;
        OnWatchedClick(WatchedToggle, new RoutedEventArgs());
        await Task.Delay(300);
        Check("watched: list not replaced", ReferenceEquals(source, MovieGrid.ItemsSource));
        Check("watched: date shown", seen || WatchedDate.Text.Length > 0, WatchedDate.Text);
        WatchedToggle.IsChecked = seen;
        OnWatchedClick(WatchedToggle, new RoutedEventArgs());

        int? rate = m.MyRating;
        MyRating.Value = 7;
        OnMyRatingChanged(MyRating, this);
        await Task.Delay(300);
        Check("rating: saved on movie", m.MyRating == 7, m.MyRating?.ToString() ?? "null");
        Check("rating: label", MyRatingLabel.Text == "7/10", MyRatingLabel.Text);
        Check("rating: list not replaced", ReferenceEquals(source, MovieGrid.ItemsSource));
        MyRating.Value = rate ?? -1;
        OnMyRatingChanged(MyRating, this);
        await Task.Delay(300);
        Check("rating: back", m.MyRating == rate);

        string? notes = m.Notes;
        NotesBox.Text = "check note";
        CommitNotes();
        await Task.Delay(300);
        Check("notes: kept", m.Notes == "check note");
        Check("notes: list not replaced", ReferenceEquals(source, MovieGrid.ItemsSource));
        Check("all marks: scroll kept", Math.Abs(GridOffset - y) < 1, $"{y} -> {GridOffset}");
        NotesBox.Text = notes ?? "";
        CommitNotes();
        await saving;

        // with a filter that the mark takes the movie out of, the list follows and keeps its place
        F.Status.Add("unwatched");
        BuildFacets();
        Apply();
        await Task.Delay(800);
        await ScrollGrid(3000);
        y = GridOffset;
        var target = view[40];
        ShowDetail(target);
        await Task.Delay(300);
        WatchedToggle.IsChecked = true;
        OnWatchedClick(WatchedToggle, new RoutedEventArgs());
        await Task.Delay(800);
        Check("filtered: movie left the list", !view.Contains(target));
        Check("filtered: place kept", Math.Abs(GridOffset - y) < 2, $"{y} -> {GridOffset}");
        WatchedToggle.IsChecked = false;
        OnWatchedClick(WatchedToggle, new RoutedEventArgs());
        await saving;
        CloseDetailPane();
        ResetFilters();
        await Task.Delay(500);
    }

    // ---------- selection: kept across changes ----------
    async Task CheckSelection(string dir)
    {
        await ScrollGrid(2000);
        SetSelecting(true);
        await Task.Delay(500);
        for (int i = 30; i < 34; i++) MovieGrid.SelectedItems.Add(MovieGrid.Items[i]);
        await Task.Delay(500);
        var chosen = Selected.ToList();
        Check("selection: tracked", sel.Count == 4, sel.Count.ToString());
        Check("selection: count text", BulkCount.Text.StartsWith("4 "), BulkCount.Text);
        await Shot(dir, "m-select", Root);
        double y = GridOffset;
        var was = chosen.Select(m => m.Fav).ToList();
        var source = MovieGrid.ItemsSource;
        BulkSet(Loc.T("Added to favorites"), fav: true);
        await Task.Delay(500);
        Check("bulk: selection kept", sel.Count == 4 && MovieGrid.SelectedItems.Count == 4, $"{sel.Count}/{MovieGrid.SelectedItems.Count}");
        Check("bulk: scroll kept", Math.Abs(GridOffset - y) < 1, $"{y} -> {GridOffset}");
        Check("bulk: list not replaced", ReferenceEquals(source, MovieGrid.ItemsSource));
        Check("bulk: marks on", chosen.All(m => m.Fav));
        foreach (var (m, f) in chosen.Zip(was)) if (!f) lib.SetUser(new[] { m.K }, fav: false);
        await LoadAsync();
        await Task.Delay(800);
        Check("reload: selection kept", sel.Count == 4 && MovieGrid.SelectedItems.Count == 4, $"{sel.Count}/{MovieGrid.SelectedItems.Count}");
        Check("reload: scroll kept", Math.Abs(GridOffset - y) < 2, $"{y} -> {GridOffset}");
        // another filter hides some of them; taking it off brings them back selected
        F.Genre.Add("Horror");
        BuildFacets();
        Apply();
        await Task.Delay(500);
        Check("filter: selection survives", sel.Count == 4, sel.Count.ToString());
        F.Genre.Clear();
        BuildFacets();
        Apply();
        await Task.Delay(800);
        Check("filter off: rows selected again", MovieGrid.SelectedItems.Count == 4, MovieGrid.SelectedItems.Count.ToString());
        // shift-click: the range goes through the same code as a real click
        var a = shown[10];
        var b = shown[15];
        sel.Clear();
        syncingSel = true;
        MovieGrid.SelectedItems.Clear();
        syncingSel = false;
        SelectBetween(a, b);
        await Task.Delay(300);
        Check("range: selected", sel.Count == 6 && MovieGrid.SelectedItems.Count == 6, $"{sel.Count}/{MovieGrid.SelectedItems.Count}");
        foreach (var x in BulkActions.Children.OfType<Button>().Where(x => (string)x.Tag == "all")) Press(x);
        foreach (var x in BulkActions.Children.OfType<HyperlinkButton>().Where(x => (string)x.Tag == "all")) Press(x);
        await Task.Delay(500);
        Check("select all", sel.Count == view.Count && MovieGrid.SelectedItems.Count == view.Count, $"{sel.Count}/{MovieGrid.SelectedItems.Count}/{view.Count}");
        foreach (var x in BulkActions.Children.OfType<HyperlinkButton>().Where(x => (string)x.Tag == "none")) Press(x);
        await Task.Delay(300);
        Check("clear", sel.Count == 0 && MovieGrid.SelectedItems.Count == 0);
        Check("no selection: actions off", !BulkActions.Children.OfType<Button>().First(x => (string)x.Tag == "fav").IsEnabled);
        Check("esc leaves selection", EscapeChain() && !selecting);
    }

    async Task CheckReload()
    {
        await ScrollGrid(5000);
        double y = GridOffset;
        var rendered = renderCount;
        await LoadAsync();
        await Task.Delay(800);
        Check("reload: scroll kept", Math.Abs(GridOffset - y) < 2, $"{y} -> {GridOffset}");
        Check("reload: one render", renderCount == rendered + 1, $"{rendered}->{renderCount}");
        // a change that arrives during a load is not lost
        var first = LoadAsync();
        var second = LoadAsync();
        Check("reload: second request waits", second.IsCompleted);
        await first;
        Check("reload: second request ran", renderCount == rendered + 3, $"{rendered}->{renderCount}");
    }

    // ---------- UD2: views open in both apps ----------
    async Task CheckViews(string dir)
    {
        const string python = """
            {"F":{"q":"love","status":["unwatched"],"genre":["Drama"],"decade":["1990"],"yMin":1990,"yMax":2005,"rMin":7.5,"vMin":4,"rtMin":60,"mcMin":0,
            "runtime":["120"],"res":["1080p"],"src":[],"tech":[],"type":["movie"],"country":"","lang":"","person":null},"sort":"votes","rev":true,"group":"decade"}
            """;
        lib.SaveView("check python view", JsonNode.Parse(python));
        await LoadViews();
        var v = views.Find(x => x.Name == "check python view")!;
        RestoreView(v);
        await Task.Delay(800);
        Check("view: search", SearchBox.Text == "love" && F.Q == "love", SearchBox.Text);
        Check("view: sets", F.Status.Contains("unwatched") && F.Genre.Contains("Drama") && F.Decade.Contains("1990") && F.Runtime.Contains("120") && F.Res.Contains("1080p") && F.Type.Contains("movie"));
        Check("view: checks shown", status.Find(o => o.Key == "unwatched")!.IsChecked && genre.Find(o => o.Key == "Drama")!.IsChecked);
        Check("view: year boxes", YearMin.Value == 1990 && YearMax.Value == 2005, $"{YearMin.Value} {YearMax.Value}");
        Check("view: sliders", RatingMin.Value == 7.5 && VotesMin.Value == 4 && RtMin.Value == 60, $"{RatingMin.Value} {VotesMin.Value} {RtMin.Value}");
        Check("view: labels", RatingLabel.Text.Contains("7.5") && VotesLabel.Text.Contains("10k"), RatingLabel.Text + " / " + VotesLabel.Text);
        Check("view: sort", sortKey == "votes" && reverse && ((ComboBoxItem)SortBox.SelectedItem).Tag as string == "votes" && ReverseBtn.IsChecked == true);
        Check("view: group", groupKey == "decade" && ((ComboBoxItem)GroupBox.SelectedItem).Tag as string == "decade");
        Check("view: results filtered", view.Count > 0 && view.Count < 300 && view.All(m => !m.Watched && m.Genres.Contains("Drama")), view.Count.ToString());
        await Shot(dir, "m-view", Root);
        // saved here, the same object comes back out of the table
        ViewName.Text = "check native view";
        await SaveCurrentView();
        var stored = views.Find(x => x.Name == "check native view")!;
        Check("view: stored as Python stores it", JsonNode.DeepEquals(JsonNode.Parse(python), stored.State), stored.State.ToJsonString());
        Check("view: list shows both", Under<Button>(ViewsList).Count(b => b.Content is TextBlock t && t.Text.StartsWith("check ")) == 2);
        await Shot(dir, "m-views", ViewsList);
        foreach (var x in views.Where(x => x.Name.StartsWith("check "))) lib.DeleteView(x.Id);
        await LoadViews();
        // delete asks first
        lib.SaveView("check delete", new JsonObject());
        await LoadViews();
        var del = DeleteView(views.Find(x => x.Name == "check delete")!);
        await Task.Delay(600);
        Check("view: delete asks", OpenDialog() != null);
        await Shot(dir, "m-view-delete", DialogBox());
        Press(DialogButton("CloseButton"));
        await del;
        await LoadViews();
        Check("view: cancel keeps it", views.Exists(x => x.Name == "check delete"));
        del = DeleteView(views.Find(x => x.Name == "check delete")!);
        await Task.Delay(600);
        Press(DialogButton("PrimaryButton"));
        await del;
        Check("view: yes deletes it", !views.Exists(x => x.Name == "check delete"));
        sortKey = "rating";
        reverse = false;
        groupKey = "none";
        restoring = true;
        SelectTag(SortBox, sortKey);
        SelectTag(GroupBox, groupKey);
        ReverseBtn.IsChecked = false;
        restoring = false;
        UpdateSortHeader();
        ResetFilters();
    }

    // ---------- U13: the table ----------
    async Task CheckList(string dir)
    {
        SetList(true);
        await Task.Delay(1500);
        Check("table: sort header marked", (HeadImdb.Content as string)?.Contains('▲') == true || (HeadImdb.Content as string)?.Contains('▼') == true, HeadImdb.Content as string ?? "");
        await Shot(dir, "m-list", Root);
        OnHeaderSort(HeadYear, new RoutedEventArgs());
        await Task.Delay(400);
        Check("table: click sorts", sortKey == "year-desc" && !reverse && ((ComboBoxItem)SortBox.SelectedItem).Tag as string == "year-desc");
        int? top = view[0].Year;
        OnHeaderSort(HeadYear, new RoutedEventArgs());
        await Task.Delay(400);
        Check("table: second click reverses", sortKey == "year-desc" && reverse && view[^1].Year == top, $"{top} -> {view[^1].Year}");
        // choosing a sort in the list starts its normal direction
        SortBox.SelectedIndex = 0;
        await Task.Delay(300);
        Check("sort change clears reverse", sortKey == "title" && !reverse && ReverseBtn.IsChecked == false);
        SelectTag(SortBox, "rating");
        await Task.Delay(300);
        SetList(false);
        await Task.Delay(800);
    }

    // ---------- group headings ----------
    async Task CheckGroups(string dir)
    {
        SelectTag(GroupBox, "decade");
        await Task.Delay(1500);
        await Shot(dir, "m-group", Root);
        int before = shown.Count;
        var fake = Under<Button>(MovieGrid).First(b => b.Tag is string);
        var label = (string)fake.Tag;
        Press(fake);
        await Task.Delay(1000);
        Check("group: closed hides its movies", shown.Count < before, $"{before} -> {shown.Count}");
        var first = Groups(view.Select(m => itemOf[m]).ToList())[0];
        Check("group: closed keeps heading and count", first.Closed && first.Count == 0 && first.Total > 0, $"{first.Label} {first.Total}");
        await Shot(dir, "m-group-closed", Root);
        Press(Under<Button>(MovieGrid).First(b => (string?)b.Tag == label));
        await Task.Delay(500);
        Check("group: opens again", shown.Count == before);
        SelectTag(GroupBox, "none");
        await Task.Delay(500);
    }

    // ---------- U12: search ----------
    async Task CheckSearch(string dir)
    {
        recentSearches = new List<string> { "nolan", "rating:>8" };
        ShowSearchPanel(true);
        await Task.Delay(800);
        Check("tips: open", SearchPanelOpen);
        Check("tips: recent listed", Texts(QPanelBody).Contains("nolan"));
        Check("tips: examples listed", Texts(QPanelBody).Contains("year:1990-1999") && Texts(QPanelBody).Contains("Critics agree"));
        await Shot(dir, "m-search-tips", Root);
        var example = Under<Button>(QPanelBody).First(b => Texts(b).Contains("rating:>8") && Texts(b).Contains("IMDb rating above 8"));
        Press(example);
        await Task.Delay(500);
        Check("tips: click adds", SearchBox.Text == "rating:>8" && F.Q == "rating:>8", SearchBox.Text);
        Check("tips: closed after", !SearchPanelOpen);
        Check("tag: search term listed", Texts(ActiveTags).Contains("rating:>8"), Texts(ActiveTags));
        RemoveTag("q:rating:>8");
        await Task.Delay(300);
        Check("tag: removes the term", SearchBox.Text.Length == 0 && F.Q.Length == 0);
        RememberSearch("a");
        RememberSearch("second");
        RememberSearch("nolan");
        Check("recent: newest first, no one letter, no twins", recentSearches.SequenceEqual(new[] { "nolan", "second", "rating:>8" }), string.Join(",", recentSearches));
        for (int i = 0; i < 8; i++) RememberSearch("query " + i);
        Check("recent: six kept", recentSearches.Count == 6);
        SetSearchText("audio:persian");
        Apply();
        await Task.Delay(300);
        Check("search: audio field works", view.All(m => m.FAudio.Contains("persian")), view.Count.ToString());
        SetSearchText("matrix");
        Apply();
        await Task.Delay(300);
        Check("esc: closes tips first", ShowTipsThenEsc());
        DetailSplit.IsPaneOpen = true;
        Check("esc: then the pane", EscapeChain() && !DetailSplit.IsPaneOpen);
        Check("esc: then the search text", EscapeChain() && SearchBox.Text.Length == 0 && F.Q.Length == 0);
        Check("esc: nothing left", !EscapeChain());
        await Task.Delay(300);
    }

    bool ShowTipsThenEsc()
    {
        ShowSearchPanel(true);
        return EscapeChain() && !SearchPanelOpen && SearchBox.Text == "matrix";
    }

    // ---------- U11: tags, section counts ----------
    async Task CheckFilters(string dir)
    {
        Under<CheckBox>(GenreList).First(c => (c.Content as string)?.StartsWith("Comedy") == true).IsChecked = true;
        await Task.Delay(300);
        Check("option: filters", F.Genre.Contains("Comedy") && view.All(m => m.Genres.Contains("Comedy")), view.Count.ToString());
        Check("option: the sets stay the truth", genre.Find(o => o.Key == "Comedy")!.IsChecked);
        KindBar.SelectedItem = KindSeries;
        await Task.Delay(500);
        Check("section: series only, filters stay", view.All(m => m.IsSeries) && F.Genre.Contains("Comedy") && pool.Count == seriesCount, $"{view.Count} of {pool.Count}");
        KindBar.SelectedItem = KindAll;
        await Task.Delay(500);
        Check("section: back", pool.Count == all.Count);
        F.Status.Add("top");
        F.YMin = 1990;
        F.RMin = 7.5;
        F.VMin = 5;
        F.RtMin = 60;
        F.McMin = 50;
        F.Person = (1, "Check Person");
        SyncFilterControls();
        BuildFacets();
        Apply();
        await Task.Delay(800);
        string tags = Texts(ActiveTags);
        Check("tags: all listed", tags.Contains("Comedy") && tags.Contains("Top rated 8+") && tags.Contains("1990–…") && tags.Contains("IMDb ≥ 7.5") && tags.Contains("votes ≥ 100k") && tags.Contains("Rotten Tomatoes ≥ 60%") && tags.Contains("Metacritic ≥ 50") && tags.Contains("Check Person"), tags);
        Check("sections: counts", (string)SecGenre.Header == "Genre  (1)" && (string)SecScores.Header == "Ratings  (4)" && (string)SecYear.Header == "Year  (1)", $"{SecGenre.Header} {SecScores.Header} {SecYear.Header}");
        await Shot(dir, "m-tags", Root);
        foreach (var k in new[] { "person", "rMin", "vMin", "rtMin", "mcMin", "year", "status:top" }) RemoveTag(k);
        await Task.Delay(300);
        Check("tags: remove", F.Person == null && F.RMin == 0 && RatingMin.Value == 0 && F.YMin == null && double.IsNaN(YearMin.Value) && F.Status.Count == 0, Texts(ActiveTags));
        RemoveTag("genre:Comedy");
        await Task.Delay(300);
        Check("tags: gone", ActiveTags.Children.Count == 0 && (string)SecGenre.Header == "Genre");
        Check("tags: row hidden", ActiveTags.Visibility == Visibility.Collapsed);
        // a stats bar: a person opens the profile instead of filtering
        PickFromStats("decade", "1990", "1990s");
        await Task.Delay(300);
        Check("stats: decade filters", F.Decade.Contains("1990") && view.All(m => m.Decade == 1990));
        var w = F.Person;
        var someone = all.First(m => m.Dir.Count > 0).Dir[0];
        PickFromStats("person", someone.Id.ToString(), someone.Name);
        await Task.Delay(1500);
        Check("stats: person opens a profile, filters stay", F.Person == w && F.Decade.Contains("1990") && Wnd<PersonWindow>() != null);
        Wnd<PersonWindow>().Close();
        ResetFilters();
        await Task.Delay(300);
    }

    // ---------- U9: the movie pane ----------
    async Task CheckDetail(string dir)
    {
        var rich = all.Where(m => m.Tconst != null && m.Cast.Count > 5 && m.Wri.Count > 0 && m.Crew.Count > 0 && m.Akas.Length > 0 && m.Countries.Length > 0 && !string.IsNullOrEmpty(m.Plot) && m.Rt != null && m.Mc != null)
            .OrderByDescending(m => m.Collection != null).ThenByDescending(m => m.TmdbCastMembers.Count).First();
        ShowDetail(rich);
        await Task.Delay(1500);
        Check("pane: title and rows", DetailTitle.Text == rich.Display && FaRow.Visibility == Visibility.Visible);
        Check("pane: links", ((string)ImdbLink.Content).EndsWith(rich.K) && LetterboxdLink.Visibility == Visibility.Visible && RemoveAllBtn.Visibility == Visibility.Visible);
        var cast = (List<CastItem>)CastList.ItemsSource;
        Check("pane: full cast", cast.Count == rich.Cast.Count, $"{cast.Count} of {rich.Cast.Count}");
        int made = Under<Button>(CastList).Count();
        Check("pane: cast is lazy or short", made <= cast.Count, $"{made} made");
        Press(Under<Button>(CastList).First());
        await Task.Delay(1500);
        Check("pane: a cast member opens the profile", Wnd<PersonWindow>() != null);
        Wnd<PersonWindow>().Close();
        Check("pane: writers and crew", WriText.Inlines.Count > 0 && CrewText.Inlines.Count > 0 && CrewLabel.Visibility == Visibility.Visible);
        Check("pane: akas", AkaLabel.Visibility == Visibility.Visible);
        Check("pane: facts", Texts(DetailFacts).Contains(rich.Countries[0]) && Texts(DetailFacts).Contains("Match"), Texts(DetailFacts));
        Check("pane: files heading", FilesLabel.Text == $"Your files ({rich.Files.Count})");
        await Shot(dir, "m-pane-top", (FrameworkElement)DetailSplit.Pane);
        DetailScroll.ChangeView(null, 700, null, true);
        await Task.Delay(1200);
        await Shot(dir, "m-pane-mid", (FrameworkElement)DetailSplit.Pane);
        DetailScroll.ChangeView(null, 100000, null, true);
        await Task.Delay(1200);
        await Shot(dir, "m-pane-end", (FrameworkElement)DetailSplit.Pane);
        // a genre replaces the genre filter and closes the pane
        var chip = Under<Button>(DetailMeta).First();
        string g = (string)chip.Content;
        Press(chip);
        await Task.Delay(500);
        Check("genre: filters and closes", F.Genre.SetEquals(new[] { g }) && !DetailSplit.IsPaneOpen, g);
        ResetFilters();
        var plain = all.First(m => m.Tconst != null && string.IsNullOrEmpty(m.Plot));
        ShowDetail(plain);
        await Task.Delay(400);
        Check("pane: no plot hint", PlotText.Text.Contains("TMDB key"), PlotText.Text);
        var inCollection = all.Find(m => !string.IsNullOrEmpty(m.Collection));
        if (inCollection != null)
        {
            ShowDetail(inCollection);
            await Task.Delay(400);
            Press(Under<HyperlinkButton>(DetailFacts).First());
            await Task.Delay(500);
            Check("collection: searches and closes", F.Q == inCollection.Collection && !DetailSplit.IsPaneOpen && view.Contains(view.Find(m => m.K == inCollection.K)!), F.Q);
            ResetFilters();
        }
        else Note("skip  no movie with a collection in this data");
        // Persian title: edited, saved, and the pane comes back with it
        var m = all.First(x => x.Tconst != null);
        string? fa = m.FaTitle;
        ShowDetail(m);
        await Task.Delay(300);
        OnFaEdit(FaEditBtn, new RoutedEventArgs());
        Check("persian: editor opens", FaEditor.Visibility == Visibility.Visible);
        await Shot(dir, "m-pane-fa", (FrameworkElement)DetailSplit.Pane);
        FaInput.Text = "عنوان آزمایشی";
        OnFaSave(FaEditBtn, new RoutedEventArgs());
        await Task.Delay(2500);
        Check("persian: pane shows the new title", FaText.Text == "عنوان آزمایشی" && current?.FaTitle == "عنوان آزمایشی" && FaEditor.Visibility == Visibility.Collapsed, FaText.Text);
        OnFaReset(FaEditBtn, new RoutedEventArgs());
        await Task.Delay(2500);
        Check("persian: reset", current?.FaTitle == fa, current?.FaTitle ?? "null");
        // notes go with the pane
        string? n = m.Notes;
        NotesBox.Text = "note while closing";
        CloseDetailPane();
        await saving;
        Check("notes: closing saves", current?.Notes == "note while closing");
        NotesBox.Text = n ?? "";
        CommitNotes();
        await saving;
    }

    // ---------- U15: fix match ----------
    async Task CheckFix(string dir)
    {
        var loose = all.Find(m => m.Tconst == null);
        if (loose == null)
        {
            Note("skip  no unmatched movie in this data");
            loose = all[0];
        }
        ShowDetail(loose);
        await Task.Delay(800);
        var host = fixHosts[loose.Files[0].Id];
        Check("fix: opens by itself when unmatched", loose.Tconst != null || host.Child is FixPanel, loose.Display);
        if (host.Child == null) OpenFix(loose.Files[0].Id);
        var panel = (FixPanel)host.Child!;
        await Task.Delay(6000);
        await Shot(dir, "m-fix", (FrameworkElement)DetailSplit.Pane);
        var box = Under<TextBox>(panel).First();
        Check("fix: box hint", box.PlaceholderText == "Title and year, or IMDb link / tt-number", box.PlaceholderText);
        box.Text = "https://www.imdb.com/title/tt0133093/";
        Press(Under<Button>(panel).First(b => (b.Content as string) == "Search"));
        await Task.Delay(500);
        Check("fix: a link gives one row", Texts(panel).Contains("tt0133093") && Under<Button>(panel).Any(b => (b.Content as string) == "Use this") && !Under<Button>(panel).Any(b => (b.Content as string) == "Mark as not on IMDb"), Texts(panel));
        await Shot(dir, "m-fix-link", (FrameworkElement)DetailSplit.Pane);
        box.Text = "The Matrix 1999";
        Press(Under<Button>(panel).First(b => (b.Content as string) == "Search"));
        await Task.Delay(6000);
        await Shot(dir, "m-fix-results", (FrameworkElement)DetailSplit.Pane);
        Check("fix: results have View and Use this", Under<Button>(panel).Any(b => (b.Content as string) == "View") && Under<Button>(panel).Any(b => (b.Content as string) == "Mark as not on IMDb"), Texts(panel));
        // not on IMDb asks first; Cancel leaves everything
        Press(Under<Button>(panel).First(b => (b.Content as string) == "Mark as not on IMDb"));
        await Task.Delay(700);
        Check("fix: not on IMDb asks", OpenDialog() != null);
        await Shot(dir, "m-fix-confirm", DialogBox());
        Press(DialogButton("CloseButton"));
        await Task.Delay(500);
        Check("fix: cancel keeps the pane", DetailSplit.IsPaneOpen);
        // the dialog the Problems window uses asks inside itself
        var dlg = Dialogs.FixMatch(Root.XamlRoot, lib, loose.Files[0].Id, loose.Files[0].Raw);
        await Task.Delay(6000);
        await Shot(dir, "m-fix-dialog", DialogBox());
        var dp = Under<FixPanel>(DialogBox()).First();
        Press(Under<Button>(dp).First(b => (b.Content as string) == "Mark as not on IMDb"));
        await Task.Delay(500);
        Check("fix dialog: asks inside", Texts(dp).Contains("keeps no IMDb data"), Texts(dp));
        await Shot(dir, "m-fix-dialog-confirm", DialogBox());
        Press(Under<Button>(dp).First(b => (b.Content as string) == "Cancel"));
        await Task.Delay(300);
        Press(DialogButton("CloseButton"));
        Check("fix dialog: cancel returns nothing", await dlg == null);
        CloseDetailPane();
    }

    // ---------- U10: poster by hand ----------
    async Task CheckPoster(string dir)
    {
        var m = all.First(x => x.Tconst != null && x.HasPoster && File.Exists(lib.PosterPath(x)));
        string keep = Path.Combine(dir, "poster-keep.jpg");
        File.Copy(lib.PosterPath(m), keep, true);
        byte[] before = File.ReadAllBytes(keep);
        ShowDetail(m);
        await Task.Delay(500);
        Check("poster: button text", PosterBtnText.Text == "Change poster", PosterBtnText.Text);
        OnPosterToggle(PosterBtn, new RoutedEventArgs());
        await Task.Delay(300);
        Check("poster: box open with hint", PosterBox.Visibility == Visibility.Visible && PosterMsg.Text.StartsWith("JPEG, PNG"));
        Check("poster: remove offered", PosterClearBtn.Visibility == Visibility.Visible);
        await Shot(dir, "m-poster", (FrameworkElement)DetailSplit.Pane);
        OnPosterUse(PosterBtn, new RoutedEventArgs());
        await Task.Delay(300);
        Check("poster: empty box warns", PosterMsg.Text.StartsWith("Paste an image"), PosterMsg.Text);
        PosterUrl.Text = Path.Combine(dir, "no-such-file.jpg");
        OnPosterUse(PosterBtn, new RoutedEventArgs());
        await Task.Delay(800);
        Check("poster: bad path says so", PosterMsg.Text.Contains("not a web address"), PosterMsg.Text);
        string notImage = Path.Combine(dir, "not-an-image.txt");
        File.WriteAllText(notImage, "hello");
        PosterUrl.Text = notImage;
        OnPosterUse(PosterBtn, new RoutedEventArgs());
        await Task.Delay(800);
        Check("poster: not an image says so", PosterMsg.Text.StartsWith("That is not an image"), PosterMsg.Text);
        var icon = Path.Combine(dir, "tiny.png");
        File.WriteAllBytes(icon, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
        PosterUrl.Text = icon;
        OnPosterUse(PosterBtn, new RoutedEventArgs());
        await Task.Delay(3000);
        Check("poster: saved and the pane reopened", File.ReadAllBytes(lib.PosterPath(m)).Length < 200 && current?.K == m.K && PosterBox.Visibility == Visibility.Collapsed && DetailSplit.IsPaneOpen);
        Check("poster: skips the image cache", MovieItem.NewPosters.Contains(m.K));
        OnPosterToggle(PosterBtn, new RoutedEventArgs());
        OnPosterClear(PosterBtn, new RoutedEventArgs());
        await Task.Delay(3000);
        Check("poster: removed", current?.K == m.K && current?.HasPoster == false && PosterBtnText.Text == "Add poster" && DetailPlaceholder.Text.Length > 0, PosterBtnText.Text);
        lib.SetPosterFrom(m.Tconst!.Value, keep);
        await Task.Delay(2500);
        Check("poster: the old one is back", File.ReadAllBytes(lib.PosterPath(m)).SequenceEqual(before));
        CloseDetailPane();
    }

    // ---------- Load list: read-only, the dialog only previews and is cancelled ----------
    public async Task CheckLoadList(string dir)
    {
        checkLog = Path.Combine(dir, "checks.log");
        await rootLoaded.Task;
        string file = Path.Combine(dir, "to-load.txt");
        File.WriteAllText(file, "Heat.1995.720p\nHeat.1995.720p\nA.Film.Nobody.Has.1901.1080p\n");
        OnLoadList(this, new RoutedEventArgs());
        await Task.Delay(900);
        Check("load list: dialog opens", OpenDialog() != null && Texts(DialogBox()).Contains("Read"), Texts(DialogBox()));
        await Shot(dir, "ll-pick", DialogBox());
        var box = Under<TextBox>(DialogBox()).First();
        box.Text = file;
        Press(Under<Button>(DialogBox()).First(b => b.Content as string == "Read"));
        await Task.Delay(1500);
        string t = Texts(DialogBox());
        Check("load list: preview shows counts", t.Contains("3 movie names found") || t.Contains("2 movie names found"), t);
        Check("load list: repeated name flagged", t.Contains("repeated inside this list"), t);
        Check("load list: both choices", t.Contains("Add to current list") && t.Contains("Start a new list"), t);
        await Shot(dir, "ll-choose", DialogBox());
        Press(DialogButton("SecondaryButton"));
        await Task.Delay(700);
        Check("load list: new list asks first", Texts(DialogBox()).Contains("previous lists") && OpenDialog() != null, Texts(DialogBox()));
        await Shot(dir, "ll-confirm", DialogBox());
        Press(DialogButton("CloseButton"));
        await Task.Delay(500);
        Check("load list: cancel closes", OpenDialog() == null);
        Note(failed == 0 ? "all checks passed" : $"{failed} checks FAILED");
    }

    // ---------- U2: remove from list ----------
    async Task CheckRemove(string dir)
    {
        var m = all.First(x => x.Tconst != null && x.Files.Count == 1);
        string raw = m.Files[0].Raw;
        ShowDetail(m);
        await Task.Delay(500);
        int count = all.Count;
        OnRemoveMovie(RemoveAllBtn, new RoutedEventArgs());
        await Task.Delay(700);
        Check("remove: asks", OpenDialog() != null && Texts(DialogBox()).Contains("Remove this title from your list?"), Texts(DialogBox()));
        await Shot(dir, "m-remove", DialogBox());
        Press(DialogButton("CloseButton"));
        await Task.Delay(500);
        Check("remove: cancel keeps it", DetailSplit.IsPaneOpen && all.Count == count);
        OnRemoveMovie(RemoveAllBtn, new RoutedEventArgs());
        await Task.Delay(700);
        Press(DialogButton("PrimaryButton"));
        await Task.Delay(3000);
        Check("remove: gone, pane closed", all.Count == count - 1 && !all.Exists(x => x.K == m.K) && !DetailSplit.IsPaneOpen, $"{count} -> {all.Count}");
        Check("remove: remembered", lib.RemovedItems().Exists(x => x.Raw == raw));
        lib.RestoreRemoved(new[] { raw });
        await Task.Delay(2500);
        Check("remove: restored for the next check", all.Exists(x => x.K == m.K));

        // bulk: a two-file selection counts entries like Python
        var two = all.Where(x => x.Tconst != null && x.Files.Count == 1).Take(2).ToList();
        SetSelecting(true);
        foreach (var x in two) sel.Add(x.K);
        Render(keep: true);
        UpdateBulkCount();
        await Task.Delay(500);
        var bulk = BulkActions.Children.OfType<Button>().First(x => (string)x.Tag == "remove");
        Press(bulk);
        await Task.Delay(700);
        Check("bulk remove: asks for 2", Texts(DialogBox()).Contains("Remove 2 titles from your list?"), Texts(DialogBox()));
        Press(DialogButton("PrimaryButton"));
        await Task.Delay(3000);
        Check("bulk remove: gone and selection empty", sel.Count == 0 && !all.Exists(x => x.K == two[0].K));
        lib.RestoreRemoved(two.Select(x => x.Files[0].Raw));
        await Task.Delay(2500);
        SetSelecting(false);
        Check("bulk remove: restored", all.Exists(x => x.K == two[0].K));
    }

    // ---------- UD10: watch next ----------
    async Task CheckWatchNext(string dir)
    {
        var show = Dialogs.WatchNext(this, () => all, () => view, ShowDetail, WatchedFromPick);
        await Task.Delay(1500);
        var box = DialogBox();
        Check("wn: counts in the pool names", ((ComboBoxItem)Under<ComboBox>(box).First().SelectedItem).Content?.ToString()?.Contains($"{view.Count:N0}") == true);
        await Shot(dir, "m-wn", box);
        var combos = Under<ComboBox>(box).ToList();
        combos[1].SelectedIndex = 2;   // hidden gems
        await Task.Delay(500);
        Check("wn: gems hint", Texts(box).Contains("Hidden gems: rated 7.2"), "");
        var firstPick = Under<Button>(box).First(b => (b.Content as string) == "Watched");
        int cards = Under<Button>(box).Count(b => (b.Content as string) == "Watched");
        var marked = all.Where(m => m.Watched).Select(m => m.K).ToHashSet();
        Press(firstPick);
        await Task.Delay(800);
        var newly = all.Where(m => m.Watched && !marked.Contains(m.K)).ToList();
        Check("wn: watched saved and a new pick takes its place", newly.Count == 1 && Under<Button>(DialogBox()).Count(b => (b.Content as string) == "Watched") == cards, $"{newly.Count} / {cards}");
        Press(DialogButton("CloseButton"));
        await show;
        foreach (var m in newly) lib.SetUser(new[] { m.K }, watched: false);
        await saving;
        await LoadAsync();
        show = Dialogs.WatchNext(this, () => all, () => view, ShowDetail, WatchedFromPick);
        await Task.Delay(1000);
        Check("wn: choices remembered", ((ComboBoxItem)Under<ComboBox>(DialogBox()).ElementAt(1).SelectedItem).Tag as string == "gems");
        Press(DialogButton("CloseButton"));
        await show;
    }

    // ---------- UD8: the job bar ----------
    async Task CheckJobs(string dir)
    {
        Toast("Sample message");
        Toast("Sample information", "info");
        Toast("Sample error", "err");
        await Task.Delay(300);
        Check("toast: shown", ToastHost.Children.Count >= 3);
        for (int i = 0; i < 4; i++) Toast("x");
        await Task.Delay(100);
        Check("toast: three at most", ToastHost.Children.Count == 3, ToastHost.Children.Count.ToString());
        var stop = new ManualResetEventSlim();
        lib.Start(() =>
        {
            lib.Progress("Matching titles with IMDb", 30, 100);
            stop.Wait(TimeSpan.FromSeconds(4));
        }, stoppable: false);
        await Task.Delay(700);
        Check("job: bar shows", JobBar.Visibility == Visibility.Visible && JobText.Text.Contains("30 / 100") && JobText.Text.Contains("—"), JobText.Text);
        Check("job: no Stop when it cannot stop", JobStop.Visibility == Visibility.Collapsed);
        await Shot(dir, "m-job", Root);
        stop.Set();
        await Task.Delay(800);
        Check("job: bar leaves", JobBar.Visibility == Visibility.Collapsed);
        Check("job: finish toast", Texts(ToastHost).Contains("Update finished"), Texts(ToastHost));
        lib.Start(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                lib.Progress("Reading lists", i, 100);
                Thread.Sleep(100);
            }
        });
        await Task.Delay(600);
        Check("job: Stop offered", JobStop.Visibility == Visibility.Visible);
        lib.Stop();
        OnJobChanged();
        await Task.Delay(30);
        await Task.Delay(800);
        Check("job: stopped toast", Texts(ToastHost).Contains("Update stopped"), Texts(ToastHost));
        lib.Start(() => throw new InvalidOperationException("No .txt files found in X"));
        await Task.Delay(800);
        Check("job: error bar", JobBar.Visibility == Visibility.Visible && JobText.Text.StartsWith("The last update stopped:") && JobLog.Visibility == Visibility.Visible && JobDismiss.Visibility == Visibility.Visible && JobStop.Visibility == Visibility.Collapsed, JobText.Text);
        await Shot(dir, "m-job-error", Root);
        OnJobDismiss(JobDismiss, new RoutedEventArgs());
        Check("job: dismissed stays away", dismissedError != null && JobBar.Visibility == Visibility.Collapsed);
        UpdateJobUi();
        Check("job: still away after a refresh", JobBar.Visibility == Visibility.Collapsed);
        dismissedError = null;
        await Task.Delay(8000);
        Check("toast: leaves by itself", ToastHost.Children.Count == 0, ToastHost.Children.Count.ToString());
        // queued job: the second update waits behind the first, once
        var gate = new ManualResetEventSlim();
        lib.Start(() => gate.Wait(TimeSpan.FromSeconds(3)));
        await Task.Delay(200);
        Check("update: no second job while one runs", !lib.Start(() => { }));
        gate.Set();
        await Task.Delay(1500);
        await LoadAsync();
    }

    // ---------- UD3, U15: the top bar ----------
    async Task CheckTopBar(string dir)
    {
        await Task.Delay(1500);
        Check("badge: problems", ProblemsBadge.Visibility == Visibility.Visible && ProblemsCount.Text.Length > 0, ProblemsCount.Text);
        Check("badge: wishlist text fits", WishCount.Text.Length is 0 or <= 3, WishCount.Text);
        ThemeBtn.Flyout.ShowAt(ThemeBtn);
        await Task.Delay(600);
        Check("theme menu: three choices", ThemeMenu.Items.Count == 3 && ThemeMenu.Items.OfType<RadioMenuFlyoutItem>().Count(i => i.IsChecked) == 1);
        ThemeBtn.Flyout.Hide();
        await Task.Delay(300);
        await Shot(dir, "m-top", AppTitleBar);
        string was = Theme;
        SetTheme("dark");
        ShowDetail(view[2]);
        await Task.Delay(800);
        Check("theme: dark moon", ThemeIcon.Glyph == "\uE708" && Root.RequestedTheme == ElementTheme.Dark);
        Root.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20));
        await Task.Delay(300);
        await Shot(dir, "m-dark", Root);
        SetTheme("light");
        await Task.Delay(800);
        Check("theme: light sun", ThemeIcon.Glyph == "\uE706");
        Root.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3));
        await Task.Delay(300);
        await Shot(dir, "m-light", Root);
        SetTheme(was);
        CloseDetailPane();
        await Task.Delay(300);
        await ScrollGrid(3000);
        await Task.Delay(300);
        Check("to top: shows far down", ToTopBtn.Visibility == Visibility.Visible);
        await Shot(dir, "m-totop", Root);
        OnToTop(ToTopBtn, new RoutedEventArgs());
        await Task.Delay(1200);
        Check("to top: goes up and hides", GridOffset < 5 && ToTopBtn.Visibility == Visibility.Collapsed, GridOffset.ToString());
    }

    // ---------- language ----------
    async Task CheckLanguage(string dir)
    {
        var m = view[3];
        ShowDetail(m);
        F.Genre.Add("Drama");
        F.RMin = 8;
        SyncFilterControls();
        BuildFacets();
        Apply();
        await Task.Delay(800);
        var s0 = lib.LoadSettings().Language;
        OnLanguageClick(LangBtn, new RoutedEventArgs());
        await Task.Delay(1800);
        Check("language: switched at once", Loc.IsFa && Root.FlowDirection == FlowDirection.RightToLeft && (string)LangBtn.Content == "EN");
        Check("language: saved", lib.LoadSettings().Language == "fa");
        Check("language: pane text", FilesLabel.Text.Contains(Loc.T("Your files ({0})").Split('(')[0].Trim()) && DetailTitle.Text == m.Display, FilesLabel.Text);
        Check("language: filters kept", F.Genre.Contains("Drama") && RatingLabel.Text.Contains("8.0"), RatingLabel.Text);
        await Shot(dir, "m-fa", Root);
        await Shot(dir, "m-fa-pane", (FrameworkElement)DetailSplit.Pane);
        ShowSearchPanel(true);
        await Task.Delay(500);
        await Shot(dir, "m-fa-tips", Root);
        ShowSearchPanel(false);
        SetSelecting(true);
        await Task.Delay(500);
        await Shot(dir, "m-fa-select", Root);
        SetSelecting(false);
        SetList(true);
        await Task.Delay(1200);
        await Shot(dir, "m-fa-list", Root);
        SetList(false);
        await Task.Delay(500);
        var show = RemoveFiles(new List<int> { m.Files[0].Id });
        await Task.Delay(700);
        await Shot(dir, "m-fa-remove", DialogBox());
        Press(DialogButton("CloseButton"));
        await show;
        var wn = Dialogs.WatchNext(this, () => all, () => view, ShowDetail, WatchedFromPick);
        await Task.Delay(1200);
        await Shot(dir, "m-fa-wn", DialogBox());
        Press(DialogButton("CloseButton"));
        await wn;
        ResetFilters();
        await Task.Delay(800);
        CloseDetailPane();
        OnLanguageClick(LangBtn, new RoutedEventArgs());
        await Task.Delay(1500);
        Check("language: back to English", !Loc.IsFa && (string)LangBtn.Content == "FA" && Root.FlowDirection == FlowDirection.LeftToRight);
        var s = lib.LoadSettings();
        s.Language = s0;
        lib.SaveSettings(s);
    }

    // ---------- U15: poster size ----------
    async Task CheckPosterSize(string dir)
    {
        int wide = MovieItem.PosterWidth;
        SetPosterSize("s");
        await Task.Delay(1500);
        Check("size: small", MovieItem.CardWidth == 132 && MovieItem.PosterWidth < wide, MovieItem.PosterWidth.ToString());
        double smallCard = Under<GridViewItem>(MovieGrid).First().ActualWidth;
        await Shot(dir, "m-size-s", Root);
        SetPosterSize("l");
        await Task.Delay(1500);
        double largeCard = Under<GridViewItem>(MovieGrid).First().ActualWidth;
        Check("size: large", MovieItem.CardWidth == 204 && MovieItem.PosterWidth > wide && largeCard > smallCard, $"{MovieItem.PosterWidth}, cards {smallCard} -> {largeCard}");
        await Shot(dir, "m-size-l", Root);
        SetPosterSize("m");
        await Task.Delay(800);
        Check("size: medium", MovieItem.PosterWidth == wide);
    }

    // ---------- U15: the empty screens ----------
    async Task CheckEmpty(string dir)
    {
        SetSearchText("zzzqqqxxx");
        Apply();
        await Task.Delay(400);
        Check("empty: filters text and button", EmptyPanel.Visibility == Visibility.Visible && EmptyTitle.Text == "No movies match these filters." && EmptyButtons.Children.Count == 1);
        await Shot(dir, "m-empty", Root);
        Press((Button)EmptyButtons.Children[0]);
        await Task.Delay(400);
        Check("empty: reset works", F.Q.Length == 0 && view.Count > 0);
        // the screens of an empty library are decided from the job state
        var keep = all;
        all = new();
        view = new();
        pool = new();
        RenderEmpty();
        Check("empty: building", EmptyTitle.Text == "Building your library…", EmptyTitle.Text);
        lib.Job.Error = "InvalidOperationException: No .txt files found in X";
        RenderEmpty();
        Check("empty: no list", EmptyTitle.Text == "No movie list found yet" && EmptyButtons.Children.Count == 2, EmptyTitle.Text);
        await Shot(dir, "m-empty-nolist", Root);
        kind = "series";
        all = keep;
        RenderEmpty();
        Check("empty: no series", EmptyTitle.Text == "No series in your library yet.", EmptyTitle.Text);
        await Shot(dir, "m-empty-series", Root);
        kind = "all";
        lib.Job.Error = null;
        view = all;
        pool = all;
        BuildFacets();
        Apply();
        await Task.Delay(300);
    }

    // ---------- filters across a restart ----------
    async Task CheckPrefsWrite()
    {
        F.Genre.Add("Drama");
        F.Decade.Add("1990");
        F.YMin = 1991;
        F.RMin = 7;
        F.Lang = "English";
        F.Q = "not kept";
        SyncFilterControls();
        BuildFacets();
        Apply();
        OnHeaderSort(HeadYear, new RoutedEventArgs());
        recentSearches = new List<string> { "kept search" };
        sections["runtime"] = true;
        await Task.Delay(1500);
        WritePrefs(PrefsNow());
        Note("wrote " + File.ReadAllText(prefsPath));
    }

    void CheckPrefsRead()
    {
        Check("restart: filters back", F.Genre.Contains("Drama") && F.Decade.Contains("1990") && F.YMin == 1991 && F.RMin == 7 && F.Lang == "English", F.ToJson().ToJsonString());
        Check("restart: controls show them", YearMin.Value == 1991 && RatingMin.Value == 7 && genre.Find(o => o.Key == "Drama")?.IsChecked == true);
        Check("restart: tags show them", Texts(ActiveTags).Contains("Drama") && Texts(ActiveTags).Contains("IMDb ≥ 7"), Texts(ActiveTags));
        Check("restart: search text not kept", F.Q.Length == 0 && SearchBox.Text.Length == 0);
        Check("restart: results filtered", view.Count > 0 && view.All(m => m.Genres.Contains("Drama") && m.Decade == 1990), view.Count.ToString());
        Check("restart: one render at start-up", renderCount == 1, renderCount.ToString());
        Check("restart: sort kept", sortKey == "year-desc");
        Check("restart: section kept", SecRuntime.IsExpanded);
        Check("restart: recent kept", recentSearches.Contains("kept search"));
    }

    // ---------- the first seconds ----------
    async Task DebugStart(string dir)
    {
        checkLog = Path.Combine(dir, "checks.log");
        var sw = Stopwatch.StartNew();
        Root.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        await rootLoaded.Task;
        if (Environment.GetEnvironmentVariable("MOVIESHELF_SHOT_ONLY") == "welcome")
        {
            while (OpenDialog() == null && sw.Elapsed.TotalSeconds < 20) await Task.Delay(100);
            Note($"welcome after {sw.Elapsed.TotalSeconds:0.0} s, first run {lib.FirstRun}, job running {lib.Job.Running}");
            Check("first run: welcome opens", OpenDialog() != null);
            Check("first run: no update yet", !lib.Job.Running);
            await Task.Delay(800);
            await Shot(dir, "s-welcome", DialogBox());
            Press(DialogButton("CloseButton"));
            await Task.Delay(800);
            await Shot(dir, "s-building", Root);
            Check("first run: building text", EmptyTitle.Text == "Building your library…", EmptyTitle.Text);
            return;
        }
        while (!lib.Job.Running && sw.Elapsed.TotalSeconds < 20) await Task.Delay(50);
        double started = sw.Elapsed.TotalMilliseconds;
        Check("start: update running", lib.Job.Running, $"after {started:0} ms");
        while (JobBar.Visibility != Visibility.Visible && lib.Job.Running) await Task.Delay(10);
        Check("start: job bar shows it", JobBar.Visibility == Visibility.Visible, JobText.Text);
        await Shot(dir, "s-update", Root);
        Check("start: tips stay closed", !SearchPanelOpen);
        string today = Path.Combine(lib.DataDir, "backups", $"personal-{DateTime.Now:yyyy-MM-dd}.json");
        Check("start: today's backup", File.Exists(today), today);
        lib.Stop();
        await Task.Delay(2500);
        Note("job after stop: running=" + lib.Job.Running + " stopped=" + lib.Job.Stopped);
    }

    // real key presses, sent only while this window is in front
    async Task CheckKeys()
    {
        Root.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Activate();
        SetForegroundWindow(hwnd);
        await Task.Delay(1000);
        bool front = GetForegroundWindow() == hwnd;
        var kids = new List<IntPtr>();
        EnumChildWindows(hwnd, (h, _) =>
        {
            var name = new System.Text.StringBuilder(128);
            GetClassName(h, name, 128);
            Note($"child {h} {name}");
            if (name.ToString().Contains("InputSite")) kids.Add(h);
            return true;
        }, IntPtr.Zero);
        Note(front ? "keys go through the keyboard" : $"not in front: keys are posted to {kids.Count} input window(s)");
        void Key(ushort vk)
        {
            if (front && GetForegroundWindow() == hwnd)
            {
                keybd_event((byte)vk, 0, 0, 0);
                keybd_event((byte)vk, 0, 2, 0);
                return;
            }
            foreach (var h in kids)
            {
                PostMessage(h, 0x100, (nuint)vk, 1);
                PostMessage(h, 0x101, (nuint)vk, unchecked((nint)0xC0000001));
            }
        }
        MovieGrid.Focus(FocusState.Keyboard);
        await Task.Delay(500);
        Key(0xBF);   // the / key on a US layout
        await Task.Delay(800);
        Check("key /: search has the focus", FocusManager_Search(), FocusManager.GetFocusedElement(Root.XamlRoot)?.GetType().Name ?? "none");
        Check("key /: tips open", SearchPanelOpen);
        Check("key /: no slash typed", SearchBox.Text.Length == 0, SearchBox.Text);
        Key(0x1B);
        await Task.Delay(500);
        Check("key Esc: tips closed", !SearchPanelOpen);
        Key(0x4D);   // m
        Key(0x41);   // a
        await Task.Delay(800);
        Check("typing goes to the box", SearchBox.Text.Equals("ma", StringComparison.OrdinalIgnoreCase), SearchBox.Text);
        Check("typing filters after the delay", F.Q.Equals("ma", StringComparison.OrdinalIgnoreCase), F.Q);
        Key(0x1B);
        await Task.Delay(500);
        Check("key Esc: clears the text", SearchBox.Text.Length == 0, SearchBox.Text);
        ShowDetail(view[0]);
        await Task.Delay(800);
        Key(0x1B);
        await Task.Delay(500);
        Check("key Esc: closes the pane", !DetailSplit.IsPaneOpen);
        ShowDetail(view[0]);
        await Task.Delay(500);
        NotesBox.Focus(FocusState.Programmatic);
        await Task.Delay(300);
        Key(0xBF);
        await Task.Delay(500);
        Check("key /: in a text box it is only a slash", NotesBox.Text.EndsWith('/') && !SearchPanelOpen, NotesBox.Text);
        NotesBox.Text = view[0].Notes ?? "";
        CloseDetailPane();
        SetSelecting(true);
        await Task.Delay(300);
        Key(0x1B);
        await Task.Delay(500);
        Check("key Esc: leaves selection", !selecting);
        MovieGrid.Focus(FocusState.Keyboard);
        await Task.Delay(300);
        var c = MovieGrid.ContainerFromIndex(0) as GridViewItem;
        c?.Focus(FocusState.Keyboard);
        await Task.Delay(300);
        Key(0x0D);   // Enter
        await Task.Delay(800);
        Check("key Enter: opens the focused poster", DetailSplit.IsPaneOpen);
        CloseDetailPane();
    }

    bool FocusManager_Search() => FocusManager.GetFocusedElement(Root.XamlRoot) is DependencyObject d && (d == SearchBox || Under<DependencyObject>(SearchBox).Contains(d));

    delegate bool EnumProc(IntPtr hwnd, IntPtr param);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr hwnd, EnumProc proc, IntPtr param);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int size);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hwnd, uint msg, nuint wparam, nint lparam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern void keybd_event(byte vk, byte scan, uint flags, nuint extra);
}
