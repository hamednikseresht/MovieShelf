using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MovieShelf.Core;
using Windows.System;

namespace MovieShelf.App;

// The search box: tips and recent searches, the keyboard shortcuts, and the row of active filters above the results.
public sealed partial class MainWindow
{
    static readonly (string Query, string Text)[] SearchExamples =
    {
        ("year:1990-1999", "Released in the 90s"), ("rating:>8", "IMDb rating above 8"), ("genre:horror", "One genre"),
        ("dir:nolan", "A director"), ("actor:\"tom hanks\"", "A cast member"), ("res:1080p", "A quality"),
        ("rt:>=90 mc:>=75", "Critics agree"), ("is:unwatched", "Not watched yet"), ("-zombie", "Leave a word out"), ("\"love story\"", "Exact phrase"),
    };

    string searchAtFocus = "";
    bool tabbing;

    /// <summary>Puts text in the box; only what the user types goes through the delay.</summary>
    void SetSearchText(string text)
    {
        searchTimer.Stop();
        F.Q = text;
        SearchBox.Text = text;
    }

    void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        ShowSearchPanel(SearchBox.Text.Length == 0);
        searchTimer.Stop();
        searchTimer.Start();
    }

    void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        searchTimer.Stop();
        F.Q = SearchBox.Text;
        RememberSearch(F.Q);
        ShowSearchPanel(false);
        Apply();
    }

    void OnSearchFocus(object sender, RoutedEventArgs e)
    {
        searchAtFocus = SearchBox.Text;
        // focus also comes back here when a dialog closes, which is not the user asking for the tips
        if (tabbing && SearchBox.Text.Length == 0) ShowSearchPanel(true);
    }

    void OnSearchPressed(object sender, PointerRoutedEventArgs e)
    {
        if (SearchBox.Text.Length == 0) ShowSearchPanel(true);
    }

    void OnSearchBlur(object sender, RoutedEventArgs e)
    {
        if (SearchBox.Text != searchAtFocus) RememberSearch(SearchBox.Text);
    }

    void OnSearchHelp(object sender, RoutedEventArgs e)
    {
        bool open = !SearchPanelOpen;
        ShowSearchPanel(open);
        if (open) SearchBox.Focus(FocusState.Programmatic);
    }

    void RememberSearch(string query)
    {
        string q = query.Trim();
        if (q.Length < 2) return;
        recentSearches.Remove(q);
        recentSearches.Insert(0, q);
        if (recentSearches.Count > 6) recentSearches.RemoveRange(6, recentSearches.Count - 6);
        SavePrefsSoon();
    }

    bool SearchPanelOpen => QPanel.Visibility == Visibility.Visible;

    void ShowSearchPanel(bool open)
    {
        if (!open)
        {
            QPanel.Visibility = Visibility.Collapsed;
            return;
        }
        FillSearchPanel();
        var at = SearchHost.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0, SearchHost.ActualHeight));
        QPanel.Margin = new Thickness(at.X, Math.Max(0, at.Y + 4 - Root.RowDefinitions[0].ActualHeight), 0, 0);
        QPanel.Width = Math.Max(460, SearchHost.ActualWidth);
        QPanel.Visibility = Visibility.Visible;
    }

    void FillSearchPanel()
    {
        QPanelBody.Children.Clear();
        TextBlock Head(string text) => new() { Text = Loc.T(text), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Style = (Style)Root.Resources["Muted"], Margin = new Thickness(0, 4, 0, 2) };
        // the buttons leave the focus in the search box
        Button Item(object content, Action click)
        {
            var b = new Button
            {
                Content = content, Background = null, BorderThickness = new Thickness(0), Padding = new Thickness(6, 4, 6, 4), AllowFocusOnInteraction = false,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            b.Click += (_, _) =>
            {
                click();
                ShowSearchPanel(false);
                SearchBox.Focus(FocusState.Programmatic);
            };
            return b;
        }
        if (recentSearches.Count > 0)
        {
            QPanelBody.Children.Add(Head("Recent searches"));
            foreach (var r in recentSearches)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                line.Children.Add(new FontIcon { Glyph = "\uE823", FontSize = 13 });
                line.Children.Add(new TextBlock { Text = r, TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = WinKit.DirOf(r) });
                QPanelBody.Children.Add(Item(line, () =>
                {
                    SetSearchText(r);
                    Apply();
                }));
            }
        }
        QPanelBody.Children.Add(Head("Narrow a search (click to add)"));
        var grid = new Grid { ColumnSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < SearchExamples.Length; i++)
        {
            var (query, text) = SearchExamples[i];
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var line = new StackPanel();
            line.Children.Add(new TextBlock { Text = query, FontFamily = new FontFamily("Consolas"), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left });
            line.Children.Add(new TextBlock { Text = Loc.T(text), FontSize = 12, Style = (Style)Root.Resources["Muted"] });
            var b = Item(line, () =>
            {
                SetSearchText((F.Q.Trim().Length > 0 ? F.Q.Trim() + " " : "") + query);
                Apply();
            });
            Grid.SetRow(b, i / 2);
            Grid.SetColumn(b, i % 2);
            grid.Children.Add(b);
        }
        QPanelBody.Children.Add(grid);
        QPanelBody.Children.Add(new TextBlock
        {
            Text = Loc.T("All words must match. Numbers take >8, <=1980 or 1990-1999. More fields: writer: country: lang: type: src: codec: audio: sub: file: votes: runtime: has:poster is:fav"),
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0), Style = (Style)Root.Resources["Muted"],
        });
    }

    // ---------- keyboard ----------
    void AddShortcuts()
    {
        // before the controls see it: the search box keeps Esc for itself. Dialogs and open menus are not under Root and deal with it first.
        Root.PreviewKeyDown += (_, a) =>
        {
            tabbing = a.Key == VirtualKey.Tab;
            if (a.Key == VirtualKey.Escape && !DialogOpen) a.Handled = EscapeChain();
        };
        SearchBox.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnSearchPressed), true);
        Root.CharacterReceived += (_, a) =>
        {
            if (a.Character != '/' || DialogOpen || InTextBox) return;
            a.Handled = true;
            FocusSearch();
        };
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnRootPressed), true);
    }

    bool DialogOpen => VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Count > 0;

    bool InTextBox => FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox or PasswordBox or RichEditBox or AutoSuggestBox or NumberBox or ComboBox;

    void FocusSearch()
    {
        SearchBox.Focus(FocusState.Programmatic);
        if (SearchBox.Text.Length == 0) ShowSearchPanel(true);
    }

    /// <summary>Esc closes the nearest thing: the search tips, the movie pane, selection mode, then the search text. False when there was nothing to close.</summary>
    bool EscapeChain()
    {
        if (SearchPanelOpen) ShowSearchPanel(false);
        else if (DetailSplit.IsPaneOpen) CloseDetailPane();
        else if (selecting) SetSelecting(false);
        else if (SearchBox.Text.Length > 0 || F.Q.Length > 0)
        {
            SetSearchText("");
            Apply();
        }
        else return false;
        return true;
    }

    void OnRootPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!SearchPanelOpen) return;
        for (var d = e.OriginalSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
            if (d == QPanel || d == SearchHost) return;
        ShowSearchPanel(false);
    }

    // ---------- active filters ----------
    static readonly Dictionary<string, string> SectionOf = new()
    {
        ["status"] = "library", ["genre"] = "genre", ["decade"] = "year", ["runtime"] = "runtime", ["res"] = "quality", ["src"] = "quality", ["tech"] = "quality", ["type"] = "type",
    };

    List<FilterOption> OptionsOf(string key) => key switch
    {
        "status" => status, "genre" => genre, "decade" => decade, "runtime" => runtime, "res" => res, "src" => src, "tech" => tech, _ => type,
    };

    void RenderActive()
    {
        var tags = new List<(string Key, string Label, string? Glyph)>();
        foreach (var raw in Exporter.SearchTags(F.Q)) tags.Add(("q:" + raw, raw, "\uE721"));
        if (F.Person is { } p) tags.Add(("person", p.Name, "\uE77B"));
        foreach (var k in LibraryFilter.SetKeys)
            foreach (var v in F.Set(k))
                tags.Add(($"{k}:{v}", OptionsOf(k).Find(o => o.Key == v) is { } o ? Loc.T(o.Label) : v, null));
        if (F.YMin != null || F.YMax != null) tags.Add(("year", $"{F.YMin?.ToString() ?? "…"}–{F.YMax?.ToString() ?? "…"}", null));
        if (F.RMin != 0) tags.Add(("rMin", $"IMDb ≥ {F.RMin}", null));
        if (F.VMin != 0) tags.Add(("vMin", $"{Loc.T("votes")} ≥ {FmtVotes(Math.Pow(10, F.VMin))}", null));
        if (F.RtMin != 0) tags.Add(("rtMin", $"Rotten Tomatoes ≥ {F.RtMin}%", null));
        if (F.McMin != 0) tags.Add(("mcMin", $"Metacritic ≥ {F.McMin}", null));
        if (F.Country.Length > 0) tags.Add(("country", F.Country, null));
        if (F.Lang.Length > 0) tags.Add(("lang", F.Lang, null));

        UpdateSections();
        if (tags.Count == 0 && ActiveTags.Children.Count == 0) return;
        ActiveTags.Children.Clear();
        foreach (var (key, label, glyph) in tags)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (glyph != null) row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, FlowDirection = WinKit.DirOf(label) });
            var x = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 9 }, Background = null, BorderThickness = new Thickness(0), Padding = new Thickness(5), MinWidth = 0, MinHeight = 0 };
            ToolTipService.SetToolTip(x, string.Format(Loc.T("Remove filter {0}"), label));
            x.Click += (_, _) => RemoveTag(key);
            row.Children.Add(x);
            ActiveTags.Children.Add(new Border { Style = (Style)Root.Resources["ChipBox"], Padding = new Thickness(10, 1, 4, 1), Child = row });
        }
        ActiveTags.Visibility = tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // how many filters are on in each section, shown beside its heading
    void UpdateSections()
    {
        var n = new Dictionary<string, int>();
        foreach (var (key, sec) in SectionOf) n[sec] = n.GetValueOrDefault(sec) + F.Set(key).Count;
        n["year"] = n.GetValueOrDefault("year") + (F.YMin != null || F.YMax != null ? 1 : 0);
        n["scores"] = (F.RMin != 0 ? 1 : 0) + (F.VMin != 0 ? 1 : 0) + (F.RtMin != 0 ? 1 : 0) + (F.McMin != 0 ? 1 : 0);
        n["region"] = (F.Country.Length > 0 ? 1 : 0) + (F.Lang.Length > 0 ? 1 : 0);
        foreach (var (box, label) in new[] { (SecLibrary, "Library"), (SecGenre, "Genre"), (SecYear, "Year"), (SecScores, "Ratings"), (SecRuntime, "Runtime"), (SecQuality, "Quality"), (SecType, "Type"), (RegionExpander, "Country & language") })
        {
            int count = n.GetValueOrDefault((string)box.Tag);
            string text = count > 0 ? $"{Loc.T(label)}  ({count})" : Loc.T(label);
            if (!Equals(box.Header, text)) box.Header = text;
        }
    }

    void RemoveTag(string key)
    {
        if (key.StartsWith("q:", StringComparison.Ordinal))
        {
            string raw = key[2..];
            int at = F.Q.IndexOf(raw, StringComparison.Ordinal);
            string q = at < 0 ? F.Q : F.Q.Remove(at, raw.Length);
            SetSearchText(Regex.Replace(q, @"\s+", " ").Trim());
            Apply();
            return;
        }
        switch (key)
        {
            case "person": F.Person = null; break;
            case "year": F.YMin = F.YMax = null; break;
            case "rMin": F.RMin = 0; break;
            case "vMin": F.VMin = 0; break;
            case "rtMin": F.RtMin = 0; break;
            case "mcMin": F.McMin = 0; break;
            case "country": F.Country = ""; break;
            case "lang": F.Lang = ""; break;
            default:
                int i = key.IndexOf(':');
                F.Set(key[..i]).Remove(key[(i + 1)..]);
                break;
        }
        SyncFilterControls();
        BuildFacets();
        Apply();
    }
}
