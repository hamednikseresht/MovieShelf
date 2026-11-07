"""Full backup: one .zip with everything the app has, and the way back.

Inside the zip:  manifest.json · library.db (every table: movies, matches, titles, cast, scores, your favorites, notes,
saved views, wishlist, scanned folders, ...) · settings.json (with the API keys unless you leave them out) ·
lists/ (your list .txt files) · posters/ · and, only if asked, imdb/ and imdb_index.db (about 3 GB; they can be downloaded again).

The zip can hold your API keys in plain text: keep it private."""
import glob
import json
import os
import shutil
import sqlite3
import tempfile
import time
import zipfile

from . import backup, paths

FORMAT = 1
KEY_FIELDS = ('tmdb_key', 'omdb_key')


def _files(folder, pattern='*'):
    return [p for p in glob.glob(os.path.join(folder, pattern)) if os.path.isfile(p)]


def _size(paths):
    return sum(os.path.getsize(p) for p in paths)


def info(lib):
    """What a full backup would contain, with sizes, for the options window."""
    s = lib.settings()
    posters = _files(lib.poster_dir)
    lists = _files(s['list_dir'], '*.txt') if os.path.isdir(s['list_dir']) else []
    imdb = _files(lib.imdb_dir) + ([lib.index_path] if os.path.exists(lib.index_path) else [])
    return {'db': os.path.getsize(lib.db_path), 'posters': {'n': len(posters), 'bytes': _size(posters)},
            'lists': {'n': len(lists), 'bytes': _size(lists)}, 'imdb': {'n': len(imdb), 'bytes': _size(imdb)},
            'keys': any(s.get(k) for k in KEY_FIELDS), 'list_dir': s['list_dir']}


def default_path(lib):
    return os.path.join(backup.folder(lib), f'MovieShelf full backup {time.strftime("%Y-%m-%d %H%M")}.zip')


def save_full(lib, path, opts):
    """Write the zip. opts: {posters, lists, imdb, keys} (booleans). Runs as a job: progress shows in the top bar."""
    path = os.path.abspath(path)
    if not path.lower().endswith('.zip'):
        path += '.zip'
    os.makedirs(os.path.dirname(path), exist_ok=True)
    s = lib.settings()
    tmp = tempfile.mkdtemp(prefix='movieshelf_backup_')
    part = path + '.part'
    try:
        lib.progress('Backup: copying the database')
        dbcopy = os.path.join(tmp, 'library.db')
        with lib.lock:
            dest = sqlite3.connect(dbcopy)
            lib.db.backup(dest)
            dest.close()
        out = dict(s)
        if not opts.get('keys'):
            for k in KEY_FIELDS:
                out[k] = ''
        settings_file = os.path.join(tmp, 'settings.json')
        with open(settings_file, 'w', encoding='utf-8') as f:
            json.dump(out, f, indent=2, ensure_ascii=False)
        entries = [('library.db', dbcopy, True), ('settings.json', settings_file, True)]
        lists = _files(s['list_dir'], '*.txt') if opts.get('lists') and os.path.isdir(s['list_dir']) else []
        entries += [(f'lists/{os.path.basename(p)}', p, True) for p in lists]
        posters = _files(lib.poster_dir) if opts.get('posters') else []
        entries += [(f'posters/{os.path.basename(p)}', p, False) for p in posters]  # jpegs do not compress
        entries += [(f'people/{os.path.basename(p)}', p, False) for p in (_files(lib.people_dir) if opts.get('posters') else [])]
        imdb = []
        if opts.get('imdb'):
            imdb = [(f'imdb/{os.path.basename(p)}', p, False) for p in _files(lib.imdb_dir)]
            if os.path.exists(lib.index_path):
                imdb.append(('imdb_index.db', lib.index_path, True))
        entries += imdb
        manifest = {'app': 'MovieShelf', 'format': FORMAT, 'created': time.strftime('%Y-%m-%d %H:%M:%S'),
                    'has_keys': bool(opts.get('keys')) and any(s.get(k) for k in KEY_FIELDS),
                    'has': {'lists': len(lists), 'posters': len(posters), 'imdb': len(imdb)},
                    'list_dir': s['list_dir'], 'media_dirs': s.get('media_dirs') or []}
        with zipfile.ZipFile(part, 'w', zipfile.ZIP_DEFLATED, allowZip64=True) as z:
            z.writestr('manifest.json', json.dumps(manifest, indent=1, ensure_ascii=False))
            for i, (arc, fp, compress) in enumerate(entries, 1):
                if i % 50 == 0 or arc.startswith('imdb'):
                    lib.progress('Backup: writing files', i, len(entries))
                z.write(fp, arc, compress_type=zipfile.ZIP_DEFLATED if compress else zipfile.ZIP_STORED)
        os.replace(part, path)
        lib.log(f'Full backup saved: {path} ({os.path.getsize(path) / 1e6:.0f} MB, {len(entries)} files)')
        return path
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
        if os.path.exists(part):
            os.remove(part)


def inspect(path):
    """The manifest of a backup file, or ValueError saying why it is not one."""
    try:
        with zipfile.ZipFile(path) as z:
            m = json.loads(z.read('manifest.json'))
            names = z.namelist()
    except (OSError, KeyError, zipfile.BadZipFile, ValueError):
        raise ValueError('This is not a MovieShelf full backup (.zip).')
    if m.get('app') not in ('MovieShelf', 'MovieDB') or 'library.db' not in names:  # MovieDB: backups made before the rename
        raise ValueError('This is not a MovieShelf full backup (.zip).')
    m['has_settings'] = 'settings.json' in names
    m['size'] = os.path.getsize(path)
    return m


def _safe(name):
    """A zip member name that stays inside the folder it is extracted to."""
    parts = name.replace('\\', '/').split('/')
    return bool(name) and not os.path.isabs(name) and '..' not in parts and ':' not in name


def _extract(z, member, target):
    if not _safe(member):
        raise ValueError(f'Unsafe path in the backup: {member}')
    os.makedirs(os.path.dirname(target), exist_ok=True)
    with z.open(member) as src, open(target, 'wb') as dst:
        shutil.copyfileobj(src, dst, 1 << 20)


def restore_full(lib, path, opts):
    """Replace the library (and, as chosen, settings, API keys, list files, posters, IMDb data) with a backup.
    What exists now is copied to data/backups/before-restore-<time> first. opts: {settings, keys, lists, posters, imdb}."""
    m = inspect(path)
    stamp = time.strftime('%Y-%m-%d_%H%M%S')
    safety = os.path.join(backup.folder(lib), f'before-restore-{stamp}')
    os.makedirs(safety, exist_ok=True)
    lib.progress('Restore: saving what you have now')
    with lib.lock:
        dest = sqlite3.connect(os.path.join(safety, 'library.db'))
        lib.db.backup(dest)
        dest.close()
    if os.path.exists(lib.settings_path):
        shutil.copy2(lib.settings_path, os.path.join(safety, 'settings.json'))
    cur = lib.settings()
    for p in _files(cur['list_dir'], '*.txt') if os.path.isdir(cur['list_dir']) else []:
        os.makedirs(os.path.join(safety, 'lists'), exist_ok=True)
        shutil.copy2(p, os.path.join(safety, 'lists', os.path.basename(p)))
    warnings, done = [], {}
    tmp = tempfile.mkdtemp(prefix='movieshelf_restore_')
    try:
        with zipfile.ZipFile(path) as z:
            names = z.namelist()
            lib.progress('Restore: reading the database')
            dbfile = os.path.join(tmp, 'library.db')
            _extract(z, 'library.db', dbfile)
            src = sqlite3.connect(dbfile)
            with lib.lock:
                src.backup(lib.db)  # the open database takes over the backup's pages
                lib._ensure_schema()  # a backup from an older version may lack newer tables and columns
            src.close()
            done['library'] = True
            settings = cur
            if opts.get('settings') and 'settings.json' in names:
                with z.open('settings.json') as f:
                    new = json.load(f)
                if not opts.get('keys'):
                    for k in KEY_FIELDS:
                        new[k] = cur.get(k, '')  # keep the keys that are set now
                elif not any(new.get(k) for k in KEY_FIELDS):
                    for k in KEY_FIELDS:
                        new[k] = cur.get(k, '')  # the backup has none: do not erase yours
                settings = {**cur, **new}
                done['settings'] = True
            if opts.get('lists'):
                lists = [n for n in names if n.startswith('lists/') and n.endswith('.txt')]
                if lists:
                    target = settings['list_dir']
                    if not os.path.isdir(target):
                        target = paths.default_list_dir()
                        settings['list_dir'] = target
                        warnings.append(f'The old list folder does not exist here, so the list files went to {target} and the list folder setting now points to it.')
                    for n in lists:
                        _extract(z, n, os.path.join(target, os.path.basename(n)))
                    done['lists'] = len(lists)
            missing = [d for d in settings.get('media_dirs') or [] if not os.path.isdir(d)]
            if missing:
                warnings.append('Movie folders not found on this PC (change them in Settings → General): ' + ', '.join(missing))
            if opts.get('posters'):
                posters = [n for n in names if n.startswith('posters/') and not n.endswith('/')]
                for i, n in enumerate(posters, 1):
                    if i % 100 == 0:
                        lib.progress('Restore: posters', i, len(posters))
                    _extract(z, n, os.path.join(lib.poster_dir, os.path.basename(n)))
                for n in [n for n in names if n.startswith('people/') and not n.endswith('/')]:
                    _extract(z, n, os.path.join(lib.people_dir, os.path.basename(n)))
                done['posters'] = len(posters)
            if opts.get('imdb'):
                imdb = [n for n in names if n.startswith('imdb/') and not n.endswith('/')]
                for i, n in enumerate(imdb, 1):
                    lib.progress('Restore: IMDb data', i, len(imdb))
                    _extract(z, n, os.path.join(lib.imdb_dir, os.path.basename(n)))
                if 'imdb_index.db' in names:
                    _extract(z, 'imdb_index.db', lib.index_path)
                    lib.matcher.reset()
                done['imdb'] = len(imdb)
        with open(lib.settings_path, 'w', encoding='utf-8') as f:
            json.dump(settings, f, indent=2, ensure_ascii=False)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    lib._cand_cache.clear()
    lib.version += 1
    lib.log(f'Full backup restored from {os.path.basename(path)} (made {m.get("created")}). A copy of the previous state is in {safety}')
    for w in warnings:
        lib.log(w)
    return {'done': done, 'warnings': warnings, 'safety': safety}
