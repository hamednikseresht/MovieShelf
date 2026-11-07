using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieShelf.Core;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MovieShelf.App;

/// <summary>Small helpers the secondary windows (wishlist, statistics, collections, backups) share.</summary>
internal static class WinKit
{
    static readonly object settingsGate = new();

    /// <summary>Title bar, backdrop, size and icon, the same set-up as the Problems window. <paramref name="relocalise"/> redraws the window's text
    /// after a language change (the title is done here, before it runs). <paramref name="help"/> is a Help topic: a "?" in the title bar opens it.</summary>
    public static void Setup(Window w, TitleBar bar, string title, int width, int height, Action? relocalise = null, string? help = null)
    {
        Button? ask = help == null ? null : new Button { Content = "?", Padding = new Thickness(11, 3, 11, 4), Margin = new Thickness(0, 0, 8, 0) };
        void Titles()
        {
            w.Title = Loc.T(title);
            bar.Title = Loc.T(title);
            if (ask == null) return;
            ToolTipService.SetToolTip(ask, Loc.T("Help"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ask, Loc.T("Help"));
        }
        if (ask != null)
        {
            ask.Click += (_, _) => HelpWindow.Open(help);
            bar.RightHeader = ask;
        }
        Titles();
        w.ExtendsContentIntoTitleBar = true;
        w.SetTitleBar(bar);
        Chrome.Attach(w, () =>
        {
            Titles();
            relocalise?.Invoke();
        });
        // what the window built in code took its colours from the old theme, so a theme change redraws it like a language change
        if (w.Content is FrameworkElement root && relocalise != null) root.ActualThemeChanged += (_, _) => relocalise();
        w.AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "MovieShelf.ico");
        if (File.Exists(icon)) w.AppWindow.SetIcon(icon);
    }

    public static void Direction(FrameworkElement root) =>
        root.FlowDirection = Loc.IsFa ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Changes a setting off the UI thread. Settings are read, changed and written one at a time, so two quick changes do not undo each other.</summary>
    public static Task SaveSettings(Action<Settings> change) => Task.Run(() =>
    {
        lock (settingsGate)
        {
            var s = App.Lib.LoadSettings();
            change(s);
            App.Lib.SaveSettings(s);
        }
    });

    /// <summary>Switches the language at once and everywhere: every open window redraws. The choice is saved unless the app is not set up yet.</summary>
    public static async void SetLanguage(string lang)
    {
        bool fa = lang == "fa";
        if (Loc.IsFa == fa) return;
        Loc.IsFa = fa;
        Chrome.Relocalise();
        try
        {
            if (!await Task.Run(() => App.Lib.FirstRun)) await SaveSettings(s => s.Language = fa ? "fa" : "en");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { App.Lib.Log("Language not saved: " + e.Message); }
    }

    /// <summary>A strip for the running job: what it does, how far it is, and Stop when it can be stopped. Fills the space of a window that hides the main job bar.</summary>
    public static FrameworkElement JobStrip(Window w)
    {
        var lib = App.Lib;
        var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        var bar = new ProgressBar();
        var stop = new Button { Content = Loc.T("Stop"), VerticalAlignment = VerticalAlignment.Center };
        stop.Click += (_, _) => lib.Stop();
        var g = new Grid { ColumnSpacing = 12, Padding = new Thickness(16, 6, 16, 8), Visibility = Visibility.Collapsed };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(text);
        left.Children.Add(bar);
        g.Children.Add(left);
        Grid.SetColumn(stop, 1);
        g.Children.Add(stop);
        void Update()
        {
            var j = lib.Job;
            g.Visibility = j.Running ? Visibility.Visible : Visibility.Collapsed;
            if (!j.Running) return;
            text.Text = j.Stopping ? Loc.T("Stopping…") : U2.Stage(j.Stage) + (j.Total > 0 ? $"  {j.Done:N0} / {j.Total:N0}" : "");
            bar.IsIndeterminate = j.Total == 0;
            bar.Maximum = Math.Max(j.Total, 1);
            bar.Value = j.Done;
            stop.Content = Loc.T("Stop");
            stop.Visibility = j.Stoppable && !j.Stopping ? Visibility.Visible : Visibility.Collapsed;
        }
        var queue = w.DispatcherQueue;
        Action changed = () => queue.TryEnqueue(Update);
        lib.JobChanged += changed;
        w.Closed += (_, _) => lib.JobChanged -= changed;
        Update();
        return g;
    }

    public static void Pick(object picker, Window w) => InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(w));

    public static async Task<StorageFile?> SaveFile(Window w, string suggestedName, string label, string ext)
    {
        var p = new FileSavePicker { SuggestedFileName = suggestedName };
        p.FileTypeChoices.Add(Loc.T(label), new List<string> { ext });
        Pick(p, w);
        return await p.PickSaveFileAsync();
    }

    public static async Task<StorageFile?> OpenFile(Window w, string ext)
    {
        var p = new FileOpenPicker();
        p.FileTypeFilter.Add(ext);
        Pick(p, w);
        return await p.PickSingleFileAsync();
    }

    /// <summary>The Windows folder chooser; null when cancelled.</summary>
    public static async Task<string?> PickFolder(Window w)
    {
        var p = new FolderPicker();
        p.FileTypeFilter.Add("*");
        Pick(p, w);
        return (await p.PickSingleFolderAsync())?.Path;
    }

    /// <summary>A dialog with the app's direction and theme and a default button; the caller sets the buttons and content.</summary>
    public static ContentDialog Dialog(XamlRoot root, string title) => new()
    {
        XamlRoot = root, Title = Loc.T(title), DefaultButton = ContentDialogButton.Close,
        FlowDirection = Loc.IsFa ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        RequestedTheme = (root.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,   // a popup does not inherit the window's theme
    };

    /// <summary>Asks a yes/no question. Returns true when the primary button was pressed.</summary>
    public static async Task<bool> Confirm(XamlRoot root, string title, string text, string ok)
    {
        var dlg = Dialog(root, title);
        dlg.Content = new TextBlock { Text = Loc.T(text), TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        dlg.PrimaryButtonText = Loc.T(ok);
        dlg.CloseButtonText = Loc.T("Cancel");
        dlg.DefaultButton = ContentDialogButton.Close;
        try { return await dlg.ShowAsync() == ContentDialogResult.Primary; }
        catch (System.Runtime.InteropServices.COMException) { return false; }   // a dialog is already open in this window
    }

    public static void Info(InfoBar bar, string message, InfoBarSeverity severity = InfoBarSeverity.Informational, string? title = null)
    {
        bar.Severity = severity;
        bar.Title = title == null ? "" : Loc.T(title);
        bar.Message = message;
        bar.IsOpen = true;
    }

    /// <summary>A size like "412 MB". In right-to-left text it is isolated so the number stays before its unit.</summary>
    public static string Size(long bytes)
    {
        string s = bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB"
            : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0} MB"
            : bytes >= 1L << 10 ? $"{bytes / 1024.0:0} KB" : $"{bytes} B";
        return Loc.IsFa ? "‪" + s + "‬" : s;
    }

    public static async void OpenUrl(string url)
    {
        try { await Windows.System.Launcher.LaunchUriAsync(new Uri(url)); }
        catch (Exception e) when (e is UriFormatException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }

    /// <summary>Right to left for a text that starts with an Arabic-script letter, else left to right (a title decides its own direction).</summary>
    public static FlowDirection DirOf(string? s)
    {
        foreach (char c in s ?? "")
        {
            if (c is >= '֐' and <= 'ࣿ') return FlowDirection.RightToLeft;
            if (char.IsLetter(c)) return FlowDirection.LeftToRight;
        }
        return FlowDirection.LeftToRight;
    }

    /// <summary>"1 file" / "5 files": the format keys {0} file and {0} files.</summary>
    public static string Count(int n, string one, string many) => string.Format(Loc.T(n == 1 ? one : many), n.ToString("N0"));

    public static string ImdbUrl(int tconst) => $"https://www.imdb.com/title/tt{tconst:0000000}/";
}
