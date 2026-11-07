using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MovieShelf.Core;

namespace MovieShelf.App;

/// <summary>A movie as the grid and the list show it. One item lives as long as its movie, so a favourite or a watched mark changes the card in place.</summary>
public sealed class MovieItem : INotifyPropertyChanged
{
    public Movie M { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The width of a card; MainWindow sets it from the poster size.</summary>
    public static double CardWidth = 172;

    /// <summary>Pixel width posters are decoded at; MainWindow scales the card width to the display.</summary>
    public static int PosterWidth = 172;

    /// <summary>Movies whose poster file changed while the app runs. The image cache knows files by name, so these skip it.</summary>
    public static readonly HashSet<string> NewPosters = new();

    public MovieItem(Movie m) => M = m;

    /// <summary>Tells the card and the row that the marks they show changed.</summary>
    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavVis)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WatchedVis)));
    }

    static string Run(int? minutes) => minutes == null ? "" : minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m";
    static Visibility Vis(bool v) => v ? Visibility.Visible : Visibility.Collapsed;

    public double CardW => CardWidth;
    public double CardH => CardWidth * 1.5;
    public string Title => M.Display;
    public string Sub => M.Orig != null && Movie.Fold(M.Orig) != Movie.Fold(M.Display) ? M.Orig : M.FaTitle ?? "";
    public string? SubTip => Sub.Length > 0 ? Sub : null;
    public string Meta => string.Join(" · ", new[]
    {
        M.YearSpan, M.IsSeries && M.Runtime != null ? Run(M.Runtime) + " / ep" : Run(M.Runtime), string.Join(", ", M.Genres.Take(2)),
    }.Where(x => !string.IsNullOrEmpty(x)));
    public string RatingText => M.Rating?.ToString("0.0") ?? "";
    public Visibility RatingVis => Vis(M.Rating != null);
    public string ResText => M.Res == "2160p" ? "4K" : M.Res ?? "";
    public Visibility ResVis => Vis(M.Res != null);
    public Visibility FlagVis => Vis(M.NeedsReview);
    public Visibility FaVis => Vis(M.Files.Any(f => f.Fa));
    public string ScoreText => string.Join("  ", new[] { M.Rt != null ? $"RT {M.Rt}%" : null, M.Mc != null ? $"MC {M.Mc}" : null }.Where(x => x != null));
    public Visibility ScoreVis => Vis(M.Rt != null || M.Mc != null);
    public Visibility FavVis => Vis(M.Fav);
    public Visibility WatchedVis => Vis(M.Watched);
    public Visibility FilesVis => Vis(M.Files.Count > 1);
    public string FilesText => $"{M.Files.Count} {Loc.T("files")}";
    public Visibility NoPosterVis => Vis(!M.HasPoster);
    public string Placeholder => M.Year != null ? $"{M.Display}\n{M.Year}" : M.Display;
    public string Year => M.YearSpan;
    public string Votes => M.Votes == 0 ? "" : M.Votes >= 1_000_000 ? $"{M.Votes / 1e6:0.0}M" : M.Votes >= 1000 ? $"{M.Votes / 1000}k" : M.Votes.ToString();
    public string RtText => M.Rt == null ? "" : M.Rt + "%";
    public string McText => M.Mc?.ToString() ?? "";
    public string MinText => M.Runtime is > 0 ? M.Runtime.ToString()! : "";
    public string GenreText => string.Join(", ", M.Genres);
    public string Directors => string.Join(", ", M.Dir.Select(p => p.Name));
    public string QualityText => M.Files.Count == 0 ? "" : string.Join(' ', new[] { M.Res, M.Files[0].Src, M.Files[0].Codec }.Where(x => !string.IsNullOrEmpty(x))) + (M.Files.Count > 1 ? $"  +{M.Files.Count - 1}" : "");

    ImageSource? Image(int width)
    {
        if (!M.HasPoster) return null;
        var img = new BitmapImage { DecodePixelWidth = width };
        if (NewPosters.Contains(M.K)) img.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        img.UriSource = new Uri(App.Lib.PosterPath(M));
        return img;
    }

    // not kept here: the grid asks again when a card comes back into view, so only the visible posters stay in memory
    public ImageSource? Poster => Image(PosterWidth);
    public ImageSource? RowPoster => Image(PosterWidth / 3);
}

/// <summary>One choice in a filter group. It holds no state of its own: it is checked when its key is in the filter's set.</summary>
public sealed class FilterOption : INotifyPropertyChanged
{
    readonly HashSet<string> set;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The user checked or unchecked an option.</summary>
    public static event Action? Toggled;

    public FilterOption(HashSet<string> set, string key, string label, int? count)
    {
        this.set = set;
        Key = key;
        Label = label;
        Count = count;
    }

    public string Key { get; }
    public string Label { get; private set; }
    public int? Count { get; private set; }

    public bool IsChecked
    {
        get => set.Contains(Key);
        set
        {
            if (value ? set.Add(Key) : set.Remove(Key)) Toggled?.Invoke();
        }
    }

    public string Text => Count != null ? $"{Loc.T(Label)}  ({Count:N0})" : Loc.T(Label);

    /// <summary>New counts in place, so a change to one movie does not rebuild the filter panel.</summary>
    public void Update(string label, int? count)
    {
        Label = label;
        Count = count;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
    }
}

/// <summary>A group of movies under a heading. A closed group keeps its heading and count but no items.</summary>
public sealed class MovieGroup : List<MovieItem>
{
    public string Label { get; }
    public int Total { get; }
    public bool Closed { get; }
    public string TotalText => Total.ToString("N0");
    public string Chevron => Closed ? (Loc.IsFa ? "\uE76B" : "\uE76C") : "\uE70D";

    public MovieGroup(string label, IEnumerable<MovieItem> items, int total, bool closed) : base(items)
    {
        Label = label;
        Total = total;
        Closed = closed;
    }
}

/// <summary>One person in the cast of the movie pane. The photo is only asked for when the row comes into view.</summary>
public sealed class CastItem
{
    readonly string? img;
    readonly Action open;

    public CastItem(Person p, string? img, Action open)
    {
        Name = p.Name;
        Role = p.Role ?? "";
        this.img = img;
        this.open = open;
    }

    public string Name { get; }
    public string Role { get; }
    public string Tip => U2.Fmt("Open the profile of {0}", Name);
    public string Initials => img != null ? "" : string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w[0]));
    public ImageSource? Photo => Uri.TryCreate(img, UriKind.Absolute, out var u) ? new BitmapImage { DecodePixelWidth = 80, UriSource = u } : null;
    public void Open() => open();
}
