"""Backend tests. Run from the MovieShelf folder:  python -m unittest discover -s tests -v
Standard library only; each test builds a small library in a temporary folder."""
import io
import json
import os
import shutil
import sys
import tempfile
import threading
import time
import unittest
import xml.dom.minidom
import zipfile

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'app'))

from movieshelf import backup, exporter, extras, issues, paths  # noqa: E402
from movieshelf.checker import compare_quality  # noqa: E402
from movieshelf.library import Cancelled, Library  # noqa: E402
from movieshelf.parse import parse  # noqa: E402


class TempLibrary(unittest.TestCase):
    """A library with a few hand-made titles and files."""

    def setUp(self):
        self.root = tempfile.mkdtemp()
        os.makedirs(os.path.join(self.root, 'data'))
        from movieshelf import paths
        self._default_lists = paths.default_list_dir
        paths.default_list_dir = lambda: os.path.join(self.root, 'default lists')  # never touch the real Documents folder
        self.lib = Library(self.root)
        self.n = 0

    def tearDown(self):
        from movieshelf import paths
        paths.default_list_dir = self._default_lists
        self.lib.db.close()
        shutil.rmtree(self.root, ignore_errors=True)

    def title(self, tconst, name, year, type_='movie', poster='x'):
        self.lib.db.execute('INSERT INTO titles(tconst, type, title, en_title, year, rating, votes, poster, genres) '
                            'VALUES (?,?,?,?,?,?,?,?,?)', (tconst, type_, name, name, year, 7.5, 1000, poster, 'Drama'))

    def file(self, raw, tconst, **kw):
        self.n += 1
        p = parse(raw)
        row = dict(raw=raw, list_file='t.txt', present=1, added='x', title=p['title'], alt_title=p['alt_title'], year=p['year'],
                   resolution=p['resolution'], source=p['source'], codec=p['codec'], bit10=p['bit10'], hdr=p['hdr'],
                   audio=p['audio'], farsi_dubbed=p['farsi_dubbed'], dual_audio=p['dual_audio'], softsub=p['softsub'],
                   edition=p['edition'], grp=p['group'], tconst=tconst, conf='exact', manual=0, matched=1)
        row.update(kw)
        cols = ', '.join(row)
        self.lib.db.execute(f'INSERT INTO files({cols}) VALUES ({", ".join("?" * len(row))})', list(row.values()))
        self.lib.db.commit()


class ParseAndQuality(unittest.TestCase):
    def test_parse_basics(self):
        p = parse('Heat.1995.1080p.BluRay.x265.10bit')
        self.assertEqual((p['title'], p['year'], p['resolution'], p['source']), ('Heat', 1995, '1080p', 'BluRay'))
        self.assertTrue(p['bit10'])

    def test_parse_farsi_dub(self):
        self.assertTrue(parse('Way.Out.West.1937.DVDRip.Farsi.Dubbed(TinyMoviez)')['farsi_dubbed'])

    def test_compare_quality(self):
        a, b = parse('X.2000.1080p.BluRay'), parse('X.2000.720p.BluRay')
        self.assertGreater(compare_quality(a, b), 0)
        self.assertLess(compare_quality(b, a), 0)

    def test_unknown_resolution_is_not_worse(self):
        self.assertEqual(compare_quality(parse('X.2000'), parse('X.2000.1080p')), 0)


class Issues(TempLibrary):
    def test_duplicate_kinds(self):
        self.title(1, 'Alpha', 2000)
        self.file('Alpha.2000.1080p.BluRay', 1)
        self.file('Alpha.2000.720p.WEB-DL', 1)
        self.title(2, 'Beta', 2001)
        self.file('Beta.2001.1080p.BluRay', 2)
        self.file('Beta.2001.Farsi.Dubbed.1080p', 2)
        self.title(3, 'Gamma', 2002)
        self.file('Gamma.2002.1080p.BluRay', 3)
        self.file('Gamma.2002.1080p.BluRay.YIFY', 3)
        out = issues.build(self.lib)
        subs = {r['title']: r['sub'] for r in out['dupes']}
        self.assertEqual(subs, {'Alpha': 'lower', 'Beta': 'versions', 'Gamma': 'same'})

    def test_different_movies_under_one_id(self):
        self.title(1, 'Saga', 2013)
        self.file('Saga.Vol.1.2013.1080p', 1)
        self.file('Saga.Vol.2.2013.1080p', 1)
        self.title(2, '3:10 to Yuma', 1957)
        self.file('3.10.to.Yuma.1957.1080p', 2)
        self.file('310.to.Yuma.1957.1080p.YIFY', 2)
        self.title(3, 'Blue', 2000)
        self.file('Blue.2000.1080p', 3)
        self.file('Blue.EXTRAS.2000.720p', 3)
        subs = {r['title']: r['sub'] for r in issues.build(self.lib)['dupes']}
        self.assertEqual(subs['Saga'], 'mixed')
        self.assertEqual(subs['Blue'], 'mixed')
        self.assertEqual(subs['3:10 to Yuma'], 'same')  # '3.10' and '310' are the same title

    def test_low_unknown_dubbed_year_and_poster(self):
        self.title(1, 'Old', 1950, poster=None)
        self.file('Old.1950.DVDRip', 1, farsi_dubbed=1)
        self.title(2, 'Odd', 2005)
        self.file('Odd.2001.1080p', 2)
        self.title(3, 'Special', 2010, type_='tvSpecial')
        self.file('Special.2010.1080p', 3)
        self.title(4, 'Show', 2011, type_='tvMiniSeries')
        self.file('Show.2011.1080p', 4)
        out = issues.build(self.lib)
        self.assertEqual([r['title'] for r in out['lowq']], ['Old'])
        self.assertEqual([r['title'] for r in out['dubonly']], ['Old'])
        self.assertEqual([r['title'] for r in out['noposter']], ['Old'])
        self.assertEqual([r['title'] for r in out['yeardiff']], ['Odd'])
        self.assertEqual([r['title'] for r in out['nonfilm']], ['Special'])  # a series has its own section: not a problem
        self.assertEqual(len(out['unknownq']), 0)

    def test_file_name_differs_from_the_film_name(self):
        self.title(1, 'The Godfather', 1972)
        self.file('Godfather.1972.1080p', 1)
        self.title(2, 'Léon: The Professional', 1994)
        self.file('Leon.The.Professional.1994.1080p', 2)
        self.title(3, 'Heat', 1995)
        self.file('Heat.1995.Directors.Cut.1080p', 3)
        self.title(4, 'Alien', 1979)
        self.file('Aliens.Nostromo.1979.1080p', 4)
        self.title(5, 'Fuzzy', 2001)
        self.file('Fuzy.2001.1080p', 5, conf='fuzzy')
        self.title(6, 'Right', 2002)
        self.file('Rihgt.2002.1080p', 6, conf='fuzzy', manual=1)
        out = issues.build(self.lib)['namediff']
        by = {r['title']: r for r in out}
        self.assertEqual(sorted(by), ['Alien', 'Right', 'The Godfather'])  # Léon, Heat: same name; Fuzzy: shown in Matching
        self.assertEqual(by['The Godfather']['sub'], 'close')
        self.assertIn('“Godfather”', by['The Godfather']['note'])
        self.assertIn('“The Godfather”', by['The Godfather']['note'])
        self.assertEqual(by['Alien']['sub'], 'far')
        self.assertEqual(out[0]['title'], 'Alien')  # the most different name first

    def test_sizes_decide_equal_quality_and_waste(self):
        self.title(1, 'Same', 2000)
        self.file('Same.2000.1080p.A', 1)
        self.file('Same.2000.1080p.B', 1)
        self.lib.db.executemany('INSERT INTO fileinfo(path, raw, size, mtime) VALUES (?,?,?,0)',
                                [('D:/a/Same.2000.1080p.A.mkv', 'Same.2000.1080p.A', 5000),
                                 ('D:/b/Same.2000.1080p.B.mkv', 'Same.2000.1080p.B', 9000),
                                 ('E:/b/Same.2000.1080p.B.mkv', 'Same.2000.1080p.B', 9000)])
        out = issues.build(self.lib)
        row = next(r for r in out['dupes'] if r['sub'] == 'same')
        self.assertEqual([f['verdict'] for f in row['files']], ['same', 'keep'])  # the bigger B is kept
        self.assertEqual(row['wasted'], 5000)
        copies = next(r for r in out['dupes'] if r['sub'] == 'copies')
        self.assertEqual(copies['wasted'], 9000)

    def test_ignore_and_restore(self):
        self.title(1, 'Alpha', 2000)
        self.file('Alpha.2000.1080p', 1)
        self.file('Alpha.2000.720p', 1)
        key = issues.build(self.lib)['dupes'][0]['ikey']
        self.lib.dismiss(key)
        self.assertTrue(issues.build(self.lib)['dupes'][0]['ignored'])
        self.assertEqual(issues.open_duplicates(self.lib), 0)
        self.lib.dismiss(key, undo=True)
        self.assertEqual(issues.open_duplicates(self.lib), 1)


class Exporting(TempLibrary):
    def movies(self):
        return [{'k': 'tt0000001', 'tt': 'tt0000001', 'en': 'Alpha & <Beta>', 'title': 'Alpha', 'year': 2000, 'rating': 8.1, 'votes': 1234,
                 'genres': ['Drama', 'Crime'], 'dir': [[1, 'Ann Director']], 'res': '1080p',
                 'files': [{'id': 1, 'raw': 'Alpha.2000.1080p', 'res': '1080p', 'src': 'BluRay', 'size': 2_000_000_000, 'paths': ['D:/m/Alpha.mkv']}]},
                {'k': 'f2', 'en': None, 'title': 'Loose file', 'year': None, 'files': [{'id': 2, 'raw': 'Loose.file', 'res': None}]}]

    def test_xlsx_is_valid_and_complete(self):
        name, ctype, data, n = exporter.build(self.movies(), {'columns': ['en', 'year', 'imdb', 'rating', 'votes', 'size', 'fname'],
                                                             'keys': ['tt0000001', 'f2'], 'format': 'xlsx',
                                                             'sheets': [{'name': 'A/B: [x]', 'keys': ['tt0000001']}, {'name': 'a/b: [x]', 'keys': ['f2']}]})
        self.assertTrue(name.endswith('.xlsx'))
        self.assertEqual(n, 2)
        z = zipfile.ZipFile(io.BytesIO(data))
        for part in z.namelist():
            xml.dom.minidom.parseString(z.read(part))  # every part is well-formed
        wb = z.read('xl/workbook.xml').decode()
        self.assertIn('name="A-B- -x-"', wb)
        self.assertIn('name="a-b- -x- (2)"', wb)  # names stay unique
        sheet = z.read('xl/worksheets/sheet1.xml').decode()
        self.assertIn('Alpha &amp; &lt;Beta&gt;', sheet)
        self.assertIn('HYPERLINK("https://www.imdb.com/title/tt0000001/"', sheet)
        self.assertIn('<v>2.0</v>', sheet)  # size in GB
        self.assertIn('<v>1234</v>', sheet)

    def test_csv_has_bom_and_rows(self):
        _, _, data, n = exporter.build(self.movies(), {'columns': ['en', 'year'], 'keys': ['tt0000001', 'f2'], 'format': 'csv'})
        self.assertTrue(data.startswith(b'\xef\xbb\xbf'))
        self.assertEqual(n, 2)

    def test_one_row_per_file(self):
        m = self.movies()
        m[0]['files'].append({'id': 3, 'raw': 'Alpha.2000.720p', 'res': '720p', 'paths': []})
        _, _, _, n = exporter.build(m, {'columns': ['en', 'fname'], 'keys': ['tt0000001'], 'rows': 'file', 'format': 'csv'})
        self.assertEqual(n, 2)


class BackupAndExtras(TempLibrary):
    def test_backup_roundtrip(self):
        self.lib.set_user('tt0000001', {'fav': 1, 'notes': 'good', 'myrating': 9})
        self.lib.save_view('Mine', {'q': 'x'})
        self.lib.wish_add([{'title': 'Heat', 'year': 1995}, {'title': 'Heat', 'year': 1995}])
        self.lib.dismiss('dupes:tt0000001:1-2')
        snap = backup.snapshot(self.lib)
        self.lib.db.executescript('DELETE FROM user; DELETE FROM views; DELETE FROM wishlist; DELETE FROM dismissed;')
        n = backup.restore(self.lib, json.loads(json.dumps(snap)))
        self.assertEqual((n['user'], n['views'], n['wishlist'], n['dismissed']), (1, 1, 1, 1))
        self.assertEqual(self.lib.db.execute('SELECT notes FROM user').fetchone()[0], 'good')

    def test_rejects_other_files(self):
        with self.assertRaises(ValueError):
            backup.restore(self.lib, {'hello': 1})
        with self.assertRaises(ValueError):
            backup.load(self.lib, '../../secret.json')

    def test_automatic_backup_once_a_day(self):
        self.assertIsNone(backup.auto(self.lib))  # nothing personal yet
        self.lib.set_user('tt0000001', {'fav': 1})
        self.assertTrue(backup.auto(self.lib))
        self.assertIsNone(backup.auto(self.lib))
        self.assertEqual(len(backup.listing(self.lib)), 1)

    def test_safe_file_names(self):
        self.assertEqual(extras._clean('3:10 to Yuma'), '3.10 to Yuma')
        self.assertEqual(extras._clean('Alien: Covenant'), 'Alien - Covenant')
        self.assertEqual(extras._clean('Who? "What"*'), 'Who What')

    def test_rename_plan_marks_conflicts(self):
        m = [{'k': 'a', 'en': 'Alpha', 'title': 'Alpha', 'year': 2000,
              'files': [{'res': '1080p', 'src': 'BluRay', 'paths': ['D:/x/old1.mkv']}, {'res': '1080p', 'src': 'BluRay', 'paths': ['D:/x/old2.mkv']}]},
             {'k': 'b', 'en': 'Beta', 'title': 'Beta', 'year': 2001, 'files': [{'res': None, 'paths': ['D:/x/Beta (2001).mkv']}]}]
        rows = extras.rename_plan(m)
        self.assertEqual([r['conflict'] for r in rows], [True, True, False])
        self.assertTrue(rows[2]['same'])
        bat = extras.plan_bat(rows).decode()
        self.assertIn('rem SKIPPED', bat)
        self.assertNotIn('ren "D:/x/Beta', bat)

    def disk(self, *rel):
        """Make an empty file under a temp 'Movies' folder; returns its path."""
        p = os.path.join(self.root, 'Movies', *rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        open(p, 'wb').close()
        return p

    def movie(self, k, title, year, raw, paths, **f):
        return {'k': k, 'en': title, 'title': title, 'year': year,
                'files': [dict({'raw': raw, 'res': '1080p', 'src': 'BluRay', 'paths': paths}, **f)]}

    def test_rename_plan_folder_and_subtitles(self):
        roots = [os.path.join(self.root, 'Movies')]
        v = self.disk('Heat.1995.1080p.BluRay', 'movie.mkv')            # movie lives in a folder named by the list entry
        self.disk('Heat.1995.1080p.BluRay', 'movie.en.srt')
        self.disk('Heat.1995.1080p.BluRay', 'movie.nfo')
        rows = extras.rename_plan([self.movie('tt0113277', 'Heat', 1995, 'Heat.1995.1080p.BluRay', [v])], roots)
        by = {r['kind']: r for r in rows if r['kind'] != 'side'}
        sides = sorted(r['new'] for r in rows if r['kind'] == 'side')
        self.assertEqual(by['folder']['new'], 'Heat (1995) [1080p BluRay]')   # dots in the old name are not an extension
        self.assertEqual(by['file']['new'], 'Heat (1995) [1080p BluRay].mkv')
        self.assertEqual(sides, ['Heat (1995) [1080p BluRay].en.srt', 'Heat (1995) [1080p BluRay].nfo'])
        bat = extras.plan_bat(rows).decode().splitlines()
        ren = [l for l in bat if ' ren ' in l]
        self.assertEqual(len(ren), 4)
        self.assertIn(os.path.join(self.root, 'Movies', 'Heat.1995.1080p.BluRay'), ren[-1])  # the folder goes last
        self.assertEqual(extras.plan_list(rows).decode('utf-8-sig').strip().splitlines(), ['Heat (1995) [1080p BluRay]'])

    def test_rename_plan_leaves_shared_and_root_folders(self):
        roots = [os.path.join(self.root, 'Movies')]
        a = self.disk('Heat.1995.mkv')                                   # directly in the movie folder: no folder to rename
        b = self.disk('Pack', 'Alpha.2000.mkv')                          # one folder, two movies: it keeps its name
        c = self.disk('Pack', 'Beta.2001.mkv')
        rows = extras.rename_plan([self.movie('tt1', 'Heat', 1995, 'Heat.1995', [a]), self.movie('tt2', 'Alpha', 2000, 'Alpha.2000', [b]),
                                   self.movie('tt3', 'Beta', 2001, 'Beta.2001', [c])], roots)
        self.assertEqual([r['kind'] for r in rows], ['file'] * 3)

    def test_rename_plan_disc_folder_untouched(self):
        v = self.disk('Heat (1995)', 'VIDEO_TS', 'VTS_01_1.VOB')
        rows = extras.rename_plan([self.movie('tt1', 'Heat', 1995, 'Heat (1995)', [v])], [os.path.join(self.root, 'Movies')])
        self.assertEqual(rows, [])

    def test_rename_plan_never_overwrites(self):
        v = self.disk('Alpha.2000.mkv')
        self.disk('Alpha (2000) [1080p BluRay].mkv')                    # the new name is already there
        rows = extras.rename_plan([self.movie('tt1', 'Alpha', 2000, 'Alpha.2000', [v])], [os.path.join(self.root, 'Movies')])
        self.assertTrue(rows[0]['conflict'])
        self.assertIn('SKIPPED', extras.plan_bat(rows).decode())

    def test_rename_plan_percent_is_escaped(self):
        v = self.disk('Fifty.mkv')
        rows = extras.rename_plan([self.movie('tt1', '100% Wolf', 2020, 'Fifty', [v])], [os.path.join(self.root, 'Movies')])
        bat = extras.plan_bat(rows).decode()
        self.assertIn('100%% Wolf (2020)', bat)
        self.assertNotIn('100% Wolf', bat)

    def test_rename_pattern(self):
        v = self.disk('x.mkv')
        m = self.movie('tt0113277', 'Heat', 1995, 'x', [v], codec='x265')
        names = lambda pat: extras.rename_plan([m], [os.path.join(self.root, 'Movies')], pat)[0]['new']
        self.assertEqual(names('{title} - {year} - {res}'), 'Heat - 1995 - 1080p.mkv')
        self.assertEqual(names('{title} ({year}) [{edition}] {codec}'), 'Heat (1995) x265.mkv')   # empty field: its brackets go
        self.assertEqual(names('{imdb} {title}'), 'tt0113277 Heat.mkv')
        m['files'][0]['src'] = None                                    # the default pattern with one field empty
        self.assertEqual(names(extras.DEFAULT_PATTERN), 'Heat (1995) [1080p].mkv')
        m['files'][0].update(res=None)
        self.assertEqual(names(extras.DEFAULT_PATTERN), 'Heat (1995).mkv')
        m['files'][0].update(res='1080p', src='BluRay')
        self.assertEqual(extras.pattern_problem('{title} {nope}'), 'Unknown: {nope}. Use ' + ', '.join('{' + k + '}' for k in extras.PATTERN_FIELDS) + '.')
        self.assertTrue(extras.pattern_problem('{year}'))      # no title: every movie would get one name
        self.assertTrue(extras.pattern_problem(''))
        self.assertEqual(extras.pattern_problem(extras.DEFAULT_PATTERN), '')
        self.assertEqual(self.lib.settings()['rename_pattern'], extras.DEFAULT_PATTERN)


class FullBackup(TempLibrary):
    def prepare(self):
        from movieshelf import fullbackup
        self.fb = fullbackup
        self.lists = os.path.join(self.root, 'lists')
        os.makedirs(self.lists)
        with open(os.path.join(self.lists, 'a.txt'), 'w', encoding='utf-8') as f:
            f.write('Heat.1995.1080p\n')
        with open(os.path.join(self.lib.poster_dir, 'tt0000001.jpg'), 'wb') as f:
            f.write(b'JPEG')
        self.lib.save_settings({'list_dir': self.lists, 'tmdb_key': 'SECRET-T', 'omdb_key': 'SECRET-O'})
        self.title(1, 'Alpha', 2000)
        self.file('Alpha.2000.1080p', 1)
        self.lib.set_user('tt0000001', {'fav': 1, 'notes': 'kept'})
        self.zip = os.path.join(self.root, 'b.zip')

    def test_everything_comes_back(self):
        self.prepare()
        self.fb.save_full(self.lib, self.zip, {'lists': True, 'posters': True, 'keys': True})
        m = self.fb.inspect(self.zip)
        self.assertTrue(m['has_keys'])
        self.assertEqual((m['has']['lists'], m['has']['posters']), (1, 1))
        # lose everything
        self.lib.db.executescript('DELETE FROM files; DELETE FROM titles; DELETE FROM user;')
        self.lib.save_settings({'tmdb_key': '', 'omdb_key': ''})
        os.remove(os.path.join(self.lists, 'a.txt'))
        os.remove(os.path.join(self.lib.poster_dir, 'tt0000001.jpg'))
        res = self.fb.restore_full(self.lib, self.zip, {'settings': True, 'keys': True, 'lists': True, 'posters': True})
        self.assertEqual(res['done']['lists'], 1)
        self.assertEqual(self.lib.db.execute('SELECT count(*) FROM files').fetchone()[0], 1)
        self.assertEqual(self.lib.db.execute('SELECT notes FROM user').fetchone()[0], 'kept')
        self.assertEqual(self.lib.settings()['tmdb_key'], 'SECRET-T')
        self.assertTrue(os.path.exists(os.path.join(self.lists, 'a.txt')))
        self.assertTrue(os.path.exists(os.path.join(self.lib.poster_dir, 'tt0000001.jpg')))
        self.assertTrue(os.path.isdir(res['safety']))  # what existed before is kept

    def test_keys_can_be_left_out(self):
        self.prepare()
        self.fb.save_full(self.lib, self.zip, {'keys': False})
        self.assertFalse(self.fb.inspect(self.zip)['has_keys'])
        with zipfile.ZipFile(self.zip) as z:
            self.assertNotIn('SECRET', z.read('settings.json').decode())
            self.assertNotIn('lists/a.txt', z.namelist())
        self.lib.save_settings({'tmdb_key': 'NEW-KEY'})
        self.fb.restore_full(self.lib, self.zip, {'settings': True, 'keys': True})
        self.assertEqual(self.lib.settings()['tmdb_key'], 'NEW-KEY')  # an empty key in the backup never erases yours

    def test_bad_files_and_unsafe_names(self):
        self.prepare()
        notzip = os.path.join(self.root, 'x.zip')
        with open(notzip, 'wb') as f:
            f.write(b'nope')
        with self.assertRaises(ValueError):
            self.fb.inspect(notzip)
        self.assertFalse(self.fb._safe('../evil.txt'))
        self.assertFalse(self.fb._safe('C:/evil.txt'))
        self.assertFalse(self.fb._safe('/abs.txt'))
        self.assertTrue(self.fb._safe('posters/a.jpg'))

    def test_old_backup_gets_new_tables(self):
        self.prepare()
        self.fb.save_full(self.lib, self.zip, {})
        self.lib.db.executescript('DROP TABLE wishlist')
        self.fb.restore_full(self.lib, self.zip, {})
        self.assertEqual(self.lib.wish_add([{'title': 'Heat', 'year': 1995}]), 1)


class PersianTitles(TempLibrary):
    def test_normalize(self):
        from movieshelf.fatitles import normalize, ZW
        self.assertEqual(normalize('این یک دنیای دیوانه ، دیوانه ، دیوانه'), 'این یک دنیای دیوانه، دیوانه، دیوانه')
        self.assertEqual(normalize('سگ ها و گربه ها'), f'سگ{ZW}ها و گربه{ZW}ها')
        self.assertEqual(normalize('می خواهم'), f'می{ZW}خواهم')
        self.assertEqual(normalize('قاعده ی بازی'), f'قاعده{ZW}ی بازی')
        self.assertEqual(normalize('كتاب ي'), 'کتاب ی')            # Arabic letter forms
        self.assertEqual(normalize('شرک 2'), 'شرک ۲')                # Persian digits
        self.assertEqual(normalize('از آن سوی جهان:'), 'از آن سوی جهان')
        self.assertEqual(normalize('Spider-Man: Homecoming'), 'Spider-Man: Homecoming')  # not Persian: left alone
        self.assertEqual(normalize(normalize('سگ ها ، می رود ؟')), normalize('سگ ها ، می رود ؟'))  # stable

    def test_reviewed_list_is_complete_and_persian(self):
        from movieshelf import fatitles
        rev = fatitles.reviewed()
        self.assertGreaterEqual(len(rev), 350)
        self.assertEqual([t for t, v in rev.items() if not fatitles.has_persian(v) or fatitles.LATIN.search(v)], [])

    def test_apply_edit_and_problem_list(self):
        from movieshelf import fatitles
        self.title(439, 'The Great Train Robbery', 1903)
        self.title(2, 'Beta', 2001)
        self.lib.db.execute("UPDATE titles SET fa_title='Serghate Bozorge Ghatar' WHERE tconst=439")
        self.lib.db.execute("UPDATE titles SET fa_title='سگ ها' WHERE tconst=2")
        self.lib.db.execute("INSERT INTO titles(tconst, type, title, en_title, year, fa_title) VALUES (3, 'movie', 'Gamma', 'Gamma', 2002, 'Latin Only')")
        self.file('The.Great.Train.Robbery.1903', 439)
        self.file('Beta.2001.1080p', 2)
        self.file('Gamma.2002.1080p', 3)
        self.assertEqual(fatitles.apply(self.lib), (1, 1))
        self.assertEqual(fatitles.apply(self.lib), (0, 0))  # only once per version
        movies = {m['tt']: m for m in self.lib.movies()}
        self.assertEqual(movies['tt0000439']['fa_t'], 'سرقت بزرگ قطار')           # reviewed
        self.assertEqual(movies['tt0000002']['fa_t'], 'سگ‌ها')               # respelled
        self.assertEqual([r['title'] for r in issues.build(self.lib)['fatitle']], ['Gamma'])  # still Latin
        self.lib.set_fa_title(3, 'گاما')
        self.assertEqual({m['tt']: m for m in self.lib.movies()}['tt0000003']['fa_t'], 'گاما')
        self.assertEqual(issues.build(self.lib)['fatitle'], [])
        self.lib.set_fa_title(439, None)  # remove the edit: falls back to the stored title
        self.lib.db.execute('DELETE FROM fa_titles WHERE tconst=439')
        self.lib.set_fa_title(2, '')      # hide
        self.assertIsNone({m['tt']: m for m in self.lib.movies()}['tt0000002']['fa_t'])

    def test_edits_are_in_the_backup(self):
        self.lib.set_fa_title(5, 'پنج')
        snap = backup.snapshot(self.lib)
        self.assertEqual(snap['fa_titles'], [{'tconst': 5, 'title': 'پنج'}])
        self.lib.db.execute('DELETE FROM fa_titles')
        backup.restore(self.lib, snap)
        self.assertEqual(self.lib.db.execute('SELECT title FROM fa_titles WHERE tconst=5').fetchone()[0], 'پنج')


class PersonProfiles(TempLibrary):
    PERSON = {'id': 77, 'name': 'Ann Director', 'biography': 'Born in a town.', 'birthday': '1950-03-04', 'deathday': None,
              'place_of_birth': 'Paris, France', 'known_for_department': 'Directing', 'also_known_as': ['A. Director'],
              'profile_path': '/abc.jpg',
              'combined_credits': {
                  'cast': [{'media_type': 'movie', 'id': 1, 'title': 'Alpha', 'release_date': '2000-01-01', 'character': 'Bob'},
                           {'media_type': 'tv', 'id': 2, 'name': 'A Show', 'first_air_date': '2001-01-01', 'character': 'X'}],
                  'crew': [{'media_type': 'movie', 'id': 1, 'title': 'Alpha', 'release_date': '2000-01-01', 'job': 'Director', 'department': 'Directing'},
                           {'media_type': 'movie', 'id': 3, 'title': 'Gamma', 'release_date': '2010-05-05', 'job': 'Executive Producer', 'department': 'Production'},
                           {'media_type': 'movie', 'id': 4, 'title': 'Delta', 'release_date': '1999-05-05', 'job': 'Screenplay', 'department': 'Writing'},
                           {'media_type': 'movie', 'id': 5, 'title': 'Eps', 'release_date': '', 'job': 'Original Music Composer', 'department': 'Sound'}]}}

    def fake_tmdb(self):
        from movieshelf import online
        outer = self

        class Fake:
            calls = 0

            def __init__(self, key):
                pass

            def _get(self, path, **kw):
                Fake.calls += 1
                if path.startswith('/find/'):
                    return {'person_results': [{'id': 77}]}
                return dict(outer.PERSON)
            person = online.Tmdb.person
            _role = staticmethod(online.Tmdb._role)
            _credit = staticmethod(online.Tmdb._credit)
        return Fake

    def test_credits_are_grouped_and_movies_only(self):
        p = self.fake_tmdb()('k').person(5)
        roles = sorted((c['title'], c['role']) for c in p['credits'])
        self.assertEqual(roles, [('Alpha', 'Actor'), ('Alpha', 'Director'), ('Delta', 'Writer'), ('Eps', 'Composer'), ('Gamma', 'Producer')])
        self.assertEqual([c['year'] for c in p['credits']], [2010, 2000, 2000, 1999, None])  # newest first
        self.assertEqual((p.get('biography_fa'), p['place']), (None, 'Paris, France'))

    def test_profile_is_fetched_once_and_kept(self):
        from movieshelf import online
        fake = self.fake_tmdb()
        self.lib.save_settings({'tmdb_key': 'k'})
        self.lib.db.execute("INSERT INTO people(nconst, name, birth, death, prof) VALUES (5, 'Ann Director', 1950, NULL, 'director,writer')")
        self.lib.db.commit()
        orig_t, orig_b = online.Tmdb, online.get_bytes
        online.Tmdb, online.get_bytes = fake, lambda url, timeout=30: b'JPEG'
        try:
            a = self.lib.person_profile(5)
            calls = fake.calls
            b = self.lib.person_profile(5)
            self.assertEqual(fake.calls, calls)               # second time comes from the database
            self.lib.person_profile(5, refresh=True)
            self.assertGreater(fake.calls, calls)
        finally:
            online.Tmdb, online.get_bytes = orig_t, orig_b
        self.assertTrue(a['photo'] and b['photo'])
        self.assertTrue(os.path.exists(self.lib.person_photo_path(5)))
        self.assertEqual((a['birth'], a['prof'], a['profile']['name']), (1950, ['director', 'writer'], 'Ann Director'))

    def test_scan_people_by_scope_and_skip_known(self):
        from movieshelf import online
        fake = self.fake_tmdb()
        self.lib.save_settings({'tmdb_key': 'k'})
        self.title(1, 'Alpha', 2000)
        self.lib.db.execute("UPDATE titles SET directors='[[10,\"Dir\"]]', writers='[[11,\"Wri\"]]', "
                            "cast_='[[20,\"A1\",\"x\"],[21,\"A2\",\"y\"],[22,\"A3\",\"\"],[23,\"A4\",\"\"],[24,\"A5\",\"\"],[25,\"A6\",\"\"]]', "
                            "crew='[[30,\"Prod\",\"producer\"]]' WHERE tconst=1")
        self.file('Alpha.2000.1080p', 1)
        self.assertEqual(self.lib.people_to_scan('directors'), [10, 11])
        self.assertEqual(self.lib.people_to_scan('main'), [10, 11, 20, 21, 22, 23, 24])   # first five actors only
        self.assertEqual(len(self.lib.people_to_scan('all')), 9)
        orig_t, orig_b = online.Tmdb, online.get_bytes
        online.Tmdb, online.get_bytes = fake, lambda url, timeout=30: b'JPEG'
        try:
            self.lib.fetch_people('directors')
            self.assertEqual(self.lib.people_to_scan('directors'), [])                    # already fetched: skipped
            self.assertEqual(self.lib.people_status()['fetched'], 2)
            self.assertEqual(self.lib.people_to_scan('main'), [20, 21, 22, 23, 24])
        finally:
            online.Tmdb, online.get_bytes = orig_t, orig_b

    def test_scan_people_needs_a_key(self):
        with self.assertRaises(RuntimeError):
            self.lib.fetch_people('main')

    def test_without_a_key_the_imdb_facts_still_show(self):
        self.lib.db.execute("INSERT INTO people(nconst, name, birth, death, prof) VALUES (6, 'Old Actor', 1900, 1970, 'actor')")
        self.lib.db.commit()
        p = self.lib.person_profile(6)
        self.assertEqual((p['name'], p['birth'], p['death'], p['tmdb'], p['profile']), ('Old Actor', 1900, 1970, False, None))

    def test_birth_years_are_read_from_name_basics(self):
        import gzip
        from movieshelf import imdb_index
        d = os.path.join(self.root, 'imdb')
        os.makedirs(d)
        with gzip.open(os.path.join(d, 'name.basics.tsv.gz'), 'wt', encoding='utf-8') as f:
            f.write('nconst\tprimaryName\tbirthYear\tdeathYear\tprimaryProfession\tknownForTitles\n'
                    'nm0000005\tAnn Director\t1950\t\\N\tdirector,writer\ttt1\n'
                    'nm0000006\tOld Actor\t1900\t1970\tactor,miscellaneous\ttt2\n')
        out = imdb_index.read_basics(d, [5, 6, 7])
        self.assertEqual(out, {5: (1950, None, 'director,writer'), 6: (1900, 1970, 'actor,miscellaneous')})


class Collections(TempLibrary):
    def test_missing_parts_are_found_by_title_and_year(self):
        self.title(1, 'Alpha', 2000)
        self.title(2, 'Alpha: Reloaded', 2003)
        self.lib.db.execute("UPDATE titles SET collection='Alpha Collection'")
        self.file('Alpha.2000.1080p', 1)
        self.file('Alpha.Reloaded.2003.1080p', 2)
        parts = [{'title': 'Alpha', 'orig': None, 'year': 2000}, {'title': 'Alpha: Reloaded', 'orig': None, 'year': 2004},
                 {'title': 'Alpha Returns', 'orig': None, 'year': 2008}]
        self.lib.db.execute('INSERT INTO collections VALUES (?,?,?)', ('Alpha Collection', 9, json.dumps(parts)))
        self.lib.db.commit()
        c = self.lib.collections()[0]
        self.assertEqual(len(c['have']), 2)
        self.assertEqual([m['title'] for m in c['missing']], ['Alpha Returns'])  # 2004 vs 2003 still counts as owned
        self.assertEqual(c['total'], 3)

    def test_unchecked_collection_has_no_missing_list(self):
        self.title(1, 'Alpha', 2000)
        self.lib.db.execute("UPDATE titles SET collection='Alpha Collection'")
        self.file('Alpha.2000.1080p', 1)
        c = self.lib.collections()[0]
        self.assertFalse(c['fetched'])
        self.assertEqual(c['missing'], [])


class WatchedAndBulk(TempLibrary):
    def test_bulk_update_and_watched_date(self):
        self.lib.set_user_many(['tt0000001', 'tt0000002', 'f9'], {'watched': 1, 'myrating': 8})
        rows = {r['mkey']: r for r in self.lib.db.execute('SELECT * FROM user')}
        self.assertEqual(len(rows), 3)
        self.assertTrue(all(r['watched'] == 1 and r['myrating'] == 8 and r['watched_at'] for r in rows.values()))
        first = rows['tt0000001']['watched_at']
        self.lib.set_user_many(['tt0000001'], {'watched': 1})  # watching again keeps the first date
        self.assertEqual(self.lib.db.execute("SELECT watched_at FROM user WHERE mkey='tt0000001'").fetchone()[0], first)
        self.lib.set_user_many(['tt0000001'], {'watched': 0})
        self.assertIsNone(self.lib.db.execute("SELECT watched_at FROM user WHERE mkey='tt0000001'").fetchone()[0])

    def test_unknown_fields_are_ignored(self):
        self.assertEqual(self.lib.set_user_many(['tt0000001'], {'evil': 1}), 0)


class MediaFacts(TempLibrary):
    PROBE = {'streams': [
        {'codec_type': 'video', 'codec_name': 'h264', 'width': 1920, 'height': 800},
        {'codec_type': 'video', 'codec_name': 'mjpeg', 'width': 600, 'height': 900, 'disposition': {'attached_pic': 1}},
        {'codec_type': 'audio', 'tags': {'language': 'eng'}}, {'codec_type': 'audio', 'tags': {'language': 'fas'}},
        {'codec_type': 'audio', 'tags': {'language': 'und'}}, {'codec_type': 'subtitle', 'tags': {'language': 'fre'}}],
        'format': {'duration': '8160.5', 'bit_rate': '9000000'}}

    def test_parse_probe(self):
        from movieshelf import probe
        p = probe.parse_probe(self.PROBE)
        self.assertEqual((p['duration'], p['width'], p['height'], p['vcodec']), (8160, 1920, 800, 'h264'))  # the cover art is not the picture
        self.assertEqual((p['audio'], p['subs'], p['n_audio']), (['English', 'Persian'], ['French'], 3))
        self.assertEqual(probe.real_height_class(1920, 800), '1080p')  # cropped wide films are still 1080p
        self.assertEqual(probe.real_height_class(1280, 720), '720p')

    def test_language_lists_do_not_replace_the_channel_layout(self):
        self.title(1, 'Alpha', 2000)
        self.file('Alpha.2000.1080p.BluRay.5.1', 1)
        self.lib.db.execute("INSERT INTO fileinfo(path, raw, size, mtime) VALUES ('D:/a.mkv', 'Alpha.2000.1080p.BluRay.5.1', 1, 0)")
        self.lib.db.execute("INSERT INTO mediainfo(path, size, mtime, ok, duration, width, height, audio, subs) "
                            "VALUES ('D:/a.mkv', 1, 0, 1, 100, 1920, 800, '[\"English\"]', '[\"French\"]')")
        f = self.lib.movies()[0]['files'][0]
        self.assertEqual((f['audio'], f['alang'], f['slang']), ('5.1', ['English'], ['French']))  # 'audio' is the channel layout

    def test_picture_and_length_problems(self):
        self.title(1, 'Long', 2000)
        self.lib.db.execute('UPDATE titles SET runtime=140 WHERE tconst=1')
        self.file('Long.2000.1080p', 1)
        self.title(2, 'Fine', 2001)
        self.lib.db.execute('UPDATE titles SET runtime=100 WHERE tconst=2')
        self.file('Fine.2001.720p', 2)
        self.lib.db.executemany('INSERT INTO fileinfo(path, raw, size, mtime) VALUES (?,?,?,0)',
                                [('D:/Long.mkv', 'Long.2000.1080p', 1), ('D:/Fine.mkv', 'Fine.2001.720p', 1)])
        self.lib.db.executemany('INSERT INTO mediainfo(path, size, mtime, ok, duration, width, height) VALUES (?,?,0,1,?,?,?)',
                                [('D:/Long.mkv', 1, 1800, 1280, 720), ('D:/Fine.mkv', 1, 6000, 1280, 720)])
        out = issues.build(self.lib)
        self.assertEqual([r['title'] for r in out['realq']], ['Long'])  # named 1080p, picture 1280x720
        self.assertEqual([r['title'] for r in out['runtime']], ['Long'])  # 30 min against 140

    def test_folder_scan_lists_the_videos_it_finds(self):
        self.file('Listed.2000.1080p', None)
        movies = os.path.join(self.root, 'movies')
        os.makedirs(os.path.join(movies, 'Heat (1995) 1080p'))
        for rel, size in (('Listed.2000.1080p.mkv', 10), ('Unlisted.2010.720p.mkv', 31_000_000), ('Tiny.sample.mkv', 100),
                          ('Show.S01E02.mkv', 40_000_000), ('Heat (1995) 1080p/movie.mkv', 31_000_000), ('notes.txt', 5)):
            with open(os.path.join(movies, rel), 'wb') as f:
                f.write(b'\0' * size)
        lists = os.path.join(self.root, 'lists')
        os.makedirs(lists)
        open(os.path.join(lists, 'a.txt'), 'w').close()
        s = self.lib.settings(); s['media_dirs'] = [movies]; s['list_dir'] = lists; self.lib.save_settings(s)
        self.lib.scan_media()
        self.assertEqual(sorted(r[0] for r in self.lib.db.execute('SELECT raw FROM fileinfo')),
                         ['Heat (1995) 1080p', 'Listed.2000.1080p', 'Unlisted.2010.720p'])  # each has a path, for Show in folder
        from movieshelf.library import FOLDER_SRC
        taken = sorted(r[0] for r in self.lib.db.execute('SELECT raw FROM files WHERE list_file=?', (FOLDER_SRC,)))
        self.assertEqual(taken, ['Heat (1995) 1080p', 'Unlisted.2010.720p'])  # no sample, episode, tiny file or non-video
        self.assertEqual(self.lib.unlisted_items(), [])


class JunkFoldersAndPosters(TempLibrary):
    PNG = b'\x89PNG\r\n\x1a\n' + b'\0' * 32
    JPG = b'\xff\xd8\xff\xe0' + b'\0' * 32

    def make(self, movies, rel, size=5):
        path = os.path.join(movies, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'wb') as f:
            f.truncate(size)

    def scan_folders(self):
        movies = os.path.join(self.root, 'movies')
        for raw in ('Junk.2001.1080p', 'Empty.2002.720p', 'Good.2003.1080p', 'Nested.2004.720p', 'SampleOnly.2005.720p'):
            self.file(raw, None)
        os.makedirs(os.path.join(movies, 'Empty.2002.720p'))
        self.make(movies, 'Junk.2001.1080p/Junk.2001.1080p.nfo')
        self.make(movies, 'Junk.2001.1080p/poster.jpg')
        self.make(movies, 'Junk.2001.1080p/RARBG.url')
        self.make(movies, 'Good.2003.1080p/Good.2003.1080p.mkv', 31_000_000)
        self.make(movies, 'Good.2003.1080p/Good.2003.1080p.nfo')
        self.make(movies, 'Nested.2004.720p/CD1/movie.avi', 31_000_000)
        self.make(movies, 'SampleOnly.2005.720p/Sample.mkv', 100)
        s = self.lib.settings(); s['media_dirs'] = [movies]; self.lib.save_settings(s)
        self.lib.scan_media()
        return movies

    def test_folders_without_video_are_reported(self):
        self.scan_folders()
        rows = issues.build(self.lib)['novideo']
        self.assertEqual(sorted(r['title'] for r in rows), ['Empty', 'Junk', 'SampleOnly'])  # not Good, not Nested
        by = {r['title']: r for r in rows}
        self.assertIn('empty', by['Empty']['note'])
        self.assertIn('Junk.2001.1080p.nfo', by['Junk']['note'])
        self.assertEqual(by['Junk']['junk'][0]['nfiles'], 3)
        self.assertEqual(len(by['Junk']['junk_ids']), 1)
        self.assertEqual(issues.open_novideo(self.lib), 3)
        self.assertEqual(self.lib.stats()['novideo'], 3)

    def test_a_video_elsewhere_clears_the_report(self):
        movies = self.scan_folders()
        self.make(movies, 'Other disk/Junk.2001.1080p.mkv', 31_000_000)  # the movie is in another folder
        self.lib.scan_media()
        self.assertEqual(sorted(r['title'] for r in issues.build(self.lib)['novideo']), ['Empty', 'SampleOnly'])

    def test_remove_from_list_survives_a_rescan_and_can_be_undone(self):
        lists = os.path.join(self.root, 'lists')
        os.makedirs(lists)
        with open(os.path.join(lists, 'a.txt'), 'w', encoding='utf-8') as f:
            f.write('Junk.2001.1080p\nGood.2003.1080p\n')
        s = self.lib.settings(); s['list_dir'] = lists; self.lib.save_settings(s)
        self.lib.scan()
        fid = self.lib.db.execute("SELECT id FROM files WHERE raw='Junk.2001.1080p'").fetchone()[0]
        v = self.lib.version
        self.assertEqual(self.lib.remove_from_list([fid, fid, 99999]), 1)
        self.assertGreater(self.lib.version, v)
        self.assertEqual([m['ptitle'] for m in self.lib.movies()], ['Good'])
        self.assertEqual(self.lib.remove_from_list([fid]), 0)  # already removed
        self.lib.scan()  # the line is still in the list file, but the entry stays out
        self.assertEqual([m['ptitle'] for m in self.lib.movies()], ['Good'])
        self.assertEqual([r['raw'] for r in self.lib.removed_items()], ['Junk.2001.1080p'])
        with open(os.path.join(lists, 'a.txt'), encoding='utf-8') as f:
            self.assertIn('Junk.2001.1080p', f.read())  # the user's list file is untouched
        self.assertEqual(self.lib.restore_removed(['Junk.2001.1080p']), 1)
        self.lib.scan()
        self.assertEqual(sorted(m['ptitle'] for m in self.lib.movies()), ['Good', 'Junk'])
        self.assertEqual(self.lib.removed_items(), [])

    def test_removed_entry_is_not_offered_as_on_disk(self):
        movies = os.path.join(self.root, 'movies')
        self.file('Gone.2001.1080p', None)
        self.make(movies, 'Gone.2001.1080p.mkv', 31_000_000)
        s = self.lib.settings(); s['media_dirs'] = [movies]; self.lib.save_settings(s)
        self.lib.remove_from_list([self.lib.db.execute('SELECT id FROM files').fetchone()[0]])
        self.lib.scan_media()
        self.assertEqual(self.lib.unlisted_items(), [])

    def test_adding_a_removed_name_again_brings_it_back(self):
        lists = os.path.join(self.root, 'lists')
        os.makedirs(lists)
        s = self.lib.settings(); s['list_dir'] = lists; self.lib.save_settings(s)
        self.file('Back.2001.1080p', None)
        self.lib.remove_from_list([self.lib.db.execute('SELECT id FROM files').fetchone()[0]])
        self.lib.start = lambda *a, **k: True  # no update job in a test
        self.assertEqual(self.lib.add_from_check([{'raw': 'Back.2001.1080p'}]), 1)
        self.assertEqual(self.lib.removed_items(), [])

    def test_poster_added_by_hand(self):
        self.title(1, 'Alpha', 2000, poster=None)
        self.file('Alpha.2000.1080p', 1)
        self.assertEqual([r['title'] for r in issues.build(self.lib)['noposter']], ['Alpha'])
        with self.assertRaises(ValueError):
            self.lib.set_poster(1, b'<html>not an image</html>')
        with self.assertRaises(ValueError):
            self.lib.set_poster(2, self.PNG)  # no such title
        self.lib.set_poster(1, self.PNG)
        self.assertEqual(open(self.lib.poster_path(1), 'rb').read(), self.PNG)
        self.assertEqual(issues.build(self.lib)['noposter'], [])
        self.assertTrue(self.lib.movies()[0]['poster'])
        self.lib.clear_poster(1)
        self.assertFalse(os.path.exists(self.lib.poster_path(1)))
        self.assertEqual([r['title'] for r in issues.build(self.lib)['noposter']], ['Alpha'])

    def test_poster_from_a_file_path(self):
        self.title(1, 'Alpha', 2000, poster=None)
        pic = os.path.join(self.root, 'cover.jpg')
        with open(pic, 'wb') as f:
            f.write(self.JPG)
        self.lib.set_poster(1, self.lib.poster_bytes('"' + pic + '"'))
        self.assertEqual(open(self.lib.poster_path(1), 'rb').read(), self.JPG)
        notes = os.path.join(self.root, 'notes.txt')
        with open(notes, 'w') as f:
            f.write('hello')
        for bad in ('', notes, os.path.join(self.root, 'missing.png')):
            with self.assertRaises(ValueError):
                self.lib.poster_bytes(bad)

    def test_rescan_picks_up_new_list_lines_and_new_folders(self):
        lists = os.path.join(self.root, 'lists')
        os.makedirs(lists)
        listfile = os.path.join(lists, 'a.txt')
        with open(listfile, 'w', encoding='utf-8') as f:
            f.write('Good.2003.1080p\n')
        movies = os.path.join(self.root, 'movies')
        s = self.lib.settings(); s['list_dir'] = lists; s['media_dirs'] = [movies]; self.lib.save_settings(s)
        os.makedirs(movies)
        self.lib.match_all = lambda *a, **k: None  # no IMDb data in a test
        self.lib.start = lambda *a, **k: True  # the cast and poster job is not needed
        self.lib.rescan()
        self.assertEqual(issues.build(self.lib)['novideo'], [])
        with open(listfile, 'a', encoding='utf-8') as f:
            f.write('Junk.2001.1080p\n')  # a new line in the list ...
        self.make(movies, 'Junk.2001.1080p/RARBG.url')  # ... whose folder holds only junk
        self.lib.rescan()
        self.assertEqual([r['title'] for r in issues.build(self.lib)['novideo']], ['Junk'])
        self.make(movies, 'Junk.2001.1080p/Junk.2001.1080p.mkv', 31_000_000)  # the movie arrives
        self.lib.rescan()
        self.assertEqual(issues.build(self.lib)['novideo'], [])

    def test_update_is_queued_while_a_job_runs(self):
        from movieshelf import server
        import urllib.request
        web = os.path.join(self.root, 'web')
        os.makedirs(web)
        httpd = server.serve(self.lib, web)
        try:
            self.lib.job['running'] = True
            r = urllib.request.urlopen(urllib.request.Request(f'http://127.0.0.1:{httpd.server_address[1]}/api/rescan', b'{}', {'Content-Type': 'application/json'}))
            self.assertEqual(json.loads(r.read()), {'started': False, 'stopping': False})
            self.assertEqual(len(self.lib.queue), 1)
        finally:
            self.lib.job['running'] = False
            self.lib.queue.clear()
            httpd.shutdown()
            httpd.server_close()

    def test_episodes_are_not_duplicates(self):
        self.title(1, 'Show', 2010, poster='x')
        self.file('Show.S01E01.720p', 1)
        self.file('Show.S01E02.720p', 1)
        out = issues.build(self.lib)
        self.assertEqual([(r['sub'], r['wasted']) for r in out['dupes']], [('series', 0)])
        self.assertIn('episode numbers', out['dupes'][0]['note'])
        self.assertEqual(issues.open_duplicates(self.lib), 0)  # the header badge ignores series
        self.lib.version += 1
        # the user says it is not a series: the old duplicate check applies again
        self.lib.dismiss('noseries:tt0000001')
        self.assertEqual([r['sub'] for r in issues.build(self.lib)['dupes']], ['mixed'])

    def test_a_title_marked_as_a_series_leaves_the_duplicates(self):
        self.title(2, 'Odd', 2011, poster='x')
        self.file('Odd.2011.720p', 2)
        self.file('Odd.2011.1080p', 2)
        self.assertEqual([r['sub'] for r in issues.build(self.lib)['dupes']], ['lower'])
        self.lib.dismiss('series:tt0000002')
        rows = issues.build(self.lib)['dupes']
        self.assertEqual([(r['sub'], r['note']) for r in rows], [('series', 'You marked this as a series')])
        self.lib.dismiss('series:tt0000002', undo=True)
        self.assertEqual([r['sub'] for r in issues.build(self.lib)['dupes']], ['lower'])

    def test_a_tv_series_title_with_several_files_is_a_series(self):
        self.title(3, 'Saga', 2005, type_='tvSeries', poster='x')
        self.file('Saga.Part.One.720p', 3)
        self.file('Saga.Part.Two.720p', 3)
        self.assertEqual([r['sub'] for r in issues.build(self.lib)['dupes']], ['series'])

    def test_subtitle_lines_are_not_movies(self):
        from movieshelf import checker
        lists = os.path.join(self.root, 'lists')
        os.makedirs(lists)
        with open(os.path.join(lists, 'a.txt'), 'w', encoding='utf-8') as f:
            f.write('Heat.1995.1080p.mkv\nHeat.1995.1080p.srt\nHeat.1995.1080p.nfo\nAlien.1979.720p\n')
        s = self.lib.settings(); s['list_dir'] = lists; self.lib.save_settings(s)
        self.lib.scan()
        self.assertEqual(sorted(r[0] for r in self.lib.db.execute('SELECT raw FROM files WHERE present=1')), ['Alien.1979.720p', 'Heat.1995.1080p.mkv'])
        self.assertEqual([e['raw'] for e in checker.entries_from_text('Heat.1995.1080p.srt\nAlien.1979.720p\n')], ['Alien.1979.720p'])
        self.assertEqual([e['raw'] for e in checker.entries_from_text('name\nHeat.1995.srt\nAlien.1979\n', 'x.csv')], ['Alien.1979'])
        self.assertFalse(checker.is_side_file('Dr.No.1962.1080p'))
        self.assertFalse(checker.is_side_file('April.2025.1080p.WEB-DL.Farsi.Sub'))  # a release name, not a subtitle file

    def test_edited_list_is_read_again_in_any_encoding(self):
        lists = os.path.join(self.root, 'lists')
        os.makedirs(lists)
        listfile = os.path.join(lists, 'a.txt')
        s = self.lib.settings(); s['list_dir'] = lists; self.lib.save_settings(s)
        present = lambda: sorted(r[0] for r in self.lib.db.execute('SELECT raw FROM files WHERE present=1'))
        with open(listfile, 'w', encoding='utf-16') as f:  # Notepad: Save as Unicode
            f.write('Heat.1995.1080p\nAlien.1979.720p\n')
        self.lib.scan()
        self.assertEqual(present(), ['Alien.1979.720p', 'Heat.1995.1080p'])
        with open(listfile, 'w', encoding='utf-8') as f:  # one removed, one added
            f.write('Heat.1995.1080p\nRonin.1998.1080p\n')
        self.lib.scan()
        self.assertEqual(present(), ['Heat.1995.1080p', 'Ronin.1998.1080p'])

    def test_show_in_folder_accepts_a_video_in_no_list(self):
        from movieshelf import extras
        movies = os.path.join(self.root, 'movies')
        self.make(movies, 'Loose.2010.1080p/Loose.2010.1080p.mkv', 31_000_000)
        lists = os.path.join(self.root, 'lists')
        os.makedirs(lists)
        open(os.path.join(lists, 'a.txt'), 'w').close()
        s = self.lib.settings(); s['media_dirs'] = [movies]; s['list_dir'] = lists; self.lib.save_settings(s)
        self.lib.scan_media()
        path = self.lib.db.execute('SELECT path FROM fileinfo').fetchone()[0]
        shown = []
        import subprocess
        real = subprocess.Popen
        subprocess.Popen = lambda cmd, *a, **k: shown.append(cmd)
        try:
            self.assertTrue(extras.reveal(self.lib, path))
        finally:
            subprocess.Popen = real
        self.assertIn(os.path.normpath(path), shown[0])
        with self.assertRaises(FileNotFoundError):
            extras.reveal(self.lib, os.path.join(movies, 'other.mkv'))

    def test_movies_in_the_folder_are_listed_by_themselves(self):
        from movieshelf.library import FOLDER_SRC
        lists = os.path.join(self.root, 'lists')
        movies = os.path.join(self.root, 'movies')
        os.makedirs(lists)
        with open(os.path.join(lists, 'a.txt'), 'w', encoding='utf-8') as f:
            f.write('Heat.1995.1080p\n')
        names = ['April.2025.1080p.WEB-DL.Farsi.Sub', 'Audition.1999.720p.YIFY.Farsi.Sub']
        for n in names:
            self.make(movies, f'{n}/{n}.mkv', 31_000_000)
        s = self.lib.settings(); s['list_dir'] = lists; s['media_dirs'] = [movies]; self.lib.save_settings(s)
        self.lib.start = lambda *a, **k: True
        self.lib.scan(); self.lib.scan_media()
        present = lambda: sorted(r[0] for r in self.lib.db.execute('SELECT raw FROM files WHERE present=1'))
        self.assertEqual(present(), sorted(['Heat.1995.1080p'] + names))
        self.assertEqual({r[0] for r in self.lib.db.execute('SELECT list_file FROM files WHERE raw=?', (names[0],))}, {FOLDER_SRC})
        self.lib.scan(); self.lib.scan_media()  # a second update changes nothing
        self.assertEqual(present(), sorted(['Heat.1995.1080p'] + names))
        self.assertEqual(self.lib.unlisted_items(), [])

    def test_movies_of_a_folder_that_is_away_stay(self):
        movies = os.path.join(self.root, 'movies')
        lists = os.path.join(self.root, 'lists')
        os.makedirs(lists)
        open(os.path.join(lists, 'a.txt'), 'w').close()
        self.make(movies, 'Ronin.1998.1080p.mkv', 31_000_000)
        s = self.lib.settings(); s['list_dir'] = lists; s['media_dirs'] = [movies]; self.lib.save_settings(s)
        self.lib.scan(); self.lib.scan_media()
        present = lambda: self.lib.db.execute("SELECT present FROM files WHERE raw='Ronin.1998.1080p'").fetchone()[0]
        self.assertEqual(present(), 1)
        shutil.move(movies, movies + '-away')  # the drive is not there: not the same as the movie being deleted
        self.lib.scan(); self.lib.scan_media()
        self.assertEqual(present(), 1)

    def test_a_list_and_a_folder_together(self):
        movies = os.path.join(self.root, 'movies')
        lists = os.path.join(self.root, 'lists')
        os.makedirs(lists)
        with open(os.path.join(lists, 'a.txt'), 'w', encoding='utf-8') as f:
            f.write('Heat.1995.1080p\n')
        self.make(movies, 'Heat.1995.1080p.mkv', 31_000_000)
        self.make(movies, 'Ronin.1998.1080p.mkv', 31_000_000)
        s = self.lib.settings(); s['list_dir'] = lists; s['media_dirs'] = [movies]; self.lib.save_settings(s)
        present = lambda: sorted(r[0] for r in self.lib.db.execute('SELECT raw FROM files WHERE present=1'))
        self.lib.scan(); self.lib.scan_media()
        self.assertEqual(present(), ['Heat.1995.1080p', 'Ronin.1998.1080p'])
        with open(os.path.join(lists, 'a.txt'), 'a', encoding='utf-8') as f:
            f.write('Ronin.1998.1080p\n')  # later also put in a list: the list owns it now
        self.lib.scan(); self.lib.scan_media()
        self.assertEqual(self.lib.db.execute("SELECT list_file FROM files WHERE raw='Ronin.1998.1080p'").fetchone()[0], 'a.txt')
        self.assertEqual(present(), ['Heat.1995.1080p', 'Ronin.1998.1080p'])

    def test_movie_folders_can_be_the_only_source(self):
        movies = os.path.join(self.root, 'movies')
        lists = os.path.join(self.root, 'nolists')
        os.makedirs(lists)
        self.make(movies, 'Heat.1995.1080p/Heat.1995.1080p.mkv', 31_000_000)
        self.make(movies, 'Alien.1979.720p.mkv', 31_000_000)
        self.make(movies, 'Alien.1979.720p.srt')
        self.make(movies, 'Sample/sample.mkv', 1000)
        s = self.lib.settings(); s['list_dir'] = lists; s['media_dirs'] = [movies]; self.lib.save_settings(s)
        self.assertTrue(self.lib.use_folders())
        self.lib.scan()  # no list: not an error, the folders are the source
        self.lib.scan_media()
        raws = lambda: sorted(r[0] for r in self.lib.db.execute('SELECT raw FROM files WHERE present=1'))
        self.assertEqual(raws(), ['Alien.1979.720p', 'Heat.1995.1080p'])
        self.assertEqual(self.lib.unlisted_items(), [])
        self.assertEqual({r[0] for r in self.lib.db.execute('SELECT list_file FROM files')}, {'(movie folders)'})
        self.lib.scan(); self.lib.scan_media()  # nothing doubles up on the next run
        self.assertEqual(raws(), ['Alien.1979.720p', 'Heat.1995.1080p'])
        os.remove(os.path.join(movies, 'Alien.1979.720p.mkv'))  # the video is gone, so is the movie
        self.lib.scan(); self.lib.scan_media()
        self.assertEqual(raws(), ['Heat.1995.1080p'])
        fid = self.lib.db.execute("SELECT id FROM files WHERE raw='Heat.1995.1080p'").fetchone()[0]
        self.lib.remove_from_list([fid])  # a movie from a folder can be removed too, and stays out
        self.lib.scan(); self.lib.scan_media()
        self.assertEqual(raws(), [])
        self.assertEqual(self.lib.unlisted_items(), [])

    def test_no_list_and_no_folder_says_so(self):
        s = self.lib.settings(); s['list_dir'] = os.path.join(self.root, 'empty'); self.lib.save_settings(s)
        os.makedirs(s['list_dir'])
        with self.assertRaisesRegex(RuntimeError, 'No .txt list found'):
            self.lib.scan()

    def test_first_run_with_only_a_movie_folder(self):
        movies = os.path.join(self.root, 'movies')
        os.makedirs(movies)
        empty = os.path.join(self.root, 'lists')
        os.makedirs(empty)
        self.lib.start = lambda *a, **k: True
        with self.assertRaises(ValueError):
            self.lib.start_fresh(None, None)  # no list and no movie folder: nothing to build from
        with self.assertRaises(ValueError):
            self.lib.start_fresh(empty, os.path.join(self.root, 'nope'))  # not a folder
        self.assertTrue(self.lib.start_fresh(None, movies))
        self.assertEqual(self.lib.settings()['media_dirs'], [movies])

    def test_poster_over_http(self):
        import base64
        import urllib.error
        import urllib.request
        from movieshelf import server
        self.title(1, 'Alpha', 2000, poster=None)
        web = os.path.join(self.root, 'web')
        os.makedirs(web)
        httpd = server.serve(self.lib, web)
        base = f'http://127.0.0.1:{httpd.server_address[1]}'
        post = lambda path, body: json.loads(urllib.request.urlopen(urllib.request.Request(
            base + path, json.dumps(body).encode(), {'Content-Type': 'application/json'})).read())
        try:
            self.assertEqual(post('/api/poster/1', {'data': 'data:image/png;base64,' + base64.b64encode(self.PNG).decode()}), {'ok': True})
            r = urllib.request.urlopen(base + '/poster/tt0000001.jpg')
            self.assertEqual((r.headers['Content-Type'], r.read()), ('image/png', self.PNG))
            etag = r.headers['ETag']
            req = urllib.request.Request(base + '/poster/tt0000001.jpg', headers={'If-None-Match': etag})
            with self.assertRaises(urllib.error.HTTPError) as cm:
                urllib.request.urlopen(req)
            self.assertEqual(cm.exception.code, 304)
            self.assertIn('error', post('/api/poster/1', {'data': 'data:text/plain;base64,aGVsbG8='}))
            self.assertIn('error', post('/api/poster/1', {'source': 'C:/no/such/file.png'}))
            self.assertEqual(post('/api/poster/1', {'clear': True}), {'ok': True})
            with self.assertRaises(urllib.error.HTTPError):
                urllib.request.urlopen(base + '/poster/tt0000001.jpg')
            self.file('Alpha.2000.1080p', 1)
            ids = [self.lib.db.execute('SELECT id FROM files').fetchone()[0]]
            self.assertEqual(post('/api/remove', {'ids': ids}), {'removed': 1})
            self.assertEqual([r['raw'] for r in json.loads(urllib.request.urlopen(base + '/api/removed').read())], ['Alpha.2000.1080p'])
            self.assertEqual(post('/api/removed/restore', {'raws': ['Alpha.2000.1080p']}), {'restored': 1})
        finally:
            httpd.shutdown()
            httpd.server_close()


class FirstRun(TempLibrary):
    def wait(self):
        for _ in range(200):
            if not self.lib.job['running']:
                return
            time.sleep(0.01)

    def test_nothing_runs_before_a_start_is_chosen(self):
        self.assertTrue(self.lib.first_run())
        lists = os.path.join(self.root, 'lists')
        os.makedirs(lists)
        with self.assertRaises(ValueError):  # no .txt in the folder: nothing is saved, nothing starts
            self.lib.start_fresh(lists)
        self.assertTrue(self.lib.first_run())
        self.assertFalse(self.lib.job['running'])

    def test_start_fresh_loads_the_list_and_builds(self):
        src = os.path.join(self.root, 'mine')
        os.makedirs(src)
        with open(os.path.join(src, 'a.txt'), 'w') as f:
            f.write('Heat.1995.1080p\n')
        calls = []
        self.lib.full_update = lambda: calls.append(1)
        self.assertTrue(self.lib.start_fresh(os.path.join(src, 'a.txt')))
        self.wait()
        self.assertEqual(calls, [1])
        self.assertFalse(self.lib.first_run())
        self.assertEqual(open(os.path.join(self.lib.settings()['list_dir'], 'a.txt'), encoding='utf-8').read(), 'Heat.1995.1080p\n')


class LoadList(TempLibrary):
    """The Load list button: preview the duplicates, then add to the current list or start a new one."""
    wait = FirstRun.wait

    def setUp(self):
        super().setUp()
        self.lists = os.path.join(self.root, 'lists')
        self.write(self.lists, 'a.txt', 'Heat.1995.1080p\n')
        self.lib.save_settings({'list_dir': self.lists})
        self.lib.start = lambda *a, **k: True  # no background update in these tests

    def write(self, folder, name, text):
        os.makedirs(folder, exist_ok=True)
        with open(os.path.join(folder, name), 'w', encoding='utf-8') as f:
            f.write(text)

    def test_add_joins_the_current_list(self):
        other = os.path.join(self.root, 'other')
        self.write(other, 'b.txt', 'Alien.1979.1080p\n')
        self.write(other, 'a.txt', 'Different.2001.720p\n')  # same file name as a list the library already has
        r = self.lib.load_list({'path': other}, 'add')
        self.assertEqual((r['lists'], r['names'], r['moved']), (2, 2, 0))
        self.assertEqual(sorted(os.listdir(self.lists)), ['a (2).txt', 'a.txt', 'b.txt'])
        self.assertEqual(open(os.path.join(self.lists, 'a.txt'), encoding='utf-8').read(), 'Heat.1995.1080p\n')  # not overwritten
        self.assertEqual(self.lib.load_list({'path': os.path.join(other, 'b.txt')}, 'add')['lists'], 0)  # same names again
        self.lib.scan()
        self.assertEqual(self.lib.db.execute('SELECT count(*) FROM files WHERE present=1').fetchone()[0], 3)

    def test_new_puts_the_old_lists_aside_and_deletes_nothing(self):
        self.lib.scan()
        src = os.path.join(self.root, 'src')
        self.write(src, 'fresh.txt', 'Alien.1979.1080p\nRonin.1998.1080p\n')
        r = self.lib.load_list({'path': os.path.join(src, 'fresh.txt')}, 'new')
        self.assertEqual((r['lists'], r['names'], r['moved']), (1, 2, 1))
        self.assertEqual([f for f in os.listdir(self.lists) if f.endswith('.txt')], ['fresh.txt'])
        aside = os.path.join(self.lists, 'previous lists')
        stamp = os.listdir(aside)
        self.assertEqual(len(stamp), 1)
        self.assertEqual(os.listdir(os.path.join(aside, stamp[0])), ['a.txt'])  # kept
        self.lib.scan()
        self.assertEqual(sorted(r[0] for r in self.lib.db.execute('SELECT raw FROM files WHERE present=1')),
                         ['Alien.1979.1080p', 'Ronin.1998.1080p'])

    def test_new_keeps_the_movies_found_in_the_folders(self):
        movies = os.path.join(self.root, 'movies')
        os.makedirs(movies)
        with open(os.path.join(movies, 'Fargo.1996.1080p.mkv'), 'wb') as f:
            f.truncate(31_000_000)
        self.lib.save_settings({'media_dirs': [movies]})
        self.lib.scan(); self.lib.scan_media()
        self.lib.load_list({'text': 'Alien.1979.1080p\n', 'name': 'x.txt'}, 'new')
        self.lib.scan(); self.lib.scan_media()
        self.assertEqual(sorted(r[0] for r in self.lib.db.execute('SELECT raw FROM files WHERE present=1')),
                         ['Alien.1979.1080p', 'Fargo.1996.1080p'])

    def test_sources_csv_text_and_a_folder_of_movies(self):
        r = self.lib.load_list({'text': 'title,year\nAlien,1979\nRonin,1998\n', 'name': 'x.csv'}, 'add')
        self.assertEqual(r['names'], 2)
        self.assertTrue(os.path.exists(os.path.join(self.lists, 'x.txt')))
        movies = os.path.join(self.root, 'movies')
        os.makedirs(movies)
        for n in ('Fargo.1996.1080p.mkv', 'Seven.1995.720p.mkv'):
            with open(os.path.join(movies, n), 'wb') as f:
                f.truncate(31_000_000)
        self.assertEqual(self.lib.load_list({'path': movies}, 'add')['names'], 2)
        with self.assertRaises(ValueError):
            self.lib.load_list({'path': os.path.join(self.root, 'nothing.txt')}, 'add')
        with self.assertRaises(ValueError):
            self.lib.load_list({'path': os.path.join(self.root, 'lists', 'a.txt')}, 'maybe')  # must choose add or new
        with self.assertRaises(ValueError):
            self.lib.load_list({'text': '\n\n', 'name': 'e.txt'}, 'add')

    def test_loading_a_removed_name_again_brings_it_back(self):
        self.lib.scan()
        fid = self.lib.db.execute('SELECT id FROM files').fetchone()[0]
        self.lib.remove_from_list([fid])
        self.lib.scan()
        self.assertEqual(self.lib.db.execute('SELECT count(*) FROM files WHERE present=1').fetchone()[0], 0)
        self.lib.load_list({'text': 'Heat.1995.1080p\n', 'name': 'again.txt'}, 'add')
        self.assertEqual(self.lib.db.execute('SELECT count(*) FROM removed').fetchone()[0], 0)

    def test_preview_warns_about_duplicates(self):
        self.title(1, 'Heat', 1995)
        self.file('Heat.1995.1080p', 1)
        text = 'Heat.1995.1080p\nAlien.1979.720p\nAlien.1979.720p\nHeat 1995 720p\nRonin.1998.1080p\n'
        pv = self.lib.preview_list({'text': text, 'name': 'n.txt'})
        self.assertEqual(pv['names'], 4)
        self.assertEqual(pv['repeated'], ['Alien.1979.720p'])
        self.assertEqual(pv['existing'], ['Heat.1995.1080p'])
        self.assertEqual(pv['similar'], ['Heat 1995 720p'])  # same title and year, another file name
        self.assertEqual(pv['new_n'], 3)
        self.assertEqual(self.lib.db.execute('SELECT count(*) FROM files').fetchone()[0], 1)  # a preview adds nothing
        with self.assertRaises(ValueError):
            self.lib.preview_list({'text': '', 'name': 'n.txt'})

    def test_over_http(self):
        import urllib.request
        from movieshelf import server
        web = os.path.join(self.root, 'web')
        os.makedirs(web)
        httpd = server.serve(self.lib, web)
        base = f'http://127.0.0.1:{httpd.server_address[1]}'
        post = lambda path, body: json.loads(urllib.request.urlopen(urllib.request.Request(
            base + path, json.dumps(body).encode(), {'Content-Type': 'application/json'})).read())
        try:
            body = {'text': 'Heat.1995.1080p\nAlien.1979.720p\nAlien.1979.720p\n', 'name': 'n.txt'}
            pv = post('/api/lists/preview', body)
            self.assertEqual((pv['names'], pv['repeated']), (2, ['Alien.1979.720p']))
            self.assertIn('error', post('/api/lists/preview', {'path': os.path.join(self.root, 'nope.txt')}))
            self.assertIn('error', post('/api/lists/load', {**body, 'mode': ''}))
            self.assertEqual(post('/api/lists/load', {**body, 'mode': 'add'})['names'], 2)
        finally:
            httpd.shutdown()
            httpd.server_close()

    def test_the_quick_backup_keeps_the_lists(self):
        from movieshelf import backup
        self.write(self.lists, 'b.txt', 'Alien.1979.1080p\nRonin.1998.1080p\n')
        path = backup.auto(self.lib)
        self.assertTrue(path)
        self.assertEqual(json.load(open(path, encoding='utf-8'))['lists']['b.txt'], ['Alien.1979.1080p', 'Ronin.1998.1080p'])
        os.remove(os.path.join(self.lists, 'b.txt'))
        with open(os.path.join(self.lists, 'a.txt'), 'w', encoding='utf-8') as f:  # a.txt changed meanwhile: it is not rewritten
            f.write('Other.2000.720p\n')
        n = backup.restore(self.lib, backup.load(self.lib, os.path.basename(path)))
        self.assertEqual(n['lists'], 3)  # the two names of b.txt and the one of a.txt that no list has any more
        self.assertEqual(open(os.path.join(self.lists, 'b.txt'), encoding='utf-8').read(), 'Alien.1979.1080p\nRonin.1998.1080p\n')
        self.assertEqual(open(os.path.join(self.lists, 'a.txt'), encoding='utf-8').read(), 'Other.2000.720p\n')
        self.assertIn('Heat.1995.1080p', open(os.path.join(self.lists, 'a (restored).txt'), encoding='utf-8').read())


class StopJob(TempLibrary):
    wait = FirstRun.wait

    def running_job(self, fn, started):
        self.assertTrue(self.lib.start(fn))
        self.assertTrue(started.wait(2))

    def test_stop_ends_the_job_and_drops_the_queue(self):
        started = threading.Event()

        def slow():
            started.set()
            for i in range(1000):
                self.lib.progress('Working', i, 1000)
                time.sleep(0.01)
        self.running_job(slow, started)
        self.lib.start(lambda: None, queue=True)
        self.assertTrue(self.lib.stop_job())
        self.wait()
        self.assertFalse(self.lib.job['running'])
        self.assertTrue(self.lib.job['stopped'])
        self.assertIsNone(self.lib.job['error'])
        self.assertEqual(self.lib.queue, [])
        self.assertTrue(self.lib.start(lambda: None))  # a stopped job does not block the next one
        self.wait()
        self.assertFalse(self.lib.job['stopped'])

    def test_nothing_to_stop(self):
        self.assertFalse(self.lib.stop_job())

    def test_a_restore_cannot_be_stopped(self):
        started, release = threading.Event(), threading.Event()

        def restore():
            started.set()
            release.wait(2)
        self.lib.restore_full_backup = restore
        self.running_job(self.lib.restore_full_backup, started)
        self.assertFalse(self.lib.job['stoppable'])
        self.assertFalse(self.lib.stop_job())
        release.set()
        self.wait()

    def test_stopped_download_leaves_no_half_file(self):
        from movieshelf import imdb_index
        class Resp:
            headers = {'Content-Length': str(5 << 20)}
            def __enter__(self): return self
            def __exit__(self, *a): return False
            def read(self, n): return b'x' * n
        calls = []
        def progress(stage, done=0, total=0):
            calls.append(done)
            if len(calls) == 2:
                raise Cancelled()
        real = imdb_index.urllib.request.urlopen
        imdb_index.urllib.request.urlopen = lambda *a, **k: Resp()
        try:
            with self.assertRaises(Cancelled):
                imdb_index.download(os.path.join(self.root, 'imdb'), progress, names=['title.ratings'])
        finally:
            imdb_index.urllib.request.urlopen = real
        self.assertEqual(os.listdir(os.path.join(self.root, 'imdb')), [])


class DataHome(unittest.TestCase):
    def test_home_is_one_app_data_folder(self):
        old = {k: os.environ.pop(k, None) for k in ('MOVIESHELF_HOME', 'LOCALAPPDATA')}
        try:
            os.environ['LOCALAPPDATA'] = os.path.join('C:' + os.sep, 'Users', 'x', 'AppData', 'Local')
            self.assertEqual(paths.home(), os.path.join(os.environ['LOCALAPPDATA'], 'MovieShelf'))
            with tempfile.TemporaryDirectory() as d:
                self.assertEqual(paths.home(d), os.path.join(os.environ['LOCALAPPDATA'], 'MovieShelf'))  # no portable.txt
                open(os.path.join(d, 'portable.txt'), 'w').close()
                self.assertEqual(paths.home(d), os.path.abspath(d))
                os.environ['MOVIESHELF_HOME'] = os.path.join(d, 'elsewhere')
                self.assertEqual(paths.home(d), os.path.abspath(os.path.join(d, 'elsewhere')))
        finally:
            for k, v in old.items():
                os.environ.pop(k, None)
                if v is not None:
                    os.environ[k] = v

    def test_default_list_folder_is_in_documents(self):
        self.assertEqual(paths.default_list_dir(), os.path.join(os.path.expanduser('~'), 'Documents', 'MovieShelf', 'lists'))


if __name__ == '__main__':
    unittest.main()
