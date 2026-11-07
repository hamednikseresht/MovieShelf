using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace MovieShelf.Core;

public sealed partial class Library
{
    static readonly string[] OldTempFiles = { "instance.json", "setup.log", "movieshelf.log", "fa_latin.json" };   // temporary or no longer used

    /// <summary>Moves a data\ folder of an older version out of the project folder to &lt;root&gt;\data unless a library is there; call it before opening the Library. True when it moved.</summary>
    public static bool MigrateOldData(string projectDir, string root)
    {
        string old = Path.GetFullPath(Path.Combine(projectDir, "data")), data = Path.GetFullPath(Path.Combine(root, "data"));
        if (string.Equals(old, data, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(old, "library.db")) || File.Exists(Path.Combine(data, "library.db")))
            return false;
        if (OldAppRunning(old)) throw new InvalidOperationException("MovieShelf is open. Close it so your data can be moved.");
        Directory.CreateDirectory(data);
        foreach (var entry in Directory.GetFileSystemEntries(old))
        {
            string name = Path.GetFileName(entry), dest = Path.Combine(data, name);
            if (OldTempFiles.Contains(name)) continue;
            if (Directory.Exists(dest) && !Directory.EnumerateFileSystemEntries(dest).Any()) Directory.Delete(dest);   // an empty folder the app made already
            if (!Path.Exists(dest)) MoveEntry(entry, dest);
        }
        foreach (var name in OldTempFiles)
        {
            try { File.Delete(Path.Combine(old, name)); }
            catch (IOException) { }
        }
        Directory.Delete(old);
        return true;
    }

    /// <summary>A rename when it is the same drive, so even gigabytes take a second; a copy when it is not.</summary>
    static void MoveEntry(string src, string dest)
    {
        if (!Directory.Exists(src))
        {
            File.Move(src, dest);
            return;
        }
        try { Directory.Move(src, dest); }
        catch (IOException)
        {
            Directory.CreateDirectory(dest);
            foreach (var e in Directory.GetFileSystemEntries(src)) MoveEntry(e, Path.Combine(dest, Path.GetFileName(e)));
            Directory.Delete(src);
        }
    }

    static bool OldAppRunning(string dataDir)
    {
        try
        {
            int port = (int)JsonNode.Parse(File.ReadAllText(Path.Combine(dataDir, "instance.json")))!["port"]!;
            using var tcp = new TcpClient();
            return tcp.ConnectAsync("127.0.0.1", port).Wait(500);
        }
        catch (Exception) { return false; }
    }

    /// <summary>First-run choice "start fresh": a movie folder and/or a list file or folder is the source of movies; one of the two is enough.
    /// The list is loaded (as Load list, mode "add"), the movie folder is saved, and settings.json is written (this ends <see cref="FirstRun"/>).
    /// The caller then starts the update job (the Python start_fresh does that itself). Throws InvalidOperationException when already set up,
    /// ArgumentException (message for the user) when neither is given or the folder is not there.</summary>
    public void StartFresh(string? listPath = null, string? mediaDir = null)
    {
        if (!FirstRun) throw new InvalidOperationException("MovieShelf is already set up.");
        string? m = string.IsNullOrWhiteSpace(mediaDir) ? null : Path.GetFullPath(mediaDir.Trim().Trim('"'));
        string lp = (listPath ?? "").Trim().Trim('"');
        if (m != null && !Directory.Exists(m)) throw new ArgumentException($"{m} is not a folder.");
        if (m == null && lp.Length == 0) throw new ArgumentException("Give a movie folder, a list (.txt or .csv), or both: the library is built from them.");
        if (lp.Length > 0) LoadList(new ListSource(lp), "add", rescan: false);
        var s = LoadSettings();
        if (m != null) s.MediaDirs = new List<string> { m };
        SaveSettings(s);   // also marks the start as chosen
    }
}
