"""Collections (franchises), the rename plan and 'show in folder'. All read-only: nothing here changes a file."""
import csv
import io
import json
import os
import re
import subprocess

from . import checker, online
from .parse import parse

SIDE_EXT = checker.SIDE_EXT | {'.sub'}  # subtitle and side files that travel with a video ('.sub' too: here it is a file ending)
BAD_NAME = re.compile(r'[<>:"/\\|?*\x00-\x1f]')


def _norm(s):
    return re.sub(r'[^a-z0-9]+', '', (s or '').lower())


# ---------------------------------------------------------------- collections
def collections_view(db):
    """Collections found in your library, with the parts TMDB lists when they were fetched.
    {name, have:[{k,title,year}], missing:[{title,year}], total, fetched}"""
    by_coll = {}
    known = {}  # normalised title -> years of every movie in the library
    for r in db.execute('SELECT tconst, title, en_title, orig, year, collection FROM titles '
                        'WHERE tconst IN (SELECT tconst FROM files WHERE present=1)'):
        for t in (r['title'], r['en_title'], r['orig']):
            if t:
                known.setdefault(_norm(t), set()).add(r['year'])
        if r['collection']:
            by_coll.setdefault(r['collection'], []).append(
                {'k': f'tt{r["tconst"]:07d}', 'title': r['en_title'] or r['title'], 'year': r['year']})
    parts = {r['name']: json.loads(r['parts']) for r in db.execute('SELECT name, parts FROM collections')}
    out = []
    for name, have in by_coll.items():
        have.sort(key=lambda m: m['year'] or 9999)
        missing = []
        for p in parts.get(name) or []:
            years = known.get(_norm(p['title'])) or known.get(_norm(p.get('orig'))) or set()
            if not any(y and p['year'] and abs(y - p['year']) <= 1 for y in years):
                missing.append({'title': p['title'], 'year': p['year']})
        out.append({'name': name, 'have': have, 'missing': missing,
                    'total': len(parts[name]) if name in parts else None, 'fetched': name in parts})
    out.sort(key=lambda c: (not c['missing'], -len(c['have']), c['name'].lower()))
    return out


def fetch_collections(lib):
    """Ask TMDB for the parts of every collection in the library (needs a TMDB key). A job: lib.start(...)."""
    key = lib.settings().get('tmdb_key')
    if not key:
        raise RuntimeError('Add a TMDB key in Settings first.')
    tm = online.Tmdb(key)
    names = [r[0] for r in lib.db.execute(
        'SELECT DISTINCT collection FROM titles WHERE collection IS NOT NULL AND tconst IN '
        '(SELECT tconst FROM files WHERE present=1)')]
    for i, name in enumerate(names, 1):
        lib.progress('Fetching collections from TMDB', i, len(names))
        try:
            cid = tm.search_collection(name)
            if not cid:
                continue
            parts = tm.collection(cid)
        except Exception as e:
            lib.log(f'Collection {name}: {e}')
            continue
        with lib.lock:
            lib.db.execute('INSERT OR REPLACE INTO collections(name, tmdb_id, parts) VALUES (?,?,?)',
                           (name, cid, json.dumps(parts, ensure_ascii=False)))
            lib.db.commit()
    lib.version += 1
    lib.log(f'Collections: checked {len(names)}')


# ---------------------------------------------------------------- rename plan
def _clean(s):
    """A safe Windows file name: '3:10 to Yuma' -> '3.10 to Yuma', 'Alien: Covenant' -> 'Alien - Covenant'."""
    s = re.sub(r'(?<=\d):(?=\d)', '.', s or '')
    s = re.sub(r'\s*[:/\\]\s*', ' - ', s)
    return re.sub(r'\s+', ' ', BAD_NAME.sub('', s)).strip(' .')


def _low(p):
    return os.path.normcase(os.path.normpath(p))


def _listing(d, cache):
    """(video stems, other file names) directly in folder d, or None when it cannot be read."""
    if d not in cache:
        try:
            names = [n for n in os.listdir(d) if os.path.isfile(os.path.join(d, n))]
        except OSError:
            cache[d] = None
        else:
            vids = [os.path.splitext(n)[0] for n in names if os.path.splitext(n)[1].lower() in checker.VIDEO_EXT]
            cache[d] = (vids, [n for n in names if os.path.splitext(n)[1].lower() not in checker.VIDEO_EXT])
    return cache[d]


DEFAULT_PATTERN = '{title} ({year}) [{res} {src}]'
PATTERN_FIELDS = {
    'title': 'the movie title (English if known)', 'year': 'release year', 'res': 'resolution, like 1080p',
    'src': 'source, like BluRay', 'codec': 'video codec, like x265', 'dub': '"Farsi dubbed" when it is',
    'edition': 'edition, like Extended', 'group': 'release group', 'imdb': 'IMDb id, like tt0113277',
}


def pattern_problem(pattern):
    """'' when the pattern is usable, else what is wrong with it (shown to the user)."""
    pattern = (pattern or '').strip()
    if not pattern:
        return 'The pattern is empty.'
    names = re.findall(r'\{([^{}]*)\}', pattern)
    bad = [n for n in names if n not in PATTERN_FIELDS]
    if bad:
        return 'Unknown: ' + ', '.join('{' + n + '}' for n in dict.fromkeys(bad)) + '. Use ' + ', '.join('{' + n + '}' for n in PATTERN_FIELDS) + '.'
    if 'title' not in names and 'imdb' not in names:
        return 'The pattern needs {title} (or {imdb}), or every movie would get the same name.'
    if re.sub(r'\{[^{}]*\}', '', pattern).count('{') or re.sub(r'\{[^{}]*\}', '', pattern).count('}'):
        return 'A { or } is not closed.'
    return ''


def _name(pattern, m, f, title):
    """One movie file's new name (no extension) from the user's pattern; empty fields vanish with their brackets."""
    vals = {'title': title, 'year': str(m['year']) if m.get('year') else '', 'res': f.get('res') or '', 'src': f.get('src') or '',
            'codec': f.get('codec') or '', 'dub': 'Farsi dubbed' if f.get('fa') else '', 'edition': f.get('ed') or '',
            'group': f.get('grp') or '', 'imdb': m['k'] if str(m.get('k', '')).startswith('tt') else ''}
    s = re.sub(r'\{([^{}]*)\}', lambda x: _clean(vals.get(x.group(1), '')), pattern)
    s = re.sub(r'([(\[])\s+|\s+([)\]])', r'\1\2', s)   # '[1080p ]' -> '[1080p]'
    s = re.sub(r'\(\s*\)|\[\s*\]', '', s)
    s = re.sub(r'(\s*[-_.]+\s*)+$|^(\s*[-_.]+\s*)+', '', re.sub(r'\s+', ' ', s).strip())
    return _clean(s)[:150].strip(' .')


def rename_plan(movies, roots=(), pattern=DEFAULT_PATTERN):
    """Proposed names (the user's pattern, by default `Title (Year) [1080p BluRay]`) for every movie whose location is known: the video file, the
    subtitle / nfo files that sit beside it with the same name, and the movie's own folder (a folder named like the
    movie that holds that one movie; never a movie folder you listed itself, never a disc folder).
    Rows: {kind 'file'|'side'|'folder', k, path, old, new, new_stem, target, same, conflict, why}.
    A name is a conflict when two things would get the same new path, or the new name is already taken on disk."""
    roots = {_low(r) for r in roots or [] if r}
    rows, cache = [], {}
    folders = {}  # folder path -> its row, so a folder is planned once
    for m in movies:
        title = _clean(m.get('en') or m.get('title'))
        if not title:
            continue
        for f in m['files']:
            for path in f.get('paths') or []:
                folder, base = os.path.split(path)
                stem, ext = os.path.splitext(base)
                if any(x.lower() in checker.DISC_DIRS for x in re.split(r'[\\/]', folder)):
                    continue  # a DVD / Blu-ray structure: its files must keep their names
                new = _name(pattern, m, f, title)
                if not new:
                    continue
                rows.append({'kind': 'file', 'k': m['k'], 'path': path, 'old': base, 'new': new + ext, 'new_stem': new,
                             'target': os.path.join(folder, new + ext), 'same': stem.lower() == new.lower(),
                             'conflict': False, 'why': ''})
                lst = _listing(folder, cache)
                if lst:  # subtitles and other side files of this video: same name, longer ending (Movie.en.srt)
                    longer = [s for s in lst[0] if len(s) > len(stem) and s.lower().startswith(stem.lower() + '.')]
                    for name in lst[1]:
                        if (os.path.splitext(name)[1].lower() in SIDE_EXT and len(name) > len(stem) + 1
                                and name.lower().startswith(stem.lower() + '.')
                                and not any(name.lower().startswith(s.lower() + '.') for s in longer)):
                            suffix = name[len(stem):]
                            rows.append({'kind': 'side', 'k': m['k'], 'path': os.path.join(folder, name), 'old': name,
                                         'new': new + suffix, 'new_stem': new, 'target': os.path.join(folder, new + suffix),
                                         'same': stem.lower() == new.lower(), 'conflict': False, 'why': ''})
                if (not folder or _low(folder) in roots or _low(folder) == _low(os.path.dirname(folder))
                        or (_low(folder) in folders and folders[_low(folder)]['new_stem'] == new)):
                    continue  # a movie folder you listed, a drive root, or a folder already planned
                fname = os.path.basename(folder)
                alone = len(lst[0]) == 1 if lst else sum(1 for r in rows if r['kind'] == 'file' and os.path.dirname(r['path']) == folder) == 1
                same_name = f.get('raw', '').lower() == fname.lower() or (
                    alone and _norm(parse(fname)['title']) in {_norm(parse(stem)['title']), _norm(title)})
                if not same_name:
                    continue
                if _low(folder) in folders:  # two movies claim one folder: neither can have it
                    folders[_low(folder)]['conflict'], folders[_low(folder)]['why'] = True, 'two movies share this folder'
                    continue
                parent = os.path.dirname(folder)
                row = {'kind': 'folder', 'k': m['k'], 'path': folder, 'old': fname, 'new': new, 'new_stem': new,
                       'target': os.path.join(parent, new), 'same': fname.lower() == new.lower(), 'conflict': False, 'why': ''}
                folders[_low(folder)] = row
                rows.append(row)
    taken = {}
    for r in rows:
        taken.setdefault(_low(r['target']), []).append(r)
    for group in taken.values():
        if len(group) > 1 and len({_low(r['path']) for r in group}) > 1:
            for r in group:
                if not r['same']:
                    r['conflict'], r['why'] = True, 'two items would get this name'
    for r in rows:
        if not r['same'] and not r['conflict'] and _low(r['target']) != _low(r['path']) and os.path.exists(r['target']):
            r['conflict'], r['why'] = True, 'that name is already taken'
    return rows


def _bat(s):
    return str(s).replace('%', '%%')


def plan_bat(rows):
    """A Windows batch file with one `ren` per item: side files and videos first, then the folders, innermost first
    (a folder's path changes when it is renamed). `if exist` keeps it from overwriting anything. Nothing runs until
    the user opens it."""
    out = ['@echo off', 'chcp 65001 >nul', 'rem MovieShelf rename plan. Read it first, then run it. Lines starting with rem are skipped.']
    todo = [r for r in rows if r['kind'] != 'folder'] + sorted((r for r in rows if r['kind'] == 'folder'),
                                                               key=lambda r: -len(os.path.normpath(r['path']).split(os.sep)))
    for r in todo:
        if r['same']:
            continue
        if r['conflict']:
            out.append(f'rem SKIPPED ({r["why"]}): {_bat(r["path"])}')
            continue
        out.append(f'if exist "{_bat(r["target"])}" (echo SKIPPED - the new name is taken) else ren "{_bat(r["path"])}" "{_bat(r["new"])}"')
    out.append('echo Done.')
    out.append('pause')
    return ('\r\n'.join(out) + '\r\n').encode('utf-8')


KIND_NAME = {'file': 'File', 'side': 'Subtitle / side file', 'folder': 'Folder'}


def plan_csv(rows):
    out = io.StringIO()
    w = csv.writer(out)
    w.writerow(['Type', 'Folder', 'Current name', 'New name', 'Status'])
    for r in rows:
        w.writerow([KIND_NAME[r['kind']], os.path.dirname(r['path']), r['old'], r['new'],
                    'already named' if r['same'] else 'conflict: ' + r['why'] if r['conflict'] else 'rename'])
    return ('﻿' + out.getvalue()).encode('utf-8')


def plan_list(rows):
    """The new movie names (no extension), one per line: put these in your list in place of the old names. A movie
    whose folder is renamed is listed too, even when its file already had the right name."""
    names = dict.fromkeys(r['new_stem'] for r in rows if r['kind'] != 'side' and not r['same'] and not r['conflict'])
    return ('﻿' + '\r\n'.join(names) + '\r\n').encode('utf-8')


# ---------------------------------------------------------------- show in folder
def reveal(lib, path):
    """Open Explorer with the file selected (a folder with no video in it is opened). Only paths the folder scan
    found can be opened this way."""
    db = lib.reader()
    try:
        row = db.execute('SELECT 1 FROM fileinfo WHERE path=?', (path,)).fetchone()
        if not row:  # a movie taken from a folder can be shown too
            row = db.execute('SELECT 1 FROM unlisted WHERE path=?', (path,)).fetchone()
        folder = not row and db.execute('SELECT 1 FROM novideo WHERE path=?', (path,)).fetchone()
    finally:
        db.close()
    if not (row or folder) or not os.path.exists(path):
        raise FileNotFoundError('That file is not where it was. Scan the movie folders again.')
    if os.name == 'nt':
        subprocess.Popen(['explorer', os.path.normpath(path)] if folder else ['explorer', '/select,', os.path.normpath(path)])
    return True
