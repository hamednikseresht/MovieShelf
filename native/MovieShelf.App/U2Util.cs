using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinRT.Interop;

namespace MovieShelf.App;

/// <summary>Small helpers shared by the CheckWindow, PersonWindow, RenamePlanWindow and UnlistedWindow.</summary>
internal static class U2
{
    /// <summary>Title bar, backdrop, size and icon, the same way ProblemsWindow sets them up. See <see cref="WinKit.Setup"/> for the last two.</summary>
    public static void Setup(Window w, TitleBar bar, string title, int width, int height, Action? relocalise = null, string? help = null) =>
        WinKit.Setup(w, bar, title, width, height, relocalise, help);

    public static void InitPicker(object picker, Window w) => InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(w));

    public static FlowDirection Flow => Loc.IsFa ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    static readonly Dictionary<(string Key, bool Dark), Brush> brushes = new();

    /// <summary>A theme brush for the theme the app shows now. A resource read from the application follows Windows, and the app's theme can differ from it.</summary>
    public static Brush Res(string key)
    {
        bool dark = Dark;
        if (brushes.TryGetValue((key, dark), out var b)) return b;
        var holder = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            $"<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' RequestedTheme='{(dark ? "Dark" : "Light")}' Background='{{ThemeResource {key}}}'/>");
        return brushes[(key, dark)] = holder.Background;
    }

    public static bool Dark => Chrome.IsDark;

    static Color C(byte r, byte g, byte b, byte a = 255) => Color.FromArgb(a, r, g, b);

    /// <summary>Text and tinted background colours of a status badge, readable in both themes.</summary>
    public static (SolidColorBrush Fg, SolidColorBrush Bg) Badge(string status)
    {
        bool d = Dark;
        Color fg = status switch
        {
            "new" => d ? C(0x6C, 0xCB, 0x5F) : C(0x0F, 0x7B, 0x0F),
            "better" => d ? C(0x60, 0xCD, 0xFF) : C(0x00, 0x5F, 0xB8),
            "version" => d ? C(0xC4, 0xA5, 0xFF) : C(0x6B, 0x3F, 0xC4),
            "worse" => d ? C(0xFF, 0xB4, 0x54) : C(0x9D, 0x5D, 0x00),
            "unmatched" => d ? C(0xFF, 0x99, 0xA4) : C(0xC4, 0x2B, 0x1C),
            _ => d ? C(0xB0, 0xB0, 0xB0) : C(0x5A, 0x5A, 0x5A),
        };
        return (new SolidColorBrush(fg), new SolidColorBrush(Color.FromArgb(0x2A, fg.R, fg.G, fg.B)));
    }

    /// <summary>1.9 GB, 450 MB, 12 KB.</summary>
    public static string Size(long? bytes)
    {
        if (bytes is not long b || b <= 0) return "";
        if (b >= 1_000_000_000) return $"{b / 1e9:0.0} GB";
        if (b >= 1_000_000) return $"{b / 1e6:0} MB";
        return $"{Math.Max(1, b / 1000)} KB";
    }

    public static string Votes(int? v) => v is not int n || n <= 0 ? "" : n >= 1_000_000 ? $"{n / 1e6:0.0}M" : n >= 1000 ? $"{n / 1000}k" : n.ToString();

    public static Visibility Vis(bool v) => v ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Opens a web address in the default browser. Only http(s) is allowed.</summary>
    public static async void Launch(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp))
                await Windows.System.Launcher.LaunchUriAsync(u);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException) { }
    }

    public static string ImdbTitleUrl(int tconst) => $"https://www.imdb.com/title/tt{tconst:0000000}/";

    /// <summary>Formats a template that came through Loc.T, e.g. Fmt("Add {0} selected to library", 3).</summary>
    public static string Fmt(string template, params object[] args) => string.Format(Loc.T(template), args);

    /// <summary>The core's progress stages are English text; translate them, including 'Reading folder (200 videos)'.</summary>
    public static string Stage(string stage)
    {
        var m = System.Text.RegularExpressions.Regex.Match(stage, @"^(Reading folder|Reading movie folders) \((\d+) videos\)$");
        if (m.Success) return $"{Loc.T(m.Groups[1].Value)} ({m.Groups[2].Value} {Loc.T("videos")})";
        m = System.Text.RegularExpressions.Regex.Match(stage, @"^Downloading posters \((\d+) saved\)$");
        if (m.Success) return Fmt("Downloading posters ({0} saved)", m.Groups[1].Value);
        m = System.Text.RegularExpressions.Regex.Match(stage, @"^Downloading (\S+)(.*)$");
        if (m.Success) return Fmt("Downloading {0}", m.Groups[1].Value) + m.Groups[2].Value;
        m = System.Text.RegularExpressions.Regex.Match(stage, @"^(.+) (\(\d+[kM]\))$");
        return m.Success ? Loc.T(m.Groups[1].Value) + " " + m.Groups[2].Value : Loc.T(stage);
    }

    /// <summary>The check job's error text is 'ValueError: No movies found...'; show the message without the Python-style exception name, translated.</summary>
    public static string CheckError(string err)
    {
        var m = System.Text.RegularExpressions.Regex.Match(err, @"^\w+(Error|Exception): (.*)$", System.Text.RegularExpressions.RegexOptions.Singleline);
        string msg = m.Success ? m.Groups[2].Value : err;
        return msg.StartsWith("Not found: ") ? Loc.T("Not found") + ": " + msg["Not found: ".Length..] : Loc.T(msg);
    }

    /// <summary>A long path cut at the front, so the part that tells files apart (folder and file name) stays visible.</summary>
    public static string ShortPath(string path, int max = 80) => path.Length <= max ? path : "…" + path[^(max - 1)..];

    public static async Task WriteAsync(string path, byte[] data) => await Task.Run(() => File.WriteAllBytes(path, data));

    /// <summary>A card used for the small number tiles (count over caption).</summary>
    public static Border Tile(string number, string caption)
    {
        var sp = new StackPanel { Spacing = 0 };
        sp.Children.Add(new TextBlock { Text = number, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        sp.Children.Add(new TextBlock { Text = caption, FontSize = 12, Foreground = Res("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Child = sp, Padding = new Thickness(14, 8, 14, 8), CornerRadius = new CornerRadius(6), MinWidth = 120,
            Background = Res("CardBackgroundFillColorDefaultBrush"), BorderBrush = Res("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(1),
        };
    }
}
