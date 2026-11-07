using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieShelf.Core;

namespace MovieShelf.App;

/// <summary>An automatic daily backup as the list shows it.</summary>
public sealed class AutoBackupVm
{
    public BackupFile F { get; init; } = null!;
    public string Date => F.Name.Replace("personal-", "").Replace(".json", "");
    public string SizeText => WinKit.Size(F.Size);
    public string RestoreLabel => Loc.T("Restore");
}

/// <summary>Personal (quick) backups and full backups: save, restore, and the automatic daily copies.</summary>
public sealed partial class BackupsWindow : Window
{
    static BackupsWindow? instance;

    /// <summary>Shows the backups window, or brings the open one to the front.
    /// <paramref name="onChanged"/> runs after a restore changed the library (the main window reloads);
    /// <paramref name="runUpdate"/> starts the library update that applies restored manual matches.</summary>
    public static void Open(Action onChanged, Action runUpdate)
    {
        if (instance == null) instance = new BackupsWindow();
        instance.onChanged = onChanged;
        instance.runUpdate = runUpdate;
        instance.Activate();
    }

    internal static BackupsWindow? Instance => instance;

    /// <summary>Goes to the "quick" or "full" tab.</summary>
    internal void ShowTab(string key)
    {
        if (Tabs.Items.FirstOrDefault(i => (string)i.Tag == key) is { } item) Tabs.SelectedItem = item;
    }

    readonly Library lib = App.Lib;
    Action onChanged = () => { };
    Action runUpdate = () => { };
    string tab = "quick";
    FullBackupInfo? backupInfo;
    string? zipPath;
    string? jobKind;        // "save" or "restore" while this window started the running job
    string jobDest = "";
    bool firstRestore;      // a backup restored on a fresh install is followed by an update, for what it did not hold
    bool wasRunning;
    readonly Action jobHandler;

    BackupsWindow()
    {
        InitializeComponent();
        WinKit.Setup(this, AppTitleBar, "Backups", 900, 860, Relocalise, "settings");
        Tabs.Items.Add(new SelectorBarItem { Text = Loc.T("Quick backup"), Tag = "quick" });
        Tabs.Items.Add(new SelectorBarItem { Text = Loc.T("Full backup"), Tag = "full" });
        Tabs.SelectedItem = Tabs.Items[0];
        WinKit.Direction(Root);
        Loc.Apply(Root);
        AppTitleBar.Title = Loc.T("Backups");
        UpdateBtn.Visibility = Visibility.Collapsed;
        SetOptionTexts();

        var dq = DispatcherQueue;
        jobHandler = () => dq.TryEnqueue(UpdateJobUi);
        lib.JobChanged += jobHandler;
        Closed += (_, _) =>
        {
            lib.JobChanged -= jobHandler;
            instance = null;
        };
        UpdateJobUi();
        _ = LoadAutoAsync();
        _ = LoadInfoAsync();
    }

    void Relocalise()
    {
        WinKit.Direction(Root);
        Loc.Apply(Root);
        Info.IsOpen = PersonalInfo.IsOpen = FullInfo.IsOpen = false;
        SetOptionTexts();
        UpdateJobUi();
        _ = LoadAutoAsync();
        _ = LoadInfoAsync();
        if (zipPath != null) _ = ShowZipAsync(zipPath);
    }

    void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem i) return;
        tab = (string)i.Tag;
        PersonalPanel.Visibility = tab == "quick" ? Visibility.Visible : Visibility.Collapsed;
        FullPanel.Visibility = tab == "full" ? Visibility.Visible : Visibility.Collapsed;
    }

    static bool IsExpected(Exception e) =>
        e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException
            or Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException or System.Runtime.InteropServices.COMException;

    // ---------- quick backup ----------
    async Task LoadAutoAsync()
    {
        try
        {
            var list = await Task.Run(lib.ListBackups);
            AutoList.ItemsSource = list.Select(f => new AutoBackupVm { F = f }).ToList();
            AutoEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception e) when (IsExpected(e))
        {
            WinKit.Info(Info, e.Message, InfoBarSeverity.Error);
        }
    }

    async void OnExportPersonal(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await WinKit.SaveFile(this, Path.GetFileNameWithoutExtension(Library.BackupFileName()), "MovieShelf backup", ".json");
            if (file == null) return;
            string path = file.Path;
            await Task.Run(() => lib.SaveBackupFile(path));
            WinKit.Info(PersonalInfo, $"{Loc.T("Saved")}: {path}", InfoBarSeverity.Success);
            UpdateBtn.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            WinKit.Info(PersonalInfo, ex.Message, InfoBarSeverity.Error);
            UpdateBtn.Visibility = Visibility.Collapsed;
        }
    }

    async void OnImportPersonal(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await WinKit.OpenFile(this, ".json");
            if (file != null) await ImportPersonalAsync(file.Path, true);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            WinKit.Info(PersonalInfo, ex.Message, InfoBarSeverity.Error);
            UpdateBtn.Visibility = Visibility.Collapsed;
        }
    }

    internal async Task ImportPersonalAsync(string path, bool ask)
    {
        try
        {
            if (ask && !await WinKit.Confirm(Root.XamlRoot, "Restore this backup?", "Favorites, ratings, notes and views from the backup are merged into your library. Nothing is deleted.", "Restore")) return;
            var counts = await Task.Run(() => lib.RestoreBackupFile(path));
            ShowRestored(counts);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            WinKit.Info(PersonalInfo, ex.Message, InfoBarSeverity.Error);
            UpdateBtn.Visibility = Visibility.Collapsed;
        }
    }

    async void OnRestoreAuto(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is AutoBackupVm v) await RestoreAutoAsync(v.F.Name, true);
    }

    internal async Task RestoreAutoAsync(string name, bool ask)
    {
        try
        {
            if (ask && !await WinKit.Confirm(Root.XamlRoot, "Restore this backup?", "Favorites, ratings, notes and views from the backup are merged into your library. Nothing is deleted.", "Restore")) return;
            var counts = await Task.Run(() => lib.RestoreBackup(name));
            ShowRestored(counts);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            WinKit.Info(PersonalInfo, ex.Message, InfoBarSeverity.Error);
            UpdateBtn.Visibility = Visibility.Collapsed;
        }
    }

    void ShowRestored(BackupCounts c)
    {
        string msg = string.Format(Loc.T("Restored {0} movie notes and flags, {1} manual matches, {2} ignored problems, {3} views and {4} wishlist items."),
            c.User.ToString("N0"), c.Manual.ToString("N0"), c.Dismissed.ToString("N0"), c.Views.ToString("N0"), c.Wishlist.ToString("N0"));
        if (c.NeedsUpdate)
        {
            WinKit.Info(PersonalInfo, msg + " " + Loc.T("The manual matches are applied by an update."), InfoBarSeverity.Warning, "An update is needed");
            UpdateBtn.Visibility = Visibility.Visible;
        }
        else
        {
            WinKit.Info(PersonalInfo, msg, InfoBarSeverity.Success);
            UpdateBtn.Visibility = Visibility.Collapsed;
        }
        onChanged();
        WishlistWindow.NotifyChanged();
    }

    void OnUpdateNow(object sender, RoutedEventArgs e)
    {
        PersonalInfo.IsOpen = false;
        try { runUpdate(); }
        catch (Exception ex) when (IsExpected(ex)) { WinKit.Info(Info, ex.Message, InfoBarSeverity.Error); }
    }

    // ---------- full backup: save ----------
    // The texts with sizes are set once the numbers are known (LoadInfoAsync).
    void SetOptionTexts()
    {
        SvSettings.Content = Loc.T("All settings (list folder, movie folders, update options)");
        SvKeys.Content = Loc.T("API keys (TMDB, OMDb)");
        RsLibrary.Content = Loc.T("The library (replaces your current one)");
        RsSettings.Content = Loc.T("Settings");
        RsKeys.Content = Loc.T("API keys");
        RsLists.Content = Loc.T("List files");
        RsPosters.Content = Loc.T("Posters");
        RsImdb.Content = Loc.T("IMDb data (large; replaces the downloaded data)");
    }

    async Task LoadInfoAsync()
    {
        try
        {
            backupInfo = await Task.Run(lib.GetFullBackupInfo);
        }
        catch (Exception e) when (IsExpected(e))
        {
            WinKit.Info(Info, e.Message, InfoBarSeverity.Error);
            return;
        }
        var i = backupInfo;
        SvDb.Content = string.Format(Loc.T("The library: every movie, match, detail, score, your favorites, watched flags, ratings, notes, saved views and wishlist ({0})"), WinKit.Size(i.Db));
        SvKeys.IsEnabled = i.Keys;
        SvKeys.IsChecked = i.Keys;
        SvKeysNote.Text = Loc.T(i.Keys ? "Stored in the file as plain text. Keep the backup private." : "No API keys are set.");
        SvLists.IsEnabled = i.Lists.N > 0;
        SvLists.IsChecked = i.Lists.N > 0;
        SvLists.Content = string.Format(Loc.T("Your list files ({0}, {1})"), WinKit.Count(i.Lists.N, "{0} file", "{0} files"), WinKit.Size(i.Lists.Bytes));
        SvPosters.IsEnabled = i.Posters.N > 0;
        SvPosters.IsChecked = i.Posters.N > 0;
        SvPosters.Content = string.Format(Loc.T("Posters and person photos ({0}, {1})"), WinKit.Count(i.Posters.N, "{0} file", "{0} files"), WinKit.Size(i.Posters.Bytes));
        SvImdb.IsEnabled = i.Imdb.N > 0;
        SvImdb.IsChecked = false;
        SvImdb.Content = string.Format(Loc.T("IMDb data ({0})"), WinKit.Size(i.Imdb.Bytes));
        SvImdbNote.Text = Loc.T("Can be downloaded again, so it is off by default. Tick it to restore without downloading it.");
        OnSaveOptionChanged(this, new RoutedEventArgs());
    }

    FullBackupOptions SaveOptions() => new()
    {
        Settings = true,
        Keys = SvKeys.IsEnabled && SvKeys.IsChecked == true,
        Lists = SvLists.IsEnabled && SvLists.IsChecked == true,
        Posters = SvPosters.IsEnabled && SvPosters.IsChecked == true,
        Imdb = SvImdb.IsEnabled && SvImdb.IsChecked == true,
    };

    void OnSaveOptionChanged(object sender, RoutedEventArgs e)
    {
        if (backupInfo == null) return;
        var o = SaveOptions();
        long total = backupInfo.Db + (o.Lists ? backupInfo.Lists.Bytes : 0) + (o.Posters ? backupInfo.Posters.Bytes : 0) + (o.Imdb ? backupInfo.Imdb.Bytes : 0);
        SvSummary.Text = string.Format(Loc.T("About {0} before compression"), WinKit.Size(total));
    }

    async void OnSaveFull(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await WinKit.SaveFile(this, $"MovieShelf full backup {DateTime.Now:yyyy-MM-dd}", "Full backup (.zip)", ".zip");
            if (file != null) SaveFull(file.Path);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            WinKit.Info(Info, ex.Message, InfoBarSeverity.Error);
        }
    }

    internal void SaveFull(string path)
    {
        try
        {
            var (started, dest) = lib.StartSaveFullBackup(path, SaveOptions());
            if (!started)
            {
                WinKit.Info(Info, Loc.T("Another update is running. Try again when it has finished."), InfoBarSeverity.Warning);
                return;
            }
            Info.IsOpen = false;
            jobKind = "save";
            jobDest = dest;
            UpdateJobUi();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            WinKit.Info(Info, ex.Message, InfoBarSeverity.Error);
        }
    }

    // ---------- full backup: restore ----------
    async void OnPickZip(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await WinKit.OpenFile(this, ".zip");
            if (file != null) await ShowZipAsync(file.Path);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            WinKit.Info(FullInfo, ex.Message, InfoBarSeverity.Error);
        }
    }

    internal async Task ShowZipAsync(string path)
    {
        try
        {
            FullInfo.IsOpen = false;
            var m = await Task.Run(() => Library.InspectFullBackup(path));
            zipPath = path;
            ZipPath.Text = path;
            ZipTitle.Text = string.Format(Loc.T("Made {0}"), m.Created);
            ZipDetail.Text = string.Join(" · ", new[]
            {
                WinKit.Size(m.Size), WinKit.Count(m.Has.Lists, "{0} list file", "{0} list files"), WinKit.Count(m.Has.Posters, "{0} poster", "{0} posters"),
                Loc.T(m.Has.Imdb > 0 ? "IMDb data included" : "no IMDb data"), Loc.T(m.HasKeys ? "API keys included" : "API keys not included"),
            });
            Enable(RsSettings, m.HasSettings);
            Enable(RsKeys, m.HasKeys);
            Enable(RsLists, m.Has.Lists > 0);
            Enable(RsPosters, m.Has.Posters > 0);
            Enable(RsImdb, m.Has.Imdb > 0);
            RestoreOptions.Visibility = Visibility.Visible;
            RestoreFullBtn.IsEnabled = !lib.Job.Running;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            zipPath = null;
            ZipPath.Text = "";
            RestoreOptions.Visibility = Visibility.Collapsed;
            RestoreFullBtn.IsEnabled = false;
            WinKit.Info(FullInfo, ex.Message, InfoBarSeverity.Error);
        }
    }

    static void Enable(CheckBox c, bool has)
    {
        c.IsEnabled = has;
        c.IsChecked = has;
    }

    static bool On(CheckBox c) => c.IsEnabled && c.IsChecked == true;

    async void OnRestoreFull(object sender, RoutedEventArgs e) => await RestoreFullAsync(true);

    internal async Task RestoreFullAsync(bool ask)
    {
        if (zipPath == null) return;
        try
        {
            bool first = lib.FirstRun;
            if (ask && !first && !await WinKit.Confirm(Root.XamlRoot, "Replace your library with this backup?",
                    "Your current library and the parts you ticked are replaced. A safety copy is saved first, so this can be undone.", "Restore")) return;
            var opts = new FullBackupOptions { Settings = On(RsSettings), Keys = On(RsKeys), Lists = On(RsLists), Posters = On(RsPosters), Imdb = On(RsImdb) };
            if (!lib.StartRestoreFullBackup(zipPath, opts))
            {
                WinKit.Info(Info, Loc.T("Another update is running. Try again when it has finished."), InfoBarSeverity.Warning);
                return;
            }
            Info.IsOpen = false;
            FullInfo.IsOpen = false;
            firstRestore = first;
            jobKind = "restore";
            UpdateJobUi();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            WinKit.Info(FullInfo, ex.Message, InfoBarSeverity.Error);
        }
    }

    // ---------- job progress ----------
    void UpdateJobUi()
    {
        var j = lib.Job;
        JobPanel.Visibility = j.Running ? Visibility.Visible : Visibility.Collapsed;
        if (j.Running)
        {
            JobText.Text = Loc.T(j.Stage) + (j.Total > 0 ? $"  {j.Done:N0} / {j.Total:N0}" : "");
            JobBar.IsIndeterminate = j.Total == 0;
            JobBar.Maximum = Math.Max(j.Total, 1);
            JobBar.Value = j.Done;
        }
        StopBtn.Visibility = j.Stoppable ? Visibility.Visible : Visibility.Collapsed;
        StopBtn.IsEnabled = j.Running && !j.Stopping && j.Stoppable;
        bool idle = !j.Running;
        ImportBtn.IsEnabled = idle;
        AutoList.IsEnabled = idle;
        SaveFullBtn.IsEnabled = idle;
        PickZipBtn.IsEnabled = idle;
        RestoreFullBtn.IsEnabled = idle && zipPath != null;
        if (wasRunning && idle && jobKind != null) Finished(j);
        wasRunning = j.Running;
    }

    void Finished(JobState j)
    {
        string kindDone = jobKind!;
        jobKind = null;
        if (kindDone == "save")
        {
            if (j.Error != null) WinKit.Info(Info, j.Error, InfoBarSeverity.Error);
            else if (j.Stopped)
            {
                WinKit.Info(Info, Loc.T("Stopped. No backup file was written."), InfoBarSeverity.Informational);
                _ = DropEmptyFile(jobDest);
            }
            else WinKit.Info(Info, string.Format(Loc.T("Full backup saved: {0}"), jobDest), InfoBarSeverity.Success);
            _ = LoadInfoAsync();
            return;
        }
        if (j.Error != null)
        {
            WinKit.Info(Info, j.Error, InfoBarSeverity.Error);
        }
        else
        {
            var r = lib.RestoreResult;
            var lines = new List<string>();
            if (r != null)
            {
                if (r.Warnings.Count > 0) lines.AddRange(r.Warnings);
                if (r.Safety.Length > 0) lines.Add(string.Format(Loc.T("A copy of the previous state is in {0}"), r.Safety));
            }
            if (r is { Warnings.Count: > 0 }) WinKit.Info(Info, string.Join("\n", lines), InfoBarSeverity.Warning, "Restored, with notes");
            else WinKit.Info(Info, string.Join("\n", lines), InfoBarSeverity.Success, "Backup restored");
        }
        _ = LoadInfoAsync();
        _ = LoadAutoAsync();
        onChanged();
        WishlistWindow.NotifyChanged();
        if (firstRestore && j.Error == null) runUpdate();
        firstRestore = false;
    }

    /// <summary>The save picker leaves an empty file behind; a backup that was stopped does not need it.</summary>
    static Task DropEmptyFile(string path) => Task.Run(() =>
    {
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length == 0) File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    });

    void OnStop(object sender, RoutedEventArgs e)
    {
        lib.Stop();
        UpdateJobUi();
    }
}
