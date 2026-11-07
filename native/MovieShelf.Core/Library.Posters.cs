namespace MovieShelf.Core;

// Posters added by hand. Every message here is for the user: the UI shows ArgumentException.Message.
public sealed partial class Library
{
    public const int PosterMax = 15_000_000;   // bytes: the biggest poster image accepted by hand

    static bool HasBytes(ReadOnlySpan<byte> data, int at, ReadOnlySpan<byte> sig) =>
        data.Length >= at + sig.Length && data.Slice(at, sig.Length).SequenceEqual(sig);

    /// <summary>The media type of a JPEG, PNG, WebP or GIF image from its first bytes, or null for anything else.</summary>
    public static string? ImageType(ReadOnlySpan<byte> data)
    {
        if (HasBytes(data, 0, new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
        if (HasBytes(data, 0, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', (byte)'\r', (byte)'\n', 0x1A, (byte)'\n' })) return "image/png";
        if (HasBytes(data, 0, "RIFF"u8) && HasBytes(data, 8, "WEBP"u8)) return "image/webp";
        if (HasBytes(data, 0, "GIF87a"u8) || HasBytes(data, 0, "GIF89a"u8)) return "image/gif";
        return null;
    }

    /// <summary>Image bytes from what the user gave: a web address or the path of an image file on this PC. Blocking for a web address.</summary>
    public byte[] PosterBytes(string? source)
    {
        string src = (source ?? "").Trim().Trim('"');
        if (src.Length == 0) throw new ArgumentException("Choose an image file, or paste a web address or a file path.");
        byte[] data;
        if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try { data = Net.GetBytesAsync(src).GetAwaiter().GetResult(); }
            catch (Exception e)
            {
                throw new ArgumentException($"The address could not be opened: {e.Message}");
            }
        }
        else
        {
            if (src.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) src = src[8..].Replace('/', Path.DirectorySeparatorChar);
            if (!File.Exists(src)) throw new ArgumentException("That is not a web address (http...) or a file on this PC.");
            if (new FileInfo(src).Length > PosterMax) throw new ArgumentException("The image is larger than 15 MB.");
            data = File.ReadAllBytes(src);
        }
        if (ImageType(data) == null)
            throw new ArgumentException("That is not an image (JPEG, PNG, WebP or GIF). For a web picture, copy the image address, not the page address.");
        return data;
    }

    /// <summary>Uses these image bytes as the poster of a movie. A later poster download never replaces it.</summary>
    public void SetPoster(int tconst, byte[]? data)
    {
        lock (Lock)
            if (Scalar<int>("SELECT count(*) FROM titles WHERE tconst=?", tconst) == 0)
                throw new ArgumentException("This movie has no IMDb match yet, so it cannot have a poster.");
        if (data == null || data.Length == 0) throw new ArgumentException("The image is empty.");
        if (data.Length > PosterMax) throw new ArgumentException("The image is larger than 15 MB.");
        if (ImageType(data) == null) throw new ArgumentException("That is not an image (JPEG, PNG, WebP or GIF).");
        string path = PosterPath(tconst);
        File.WriteAllBytes(path + ".tmp", data);
        File.Move(path + ".tmp", path, overwrite: true);
        lock (Lock) Exec("UPDATE titles SET poster=?, poster_tried=1 WHERE tconst=?", Path.GetFileName(path), tconst);
        Version++;
        DataChanged?.Invoke();
    }

    /// <summary>Sets a movie's poster from a web address or a file path (<see cref="PosterBytes"/>, then <see cref="SetPoster(int, byte[])"/>).</summary>
    public void SetPosterFrom(int tconst, string? source) => SetPoster(tconst, PosterBytes(source));

    /// <summary>Removes the poster of a movie. It is fetched again only by "Retry missing posters" (<c>FetchPosters(retry: true)</c>).</summary>
    public void ClearPoster(int tconst)
    {
        try { File.Delete(PosterPath(tconst)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        lock (Lock) Exec("UPDATE titles SET poster=NULL, poster_tried=1 WHERE tconst=?", tconst);
        Version++;
        DataChanged?.Invoke();
    }
}
