using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using MovieShelf.Core;

namespace MovieShelf.App;

/// <summary>"Fix match": finds the IMDb title of one file by name, or takes a pasted link or tt-number. It sits in a file's box in the movie pane, and in the Fix match dialog.</summary>
public sealed class FixPanel : StackPanel
{
    readonly Library lib;
    readonly int fileId;
    readonly Func<Task<bool>>? confirm;
    readonly Action<int?> saved;
    readonly TextBox input;
    readonly StackPanel cands = new() { Spacing = 8 };
    int run;

    /// <param name="confirm">Asks before "not on IMDb". Null: the panel asks itself, because a dialog cannot open a second one.</param>
    /// <param name="saved">Called with the chosen id, or null for "not on IMDb", once the choice is stored.</param>
    public FixPanel(Library lib, int fileId, Func<Task<bool>>? confirm, Action<int?> saved, string? firstQuery = null)
    {
        this.lib = lib;
        this.fileId = fileId;
        this.confirm = confirm;
        this.saved = saved;
        Spacing = 8;
        Margin = new Thickness(0, 8, 0, 0);
        input = new TextBox { Text = firstQuery ?? "", PlaceholderText = Loc.T("Title and year, or IMDb link / tt-number") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, Loc.T("Search IMDb"));
        var go = new Button { Content = Loc.T("Search") };
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(input);
        Grid.SetColumn(go, 1);
        row.Children.Add(go);
        Children.Add(row);
        Children.Add(cands);
        go.Click += async (_, _) => await Search();
        input.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) await Search();
        };
        Loaded += (_, _) => input.Focus(FocusState.Programmatic);
    }

    /// <summary>Runs the first search; with nothing typed it searches by the file's own name.</summary>
    public void Start() => _ = Search();

    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };

    Button UseButton(int? tconst)
    {
        var b = new Button { Content = Loc.T("Use this") };
        b.Click += async (_, _) => await Use(b, tconst);
        return b;
    }

    async Task Search()
    {
        int id = ++run;
        string q = input.Text.Trim();
        cands.Children.Clear();
        var pasted = Regex.Match(q, @"tt\d+");
        if (pasted.Success)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = pasted.Value, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left });
            var use = UseButton(int.TryParse(pasted.Value[2..], out int tc) ? tc : null);
            Grid.SetColumn(use, 1);
            row.Children.Add(use);
            cands.Children.Add(row);
            return;
        }
        cands.Children.Add(Note(Loc.T("Searching IMDb…")));
        List<Candidate2> found;
        try { found = await Task.Run(() => lib.Lookup(fileId, q.Length == 0 ? null : q)); }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
        {
            if (id == run)
            {
                cands.Children.Clear();
                cands.Children.Add(Note(e.Message));
            }
            return;
        }
        if (id != run) return;
        cands.Children.Clear();
        if (found.Count == 0) cands.Children.Add(Note(Loc.T("Nothing found. Try another spelling or paste the IMDb link.")));
        foreach (var c in found) cands.Children.Add(Row(c));
        var none = new Button { Content = Loc.T("Mark as not on IMDb") };
        none.Click += async (_, _) => await Use(none, null);
        cands.Children.Add(none);
    }

    FrameworkElement Row(Candidate2 c)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var thumb = new Border { Width = 44, Height = 64, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Top, Background = U2.Res("CardBackgroundFillColorSecondaryBrush") };
        // the small version of IMDb's picture; it arrives when it arrives
        if (Uri.TryCreate(c.Image?.Replace("._V1_.", "._V1_QL50_UX80_."), UriKind.Absolute, out var img))
            thumb.Child = new Image { Source = new BitmapImage { DecodePixelWidth = 80, UriSource = img }, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
        grid.Children.Add(thumb);
        string tt = $"tt{c.Tconst:0000000}";
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = c.Title + (c.Year != null ? $" {c.Year}" : ""), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.DetectFromContent });
        text.Children.Add(new TextBlock
        {
            Text = string.Join(" · ", new[]
            {
                c.Type != null ? Loc.T(MainWindow.TypeLabel.GetValueOrDefault(c.Type, c.Type)) : null, c.Orig != null && c.Orig != c.Title ? c.Orig : null,
                c.Rating != null ? $"IMDb {c.Rating:0.0}" : null, c.Votes > 0 ? $"{U2.Votes(c.Votes)} {Loc.T("votes")}" : null, c.Stars, tt, c.How,
            }.Where(x => !string.IsNullOrEmpty(x))),
            FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.LeftToRight,
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
        var view = new Button { Content = Loc.T("View") };
        view.Click += (_, _) => U2.Launch(U2.ImdbTitleUrl(c.Tconst));
        buttons.Children.Add(view);
        buttons.Children.Add(UseButton(c.Tconst));
        text.Children.Add(buttons);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    Task<bool> AskHere()
    {
        var answer = new TaskCompletionSource<bool>();
        var keep = cands.Children.ToList();
        cands.Children.Clear();
        cands.Children.Add(new TextBlock { Text = Loc.T("Mark as not on IMDb?"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        cands.Children.Add(new TextBlock { Text = Loc.T("This file keeps no IMDb data (no poster, cast or ratings). You can fix it again later."), TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var no = new Button { Content = Loc.T("Cancel") };
        var yes = new Button { Content = Loc.T("Mark as not on IMDb"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        void Done(bool result)
        {
            cands.Children.Clear();
            foreach (var c in keep) cands.Children.Add(c);
            answer.TrySetResult(result);
        }
        no.Click += (_, _) => Done(false);
        yes.Click += (_, _) => Done(true);
        buttons.Children.Add(no);
        buttons.Children.Add(yes);
        cands.Children.Add(buttons);
        return answer.Task;
    }

    async Task Use(Button b, int? tconst)
    {
        if (tconst == null && !await (confirm ?? AskHere)()) return;
        object was = b.Content;
        b.IsEnabled = false;
        b.Content = Loc.T("Saving…");
        try
        {
            await Task.Run(() =>
            {
                if (tconst != null) lib.SetMatchAndRefresh(fileId, tconst);
                else lib.SetMatch(fileId, null);
            });
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
        {
            b.IsEnabled = true;
            b.Content = was;
            cands.Children.Insert(0, Note(e.Message));
            return;
        }
        saved(tconst);
    }
}
