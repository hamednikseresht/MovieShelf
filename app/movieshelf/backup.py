"""Backup and restore of everything that only exists in the user's database: favorites, watched, ratings,
notes, manual IMDb matches, ignored problems, saved views and the wishlist. The IMDb data itself can
always be downloaded again, so it is not part of a backup."""
import glob
import json
import os
import time

from . import checker

KEEP = 14  # automatic backups kept


def list_files(lib):
    """{file name: [movie names]} of every .txt list in the list folder: the movies the user brought in."""
    out = {}
    for f in sorted(glob.glob(os.path.join(lib.settings()['list_dir'], '*.txt'))):
        try:
            names = [l.strip() for l in checker.read_text(f).splitlines() if l.strip()]
        except OSError:
            continue
        if names:
            out[os.path.basename(f)] = names
    return out


def snapshot(lib):
    db = lib.reader()
    try:
        rows = lambda sql: [dict(r) for r in db.execute(sql)]
        return {
            'app': 'MovieShelf', 'format': 1, 'created': time.strftime('%Y-%m-%d %H:%M:%S'),
            'user': rows('SELECT mkey, fav, watched, myrating, notes, watched_at FROM user'),
            'manual': rows('SELECT raw, tconst FROM files WHERE manual=1'),
            'forced': rows('SELECT raw, tconst FROM forced'),
            'dismissed': [r['ikey'] for r in db.execute('SELECT ikey FROM dismissed')],
            'views': rows('SELECT name, state FROM views ORDER BY id'),
            'wishlist': rows('SELECT title, year, tconst, note, source, status, added FROM wishlist ORDER BY id'),
            'fa_titles': rows("SELECT tconst, title FROM fa_titles WHERE source='user'"),
            'removed': rows('SELECT raw, tconst, title, removed FROM removed'),
            'lists': list_files(lib),
        }
    finally:
        db.close()


def restore(lib, data):
    """Merge a backup into the library. Returns how many of each thing it contained."""
    if not isinstance(data, dict) or data.get('app') != 'MovieShelf':
        raise ValueError('This is not a MovieShelf backup file.')
    n = {}
    with lib.lock:
        db = lib.db
        for u in data.get('user') or []:
            db.execute('INSERT OR IGNORE INTO user(mkey) VALUES (?)', (u['mkey'],))
            db.execute('UPDATE user SET fav=?, watched=?, myrating=?, notes=?, watched_at=?, updated=? WHERE mkey=?',
                       (u.get('fav') or 0, u.get('watched') or 0, u.get('myrating'), u.get('notes'), u.get('watched_at'),
                        time.strftime('%Y-%m-%d %H:%M:%S'), u['mkey']))
        n['user'] = len(data.get('user') or [])
        # manual matches wait in 'forced'; the next scan applies them to the files they name
        for m in (data.get('manual') or []) + (data.get('forced') or []):
            db.execute('INSERT OR REPLACE INTO forced(raw, tconst) VALUES (?,?)', (m['raw'], m.get('tconst')))
        n['manual'] = len(data.get('manual') or []) + len(data.get('forced') or [])
        for k in data.get('dismissed') or []:
            db.execute('INSERT OR IGNORE INTO dismissed(ikey) VALUES (?)', (k,))
        n['dismissed'] = len(data.get('dismissed') or [])
        for v in data.get('views') or []:
            db.execute('INSERT OR REPLACE INTO views(name, state) VALUES (?,?)', (v['name'], v['state']))
        n['views'] = len(data.get('views') or [])
        for w in data.get('wishlist') or []:
            db.execute('INSERT OR IGNORE INTO wishlist(title, year, tconst, note, source, status, added) VALUES (?,?,?,?,?,?,?)',
                       (w['title'], w.get('year'), w.get('tconst'), w.get('note'), w.get('source'),
                        w.get('status') or 'wanted', w.get('added')))
        n['wishlist'] = len(data.get('wishlist') or [])
        for f in data.get('fa_titles') or []:
            db.execute("INSERT OR REPLACE INTO fa_titles(tconst, title, source) VALUES (?,?,'user')", (f['tconst'], f['title']))
        n['fa_titles'] = len(data.get('fa_titles') or [])
        for r in data.get('removed') or []:
            db.execute('INSERT OR IGNORE INTO removed(raw, tconst, title, removed) VALUES (?,?,?,?)',
                       (r['raw'], r.get('tconst'), r.get('title'), r.get('removed')))
        n['removed'] = len(data.get('removed') or [])
        db.commit()
    n['lists'] = _restore_lists(lib, data.get('lists') or {})
    lib.version += 1
    if n['manual'] or n['lists']:
        lib.start(lib.full_update, queue=True)
    return n


def _restore_lists(lib, lists):
    """Names the backup holds that no list has now are written back to a list file (same file name; 'name (restored)' if
    that file exists). Lists that exist are never rewritten, and a name the user removed stays removed.
    Returns how many names came back."""
    if not isinstance(lists, dict) or not lists:
        return 0
    dest = lib.settings()['list_dir']
    os.makedirs(dest, exist_ok=True)
    have = set()
    for f in glob.glob(os.path.join(dest, '*.txt')):
        try:
            have.update(l.strip() for l in checker.read_text(f).splitlines())
        except OSError:
            pass
    with lib.lock:
        gone = {r[0] for r in lib.db.execute('SELECT raw FROM removed')}
    back = 0
    for name, names in lists.items():
        name = os.path.basename(str(name))
        if not name.lower().endswith('.txt'):
            continue
        missing = [x for x in dict.fromkeys(str(n).strip() for n in names) if x and x not in have and x not in gone]
        if not missing:
            continue
        target = os.path.join(dest, name)
        if os.path.exists(target):
            target = os.path.join(dest, os.path.splitext(name)[0] + ' (restored).txt')
        with open(target, 'a', encoding='utf-8') as fh:
            fh.write('\n'.join(missing) + '\n')
        have.update(missing)
        back += len(missing)
    return back


def folder(lib):
    d = os.path.join(lib.data, 'backups')
    os.makedirs(d, exist_ok=True)
    return d


def auto(lib):
    """One backup file per day, written when the app starts. Old ones are pruned."""
    path = os.path.join(folder(lib), f'personal-{time.strftime("%Y-%m-%d")}.json')
    snap = snapshot(lib)
    if os.path.exists(path):  # today's copy is rewritten only when something in it changed (new list names, a new favorite)
        try:
            with open(path, encoding='utf-8') as f:
                old = json.load(f)
        except (OSError, ValueError):
            old = None
        if old and {k: v for k, v in old.items() if k != 'created'} == {k: v for k, v in snap.items() if k != 'created'}:
            return None
    if not any(snap[k] for k in ('user', 'manual', 'dismissed', 'views', 'wishlist', 'fa_titles', 'removed', 'lists')):
        return None  # nothing personal yet
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(snap, f, ensure_ascii=False)
    for old in sorted(glob.glob(os.path.join(folder(lib), 'personal-*.json')))[:-KEEP]:
        os.remove(old)
    return path


def listing(lib):
    out = []
    for p in sorted(glob.glob(os.path.join(folder(lib), 'personal-*.json')), reverse=True):
        out.append({'name': os.path.basename(p), 'size': os.path.getsize(p)})
    return out


def load(lib, name):
    """Read one automatic backup by file name (no paths: only files in the backups folder)."""
    if os.path.basename(name) != name or not name.startswith('personal-') or not name.endswith('.json'):
        raise ValueError('Unknown backup.')
    with open(os.path.join(folder(lib), name), encoding='utf-8') as f:
        return json.load(f)
