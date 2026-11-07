"""Download the official IMDb non-commercial datasets and build a compact,
movie-only SQLite index used for offline matching and details.

Data source: https://datasets.imdbws.com/ (personal, non-commercial use)."""
import gzip
import os
import sqlite3
import time
import urllib.request

from .parse import key

BASE_URL = 'https://datasets.imdbws.com/'
FILES = ['title.ratings', 'title.basics', 'title.akas', 'title.crew', 'title.principals', 'name.basics']
UA = 'MovieShelf/1.0 (personal collection manager)'

# titleType -> small int. Everything else (episodes, games) is skipped.
TYPES = {'movie': 0, 'tvMovie': 1, 'video': 2, 'tvSpecial': 3, 'short': 4, 'tvShort': 5,
         'tvMiniSeries': 6, 'tvSeries': 7}
TYPE_NAMES = {v: k for k, v in TYPES.items()}
AKA_REGIONS = {'XWW', 'US', 'GB', 'IR', 'CA', 'AU'}
AKA_LANGS = {'en', 'fa'}
NULL = '\\N'


def tid(s):
    return int(s[2:])


def _nz(v):
    return None if v == NULL else v


def download(data_dir, progress, force=False, names=None):
    os.makedirs(data_dir, exist_ok=True)
    names = names or FILES
    for i, name in enumerate(names):
        path = os.path.join(data_dir, name + '.tsv.gz')
        url = BASE_URL + name + '.tsv.gz'
        req = urllib.request.Request(url, headers={'User-Agent': UA})
        if os.path.exists(path) and not force:
            continue
        progress(f'Downloading {name}', i, len(names))
        tmp = path + '.part'
        try:
            with urllib.request.urlopen(req, timeout=60) as r, open(tmp, 'wb') as f:
                total = int(r.headers.get('Content-Length') or 0)
                got = 0
                while True:
                    chunk = r.read(1 << 20)
                    if not chunk:
                        break
                    f.write(chunk)
                    got += len(chunk)
                    progress(f'Downloading {name} ({got >> 20}/{total >> 20} MB)' if total else f'Downloading {name} ({got >> 20} MB)', got, total)
        except BaseException:  # stopped by the user, or the connection failed: no half file is left behind
            if os.path.exists(tmp):
                os.remove(tmp)
            raise
        os.replace(tmp, path)


def file_age_days(data_dir, name):
    path = os.path.join(data_dir, name + '.tsv.gz')
    return (time.time() - os.path.getmtime(path)) / 86400 if os.path.exists(path) else None


def read_ratings(data_dir):
    return {tid(r[0]): (float(r[1]), int(r[2])) for r in _lines(os.path.join(data_dir, 'title.ratings.tsv.gz'))}


def have_files(data_dir):
    return all(os.path.exists(os.path.join(data_dir, n + '.tsv.gz')) for n in FILES)


def _lines(path):
    with gzip.open(path, 'rt', encoding='utf-8', newline='\n') as f:
        next(f)  # header
        for line in f:
            yield line.rstrip('\n').split('\t')


def build_index(data_dir, db_path, progress):
    tmp = db_path + '.tmp'
    if os.path.exists(tmp):
        os.remove(tmp)
    db = sqlite3.connect(tmp)
    db.executescript('''
        PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;
        CREATE TABLE titles(id INTEGER PRIMARY KEY, type INT, title TEXT, orig TEXT, year INT,
                            endyear INT, runtime INT, genres TEXT, rating REAL, votes INT);
        CREATE TABLE keys(k TEXT, id INT, src INT);
        CREATE TABLE akas(id INT, title TEXT, region TEXT, lang TEXT, types TEXT);
        CREATE TABLE meta(k TEXT PRIMARY KEY, v TEXT);
    ''')
    p = lambda f: os.path.join(data_dir, f + '.tsv.gz')

    progress('Reading ratings', 0, 1)
    ratings = {}
    for r in _lines(p('title.ratings')):
        ratings[tid(r[0])] = (float(r[1]), int(r[2]))

    kept = bytearray(50_000_000)
    rows, keyrows, n = [], [], 0
    for r in _lines(p('title.basics')):
        n += 1
        if n % 500_000 == 0:
            progress(f'Indexing titles ({n // 1000}k)', n, 12_000_000)
        t = TYPES.get(r[1])
        if t is None or r[4] == '1':
            continue
        i = tid(r[0])
        if i >= len(kept):
            kept.extend(bytearray(i - len(kept) + 10_000_000))
        kept[i] = 1
        rt = ratings.get(i, (None, 0))
        rows.append((i, t, r[2], r[3], _nz(r[5]), _nz(r[6]), _nz(r[7]), _nz(r[8]), rt[0], rt[1]))
        k1, k2 = key(r[2]), key(r[3])
        keyrows.append((k1, i, 0))
        if k2 != k1:
            keyrows.append((k2, i, 0))
        if len(rows) >= 50_000:
            db.executemany('INSERT INTO titles VALUES (?,?,?,?,?,?,?,?,?,?)', rows)
            db.executemany('INSERT INTO keys VALUES (?,?,?)', keyrows)
            rows, keyrows = [], []
    db.executemany('INSERT INTO titles VALUES (?,?,?,?,?,?,?,?,?,?)', rows)
    db.executemany('INSERT INTO keys VALUES (?,?,?)', keyrows)
    db.commit()
    del ratings

    # akas: alternative titles (English, Persian, festival names...)
    cur_id, cur_keys = -1, set()
    keyrows, akarows, n = [], [], 0
    for r in _lines(p('title.akas')):
        n += 1
        if n % 1_000_000 == 0:
            progress(f'Indexing alternative titles ({n // 1_000_000}M)', n, 55_000_000)
        i = tid(r[0])
        if i >= len(kept) or not kept[i]:
            continue
        if i != cur_id:
            cur_id, cur_keys = i, set()
        title = r[2]
        k = key(title)
        if k and k not in cur_keys:
            cur_keys.add(k)
            keyrows.append((k, i, 1))
        region, lang = _nz(r[3]), _nz(r[4])
        if region in AKA_REGIONS or lang in AKA_LANGS:
            akarows.append((i, title, region, lang, _nz(r[5])))
        if len(keyrows) >= 100_000:
            db.executemany('INSERT INTO keys VALUES (?,?,?)', keyrows)
            keyrows = []
        if len(akarows) >= 100_000:
            db.executemany('INSERT INTO akas VALUES (?,?,?,?,?)', akarows)
            akarows = []
    db.executemany('INSERT INTO keys VALUES (?,?,?)', keyrows)
    db.executemany('INSERT INTO akas VALUES (?,?,?,?,?)', akarows)
    db.commit()

    progress('Creating search indexes', 0, 1)
    db.executescript('''
        CREATE INDEX keys_k ON keys(k);
        CREATE INDEX keys_id ON keys(id);
        CREATE INDEX akas_id ON akas(id);
        CREATE INDEX titles_year ON titles(year);
    ''')
    db.execute("INSERT INTO meta VALUES ('built', ?)", (time.strftime('%Y-%m-%d %H:%M'),))
    db.commit()
    db.close()
    if os.path.exists(db_path):
        os.remove(db_path)
    os.replace(tmp, db_path)


def read_people(data_dir, ids, progress):
    """Stream crew/principals/names for the given title ids.
    Returns (credits: {id: {'directors': [...], 'writers': [...], 'cast': [...], 'crew': [...]}},
             names: {nconst_int: name}, basics: {nconst_int: (birth year, death year, professions)})."""
    p = lambda f: os.path.join(data_dir, f + '.tsv.gz')
    ids = set(ids)
    credits = {i: {'directors': [], 'writers': [], 'cast': [], 'crew': []} for i in ids}
    need = set()
    n = 0
    for r in _lines(p('title.crew')):
        n += 1
        if n % 1_000_000 == 0:
            progress(f'Reading directors/writers ({n // 1_000_000}M)', n, 12_000_000)
        i = tid(r[0])
        if i not in ids:
            continue
        c = credits[i]
        if r[1] != NULL:
            c['directors'] = [tid(x) for x in r[1].split(',')]
        if r[2] != NULL:
            c['writers'] = [tid(x) for x in r[2].split(',')][:8]
        need.update(c['directors'])
        need.update(c['writers'])
    n = 0
    for r in _lines(p('title.principals')):
        n += 1
        if n % 2_000_000 == 0:
            progress(f'Reading cast ({n // 1_000_000}M)', n, 95_000_000)
        i = tid(r[0])
        if i not in ids:
            continue
        cat, nc = r[3], tid(r[2])
        need.add(nc)
        if cat in ('actor', 'actress', 'self'):
            chars = r[5]
            ch = None
            if chars != NULL:
                ch = chars.strip('[]').replace('","', ' / ').strip('"')
            credits[i]['cast'].append([nc, ch])
        elif cat not in ('director', 'writer'):
            job = r[4] if r[4] != NULL else cat.replace('_', ' ')
            credits[i]['crew'].append([nc, job])
    names, basics = {}, {}
    n = 0
    for r in _lines(p('name.basics')):
        n += 1
        if n % 1_000_000 == 0:
            progress(f'Reading names ({n // 1_000_000}M)', n, 15_000_000)
        i = tid(r[0])
        if i in need:
            names[i] = r[1]
            basics[i] = _basics(r)
    return credits, names, basics


def _basics(r):
    """(birth year, death year, professions) from a name.basics row."""
    year = lambda v: int(v) if v != NULL and v.isdigit() else None
    return year(r[2]), year(r[3]), (r[4].replace('_', ' ') if len(r) > 4 and r[4] != NULL else '')


def read_basics(data_dir, ids, progress=lambda *a: None):
    """Birth year, death year and professions for the given people ids, from name.basics: {id: (birth, death, professions)}."""
    ids, out, n = set(ids), {}, 0
    for r in _lines(os.path.join(data_dir, 'name.basics.tsv.gz')):
        n += 1
        if n % 1_000_000 == 0:
            progress(f'Reading people details ({n // 1_000_000}M)', n, 15_000_000)
        i = tid(r[0])
        if i in ids:
            out[i] = _basics(r)
    return out
