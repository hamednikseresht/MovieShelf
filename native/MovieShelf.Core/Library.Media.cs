using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace MovieShelf.Core;

/// <summary>A video found in the movie folders that is in no list.</summary>
public sealed record UnlistedItem(string Path, string Raw, long? Size);

/// <summary>How far the ffprobe reading is: ffprobe path (null = not found), files in the movie folders, files read, files ffprobe could not read.</summary>
public sealed record MediaProbeStatus(string? Ffprobe, int Files, int Read, int Failed);

/// <summary>An item to add from the list check: the name, and (only when the user picked it) the IMDb id (null = not on IMDb).</summary>
public sealed record AddItem(string Raw, bool HasChoice = false, int? Choice = null);

/// <summary>ffprobe facts of one scanned file, as stored in mediainfo.</summary>
public sealed record MediaRow(int? Duration, int? Width, int? Height, string? VCodec, long? Bitrate, string[] Audio, string[] Subs);

public sealed partial class Library
{
    // ---------- movie folders ----------
    /// <summary>Find the real video files in the movie folders (settings MediaDirs): path and size for every list entry, and the videos that
    /// are in no list (they become library entries, see <see cref="FoldersToLibrary"/>). Read-only on disk; a job (lib.Start(lib.ScanMedia)).</summary>
    public void ScanMedia()
    {
        var dirs = LoadSettings().MediaDirs.Where(d => d.Length > 0 && Directory.Exists(d)).ToList();
        if (dirs.Count == 0)   // a drive that is away: the movies from it stay as they are
        {
            Log("The movie folders cannot be read now (is the drive connected?)");
            return;
        }
        var wanted = new Dictionary<string, string>();
        using (var rd = Reader())
        using (var c = Cmd(rd, "SELECT raw FROM files WHERE present=1", Array.Empty<object?>()))
        using (var r = c.ExecuteReader())
            while (r.Read()) wanted[r.GetString(0).ToLowerInvariant()] = r.GetString(0);
        var removed = new HashSet<string>();
        using (var rd = Reader())
        using (var c = Cmd(rd, "SELECT raw FROM removed", Array.Empty<object?>()))
        using (var r = c.ExecuteReader())
            while (r.Read()) removed.Add(r.GetString(0).ToLowerInvariant());
        var found = new List<(string Path, string Raw, long Size, double MTime)>();
        var unlisted = new List<(string Path, string Raw, long Size, double MTime)>();
        int n = 0;
        var noVideo = new NoVideoScan(wanted);
        foreach (var root in dirs)
            foreach (var (dirpath, dirnames, names) in FolderUtil.Walk(root))
            {
                dirnames.RemoveAll(d => d.StartsWith('$') || d.StartsWith('.'));
                string parent = FolderUtil.BaseName(dirpath);
                noVideo.Folder(root, dirpath);
                foreach (var name in names)
                {
                    var (stem, ext) = FolderUtil.SplitExt(name);
                    if (!Checker.VideoExt.Contains(ext.ToLowerInvariant())) continue;
                    n++;
                    if (n % 200 == 0) Progress($"Reading movie folders ({n} videos)", n, 0);
                    string path = Path.Combine(dirpath, name);
                    long size;
                    double mtime;
                    try
                    {
                        var fi = new FileInfo(path);
                        size = fi.Length;
                        mtime = FolderUtil.MTime(fi.LastWriteTimeUtc);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                    noVideo.Video(root, dirpath, stem, size);
                    if (wanted.TryGetValue(stem.ToLowerInvariant(), out var raw) || wanted.TryGetValue(parent.ToLowerInvariant(), out raw))
                    {
                        found.Add((path, raw, size, mtime));
                        continue;
                    }
                    if (removed.Contains(stem.ToLowerInvariant()) || removed.Contains(parent.ToLowerInvariant()))
                        continue;   // taken out of the list on purpose: not 'on disk but not in your lists'
                    // in no list: skip samples, trailers, extras, episodes and tiny files
                    if (size < 30_000_000 || Checker.EpisodeRe.IsMatch(stem) || (Checker.ExtraRe.IsMatch(stem) && size < 1_500_000_000)) continue;
                    string cand = stem;
                    if (!Checker.YearIn.IsMatch(stem) && Checker.YearIn.IsMatch(parent)
                        && Path.TrimEndingDirectorySeparator(Path.GetFullPath(dirpath)) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
                        cand = parent;   // 'movie.mkv' inside 'Heat (1995) 1080p'
                    unlisted.Add((path, cand, size, mtime));
                }
            }
        var junk = noVideo.Result();
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            StoreNoVideo(junk);
            Exec("DELETE FROM fileinfo");
            using (var ins = Cmd("INSERT OR REPLACE INTO fileinfo(path, raw, size, mtime) VALUES (?,?,?,?)", "", "", 0L, 0.0))
                foreach (var f in found)
                {
                    ins.Parameters["$p0"].Value = f.Path; ins.Parameters["$p1"].Value = f.Raw;
                    ins.Parameters["$p2"].Value = f.Size; ins.Parameters["$p3"].Value = f.MTime;
                    ins.ExecuteNonQuery();
                }
            Exec("DELETE FROM mediainfo WHERE path NOT IN (SELECT path FROM fileinfo)");
            Exec("DELETE FROM unlisted");
            using (var ins = Cmd("INSERT OR REPLACE INTO unlisted(path, raw, size, mtime) VALUES (?,?,?,?)", "", "", 0L, 0.0))
                foreach (var f in unlisted)
                {
                    ins.Parameters["$p0"].Value = f.Path; ins.Parameters["$p1"].Value = f.Raw;
                    ins.Parameters["$p2"].Value = f.Size; ins.Parameters["$p3"].Value = f.MTime;
                    ins.ExecuteNonQuery();
                }
            tx.Commit();
        }
        int taken = FoldersToLibrary(found.Select(f => f.Raw), unlisted.Select(u => u.Raw));
        lock (Lock)   // the movies just taken from the folders: path and size, so Show in folder works at once
        {
            using var tx = db.BeginTransaction();
            using (var ins = Cmd("INSERT OR REPLACE INTO fileinfo(path, raw, size, mtime) VALUES (?,?,?,?)", "", "", 0L, 0.0))
                foreach (var f in unlisted)
                {
                    ins.Parameters["$p0"].Value = f.Path; ins.Parameters["$p1"].Value = f.Raw;
                    ins.Parameters["$p2"].Value = f.Size; ins.Parameters["$p3"].Value = f.MTime;
                    ins.ExecuteNonQuery();
                }
            Exec("DELETE FROM mediainfo WHERE path NOT IN (SELECT path FROM fileinfo)");
            tx.Commit();
        }
        Version++;
        Log($"Movie folders: {found.Count} of {n} videos belong to your list, {unlisted.Count - taken} are in no list, {junk.Count} list folders have no video file"
            + (taken > 0 ? $"; {taken} movies taken from the folders themselves" : ""));
    }

    /// <summary>The movie folders are a source of movies: every real video in no list becomes a library entry (list_file <see cref="FolderSrc"/>),
    /// and such an entry goes away when its video does. Returns how many entries were added.</summary>
    int FoldersToLibrary(IEnumerable<string> foundRaws, IEnumerable<string> unlistedRaws)
    {
        var cands = unlistedRaws.Distinct().ToList();
        var keep = cands.Concat(foundRaws).ToHashSet();
        int added = 0;
        lock (Lock)
        {
            var existing = new Dictionary<string, (int Id, string? List, bool Present)>();
            using (var c = Cmd("SELECT id, raw, list_file, present FROM files"))
            using (var r = c.ExecuteReader())
                while (r.Read())
                    existing[r.GetString(1)] = (r.GetInt32(0), r.IsDBNull(2) ? null : r.GetString(2), !r.IsDBNull(3) && r.GetInt64(3) != 0);
            using var tx = db.BeginTransaction();
            foreach (var raw in cands)
            {
                if (existing.TryGetValue(raw, out var old))
                {
                    if (!old.Present && old.List != FolderSrc)   // dropped from its list, but the video is here
                    {
                        Exec("UPDATE files SET list_file=?, present=1 WHERE id=?", FolderSrc, old.Id);
                        added++;
                    }
                    continue;
                }
                var p = NameParser.Parse(raw);
                Exec("INSERT INTO files(raw, list_file, added, title, alt_title, year, resolution, source, codec, bit10, hdr, audio, farsi_dubbed, dual_audio, softsub, edition, grp) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                    raw, FolderSrc, Now(), p.Title, p.AltTitle, p.Year, p.Resolution, p.Source, p.Codec, p.Bit10 ? 1 : 0, p.Hdr ? 1 : 0,
                    p.Audio, p.FarsiDubbed ? 1 : 0, p.DualAudio ? 1 : 0, p.Softsub ? 1 : 0, p.Edition, p.Group);
                added++;
            }
            foreach (var (raw, row) in existing.Where(e => e.Value.List == FolderSrc))
                Exec("UPDATE files SET present=? WHERE id=?", keep.Contains(raw) ? 1 : 0, row.Id);
            Exec("DELETE FROM unlisted WHERE raw IN (SELECT raw FROM files WHERE present=1)");
            tx.Commit();
        }
        return added;
    }

    /// <summary>Videos on disk that are in no list (Problems, On disk tab), by name.</summary>
    public List<UnlistedItem> UnlistedItems()
    {
        using var rd = Reader();
        var l = new List<UnlistedItem>();
        using var c = Cmd(rd, "SELECT path, raw, size FROM unlisted ORDER BY raw COLLATE NOCASE", Array.Empty<object?>());
        using var r = c.ExecuteReader();
        while (r.Read()) l.Add(new UnlistedItem(r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? null : r.GetInt64(2)));
        return l;
    }

    // ---------- ffprobe ----------
    /// <summary>Read length, picture size, audio languages and subtitles of the files in the movie folders with ffprobe. Only new or changed
    /// files are read; failures are remembered so they are not retried every time. A job (lib.Start(lib.ProbeMedia)).</summary>
    public void ProbeMedia()
    {
        var ff = Probe.FindFfprobe(LoadSettings())
            ?? throw new InvalidOperationException("ffprobe was not found. Install ffmpeg (winget install Gyan.FFmpeg) or set its path in Settings.");
        var todo = new List<(string Path, long? Size, double? MTime)>();
        lock (Lock)
        {
            using (var c = Cmd("SELECT f.path, f.size, f.mtime, m.path, m.size, m.mtime FROM fileinfo f LEFT JOIN mediainfo m ON m.path=f.path"))
            using (var r = c.ExecuteReader())
                while (r.Read())
                {
                    long? size = r.IsDBNull(1) ? null : r.GetInt64(1);
                    double? mtime = r.IsDBNull(2) ? null : r.GetDouble(2);
                    bool changed = r.IsDBNull(3)
                        || size != (r.IsDBNull(4) ? null : r.GetInt64(4))
                        || (mtime == null) != r.IsDBNull(5) || (mtime != null && Math.Abs(mtime.Value - r.GetDouble(5)) > 0.002);   // tolerant: Python and .NET round the time differently
                    if (changed) todo.Add((r.GetString(0), size, mtime));
                }
            Exec("DELETE FROM mediainfo WHERE path NOT IN (SELECT path FROM fileinfo)");
        }
        var batch = new List<object?[]>();
        int done = 0;
        const int Chunk = 8;
        for (int i = 0; i < todo.Count; i += Chunk)
        {
            Progress("Reading audio and subtitles with ffprobe", done, todo.Count);
            var part = todo.Skip(i).Take(Chunk).ToList();
            var res = new ProbeInfo?[part.Count];
            Parallel.For(0, part.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, k => res[k] = Probe.ProbeFile(ff, part[k].Path));
            for (int k = 0; k < part.Count; k++)
            {
                var (path, size, mtime) = part[k];
                var info = res[k];
                batch.Add(new object?[]
                {
                    path, size, mtime, info != null ? 1 : 0, info?.Duration, info?.Width, info?.Height, info?.VCodec, info?.Bitrate,
                    PyJsonList(info?.Audio), PyJsonList(info?.Subs), info?.NAudio, info?.NSubs,
                });
                done++;
                if (batch.Count >= 100) { SaveProbe(batch); batch = new(); }
            }
        }
        SaveProbe(batch);
        Version++;
        Log($"ffprobe: read {todo.Count} files");
    }

    // Python json.dumps of a list of names: ["English", "Persian"]
    static string PyJsonList(IEnumerable<string>? l) =>
        "[" + string.Join(", ", (l ?? Array.Empty<string>()).Select(s => System.Text.Json.JsonSerializer.Serialize(s))) + "]";

    /// <summary>Stores probe rows: path, size, mtime, ok, duration, width, height, vcodec, bitrate, audio (json), subs (json), n_audio, n_subs.</summary>
    void SaveProbe(List<object?[]> batch)
    {
        if (batch.Count == 0) return;
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            foreach (var row in batch)
                Exec("INSERT OR REPLACE INTO mediainfo(path, size, mtime, ok, duration, width, height, vcodec, bitrate, audio, subs, n_audio, n_subs) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)", row);
            tx.Commit();
        }
    }

    public MediaProbeStatus ProbeStatus()
    {
        using var rd = Reader();
        int Q(string sql) { using var c = Cmd(rd, sql, Array.Empty<object?>()); return Convert.ToInt32(c.ExecuteScalar()); }
        return new MediaProbeStatus(Probe.FindFfprobe(LoadSettings()), Q("SELECT count(*) FROM fileinfo"),
            Q("SELECT count(*) FROM mediainfo WHERE ok=1"), Q("SELECT count(*) FROM mediainfo WHERE ok=0"));
    }

    // ---------- names from the list check ----------
    /// <summary>Append chosen names to '&lt;list folder&gt;/added.txt' and remember the IMDb choices (table forced). Returns how many names were new.
    /// A name counts as new unless it is in the library now, so a name that left the lists or was removed can come back: adding it undoes the removal.
    /// With <paramref name="update"/> the full update is queued, as in the Python app.</summary>
    public int AddFromCheck(IEnumerable<AddItem> items, bool update = true)
    {
        var s = LoadSettings();
        var have = new HashSet<string>();
        lock (Lock)
            using (var c = Cmd("SELECT raw FROM files WHERE present=1"))
            using (var r = c.ExecuteReader())
                while (r.Read()) have.Add(r.GetString(0));
        var add = items.Where(it => !string.IsNullOrEmpty(it.Raw) && !have.Contains(it.Raw.Trim())).ToList();
        if (add.Count == 0) return 0;
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            foreach (var it in add) Exec("DELETE FROM removed WHERE raw=?", it.Raw.Trim());
            tx.Commit();
        }
        Directory.CreateDirectory(s.ListDir);
        string path = Path.Combine(s.ListDir, "added.txt");
        bool needsNl = File.Exists(path) && new FileInfo(path).Length > 0 && !EndsWithNewline(path);
        using (var f = new StreamWriter(path, true, new System.Text.UTF8Encoding(false)) { NewLine = Environment.NewLine })   // Python's text mode: \r\n on Windows
        {
            if (needsNl) f.WriteLine();
            foreach (var it in add) f.WriteLine(it.Raw.Trim());
        }
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            foreach (var it in add.Where(i => i.HasChoice))
                Exec("INSERT OR REPLACE INTO forced(raw, tconst) VALUES (?,?)", it.Raw.Trim(), it.Choice is > 0 ? it.Choice : null);
            tx.Commit();
        }
        if (update) StartFullUpdate();
        return add.Count;
    }

    static bool EndsWithNewline(string path)
    {
        using var f = File.OpenRead(path);
        f.Seek(-1, SeekOrigin.End);
        return f.ReadByte() == '\n';
    }

    // ---------- show in folder ----------
    /// <summary>Open Explorer with the file selected. Only files the folder scan found (table fileinfo) can be opened this way.</summary>
    public bool Reveal(string path)
    {
        if (RevealNoVideoFolder(path)) return true;
        bool known;
        using (var rd = Reader())
        {
            using (var c = Cmd(rd, "SELECT 1 FROM fileinfo WHERE path=?", new object?[] { path }))
                known = c.ExecuteScalar() != null;
            if (!known)   // a movie taken from a folder can be shown too
                using (var c = Cmd(rd, "SELECT 1 FROM unlisted WHERE path=?", new object?[] { path }))
                    known = c.ExecuteScalar() != null;
        }
        if (!known || !File.Exists(path)) throw new FileNotFoundException("That file is not where it was. Scan the movie folders again.");
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = RevealArgs(path), UseShellExecute = false });
        return true;
    }

    /// <summary>The explorer.exe arguments that select a file.</summary>
    public static string RevealArgs(string path) => $"/select,\"{Path.GetFullPath(path)}\"";

    // ---------- what LoadMovies joins in ----------
    /// <summary>ffprobe facts of the files read OK, by path (movies() media dict).</summary>
    static Dictionary<string, MediaRow> LoadMediaRows(SqliteConnection rd)
    {
        var d = new Dictionary<string, MediaRow>();
        using var c = Cmd(rd, "SELECT path, duration, width, height, vcodec, bitrate, audio, subs FROM mediainfo WHERE ok=1", Array.Empty<object?>());
        using var r = c.ExecuteReader();
        while (r.Read())
            d[r.GetString(0)] = new MediaRow(r.IsDBNull(1) ? null : (int)r.GetInt64(1), r.IsDBNull(2) ? null : (int)r.GetInt64(2),
                r.IsDBNull(3) ? null : (int)r.GetInt64(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetInt64(5),
                Movie.ParseStrings(r.IsDBNull(6) ? null : r.GetString(6)), Movie.ParseStrings(r.IsDBNull(7) ? null : r.GetString(7)));
        return d;
    }

    /// <summary>_media_fields: the ffprobe facts of the first scanned path of the file (nothing when it was not read).</summary>
    static void ApplyMedia(MovieFile f, Dictionary<string, MediaRow> media)
    {
        var m = f.Paths.Where(media.ContainsKey).Select(p => media[p]).FirstOrDefault();
        if (m == null) return;
        f.Dur = m.Duration; f.W = m.Width; f.H = m.Height; f.VCodec = m.VCodec; f.Br = m.Bitrate; f.ALang = m.Audio; f.SLang = m.Subs;
    }
}
