"""Local HTTP API + static UI. Bound to 127.0.0.1 only."""
import csv
import io
import json
import mimetypes
import os
import re
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

from . import backup, exporter, extras, fullbackup
from .library import Library, image_type


def make_handler(lib: Library, web_dir):
    cache = {'version': -1, 'body': None}

    def movies_json():
        if cache['version'] != lib.version or cache['body'] is None:
            v = lib.version
            db = lib.reader()  # no lock: a running update does not hold the list back
            try:
                data = lib.movies(db)
            finally:
                db.close()
            cache['body'] = json.dumps({'version': v, 'movies': data}, ensure_ascii=False,
                                       separators=(',', ':')).encode('utf-8')
            cache['version'] = v
        return cache['body']

    class H(BaseHTTPRequestHandler):
        protocol_version = 'HTTP/1.1'

        def log_message(self, *a):
            pass

        def send(self, code, body, ctype='application/json; charset=utf-8', headers=None):
            if isinstance(body, (dict, list)):
                body = json.dumps(body, ensure_ascii=False).encode('utf-8')
            elif isinstance(body, str):
                body = body.encode('utf-8')
            self.send_response(code)
            self.send_header('Content-Type', ctype)
            self.send_header('Content-Length', str(len(body)))
            for k, v in (headers or {}).items():
                self.send_header(k, v)
            self.end_headers()
            self.wfile.write(body)

        def read_movies(self):
            db = lib.reader()
            try:
                return lib.movies(db)
            finally:
                db.close()

        def body(self):
            n = int(self.headers.get('Content-Length') or 0)
            return json.loads(self.rfile.read(n) or b'{}') if n else {}

        def do_GET(self):
            u = urlparse(self.path)
            path, qs = u.path, parse_qs(u.query)
            if path == '/' or path == '/index.html':
                return self.static('index.html')
            if path.startswith('/static/'):
                return self.static(path[len('/static/'):])
            m = re.fullmatch(r'/poster/tt(\d+)\.jpg', path)
            if m:
                p = lib.poster_path(int(m.group(1)))
                if os.path.exists(p):
                    # revalidated each time (a cheap 304), so a poster added by hand shows at once
                    st = os.stat(p)
                    etag = f'"{st.st_mtime_ns:x}-{st.st_size:x}"'
                    head = {'Cache-Control': 'no-cache', 'ETag': etag}
                    if self.headers.get('If-None-Match') == etag:
                        return self.send(304, b'', 'image/jpeg', head)
                    with open(p, 'rb') as f:
                        data = f.read()
                    return self.send(200, data, image_type(data) or 'image/jpeg', head)
                return self.send(404, b'', 'image/jpeg')
            m = re.fullmatch(r'/person/nm(\d+)\.jpg', path)
            if m:
                p = lib.person_photo_path(int(m.group(1)))
                if os.path.exists(p):
                    with open(p, 'rb') as f:
                        return self.send(200, f.read(), 'image/jpeg', {'Cache-Control': 'max-age=604800'})
                return self.send(404, b'', 'image/jpeg')
            m = re.fullmatch(r'/api/person/(\d+)', path)
            if m:
                return self.send(200, lib.person_profile(int(m.group(1)), refresh=bool(qs.get('refresh'))))
            if path == '/api/movies':
                return self.send(200, movies_json())
            if path == '/api/status':
                s = lib.settings()
                return self.send(200, {'job': {**lib.job, 'queued': len(lib.queue)}, 'version': lib.version, 'stats': lib.stats(),
                                       'first_run': lib.first_run(),
                                       'settings': {**s, 'tmdb_key': bool(s.get('tmdb_key')), 'omdb_key': bool(s.get('omdb_key'))}})
            if path == '/api/problems':
                with lib.lock:
                    return self.send(200, lib.problems())
            if path == '/api/removed':
                return self.send(200, lib.removed_items())
            if path == '/api/probe':
                return self.send(200, lib.probe_status())
            if path == '/api/people/status':
                return self.send(200, lib.people_status())
            if path == '/api/views':
                return self.send(200, lib.list_views())
            if path == '/api/wishlist':
                return self.send(200, lib.wishlist_items())
            if path == '/api/collections':
                return self.send(200, {'collections': lib.collections(), 'tmdb': bool(lib.settings().get('tmdb_key'))})
            if path == '/api/backup/full/info':
                return self.send(200, fullbackup.info(lib))
            if path == '/api/backup/full/result':
                return self.send(200, lib.restore_result or {})
            if path == '/api/backups':
                return self.send(200, backup.listing(lib))
            if path == '/api/rename-plan':
                pattern = (qs.get('pattern', [None])[0] or lib.settings().get('rename_pattern') or extras.DEFAULT_PATTERN).strip()
                bad = extras.pattern_problem(pattern)
                if bad:
                    return self.send(200, {'pattern': pattern, 'error': bad, 'fields': extras.PATTERN_FIELDS})
                if qs.get('pattern') and pattern != lib.settings().get('rename_pattern'):
                    lib.save_settings({'rename_pattern': pattern})  # the pattern the user typed is kept
                rows = extras.rename_plan(self.read_movies(), lib.settings().get('media_dirs'), pattern)
                todo = [r for r in rows if not r['same'] and not r['conflict']]
                return self.send(200, {'pattern': pattern, 'fields': extras.PATTERN_FIELDS, 'total': len(rows), 'todo': len(todo),
                                       'conflicts': sum(r['conflict'] for r in rows), 'same': sum(r['same'] for r in rows),
                                       'folders': sum(r['kind'] == 'folder' for r in todo), 'sample': todo[:300]})
            if path == '/api/issues':
                return self.send(200, lib.issues())
            if path == '/api/check':
                c = lib.check
                return self.send(200, {k: v for k, v in c.items() if k != 'result'} | {'has_result': c['result'] is not None})
            if path == '/api/check/result':
                return self.send(200, lib.check['result'] or {})
            if path == '/api/check.csv':
                return self.check_csv()
            if path == '/api/problems.csv':
                return self.problems_csv()
            if path == '/api/issues.csv':
                return self.issues_csv(qs.get('kind', ['dupes'])[0], qs.get('sub', ['all'])[0])
            m = re.fullmatch(r'/api/lookup/(\d+)', path)
            if m:
                return self.send(200, lib.lookup(int(m.group(1)), (qs.get('q') or [None])[0]))
            if path == '/api/export.csv':
                return self.export()
            if path == '/api/export/columns':
                return self.send(200, exporter.columns_info())
            m = re.fullmatch(r'/api/export/file/([\w-]+)', path)
            if m:
                item = exporter.STORE.get(m.group(1))
                if not item:
                    return self.send(404, {'error': 'This export has expired. Export again.'})
                return self.send(200, item[2], item[1], {'Content-Disposition': f'attachment; filename="{item[0]}"'})
            return self.send(404, {'error': 'not found'})

        def do_POST(self):
            origin = self.headers.get('Origin')
            if origin and urlparse(origin).netloc != self.headers.get('Host'):  # a page from another site must not drive the app
                return self.send(403, {'error': 'forbidden'})
            path = urlparse(self.path).path
            b = self.body()
            if path == '/api/views':
                try:
                    lib.save_view(b.get('name'), b.get('state') or {})
                    return self.send(200, {'ok': True})
                except ValueError as e:
                    return self.send(200, {'error': str(e)})
            if path == '/api/remove':
                return self.send(200, {'removed': lib.remove_from_list(b.get('ids') or [])})
            if path == '/api/removed/restore':
                return self.send(200, {'restored': lib.restore_removed(b.get('raws') or [])})
            m = re.fullmatch(r'/api/poster/(\d+)', path)
            if m:
                tc = int(m.group(1))
                try:
                    if b.get('clear'):
                        lib.clear_poster(tc)
                    else:
                        data = b.get('data')  # an image chosen in the page: a data: URL
                        if data:
                            import base64
                            head, _, payload = str(data).partition(',')
                            data = base64.b64decode(payload, validate=False) if ';base64' in head else None
                        lib.set_poster(tc, data or lib.poster_bytes(b.get('source')))
                    return self.send(200, {'ok': True})
                except ValueError as e:
                    return self.send(200, {'error': str(e)})
            if path == '/api/fatitle':
                title = b.get('title')
                lib.set_fa_title(int(b.get('tconst') or 0), None if title is None else str(title).strip())
                return self.send(200, {'ok': True})
            if path == '/api/views/delete':
                lib.delete_view(int(b.get('id') or 0))
                return self.send(200, {'ok': True})
            if path == '/api/wishlist/add':
                return self.send(200, {'added': lib.wish_add(b.get('items') or [])})
            if path == '/api/wishlist/set':
                lib.wish_set(int(b.get('id') or 0), b.get('status'), bool(b.get('delete')))
                return self.send(200, {'ok': True})
            if path == '/api/collections/fetch':
                return self.send(200, {'started': lib.start(extras.fetch_collections, lib, queue=True)})
            if path == '/api/people/scan':
                scope = b.get('scope') if b.get('scope') in ('directors', 'main', 'all') else 'main'
                lib.save_settings({'people_scope': scope})
                return self.send(200, {'started': lib.start(lib.fetch_people, scope, queue=True)})
            if path == '/api/probe/start':
                return self.send(200, {'started': lib.start(lib.probe_media, queue=True)})
            if path == '/api/job/stop':
                return self.send(200, {'stopping': lib.stop_job()})
            if path in ('/api/lists/preview', '/api/lists/load'):
                src = {'text': b['text'], 'name': b.get('name')} if b.get('text') is not None else {'path': b.get('path')}
                try:
                    if path == '/api/lists/preview':
                        return self.send(200, lib.preview_list(src))
                    return self.send(200, lib.load_list(src, str(b.get('mode') or '')))
                except (ValueError, OSError) as e:
                    return self.send(200, {'error': str(e)})
            if path in ('/api/rescan', '/api/media/scan'):
                if b.get('now'):  # the user chose to stop the running job (a long people scan, say) and check right away
                    lib.stop_job()
                started = lib.start(lib.rescan, queue=True)
                return self.send(200, {'started': started, 'stopping': bool(lib.job.get('stopping'))})
            if path == '/api/reveal':
                try:
                    return self.send(200, {'ok': extras.reveal(lib, str(b.get('path') or ''))})
                except Exception as e:
                    return self.send(200, {'error': str(e)})
            if path == '/api/firstrun/fresh':
                if not lib.first_run():
                    return self.send(200, {'error': 'MovieShelf is already set up.'})
                try:
                    return self.send(200, {'started': lib.start_fresh(b.get('list_path'), b.get('media_dir'))})
                except (ValueError, OSError) as e:
                    return self.send(200, {'error': str(e)})
            if path == '/api/backup/full/save':
                dest = str(b.get('path') or '') or fullbackup.default_path(lib)
                if not dest.lower().endswith('.zip') or not os.path.isdir(os.path.dirname(os.path.abspath(dest))):
                    return self.send(200, {'error': 'Choose a file name ending in .zip in a folder that exists.'})
                return self.send(200, {'started': lib.start(lib.save_full_backup, dest, b.get('options') or {}), 'path': dest})
            if path == '/api/backup/full/inspect':
                try:
                    return self.send(200, fullbackup.inspect(str(b.get('path') or '').strip().strip('"')))
                except ValueError as e:
                    return self.send(200, {'error': str(e)})
            if path == '/api/backup/full/restore':
                src = str(b.get('path') or '').strip().strip('"')
                try:
                    fullbackup.inspect(src)
                except ValueError as e:
                    return self.send(200, {'error': str(e)})
                lib.restore_result = None
                started = lib.start(lib.restore_full_backup, src, b.get('options') or {})
                if started and b.get('update'):
                    lib.start(lib.full_update, queue=True)  # first run: fetch whatever the backup did not hold (IMDb data, new posters)
                return self.send(200, {'started': started})
            if path == '/api/backup/make':
                data = json.dumps(lib.make_backup(), ensure_ascii=False, indent=1).encode('utf-8')
                name = f'MovieShelf backup {time.strftime("%Y-%m-%d")}.json'
                return self.send(200, {'token': exporter.keep(name, 'application/json', data), 'name': name, 'rows': 0})
            if path == '/api/backup/import':
                try:
                    return self.send(200, {'restored': lib.restore_backup(b.get('data'), b.get('name'))})
                except Exception as e:
                    return self.send(200, {'error': f'{type(e).__name__}: {e}'})
            if path == '/api/stash':  # the page made a file (wishlist text, ...): offer it as a download / Save as
                data = str(b.get('text') or '').encode('utf-8')
                if b.get('bom'):
                    data = b'\xef\xbb\xbf' + data
                name = os.path.basename(str(b.get('name') or 'export.txt'))
                return self.send(200, {'token': exporter.keep(name, b.get('type') or 'text/plain; charset=utf-8', data), 'name': name, 'rows': int(b.get('rows') or 0)})
            if path == '/api/rename-plan/file':
                pattern = (b.get('pattern') or lib.settings().get('rename_pattern') or extras.DEFAULT_PATTERN).strip()
                if extras.pattern_problem(pattern):
                    return self.send(400, {'error': extras.pattern_problem(pattern)})
                rows = extras.rename_plan(self.read_movies(), lib.settings().get('media_dirs'), pattern)
                kind = b.get('kind')
                data, name, ctype = {
                    'bat': (extras.plan_bat, 'MovieShelf rename plan.bat', 'application/octet-stream'),
                    'csv': (extras.plan_csv, 'MovieShelf rename plan.csv', 'text/csv; charset=utf-8'),
                    'list': (extras.plan_list, 'MovieShelf new names.txt', 'text/plain; charset=utf-8'),
                }.get(kind, (None, None, None))
                if not data:
                    return self.send(404, {'error': 'unknown file'})
                return self.send(200, {'token': exporter.keep(name, ctype, data(rows)), 'name': name, 'rows': len(rows)})
            if path == '/api/issue/dismiss':
                lib.dismiss(str(b.get('ikey') or ''), bool(b.get('undo')))
                return self.send(200, {'ok': True})
            if path == '/api/export':
                try:
                    db = lib.reader()
                    try:
                        movies = lib.movies(db)
                    finally:
                        db.close()
                    name, ctype, data, n = exporter.build(movies, b)
                    return self.send(200, {'token': exporter.keep(name, ctype, data), 'name': name, 'rows': n, 'size': len(data)})
                except Exception as e:
                    return self.send(200, {'error': f'{type(e).__name__}: {e}'})
            if path == '/api/update':
                ok = lib.start(lib.full_update, bool(b.get('force_download')), queue=True)
                return self.send(200, {'started': ok})
            if path == '/api/posters':
                ok = lib.start(lib.fetch_posters, True, queue=True)
                return self.send(200, {'started': ok})
            if path == '/api/rematch':
                def rematch():
                    lib.match_all(only_unmatched=False)
                    lib.enrich()
                    lib.fetch_posters()
                    lib.fetch_tmdb()
                return self.send(200, {'started': lib.start(rematch)})
            if path == '/api/ratings':
                return self.send(200, {'started': lib.start(lib.refresh_ratings, queue=True)})
            if path == '/api/scores':
                return self.send(200, {'started': lib.start(lib.fetch_scores, True, queue=True)})
            if path == '/api/check':
                try:
                    return self.send(200, {'started': lib.start_check(b)})
                except Exception as e:
                    return self.send(200, {'started': False, 'error': str(e)})
            if path == '/api/check/add':
                return self.send(200, {'added': lib.add_from_check(b.get('items') or [])})
            m = re.fullmatch(r'/api/accept/(\d+)', path)
            if m:
                lib.accept(int(m.group(1)))
                return self.send(200, {'ok': True})
            if path == '/api/omdb/test':
                from .scores import omdb_check
                try:
                    ok, err = omdb_check(b.get('key', '').strip())
                    return self.send(200, {'ok': ok, 'error': err})
                except Exception as e:
                    return self.send(200, {'ok': False, 'error': str(e)})
            if path == '/api/settings':
                s = lib.save_settings(b)
                return self.send(200, {**s, 'tmdb_key': bool(s.get('tmdb_key')), 'omdb_key': bool(s.get('omdb_key'))})
            if path == '/api/tmdb/test':
                from .online import Tmdb
                try:
                    return self.send(200, {'ok': Tmdb(b.get('key', '')).check()})
                except Exception as e:
                    return self.send(200, {'ok': False, 'error': str(e)})
            m = re.fullmatch(r'/api/match/(\d+)', path)
            if m:
                tt = str(b.get('imdb') or '').strip()
                mm = re.search(r'tt(\d+)', tt) or re.fullmatch(r'(\d+)', tt)
                tconst = int(mm.group(1)) if mm else None
                threading.Thread(target=lib.set_match, args=(int(m.group(1)), tconst), daemon=True).start()
                return self.send(200, {'ok': True, 'tconst': tconst})
            if path == '/api/user/bulk':
                n = lib.set_user_many([str(k) for k in b.get('keys') or []], b.get('data') or {})
                return self.send(200, {'ok': True, 'n': n})
            m = re.fullmatch(r'/api/user/([\w]+)', path)
            if m:
                lib.set_user(m.group(1), b)
                return self.send(200, {'ok': True})
            return self.send(404, {'error': 'not found'})

        def static(self, rel):
            p = os.path.normpath(os.path.join(web_dir, rel))
            if not p.startswith(os.path.normpath(web_dir)) or not os.path.isfile(p):
                return self.send(404, b'', 'text/plain')
            ctype = mimetypes.guess_type(p)[0] or 'application/octet-stream'
            if ctype.startswith('text/') or ctype.endswith('javascript'):
                ctype += '; charset=utf-8'
            with open(p, 'rb') as f:
                self.send(200, f.read(), ctype, {'Cache-Control': 'no-cache'})

        def export(self):
            out = io.StringIO()
            w = csv.writer(out)
            w.writerow(['IMDb', 'Title', 'English title', 'Original title', 'Persian title', 'Year', 'Type', 'Rating',
                        'Votes', 'Rotten Tomatoes %', 'Metacritic', 'Runtime', 'Genres', 'Directors', 'Cast', 'Countries', 'Languages', 'Resolution',
                        'Files', 'Favorite', 'Watched', 'My rating', 'Notes', 'Match'])
            for m in lib.movies():
                w.writerow([m.get('tt', ''), m.get('title'), m.get('en'), m.get('orig'), m.get('fa_t'), m.get('year'),
                            m.get('type'), m.get('rating'), m.get('votes'), m.get('rt'), m.get('mc'), m.get('runtime'),
                            ', '.join(m.get('genres') or []), ', '.join(d[1] for d in m.get('dir') or []),
                            ', '.join(c[1] for c in (m.get('cast') or [])[:8]), ', '.join(m.get('countries') or []),
                            ', '.join(m.get('langs') or []), m.get('res'),
                            ' | '.join(f['raw'] for f in m['files']), m.get('fav', 0), m.get('watched', 0),
                            m.get('myrating'), m.get('notes'), m.get('conf')])
            self.csv_out(out, 'movies.csv')

        def csv_out(self, out, name):
            # utf-8 BOM so Excel shows Persian / accented titles correctly
            self.send(200, '﻿' + out.getvalue(), 'text/csv; charset=utf-8',
                      {'Content-Disposition': f'attachment; filename="{name}"'})

        def check_csv(self):
            r = lib.check['result'] or {'items': [], 'missing': []}
            out = io.StringIO()
            w = csv.writer(out)
            w.writerow(['Status', 'Name', 'Quality', 'IMDb', 'IMDb title', 'IMDb year', 'Match', 'Problem',
                        'In library', 'Also in this list', 'Path', 'Size GB'])
            for it in r['items']:
                im = it.get('imdb') or {}
                w.writerow([it['status'], it['raw'], it['quality'], f"tt{it['tconst']:07d}" if it['tconst'] else '',
                            im.get('title'), im.get('year'), it['conf'], it.get('reason', ''),
                            ' | '.join(f"{f['raw']} ({f['quality']})" for f in it.get('lib') or []),
                            ' | '.join(it.get('twins') or []), it.get('path') or '',
                            round(it['size'] / 1e9, 2) if it.get('size') else ''])
            for m in r['missing']:
                w.writerow(['only in library', ' | '.join(m['files']), '', f"tt{m['tconst']:07d}", m['title'], m['year'],
                            '', '', '', '', '', ''])
            self.csv_out(out, 'list-check.csv')

        def issues_csv(self, kind, sub):
            rows = lib.issues().get(kind) or []
            if sub != 'all':
                rows = [r for r in rows if r.get('sub') == sub]
            out = io.StringIO()
            w = csv.writer(out)
            w.writerow(['Title', 'Year', 'IMDb', 'Rating', 'Votes', 'Problem', 'Files', 'Ignored'])
            for r in rows:
                w.writerow([r['title'], r['year'], r['tt'] or '', r['rating'], r['votes'], r['note'],
                            ' | '.join(f"{f['raw']} [{f['quality']}{', ' + f['verdict'] if f['verdict'] else ''}]" for f in r['files']),
                            'yes' if r['ignored'] else ''])
            self.csv_out(out, f'problems-{kind}.csv')

        def problems_csv(self):
            out = io.StringIO()
            w = csv.writer(out)
            w.writerow(['Name', 'Problem', 'Current IMDb', 'Current title', 'Year', 'Other candidates'])
            with lib.lock:
                rows = lib.problems()
            for p in rows:
                c = p['current'] or {}
                w.writerow([p['raw'], p['reason'], f"tt{c['tconst']:07d}" if c else '', c.get('title'), c.get('year'),
                            ' | '.join(f"tt{x['tconst']:07d} {x['title']} ({x['year']})" for x in p['cands'])])
            self.csv_out(out, 'problems.csv')

    return H


class _Server(ThreadingHTTPServer):
    daemon_threads = True

    def handle_error(self, request, client_address):
        # the window closing a connection mid-request is normal: keep it out of the log so real errors stand out
        if isinstance(sys.exc_info()[1], (ConnectionError, TimeoutError)):
            return
        super().handle_error(request, client_address)


def serve(lib, web_dir, port=0):
    httpd = _Server(('127.0.0.1', port), make_handler(lib, web_dir))
    t = threading.Thread(target=httpd.serve_forever, daemon=True)
    t.start()
    return httpd
