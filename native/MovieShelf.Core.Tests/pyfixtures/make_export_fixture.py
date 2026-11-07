"""Writes the export fixtures from the Python exporter and the web app's grouping code.

Run with the Python that has the MovieShelf app: python -B make_export_fixture.py
Reads ../../../app (movieshelf/exporter.py, web/app.js) or the folder given as the first argument, and never writes there."""
import importlib.util
import json
import os
import random
import subprocess
import sys

sys.dont_write_bytecode = True
here = os.path.dirname(os.path.abspath(__file__))
app = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, '..', '..', '..', 'app')
spec = importlib.util.spec_from_file_location('exporter', os.path.join(app, 'movieshelf', 'exporter.py'))
exporter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(exporter)


class FrozenTime:
    @staticmethod
    def strftime(fmt):
        return {'%Y-%m-%d': '2026-01-02', '%Y-%m-%d %H:%M': '2026-01-02 03:04'}[fmt]


exporter.time = FrozenTime


def file(id, raw, **kw):
    f = {'id': id, 'raw': raw, 'res': None, 'src': None, 'codec': None, 'b10': 0, 'hdr': 0, 'audio': None, 'fa': 0, 'dual': 0,
         'sub': 0, 'ed': None, 'grp': None, 'conf': 'exact', 'list': 'list.txt', 'size': None, 'paths': []}
    f.update(kw)
    return f


def movie(k, files, **kw):
    m = {'k': k, 'files': files, 'res': files[0].get('res'), 'conf': 'exact', 'genres': []}
    if k.startswith('tt'):
        m.update({'tt': k, 'type': 'movie', 'title': 'Untitled', 'orig': None, 'en': None, 'fa_t': None, 'year': None, 'runtime': None,
                  'rating': None, 'votes': 0, 'dir': [], 'wri': [], 'cast': [], 'plot': None, 'tagline': None, 'countries': [], 'langs': [],
                  'coll': None, 'rt': None, 'mc': None})
    m.update(kw)
    return m


full1 = file(1, 'The.Matrix.1999.1080p.BluRay.x265.10bit.HDR.DTS.Dubbed.Farsi-GRP', res='1080p', src='BluRay', codec='x265', b10=1, hdr=1,
             audio='5.1', fa=1, dual=1, sub=1, ed='Remastered', grp='GRP', size=4444444444, paths=[r'D:\Films\Matrix.mkv', r'E:\Copy\Matrix.mkv'],
             dur=8160, w=1920, h=1040, alang=['English', 'Persian'], slang=['English'], list='a.txt')
full2 = file(2, 'The.Matrix.1999.720p.WEB-DL.x264', res='720p', src='WEB-DL', codec='x264', audio='2.0', grp='Other', size=1500000000,
             paths=[r'D:\Films\Matrix 720.mkv'], dur=8100, w=1280, h=694, alang=['English', 'French'], slang=['Persian', 'English'], list='b.txt')
MOVIES = [
    movie('tt0133093', [full1, full2], type='movie', title='The Matrix', en='The Matrix (en)', orig='The Matrix', fa_t='ماتریکس', year=1999,
          runtime=136, genres=['Action', 'Sci-Fi'], countries=['United States', 'Australia'], langs=['English'], coll='The Matrix Collection',
          plot='A hacker & his "friends" <learn>\nthe truth\x07, with\ttabs\x0b and\x1f controls.', tagline='Free your mind', rating=8.7, votes=2100000,
          rt=83, mc=73, dir=[[905152, 'Lana Wachowski'], [905154, 'Lilly Wachowski']], wri=[[905152, 'Lana Wachowski']],
          cast=[[i, f'Actor {i}', f'Role {i}'] for i in range(1, 13)], res='1080p', fav=1, watched=1, myrating=9, notes='First line\nSecond & last', watched_at='2024-01-02'),
    movie('tt0000002', [file(3, 'Plain.Eight.2001.mkv', res='SD', size=125000000)], title='Plain Eight', year=2001, rating=8.0, votes=0,
          dir=[[1, ''], [2, 'Named'], [3]], cast=[[9, ''], [10, 'Only Name']], type='weird', conf='xyz', fav=0, watched=0, myrating=0, notes=''),
    movie('f7', [file(7, 'Unknown.Thing.1999.720p', res='720p', src='HDTV', conf='none', size=999)], title='Unknown Thing', year=1999, conf='none'),
    movie('f8', [file(8, 'No.Year.Here', conf='none')], title='No Year Here', conf='none', myrating=None),
    movie('tt0000005', [file(4, 'Long.Notes.2010', res='2160p', size=375000000, dur=90), file(5, 'Long.Notes.2010.part2', res='2160p', size=2675000000, dur=150, w=1920, h=None)],
          title='Long Notes', en='', orig='', year=2010, type='tvMiniSeries', rating=0.0, rt=0, mc=0, conf='manual',
          notes='ab\x01' + 'x' * 31990 + '\U0001F600' * 30, plot='p' * 40000, genres=['Comedy']),
    movie('tt0000006', [file(6, 'Same.Source.A', src='BluRay', codec='x264', audio='5.1', ed='Extended', grp='A', size=1005000000),
                        file(9, 'Same.Source.B', src='BluRay', codec='x264', audio='5.1', ed='Extended', grp='A', list='a.txt', size=None, w=640, h=480)],
          title='سینمای ایران', fa_t='سینمای ایران', year=1985, type='tvSeries', rating=9.5, runtime=45, genres=['Drama', 'History'],
          countries=['Iran'], langs=['Persian', 'English'], conf='review', tagline='<&>'),
    movie('tt0000010', [file(10, 'Quote"And,Comma.2020', grp='Q,"R', size=1)], title='Quote "And", Comma', en='Quote "And", Comma', year=2020, type='video',
          rating=7.25, votes=12, conf='fuzzy', notes='cell, with "quotes"', plot='line\r\nbreak'),
]


def write_bytes(name, data):
    with open(os.path.join(here, name), 'wb') as f:
        f.write(data)


all_cols = [c[0] for c in exporter.COLUMNS]
keys = [m['k'] for m in MOVIES]
REQUESTS = {
    'default_movie': {},
    'all_movie': {'columns': all_cols, 'info': {'scope': 'Whole library'}},
    'all_file': {'columns': all_cols, 'rows': 'file', 'info': {'scope': 'Current results', 'filters': ['year:1999', 'Drama', 'genre: Comedy']}},
    'sheets': {'columns': ['en', 'imdb', 'rating', 'size'], 'keys': keys,
               'sheets': [{'name': '9.0 and up', 'keys': ['tt0000006']}, {'name': '8.0 – 8.9', 'keys': ['tt0133093', 'tt0000002']},
                          {'name': "'Who's: a/b*c?[x]\\'", 'keys': ['f7']}, {'name': 'x' * 40, 'keys': ['f8']}, {'name': 'X' * 40, 'keys': ['tt0000005']},
                          {'name': '', 'keys': ['tt0000010']}, {'name': 'A & "B" <C>', 'keys': []}],
               'info': {'scope': 'Selected movies', 'filters': ['search: a & b']}},
    'one_column': {'columns': ['orig'], 'format': 'csv'},
    'all_csv': {'columns': all_cols, 'format': 'csv'},
    'all_file_csv': {'columns': all_cols, 'rows': 'file', 'format': 'csv'},
}
for name, req in REQUESTS.items():
    fname, ctype, data, rows = exporter.build(MOVIES, dict(req))
    write_bytes(f'export_{name}.{"csv" if req.get("format") == "csv" else "xlsx"}', data)
    req['_rows'] = rows
    req['_fname'] = fname

random.seed(7)
sizes = [1, 999, 10 ** 7, 125000000, 375000000, 625000000, 875000000, 1125000000, 1005000000, 2675000000, 1015000000] + [random.randrange(1, 40 * 10 ** 9) for _ in range(3000)]
fixture = {
    'columns': exporter.columns_info(),
    'widths': [c[3] for c in exporter.COLUMNS],
    'kinds': [c[4] for c in exporter.COLUMNS],
    'movies': MOVIES,
    'rows_movie': exporter.cell_values(exporter.build_rows(MOVIES, keys, False), all_cols),
    'rows_file': exporter.cell_values(exporter.build_rows(MOVIES, keys, True), all_cols),
    'requests': REQUESTS,
    'sizes': [[n, exporter._gb(n)] for n in sizes],
    'minutes': [[s, exporter._minutes(s)] for s in (0, 29, 30, 31, 89, 90, 91, 150, 210, 5400, 8160)],
    'sheet_names': [['Drama', 'drama', 'DRAMA', 'a' * 40, 'a' * 40, 'a' * 40, '', "'quoted'", ' spaced ', 'a:b/c', 'Export info'],
                    exporter._sheet_names(['Drama', 'drama', 'DRAMA', 'a' * 40, 'a' * 40, 'a' * 40, '', "'quoted'", ' spaced ', 'a:b/c', 'Export info'])],
}
with open(os.path.join(here, 'export_fixture.json'), 'w', encoding='utf-8') as f:
    json.dump(fixture, f, ensure_ascii=True, indent=None)

# the grouping lives in the web app: run its own code over the same movies
js = open(os.path.join(app, 'web', 'app.js'), encoding='utf-8').read()
start, end = js.index('const GROUPS = {'), js.index('function buildRows()')
fold = next(line for line in js.splitlines() if line.startswith('const fold ='))
script = f"""
const RES_ORDER = ['2160p', '1080p', '720p', '576p', '480p', 'SD'];
const TYPE_LABEL = {{ movie: 'Movie', tvMovie: 'TV movie', video: 'Video', short: 'Short', tvShort: 'TV short', tvSpecial: 'TV special', tvMiniSeries: 'Mini-series', tvSeries: 'TV series' }};
{fold}
let GROUP = 'none', REV = false, SORT = 'title';
const $ = () => ({{ value: SORT }});
{js[start:end]}
const movies = JSON.parse(require('fs').readFileSync(0, 'utf8'));
for (const m of movies) {{ m.tkey = fold(m.en || m.title); m.decade = m.year ? Math.floor(m.year / 10) * 10 : null; }}
const out = [];
for (const g of ['none', 'decade', 'year', 'genre', 'rating', 'res', 'type', 'letter', 'seen'])
  for (const [sort, rev] of [['title', false], ['title', true], ['year-desc', false], ['year-desc', true]]) {{
    GROUP = g; SORT = sort; REV = rev;
    out.push({{ group: g, sort, rev, sheets: groupList(movies).map(e => ({{ label: e.label, keys: e.items.map(m => m.k) }})) }});
  }}
console.log(JSON.stringify(out));
"""
done = subprocess.run(['node', '-e', script], input=json.dumps(MOVIES).encode('utf-8'), capture_output=True, check=True)
write_bytes('export_groups.json', done.stdout)
print('ok', len(MOVIES), 'movies')
