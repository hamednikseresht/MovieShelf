using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieShelf.Core;
using Windows.Storage.Pickers;

namespace MovieShelf.App;

public static partial class Dialogs
{
    /// <summary>The welcome dialog of a fresh install. Returns true when the library is set up and the first update may start.</summary>
    public static async Task<bool> FirstRun(MainWindow main)
    {
        var lib = App.Lib;
        var dlg = NewDialog(main.Content.XamlRoot, "Welcome to MovieShelf");
        dlg.RequestedTheme = (main.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default;
        dlg.PrimaryButtonText = Loc.T("Start");
        dlg.SecondaryButtonText = Loc.T("Load a backup");
        dlg.CloseButtonText = Loc.T("Cancel");

        TextBlock Text(string text, bool strong = false)
        {
            var t = new TextBlock { Text = Loc.T(text), TextWrapping = TextWrapping.Wrap };
            if (strong) t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            return t;
        }

        TextBox Field(string placeholder) => new() { PlaceholderText = placeholder, HorizontalAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.LeftToRight };
        Grid Line(TextBox box, Button button)
        {
            var g = new Grid { ColumnSpacing = 8 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(box);
            Grid.SetColumn(button, 1);
            g.Children.Add(button);
            return g;
        }
        TextBlock Small(string text) => new() { Text = Loc.T(text), TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75 };

        // the library is built from a movie folder and/or a list file: one is enough
        var media = Field(@"D:\Movies");
        var browseMedia = new Button { Content = Loc.T("Browse") };
        browseMedia.Click += async (_, _) =>
        {
            try
            {
                var p = new FolderPicker();
                p.FileTypeFilter.Add("*");
                Pick(p, main);
                var f = await p.PickSingleFolderAsync();
                if (f != null) media.Text = f.Path;
            }
            catch (System.Runtime.InteropServices.COMException) { }
        };
        var list = Field(@"D:\Lists\movies.txt");
        var browseList = new Button { Content = Loc.T("Choose a file…") };
        browseList.Click += async (_, _) =>
        {
            try
            {
                var p = new FileOpenPicker();
                p.FileTypeFilter.Add(".txt");
                p.FileTypeFilter.Add(".csv");
                p.FileTypeFilter.Add(".tsv");
                Pick(p, main);
                var f = await p.PickSingleFileAsync();
                if (f != null) list.Text = f.Path;
            }
            catch (System.Runtime.InteropServices.COMException) { }
        };
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };

        var panel = new StackPanel { Spacing = 10, MinWidth = 480 };
        panel.Children.Add(Text("How do you want to start?"));
        panel.Children.Add(Text("Start with the defaults", true));
        panel.Children.Add(Text("Read your movie folder or list, download the IMDb data (about 2 GB, 10 to 20 minutes) and build a new library."));
        panel.Children.Add(Text("Movie folder"));
        panel.Children.Add(Line(media, browseMedia));
        panel.Children.Add(Small("The app goes through it by itself and lists every movie it finds. More folders can be added later in Settings."));
        panel.Children.Add(Text("Movie list (optional)"));
        panel.Children.Add(Line(list, browseList));
        panel.Children.Add(Small("A .txt or .csv file with movie names, or a folder of them. More can be loaded later with the Load list button."));
        panel.Children.Add(info);
        panel.Children.Add(Text("Load a backup", true));
        panel.Children.Add(Text("Bring back a full backup made on this or another PC: library, settings, API keys, list files and posters."));
        dlg.Content = new ScrollViewer { Content = panel, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        bool ok = false;
        dlg.PrimaryButtonClick += async (_, a) =>
        {
            var wait = a.GetDeferral();     // the folder and list are read and the settings written off the UI thread; the dialog stays open meanwhile
            string mediaDir = media.Text.Trim(), listPath = list.Text.Trim();
            try
            {
                await Task.Run(() => lib.StartFresh(listPath.Length > 0 ? listPath : null, mediaDir.Length > 0 ? mediaDir : null));
                ok = true;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                info.Text = e.Message;
                a.Cancel = true;
            }
            wait.Complete();
        };
        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Secondary) main.OpenBackups();
        return ok;
    }
}
