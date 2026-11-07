namespace MovieShelf.Core;

public sealed partial class Library
{
    sealed record QueuedJob(Action Work, bool Stoppable, object Key);

    readonly List<QueuedJob> queued = new();

    /// <summary>Jobs waiting behind the running one.</summary>
    public int QueuedCount { get { lock (queued) return queued.Count; } }

    /// <summary>Runs <paramref name="work"/> now, or once after the running job (Python's queue=True); the same <paramref name="key"/>, or without one the same method, is not queued twice.</summary>
    public bool StartOrQueue(Action work, bool stoppable = true, object? key = null)
    {
        key ??= work;
        bool busy, added = false;
        lock (queued)
        {
            busy = Job.Running || queued.Count > 0;
            if (busy && !queued.Any(q => q.Key.Equals(key)))
            {
                queued.Add(new QueuedJob(work, stoppable, key));
                added = true;
            }
        }
        if (added) Log("Queued: will run when the current job finishes");
        if (busy) return false;
        return Start(work, stoppable) || StartOrQueue(work, stoppable, key);   // another job got in first: wait behind it
    }

    /// <summary>Called when a job ends, however it ended: starts the next queued one. (Stop empties the queue itself.)</summary>
    void RunQueued()
    {
        QueuedJob next;
        lock (queued)
        {
            if (queued.Count == 0) return;
            next = queued[0];
            queued.RemoveAt(0);
        }
        if (!Start(next.Work, next.Stoppable))
            lock (queued) queued.Insert(0, next);   // another job got in first: this one follows it
    }

    /// <summary>Starts the whole update, or queues it once behind the running job.</summary>
    public bool StartFullUpdate(bool forceDownload = false) => StartOrQueue(() => FullUpdate(forceDownload), key: ("full update", forceDownload));

    /// <summary>The whole update, in the order of the Python app: IMDb data, lists, movie folders, matching, Persian titles, details, posters, scores, people.</summary>
    public void FullUpdate(bool forceDownload = false)
    {
        EnsureIndex(forceDownload);
        Scan();
        var s = LoadSettings();
        if (s.MediaDirs.Count > 0)
        {
            ScanMedia();
            if (s.ProbeMedia && Probe.FindFfprobe(s) != null) ProbeMedia();
        }
        MatchAll();
        ApplyFaTitles();   // Python does this first; here titles new in this update get the review at once
        Enrich();
        FillPeopleBasics();
        FetchPosters();
        FetchTmdb();
        FetchScores();
        s = LoadSettings();
        if (s.PeopleAuto && s.TmdbKey != "") FetchPeople(s.PeopleScope);
        Version++;
        IssueRev++;
    }

    /// <summary>"Check again", a job: reads the lists and movie folders again, matches what is new and rebuilds the problem list. All of it is local
    /// and quick; cast and posters of new titles come afterwards as a separate job (<see cref="FillNew"/>), so the result does not wait for the network.</summary>
    public void Rescan()
    {
        Scan();
        if (LoadSettings().MediaDirs.Count > 0) ScanMedia();
        if (!Matcher.HasIndex)
            throw new InvalidOperationException("The IMDb index is not built yet. Run an update first.");
        MatchAll();
        Version++;
        IssueRev++;
        StartOrQueue(FillNew);
    }

    /// <summary>Cast, crew and posters of titles that were just added. A failure (offline, say) is logged, not fatal.</summary>
    public void FillNew()
    {
        foreach (var (name, step) in new (string, Action)[] { ("enrich", () => Enrich()), ("fetch_posters", () => FetchPosters()) })
        {
            try { step(); }
            catch (Exception e) when (e is not OperationCanceledException) { Log($"{name} skipped: {e.Message}"); }
        }
        Version++;
    }

    /// <summary>Starts <see cref="Rescan"/>, or queues it once behind the running job. With <paramref name="now"/> the running job is stopped
    /// first (the user chose to stop a long people scan, say, and check at once).</summary>
    public bool StartRescan(bool now = false)
    {
        if (now) Stop();
        return StartOrQueue(Rescan);
    }

    /// <summary>Settings "Re-match": matches every automatic match again, then fetches what the new matches need. A job.</summary>
    public void Rematch()
    {
        MatchAll(onlyUnmatched: false);
        Enrich();
        FetchPosters();
        FetchTmdb();
    }

    /// <summary>Starts <see cref="Rematch"/>; false when a job is already running (not queued, as in Python).</summary>
    public bool StartRematch() => Start(Rematch);

    /// <summary>Call once at every start, like the Python launcher: today's automatic backup, then the full update (not on the very first start). True when the update started.</summary>
    public bool RunStartupJobs()
    {
        try { AutoBackup(); }
        catch (Exception e) { Log($"Automatic backup skipped: {e.Message}"); }
        return !FirstRun && Start(() => FullUpdate());
    }

    /// <summary>Gives a file the chosen IMDb title and, unless the title already has its details, fetches them in the background (queued behind a running job).</summary>
    public void SetMatchAndRefresh(int fileId, int? tconst)
    {
        if (SetMatch(fileId, tconst)) StartOrQueue(() => RefreshTitle(tconst!.Value));
    }

    /// <summary>After the user chose another IMDb title for a file: details, poster and TMDB data of that title.</summary>
    public void RefreshTitle(int tconst)
    {
        Enrich(new[] { tconst });
        FetchPosters();
        FetchTmdb(new[] { tconst });
    }
}
