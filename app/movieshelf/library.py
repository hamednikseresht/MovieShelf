"""Collection database: scan list files, match to IMDb (matcher.py), enrich with cast,
posters, critic scores and (optionally) TMDB data."""
import glob
import json
import os
import shutil
import sqlite3
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from . import backup, checker, extras, fatitles, fullbackup, imdb_index, issues, online, paths, probe, scores
from .matcher import UNSURE, Matcher, reason
from .parse import parse

SCHEMA = '''
CREATE TABLE IF NOT EXISTS files(
    id INTEGER PRIMARY KEY, raw TEXT UNIQUE, list_file TEXT, present INT DEFAULT 1, added TEXT,
    title TEXT, alt_title TEXT, year INT, resolution TEXT, source TEXT, codec TEXT, bit10 INT, hdr INT,
    audio TEXT, farsi_dubbed INT, dual_audio INT, softsub INT, edition TEXT, grp TEXT,
    tconst INT, conf TEXT, score REAL, manual INT DEFAULT 0, matched INT DEFAULT 0);
CREATE INDEX IF NOT EXISTS files_tconst ON files(tconst);
CREATE TABLE IF NOT EXISTS titles(
    tconst INTEGER PRIMARY KEY, type TEXT, title TEXT, orig TEXT, year INT, endyear INT, runtime INT,
    genres TEXT, rating REAL, votes INT, en_title TEXT, fa_title TEXT, akas TEXT,
    directors TEXT, writers TEXT, cast_ TEXT, crew TEXT, enriched INT DEFAULT 0,
    poster TEXT, poster_url TEXT, poster_tried INT DEFAULT 0,
    plot TEXT, tagline TEXT, countries TEXT, languages TEXT, orig_lang TEXT, tmdb_id INT,
    tmdb_done INT DEFAULT 0, backdrop_url TEXT, tmdb_cast TEXT, collection TEXT, updated TEXT);
CREATE TABLE IF NOT EXISTS user(mkey TEXT PRIMARY KEY, fav INT DEFAULT 0, watched INT DEFAULT 0,
    myrating INT, notes TEXT, updated TEXT);
CREATE TABLE IF NOT EXISTS people(nconst INTEGER PRIMARY KEY, name TEXT);
CREATE TABLE IF NOT EXISTS forced(raw TEXT PRIMARY KEY, tconst INT);
CREATE TABLE IF NOT EXISTS dismissed(ikey TEXT PRIMARY KEY);
CREATE TABLE IF NOT EXISTS views(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT UNIQUE, state TEXT);
CREATE TABLE IF NOT EXISTS wishlist(id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT, year INT, tconst INT, note TEXT,
    source TEXT, status TEXT DEFAULT 'wanted', added TEXT, UNIQUE(title, year));
CREATE TABLE IF NOT EXISTS fileinfo(path TEXT PRIMARY KEY, raw TEXT, size INT, mtime REAL);
CREATE INDEX IF NOT EXISTS fileinfo_raw ON fileinfo(raw);
CREATE TABLE IF NOT EXISTS collections(name TEXT PRIMARY KEY, tmdb_id INT, parts TEXT);
CREATE TABLE IF NOT EXISTS fa_titles(tconst INTEGER PRIMARY KEY, title TEXT, source TEXT);
CREATE TABLE IF NOT EXISTS persons(nconst INTEGER PRIMARY KEY, tmdb_id INT, data TEXT, photo INT DEFAULT 0, fetched TEXT);
CREATE TABLE IF NOT EXISTS unlisted(path TEXT PRIMARY KEY, raw TEXT, size INT, mtime REAL);
CREATE TABLE IF NOT EXISTS removed(raw TEXT PRIMARY KEY, tconst INT, title TEXT, removed TEXT);
CREATE TABLE IF NOT EXISTS novideo(path TEXT PRIMARY KEY, raw TEXT, nfiles INT, size INT, names TEXT);
CREATE TABLE IF NOT EXISTS mediainfo(path TEXT PRIMARY KEY, size INT, mtime REAL, ok INT, duration INT, width INT, height INT,
    vcodec TEXT, bitrate INT, audio TEXT, subs TEXT, n_audio INT, n_subs INT);
'''
# columns added after the first release (old databases get them via ALTER TABLE)
NEW_COLUMNS = [('titles', 'rt', 'INT'), ('titles', 'mc', 'INT'), ('titles', 'scores_at', 'TEXT'),
               ('titles', 'omdb_done', 'INT DEFAULT 0'), ('user', 'watched_at', 'TEXT'), ('people', 'birth', 'INT'),
               ('people', 'death', 'INT'), ('people', 'prof', 'TEXT')]

PARSER_VERSION = 2  # bump when parse.py changes so stored names are re-read
FILE_FIELDS = ['title', 'alt_title', 'year', 'resolution', 'source', 'codec', 'bit10', 'hdr', 'audio',
               'farsi_dubbed', 'dual_audio', 'softsub', 'edition', 'group']


def now():
    return time.strftime('%Y-%m-%d %H:%M:%S')


POSTER_MAX = 15_000_000  # bytes: the biggest poster image accepted by hand


def image_type(data):
    """The media type of a JPEG, PNG, WebP or GIF image from its first bytes, or None for anything else."""
    if data[:3] == b'\xff\xd8\xff':
        return 'image/jpeg'
    if data[:8] == b'\x89PNG\r\n\x1a\n':
        return 'image/png'
    if data[:4] == b'RIFF' and data[8:12] == b'WEBP':
        return 'image/webp'
    if data[:6] in (b'GIF87a', b'GIF89a'):
        return 'image/gif'
    return None


def is_real_video(stem, size):
    """A video that can be the movie itself: not a tiny clip, sample, trailer or extra."""
    return not (size < 30_000_000 or (checker.EXTRA_RE.search(stem) and size < 1_500_000_000))


def folder_contents(d, limit=6):
    """(number of files, total bytes, first few file names) of everything inside a folder."""
    n, total, names = 0, 0, []
    for dp, _, fs in os.walk(d):
        for f in sorted(fs):
            n += 1
            try:
                total += os.path.getsize(os.path.join(dp, f))
            except OSError:
                pass
            if len(names) < limit:
                names.append(os.path.relpath(os.path.join(dp, f), d))
    return n, total, names


LIST_EXT = ('.txt', '.csv', '.tsv')
FOLDER_SRC = '(movie folders)'  # files.list_file of a movie that comes from a movie folder, not from a list


class Cancelled(BaseException):
    """Raised inside a running job when the user presses Stop. A BaseException, so no 'except Exception' swallows it."""


class Library:
    def __init__(self, root):
        self.root = os.path.abspath(root)
        self.data = os.path.join(root, 'data')
        self.imdb_dir = os.path.join(self.data, 'imdb')
        self.index_path = os.path.join(self.data, 'imdb_index.db')
        self.poster_dir = os.path.join(self.data, 'posters')
        os.makedirs(self.poster_dir, exist_ok=True)
        self.people_dir = os.path.join(self.data, 'people')  # person photos
        os.makedirs(self.people_dir, exist_ok=True)
        self.db_path = os.path.join(self.data, 'library.db')
        self.settings_path = os.path.join(self.data, 'settings.json')
        self.lock = threading.RLock()
        self.db = sqlite3.connect(self.db_path, check_same_thread=False)
        self.db.row_factory = sqlite3.Row
        self._ensure_schema()
        self.db.execute('PRAGMA journal_mode=WAL')
        self.job = {'running': False, 'stage': '', 'done': 0, 'total': 0, 'log': [], 'error': None,
                    'stoppable': True, 'stopping': False, 'stopped': False}
        self._stop = threading.Event()  # set by stop_job; progress() and the download workers raise Cancelled
        self.restore_result = None  # what the last full restore did (shown by the Backup window)
        self.issue_rev = 0  # bumped when a problem is ignored or restored
        self.version = 0  # bumped whenever data changes, so the UI knows to reload
        self.queue = []   # jobs asked for while another one was running
        self.matcher = Matcher(self.index_path)
        self._cand_cache = {}  # problem list candidates per (file id, tconst, conf)
        # list check runs beside the main job (read-only), with its own progress
        self.check = {'running': False, 'stage': '', 'done': 0, 'total': 0, 'error': None, 'result': None}

    def _ensure_schema(self):
        """Tables and columns every version needs (also run after a backup from an older version is restored)."""
        self.db.executescript(SCHEMA)
        for table, col, typ in NEW_COLUMNS:
            if col not in {r[1] for r in self.db.execute(f'PRAGMA table_info({table})')}:
                self.db.execute(f'ALTER TABLE {table} ADD COLUMN {col} {typ}')
        self.db.commit()

    # ---------- settings ----------
    def settings(self):
        s = {'list_dir': paths.default_list_dir(), 'tmdb_key': '', 'omdb_key': '',
             'online_posters': True, 'online_fallback': True, 'auto_refresh': True, 'media_dirs': [],
             'probe_media': False, 'ffprobe_path': '', 'people_auto': False, 'people_scope': 'main',
             'rename_pattern': '{title} ({year}) [{res} {src}]'}  # = extras.DEFAULT_PATTERN
        if os.path.exists(self.settings_path):
            with open(self.settings_path, encoding='utf-8-sig') as f:  # a BOM from an editor or the installer is fine
                s.update(json.load(f))
        return s

    def first_run(self):
        """True until a start was chosen (a backup restored or 'start fresh'): nothing is downloaded or built before that."""
        return not os.path.exists(self.settings_path)

    def start_fresh(self, list_path=None, media_dir=None):
        """First-run choice 'start with the defaults': a movie folder and/or a list file or folder is the source of movies,
        then everything is downloaded and built. One of the two is enough."""
        m = os.path.abspath(str(media_dir).strip().strip('"')) if media_dir and str(media_dir).strip() else None
        lp = str(list_path or '').strip().strip('"')
        if m and not os.path.isdir(m):
            raise ValueError(f'{m} is not a folder.')
        if not m and not lp:
            raise ValueError('Give a movie folder, a list (.txt or .csv), or both: the library is built from them.')
        if lp:
            self.load_list({'path': lp}, 'add', rescan=False)
        self.save_settings({'media_dirs': [m] if m else list(self.settings().get('media_dirs') or [])})  # also marks the start as chosen
        return self.start(self.full_update)

    def _list_sources(self, src):
        """[(name, [{'raw', 'tconst'}])] from {'path'}: a list file (.txt .csv .tsv), a folder of such lists, or a folder of
        movies (the names of its video files); or from {'text', 'name'}: a list the page read itself."""
        if src.get('text') is not None:
            name = os.path.basename(str(src.get('name') or 'list.txt'))
            return [(os.path.splitext(name)[0] or 'list', checker.entries_from_text(str(src['text']), name))]
        p = os.path.abspath(str(src.get('path') or '').strip().strip('"'))
        if os.path.isfile(p):
            if not p.lower().endswith(LIST_EXT):
                raise ValueError(f'{p} is not a list. Use a .txt or .csv file, or a folder.')
            return [(os.path.splitext(os.path.basename(p))[0], checker.entries_from_text(checker.read_text(p), p))]
        if os.path.isdir(p):
            files = sorted(f for f in glob.glob(os.path.join(p, '*')) if f.lower().endswith(LIST_EXT) and os.path.isfile(f))
            if files:
                return [(os.path.splitext(os.path.basename(f))[0], checker.entries_from_text(checker.read_text(f), f)) for f in files]
            return [(os.path.basename(p) or 'folder', [e for e in checker.entries_from_folder(p) if not e.get('episode')])]
        raise ValueError(f'{p} was not found.')

    def preview_list(self, src):
        """What loading this list would do, before anything changes: how many names, which ones are repeated inside the list,
        which are already in the library (same name), and which look like a movie the library already has under another
        file name (same title and year)."""
        sources = self._list_sources(src)
        raws, seen, repeated = [], set(), {}
        for _, es in sources:
            for e in es:
                r = (e.get('raw') or '').strip()
                if not r:
                    continue
                if r in seen:
                    repeated[r] = repeated.get(r, 1) + 1
                else:
                    seen.add(r)
                    raws.append(r)
        db = self.reader()
        try:
            mine = {r['raw']: (r['title'], r['year']) for r in db.execute('SELECT raw, title, year FROM files WHERE present=1')}
        finally:
            db.close()
        lower = {k.lower() for k in mine}
        same_name = [r for r in raws if r.lower() in lower]
        movies = {((t or '').lower(), y) for t, y in mine.values() if t}
        other_file = []
        for r in raws:
            if r.lower() in lower:
                continue
            p = parse(r)
            if p.get('title') and (p['title'].lower(), p.get('year')) in movies:
                other_file.append(r)
        if not any(es for _, es in sources):
            raise ValueError('No movie names found there. A list is a .txt file with one name per line, or a .csv with a name, title or IMDb id column.')
        return {'lists': len(sources), 'names': len(raws), 'repeated': list(repeated)[:15], 'repeated_n': len(repeated),
                'existing': same_name[:15], 'existing_n': len(same_name),
                'similar': other_file[:15], 'similar_n': len(other_file), 'new_n': len(raws) - len(same_name)}

    def load_list(self, src, mode='add', rescan=True):
        """The one way to bring a list in ('Load list'). mode 'add': the names join the lists the library already has.
        mode 'new': the lists it has now are put aside (moved to 'previous lists' in the list folder, never deleted) and
        this one replaces them; movies that come from the movie folders stay. Returns {'lists', 'names', 'moved'}."""
        if mode not in ('add', 'new'):
            raise ValueError('Choose whether to add to the current list or start a new one.')
        sources = [(n, [e for e in es if e.get('raw')]) for n, es in self._list_sources(src)]
        sources = [(n, es) for n, es in sources if es]
        if not sources:
            raise ValueError('No movie names found there. A list is a .txt file with one name per line, or a .csv with a name, title or IMDb id column.')
        dest = self.settings()['list_dir']
        os.makedirs(dest, exist_ok=True)
        moved = 0
        if mode == 'new':
            old = glob.glob(os.path.join(dest, '*.txt'))
            if old:
                aside = os.path.join(dest, 'previous lists', time.strftime('%Y-%m-%d %H-%M-%S'))
                os.makedirs(aside, exist_ok=True)
                for f in old:
                    shutil.move(f, os.path.join(aside, os.path.basename(f)))
                moved = len(old)
        have = [{l.strip() for l in checker.read_text(f).splitlines()} for f in glob.glob(os.path.join(dest, '*.txt'))]
        written, names = 0, set()
        for stem, entries in sources:
            raws = list(dict.fromkeys(e['raw'].strip() for e in entries if e['raw'].strip()))
            names.update(raws)
            if any(set(raws) <= h for h in have):
                continue  # every name is already in a list the library reads
            target, n = os.path.join(dest, f'{stem}.txt'), 2
            while os.path.exists(target):
                target, n = os.path.join(dest, f'{stem} ({n}).txt'), n + 1
            with open(target, 'w', encoding='utf-8') as f:
                f.write('\n'.join(raws) + '\n')
            have.append(set(raws))
            written += 1
        with self.lock:
            self.db.executemany('DELETE FROM removed WHERE raw=?', [(r,) for r in names])  # loading a name again undoes its removal
            self.db.executemany('INSERT OR REPLACE INTO forced(raw, tconst) VALUES (?,?)',
                                [(e['raw'].strip(), e['tconst']) for _, es in sources for e in es if e.get('tconst')])
            self.db.commit()
        if rescan:
            self.start(self.rescan, queue=True)
        return {'lists': written, 'names': len(names), 'moved': moved}

    def save_settings(self, upd):
        s = self.settings()
        s.update({k: v for k, v in upd.items() if k in s})
        with open(self.settings_path, 'w', encoding='utf-8') as f:
            json.dump(s, f, indent=2)
        return s

    # ---------- job runner ----------
    def check_stop(self):
        if self._stop.is_set():
            raise Cancelled()

    def stop_job(self):
        """Stop the running job at its next step (a download stops within a second). Queued jobs are dropped.
        A restore cannot be stopped: it replaces the library. Returns True if a stop was asked for."""
        if not self.job['running'] or not self.job.get('stoppable', True):
            return False
        self.queue.clear()
        self._stop.set()
        self.job['stopping'] = True
        self.log('Stopping…')
        return True

    def progress(self, stage, done=0, total=0):
        self.check_stop()
        self.job.update(stage=stage, done=done, total=total)

    def log(self, msg):
        self.job['log'] = (self.job['log'] + [f'{time.strftime("%H:%M:%S")} {msg}'])[-200:]

    def start(self, fn, *args, queue=False):
        if self.job['running']:
            if queue and (fn, args) not in self.queue:  # run it after the current job
                self.queue.append((fn, args))
                self.log('Queued: will run when the current job finishes')
            return False
        self._stop.clear()
        self.job = {'running': True, 'stage': 'Starting', 'done': 0, 'total': 0, 'log': [], 'error': None,
                    'stoppable': fn != self.restore_full_backup, 'stopping': False, 'stopped': False}

        def run():
            try:
                fn(*args)
                self.progress('Finished')
            except Cancelled:
                self.job['stopped'] = True
                self.log('Stopped. What was finished is kept; the next update continues from there.')
            except Exception as e:  # report in UI
                import traceback
                traceback.print_exc()
                self.job['error'] = f'{type(e).__name__}: {e}'
                self.log(f'ERROR {e}')
            finally:
                self.job['running'] = False
                self.version += 1
                if self.queue:
                    nfn, nargs = self.queue.pop(0)
                    self.start(nfn, *nargs)
        threading.Thread(target=run, daemon=True).start()
        return True

    # ---------- full update pipeline ----------
    def full_update(self, force_download=False):
        fatitles.apply(self)  # spelling clean-up and the reviewed Persian titles (once per version)
        if force_download or not imdb_index.have_files(self.imdb_dir):
            imdb_index.download(self.imdb_dir, self.progress, force=force_download)
            self.log('IMDb datasets downloaded')
        if force_download or not os.path.exists(self.index_path):
            self.log('Building IMDb index (one-time, a few minutes)')
            imdb_index.build_index(self.imdb_dir, self.index_path, self.progress)
            self.matcher.reset()
            self.log('IMDb index built')
            if force_download:
                with self.lock:
                    self.db.execute('UPDATE titles SET enriched=0')
                    self.db.commit()
        elif self.settings().get('auto_refresh') and (imdb_index.file_age_days(self.imdb_dir, 'title.ratings') or 0) > 7:
            try:
                self.refresh_ratings()
            except Exception as e:  # offline: keep going with the ratings we have
                self.log(f'Ratings refresh skipped: {e}')
        self.scan()
        if self.settings().get('media_dirs'):
            self.scan_media()
            if self.settings().get('probe_media') and probe.find_ffprobe(self.settings()):
                self.probe_media()
        self.match_all()
        self.enrich()
        self.fill_people_basics()
        self.fetch_posters()
        self.fetch_tmdb()
        self.fetch_scores()
        if self.settings().get('people_auto') and self.settings().get('tmdb_key'):
            self.fetch_people(self.settings().get('people_scope') or 'main')
        self.version += 1
        self.progress('Preparing the problem list')
        with self.lock:
            self.problems()  # warms the candidate cache so the Problems window opens instantly

    def rescan(self):
        """'Check again': read the list files and the movie folders again, match what is new and rebuild the problem
        list. All of it is local and quick. Cast and posters of new titles come afterwards, as a separate job
        (fill_new), so the result does not wait for IMDb's big files or the network."""
        self.scan()
        if self.settings().get('media_dirs'):
            self.scan_media()
        self.match_all()
        self.version += 1
        self.progress('Preparing the problem list')
        with self.lock:
            self.problems()
        self.start(self.fill_new, queue=True)

    def fill_new(self):
        """Cast, crew and posters of titles that were just added. A failure (offline, say) is logged, not fatal."""
        for step in (self.enrich, self.fetch_posters):
            try:
                step()
            except Cancelled:
                raise
            except Exception as e:
                self.log(f'{step.__name__} skipped: {e}')

    # ---------- IMDb ratings refresh (small download, ~8 MB) ----------
    def refresh_ratings(self):
        self.log('Downloading latest IMDb ratings')
        imdb_index.download(self.imdb_dir, self.progress, force=True, names=['title.ratings'])
        self.progress('Updating ratings', 0, 1)
        ratings = imdb_index.read_ratings(self.imdb_dir)
        with self.lock:
            old = {r[0]: (r[1], r[2]) for r in self.db.execute('SELECT tconst, rating, votes FROM titles')}
            upd = [(ratings[t][0], ratings[t][1], now(), t) for t in old if t in ratings and ratings[t] != old[t]]
            self.db.executemany('UPDATE titles SET rating=?, votes=?, updated=? WHERE tconst=?', upd)
            self.db.commit()
        if os.path.exists(self.index_path):  # keep the matching index in step
            ix = sqlite3.connect(self.index_path)
            ix.executemany('UPDATE titles SET rating=?, votes=? WHERE id=?',
                           [(r, v, i) for i, (r, v) in ratings.items()])
            ix.commit()
            ix.close()
        self.version += 1
        self.log(f'IMDb ratings updated: {len(upd)} of your titles changed')

    # ---------- Rotten Tomatoes / Metacritic ----------
    def fetch_scores(self, force=False):
        """Wikidata for everything (refreshed monthly), then OMDb (if a key is set) for titles still missing."""
        q = 'SELECT tconst FROM titles WHERE type IS NOT NULL'
        if not force:
            q += " AND (scores_at IS NULL OR scores_at < datetime('now', '-30 days'))"
        ids = [r[0] for r in self.db.execute(q + ' ORDER BY votes DESC')]
        got = 0
        for n in range(0, len(ids), 250):
            self.progress('Getting Rotten Tomatoes & Metacritic scores (Wikidata)', n, len(ids))
            batch = ids[n:n + 250]
            try:
                res = scores.wikidata_scores(batch)
            except Exception as e:
                self.log(f'Wikidata not reachable ({e}); scores will be retried next update')
                break
            with self.lock:
                self.db.executemany(
                    'UPDATE titles SET rt=coalesce(?, rt), mc=coalesce(?, mc), scores_at=? WHERE tconst=?',
                    [(res.get(t, {}).get('rt'), res.get(t, {}).get('mc'), now(), t) for t in batch])
                self.db.commit()
            got += len(res)
            self.version += 1
            time.sleep(0.5)
        if ids:
            self.log(f'Critic scores: found for {got} of {len(ids)} titles on Wikidata')
        self.fetch_omdb()

    def fetch_omdb(self):
        key = self.settings().get('omdb_key')
        if not key:
            return
        ids = [r[0] for r in self.db.execute(
            'SELECT tconst FROM titles WHERE type IS NOT NULL AND omdb_done=0 AND (rt IS NULL OR mc IS NULL) '
            'ORDER BY votes DESC')]
        got = 0
        for n, t in enumerate(ids):
            if n % 10 == 0:
                self.progress('Getting missing scores from OMDb', n, len(ids))
            try:
                res = scores.omdb_scores(key, t)
            except scores.LimitReached:
                self.log(f'OMDb daily limit reached after {n} titles; the rest continue on the next update')
                break
            except PermissionError as e:
                self.log(f'OMDb: {e}. Check the key in Settings.')
                break
            except Exception as e:
                self.log(f'OMDb not reachable ({e})')
                break
            with self.lock:
                self.db.execute('UPDATE titles SET rt=coalesce(rt, ?), mc=coalesce(mc, ?), omdb_done=1 WHERE tconst=?',
                                (res.get('rt'), res.get('mc'), t))
                if n % 20 == 0:
                    self.db.commit()
            got += bool(res)
        self.db.commit()
        self.version += 1
        if ids:
            self.log(f'OMDb added scores for {got} titles')

    # ---------- scan ----------
    def scan(self):
        s = self.settings()
        files = sorted(glob.glob(os.path.join(s['list_dir'], '*.txt')))
        folders_set = any(d for d in s.get('media_dirs') or [])
        if not files and not self.use_folders():
            raise RuntimeError(f'No .txt list found in {s["list_dir"]} and no movie folder can be read. Add a movie folder in '
                               f'Settings → General, or bring a list in with the Load list button.')
        seen = set()
        src_of = {}
        new = 0
        with self.lock:
            existing = {r['raw']: r['id'] for r in self.db.execute('SELECT id, raw FROM files')}
            removed = {r[0] for r in self.db.execute('SELECT raw FROM removed')}  # taken out of the list by the user
            for path in files:
                lines = [l.strip() for l in checker.read_text(path).splitlines()]  # UTF-8, UTF-16 (Notepad 'Unicode') or Windows-1256
                for raw in lines:
                    if not raw or raw in seen or raw in removed or checker.is_side_file(raw):
                        continue
                    seen.add(raw)
                    src_of[raw] = os.path.basename(path)
                    if raw in existing:
                        continue
                    p = parse(raw)
                    vals = [p.get(k) for k in FILE_FIELDS]
                    self.db.execute(
                        'INSERT INTO files(raw, list_file, added, title, alt_title, year, resolution, source, codec, '
                        'bit10, hdr, audio, farsi_dubbed, dual_audio, softsub, edition, grp) '
                        'VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)',
                        [raw, os.path.basename(path), now()] + vals)
                    new += 1
            # movies that come from the movie folders are kept or dropped by scan_media, not by the lists
            if folders_set:  # a movie from a movie folder is kept by scan_media, or left alone while the drive is away
                self.db.execute('UPDATE files SET present=0 WHERE list_file IS NOT ?', (FOLDER_SRC,))
            else:
                self.db.execute('UPDATE files SET present=0')
            self.db.executemany('UPDATE files SET present=1, list_file=CASE WHEN list_file=? THEN ? ELSE list_file END WHERE raw=?',
                                [(FOLDER_SRC, src_of[r], r) for r in seen])
            # IMDb choices made in the list check for lines that were just added
            for raw, tc in self.db.execute('SELECT raw, tconst FROM forced').fetchall():
                if raw in seen:
                    self.db.execute('UPDATE files SET tconst=?, conf=?, manual=1, matched=1 WHERE raw=?',
                                    (tc, 'manual' if tc else 'none', raw))
                    self.db.execute('DELETE FROM forced WHERE raw=?', (raw,))
            self.db.commit()
        self._reparse_if_needed()
        gone = self.db.execute('SELECT count(*) FROM files WHERE present=0').fetchone()[0]
        self.log(f'Scanned {len(seen)} entries: {new} new, {gone} no longer in list')

    def use_folders(self):
        """Can the movie folders be read now? They are always a source of movies."""
        return any(d and os.path.isdir(d) for d in self.settings().get('media_dirs') or [])

    def scan_media(self):
        """Find the real video files in the movie folders (settings 'media_dirs'): path and size for every list
        entry, and the videos that are in no list (they become library entries, see _folders_to_library). Read-only."""
        dirs = [d for d in self.settings().get('media_dirs') or [] if d and os.path.isdir(d)]
        if not dirs:  # a drive that is away: the movies from it stay as they are
            self.log('The movie folders cannot be read now (is the drive connected?)')
            return
        wanted = {r['raw'].lower(): r['raw'] for r in self.db.execute('SELECT raw FROM files WHERE present=1')}
        removed = {r[0].lower() for r in self.db.execute('SELECT raw FROM removed')}
        found, unlisted, n = [], [], 0
        real_raws = set()    # list entries that have a real video file
        real_dirs = set()    # folders that hold a real video, or have one somewhere below
        named_dirs = {}      # folders named like a list entry -> that entry
        for root in dirs:
            rootn = os.path.normpath(root)
            for dirpath, dirnames, names in os.walk(root):
                dirnames[:] = [d for d in dirnames if not d.startswith(('$', '.'))]
                parent = os.path.basename(dirpath)
                dn = os.path.normpath(dirpath)
                if dn != rootn and parent.lower() in wanted:
                    named_dirs[dn] = wanted[parent.lower()]
                for name in names:
                    stem, ext = os.path.splitext(name)
                    if ext.lower() not in checker.VIDEO_EXT:
                        continue
                    n += 1
                    if n % 200 == 0:
                        self.progress(f'Reading movie folders ({n} videos)', n, 0)
                    path = os.path.join(dirpath, name)
                    try:
                        st = os.stat(path)
                    except OSError:
                        continue
                    raw = wanted.get(stem.lower()) or wanted.get(parent.lower())
                    if is_real_video(stem, st.st_size):
                        d = dn
                        while d not in real_dirs:
                            real_dirs.add(d)
                            up = os.path.dirname(d)
                            if d == rootn or up == d:
                                break
                            d = up
                        if raw:
                            real_raws.add(raw)
                    if raw:
                        found.append((path, raw, st.st_size, st.st_mtime))
                        continue
                    if stem.lower() in removed or parent.lower() in removed:
                        continue  # taken out of the list on purpose: not 'on disk but not in your lists'
                    # in no list: skip samples, trailers, extras, episodes and tiny files
                    if st.st_size < 30_000_000 or checker.EPISODE_RE.search(stem) or (
                            checker.EXTRA_RE.search(stem) and st.st_size < 1_500_000_000):
                        continue
                    cand = stem
                    if not checker.YEAR_IN.search(stem) and checker.YEAR_IN.search(parent) and os.path.normpath(dirpath) != os.path.normpath(root):
                        cand = parent  # 'movie.mkv' inside 'Heat (1995) 1080p'
                    unlisted.append((path, cand, st.st_size, st.st_mtime))
        # a folder named like a list entry that holds no real video: only junk (.nfo, .jpg, .url, ...), or empty
        junk = []
        for d, raw in sorted(named_dirs.items()):
            if d in real_dirs or raw in real_raws:
                continue
            cnt, size, sample = folder_contents(d)
            junk.append((d, raw, cnt, size, json.dumps(sample)))
        with self.lock:
            self.db.execute('DELETE FROM novideo')
            self.db.executemany('INSERT OR REPLACE INTO novideo(path, raw, nfiles, size, names) VALUES (?,?,?,?,?)', junk)
            self.db.execute('DELETE FROM fileinfo')
            self.db.executemany('INSERT OR REPLACE INTO fileinfo(path, raw, size, mtime) VALUES (?,?,?,?)', found)
            self.db.execute('DELETE FROM mediainfo WHERE path NOT IN (SELECT path FROM fileinfo)')
            self.db.execute('DELETE FROM unlisted')
            self.db.executemany('INSERT OR REPLACE INTO unlisted(path, raw, size, mtime) VALUES (?,?,?,?)', unlisted)
            self.db.commit()
        taken = self._folders_to_library(found, unlisted)
        with self.lock:  # the movies just taken from the folders: path and size, so Show in folder works at once
            self.db.executemany('INSERT OR REPLACE INTO fileinfo(path, raw, size, mtime) VALUES (?,?,?,?)', unlisted)
            self.db.execute('DELETE FROM mediainfo WHERE path NOT IN (SELECT path FROM fileinfo)')
            self.db.commit()
        self.version += 1
        self.log(f'Movie folders: {len(found)} of {n} videos belong to your list, {len(unlisted) - taken} are in no list, '
                 f'{len(junk)} list folders have no video file' + (f'; {taken} movies taken from the folders themselves' if taken else ''))

    def _folders_to_library(self, found, unlisted):
        """The movie folders are a source of movies: every real video in no list becomes a library entry (list_file
        FOLDER_SRC), and such an entry goes away when its video does. Returns how many entries were added."""
        cands = list(dict.fromkeys(u[1] for u in unlisted))
        keep = set(cands) | {f[1] for f in found}
        added = 0
        with self.lock:
            existing = {r['raw']: r for r in self.db.execute('SELECT id, raw, list_file, present FROM files')}
            for raw in cands:
                if raw in existing:
                    old = existing[raw]
                    if not old['present'] and old['list_file'] != FOLDER_SRC:  # dropped from its list, but the video is here
                        self.db.execute('UPDATE files SET list_file=?, present=1 WHERE id=?', (FOLDER_SRC, old['id']))
                        added += 1
                    continue
                p = parse(raw)
                self.db.execute(
                    'INSERT INTO files(raw, list_file, added, title, alt_title, year, resolution, source, codec, '
                    'bit10, hdr, audio, farsi_dubbed, dual_audio, softsub, edition, grp) '
                    'VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)',
                    [raw, FOLDER_SRC, now()] + [p.get(k) for k in FILE_FIELDS])
                added += 1
            self.db.executemany('UPDATE files SET present=? WHERE id=?',
                                [(1 if r['raw'] in keep else 0, r['id']) for r in existing.values() if r['list_file'] == FOLDER_SRC])
            self.db.execute('DELETE FROM unlisted WHERE raw IN (SELECT raw FROM files WHERE present=1)')
            self.db.commit()
        return added

    def unlisted_items(self):
        db = self.reader()
        try:
            return [dict(r) for r in db.execute('SELECT path, raw, size FROM unlisted ORDER BY raw COLLATE NOCASE')]
        finally:
            db.close()

    def probe_media(self):
        """Read length, picture size, audio languages and subtitles of the files in the movie folders with ffprobe.
        Only new or changed files are read; failures are remembered so they are not retried every time."""
        ff = probe.find_ffprobe(self.settings())
        if not ff:
            raise RuntimeError('ffprobe was not found. Install ffmpeg (winget install Gyan.FFmpeg) or set its path in Settings.')
        todo = self.db.execute('SELECT f.path, f.size, f.mtime FROM fileinfo f LEFT JOIN mediainfo m ON m.path=f.path '
                               'WHERE m.path IS NULL OR m.size IS NOT f.size OR m.mtime IS NOT f.mtime').fetchall()
        self.db.execute('DELETE FROM mediainfo WHERE path NOT IN (SELECT path FROM fileinfo)')
        self.db.commit()

        def one(r):
            self.check_stop()
            return r, probe.probe_file(ff, r['path'])
        done, batch = 0, []
        with ThreadPoolExecutor(4) as ex:
            for r, info in ex.map(one, todo):
                done += 1
                if done % 10 == 0:
                    self.progress('Reading audio and subtitles with ffprobe', done, len(todo))
                i = info or {}
                batch.append((r['path'], r['size'], r['mtime'], 1 if info else 0, i.get('duration'), i.get('width'), i.get('height'),
                              i.get('vcodec'), i.get('bitrate'), json.dumps(i.get('audio') or []), json.dumps(i.get('subs') or []),
                              i.get('n_audio'), i.get('n_subs')))
                if len(batch) >= 100:
                    self._save_probe(batch)
                    batch = []
        self._save_probe(batch)
        self.version += 1
        self.log(f'ffprobe: read {len(todo)} files')

    def _save_probe(self, batch):
        if not batch:
            return
        with self.lock:
            self.db.executemany('INSERT OR REPLACE INTO mediainfo(path, size, mtime, ok, duration, width, height, vcodec, bitrate, '
                                'audio, subs, n_audio, n_subs) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)', batch)
            self.db.commit()

    def probe_status(self):
        db = self.reader()
        try:
            q = lambda sql: db.execute(sql).fetchone()[0]
            return {'ffprobe': probe.find_ffprobe(self.settings()), 'files': q('SELECT count(*) FROM fileinfo'),
                    'read': q('SELECT count(*) FROM mediainfo WHERE ok=1'), 'failed': q('SELECT count(*) FROM mediainfo WHERE ok=0')}
        finally:
            db.close()

    def _reparse_if_needed(self):
        """Re-parse stored names when the parser improved; changed titles get re-matched."""
        if self.settings().get('parser_version') == PARSER_VERSION:
            return
        changed = 0
        with self.lock:
            for r in self.db.execute('SELECT * FROM files').fetchall():
                p = parse(r['raw'])
                vals = [p.get(k) for k in FILE_FIELDS]
                rematch = not r['manual'] and (p['title'], p['alt_title'], p['year']) != (r['title'], r['alt_title'], r['year'])
                changed += rematch
                self.db.execute(
                    'UPDATE files SET title=?, alt_title=?, year=?, resolution=?, source=?, codec=?, bit10=?, hdr=?, audio=?, '
                    'farsi_dubbed=?, dual_audio=?, softsub=?, edition=?, grp=?' + (', matched=0' if rematch else '') +
                    ' WHERE id=?', vals + [r['id']])
            self.db.commit()
        s = self.settings()
        s['parser_version'] = PARSER_VERSION
        with open(self.settings_path, 'w', encoding='utf-8') as f:
            json.dump(s, f, indent=2)
        self.log(f'Re-read file names with improved parser ({changed} to re-match)')

    # ---------- matching (see matcher.py) ----------
    def match_all(self, only_unmatched=True):
        ix = self.matcher.open()
        rows = self.db.execute('SELECT * FROM files WHERE manual=0' + (' AND matched=0' if only_unmatched else '')).fetchall()
        res = []
        for n, f in enumerate(rows):
            if n % 200 == 0:
                self.progress('Matching titles with IMDb', n, len(rows))
            p = dict(f)
            tc, conf, sc = self.matcher.pick(ix, p)
            res.append((tc, conf, sc, f['id']))
        with self.lock:
            self.db.executemany('UPDATE files SET tconst=?, conf=?, score=?, matched=1 WHERE id=?', res)
            self.db.commit()
        none = [r for r in res if r[0] is None]
        self.log(f'Matched {len(res) - len(none)} of {len(res)} offline')
        if none and self.settings().get('online_fallback'):
            self.online_match([r[3] for r in none])
        self.import_titles(ix)
        ix.close()

    def online_match(self, file_ids):
        found = 0
        for n, fid in enumerate(file_ids):
            self.progress('Searching IMDb online for unmatched titles', n, len(file_ids))
            f = self.db.execute('SELECT * FROM files WHERE id=?', (fid,)).fetchone()
            try:
                best = self.matcher.online_find(f)
            except Exception as e:
                self.log(f'Online search failed: {e}')
                return
            if best:
                found += 1
                with self.lock:
                    self.db.execute("UPDATE files SET tconst=?, conf='online', score=? WHERE id=?",
                                    (best['tconst'], 0, fid))
                    self.db.commit()
        self.log(f'Online search matched {found} of {len(file_ids)} more')

    def import_titles(self, ix, force_ids=None):
        """Copy basic info for matched tconsts from the index into the library."""
        if force_ids:
            ids = list(force_ids)
        else:
            ids = [r[0] for r in self.db.execute(
                'SELECT DISTINCT tconst FROM files WHERE tconst IS NOT NULL AND tconst NOT IN (SELECT tconst FROM titles)')]
        rows = []
        for i in ids:
            r = ix.execute('SELECT * FROM titles WHERE id=?', (i,)).fetchone()
            if not r:
                rows.append((i, None, None, None, None, None, None, None, None, None, None, None, None))
                continue
            akas = ix.execute('SELECT title, region, lang, types FROM akas WHERE id=?', (i,)).fetchall()
            en, fa = self._pick_akas(r, akas)
            alist = sorted({a['title'] for a in akas} - {r['title'], r['orig']})
            rows.append((i, imdb_index.TYPE_NAMES.get(r['type']), r['title'], r['orig'], r['year'], r['endyear'],
                         r['runtime'], r['genres'], r['rating'], r['votes'], en, fa, json.dumps(alist[:25], ensure_ascii=False)))
        with self.lock:
            for row in rows:
                self.db.execute(
                    'INSERT INTO titles(tconst,type,title,orig,year,endyear,runtime,genres,rating,votes,en_title,fa_title,akas,updated) '
                    'VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(tconst) DO UPDATE SET type=excluded.type, title=excluded.title, '
                    'orig=excluded.orig, year=excluded.year, endyear=excluded.endyear, runtime=excluded.runtime, genres=excluded.genres, '
                    'rating=excluded.rating, votes=excluded.votes, en_title=excluded.en_title, '
                    'fa_title=coalesce(titles.fa_title, excluded.fa_title), akas=excluded.akas, updated=excluded.updated',
                    row + (now(),))
            self.db.commit()

    @staticmethod
    def _pick_akas(r, akas):
        def best(cands):
            cands.sort(key=lambda a: (0 if 'imdbDisplay' in (a['types'] or '') else 1))
            return cands[0]['title'] if cands else None
        en = None
        for reg in ('US', 'GB', 'CA', 'AU', 'XWW'):
            en = best([a for a in akas if a['region'] == reg and not (a['types'] or '').startswith('working')])
            if en:
                break
        en = en or best([a for a in akas if a['lang'] == 'en']) or r['title']
        fa = best([a for a in akas if a['region'] == 'IR' or a['lang'] == 'fa'])
        return en, fa

    # ---------- cast & crew ----------
    def enrich(self, ids=None):
        if ids is None:
            ids = [r[0] for r in self.db.execute('SELECT tconst FROM titles WHERE enriched=0 AND type IS NOT NULL')]
        if not ids:
            return
        self.log(f'Reading cast & crew for {len(ids)} titles')
        credits, names, basics = imdb_index.read_people(self.imdb_dir, ids, self.progress)
        with self.lock:
            self.db.executemany('INSERT OR REPLACE INTO people(nconst, name, birth, death, prof) VALUES (?,?,?,?,?)',
                                [(n, nm, *basics.get(n, (None, None, ''))) for n, nm in names.items()])
            for i, c in credits.items():
                nm = lambda n: names.get(n, f'nm{n:07d}')
                d = json.dumps([[n, nm(n)] for n in c['directors']], ensure_ascii=False)
                w = json.dumps([[n, nm(n)] for n in c['writers']], ensure_ascii=False)
                cast = json.dumps([[n, nm(n), ch] for n, ch in c['cast']], ensure_ascii=False)
                crew = json.dumps([[n, nm(n), job] for n, job in c['crew']], ensure_ascii=False)
                self.db.execute('UPDATE titles SET directors=?, writers=?, cast_=?, crew=?, enriched=1 WHERE tconst=?',
                                (d, w, cast, crew, i))
            self.db.commit()
        self.log('Cast & crew saved')

    def fill_people_basics(self):
        """Birth and death year and professions for people stored before they were kept (one pass over name.basics)."""
        if not os.path.exists(os.path.join(self.imdb_dir, 'name.basics.tsv.gz')):
            return
        ids = [r[0] for r in self.db.execute('SELECT nconst FROM people WHERE prof IS NULL')]
        if not ids:
            return
        self.log(f'Reading birth years and professions for {len(ids)} people')
        basics = imdb_index.read_basics(self.imdb_dir, ids, self.progress)
        with self.lock:
            self.db.executemany('UPDATE people SET birth=?, death=?, prof=? WHERE nconst=?',
                                [(*basics.get(n, (None, None, '')), n) for n in ids])
            self.db.commit()

    # ---------- people profiles ----------
    def _fetch_person(self, tm, nconst):
        """Ask TMDB for one person, keep the photo file and the profile. Returns the cached row (data json, photo flag)."""
        p = tm.person(nconst)
        photo = 0
        if p and p.get('photo_path'):
            data = online.get_bytes(online.TMDB_IMG + 'w342' + p['photo_path'])
            if data:
                with open(self.person_photo_path(nconst), 'wb') as f:
                    f.write(data)
                photo = 1
        data = json.dumps(p, ensure_ascii=False) if p else None
        with self.lock:
            self.db.execute('INSERT OR REPLACE INTO persons(nconst, tmdb_id, data, photo, fetched) VALUES (?,?,?,?,?)',
                            (nconst, p and p['tmdb_id'], data, photo, now()))
            self.db.commit()
        return {'data': data, 'photo': photo}

    def people_to_scan(self, scope):
        """People of the library by scope: 'directors' (directors and writers), 'main' (+ the first five billed actors),
        'all' (everyone with a credit on your movies)."""
        ids = []
        seen = set()
        db = self.reader()
        try:
            for r in db.execute('SELECT directors, writers, cast_, crew FROM titles WHERE tconst IN (SELECT tconst FROM files WHERE present=1)'):
                groups = [json.loads(r['directors'] or '[]'), json.loads(r['writers'] or '[]')]
                if scope in ('main', 'all'):
                    cast = json.loads(r['cast_'] or '[]')
                    groups.append(cast if scope == 'all' else cast[:5])
                if scope == 'all':
                    groups.append(json.loads(r['crew'] or '[]'))
                for g in groups:
                    for p in g:
                        if p[0] not in seen:
                            seen.add(p[0])
                            ids.append(p[0])
            done = {r[0] for r in db.execute('SELECT nconst FROM persons')}
        finally:
            db.close()
        return [i for i in ids if i not in done]

    def fetch_people(self, scope='main'):
        """Job: fetch TMDB profiles and photos for the people of your library, so profile windows open at once. Needs a TMDB key."""
        key = self.settings().get('tmdb_key')
        if not key:
            raise RuntimeError('Add a TMDB key in Settings first.')
        ids = self.people_to_scan(scope)
        if not ids:
            self.log('People: all profiles are already fetched')
            return
        tm = online.Tmdb(key)
        self.log(f'People: fetching {len(ids)} profiles')
        done, errors = [0], [0]

        def one(i):
            self.check_stop()
            try:
                self._fetch_person(tm, i)
            except Exception as e:
                errors[0] += 1
                if errors[0] == 1:
                    self.log(f'TMDB error (people): {e}')
            done[0] += 1
            if done[0] % 10 == 0:
                self.progress('Fetching people profiles from TMDB', done[0], len(ids))
        with ThreadPoolExecutor(24) as ex:  # a person takes two API calls and a photo; Tmdb paces all threads to the 50 a second limit
            list(ex.map(one, ids))
        self.version += 1
        self.log(f'People: {len(ids) - errors[0]} profiles fetched' + (f', {errors[0]} failed' if errors[0] else ''))

    def people_status(self):
        db = self.reader()
        try:
            q = lambda sql: db.execute(sql).fetchone()[0]
            fetched, photos = q('SELECT count(*) FROM persons WHERE data IS NOT NULL'), q('SELECT count(*) FROM persons WHERE photo=1')
        finally:
            db.close()
        s = self.settings()
        return {'tmdb': bool(s.get('tmdb_key')), 'fetched': fetched, 'photos': photos,
                'todo': {sc: len(self.people_to_scan(sc)) for sc in ('directors', 'main', 'all')}}
    def person_photo_path(self, nconst):
        return os.path.join(self.people_dir, f'nm{nconst:07d}.jpg')

    def person_profile(self, nconst, refresh=False):
        """What the profile window shows apart from the person's movies in your library: IMDb facts from the offline data and,
        with a TMDB key, photo, biography, places and the full filmography (fetched once, kept 90 days)."""
        db = self.reader()
        try:
            base = db.execute('SELECT name, birth, death, prof FROM people WHERE nconst=?', (nconst,)).fetchone()
            cached = db.execute('SELECT data, photo, fetched FROM persons WHERE nconst=?', (nconst,)).fetchone()
        finally:
            db.close()
        key = self.settings().get('tmdb_key')
        out = {'nconst': nconst, 'name': base['name'] if base else None, 'birth': base['birth'] if base else None,
               'death': base['death'] if base else None, 'prof': [p for p in (base['prof'] or '').split(',') if p] if base else [],
               'tmdb': bool(key), 'profile': None, 'photo': False, 'error': None}
        stale = not cached or refresh or (time.time() - time.mktime(time.strptime(cached['fetched'], '%Y-%m-%d %H:%M:%S'))) > 90 * 86400
        if key and stale:
            try:
                cached = self._fetch_person(online.Tmdb(key), nconst)
            except Exception as e:
                out['error'] = f'{type(e).__name__}: {e}'
        if cached and cached['data']:
            out['profile'] = json.loads(cached['data'])
            out['photo'] = bool(cached['photo']) and os.path.exists(self.person_photo_path(nconst))
        return out

    # ---------- posters ----------
    def poster_path(self, tconst):
        return os.path.join(self.poster_dir, f'tt{tconst:07d}.jpg')

    def fetch_posters(self, retry=False):
        s = self.settings()
        if not s.get('online_posters'):
            return
        q = 'SELECT tconst, poster_url FROM titles WHERE poster IS NULL'
        if not retry:
            q += ' AND poster_tried=0'
        ids = self.db.execute(q).fetchall()
        if not ids:
            return
        done = [0]
        ok = [0]

        def one(row):
            self.check_stop()
            i, url = row['tconst'], row['poster_url']
            path = self.poster_path(i)
            try:
                if not url:
                    url = online.imdb_poster_url(i)
                if url:
                    data = online.get_bytes(online.sized_amazon(url, 400))
                    with open(path, 'wb') as f:
                        f.write(data)
                    ok[0] += 1
                    return i, url, os.path.basename(path)
            except Exception:
                pass
            return i, url, None

        results = []
        with ThreadPoolExecutor(10) as ex:
            for r in ex.map(one, ids):
                results.append(r)
                done[0] += 1
                if done[0] % 25 == 0:
                    self.progress(f'Downloading posters ({ok[0]} saved)', done[0], len(ids))
                if len(results) >= 100:
                    self._save_posters(results)
                    results = []
        self._save_posters(results)
        self.log(f'Posters: {ok[0]} downloaded, {len(ids) - ok[0]} not available')

    def _save_posters(self, results):
        with self.lock:
            self.db.executemany('UPDATE titles SET poster_url=coalesce(?, poster_url), poster=?, poster_tried=1 WHERE tconst=?',
                                [(u, p, i) for i, u, p in results])
            self.db.commit()
        self.version += 1

    # ---------- TMDB ----------
    def fetch_tmdb(self, ids=None):
        s = self.settings()
        if not s.get('tmdb_key'):
            return
        tm = online.Tmdb(s['tmdb_key'])
        if ids is None:
            ids = [r[0] for r in self.db.execute('SELECT tconst FROM titles WHERE tmdb_done=0 AND type IS NOT NULL')]
        if not ids:
            return
        done = [0]

        def one(i):
            self.check_stop()
            try:
                return i, tm.details(i)
            except Exception as e:
                return i, e

        batch = []
        errors = 0
        with ThreadPoolExecutor(6) as ex:
            for i, d in ex.map(one, ids):
                done[0] += 1
                if done[0] % 20 == 0:
                    self.progress('Fetching plots & details from TMDB', done[0], len(ids))
                if isinstance(d, Exception):
                    errors += 1
                    if errors == 1:
                        self.log(f'TMDB error: {d}')
                    continue
                batch.append((i, d))
                if len(batch) >= 50:
                    self._save_tmdb(batch)
                    batch = []
        self._save_tmdb(batch)
        self.log(f'TMDB details fetched for {len(ids) - errors} titles' + (f' ({errors} errors)' if errors else ''))
        # titles without an IMDb-side poster may have one on TMDB
        self.fetch_posters(retry=True)

    def _save_tmdb(self, batch):
        with self.lock:
            for i, d in batch:
                d = d or {}
                self.db.execute(
                    'UPDATE titles SET tmdb_done=1, tmdb_id=?, plot=?, tagline=?, orig_lang=?, languages=?, countries=?, '
                    'fa_title=coalesce(?, fa_title), poster_url=coalesce(poster_url, ?), backdrop_url=?, tmdb_cast=?, collection=? '
                    'WHERE tconst=?',
                    (d.get('tmdb_id'), d.get('plot'), d.get('tagline'), d.get('orig_lang'),
                     json.dumps(d.get('languages') or [], ensure_ascii=False),
                     json.dumps(d.get('countries') or [], ensure_ascii=False), fatitles.normalize(d.get('fa_title')),
                     d.get('poster_url'), d.get('backdrop_url'),
                     json.dumps(d.get('tmdb_cast') or [], ensure_ascii=False), d.get('collection'), i))
            self.db.commit()
        self.version += 1

    # ---------- manual fixes ----------
    def set_match(self, file_id, tconst):
        with self.lock:
            self.db.execute("UPDATE files SET tconst=?, conf=?, manual=1, matched=1 WHERE id=?",
                            (tconst, 'manual' if tconst else 'none', file_id))
            self.db.commit()
        if not tconst:
            self.version += 1
            return
        have = self.db.execute('SELECT enriched FROM titles WHERE tconst=?', (tconst,)).fetchone()
        if have and have[0]:
            self.version += 1
            return
        ix = self.matcher.open()
        self.import_titles(ix, [tconst])
        ix.close()
        self.version += 1
        # people + poster + TMDB in the background
        self.start(lambda: (self.enrich([tconst]), self.fetch_posters(), self.fetch_tmdb([tconst])), queue=True)

    def lookup(self, file_id, query=None):
        f = dict(self.db.execute('SELECT * FROM files WHERE id=?', (file_id,)).fetchone())
        out = []
        seen = set()
        if os.path.exists(self.index_path):
            ix = self.matcher.open()
            p = dict(f)
            if query:
                from .parse import parse as _p
                q = _p(query)
                p['title'], p['alt_title'] = q['title'], q['alt_title']
                p['year'] = q['year'] or None
            for sc, r, how in self.matcher.candidates(ix, p) + self.matcher.fuzzy(ix, p, cutoff=75):
                if r['id'] in seen:
                    continue
                seen.add(r['id'])
                out.append({'tconst': r['id'], 'title': r['title'], 'orig': r['orig'], 'year': r['year'],
                            'type': imdb_index.TYPE_NAMES.get(r['type']), 'votes': r['votes'],
                            'rating': r['rating'], 'how': how})
            ix.close()
        try:
            q = query or (f'{f["title"]} {f["year"]}' if f['year'] else f['title'])
            for r in online.imdb_suggest(q):
                if r['tconst'] not in seen:
                    seen.add(r['tconst'])
                    out.append({'tconst': r['tconst'], 'title': r['title'], 'year': r['year'], 'type': r['kind'],
                                'stars': r['stars'], 'image': r['image'], 'how': 'online'})
        except Exception:
            pass
        return out

    def set_user(self, mkey, data):
        self.set_user_many([mkey], data)

    def set_user_many(self, mkeys, data):
        """Favorite / watched / rating / notes for one or many movies in one transaction.
        Marking a movie watched stamps the date (the Watch next dialog shows the history)."""
        allowed = {k: data[k] for k in ('fav', 'watched', 'myrating', 'notes') if k in data}
        if not allowed or not mkeys:
            return 0
        if 'watched' in allowed:
            allowed['watched_at'] = now() if allowed['watched'] else None
        sets = ', '.join(f'{k}=?' for k in allowed)
        with self.lock:
            for mkey in mkeys:
                self.db.execute('INSERT OR IGNORE INTO user(mkey) VALUES (?)', (mkey,))
                if 'watched' in allowed and allowed['watched']:  # keep the first date when it was already watched
                    self.db.execute('UPDATE user SET watched_at=coalesce(watched_at, ?) WHERE mkey=?', (allowed['watched_at'], mkey))
                    vals = {k: v for k, v in allowed.items() if k != 'watched_at'}
                    self.db.execute(f'UPDATE user SET {", ".join(f"{k}=?" for k in vals)}, updated=? WHERE mkey=?',
                                    list(vals.values()) + [now(), mkey])
                else:
                    self.db.execute(f'UPDATE user SET {sets}, updated=? WHERE mkey=?', list(allowed.values()) + [now(), mkey])
            self.db.commit()
        self.version += 1
        return len(mkeys)

    def accept(self, file_id):
        """'This match is right': keep the current IMDb id as a manual match."""
        with self.lock:
            self.db.execute("UPDATE files SET conf='manual', manual=1 WHERE id=? AND tconst IS NOT NULL", (file_id,))
            self.db.commit()
        self.version += 1

    # ---------- remove from the list ----------
    def remove_from_list(self, file_ids):
        """Take entries out of the library: they are hidden and every later scan skips them. The user's list files
        are not touched, and Settings -> Maintenance can bring an entry back. Returns how many were removed."""
        ids = sorted({int(i) for i in file_ids or []})
        if not ids:
            return 0
        q = ','.join('?' * len(ids))
        with self.lock:
            rows = self.db.execute(
                f'SELECT f.raw, f.tconst, coalesce(t.title, f.title, f.raw) AS name FROM files f '
                f'LEFT JOIN titles t ON t.tconst=f.tconst WHERE f.id IN ({q}) AND f.present=1', ids).fetchall()
            self.db.executemany('INSERT OR REPLACE INTO removed(raw, tconst, title, removed) VALUES (?,?,?,?)',
                                [(r['raw'], r['tconst'], r['name'], now()) for r in rows])
            self.db.execute(f'UPDATE files SET present=0 WHERE id IN ({q})', ids)
            self.db.commit()
        self.version += 1
        self.log(f'Removed {len(rows)} entries from the list')
        return len(rows)

    def removed_items(self):
        db = self.reader()
        try:
            return [dict(r) for r in db.execute('SELECT raw, tconst, title, removed FROM removed ORDER BY removed DESC, raw')]
        finally:
            db.close()

    def restore_removed(self, raws):
        raws = [str(r) for r in raws or []]
        if not raws:
            return 0
        q = ','.join('?' * len(raws))
        with self.lock:
            self.db.execute(f'DELETE FROM removed WHERE raw IN ({q})', raws)
            # the next scan drops it again if its line is no longer in a list file
            n = self.db.execute(f'UPDATE files SET present=1 WHERE raw IN ({q})', raws).rowcount
            self.db.commit()
        self.version += 1
        return n

    # ---------- posters added by hand ----------
    @staticmethod
    def poster_bytes(source):
        """Image bytes from what the user gave: a web address or the path of an image file on this PC."""
        src = str(source or '').strip().strip('"')
        if not src:
            raise ValueError('Choose an image file, or paste a web address or a file path.')
        if src.lower().startswith(('http://', 'https://')):
            try:
                data = online.get_bytes(src)
            except Exception as e:
                raise ValueError(f'The address could not be opened: {e}')
        else:
            if src.lower().startswith('file:///'):
                src = src[8:].replace('/', os.sep)
            if not os.path.isfile(src):
                raise ValueError('That is not a web address (http...) or a file on this PC.')
            if os.path.getsize(src) > POSTER_MAX:
                raise ValueError('The image is larger than 15 MB.')
            with open(src, 'rb') as f:
                data = f.read()
        if not image_type(data):
            raise ValueError('That is not an image (JPEG, PNG, WebP or GIF). For a web picture, copy the image address, not the page address.')
        return data

    def set_poster(self, tconst, data):
        """Use these image bytes as the poster of a movie. A later poster download never replaces it."""
        if not self.db.execute('SELECT 1 FROM titles WHERE tconst=?', (tconst,)).fetchone():
            raise ValueError('This movie has no IMDb match yet, so it cannot have a poster.')
        if not data:
            raise ValueError('The image is empty.')
        if len(data) > POSTER_MAX:
            raise ValueError('The image is larger than 15 MB.')
        if not image_type(data):
            raise ValueError('That is not an image (JPEG, PNG, WebP or GIF).')
        path = self.poster_path(tconst)
        with open(path + '.tmp', 'wb') as f:
            f.write(data)
        os.replace(path + '.tmp', path)
        with self.lock:
            self.db.execute('UPDATE titles SET poster=?, poster_tried=1 WHERE tconst=?', (os.path.basename(path), tconst))
            self.db.commit()
        self.version += 1

    def clear_poster(self, tconst):
        """Remove the poster of a movie (it is fetched again only by 'Retry missing posters')."""
        try:
            os.remove(self.poster_path(tconst))
        except OSError:
            pass
        with self.lock:
            self.db.execute('UPDATE titles SET poster=NULL, poster_tried=1 WHERE tconst=?', (tconst,))
            self.db.commit()
        self.version += 1

    # ---------- problem list ----------
    def problems(self):
        ix = self.matcher.open() if os.path.exists(self.index_path) else None
        titles = {r['tconst']: r for r in self.db.execute('SELECT tconst, title, year, votes, rating, type FROM titles')}
        out = []
        where = {}  # list entry -> the places of its video files, for "Show in folder"
        for r in self.db.execute('SELECT raw, path FROM fileinfo ORDER BY path'):
            where.setdefault(r['raw'], []).append(r['path'])
        q = ','.join('?' * len(UNSURE))
        for f in self.db.execute(f'SELECT * FROM files WHERE present=1 AND manual=0 AND conf IN ({q}) ORDER BY title', UNSURE):
            t = titles.get(f['tconst'])
            cur = {'tconst': f['tconst'], 'title': t['title'], 'year': t['year'], 'votes': t['votes'],
                   'rating': t['rating'], 'type': t['type']} if t else None
            ck = (f['id'], f['tconst'], f['conf'])
            if ck not in self._cand_cache and ix:
                self._cand_cache[ck] = self.matcher.all_cands(ix, dict(f), [f['tconst']])
            out.append({'id': f['id'], 'raw': f['raw'], 'title': f['title'], 'alt': f['alt_title'], 'year': f['year'],
                        'conf': f['conf'], 'reason': reason(f['conf'], f['year'], t['year'] if t else None),
                        'current': cur, 'cands': self._cand_cache.get(ck, []), 'paths': where.get(f['raw'], [])})
        if ix:
            ix.close()
        order = {c: i for i, c in enumerate(['none', 'review', 'fuzzy', 'noyear', 'online'])}
        out.sort(key=lambda x: order.get(x['conf'], 9))
        return out

    def issues(self):
        """Duplicates, low quality and other library problems (see issues.py)."""
        db = self.reader()
        try:
            return issues.build(self, db)
        finally:
            db.close()

    def dismiss(self, ikey, undo=False):
        self.issue_rev += 1
        with self.lock:
            if undo:
                self.db.execute('DELETE FROM dismissed WHERE ikey=?', (ikey,))
            else:
                self.db.execute('INSERT OR IGNORE INTO dismissed(ikey) VALUES (?)', (ikey,))
            self.db.commit()

    # ---------- saved views, wishlist, backup ----------
    def list_views(self):
        return [{'id': r['id'], 'name': r['name'], 'state': json.loads(r['state'] or '{}')}
                for r in self.reader().execute('SELECT * FROM views ORDER BY name COLLATE NOCASE')]

    def save_view(self, name, state):
        name = (name or '').strip()[:60]
        if not name:
            raise ValueError('Give the view a name.')
        with self.lock:
            self.db.execute('INSERT OR REPLACE INTO views(name, state) VALUES (?,?)', (name, json.dumps(state)))
            self.db.commit()

    def delete_view(self, vid):
        with self.lock:
            self.db.execute('DELETE FROM views WHERE id=?', (vid,))
            self.db.commit()

    def wishlist_items(self):
        return [dict(r) for r in self.reader().execute('SELECT * FROM wishlist ORDER BY status DESC, id DESC')]

    def wish_add(self, items):
        """items: [{title, year, tconst, note, source}]. Returns how many were new."""
        added = 0
        with self.lock:
            for it in items:
                title = (it.get('title') or '').strip()
                if not title:
                    continue
                cur = self.db.execute('INSERT OR IGNORE INTO wishlist(title, year, tconst, note, source, added) VALUES (?,?,?,?,?,?)',
                                      (title, it.get('year'), it.get('tconst'), it.get('note'), it.get('source'), now()))
                added += cur.rowcount
            self.db.commit()
        return added

    def wish_set(self, wid, status=None, delete=False):
        with self.lock:
            if delete:
                self.db.execute('DELETE FROM wishlist WHERE id=?', (wid,))
            elif status in ('wanted', 'found'):
                self.db.execute('UPDATE wishlist SET status=? WHERE id=?', (status, wid))
            self.db.commit()

    def collections(self):
        db = self.reader()
        try:
            return extras.collections_view(db)
        finally:
            db.close()

    def save_full_backup(self, path, opts):
        return fullbackup.save_full(self, path, opts)

    def restore_full_backup(self, path, opts):
        self.restore_result = fullbackup.restore_full(self, path, opts)

    def set_fa_title(self, tconst, title):
        """Your own Persian title for a movie. None removes your edit (the TMDB title shows again); '' hides the Persian title."""
        with self.lock:
            if title is None:
                self.db.execute('DELETE FROM fa_titles WHERE tconst=?', (tconst,))
            else:
                self.db.execute("INSERT OR REPLACE INTO fa_titles(tconst, title, source) VALUES (?,?,'user')", (tconst, fatitles.normalize(title) or ''))
            self.db.commit()
        self.version += 1

    def make_backup(self):
        return backup.snapshot(self)

    def restore_backup(self, data=None, name=None):
        return backup.restore(self, backup.load(self, name) if name else data)

    # ---------- list check: compare another list / folder with the library ----------
    def start_check(self, src):
        if self.check['running']:
            return False
        if not os.path.exists(self.index_path):
            raise RuntimeError('The IMDb index is not built yet. Run Setup.bat or wait for the first update.')
        self.check = {'running': True, 'stage': 'Reading list', 'done': 0, 'total': 0, 'error': None, 'result': None}

        def run():
            try:
                self.check['result'] = checker.check_list(self, src, self._check_progress)
            except Exception as e:
                import traceback
                traceback.print_exc()
                self.check['error'] = f'{type(e).__name__}: {e}'
            finally:
                self.check['running'] = False
        threading.Thread(target=run, daemon=True).start()
        return True

    def _check_progress(self, stage, done=0, total=0):
        self.check.update(stage=stage, done=done, total=total)

    def add_from_check(self, items):
        """Append chosen names to '<list folder>/added.txt' and update the library.
        items: [{'raw': ..., 'choice': tconst or None}]; 'choice' is present only when the user
        picked the movie (None = 'not on IMDb')."""
        s = self.settings()
        have = {r[0] for r in self.db.execute('SELECT raw FROM files WHERE present=1')}
        new = [it for it in items if it.get('raw') and it['raw'].strip() not in have]
        if not new:
            return 0
        with self.lock:  # adding a name again undoes its removal
            self.db.executemany('DELETE FROM removed WHERE raw=?', [(it['raw'].strip(),) for it in new])
            self.db.commit()
        os.makedirs(s['list_dir'], exist_ok=True)
        path = os.path.join(s['list_dir'], 'added.txt')
        needs_nl = os.path.exists(path) and os.path.getsize(path) > 0 and not open(path, 'rb').read().endswith(b'\n')
        with open(path, 'a', encoding='utf-8') as f:
            if needs_nl:
                f.write('\n')
            for it in new:
                f.write(it['raw'].strip() + '\n')
        with self.lock:
            self.db.executemany('INSERT OR REPLACE INTO forced(raw, tconst) VALUES (?,?)',
                                [(it['raw'].strip(), int(it['choice']) if it['choice'] else None)
                                 for it in new if 'choice' in it])
            self.db.commit()
        self.start(self.full_update, queue=True)
        return len(new)

    # ---------- export for UI ----------
    def reader(self):
        """A separate read-only connection: WAL lets it read while a job writes, so the UI never waits for the lock."""
        db = sqlite3.connect(f'file:{self.db_path}?mode=ro', uri=True)
        db.row_factory = sqlite3.Row
        return db

    def movies(self, db=None):
        """One entry per movie (files grouped by IMDb id); unmatched files stand alone.
        Pass a reader() connection to read while an update holds the lock."""
        db = db or self.db
        users = {r['mkey']: dict(r) for r in db.execute('SELECT * FROM user')}
        titles = {r['tconst']: r for r in db.execute('SELECT * FROM titles')}
        groups = {}
        order = []
        for f in db.execute('SELECT * FROM files WHERE present=1 ORDER BY id'):
            mk = f'tt{f["tconst"]:07d}' if f['tconst'] else f'f{f["id"]}'
            if mk not in groups:
                groups[mk] = []
                order.append(mk)
            groups[mk].append(f)
        fa = {r['tconst']: r['title'] for r in db.execute('SELECT tconst, title FROM fa_titles')}  # reviewed and edited Persian titles win
        info = {}
        for r in db.execute('SELECT raw, path, size FROM fileinfo ORDER BY path'):
            info.setdefault(r['raw'], []).append((r['path'], r['size']))
        media = {r['path']: r for r in db.execute('SELECT * FROM mediainfo WHERE ok=1')}
        res_rank = {'2160p': 5, '1080p': 4, '720p': 3, '576p': 2, '480p': 1, 'SD': 1}
        out = []
        for mk in order:
            fs = groups[mk]
            f0 = fs[0]
            t = titles.get(f0['tconst']) if f0['tconst'] else None
            files = [{'id': f['id'], 'raw': f['raw'], 'res': f['resolution'], 'src': f['source'], 'codec': f['codec'],
                      'b10': f['bit10'], 'hdr': f['hdr'], 'audio': f['audio'], 'fa': f['farsi_dubbed'],
                      'dual': f['dual_audio'], 'sub': f['softsub'], 'ed': f['edition'], 'grp': f['grp'],
                      'conf': f['conf'], 'list': f['list_file'],
                      'size': max((x[1] for x in info.get(f['raw'], [])), default=None),
                      'paths': [x[0] for x in info.get(f['raw'], [])],
                      **self._media_fields(media, [x[0] for x in info.get(f['raw'], [])])} for f in fs]
            best = max((f['resolution'] for f in fs), key=lambda r: res_rank.get(r, 0))
            confs = [f['conf'] for f in fs]
            conf = 'manual' if 'manual' in confs else min(confs, key=lambda c: ['exact', 'good', 'noyear', 'online', 'review', 'fuzzy', 'none'].index(c) if c in ['exact', 'good', 'noyear', 'online', 'review', 'fuzzy', 'none'] else 9)
            m = {'k': mk, 'files': files, 'res': best, 'conf': conf, 'ptitle': f0['title'], 'palt': f0['alt_title'],
                 'pyear': f0['year']}
            if t:
                j = lambda v: json.loads(v) if v else []
                m.update({
                    'tt': mk, 'type': t['type'], 'title': t['title'] or f0['title'], 'orig': t['orig'],
                    'en': t['en_title'], 'fa_t': (fa.get(t['tconst'], t['fa_title']) or None), 'year': t['year'] or f0['year'], 'endyear': t['endyear'],
                    'runtime': t['runtime'], 'genres': (t['genres'] or '').split(',') if t['genres'] else [],
                    'rating': t['rating'], 'votes': t['votes'], 'akas': j(t['akas']),
                    'dir': j(t['directors']), 'wri': j(t['writers']), 'cast': j(t['cast_']), 'crew': j(t['crew']),
                    'poster': bool(t['poster']), 'plot': t['plot'], 'tagline': t['tagline'],
                    'countries': j(t['countries']), 'langs': j(t['languages']), 'olang': t['orig_lang'],
                    'backdrop': t['backdrop_url'], 'tcast': j(t['tmdb_cast']), 'coll': t['collection'],
                    'rt': t['rt'], 'mc': t['mc'],
                })
            else:
                m.update({'title': f0['title'] or f0['raw'], 'year': f0['year'], 'genres': []})
            u = users.get(mk)
            if u:
                m.update({'fav': u['fav'], 'watched': u['watched'], 'myrating': u['myrating'], 'notes': u['notes'],
                          'watched_at': u['watched_at']})
            out.append(m)
        return out

    @staticmethod
    def _media_fields(media, paths):
        """ffprobe facts for the first scanned path of a file (empty when it was not read)."""
        m = next((media[p] for p in paths if p in media), None)
        if not m:
            return {}
        return {'dur': m['duration'], 'w': m['width'], 'h': m['height'], 'vc': m['vcodec'], 'br': m['bitrate'],
                'alang': json.loads(m['audio'] or '[]'), 'slang': json.loads(m['subs'] or '[]')}

    def stats(self):
        q = lambda s: self.db.execute(s).fetchone()[0]
        return {
            'files': q('SELECT count(*) FROM files WHERE present=1'),
            'matched': q('SELECT count(*) FROM files WHERE present=1 AND tconst IS NOT NULL'),
            'titles': q('SELECT count(DISTINCT tconst) FROM files WHERE present=1 AND tconst IS NOT NULL'),
            'posters': q('SELECT count(*) FROM titles WHERE poster IS NOT NULL'),
            'enriched': q('SELECT count(*) FROM titles WHERE enriched=1'),
            'tmdb': q('SELECT count(*) FROM titles WHERE tmdb_done=1 AND tmdb_id IS NOT NULL'),
            'scores': q('SELECT count(*) FROM titles WHERE rt IS NOT NULL OR mc IS NOT NULL'),
            'problems': q("SELECT count(*) FROM files WHERE present=1 AND manual=0 AND conf IN ('review','fuzzy','online','noyear','none')"),
            'duplicates': issues.open_duplicates(self),
            'novideo': issues.open_novideo(self),
            'wishlist': q("SELECT count(*) FROM wishlist WHERE status='wanted'"),
            'unlisted': q('SELECT count(*) FROM unlisted'),
            'ratings_age': imdb_index.file_age_days(self.imdb_dir, 'title.ratings'),
            'index': os.path.exists(self.index_path),
            'datasets': imdb_index.have_files(self.imdb_dir),
            'imdb_dir': self.imdb_dir,
        }
