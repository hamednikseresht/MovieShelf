using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MovieShelf.Core;
using WinRT.Interop;

namespace MovieShelf.App;

/// <summary>The dialogs, built in code: Settings, Fix match, Export, Watch next and the questions before something risky.</summary>
public static partial class Dialogs
{
    static ContentDialog NewDialog(XamlRoot root, string title) => new()
    {
        XamlRoot = root, Title = Loc.T(title), DefaultButton = ContentDialogButton.Primary,
        FlowDirection = Loc.IsFa ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        RequestedTheme = (root.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,   // a popup does not inherit the window's theme
    };

    static void Pick(object picker, Window w) => InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(w));

    /// <summary>A question with Cancel and one other button. Risky actions (<paramref name="danger"/>) get a red button and start on Cancel.</summary>
    public static async Task<bool> Confirm(XamlRoot root, string title, string text, string ok, bool danger = false)
    {
        var dlg = NewDialog(root, title);
        dlg.Content = new TextBlock { Text = Loc.T(text), TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        dlg.PrimaryButtonText = Loc.T(ok);
        dlg.CloseButtonText = Loc.T("Cancel");
        if (danger)
        {
            dlg.DefaultButton = ContentDialogButton.Close;
            var red = new Style(typeof(Button)) { BasedOn = (Style)Application.Current.Resources["DefaultButtonStyle"] };
            red.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xC4, 0x2B, 0x1C))));
            red.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Microsoft.UI.Colors.White)));
            dlg.PrimaryButtonStyle = red;
        }
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }

    public static Func<Task<bool>> ConfirmNotOnImdb(XamlRoot root) => () =>
        Confirm(root, "Mark as not on IMDb?", "This file keeps no IMDb data (no poster, cast or ratings). You can fix it again later.", "Mark as not on IMDb");

    // ---------- fix match ----------
    /// <summary>Lets the user pick the IMDb title for a file. Returns the chosen IMDb id, 0 for "not on IMDb", or null when cancelled.</summary>
    public static async Task<int?> FixMatch(XamlRoot root, Library lib, int fileId, string raw, string? firstQuery = null)
    {
        var dlg = NewDialog(root, "Fix match");
        dlg.CloseButtonText = Loc.T("Cancel");
        dlg.DefaultButton = ContentDialogButton.None;
        int? result = null;
        var panel = new FixPanel(lib, fileId, null, tc =>
        {
            result = tc ?? 0;
            dlg.Hide();
        }, firstQuery);
        var body = new StackPanel { Spacing = 4, MinWidth = 540 };
        body.Children.Add(new TextBlock { Text = raw, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FlowDirection = FlowDirection.LeftToRight });
        body.Children.Add(panel);
        dlg.Content = new ScrollViewer { Content = body, MaxHeight = 560 };
        dlg.Opened += (_, _) => panel.Start();
        await dlg.ShowAsync();
        return result;
    }

    // ---------- watch next ----------
    // the choices stay while the app runs, so opening it again carries on where it was
    static string wnPool = "view", wnMode = "rated";
    static int wnMaxRun;
    static double wnMinRating;
    static bool wnUnseen = true;

    public static async Task WatchNext(MainWindow main, Func<List<Movie>> all, Func<List<Movie>> view, Action<Movie> open, Action<Movie> markWatched)
    {
        var dlg = NewDialog(main.Content.XamlRoot, "Watch next");
        dlg.CloseButtonText = Loc.T("Close");
        dlg.DefaultButton = ContentDialogButton.Close;
        dlg.Resources["ContentDialogMaxWidth"] = 760d;   // room for three posters and more
        var rnd = new Random();
        var chosen = new List<Movie>();

        ComboBox Combo(string header, object current, params (string Label, object Tag)[] items)
        {
            var c = new ComboBox { Header = Loc.T(header), MinWidth = 190 };
            foreach (var (l, t) in items) c.Items.Add(new ComboBoxItem { Content = Loc.T(l), Tag = t });
            c.SelectedIndex = Math.Max(0, Array.FindIndex(items, i => Equals(i.Tag, current)));
            return c;
        }

        var pool = Combo("Choose from", wnPool, (string.Format(Loc.T("Current results ({0})"), view().Count.ToString("N0")), "view"), (string.Format(Loc.T("Whole library ({0})"), all().Count.ToString("N0")), "all"));
        var mode = Combo("Kind of pick", wnMode, ("Best rated", "rated"), ("Surprise me", "random"), ("Hidden gems", "gems"));
        var length = Combo("Length", wnMaxRun, ("Any", 0), ("Up to 100 min", 100), ("Up to 2 h", 120), ("Up to 2.5 h", 150));
        var minRating = Combo("IMDb rating", wnMinRating, ("Any", 0.0), ("6 and up", 6.0), ("7 and up", 7.0), ("8 and up", 8.0));
        var unseen = new CheckBox { Content = Loc.T("Only movies I have not watched"), IsChecked = wnUnseen };
        var hint = new TextBlock { Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        var picks = new WrapPanel { HorizontalSpacing = 12, VerticalSpacing = 12 };
        var again = new Button { Content = Loc.T("Pick again") };
        var history = new StackPanel { Spacing = 2 };

        List<Movie> Pool() => (wnPool == "all" ? all() : view()).Where(m => (!wnUnseen || !m.Watched) && (wnMaxRun == 0 || (m.Runtime != null && m.Runtime <= wnMaxRun)) && (wnMinRating == 0 || m.Rating >= wnMinRating)).ToList();

        void Pick()
        {
            var list = Pool();
            var cand = wnMode switch
            {
                "rated" => list.OrderByDescending(m => m.Rating ?? 0).ThenByDescending(m => m.Votes).Take(40).ToList(),
                "gems" => list.Where(m => m.Rating >= 7.2 && m.Votes < 60000).ToList(),
                _ => list,
            };
            chosen = cand.OrderBy(_ => rnd.Next()).Take(6).ToList();
        }

        void Show()
        {
            hint.Text = $"{Pool().Count:N0} {Loc.T("movies match.")}" + (wnMode == "gems" ? " " + Loc.T("Hidden gems: rated 7.2 or more with fewer than 60,000 votes.") : "");
            picks.Children.Clear();
            if (chosen.Count == 0) picks.Children.Add(new TextBlock { Text = Loc.T("Nothing matches. Loosen a setting above."), Opacity = 0.75 });
            foreach (var m in chosen)
            {
                var img = new Image { Width = 130, Height = 195, Stretch = Stretch.UniformToFill };
                if (m.HasPoster) img.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = 260, UriSource = new Uri(App.Lib.PosterPath(m)) };
                var open1 = new Button { Content = new Border { Width = 130, Height = 195, CornerRadius = new CornerRadius(6), Child = img }, Padding = new Thickness(0), Background = null };
                ToolTipService.SetToolTip(open1, string.Format(Loc.T("Open {0}"), m.Display));
                open1.Click += (_, _) => { dlg.Hide(); open(m); };
                var sp = new StackPanel { Width = 130, Spacing = 2 };
                sp.Children.Add(open1);
                sp.Children.Add(new TextBlock { Text = m.Display, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = FlowDirection.LeftToRight });
                sp.Children.Add(new TextBlock { Text = string.Join(" · ", new[] { m.Year?.ToString() ?? "", m.Runtime is int r && r > 0 ? (r >= 60 ? $"{r / 60}h {r % 60}m" : $"{r}m") : "", m.Rating != null ? $"IMDb {m.Rating:0.0}" : "" }.Where(x => x.Length > 0)), FontSize = 12, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = FlowDirection.LeftToRight });
                sp.Children.Add(new TextBlock { Text = string.Join(", ", m.Genres.Take(3)), FontSize = 12, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = FlowDirection.LeftToRight });
                var seen = new Button { Content = Loc.T("Watched") };
                seen.Click += (_, _) =>
                {
                    markWatched(m);
                    chosen.Remove(m);
                    // the watched movie makes room for another one
                    if (wnUnseen)
                    {
                        var more = Pool().Where(x => !chosen.Contains(x)).ToList();
                        if (more.Count > 0) chosen.Add(more[rnd.Next(more.Count)]);
                    }
                    Show();
                };
                sp.Children.Add(seen);
                picks.Children.Add(sp);
            }
            history.Children.Clear();
            var hist = all().Where(m => m.Watched && m.WatchedAt != null).OrderByDescending(m => m.WatchedAt, StringComparer.Ordinal).Take(8).ToList();
            if (hist.Count > 0) history.Children.Add(new TextBlock { Text = Loc.T("Recently watched"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            foreach (var m in hist)
            {
                var b = new HyperlinkButton { Content = $"{m.Display}" + (m.Year != null ? $" ({m.Year})" : "") + "   " + m.WatchedAt![..Math.Min(10, m.WatchedAt.Length)], Padding = new Thickness(0) };
                b.Click += (_, _) => { dlg.Hide(); open(m); };
                history.Children.Add(b);
            }
        }

        void Choose()
        {
            wnPool = (string)((ComboBoxItem)pool.SelectedItem).Tag;
            wnMode = (string)((ComboBoxItem)mode.SelectedItem).Tag;
            wnMaxRun = (int)((ComboBoxItem)length.SelectedItem).Tag;
            wnMinRating = (double)((ComboBoxItem)minRating.SelectedItem).Tag;
            wnUnseen = unseen.IsChecked == true;
            Pick();
            Show();
        }

        foreach (var c in new ComboBox[] { pool, mode, length, minRating }) c.SelectionChanged += (_, _) => Choose();
        unseen.Click += (_, _) => Choose();
        again.Click += (_, _) => Choose();
        var help = new HyperlinkButton { Content = "?", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Right };
        ToolTipService.SetToolTip(help, Loc.T("Help"));
        help.Click += (_, _) => HelpWindow.Open("collection");
        var controls = new WrapPanel { HorizontalSpacing = 12, VerticalSpacing = 8 };
        foreach (var c in new UIElement[] { pool, mode, length, minRating }) controls.Children.Add(c);
        var panel = new StackPanel { Spacing = 10, MinWidth = 680 };
        panel.Children.Add(help);
        panel.Children.Add(controls);
        panel.Children.Add(unseen);
        panel.Children.Add(hint);
        panel.Children.Add(picks);
        panel.Children.Add(again);
        panel.Children.Add(history);
        dlg.Content = new ScrollViewer { Content = panel, MaxHeight = 620 };
        Pick();
        Show();
        await dlg.ShowAsync();
    }
}
