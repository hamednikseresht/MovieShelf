"""Library problems beyond name matching: list entries whose folder has no video file, duplicates, low or
unknown quality, dubbed-only movies, year mismatches, TV titles in a movie list and missing posters. Read-only; the user can ignore an item.

When movie folders are set (Settings), file sizes and locations are known: the better copy of equal
quality is the bigger one, the same file in two folders is reported, and wasted space is added up."""
import functools
import json
import os
import re

from rapidfuzz import fuzz

from .checker import EPISODE_RE, RES_RANK, compare_quality, quality_label
from .probe import real_height_class
from .fatitles import is_latin_only
from .matcher import UNSURE
from .parse import EDITION_WORDS, key, norm

KINDS = ['novideo', 'dupes', 'lowq', 'unknownq', 'dubonly', 'realq', 'yeardiff', 'nonfilm', 'noposter', 'runtime', 'fatitle', 'namediff', 'nodata', 'samename']
# TV series and mini-series have their own section (Series) and are not problems; these are the rest
SERIES_TYPES = {'tvSeries', 'tvMiniSeries', 'tvEpisode'}  # several files of these are episodes, not copies
TV_TYPES = {'tvEpisode': 'TV episode', 'tvSpecial': 'TV special', 'tvShort': 'TV short'}
NAME_CLOSE = 70  # similarity from which a different file name counts as a small difference, not another name
LOW = RES_RANK['576p']  # 576p, 480p and SD count as low quality
EXTRA_WORDS = re.compile(r'\b(extras?|bonus|featurette|trailer|sample)\b', re.I)


def _better(a, b):
    """Quality first; for equal quality the bigger file wins."""
    return compare_quality(a, b) or ((a.get('size') or 0) - (b.get('size') or 0))


def _analyse(fs):
    """Verdict per file of one movie: 'keep' (best of its audio version), 'lower' (worse than the kept copy),
    'same' (equal quality: a redundant copy) or 'unsure' (a resolution is missing, cannot compare).
    Returns (verdicts by file id, 'same' | 'lower' | 'versions')."""
    verdict = {}
    by_audio = {}
    for f in fs:
        by_audio.setdefault(bool(f['farsi_dubbed']), []).append(f)
    for g in by_audio.values():
        best = max(g, key=functools.cmp_to_key(_better))
        for f in g:
            if f is best:
                verdict[f['id']] = 'keep'
            elif not f['resolution'] or not best['resolution']:
                verdict[f['id']] = 'unsure'
            else:
                verdict[f['id']] = 'lower' if compare_quality(f, best) < 0 else 'same'
    vs = set(verdict.values())
    sub = 'same' if vs & {'same', 'unsure'} else 'lower' if 'lower' in vs else 'versions'
    return verdict, sub


def _nums(title):
    """The numbers in a title, '3.10 to Yuma' and '310 to Yuma' counting as the same."""
    return tuple(re.findall(r'\d+', re.sub(r'(?<=\d)\.(?=\d)', '', title or '')))


def mixed_reason(fs):
    """Why files matched to one IMDb title are probably different movies (or bonus material); '' if they look alike."""
    titles = [f.get('title') for f in fs]
    if len({_nums(t) for t in titles}) > 1:
        return 'The titles differ by a number (' + ' / '.join(str(t) for t in dict.fromkeys(titles)) + ')'
    ex = [bool(EXTRA_WORDS.search(t or '')) for t in titles]
    if any(ex) and not all(ex):
        return 'One file looks like bonus material, not the movie'
    return ''


def series_reason(fs, t, mk, ignored):
    """Why several files of one title are episodes of a series, not duplicate copies; '' if they are not.
    The user's choice (Duplicates → This is a series / Not a series) wins over the guess."""
    if f'series:{mk}' in ignored:
        return 'You marked this as a series'
    if f'noseries:{mk}' in ignored:
        return ''
    if sum(1 for f in fs if EPISODE_RE.search(f['raw'])) >= 2:
        return 'The file names have episode numbers (like S01E02): a series, not copies'
    if t and t['type'] in SERIES_TYPES:
        return 'IMDb lists this as a TV series: the files are episodes, not copies'
    return ''


def _plain(name):
    """A name as the matcher sees it, edition words left out: case, accents, punctuation and '&' do not count."""
    return ''.join(EDITION_WORDS.sub(' ', norm(name)).split())


def name_diff(fs, t):
    """(file name, IMDb name, similarity 0-100) of the file whose name is furthest from the real film name, or None
    when every file is named like the film. Files that are an unsure match are left to Matching."""
    real = [n for n in dict.fromkeys((t['en_title'], t['title'], t['orig'], t['fa'])) if n and _plain(n)]
    plain = {_plain(n) for n in real}
    worst = None
    for f in fs:
        if f['conf'] in UNSURE and not f['manual']:
            continue
        names = [n for n in (f['title'], f['alt_title']) if n and _plain(n)]
        if not names or not real or any(_plain(n) in plain for n in names):
            continue
        sim, fname, rname = max((fuzz.ratio(_plain(n), _plain(r)), n, r) for n in names for r in real)
        if worst is None or sim < worst[2]:
            worst = (fname, rname, sim)
    return worst


def _rows(db):
    titles = {r['tconst']: r for r in db.execute(
        'SELECT t.tconst, t.type, t.title, t.orig, t.en_title, t.year, t.runtime, t.rating, t.votes, t.poster, '
        'coalesce(f.title, t.fa_title) AS fa, '
        '(coalesce(length(t.cast_), 0) > 2 OR coalesce(length(t.tmdb_cast), 0) > 2) AS has_cast, coalesce(t.genres, \'\') AS genres '
        'FROM titles t LEFT JOIN fa_titles f ON f.tconst = t.tconst')}
    info = {}
    for r in db.execute('SELECT raw, path, size FROM fileinfo ORDER BY path'):
        info.setdefault(r['raw'], []).append((r['path'], r['size']))
    media = {r['path']: r for r in db.execute('SELECT path, width, height, duration FROM mediainfo WHERE ok=1')}
    junk = {}
    for r in db.execute('SELECT path, raw, nfiles, size, names FROM novideo ORDER BY path'):
        junk.setdefault(r['raw'], []).append({'path': r['path'], 'nfiles': r['nfiles'], 'size': r['size'],
                                               'names': json.loads(r['names'] or '[]')})
    groups = {}
    for f in db.execute('SELECT * FROM files WHERE present=1 ORDER BY id'):
        f = dict(f)
        here = info.get(f['raw'], [])
        m = next((media[p] for p, _ in here if p in media), None)
        f['mw'], f['mh'], f['dur'] = (m['width'], m['height'], m['duration']) if m else (None, None, None)
        f['paths'] = [p for p, _ in here]
        f['size'] = max((sz for _, sz in here), default=None)
        f['copies_size'] = sum(sz or 0 for _, sz in here[1:])
        f['junk'] = junk.get(f['raw'], [])
        groups.setdefault(f['tconst'] or -f['id'], []).append(f)
    return titles, groups


def _key(f):
    return f'tt{f["tconst"]:07d}' if f['tconst'] else f'f{f["id"]}'


_same_cache = {'stamp': None, 'by': {}}  # tconst -> ((title, orig, en_title, year), [text, ...]) for one index file


def same_name_on_imdb(lib, titles, used):
    """{tconst: [text, ...]}: for every matched title, the other IMDb titles with the same name and the same year
    (the file may belong to either). Needs the offline index. Each text: tt number and votes.
    Every IMDb title has a match key for its name and its original name (keys, src 0), and two names that are equal
    when lowercased have the same key, so only the titles sharing a key are read, not every title of every year.
    The answer for a title depends only on its own names and year, so it is kept until the index file changes:
    removing a movie or ignoring a row does not look anything up again."""
    if not os.path.exists(lib.index_path) or not used:
        return {}
    st = os.stat(lib.index_path)
    stamp = (lib.index_path, st.st_mtime_ns, st.st_size)
    if _same_cache['stamp'] != stamp:
        _same_cache.update(stamp=stamp, by={})
    by = _same_cache['by']
    sig = {tc: (t['title'], t['orig'], t['en_title'], t['year']) for tc in used if (t := titles.get(tc)) is not None}
    todo = [tc for tc in used if tc in sig and by.get(tc, (None,))[0] != sig[tc]]
    if todo:
        want = {}
        for tc in todo:
            t = titles[tc]
            by[tc] = (sig[tc], [])
            if not t['year']:
                continue
            for n in {(x or '').strip().lower() for x in (t['title'], t['orig'], t['en_title'])} - {''}:
                want.setdefault((t['year'], n), []).append(tc)
        ks = sorted({key(n) for _, n in want} - {''})
        rows = {}
        if ks:
            ix = lib.matcher.open()
            try:
                for i in range(0, len(ks), 500):
                    part = ks[i:i + 500]
                    for r in ix.execute(f'SELECT t.id, t.title, t.orig, t.year, t.votes FROM keys k JOIN titles t ON t.id = k.id '
                                        f'WHERE k.src = 0 AND k.k IN ({",".join("?" * len(part))})', part):
                        rows[r['id']] = r
            finally:
                ix.close()
        seen = set()
        for r in sorted(rows.values(), key=lambda r: (r['year'] or 0, r['id'])):
            for n in (r['title'], r['orig']):
                for own in want.get((r['year'], (n or '').strip().lower()), ()):
                    if own == r['id'] or (own, r['id']) in seen:
                        continue
                    seen.add((own, r['id']))
                    lst = by[own][1]
                    if len(lst) < 5:
                        lst.append(f'tt{r["id"]:07d} ({r["votes"] or 0:,} votes)')
    return {tc: by[tc][1] for tc in used if tc in by and by[tc][1]}


def build(lib, db=None):
    """{kind: [row, ...], 'ignored': n, 'wasted': bytes}. Each row: ikey, k, tt, title, year, rating, votes,
    files [{id, raw, quality, verdict, size, paths}], note, sub, wasted, ignored."""
    db = db or lib.db
    titles, groups = _rows(db)
    ignored = {r[0] for r in db.execute('SELECT ikey FROM dismissed')}
    out = {k: [] for k in KINDS}
    same_name = same_name_on_imdb(lib, titles, {fs[0]['tconst'] for fs in groups.values() if fs[0]['tconst']})
    for fs in groups.values():
        f0 = fs[0]
        t = titles.get(f0['tconst']) if f0['tconst'] else None
        mk = _key(f0)
        verdict, sub = _analyse(fs) if len(fs) > 1 and t else ({}, '')
        row = {
            'k': mk, 'tt': mk if t else None,
            'title': (t['en_title'] or t['title']) if t else (f0['title'] or f0['raw']),
            'year': (t['year'] if t else None) or f0['year'],
            'rating': t['rating'] if t else None, 'votes': t['votes'] if t else None,
            'files': [{'id': f['id'], 'raw': f['raw'], 'quality': quality_label(f), 'verdict': verdict.get(f['id']),
                       'size': f['size'], 'paths': f['paths']} for f in fs],
        }

        def add(kind, note, sub_kind=None, ikey=None, wasted=0, **extra):
            ikey = ikey or f'{kind}:{mk}'
            out[kind].append({**row, 'ikey': ikey, 'note': note, 'sub': sub_kind, 'wasted': wasted, 'ignored': ikey in ignored,
                              **extra})

        bad = [f for f in fs if f['junk']]
        if bad:
            where = [j for f in bad for j in f['junk']]
            empty = all(not j['nfiles'] for j in where)
            add('novideo', 'The folder is empty: no video file' if empty else
                'The folder has no video file, only other files (' + ', '.join(
                    dict.fromkeys(n for j in where for n in j['names'][:3]))[:160] + ')',
                junk_ids=[f['id'] for f in bad],
                junk=[{'id': f['id'], 'raw': f['raw'], **j} for f in bad for j in f['junk']])
        episodes = series_reason(fs, t, mk, ignored) if len(fs) > 1 else ''
        if episodes:
            add('dupes', episodes, 'series', f'dupes:{mk}:series', manual=f'series:{mk}' in ignored)
        elif verdict:
            ids = '-'.join(str(f['id']) for f in fs)
            why = mixed_reason(fs)
            if why:
                add('dupes', f'{why}: probably different movies matched to one IMDb title, not duplicates', 'mixed', f'dupes:{mk}:{ids}')
            else:
                note = {'same': 'Two copies of the same quality', 'lower': 'A lower-quality copy sits beside a better one',
                        'versions': 'Dubbed and original: not redundant, probably on purpose'}[sub]
                if sub == 'same' and any(v == 'unsure' for v in verdict.values()):
                    note = 'Several copies; at least one has no resolution in its name, so they cannot be compared'
                waste = sum(f['size'] or 0 for f in fs if verdict[f['id']] in ('lower', 'same')) if sub != 'versions' else 0
                add('dupes', note, sub, f'dupes:{mk}:{ids}', waste)
        extra = [f for f in fs if len(f['paths']) > 1]
        if extra and not episodes:
            add('dupes', f'The same file is in {len(extra[0]["paths"])} folders', 'copies',
                f'dupes:{mk}:copies:' + '-'.join(str(f['id']) for f in extra), sum(f['copies_size'] for f in extra))
        ress = [f['resolution'] for f in fs]
        if all(ress) and max(RES_RANK.get(r, 0) for r in ress) <= LOW:
            add('lowq', f'Best copy is {quality_label(max(fs, key=lambda f: RES_RANK.get(f["resolution"], 0)))}')
        if not any(ress):
            add('unknownq', 'No resolution in the file name')
        if all(f['farsi_dubbed'] for f in fs):
            add('dubonly', 'Only a Farsi-dubbed copy')
        for f in fs:  # the file name claims more than the picture really is
            real = real_height_class(f['mw'], f['mh'])
            if real and f['resolution'] in RES_RANK and RES_RANK[real] < RES_RANK[f['resolution']]:
                add('realq', f'Named {f["resolution"]}, but the picture is {f["mw"]}x{f["mh"]}')
                break
        if t:
            durs = [f['dur'] for f in fs if f['dur']]
            if durs and t['runtime']:
                mins = max(durs) / 60
                if mins < t['runtime'] * 0.8 and t['runtime'] - mins > 15:
                    add('runtime', f'The file is {round(mins)} min, IMDb says {t["runtime"]} min: cut short, or a different movie')
                elif mins > t['runtime'] * 1.35 and mins - t['runtime'] > 20:
                    add('runtime', f'The file is {round(mins)} min, IMDb says {t["runtime"]} min: a different cut, or a different movie')
            fy = next((f['year'] for f in fs if f['year']), None)
            if fy and t['year'] and abs(fy - t['year']) >= 2 and not any(f['conf'] in UNSURE or f['manual'] for f in fs):
                add('yeardiff', f'The file name says {fy}, IMDb says {t["year"]}')
            nd = name_diff(fs, t)
            if nd:
                add('namediff', f'The file name says “{nd[0]}”, IMDb calls it “{nd[1]}”',
                    'close' if nd[2] >= NAME_CLOSE else 'far', sim=nd[2])
            if t['type'] in TV_TYPES:
                add('nonfilm', f'IMDb lists this as a {TV_TYPES[t["type"]]}')
            if not t['poster']:
                add('noposter', 'No poster found')
            lacking = [name for name, missing in (('poster', not t['poster']), ('cast', not t['has_cast']), ('IMDb rating', t['rating'] is None),
                                                  ('genres', not t['genres']), ('length', not t['runtime'])) if missing]
            if lacking:
                add('nodata', 'Missing: ' + ', '.join(lacking))
            if f0['tconst'] in same_name:
                add('samename', 'IMDb has other titles with this name and year: ' + '; '.join(same_name[f0['tconst']]))
            if is_latin_only(t['fa']):
                add('fatitle', f'The Persian title is written in Latin letters: {t["fa"]}')
    by_votes = lambda r: (-(r['votes'] or 0), r['title'] or '')
    for k in ('lowq', 'dubonly'):
        out[k].sort(key=by_votes)
    out['dupes'].sort(key=lambda r: (-(r['wasted'] or 0), (r['title'] or '').lower()))
    for k in ('novideo', 'unknownq', 'realq', 'yeardiff', 'nonfilm', 'noposter', 'runtime', 'fatitle', 'nodata', 'samename'):
        out[k].sort(key=lambda r: (r['title'] or '').lower())
    out['namediff'].sort(key=lambda r: (r['sim'], (r['title'] or '').lower()))  # the most different names first
    out['ignored'] = len([1 for k in KINDS for r in out[k] if r['ignored']])
    out['wasted'] = sum(r['wasted'] for r in out['dupes'] if not r['ignored'])
    return out


_cache = {}


def _open_counts(lib):
    key = (lib.version, getattr(lib, 'issue_rev', 0))
    if _cache.get('key') != key:
        db = lib.reader()
        try:
            out = build(lib, db)
            n = {k: len([r for r in out[k] if not r['ignored'] and r['sub'] != 'series']) for k in ('dupes', 'novideo')}
        finally:
            db.close()
        _cache.update(key=key, n=n)
    return _cache['n']


def open_duplicates(lib):
    """How many duplicate items are not ignored (the header badge adds it to the matching problems).
    'mixed' items count too: they are wrong matches in disguise."""
    return _open_counts(lib)['dupes']


def open_novideo(lib):
    """How many list entries have a folder without any video file (not ignored); the header badge counts them."""
    return _open_counts(lib)['novideo']
