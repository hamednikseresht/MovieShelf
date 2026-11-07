using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieShelf.Core;

namespace MovieShelf.App;

/// <summary>One bar of a chart.</summary>
public sealed class BarVm
{
    public string Kind { get; init; } = "";      // decade | genre | person | res | country ("" = not clickable)
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public int Count { get; init; }
    public double Fraction { get; init; }
    public string CountText => Count.ToString("N0");
    public string Tip => Label + ": " + CountText;
    public GridLength Fill => new(Math.Max(Fraction, 0), GridUnitType.Star);
    public GridLength Rest => new(Math.Max(1 - Fraction, 0), GridUnitType.Star);
}

/// <summary>Collection statistics: the totals and bar charts of decades, genres, people, quality, ratings and countries.
/// A click on a bar calls <c>onPick(kind, key, label)</c> and closes the window.</summary>
public sealed partial class StatsWindow : Window
{
    static StatsWindow? instance;

    /// <summary>Shows the statistics of <paramref name="movies"/>. <paramref name="onPick"/> gets the kind (decade, genre, person, res, country),
    /// the key (decade year, genre name, person id, quality, country) and the label shown on the bar.</summary>
    public static void Open(IReadOnlyList<Movie> movies, Action<string, string, string> onPick)
    {
        if (instance == null) instance = new StatsWindow();
        instance.movies = movies;
        instance.onPick = onPick;
        _ = instance.RecomputeAsync();
        instance.Activate();
    }

    internal static StatsWindow? Instance => instance;

    IReadOnlyList<Movie> movies = Array.Empty<Movie>();
    Action<string, string, string> onPick = (_, _, _) => { };
    StatsKind kind = StatsKind.All;
    CollectionStats? stats;

    StatsWindow()
    {
        InitializeComponent();
        WinKit.Setup(this, AppTitleBar, "Statistics", 1120, 880, Relocalise, "collection");
        KindBar.Items.Add(new SelectorBarItem { Text = Loc.T("All"), Tag = StatsKind.All });
        KindBar.Items.Add(new SelectorBarItem { Text = Loc.T("Movies"), Tag = StatsKind.Movies });
        KindBar.Items.Add(new SelectorBarItem { Text = Loc.T("Series"), Tag = StatsKind.Series });
        KindBar.SelectedItem = KindBar.Items[0];
        WinKit.Direction(Root);
        Loc.Apply(Root);
        AppTitleBar.Title = Loc.T("Statistics");
        Closed += (_, _) => instance = null;
    }

    void Relocalise()
    {
        WinKit.Direction(Root);
        Loc.Apply(Root);
        Info.IsOpen = false;
        if (stats != null) Build();
    }

    int generation;

    async Task RecomputeAsync()
    {
        int gen = ++generation;
        Ring.IsActive = true;
        var list = movies;
        var k = kind;
        try
        {
            var s = await Task.Run(() => CollectionStats.Compute(list, k));
            if (gen != generation) return;
            stats = s;
            Build();
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            WinKit.Info(Info, e.Message, InfoBarSeverity.Error);
        }
        if (gen == generation) Ring.IsActive = false;
    }

    void OnKindChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem i || (StatsKind)i.Tag == kind) return;
        kind = (StatsKind)i.Tag;
        _ = RecomputeAsync();
    }

    // ---------- drawing ----------
    const string Ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'";

    // Built from markup so the brushes are ThemeResource and follow the theme of this window.
    static TextBlock Secondary(string text, double size = 12)
    {
        var tb = (TextBlock)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<TextBlock {Ns} FontSize='{size}' Foreground='{{ThemeResource TextFillColorSecondaryBrush}}'/>");
        tb.Text = text;
        return tb;
    }

    Border Tile(string value, string caption)
    {
        var sp = new StackPanel { Spacing = 2 };
        sp.Children.Add(new TextBlock { Text = value, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left });
        sp.Children.Add(Secondary(Loc.T(caption)));
        return Card(sp, 148, 12);
    }

    Border Card(UIElement child, double minWidth, double padding)
    {
        var b = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            $"<Border {Ns} CornerRadius='8' BorderThickness='1' Background='{{ThemeResource CardBackgroundFillColorDefaultBrush}}' BorderBrush='{{ThemeResource CardStrokeColorDefaultBrush}}'/>");
        b.Child = child;
        b.MinWidth = minWidth;
        b.Padding = new Thickness(padding + 4, padding, padding + 4, padding);
        return b;
    }

    readonly List<Border> cards = new();

    void Build()
    {
        var s = stats!;
        TilePanel.Children.Clear();
        TilePanel.Children.Add(Tile(s.Movies.ToString("N0"), "movies"));
        TilePanel.Children.Add(Tile(s.Files.ToString("N0"), "files"));
        TilePanel.Children.Add(Tile(s.Watched.ToString("N0"), "watched"));
        TilePanel.Children.Add(Tile(s.NeedReview.ToString("N0"), "need review"));
        TilePanel.Children.Add(Tile(s.AvgRating.ToString("0.00"), "average IMDb rating"));
        TilePanel.Children.Add(Tile(string.Format(Loc.T("{0} h"), s.Hours.ToString("N0")), "total runtime"));
        ((TextBlock)((StackPanel)((Border)TilePanel.Children[5]).Child).Children[1]).Text = string.Format(Loc.T("total runtime ({0} days)"), s.Days.ToString("N0"));

        CardPanel.Children.Clear();
        cards.Clear();
        AddChart("By decade", s.Decades, "decade", k: b => b.Key!, label: b => b.Label);
        AddChart("Genres", s.Genres, "genre", k: b => b.Label, label: b => b.Label);
        AddChart("Top directors", s.Directors, "person", k: b => b.Key!, label: b => b.Label);
        AddChart("Top actors", s.Actors, "person", k: b => b.Key!, label: b => b.Label);
        AddChart("Quality", s.Quality, "res", k: b => b.Label, label: b => b.Label);
        AddChart("IMDb rating", s.Ratings, "", k: b => b.Label, label: b => b.Label);
        if (s.Countries.Count > 0) AddChart("Countries", s.Countries, "country", k: b => b.Label, label: b => b.Label);
        Layout();
    }

    void AddChart(string title, List<StatBar> bars, string pickKind, Func<StatBar, string> k, Func<StatBar, string> label)
    {
        int max = Math.Max(bars.Count == 0 ? 1 : bars.Max(b => b.Count), 1);
        var rows = bars.Select(b => new BarVm { Kind = pickKind, Key = k(b), Label = label(b), Count = b.Count, Fraction = (double)b.Count / max }).ToList();
        var list = new ItemsControl
        {
            ItemsSource = rows,
            ItemTemplate = (DataTemplate)Root.Resources[pickKind.Length > 0 ? "BarClickTemplate" : "BarPlainTemplate"],
        };
        var sp = new StackPanel { Spacing = 8 };
        sp.Children.Add(new TextBlock { Text = Loc.T(title), FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        sp.Children.Add(rows.Count == 0 ? Secondary(Loc.T("Nothing to show."), 14) : list);
        var card = Card(sp, 0, 14);
        cards.Add(card);
        CardPanel.Children.Add(card);
    }

    /// <summary>One column per ~430 px so the charts fill the window and stack on a narrow one.</summary>
    void Layout()
    {
        double w = Scroll.ActualWidth - 32;
        if (w <= 0 || cards.Count == 0) return;
        int cols = Math.Max(1, (int)((w + 16) / 440));
        double each = Math.Floor((w - 16 * (cols - 1)) / cols);
        foreach (var c in cards) c.Width = each;
    }

    void OnScrollSize(object sender, SizeChangedEventArgs e) => Layout();

    void OnBar(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not BarVm b || b.Kind.Length == 0) return;
        try
        {
            onPick(b.Kind, b.Key, b.Label);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            WinKit.Info(Info, ex.Message, InfoBarSeverity.Error);
            return;
        }
        Close();
    }
}
