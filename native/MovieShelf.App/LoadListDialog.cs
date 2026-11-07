using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieShelf.Core;

namespace MovieShelf.App;

public static partial class Dialogs
{
    /// <summary>Load list: the one way to bring movie names in. A list file (.txt, .csv), a folder of lists or a folder of movies.
    /// The dialog first shows what loading would do (names repeated in the list, already in the library, similar to a movie the library has),
    /// then asks: add to the current list, or start a new one (the old lists are moved to "previous lists", nothing is deleted).
    /// Returns the result when something was loaded; the rescan is already queued.</summary>
    public static async Task<ListLoad?> LoadList(Window owner, XamlRoot root)
    {
        var lib = App.Lib;
        var dlg = NewDialog(root, "Load list");
        dlg.CloseButtonText = Loc.T("Cancel");
        dlg.IsPrimaryButtonEnabled = false;
        dlg.PrimaryButtonText = "";
        dlg.SecondaryButtonText = "";

        TextBlock Text(string text, bool strong = false, bool dim = false)
        {
            var t = new TextBlock { Text = Loc.T(text), TextWrapping = TextWrapping.Wrap };
            if (strong) t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            if (dim) { t.Opacity = 0.75; t.FontSize = 12; }
            return t;
        }

        var panel = new StackPanel { Spacing = 10, Width = 460 };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        ListSource? src = null;
        ListLoad? result = null;
        bool askedNew = false;

        void Msg(string text, bool error = false)
        {
            message.Text = text;
            if (error) message.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            else message.ClearValue(TextBlock.ForegroundProperty);
        }

        Expander Warn(int n, string what, List<string> names)
        {
            var list = new StackPanel { Spacing = 2 };
            foreach (var x in names) list.Children.Add(new TextBlock { Text = x, FlowDirection = FlowDirection.LeftToRight, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap });
            if (n > names.Count) list.Children.Add(new TextBlock { Text = string.Format(Loc.T("… and {0} more"), (n - names.Count).ToString("N0")), Opacity = 0.75 });
            return new Expander
            {
                Header = new TextBlock { Text = n.ToString("N0") + " " + Loc.T(what), TextWrapping = TextWrapping.Wrap }, Content = list, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
        }

        void PickView()
        {
            dlg.PrimaryButtonText = "";
            dlg.SecondaryButtonText = "";
            src = null;
            askedNew = false;
            panel.Children.Clear();
            panel.Children.Add(Text("Bring in a list of movie names. Before anything is added you see which names are repeated or already in your library, and you choose whether they join your current list or start a new one."));
            var path = new TextBox { PlaceholderText = @"D:\Movies   or   C:\lists\new movies.txt", HorizontalAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.LeftToRight };

            async void Preview(ListSource s)
            {
                Msg(Loc.T("Reading…"));
                try
                {
                    var pv = await Task.Run(() => lib.PreviewList(s));
                    src = s;
                    ChooseView(pv);
                }
                catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
                {
                    Msg(Loc.T(e.Message), true);
                }
            }

            var file = new Button { Content = Loc.T("A list file…") };
            file.Click += async (_, _) =>
            {
                try
                {
                    var p = new Windows.Storage.Pickers.FileOpenPicker();
                    p.FileTypeFilter.Add(".txt");
                    p.FileTypeFilter.Add(".csv");
                    p.FileTypeFilter.Add(".tsv");
                    Pick(p, owner);
                    var f = await p.PickSingleFileAsync();
                    if (f != null) Preview(new ListSource(f.Path));
                }
                catch (System.Runtime.InteropServices.COMException e) { Msg(e.Message, true); }
            };
            var folder = new Button { Content = Loc.T("A folder…") };
            folder.Click += async (_, _) =>
            {
                try
                {
                    if (await WinKit.PickFolder(owner) is string d) Preview(new ListSource(d));
                }
                catch (System.Runtime.InteropServices.COMException e) { Msg(e.Message, true); }
            };
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { file, folder } });
            panel.Children.Add(Text(".txt with one name per line, or .csv with a name, title + year, or IMDb id column. A folder can hold list files, or movies: the names of the video files inside it are loaded. Files are only read, never moved.", dim: true));
            panel.Children.Add(Text("Or type the path of a file or folder"));
            var read = new Button { Content = Loc.T("Read") };
            void Go()
            {
                string v = path.Text.Trim().Trim('"');
                if (v.Length > 0) Preview(new ListSource(v)); else path.Focus(FocusState.Programmatic);
            }
            read.Click += (_, _) => Go();
            path.KeyDown += (_, a) => { if (a.Key == Windows.System.VirtualKey.Enter) Go(); };
            var line = new Grid { ColumnSpacing = 8 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(path);
            Grid.SetColumn(read, 1);
            line.Children.Add(read);
            panel.Children.Add(line);
            panel.Children.Add(message);
            Msg("");
        }

        void ChooseView(ListPreview pv)
        {
            askedNew = false;
            panel.Children.Clear();
            panel.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = string.Format(Loc.T(pv.Lists > 1 ? "{0} movie names found in {2} lists, {1} of them new to your library." : "{0} movie names found, {1} of them new to your library."),
                    pv.Names.ToString("N0"), pv.NewN.ToString("N0"), pv.Lists.ToString("N0")),
            });
            bool dup = pv.RepeatedN + pv.ExistingN + pv.SimilarN > 0;
            panel.Children.Add(Text(dup ? "Duplicates found. Nothing has been added yet." : "No duplicates: every name is new.", dup));
            if (pv.RepeatedN > 0) panel.Children.Add(Warn(pv.RepeatedN, "repeated inside this list (each is loaded once)", pv.Repeated));
            if (pv.ExistingN > 0) panel.Children.Add(Warn(pv.ExistingN, "already in your library (not added again)", pv.Existing));
            if (pv.SimilarN > 0) panel.Children.Add(Warn(pv.SimilarN, "look like a movie you already have (same title and year, other file name): check before you add", pv.Similar));
            panel.Children.Add(Text("Where should these names go?", true));
            panel.Children.Add(Text("Add to current list: they join the lists you already have. Start a new list: your current list files are moved to a “previous lists” folder (nothing is deleted) and this list takes their place. Movies found in your movie folders stay either way.", dim: true));
            panel.Children.Add(message);
            Msg("");
            dlg.PrimaryButtonText = Loc.T("Add to current list");
            dlg.SecondaryButtonText = Loc.T("Start a new list");
            dlg.IsPrimaryButtonEnabled = true;
            dlg.IsSecondaryButtonEnabled = true;
        }

        async void Load(ContentDialogButtonClickEventArgs a, string mode)
        {
            var wait = a.GetDeferral();
            a.Cancel = true;
            try
            {
                if (mode == "new" && !askedNew)
                {
                    // a second dialog cannot open on top of this one: ask here, on the same button
                    askedNew = true;
                    Msg(Loc.T("Your current list files are moved to the “previous lists” folder inside your list folder. Nothing is deleted. The new list takes their place. Movies from your movie folders stay. Click “Yes, start a new list” to continue."));
                    dlg.SecondaryButtonText = Loc.T("Yes, start a new list");
                    return;
                }
                Msg(Loc.T("Loading…"));
                dlg.IsPrimaryButtonEnabled = false;
                dlg.IsSecondaryButtonEnabled = false;
                var s = src!;
                try
                {
                    result = await Task.Run(() => lib.LoadList(s, mode));
                    a.Cancel = false;
                }
                catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
                {
                    Msg(Loc.T(e.Message), true);
                    dlg.IsPrimaryButtonEnabled = true;
                    dlg.IsSecondaryButtonEnabled = true;
                }
            }
            finally { wait.Complete(); }
        }
        dlg.PrimaryButtonClick += (_, a) => Load(a, "add");
        dlg.SecondaryButtonClick += (_, a) => Load(a, "new");

        PickView();
        dlg.Content = new ScrollViewer { Content = panel, MaxHeight = 540, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 14, 0) };
        await dlg.ShowAsync();
        return result;
    }
}
