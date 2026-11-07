using System.Runtime.InteropServices.WindowsRuntime;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;

namespace MovieShelf.App;

// Set MOVIESHELF_SHOT=<folder> to have the app render a few screens to PNG and exit. Used to check the UI without a display.
public sealed partial class MainWindow
{
    async Task Shot(string dir, string name, FrameworkElement el)
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(el);
        var px = await rtb.GetPixelsAsync();
        using var ms = new InMemoryRandomAccessStream();
        var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ms);
        enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, px.ToArray());
        await enc.FlushAsync();
        ms.Seek(0);
        var bytes = new byte[ms.Size];
        await ms.ReadAsync(bytes.AsBuffer(), (uint)ms.Size, InputStreamOptions.None);
        await File.WriteAllBytesAsync(Path.Combine(dir, name + ".png"), bytes);
    }

    // the secondary windows keep their single instance in a private static field
    static T Wnd<T>() where T : Window
    {
        foreach (var f in typeof(T).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic))
        {
            if (f.GetValue(null) is T w) return w;
            if (f.GetValue(null) is System.Collections.IDictionary d) foreach (var v in d.Values) if (v is T t) return t;
        }
        throw new InvalidOperationException(typeof(T).Name + " did not open");
    }

    // Set MOVIESHELF_PERF=<file> to have the app write its start-up and filtering times to that file and exit.
    async Task DebugPerf(string file)
    {
        var lines = new List<string>();
        var started = Process.GetCurrentProcess().StartTime;
        double Ms() => (DateTime.Now - started).TotalMilliseconds;
        lines.Add($"window built {Ms():0} ms");
        while (loading || all.Count == 0) await Task.Delay(5);
        lines.Add($"movies loaded {Ms():0} ms ({all.Count})");
        var frame = new TaskCompletionSource();
        void OnFrame(object? s, object e) => frame.TrySetResult();
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnFrame;
        await frame.Task;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFrame;
        lines.Add($"first frame {Ms():0} ms, {renderCount} render(s) so far");
        await Task.Delay(3000);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 20; i++) Apply();
        lines.Add($"filter + sort {sw.Elapsed.TotalMilliseconds / 20:0.0} ms per pass");
        sw.Restart();
        SetSearchText("matrix");
        for (int i = 0; i < 20; i++) Apply();
        lines.Add($"text search {sw.Elapsed.TotalMilliseconds / 20:0.0} ms per pass");
        SetSearchText("");
        sw.Restart();
        for (int i = 0; i < 5; i++) BuildFacets();
        lines.Add($"facets {sw.Elapsed.TotalMilliseconds / 5:0.0} ms per pass");
        sw.Restart();
        var again = await Task.Run(lib.LoadMovies);
        lines.Add($"LoadMovies again {sw.Elapsed.TotalMilliseconds:0} ms ({again.Count})");
        await Task.Delay(1500);
        GC.Collect();
        var me = Process.GetCurrentProcess();
        lines.Add($"working set {me.WorkingSet64 / 1048576} MB, managed {GC.GetTotalMemory(false) / 1048576} MB");
        File.WriteAllLines(file, lines);
        Environment.Exit(0);
    }

    static IEnumerable<DependencyObject> Tree(DependencyObject o)
    {
        if (o is UIElement { Visibility: Visibility.Collapsed }) yield break;
        yield return o;
        for (int i = 0, n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(o); i < n; i++)
            foreach (var c in Tree(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(o, i))) yield return c;
    }

    static string Label(ButtonBase b) => b.Content as string ?? Tree(b).OfType<TextBlock>().FirstOrDefault(t => t.Text.Length > 1)?.Text ?? "";   // a FontIcon holds a one-letter TextBlock

    // Presses a real button through its automation peer, so the Click handler runs as it does for a mouse click.
    static bool Press(DependencyObject root, string label)
    {
        var b = Tree(root).OfType<ButtonBase>().FirstOrDefault(x => Label(x) == label);
        if (b == null) return false;
        Microsoft.UI.Xaml.Automation.Peers.AutomationPeer peer = b is HyperlinkButton h
            ? new Microsoft.UI.Xaml.Automation.Peers.HyperlinkButtonAutomationPeer(h) : new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer((Button)b);
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer).Invoke();
        return true;
    }

    // Set MOVIESHELF_SHOT_DRIVE=1 on a throw-away library to press the buttons of the Problems window and log what each one did.
    async Task DriveProblems(ProblemsWindow p, string dir, string log)
    {
        var root = (FrameworkElement)p.Content;
        string Toast() => Tree(root).OfType<InfoBar>().FirstOrDefault(i => i.IsOpen)?.Message ?? "(no message)";
        string Rows(string name) => $"{name}: {((ItemsControl)root.FindName(name)).Items.Count} rows";
        void Note(string s) => File.AppendAllText(log, s + "\n");
        async Task Do(string a, string? sub, string label, bool ignored = false)
        {
            p.ShowTab(a, sub, ignored);
            await Task.Delay(1000);
            bool ok = Press(root, label);
            await Task.Delay(1200);
            Note($"[{a}/{sub}] {label}: pressed={ok} toast={Toast()} {Rows("IssueList")}");
        }
        async Task<ContentDialog?> Dialog()
        {
            await Task.Delay(800);
            return Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Select(x => x.Child).OfType<ContentDialog>().FirstOrDefault();
        }
        async Task Answer(string label)
        {
            var d = await Dialog();
            Note($"  dialog: {(d == null ? "none" : d.Title + " / " + string.Join(" | ", Tree(d).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Length > 0).Distinct().Take(6)))}");
            if (d != null) Note($"  press {label}: {Press(d, label)}");
            await Task.Delay(1200);
            Note($"  after: toast={Toast()}");
        }
        async Task Clip()
        {
            var c = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            Note("  clipboard: " + (c.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text) ? await c.GetTextAsync() : "(no text)"));
        }
        await Do("dupes", "lower", "Ignore");
        await Shot(dir, "drive-1-ignored", root);
        await Do("dupes", "lower", "Restore", true);
        await Do("dupes", "lower", "This is a series");
        await Do("dupes", "series", "Not a series");
        await Do("dupes", "same", "Copy paths to remove");
        await Clip();
        await Do("dupes", "copies", "Copy extra paths");
        await Clip();
        await Do("quality", "lowq", "Add to wishlist");
        await Do("quality", "lowq", "Add to wishlist");
        await Do("other", "namediff", "Open movie");
        await Do("other", "novideo", "Open folder");
        await Do("dupes", "same", "Show in folder");
        await Do("other", "novideo", "Remove all 4 from list");
        await Answer("Remove from list");
        await Do("dupes", "same", "Remove all from list");
        await Answer("Cancel");
        await Do("dupes", "same", "Wrong movie? Fix match");
        await Do("dupes", "same", "Not the same movie? Fix match");
        await Answer("Cancel");
        await Do("other", "noposter", "Add a poster…");
        await Answer("Use");
        await Answer("Cancel");
        p.ShowTab("disk");
        await Task.Delay(800);
        Note($"disk: select all {Press(root, "Select all 1")}, add {Tree(root).OfType<ButtonBase>().Select(Label).FirstOrDefault(l => l.StartsWith("Add ")) }");
        Note($"disk: select none {Press(root, "Select none")}, add {Tree(root).OfType<ButtonBase>().Select(Label).FirstOrDefault(l => l.StartsWith("Add "))}");
        p.ShowTab("match");
        await Task.Delay(1000);
        Note($"match: {Rows("MatchList")}");
        Note($"Looks right: {Press(root, "Looks right")}");
        await Task.Delay(1200);
        Note($"Not on IMDb: {Press(root, "Not on IMDb")}");
        await Answer("Cancel");
        Note($"Not on IMDb: {Press(root, "Not on IMDb")}");
        await Answer("Mark as not on IMDb");
        await Shot(dir, "drive-2-match", root);
        Note($"Remove: {Press(root, "Remove from list")}");
        await Answer("Remove from list");
        Note($"Check again: {Press(root, "Check again")}");
        await Task.Delay(4000);
        Note($"after check: toast={Toast()} {Rows("MatchList")} button={Tree(root).OfType<ButtonBase>().Select(Label).FirstOrDefault(l => l.StartsWith("Check"))}");
        await Shot(dir, "drive-3-end", root);
    }

    async Task DebugShots(string dir)
    {
        Directory.CreateDirectory(dir);
        var log = Path.Combine(dir, "shots.log");
        var sw = Stopwatch.StartNew();
        Root.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        try
        {
            if (Environment.GetEnvironmentVariable("MOVIESHELF_SHOT_ONLY") is "welcome" or "startup")
            {
                await DebugStart(dir);
                Environment.Exit(0);
            }
            while (loading || all.Count == 0)
            {
                await Task.Delay(200);
                if (sw.Elapsed.TotalSeconds > 180) break;
            }
            File.AppendAllText(log, $"loaded {all.Count} movies in {sw.Elapsed.TotalSeconds:0.0}s, view {view.Count}\n");
            await Task.Delay(4000);
            string? only = Environment.GetEnvironmentVariable("MOVIESHELF_SHOT_ONLY");
            if (only is "main" or "prefs1" or "prefs2" or "keys")
            {
                await DebugChecks(dir, only);
                File.AppendAllText(log, "done\n");
                Environment.Exit(0);
            }
            if (only == "loadlist")
            {
                await CheckLoadList(dir);
                File.AppendAllText(log, "done\n");
                Environment.Exit(0);
            }
            if (only == "tools")
            {
                await ToolShots(dir, log);
                File.AppendAllText(log, "done\n");
                Environment.Exit(0);
            }
            if (Environment.GetEnvironmentVariable("MOVIESHELF_SHOT_ONLY") == "export")
            {
                await ExportShots(dir, log);
                File.AppendAllText(log, "done\n");
                Environment.Exit(0);
            }
            await Shot(dir, "1-grid", Root);
            if (view.Count > 0)
            {
                ShowDetail(view[0]);
                await Task.Delay(2500);
                await Shot(dir, "2-detail", Root);
            }
            SetList(true);
            await Task.Delay(2500);
            await Shot(dir, "3-list", Root);
            SetList(false);
            await Task.Delay(500);
            Loc.IsFa = true;
            ApplyLanguage();
            if (view.Count > 0) ShowDetail(view[0]);
            await Task.Delay(2000);
            await Shot(dir, "4-fa", Root);
            Loc.IsFa = false;
            await Task.Delay(500);
            try { ApplyLanguage(); } catch (Exception ex) { File.AppendAllText(log, "ApplyLanguage: " + ex + "\n"); }
            SetSearchText("year:1999 rating:>8");
            Apply();
            await Task.Delay(2500);
            await Shot(dir, "5-search", Root);
            SetSearchText("");
            groupKey = "decade";
            Apply();
            await Task.Delay(2500);
            await Shot(dir, "7-group", Root);
            groupKey = "none";
            DetailSplit.IsPaneOpen = false;
            SetSelecting(true);
            Apply();
            await Task.Delay(1500);
            for (int i = 0; i < 3; i++) MovieGrid.SelectedItems.Add(MovieGrid.Items[i]);
            await Task.Delay(1500);
            await Shot(dir, "8-select", Root);
            SetSelecting(false);
            F.RtMin = 80;
            F.Country = "United States";
            SyncFilterControls();
            BuildFacets();
            Apply();
            FilterSplit.IsPaneOpen = true;
            await Task.Delay(2500);
            await Shot(dir, "9-filters", Root);
            F.Clear();
            SyncFilterControls();
            var p = new ProblemsWindow(_ => { });
            p.Activate();
            var pw = Stopwatch.StartNew();
            await Task.Delay(500);
            while (p.Busy && pw.Elapsed.TotalSeconds < 150) await Task.Delay(300);
            File.AppendAllText(log, $"problems ready in {pw.Elapsed.TotalSeconds:0.0}s error={p.Error} {p.Timing}\n");
            await Task.Delay(1500);
            ((Microsoft.UI.Xaml.Controls.Grid)p.Content).Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
            await Task.Delay(500);
            await Shot(dir, "6-problems", (FrameworkElement)p.Content);
            async Task Tab(string a, string? sub, bool ignored = false)
            {
                p.ShowTab(a, sub, ignored);
                await Task.Delay(1500);
                await Shot(dir, $"6-problems-{(Loc.IsFa ? "fa-" : "")}{a}-{sub}", (FrameworkElement)p.Content);
            }
            foreach (var (a, sub) in new (string, string?)[]
            {
                ("dupes", "all"), ("dupes", "lower"), ("dupes", "copies"), ("dupes", "series"), ("quality", "lowq"), ("quality", "realq"), ("quality", "dubonly"),
                ("other", "novideo"), ("other", "namediff"), ("other", "runtime"), ("other", "fatitle"), ("other", "noposter"), ("disk", null),
            })
                await Tab(a, sub);
            await Tab("dupes", "same", true);
            Loc.IsFa = true;
            Chrome.Relocalise();
            await Task.Delay(1500);
            await Tab("match", null);
            await Tab("dupes", "all");
            await Tab("other", "novideo");
            await Tab("other", "namediff");
            Loc.IsFa = false;
            Chrome.Relocalise();
            await Task.Delay(1000);
            if (Environment.GetEnvironmentVariable("MOVIESHELF_SHOT_ONLY") == "problems")
            {
                if (Environment.GetEnvironmentVariable("MOVIESHELF_SHOT_DRIVE") == "1") await DriveProblems(p, dir, log);
                File.AppendAllText(log, "done\n");
                Environment.Exit(0);
            }
            var someDir = all.FirstOrDefault(m => m.Dir.Count > 0)?.Dir[0];
            async Task Win(string name, Window w, int wait = 3000)
            {
                w.Activate();
                await Task.Delay(wait);
                if (w.Content is Microsoft.UI.Xaml.Controls.Panel pn && pn.Background == null) pn.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
                await Shot(dir, name, (FrameworkElement)w.Content);
                File.AppendAllText(log, name + " ok\n");
            }
            WishlistWindow.Open(); await Win("10-wishlist", Wnd<WishlistWindow>());
            StatsWindow.Open(all, (_, _, _) => { }); await Win("11-stats", Wnd<StatsWindow>());
            CollectionsWindow.Open(); await Win("12-collections", Wnd<CollectionsWindow>());
            BackupsWindow.Open(() => { }, () => { }); await Win("13-backups", Wnd<BackupsWindow>());
            CheckWindow.Open(() => { }); await Win("14-check", Wnd<CheckWindow>());
            RenamePlanWindow.Open(); await Win("15-rename", Wnd<RenamePlanWindow>(), 8000);
            if (someDir != null) { PersonWindow.Open(someDir.Id, someDir.Name, (_, _) => { }, _ => { }); await Win("17-person", Wnd<PersonWindow>(), 4000); }
            await ExportShots(dir, log);
            File.AppendAllText(log, "done\n");
        }
        catch (Exception e)
        {
            File.AppendAllText(log, e + "\n");
        }
        Environment.Exit(0);
    }
}
