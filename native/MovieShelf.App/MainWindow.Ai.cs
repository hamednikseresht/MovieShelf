using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieShelf.Core;

namespace MovieShelf.App;

/// <summary>The AI features (Settings → AI): search in plain words, and translating a plot.</summary>
public sealed partial class MainWindow
{
    bool aiReady;
    int translateRun;

    /// <summary>Called at start and when Settings → AI changes: the AI buttons show only with a provider set up.</summary>
    public void AiChanged()
    {
        aiReady = lib.AiReady();
        if (current != null) ShowTranslateRow(current);
    }

    void ShowTranslateRow(Movie m)
    {
        TranslatedText.Visibility = Visibility.Collapsed;
        TranslatedText.Text = "";
        TranslateMsg.Text = "";
        TranslateBtn.IsEnabled = true;
        TranslateBtnText.Text = Loc.T("Translate");
        TranslateRow.Visibility = U2.Vis(aiReady && m.Tconst != null && (!string.IsNullOrEmpty(m.Plot) || !string.IsNullOrEmpty(m.Tagline)));
    }

    async void OnTranslate(object sender, RoutedEventArgs e)
    {
        if (current is not { Tconst: int tc }) return;
        int run = ++translateRun;
        bool again = TranslatedText.Visibility == Visibility.Visible;   // a second press asks the AI again
        TranslateBtn.IsEnabled = false;
        TranslateMsg.Text = Loc.T("Translating…");
        try
        {
            var (plot, tag) = await Task.Run(() => lib.TranslateAsync(tc, again));
            if (run != translateRun || current?.Tconst != tc) return;
            TranslatedText.Text = string.Join("\n\n", new[] { tag, plot }.Where(x => x.Length > 0));
            TranslatedText.Visibility = Visibility.Visible;
            TranslateMsg.Text = "";
            TranslateBtnText.Text = Loc.T("Translate again");
        }
        catch (Exception x) when (x is InvalidOperationException or HttpRequestException or IOException or TimeoutException or Microsoft.Data.Sqlite.SqliteException)
        {
            if (run == translateRun) TranslateMsg.Text = x.Message;
        }
        if (run == translateRun) TranslateBtn.IsEnabled = true;
    }

    /// <summary>AI search: the user describes what they want; the answer becomes a search in the box (they can read and change it).</summary>
    async void OnAiSearch(object sender, RoutedEventArgs e)
    {
        if (!lib.AiReady())
        {
            if (await Dialogs.Confirm(Content.XamlRoot, "AI search", "Connect an AI model first: Settings → AI. Then describe what you want in your own words, for example “funny 90s films with Jim Carrey”.", "Open Settings"))
                await Dialogs.Settings(this, "ai");
            return;
        }
        var box = new TextBox { PlaceholderText = Loc.T("For example: funny 90s films with Jim Carrey · films like Inception · unwatched sci-fi in 4K"), AcceptsReturn = false, TextWrapping = TextWrapping.Wrap };
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75, Text = Loc.T("What you type is sent to your AI provider; the answer becomes a normal search you can change.") };
        var panel = new StackPanel { Spacing = 10, Width = 460 };
        panel.Children.Add(box);
        panel.Children.Add(note);
        var dlg = new ContentDialog
        {
            XamlRoot = Content.XamlRoot, Title = Loc.T("AI search"), Content = panel, PrimaryButtonText = Loc.T("Search"), CloseButtonText = Loc.T("Cancel"),
            DefaultButton = ContentDialogButton.Primary, FlowDirection = Loc.IsFa ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            RequestedTheme = (Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
        };
        AiSearchResult? result = null;
        dlg.PrimaryButtonClick += async (_, a) =>
        {
            a.Cancel = true;
            string text = box.Text.Trim();
            if (text.Length == 0) return;
            var wait = a.GetDeferral();
            dlg.IsPrimaryButtonEnabled = false;
            note.Text = Loc.T("Asking the AI…");
            try
            {
                var genres = all.SelectMany(m => m.Genres).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList();
                result = await Task.Run(() => lib.AiSearchAsync(text, genres));
                dlg.Hide();
            }
            catch (Exception x) when (x is InvalidOperationException or HttpRequestException or IOException or TimeoutException) { note.Text = x.Message; }
            dlg.IsPrimaryButtonEnabled = true;
            wait.Complete();
        };
        await dlg.ShowAsync();
        if (result == null) return;
        // films the AI named: only the ones in this library can show; otherwise its search
        var have = result.Imdb.Where(id => all.Any(m => m.Tconst != null && m.K == id)).ToList();
        string q = have.Count > 0 ? "id:" + string.Join(",", have) : result.Query;
        if (q.Length == 0)
        {
            Toast(Loc.T("None of the films the AI found is in your library."), "info");
            return;
        }
        F.Clear();
        SetSearchText(q);
        SyncFilterControls();
        BuildFacets();
        Apply();
        string about = result.Note.Length > 0 ? result.Note : Loc.T("AI search");
        Toast(have.Count > 0 ? $"{about} · {string.Format(Loc.T("{0} of the films it found are in your library"), have.Count)}" : about, "info");
    }
}
