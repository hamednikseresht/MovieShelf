using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace MovieShelf.App;

/// <summary>What every window shares: the backdrop, the theme and the list of open windows the language change reaches.</summary>
internal static class Chrome
{
    sealed record Entry(Window Window, Action? Relocalise);

    static readonly List<Entry> Open = new();
    static readonly UISettings System = new();
    static bool watching;

    /// <summary>"system", "light" or "dark".</summary>
    public static string Theme { get; private set; } = "system";

    public static bool IsDark => Theme == "dark" || (Theme != "light" && System.GetColorValue(UIColorType.Background).R < 128);

    /// <summary>Call once the window has its content. <paramref name="relocalise"/> redraws the window's text after a language change.</summary>
    public static void Attach(Window w, Action? relocalise = null)
    {
        if (MicaController.IsSupported()) w.SystemBackdrop = new MicaBackdrop();
        Open.Add(new Entry(w, relocalise));
        w.Closed += (_, _) => Open.RemoveAll(e => e.Window == w);
        Paint(w);
        if (watching) return;
        watching = true;
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        System.ColorValuesChanged += (_, _) => queue.TryEnqueue(() =>
        {
            if (Theme == "system") foreach (var e in Open) Paint(e.Window);
        });
    }

    public static void SetTheme(string theme)
    {
        Theme = theme is "light" or "dark" ? theme : "system";
        foreach (var e in Open) Paint(e.Window);
    }

    /// <summary>Redraws every open window after <see cref="Loc.IsFa"/> changed.</summary>
    public static void Relocalise()
    {
        foreach (var e in Open.ToList()) e.Relocalise?.Invoke();
    }

    static void Paint(Window w)
    {
        if (w.Content is not FrameworkElement root) return;
        root.RequestedTheme = Theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        bool dark = IsDark;
        // Windows 10 has no Mica, so the window needs a background of its own
        if (!MicaController.IsSupported() && root is Panel panel)
            panel.Background = new SolidColorBrush(dark ? Color.FromArgb(255, 0x20, 0x20, 0x20) : Color.FromArgb(255, 0xF3, 0xF3, 0xF3));
        // the caption buttons follow the Windows setting unless told otherwise, and the app's theme can differ from it
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var bar = w.AppWindow.TitleBar;
            bar.ButtonBackgroundColor = Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
            bar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
            bar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
            bar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(0x20, 255, 255, 255) : Color.FromArgb(0x20, 0, 0, 0);
            bar.ButtonInactiveForegroundColor = dark ? Colors.Gray : Colors.DimGray;
        }
    }
}
