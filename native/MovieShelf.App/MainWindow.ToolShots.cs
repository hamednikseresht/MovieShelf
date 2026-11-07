using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MovieShelf.App;

// MOVIESHELF_SHOT_ONLY=tools: renders the Help and Settings windows and the other tool windows in English and Persian, and checks that a language switch reaches open windows.
public sealed partial class MainWindow
{
    async Task ToolShots(string dir, string log)
    {
        void Log(string s) => File.AppendAllText(log, s + "\n");
        async Task Snap(string name, Window w, int wait = 900)
        {
            await Task.Delay(wait);
            if (w.Content is Panel p && p.Background == null) p.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
            await Shot(dir, name, (FrameworkElement)w.Content);
            Log($"{name} ok, {((FrameworkElement)w.Content).ActualWidth:0} x {((FrameworkElement)w.Content).ActualHeight:0}, window {w.AppWindow.Size.Width} x {w.AppWindow.Size.Height}");
        }

        // how fast the windows appear: from the call until the first frame after it
        async Task<double> Timed(Action open)
        {
            var sw = Stopwatch.StartNew();
            open();
            var frame = new TaskCompletionSource();
            void OnFrame(object? s, object e) => frame.TrySetResult();
            CompositionTarget.Rendering += OnFrame;
            await frame.Task;
            CompositionTarget.Rendering -= OnFrame;
            return sw.Elapsed.TotalMilliseconds;
        }

        Log($"help window {await Timed(() => HelpWindow.Open("find")):0} ms");
        var help = Wnd<HelpWindow>();
        await Snap("h1-find-en", help);
        foreach (var lang in new[] { "en", "fa" })
        {
            WinKit.SetLanguage(lang);
            await Task.Delay(300);
            int n = 0;
            var sw = Stopwatch.StartNew();
            foreach (var t in HelpDoc.Load(lang))
            {
                help.Go(t.Id);
                n += t.Blocks.Count;
                await Task.Delay(1);
            }
            Log($"help {lang}: {HelpDoc.Load(lang).Count} topics, {n} blocks, all shown in {sw.ElapsedMilliseconds} ms");
        }
        help.Go("setup");
        await Snap("h2-setup-fa", help);
        help.Go("faq");
        await Snap("h3-faq-fa", help);
        help.Find("ffprobe");
        await Snap("h4-search-fa", help);
        Log($"search fa: {help.TopicsShown} topics, showing {help.CurrentTopic}");
        help.Find("");
        WinKit.SetLanguage("en");
        await Task.Delay(300);
        help.Find("ffprobe");
        await Snap("h5-search-en", help);
        Log($"search en: {help.TopicsShown} topics, showing {help.CurrentTopic}");
        help.Find("no such words");
        await Snap("h6-nothing", help);
        help.Find("");
        help.Go("setup");
        await Snap("h7-setup-en", help);

        // settings, every tab, in both languages
        Log($"settings window {await Timed(() => SettingsWindow.Open(this, null)):0} ms");
        var set = Wnd<SettingsWindow>();
        await Snap("s1-general-en", set, 2500);
        foreach (var tab in new[] { "appearance", "online", "maintenance", "log" })
        {
            SettingsWindow.Open(this, tab);
            await Snap("s2-" + tab + "-en", set, 2000);
        }

        // the other windows open while the language changes
        async Task Show(string name, Action open)
        {
            Log($"{name} window {await Timed(open):0} ms");
            await Task.Delay(1200);
        }
        await Show("wishlist", () => WishlistWindow.Open());
        await Show("stats", () => StatsWindow.Open(all, (_, _, _) => { }));
        await Show("collections", () => CollectionsWindow.Open(_ => { }, _ => { }));
        await Show("backups", () => BackupsWindow.Open(() => { }, () => { }));
        await Show("check", () => CheckWindow.Open());
        await Show("rename", () => RenamePlanWindow.Open());
        var who = all.FirstOrDefault(m => m.Dir.Count > 0)?.Dir[0];
        if (who != null) await Show("person", () => PersonWindow.Open(who.Id, who.Name, (_, _) => { }, _ => { }));
        await Task.Delay(2500);
        if (who != null)
        {
            await Snap("w0-person-en", Wnd<PersonWindow>());
        }
        Log("switching to Persian");
        WinKit.SetLanguage("fa");
        await Task.Delay(2500);
        SettingsWindow.Open(this, "general");
        await Snap("s3-general-fa", set, 2500);
        foreach (var tab in new[] { "appearance", "online", "maintenance", "log" })
        {
            SettingsWindow.Open(this, tab);
            await Snap("s4-" + tab + "-fa", set, 2000);
        }
        await Snap("w1-wishlist-fa", Wnd<WishlistWindow>());
        await Snap("w2-stats-fa", Wnd<StatsWindow>());
        await Snap("w3-collections-fa", Wnd<CollectionsWindow>());
        await Snap("w4-backups-fa", Wnd<BackupsWindow>());
        await Snap("w5-check-fa", Wnd<CheckWindow>());
        await Snap("w6-rename-fa", Wnd<RenamePlanWindow>());
        if (who != null) await Snap("w8-person-fa", Wnd<PersonWindow>());
        await Shot(dir, "w9-main-fa", Root);
        WinKit.SetLanguage("en");
        await Task.Delay(2500);
        await Snap("w10-wishlist-en", Wnd<WishlistWindow>());
        await Snap("w11-collections-en", Wnd<CollectionsWindow>());
        await Snap("w12-rename-en", Wnd<RenamePlanWindow>());
        if (who != null) await Snap("w13-person-en", Wnd<PersonWindow>());
        await Snap("w14-check-en", Wnd<CheckWindow>());
        await Snap("w15-backups-en", Wnd<BackupsWindow>());
    }
}
