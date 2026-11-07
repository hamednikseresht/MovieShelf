using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MovieShelf.Core;
using Windows.Storage.Pickers;

namespace MovieShelf.App;

// The movie pane: everything the Python app's movie window shows, in the same order.
public sealed partial class MainWindow
{
    static readonly Dictionary<string, (string Label, string Tone)> ConfLabel = new()
    {
        ["exact"] = ("Exact match", "good"), ["good"] = ("Good match", "good"), ["manual"] = ("Set by you", "good"),
        ["online"] = ("Online match – check", "warn"), ["noyear"] = ("No year in name – check", "warn"), ["review"] = ("Ambiguous – check", "warn"),
        ["fuzzy"] = ("Fuzzy match – check", "warn"), ["none"] = ("Not found on IMDb", "bad"),
    };

    readonly Dictionary<int, Border> fixHosts = new();

    void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MovieItem item) ShowDetail(item.M);
    }

    void OnCloseDetail(object sender, RoutedEventArgs e) => CloseDetailPane();

    void CloseDetailPane()
    {
        CommitNotes();
        DetailSplit.IsPaneOpen = false;
    }

    Style StyleOf(string key) => (Style)Root.Resources[key];

    static string Run(int minutes) => minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m";

    static string FileSize(long n) => n >= 1_000_000_000
        ? (n / 1e9).ToString(n >= 10_000_000_000 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + " GB"
        : Math.Max(1, (long)Math.Round(n / 1e6, MidpointRounding.AwayFromZero)) + " MB";

    /// <summary>A small label. <paramref name="tone"/>: good, warn, bad or fa; the colours come from the badge colours of the other windows.</summary>
    Border Chip(string text, bool accent = false, string? tone = null, string? tip = null)
    {
        var label = new TextBlock { Text = text, Style = StyleOf(accent ? "ChipTextHi" : "ChipText") };
        var box = new Border { Style = StyleOf(accent ? "ChipBoxHi" : "ChipBox"), Child = label };
        if (tone != null)
        {
            var (fg, bg) = U2.Badge(tone switch { "good" => "new", "warn" => "worse", "bad" => "unmatched", _ => "better" });
            label.Foreground = fg;
            box.Background = bg;
        }
        if (tip != null) ToolTipService.SetToolTip(box, tip);
        return box;
    }

    static ImageSource PosterImage(string path, string key, int width)
    {
        var img = new BitmapImage { DecodePixelWidth = width };
        if (MovieItem.NewPosters.Contains(key)) img.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        img.UriSource = new Uri(path);
        return img;
    }

    void ShowDetail(Movie m)
    {
        CommitNotes();
        current = m;
        fixHosts.Clear();
        bool matched = m.Tconst != null;
        FaEditor.Visibility = Visibility.Collapsed;
        PosterBox.Visibility = Visibility.Collapsed;

        DetailTitle.Text = m.Display;
        bool showOrig = !string.IsNullOrEmpty(m.Orig) && Movie.Fold(m.Orig) != Movie.Fold(m.Display);
        bool showPrim = m.Title.Length > 0 && Movie.Fold(m.Title) != Movie.Fold(m.Display) && (string.IsNullOrEmpty(m.Orig) || Movie.Fold(m.Title) != Movie.Fold(m.Orig));
        OrigRow.Visibility = U2.Vis(showOrig);
        OrigText.Text = m.Orig ?? "";
        OrigText.FlowDirection = WinKit.DirOf(m.Orig);
        PrimRow.Visibility = U2.Vis(showPrim);
        PrimText.Text = m.Title;
        FaRow.Visibility = U2.Vis(matched);
        FaText.Text = m.FaTitle ?? Loc.T("none");
        FaText.FontStyle = m.FaTitle == null ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
        DetailPoster.Source = m.HasPoster ? PosterImage(lib.PosterPath(m), m.K, 260) : null;
        DetailPlaceholder.Text = m.HasPoster ? "" : m.Year != null ? $"{m.Display}\n{m.Year}" : m.Display;
        DetailBackdrop.Source = Uri.TryCreate(m.Backdrop, UriKind.Absolute, out var backdrop) ? new BitmapImage { DecodePixelWidth = 440, UriSource = backdrop } : null;

        FillMeta(m);
        TaglineText.Text = m.Tagline ?? "";
        TaglineText.Visibility = U2.Vis(!string.IsNullOrEmpty(m.Tagline));
        bool hint = string.IsNullOrEmpty(m.Plot) && matched;
        PlotText.Text = hint ? Loc.T("Add a free TMDB key in Settings to get plots, countries and cast photos.") : m.Plot ?? "";
        PlotText.Opacity = hint ? 0.7 : 1;
        PlotText.Visibility = U2.Vis(hint || !string.IsNullOrEmpty(m.Plot));
        FillPeople(DirLabel, DirText, m.Dir.Select(p => (p, "")));

        FavToggle.IsChecked = m.Fav;
        WatchedToggle.IsChecked = m.Watched;
        UpdateWatchedDate(m);
        MyRating.Value = m.MyRating ?? -1;
        UpdateMyRatingLabel(m);

        LinksPanel.Children.Clear();
        if (matched)
            foreach (var b in new UIElement[] { ImdbLink, LetterboxdLink, TmdbLink, TrailerLink, PosterBtn }) LinksPanel.Children.Add(b);
        LinksPanel.Children.Add(RemoveAllBtn);
        ImdbLink.Content = "IMDb " + m.K;
        PosterBtnText.Text = Loc.T(m.HasPoster ? "Change poster" : "Add poster");

        var photos = new Dictionary<string, string?>();
        foreach (var c in m.TmdbCastMembers) photos[Movie.Fold(c.Name)] = c.Img;
        CastLabel.Visibility = CastList.Visibility = U2.Vis(m.Cast.Count > 0);
        CastList.ItemsSource = m.Cast.Select(p => new CastItem(p, photos.GetValueOrDefault(Movie.Fold(p.Name)), () => OpenPerson(p.Id, p.Name))).ToList();
        FillPeople(WriLabel, WriText, m.Wri.Select(p => (p, "")));
        FillPeople(CrewLabel, CrewText, m.Crew.GroupBy(c => c.Role ?? "").SelectMany(g => g.Select(p => (p, g.Key))));
        AkaLabel.Visibility = AkaText.Visibility = U2.Vis(m.Akas.Length > 0);
        AkaText.Text = string.Join(" · ", m.Akas);

        NotesBox.Text = m.Notes ?? "";
        FillFacts(m);
        FilesLabel.Text = string.Format(Loc.T("Your files ({0})"), m.Files.Count);
        FilesPanel.Children.Clear();
        foreach (var f in m.Files) FilesPanel.Children.Add(FileCard(m, f));
        DetailSplit.IsPaneOpen = true;
        DetailScroll.ChangeView(null, 0, null, true);
        if (!matched) OpenFix(m.Files[0].Id);
    }

    void FillMeta(Movie m)
    {
        DetailMeta.Children.Clear();
        void Add(UIElement e) => DetailMeta.Children.Add(e);
        TextBlock Plain(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
        if (m.Rating is double r)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            line.Children.Add(new FontIcon { Glyph = "\uE735", FontSize = 13, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xF5, 0x9E, 0x0B)), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(new TextBlock { Text = r.ToString("0.0", CultureInfo.InvariantCulture), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(new TextBlock { Text = "/10 IMDb" + (m.Votes > 0 ? $" · {U2.Votes(m.Votes)} {Loc.T("votes")}" : ""), FontSize = 12, Style = StyleOf("Muted"), VerticalAlignment = VerticalAlignment.Center });
            Add(line);
        }
        if (m.Rt is int rt)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            line.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, Fill = new SolidColorBrush(rt >= 60 ? Windows.UI.Color.FromArgb(255, 0xFA, 0x32, 0x0A) : Windows.UI.Color.FromArgb(255, 0x6B, 0xB5, 0x2B)) });
            line.Children.Add(new TextBlock { Text = rt + "%", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            ToolTipService.SetToolTip(line, $"Rotten Tomatoes: {rt}% {Loc.T(rt >= 60 ? "fresh" : "rotten")}");
            Add(line);
        }
        if (m.Mc is int mc) Add(Chip(mc.ToString(), tone: mc >= 61 ? "good" : mc >= 40 ? "warn" : "bad", tip: $"Metacritic Metascore: {mc}"));
        if (m.Year != null) Add(Plain(m.EndYear != null ? $"{m.Year}–{m.EndYear}" : m.Year.ToString()!));
        if (m.Runtime is > 0) Add(Plain(Run(m.Runtime.Value) + (m.IsSeries ? " / ep" : "")));
        if (m.Type != null && m.Type != "movie") Add(Chip(Loc.T(TypeLabel.GetValueOrDefault(m.Type, m.Type))));
        foreach (var g in m.Genres)
        {
            var b = new Button { Content = g, Padding = new Thickness(8, 2, 8, 3), FontSize = 12, MinHeight = 0 };
            ToolTipService.SetToolTip(b, string.Format(Loc.T("Show all {0} movies"), g));
            b.Click += (_, _) =>
            {
                F.Genre.Clear();
                F.Genre.Add(g);
                CloseDetailPane();
                BuildFacets();
                Apply();
            };
            Add(b);
        }
    }

    void FillFacts(Movie m)
    {
        DetailFacts.Children.Clear();
        DetailFacts.RowDefinitions.Clear();
        void Row(string label, FrameworkElement value)
        {
            int row = DetailFacts.RowDefinitions.Count;
            DetailFacts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = Loc.T(label), Style = StyleOf("Muted"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(name, row);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            DetailFacts.Children.Add(name);
            DetailFacts.Children.Add(value);
        }
        TextBlock Value(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
        if (m.Countries.Length > 0) Row("Country", Value(string.Join(", ", m.Countries)));
        if (m.Langs.Length > 0) Row("Language", Value(string.Join(", ", m.Langs)));
        if (!string.IsNullOrEmpty(m.Collection))
        {
            var link = new HyperlinkButton { Content = m.Collection, Padding = new Thickness(0), FlowDirection = FlowDirection.LeftToRight };
            string name = m.Collection;
            link.Click += (_, _) =>
            {
                SetSearchText(name);
                CloseDetailPane();
                Apply();
            };
            Row("Collection", link);
        }
        var conf = ConfLabel.GetValueOrDefault(m.Conf, ("", ""));
        if (conf.Item1.Length > 0)
        {
            var chip = Chip(Loc.T(conf.Item1), tone: conf.Item2);
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            Row("Match", chip);
        }
    }

    UIElement FileCard(Movie m, MovieFile f)
    {
        var sp = new StackPanel { Spacing = 6 };
        sp.Children.Add(new TextBlock { Text = f.Raw, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FlowDirection = FlowDirection.LeftToRight, FontFamily = new FontFamily("Consolas"), FontSize = 13 });
        var wrap = new WrapPanel { HorizontalSpacing = 4, VerticalSpacing = 4 };
        void Tag(string? text, bool accent = false, string? tone = null, string? tip = null)
        {
            if (!string.IsNullOrEmpty(text)) wrap.Children.Add(Chip(text, accent, tone, tip));
        }
        Tag(f.Res, accent: true);
        Tag(f.Src);
        Tag(f.Codec);
        if (f.B10) Tag("10-bit");
        if (f.Hdr) Tag("HDR", accent: true);
        Tag(f.Audio);
        if (f.Fa) Tag(Loc.T("Farsi dubbed"), tone: "fa");
        if (f.Dual) Tag(Loc.T("Dual audio"), tone: "fa");
        if (f.Sub) Tag(Loc.T("Softsub"));
        Tag(f.Ed, accent: true);
        Tag(f.Grp);
        // facts read from the real file with ffprobe
        if (f.Dur is > 0) Tag(Run((int)Math.Round(f.Dur.Value / 60.0, MidpointRounding.AwayFromZero)));
        if (f.W != null) Tag($"{f.W}×{f.H}");
        if (f.ALang.Length > 0) Tag(Loc.T("Audio") + ": " + string.Join(", ", f.ALang), tone: "fa", tip: Loc.T("Audio languages"));
        if (f.SLang.Length > 0) Tag(Loc.T("Subs") + ": " + string.Join(", ", f.SLang), tip: Loc.T("Subtitles"));
        if (f.Size is > 0) Tag(FileSize(f.Size.Value));
        if (wrap.Children.Count > 0) sp.Children.Add(wrap);
        foreach (var path in f.Paths)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new TextBlock { Text = path, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, Style = StyleOf("Muted"), FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(text, path);
            row.Children.Add(text);
            var show = new HyperlinkButton { Content = Loc.T("Show in folder"), Padding = new Thickness(0) };
            show.Click += async (_, _) => await Reveal(path);
            Grid.SetColumn(show, 1);
            row.Children.Add(show);
            sp.Children.Add(row);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var fix = new Button { Content = Loc.T(m.Tconst != null ? "Wrong movie? Fix match" : "Find on IMDb") };
        fix.Click += (_, _) => OpenFix(f.Id);
        buttons.Children.Add(fix);
        var remove = new Button { Content = Loc.T("Remove from list") };
        remove.Click += async (_, _) => await RemoveFiles(new List<int> { f.Id }, CloseDetailPane);
        buttons.Children.Add(remove);
        sp.Children.Add(buttons);
        var host = new Border();
        fixHosts[f.Id] = host;
        sp.Children.Add(host);
        return new Border { Style = StyleOf("FileBox"), Child = sp };
    }

    async Task Reveal(string path)
    {
        try { await Task.Run(() => lib.Reveal(path)); }
        catch (Exception x) when (x is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { Toast(Loc.T(x.Message), "err"); }
    }

    void OpenFix(int fileId)
    {
        if (!fixHosts.TryGetValue(fileId, out var host)) return;
        var panel = new FixPanel(lib, fileId, Dialogs.ConfirmNotOnImdb(Root.XamlRoot), tc =>
        {
            CloseDetailPane();
            ShowInfo(Loc.T(tc != null ? "Match saved" : "Marked as not on IMDb"));
        });
        host.Child = panel;
        panel.Start();
    }

    // ---------- people ----------
    void OpenPerson(int id, string name) => PersonWindow.Open(id, name, (pid, pname) => ShowPerson(pid, pname), tc =>
    {
        var mv = all.Find(x => x.Tconst == tc);
        if (mv != null) ShowDetail(mv);
    });

    /// <summary>"Show all in the library" in a profile: every other filter goes and only this person's movies stay.</summary>
    void ShowPerson(int id, string name)
    {
        F.Clear();
        F.Person = (id, name);
        SetSearchText("");
        SyncFilterControls();
        CloseDetailPane();
        BuildFacets();
        Apply();
    }

    /// <summary>Names in the movie pane: each opens the person's profile. The heading goes when there is nobody to show.</summary>
    void FillPeople(TextBlock heading, TextBlock tb, IEnumerable<(Person P, string Job)> people)
    {
        tb.Inlines.Clear();
        bool first = true;
        foreach (var (p, job) in people)
        {
            if (!first) tb.Inlines.Add(new Run { Text = ",  " });
            first = false;
            var link = new Hyperlink { UnderlineStyle = UnderlineStyle.None };
            link.Inlines.Add(new Run { Text = p.Name });
            ToolTipService.SetToolTip(link, string.Format(Loc.T("Open the profile of {0}"), p.Name));
            var who = p;
            link.Click += (_, _) => OpenPerson(who.Id, who.Name);
            tb.Inlines.Add(link);
            if (job.Length > 0) tb.Inlines.Add(new Run { Text = " · " + job, FontSize = 12 });
        }
        heading.Visibility = tb.Visibility = U2.Vis(!first);
    }

    // ---------- your marks ----------
    void SaveMark(Action<Movie> apply, Action<Movie> write)
    {
        if (current is not { } m) return;
        apply(m);
        SaveUserData(() => write(m));
        itemOf.GetValueOrDefault(m)?.Refresh();
        AfterUserChange();
    }

    void OnFavClick(object sender, RoutedEventArgs e)
    {
        bool on = FavToggle.IsChecked == true;
        SaveMark(m => m.Fav = on, m => lib.SetUser(new[] { m.K }, fav: on));
    }

    void OnWatchedClick(object sender, RoutedEventArgs e)
    {
        bool on = WatchedToggle.IsChecked == true;
        SaveMark(m => MarkWatched(m, on), m => lib.SetUser(new[] { m.K }, watched: on));
        if (current != null) UpdateWatchedDate(current);
    }

    void UpdateWatchedDate(Movie m)
    {
        WatchedDate.Text = m.Watched && m.WatchedAt != null ? Loc.T("on") + " " + m.WatchedAt[..Math.Min(10, m.WatchedAt.Length)] : "";
        WatchedDate.Visibility = U2.Vis(WatchedDate.Text.Length > 0);
    }

    void UpdateMyRatingLabel(Movie m) => MyRatingLabel.Text = m.MyRating != null ? m.MyRating + "/10" : Loc.T("My rating");

    void OnMyRatingChanged(RatingControl sender, object args)
    {
        if (current is not { } m) return;
        int v = (int)sender.Value;
        if (v < 1)
        {
            if (m.MyRating != null) SaveMark(x => x.MyRating = null, x => lib.SetUser(new[] { x.K }, clearRating: true));
        }
        else if (m.MyRating != v) SaveMark(x => x.MyRating = v, x => lib.SetUser(new[] { x.K }, rating: v));
        UpdateMyRatingLabel(m);
    }

    void OnNotesLostFocus(object sender, RoutedEventArgs e) => CommitNotes();

    // notes are saved when the box loses focus and whenever the pane moves on, so closing the pane cannot lose them
    void CommitNotes()
    {
        if (current is not { } m || (m.Notes ?? "") == NotesBox.Text) return;
        string text = NotesBox.Text;
        m.Notes = text;
        SaveUserData(() => lib.SetUser(new[] { m.K }, notes: text));
    }

    // ---------- links ----------
    void OnOpenImdb(object sender, RoutedEventArgs e)
    {
        if (current?.Tconst is int tc) U2.Launch(U2.ImdbTitleUrl(tc));
    }

    void OnOpenLetterboxd(object sender, RoutedEventArgs e)
    {
        if (current != null) U2.Launch($"https://letterboxd.com/imdb/{current.K}/");
    }

    void OnOpenTmdb(object sender, RoutedEventArgs e)
    {
        if (current != null) U2.Launch($"https://www.themoviedb.org/redirect?external_source=imdb_id&external_id={current.K}");
    }

    void OnOpenTrailer(object sender, RoutedEventArgs e)
    {
        if (current != null) U2.Launch("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString($"{current.Display} {current.Year} trailer"));
    }

    async void OnRemoveMovie(object sender, RoutedEventArgs e)
    {
        if (current != null) await RemoveFiles(current.Files.Select(f => f.Id).ToList(), CloseDetailPane);
    }

    // ---------- Persian title ----------
    void OnFaEdit(object sender, RoutedEventArgs e)
    {
        FaInput.Text = current?.FaTitle ?? "";
        FaEditor.Visibility = Visibility.Visible;
        FaInput.Focus(FocusState.Programmatic);
    }

    void OnFaCancel(object sender, RoutedEventArgs e) => FaEditor.Visibility = Visibility.Collapsed;

    async void OnFaSave(object sender, RoutedEventArgs e) => await SendFaTitle(FaInput.Text);

    async void OnFaReset(object sender, RoutedEventArgs e) => await SendFaTitle(null);

    // the library reloads after this and the pane opens again on the new data
    async Task SendFaTitle(string? title)
    {
        if (current is not { Tconst: int tc } m) return;
        reopenKey = m.K;
        await Task.Run(() => lib.SetFaTitle(tc, title));
        ShowInfo(Loc.T("Persian title saved"));
    }

    // ---------- poster by hand ----------
    const string PosterHint = "JPEG, PNG, WebP or GIF, up to 15 MB. A poster you add is kept: updates never replace it.";

    void PosterMessage(string text, bool bad = false)
    {
        PosterMsg.Text = text;
        if (bad) PosterMsg.Foreground = U2.Badge("unmatched").Fg;
        else PosterMsg.ClearValue(TextBlock.ForegroundProperty);
    }

    void OnPosterToggle(object sender, RoutedEventArgs e)
    {
        bool open = PosterBox.Visibility != Visibility.Visible;
        PosterBox.Visibility = U2.Vis(open);
        if (!open) return;
        PosterUrl.Text = "";
        PosterMessage(Loc.T(PosterHint));
        PosterClearBtn.Visibility = U2.Vis(current?.HasPoster == true);
        PosterUrl.Focus(FocusState.Programmatic);
    }

    async void OnPosterKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) await PosterFromBox();
    }

    async void OnPosterUse(object sender, RoutedEventArgs e) => await PosterFromBox();

    async Task PosterFromBox()
    {
        string v = PosterUrl.Text.Trim();
        if (v.Length == 0)
        {
            PosterMessage(Loc.T("Paste an image address or a file path first."), bad: true);
            return;
        }
        await PosterSave(tc => lib.SetPosterFrom(tc, v), "Poster saved");
    }

    async void OnPosterPick(object sender, RoutedEventArgs e)
    {
        var p = new FileOpenPicker();
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" }) p.FileTypeFilter.Add(ext);
        U2.InitPicker(p, this);
        var file = await p.PickSingleFileAsync();
        if (file == null) return;
        string path = file.Path;
        await PosterSave(tc => lib.SetPosterFrom(tc, path), "Poster saved");
    }

    async void OnPosterClear(object sender, RoutedEventArgs e) => await PosterSave(lib.ClearPoster, "Poster removed");

    async Task PosterSave(Action<int> save, string done)
    {
        if (current is not { Tconst: int tc } m) return;
        PosterMessage(Loc.T("Saving…"));
        MovieItem.NewPosters.Add(m.K);
        reopenKey = m.K;
        try
        {
            await Task.Run(() => save(tc));
            ShowInfo(Loc.T(done));
        }
        catch (Exception x) when (x is ArgumentException or IOException or UnauthorizedAccessException)
        {
            reopenKey = null;
            PosterMessage(Loc.T(x.Message), bad: true);
            Toast(Loc.T(x.Message), "err");
        }
    }
}
