using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieShelf.Core;

namespace MovieShelf.App;

// Commands the dialogs and the secondary windows call back into, the theme, language and poster size, the job bar, the badges and the short messages.
public sealed partial class MainWindow
{
    /// <summary>"system", "light" or "dark".</summary>
    public string Theme => Chrome.Theme;

    public void SetTheme(string theme)
    {
        Chrome.SetTheme(theme);
        UpdateThemeIcon();
        SavePrefsSoon();
    }

    void UpdateThemeIcon() => ThemeIcon.Glyph = Chrome.IsDark ? "\uE708" : "\uE706";

    // the coloured chips of the movie pane are made for one theme
    void OnThemeChanged()
    {
        UpdateThemeIcon();
        if (DetailSplit.IsPaneOpen && current != null) ShowDetail(current);
    }

    void OnThemeMenuOpening(object sender, object e)
    {
        ThemeMenu.Items.Clear();
        foreach (var (key, label, glyph) in new[] { ("light", "Light", "\uE706"), ("dark", "Dark", "\uE708"), ("system", "System", "\uE7F4") })
        {
            var item = new RadioMenuFlyoutItem { Text = Loc.T(label), Icon = new FontIcon { Glyph = glyph }, GroupName = "theme", IsChecked = Theme == key };
            item.Click += (_, _) => SetTheme(key);
            ThemeMenu.Items.Add(item);
        }
    }

    /// <summary>The language button: everything switches at once and the choice is saved.</summary>
    async void OnLanguageClick(object sender, RoutedEventArgs e) => await SetLanguage(!Loc.IsFa);

    public async Task SetLanguage(bool persian)
    {
        Loc.IsFa = persian;
        Chrome.Relocalise();
        await Task.Run(() =>
        {
            var s = lib.LoadSettings();
            s.Language = persian ? "fa" : "en";
            lib.SaveSettings(s);
        });
    }

    /// <summary>"s", "m" or "l": how large the cards in the grid are.</summary>
    public string PosterSize { get; private set; } = "m";

    void SetPosterSizeNow(string size)
    {
        PosterSize = size is "s" or "l" ? size : "m";
        MovieItem.CardWidth = PosterSize switch { "s" => 132, "l" => 204, _ => 172 };
        if (Root.XamlRoot != null) UpdatePosterWidth();
    }

    public void SetPosterSize(string size)
    {
        if (size == PosterSize) return;
        SetPosterSizeNow(size);
        SavePrefsSoon();
        // cards take their size when they are made, and the panel measures the first one again only for a new list
        MovieGrid.ItemsSource = null;
        if (ready) Render(keep: true);
    }

    void OnPosterSizeOpening(object sender, object e)
    {
        PosterSizeMenu.Items.Clear();
        foreach (var (key, label) in new[] { ("s", "Small"), ("m", "Medium"), ("l", "Large") })
        {
            var item = new RadioMenuFlyoutItem { Text = Loc.T(label), GroupName = "psize", IsChecked = PosterSize == key };
            item.Click += (_, _) => SetPosterSize(key);
            PosterSizeMenu.Items.Add(item);
        }
    }

    public void Notice(string text) => ShowInfo(text);

    /// <summary>Starts the whole update (lists, IMDb data, matching, posters, scores…). <paramref name="force"/> downloads the IMDb files again.</summary>
    public async void RunUpdate(bool force = false)
    {
        if (lib.FirstRun)
        {
            if (!await Dialogs.FirstRun(this)) return;
            force = false;
        }
        bool hasLists = await Task.Run(() =>
        {
            string dir = lib.LoadSettings().ListDir;
            return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.txt").Any();
        });
        if (!hasLists)
        {
            await Dialogs.Settings(this);
            return;
        }
        if (!lib.StartFullUpdate(force)) Toast(Loc.T("An update is running. This one starts right after it."), "info");
    }

    // ---------- job bar ----------
    int jobUiQueued;
    bool wasRunning;

    // a job can report thousands of steps a second; the window only needs the latest
    void OnJobChanged()
    {
        if (Interlocked.Exchange(ref jobUiQueued, 1) == 0)
            DispatcherQueue.TryEnqueue(UpdateJobUi);
    }

    void UpdateJobUi()
    {
        Interlocked.Exchange(ref jobUiQueued, 0);
        var j = lib.Job;
        bool showErr = j.Error != null && !j.Running && j.Error != dismissedError;
        JobBar.Visibility = j.Running || showErr ? Visibility.Visible : Visibility.Collapsed;
        JobText.Text = showErr ? Loc.T("The last update stopped:") + " " + j.Error
            : j.Stopping ? Loc.T("Stopping…")
            : U2.Stage(j.Stage) + (j.Total > 0 ? $" — {j.Done:N0} / {j.Total:N0}" : "");
        JobProgress.Visibility = showErr ? Visibility.Collapsed : Visibility.Visible;
        JobProgress.IsIndeterminate = j.Running && j.Total == 0;
        JobProgress.Maximum = Math.Max(1, j.Total);
        JobProgress.Value = Math.Min(j.Done, j.Total);
        JobStop.Visibility = !showErr && j.Running && j.Stoppable && !j.Stopping ? Visibility.Visible : Visibility.Collapsed;
        JobLog.Visibility = JobDismiss.Visibility = showErr ? Visibility.Visible : Visibility.Collapsed;
        if (j.Running == wasRunning) return;
        wasRunning = j.Running;
        if (j.Running) reloadTimer.Start();
        else
        {
            reloadTimer.Stop();
            if (j.Error != null) Toast(Loc.T("Update stopped with an error. See the log in Settings."), "err");
            else Toast(Loc.T(j.Stopped ? "Update stopped" : "Update finished"));
            if (all.Count == 0) RenderEmpty();   // the empty screen explains what to do
        }
    }

    void OnJobStop(object sender, RoutedEventArgs e)
    {
        JobStop.Visibility = Visibility.Collapsed;
        lib.Stop();
    }

    void OnJobDismiss(object sender, RoutedEventArgs e)
    {
        dismissedError = lib.Job.Error;
        JobBar.Visibility = Visibility.Collapsed;
        SavePrefsSoon();
    }

    async void OnJobLog(object sender, RoutedEventArgs e) => await Dialogs.Settings(this, "log");

    // ---------- dialog windows ----------
    Border? scrim;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool EnableWindow(IntPtr hwnd, bool enable);

    /// <summary>While a dialog window is open over this one, this window is blurred and takes no input.</summary>
    public void SetModal(bool on)
    {
        EnableWindow(WinRT.Interop.WindowNative.GetWindowHandle(this), !on);
        if (on && scrim == null)
        {
            scrim = new Border();
            Grid.SetRowSpan(scrim, 3);
            Canvas.SetZIndex(scrim, 100);
            scrim.ActualThemeChanged += (_, _) => PaintScrim();
            Root.Children.Add(scrim);
            PaintScrim();
        }
        if (scrim != null) scrim.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    void PaintScrim()
    {
        var tint = scrim!.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
        scrim.Background = new Microsoft.UI.Xaml.Media.AcrylicBrush
        {
            TintColor = tint, TintOpacity = 0.1, TintLuminosityOpacity = 0.1,
            FallbackColor = Windows.UI.Color.FromArgb(0x80, tint.R, tint.G, tint.B),
        };
        scrim.Opacity = 0.75;   // the blur has a fixed strength, so the sharp window showing through is what lightens it
    }

    // ---------- short messages ----------
    public void ShowInfo(string text) => Toast(text);

    /// <summary>A short message that leaves by itself; kind "ok", "info" or "err". Errors stay longer.</summary>
    async void Toast(string text, string kind = "ok")
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new FontIcon
        {
            Glyph = kind == "err" ? "\uE7BA" : kind == "info" ? "\uE946" : "\uE73E", FontSize = 15, VerticalAlignment = VerticalAlignment.Center,
            Foreground = U2.Badge(kind == "err" ? "unmatched" : kind == "info" ? "better" : "new").Fg,
        });
        row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, VerticalAlignment = VerticalAlignment.Center });
        var box = new Border { Style = (Style)Root.Resources["ToastBox"], Child = row };
        ToastHost.Children.Add(box);
        while (ToastHost.Children.Count > 3) ToastHost.Children.RemoveAt(0);
        await Task.Delay(kind == "err" ? 7000 : 4000);
        ToastHost.Children.Remove(box);
    }

    // ---------- badges ----------
    int badgeRun;

    /// <summary>The numbers on the Problems and Wishlist buttons. Counting can take a moment on a big library, so it runs in the background.</summary>
    async void RefreshBadges()
    {
        int run = ++badgeRun;
        var (problems, wishlist) = await Task.Run(() =>
        {
            lib.OpenDuplicates();   // cached: the counters below find it ready and hold the lock for a moment only
            lock (lib.Lock)
            {
                var s = lib.GetLibraryStats();
                return (s.Problems + s.Duplicates + s.Novideo, s.Wishlist);
            }
        });
        if (run != badgeRun) return;
        ProblemsCount.Text = problems > 999 ? "999+" : problems.ToString();
        ProblemsBadge.Visibility = problems > 0 ? Visibility.Visible : Visibility.Collapsed;
        WishCount.Text = wishlist > 99 ? "99+" : wishlist.ToString();
        WishBadge.Visibility = wishlist > 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(ProblemsBtn, string.Format(Loc.T("Problems: {0} to look at"), problems));
        ToolTipService.SetToolTip(WishlistBtn, $"{Loc.T("Wishlist")}: {wishlist:N0}");
    }
}
