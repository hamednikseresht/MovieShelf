using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieShelf.Core;

namespace MovieShelf.App;

public static partial class Dialogs
{
    /// <summary>Add movie: one IMDb title by its link or tt-number (for a movie no list or folder holds). Returns what was added, or null when cancelled.
    /// A movie the library has already is reported, not added twice.</summary>
    public static async Task<AddedMovie?> AddMovie(XamlRoot root)
    {
        var lib = App.Lib;
        var dlg = NewDialog(root, "Add movie");
        dlg.CloseButtonText = Loc.T("Cancel");
        dlg.PrimaryButtonText = Loc.T("Add");
        dlg.IsPrimaryButtonEnabled = false;
        var box = new TextBox { PlaceholderText = "https://www.imdb.com/title/tt0111161/   or   tt0111161", FlowDirection = FlowDirection.LeftToRight };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, Loc.T("IMDb link or tt-number"));
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 10, Width = 460 };
        panel.Children.Add(new TextBlock { Text = Loc.T("Paste the IMDb link or the tt-number of the movie. MovieShelf reads its name and year from IMDb, adds it to your library, and fetches the cast, rating and poster."), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        panel.Children.Add(message);
        dlg.Content = panel;
        AddedMovie? result = null;

        void Msg(string text, bool error)
        {
            message.Text = text;
            if (error) message.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            else message.ClearValue(TextBlock.ForegroundProperty);
        }
        box.TextChanged += (_, _) =>
        {
            bool ok = Library.ParseImdbId(box.Text) != null;
            dlg.IsPrimaryButtonEnabled = ok;
            Msg(ok || box.Text.Trim().Length == 0 ? "" : Loc.T("That is not an IMDb link or number."), !ok && box.Text.Trim().Length > 0);
        };
        dlg.PrimaryButtonClick += async (_, a) =>
        {
            a.Cancel = true;   // stay open while it works, or when it fails
            int? id = Library.ParseImdbId(box.Text);
            if (id == null) return;
            var wait = a.GetDeferral();
            dlg.IsPrimaryButtonEnabled = false;
            Msg(Loc.T("Looking it up on IMDb…"), false);
            try
            {
                result = await Task.Run(() => lib.AddByImdb(id.Value));
                if (result.AlreadyThere) Msg(string.Format(Loc.T("{0} is already in your library."), result.Title), false);
                else dlg.Hide();
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or Microsoft.Data.Sqlite.SqliteException or HttpRequestException)
            {
                Msg(e.Message, true);
            }
            dlg.IsPrimaryButtonEnabled = true;
            wait.Complete();
        };
        await dlg.ShowAsync();
        return result;
    }
}
