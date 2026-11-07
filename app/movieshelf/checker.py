"""List check: read a list of movies from a .txt / .csv file or a folder of video files,
rank release quality, and compare the list with the library."""
import csv
import functools
import io
import os
import re
import time

from . import imdb_index
from .matcher import UNSURE, reason
from .parse import parse

VIDEO_EXT = {'.mkv', '.mp4', '.avi', '.m4v', '.mov', '.wmv', '.ts', '.m2ts', '.mpg', '.mpeg', '.webm',
             '.flv', '.iso', '.vob', '.divx', '.ogm', '.rmvb', '.3gp'}
# a list line that names one of these is a subtitle, picture or other side file of a movie, not a movie
# ('.sub' is left out on purpose: 'Movie.2025.WEB-DL.Farsi.Sub' is a release name)
SIDE_EXT = {'.srt', '.idx', '.ass', '.ssa', '.vtt', '.sup', '.smi', '.sami', '.nfo', '.jpg', '.jpeg', '.png',
            '.gif', '.webp', '.url', '.sfv', '.md5', '.txt', '.db', '.ini', '.exe', '.zip', '.rar', '.7z'}
DISC_DIRS = {'video_ts', 'bdmv'}  # DVD / Blu-ray folder structures: the parent folder is the movie
EXTRA_RE = re.compile(r'(^|[ ._\-\[(])(sample|trailer|teaser|featurette|extras?|behind[ ._]the[ ._]scenes)([ ._\-\])]|$)', re.I)
EPISODE_RE = re.compile(r'\bS\d{1,2}[ ._]?E\d{1,3}\b|\b\d{1,2}x\d{2}\b', re.I)
YEAR_IN = re.compile(r'(?<![0-9])(18[89][0-9]|19[0-9]{2}|20[0-4][0-9])(?![0-9])')

RES_RANK = {'2160p': 5, '1080p': 4, '720p': 3, '576p': 2, '480p': 1, 'SD': 1}
SRC_RANK = {'BluRay': 5, 'WEB-DL': 4, 'WEBRip': 3, 'HDTV': 2, 'HDRip': 2, 'DVDRip': 1}


def is_side_file(name):
    """True for a list line like 'Heat.1995.1080p.srt': a subtitle or other file that sits beside a movie."""
    return os.path.splitext(str(name).strip().strip('"'))[1].lower() in SIDE_EXT


def read_text(path):
    with open(path, 'rb') as f:
        data = f.read()
    for enc in ('utf-8-sig', 'utf-16', 'cp1256', 'latin-1'):
        try:
            text = data.decode(enc)
            if enc == 'utf-16' and not data[:2] in (b'\xff\xfe', b'\xfe\xff'):
                continue
            return text
        except UnicodeDecodeError:
            continue
    return data.decode('utf-8', 'replace')


def entries_from_text(text, name=''):
    """One entry per line; a CSV gets its name / title+year / IMDb id columns detected."""
    lines = [l for l in text.splitlines() if l.strip()]
    if not lines:
        return []
    is_csv = name.lower().endswith(('.csv', '.tsv'))
    if not is_csv:
        head = lines[0].lower()
        is_csv = any(d in head for d in ',;\t') and any(w in head for w in ('title', 'name', 'imdb', 'file'))
    if not is_csv:
        return [{'raw': l.strip().strip('"')} for l in lines if not is_side_file(l)]
    try:
        dialect = csv.Sniffer().sniff(lines[0], delimiters=',;\t')
    except csv.Error:
        dialect = csv.excel
    rows = list(csv.reader(io.StringIO('\n'.join(lines)), dialect))
    header = [h.strip().lower() for h in rows[0]]

    def col(*names):
        for n in names:
            for i, h in enumerate(header):
                if h == n or h.replace(' ', '') == n:
                    return i
        for n in names:
            for i, h in enumerate(header):
                if n in h:
                    return i
        return None
    c_name = col('raw', 'release', 'filename', 'file', 'name', 'path')
    c_title = col('title', 'movie', 'english title', 'original title')
    c_year = col('year', 'release year')
    c_imdb = col('imdb', 'tconst', 'imdb id', 'imdbid', 'const')
    if c_name is None and c_title is None and c_imdb is None:
        c_name, data = 0, rows  # no header: first column holds the names
    else:
        data = rows[1:]
    out = []
    for r in data:
        g = lambda i: r[i].strip() if i is not None and i < len(r) else ''
        raw = g(c_name)
        if raw:
            raw = os.path.splitext(os.path.basename(raw))[0] if os.path.splitext(raw)[1].lower() in VIDEO_EXT else raw
        elif g(c_title):
            raw = g(c_title) + (f' {g(c_year)}' if g(c_year) and g(c_year) not in g(c_title) else '')
        m = re.search(r'tt\d{5,}', g(c_imdb))
        if is_side_file(raw):
            continue
        if raw or m:
            out.append({'raw': raw or m.group(0), 'tconst': int(m.group(0)[2:]) if m else None})
    return out


def entries_from_folder(root, progress=None):
    """Video files under root (recursive). Uses the folder name when the file name
    is generic (no year) or the movie is a DVD / Blu-ray folder."""
    out, seen_disc = [], set()
    n = 0
    for dirpath, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if not d.startswith(('.', '$')) and d.lower() not in ('extras', 'featurettes', 'samples', 'sample')]
        low = dirpath.lower().replace('\\', '/')
        parts = low.split('/')
        disc = next((i for i, p in enumerate(parts) if p in DISC_DIRS), None)
        if disc is not None:
            movie_dir = os.sep.join(dirpath.replace('/', os.sep).split(os.sep)[:disc])
            if movie_dir not in seen_disc:
                seen_disc.add(movie_dir)
                out.append({'raw': os.path.basename(movie_dir), 'path': movie_dir, 'size': _dir_size(movie_dir)})
            dirs[:] = []
            continue
        for fn in files:
            stem, ext = os.path.splitext(fn)
            if ext.lower() not in VIDEO_EXT:
                continue
            path = os.path.join(dirpath, fn)
            try:
                size = os.path.getsize(path)
            except OSError:
                size = None
            if EXTRA_RE.search(stem) and (size or 0) < 1_500_000_000:
                continue
            raw = stem
            parent = os.path.basename(dirpath)
            if not YEAR_IN.search(stem) and YEAR_IN.search(parent) and os.path.normpath(dirpath) != os.path.normpath(root):
                raw = parent  # 'movie.mkv' inside 'Heat (1995) 1080p'
            out.append({'raw': raw, 'path': path, 'size': size, 'episode': bool(EPISODE_RE.search(stem))})
            n += 1
            if progress and n % 200 == 0:
                progress(f'Reading folder ({n} videos)', n, 0)
    return out


def _dir_size(d):
    total = 0
    for dp, _, fs in os.walk(d):
        for f in fs:
            try:
                total += os.path.getsize(os.path.join(dp, f))
            except OSError:
                pass
    return total


def compare_quality(a, b):
    """>0 when parsed file a is better than b, <0 when worse, 0 when equal or unknown.
    Only facts known for both files count (a name without a source is not 'worse')."""
    ra, rb = RES_RANK.get(a.get('resolution')), RES_RANK.get(b.get('resolution'))
    if not ra or not rb:
        return 0
    if ra != rb:
        return ra - rb
    sa, sb = SRC_RANK.get(a.get('source')), SRC_RANK.get(b.get('source'))
    if sa and sb and sa != sb:
        return sa - sb
    for k in ('hdr', 'bit10'):
        if bool(a.get(k)) != bool(b.get(k)):
            return 1 if a.get(k) else -1
    ch = {'7.1': 3, '5.1': 2, '2.0': 1}
    ca, cb = ch.get(a.get('audio')), ch.get(b.get('audio'))
    if ca and cb and ca != cb:
        return ca - cb
    return 0


def quality_label(f):
    bits = [f.get('resolution') or '?', f.get('source'), f.get('codec'), 'HDR' if f.get('hdr') else None,
            '10-bit' if f.get('bit10') else None, f.get('audio'),
            'Farsi dubbed' if f.get('farsi_dubbed') else None, f.get('edition')]
    return ' '.join(b for b in bits if b)


def check_list(lib, src, progress):
    """Compare a list file / folder / pasted text (src: {'path'} or {'text', 'name'}) with the
    library: new, better / worse quality, other version, duplicates, not found, and what only the
    library has. Read-only; progress(stage, done, total) reports how far it got."""
    path = (src.get('path') or '').strip().strip('"')
    if path:
        if os.path.isdir(path):
            entries, label = entries_from_folder(path, progress), path
        elif os.path.isfile(path):
            entries, label = entries_from_text(read_text(path), path), path
        else:
            raise FileNotFoundError(f'Not found: {path}')
    else:
        entries, label = entries_from_text(src.get('text') or '', src.get('name') or ''), src.get('name') or 'pasted list'
    if not entries:
        raise ValueError('No movies found in that list. A .txt needs one name per line; a folder needs video files.')
    ix = lib.matcher.open()
    have_raw = {r['raw']: r['id'] for r in lib.db.execute('SELECT id, raw FROM files WHERE present=1')}
    mine_by_tc = {}
    for f in lib.db.execute('SELECT * FROM files WHERE present=1 AND tconst IS NOT NULL'):
        mine_by_tc.setdefault(f['tconst'], []).append(dict(f))
    ltitles = {r['tconst']: r for r in lib.db.execute('SELECT tconst, title, en_title, year FROM titles')}
    online_ok = lib.settings().get('online_fallback')
    online_left = 150  # keep a big unknown folder from taking forever
    rows, seen_raw = [], {}
    for n, e in enumerate(entries):
        if n % 50 == 0:
            progress('Matching with IMDb', n, len(entries))
        raw = e['raw']
        if raw in seen_raw:
            seen_raw[raw]['copies'] += 1
            continue
        p = parse(raw)
        tc, conf = e.get('tconst'), 'imdb id'
        if not tc:
            tc, conf, _ = lib.matcher.pick(ix, p)
        if not tc and online_ok and online_left > 0 and not e.get('episode'):
            online_left -= 1
            try:
                r = lib.matcher.online_find(p)
                if r:
                    tc, conf = r['tconst'], 'online'
            except Exception:
                online_ok = False
        t = ix.execute('SELECT * FROM titles WHERE id=?', (tc,)).fetchone() if tc else None
        item = {'raw': raw, 'path': e.get('path'), 'size': e.get('size'), 'copies': 1, 'episode': e.get('episode'),
                'title': p['title'], 'year': p['year'], 'tconst': tc, 'conf': conf,
                'quality': quality_label(p), 'fa': p['farsi_dubbed'],
                'imdb': {'title': t['title'], 'orig': t['orig'] if t['orig'] != t['title'] else None, 'year': t['year'],
                         'rating': t['rating'], 'votes': t['votes'], 'type': imdb_index.TYPE_NAMES.get(t['type'])} if t else None}
        if e.get('episode') and not tc:
            item['reason'] = 'TV episode — only movies and whole series are matched'
        elif conf in UNSURE and raw not in have_raw:
            item['reason'] = reason(conf, p['year'], t['year'] if t else None)
            item['cands'] = lib.matcher.all_cands(ix, p, [tc])
        # compare with the library
        if raw in have_raw:
            item['status'] = 'have'
        elif not tc:
            item['status'] = 'unmatched'
        elif tc in mine_by_tc:
            mine = mine_by_tc[tc]
            best = max(mine, key=functools.cmp_to_key(compare_quality))
            cmp = compare_quality(p, best)
            lt = ltitles.get(tc)
            item['lib'] = [{'id': f['id'], 'raw': f['raw'], 'quality': quality_label(f)} for f in mine]
            item['lib_title'] = (lt['en_title'] or lt['title']) if lt else None
            if not p['resolution'] and not p['farsi_dubbed']:
                item['status'] = 'duplicate'  # a bare title (no quality info): you already have it
            elif not any(bool(f['farsi_dubbed']) == bool(p['farsi_dubbed']) for f in mine):
                item['status'] = 'version'  # same movie, other audio (dubbed vs original)
            elif cmp > 0:
                item['status'] = 'better'
            elif cmp < 0:
                item['status'] = 'worse'
            else:
                item['status'] = 'duplicate'
        else:
            item['status'] = 'new'
        seen_raw[raw] = item
        rows.append(item)
    ix.close()
    # the same movie more than once inside the checked list
    by_tc = {}
    for it in rows:
        if it['tconst']:
            by_tc.setdefault(it['tconst'], []).append(it)
    for its in by_tc.values():
        if len(its) > 1:
            for it in its:
                it['twins'] = [o['raw'] for o in its if o is not it]
    in_list = set(by_tc)
    missing = [{'tconst': tc, 'title': (ltitles[tc]['en_title'] or ltitles[tc]['title']) if tc in ltitles else None,
                'year': ltitles[tc]['year'] if tc in ltitles else None, 'files': [f['raw'] for f in fs]}
               for tc, fs in mine_by_tc.items() if tc not in in_list]
    missing.sort(key=lambda m: (m['title'] or '').lower())
    counts = {}
    for it in rows:
        counts[it['status']] = counts.get(it['status'], 0) + 1
    counts['unsure'] = sum(1 for it in rows if it.get('reason'))
    counts['twins'] = sum(1 for it in rows if it.get('twins'))
    progress('Done', len(entries), len(entries))
    return {'source': label, 'entries': len(entries), 'items': rows, 'counts': counts,
            'missing': missing, 'missing_count': len(missing), 'at': time.strftime('%Y-%m-%d %H:%M:%S')}
