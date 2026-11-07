using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MovieShelf.Core;
using Windows.Storage.Pickers;

namespace MovieShelf.App;

/// <summary>One line of the rename table.</summary>
public sealed class RenameRowVm
{
    public RenameRow R { get; }
    public RenameRowVm(RenameRow r) => R = r;
    public string Old => R.Old;
    public string New => R.New;
    public string Folder => FolderUtil.DirName(R.Path);
    public Visibility ArrowVis => U2.Vis(!R.Same);
    public string Kind => Loc.T(R.Kind == "folder" ? "Folder" : R.Kind == "side" ? "Subtitle" : "File");
    public string Note => R.Conflict ? Loc.T(R.Why.Length > 0 ? "Skipped: " + R.Why : "Skipped: that name is taken") : "";
    public Visibility NoteVis => U2.Vis(R.Conflict);
    public Brush RowBg => R.Conflict
        ? new SolidColorBrush(Windows.UI.Color.FromArgb(0x26, 0xE5, 0x48, 0x4D))
        : U2.Res("CardBackgroundFillColorDefaultBrush");
}

/// <summary>Shows the proposed file renames (old name to new name) and saves them as a .bat, a .csv and a list of new names.
/// Nothing is renamed by the app: the user reads and runs the .bat.</summary>
public sealed partial class RenamePlanWindow : Window
{
    static RenamePlanWindow? current;
    const int Shown = 300;

    readonly Library lib = App.Lib;
    List<RenameRow> rows = new();
    RenameSummary summary = new(0, 0, 0, 0, new());
    string tab = "todo";
    string pattern = RenamePlanner.DefaultPattern;
    bool filling;
    int generation;

    /// <summary>Opens the window (or brings the open one forward) and plans the renames of the current library.</summary>
    public static void Open()
    {
        if (current != null)
        {
            current.Activate();
            return;
        }
        current = new RenamePlanWindow();
        current.Activate();
    }

    RenamePlanWindow()
    {
        InitializeComponent();
        U2.Setup(this, AppTitleBar, "Rename plan", 960, 820, Relocalise, "files");
        pattern = lib.LoadSettings().RenamePattern is { Length: > 0 } saved ? saved : RenamePlanner.DefaultPattern;
        PatternBox.Text = pattern;
        Texts();
        Closed += (_, _) => current = null;
        _ = LoadAsync();
    }

    void Texts()
    {
        Root.FlowDirection = U2.Flow;
        Loc.Apply(Root);
        Explain.Title = Loc.T("Nothing is renamed by this app");
        Explain.Message = Loc.T("MovieShelf only proposes new names. Save the script, read it, and run it yourself if you agree. A name that is already taken is never renamed over.");
        PatternLabel.Text = Loc.T("Name pattern");
        PatternHelp.Text = Loc.T("Type your own pattern, or press a field to add it. Empty fields vanish with their brackets. The pattern is kept.");
        PreviewBtn.Content = Loc.T("Preview");
        DefaultBtn.Content = Loc.T("Default pattern");
        Chips.Children.Clear();
        foreach (var (key, tip) in RenamePlanner.Fields)
        {
            var b = new Button { Content = "{" + key + "}", Padding = new Thickness(8, 3, 8, 3), FlowDirection = FlowDirection.LeftToRight, FontSize = 12 };
            ToolTipService.SetToolTip(b, Loc.T(tip));
            b.Click += (_, _) =>
            {
                int at = PatternBox.SelectionStart;
                PatternBox.Text = PatternBox.Text.Insert(at, "{" + key + "}");
                PatternBox.SelectionStart = at + key.Length + 2;
                PatternBox.Focus(FocusState.Programmatic);
            };
            Chips.Children.Add(b);
        }
    }

    void OnPatternKey(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; OnPreview(sender, e); }
    }

    void OnDefaultPattern(object sender, RoutedEventArgs e)
    {
        PatternBox.Text = RenamePlanner.DefaultPattern;
        OnPreview(sender, e);
    }

    /// <summary>Checks the pattern, keeps it in the settings and plans again with it.</summary>
    void OnPreview(object sender, RoutedEventArgs e)
    {
        string p = PatternBox.Text.Trim();
        string problem = RenamePlanner.PatternProblem(p);
        PatternError.Visibility = problem.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PatternError.Text = problem.Length > 0 ? Loc.T(problem) : "";
        if (problem.Length > 0) return;
        pattern = p;
        _ = LoadAsync();
        _ = WinKit.SaveSettings(x => x.RenamePattern = p);
    }

    void Relocalise()
    {
        Texts();
        Msg.IsOpen = false;
        if (summary.Total > 0) ShowPlan();
    }

    void OnReload(object sender, RoutedEventArgs e) => _ = LoadAsync();

    async Task LoadAsync()
    {
        int gen = ++generation;
        string pat = pattern;
        Busy.Visibility = Visibility.Visible;
        Body.Visibility = Visibility.Collapsed;
        Empty.Visibility = Visibility.Collapsed;
        Msg.IsOpen = false;
        try
        {
            var (r, s) = await Task.Run(() =>
            {
                var plan = RenamePlanner.Plan(lib.LoadMovies(), lib.LoadSettings().MediaDirs, pat);
                return (plan, RenamePlanner.Summary(plan));
            });
            if (gen != generation) return;
            rows = r;
            summary = s;
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
        {
            Busy.Visibility = Visibility.Collapsed;
            Message(InfoBarSeverity.Error, Loc.T("Could not plan the names."), e.Message);
            return;
        }
        Busy.Visibility = Visibility.Collapsed;
        if (summary.Total == 0)
        {
            Empty.Visibility = Visibility.Visible;
            return;
        }
        Body.Visibility = Visibility.Visible;
        tab = "todo";
        ShowPlan();
    }

    void ShowPlan()
    {
        Tiles.Children.Clear();
        Tiles.Children.Add(U2.Tile(summary.Todo.ToString("N0"), Loc.T("to rename") + (summary.Folders > 0 ? " " + U2.Fmt("({0} folders)", summary.Folders.ToString("N0")) : "")));
        Tiles.Children.Add(U2.Tile(summary.Same.ToString("N0"), Loc.T("already named like this")));
        Tiles.Children.Add(U2.Tile(summary.Conflicts.ToString("N0"), Loc.T("skipped: name taken")));
        Tiles.Children.Add(U2.Tile(summary.Total.ToString("N0"), Loc.T("files with a known location")));
        filling = true;
        Tabs.Items.Clear();
        Tabs.Items.Add(new SelectorBarItem { Text = Loc.T("To rename") + $"  {summary.Todo:N0}", Tag = "todo" });
        if (summary.Conflicts > 0) Tabs.Items.Add(new SelectorBarItem { Text = Loc.T("Conflicts") + $"  {summary.Conflicts:N0}", Tag = "conflict" });
        if (summary.Same > 0) Tabs.Items.Add(new SelectorBarItem { Text = Loc.T("Already named") + $"  {summary.Same:N0}", Tag = "same" });
        Tabs.SelectedItem = Tabs.Items.FirstOrDefault(i => (string)i.Tag == tab) ?? Tabs.Items[0];
        filling = false;
        BatBtn.IsEnabled = CsvBtn.IsEnabled = rows.Count > 0;
        ListBtn.IsEnabled = summary.Todo > 0;
        Fill();
    }

    void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (filling || sender.SelectedItem is not SelectorBarItem i) return;
        tab = (string)i.Tag;
        Fill();
    }

    void Fill()
    {
        IEnumerable<RenameRow> src = tab switch
        {
            "conflict" => rows.Where(r => r.Conflict),
            "same" => rows.Where(r => r.Same),
            _ => summary.Sample,
        };
        int total = tab switch { "conflict" => summary.Conflicts, "same" => summary.Same, _ => summary.Todo };
        var list = src.Take(Shown).Select(r => new RenameRowVm(r)).ToList();
        RowList.ItemsSource = list;
        EmptyText.Text = Loc.T("Nothing here.");
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        More.Text = total > Shown
            ? U2.Fmt("Showing the first {0} of {1}. The saved files contain all of them.", Shown, total.ToString("N0"))
            : Loc.T("The saved files contain exactly these lines.");
    }

    void Message(InfoBarSeverity sev, string title, string text = "")
    {
        Msg.Severity = sev;
        Msg.Title = title;
        Msg.Message = text;
        Msg.IsOpen = true;
    }

    void OnSaveBat(object sender, RoutedEventArgs e) => _ = SaveAsync("bat", "Batch file");
    void OnSaveCsv(object sender, RoutedEventArgs e) => _ = SaveAsync("csv", "CSV file");
    void OnSaveList(object sender, RoutedEventArgs e) => _ = SaveAsync("list", "Text file");

    async Task SaveAsync(string kind, string typeName)
    {
        try
        {
            var plan = rows;
            var file = await Task.Run(() => RenamePlanner.PlanFile(kind, plan));
            if (file == null) return;
            string ext = Path.GetExtension(file.Name);
            var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(file.Name) };
            picker.FileTypeChoices.Add(Loc.T(typeName), new List<string> { ext });
            U2.InitPicker(picker, this);
            var target = await picker.PickSaveFileAsync();
            if (target == null) return;
            await U2.WriteAsync(target.Path, file.Data);
            Message(InfoBarSeverity.Success, Loc.T("Saved") + ": " + target.Path,
                kind == "bat" ? Loc.T("Nothing has been renamed. Open the file in a text editor to read it, then run it when you agree.") : "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Message(InfoBarSeverity.Error, Loc.T("Could not save the file."), ex.Message);
        }
    }
}
