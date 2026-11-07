using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using MovieShelf.Core;

namespace MovieShelf.App;

public partial class App : Application
{
    public static Library Lib { get; private set; } = null!;
    public static MainWindow Main { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(Library.DefaultRoot(), "native.log"), $"{DateTime.Now:s} {e.Exception}\n");
            }
            catch (IOException) { }
        };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    /// <summary>Stays visible with no window open yet.</summary>
    static void Message(string text) => MessageBoxW(IntPtr.Zero, text, "MovieShelf", 0x10);

    // One program per data folder, as run.py checks: two would run their updates over each other. A second start brings the first window up.
    static async Task<bool> FirstInstance(string root)
    {
        if (Environment.GetEnvironmentVariable("MOVIESHELF_SHOT") is { Length: > 0 } || Environment.GetEnvironmentVariable("MOVIESHELF_PERF") is { Length: > 0 }) return true;
        var key = AppInstance.FindOrRegisterForKey("MovieShelf " + root.ToUpperInvariant());
        if (key.IsCurrent)
        {
            key.Activated += (_, _) => Main?.DispatcherQueue.TryEnqueue(() => Main.Activate());
            return true;
        }
        await key.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
        return false;
    }

    // Older versions kept data\ in the project folder, and the built program sits in native\dist below it.
    static string ProjectDir() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string root = Library.DefaultRoot();
        try
        {
            if (!await FirstInstance(root))
            {
                Process.GetCurrentProcess().Kill();
                return;
            }
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.Combine(root, "data"));
                // only from a real Python project folder: an installed app has unrelated folders above it
                if (Directory.Exists(Path.Combine(ProjectDir(), "app", "movieshelf"))) Library.MigrateOldData(ProjectDir(), root);
            });
            Lib = new Library(root);
            Loc.IsFa = Lib.LoadSettings().Language == "fa";
            Main = new MainWindow();
            Main.Activate();
        }
        catch (Exception e)
        {
            Message($"MovieShelf could not start:\n\n{e.GetType().Name}: {e.Message}");
            Exit();
        }
    }
}
