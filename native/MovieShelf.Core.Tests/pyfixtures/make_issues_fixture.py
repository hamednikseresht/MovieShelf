# Builds issues.db (a library with one case for every branch of movieshelf/issues.py) and issues.json (what the Python app reports on it).
# Run read-only from the source:  python -B -I make_issues_fixture.py <output folder>
import csv, io, json, os, shutil, sys, tempfile
sys.dont_write_bytecode = True
import os, sys; sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'app'))
from movieshelf.library import Library, FILE_FIELDS, now
from movieshelf.parse import parse
from movieshelf import issues

out = sys.argv[1]
root = tempfile.mkdtemp()
os.makedirs(os.path.join(root, 'data'))
lib = Library(root)
db = lib.db
GB = 1_000_000_000
tc = {}


def title(name, year=2000, type='movie', en=None, orig=None, runtime=100, rating=7.5, votes=1000, poster='p.jpg', fa=None, tt=None):
    n = tt or len(tc) + 1
    db.execute('INSERT INTO titles(tconst,type,title,en_title,orig,year,runtime,rating,votes,poster,fa_title) VALUES (?,?,?,?,?,?,?,?,?,?,?)',
               (n, type, name, en, orig, year, runtime, rating, votes, poster, fa))
    tc[name] = n
    return n


def file(raw, t=None, conf='exact', manual=0, present=1, **over):
    p = parse(raw)
    vals = {k: p.get(k) for k in FILE_FIELDS}
    vals.update(over)
    db.execute('INSERT INTO files(raw,list_file,added,present,title,alt_title,year,resolution,source,codec,bit10,hdr,audio,farsi_dubbed,dual_audio,softsub,edition,grp,'
               'tconst,conf,manual,matched) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,1)',
               [raw, 'a.txt', now(), present] + [vals[k] for k in FILE_FIELDS] + [t, conf, manual])
    return db.execute('SELECT last_insert_rowid()').fetchone()[0]


def disk(raw, path, size):
    db.execute('INSERT INTO fileinfo(path,raw,size,mtime) VALUES (?,?,?,1.5)', (path, raw, size))


def probe(path, w, h, dur, ok=1):
    db.execute('INSERT INTO mediainfo(path,size,mtime,ok,duration,width,height) VALUES (?,1,1.5,?,?,?,?)', (path, ok, dur, w, h))


def junk(path, raw, nfiles, size, names):
    db.execute('INSERT INTO novideo(path,raw,nfiles,size,names) VALUES (?,?,?,?,?)', (path, raw, nfiles, size, json.dumps(names)))


def ignore(ikey):
    db.execute('INSERT INTO dismissed(ikey) VALUES (?)', (ikey,))


# ---------- duplicates ----------
t = title('Same Quality', votes=5)
file('Same.Quality.2000.1080p.BluRay.x264-A.mkv', t); file('Same.Quality.2000.1080p.BluRay.x265-B.mkv', t)
disk('Same.Quality.2000.1080p.BluRay.x264-A.mkv', r'D:\m\same a.mkv', 8 * GB); disk('Same.Quality.2000.1080p.BluRay.x265-B.mkv', r'D:\m\same b.mkv', 5 * GB)
t = title('Same No Size')
file('Same.No.Size.2000.720p.mkv', t); file('Same.No.Size.2000.720p.x264.mkv', t)
t = title('Unsure Copy')
file('Unsure.Copy.2000.1080p.mkv', t); file('Unsure.Copy.2000.mkv', t)
t = title('Lower Copy')
file('Lower.Copy.2000.720p.WEB-DL.mkv', t); file('Lower.Copy.2000.1080p.BluRay.10bit.HDR.DTS.5.1.mkv', t)
disk('Lower.Copy.2000.720p.WEB-DL.mkv', r'D:\m\lower 720.mkv', 2 * GB)
t = title('Lower Source')
file('Lower.Source.2000.1080p.WEBRip.mkv', t); file('Lower.Source.2000.1080p.BluRay.mkv', t)
t = title('Versions Pair')
file('Versions.Pair.2000.1080p.BluRay.mkv', t); file('Versions.Pair.2000.1080p.BluRay.Farsi.Dubbed.mkv', t, farsi_dubbed=1)
t = title('Star Wars')
file('Star.Wars.4.1977.1080p.mkv', t, year=1977); file('Star.Wars.6.1983.1080p.mkv', t, year=1983)
t = title('Three Ten To Yuma')
file('3.10.to.Yuma.2007.1080p.mkv', t, title='3.10 to Yuma', year=2000); file('310.to.Yuma.2007.720p.mkv', t, title='310 to Yuma', year=2000)
t = title('Bonus Pair')
file('Bonus.Pair.2000.1080p.mkv', t); file('Bonus.Pair.Featurette.2000.1080p.mkv', t, title='Bonus Pair Featurette')
t = title('Episodes Named')
file('Episodes.Named.S01E01.1080p.mkv', t); file('Episodes.Named.S01E02.1080p.mkv', t)
t = title('Episodes 1x02')
file('Episodes.1x01.2000.mkv', t); file('Episodes.1x02.2000.mkv', t)
disk('Episodes.1x01.2000.mkv', r'D:\m\ep1 a.mkv', GB); disk('Episodes.1x01.2000.mkv', r'E:\m\ep1 b.mkv', GB)
t = title('Tv Series', type='tvSeries')
file('Tv.Series.2000.part1.1080p.mkv', t); file('Tv.Series.2000.part2.1080p.mkv', t)
t = title('Mini Series', type='tvMiniSeries')
file('Mini.Series.2000.1080p.mkv', t); file('Mini.Series.2000.720p.mkv', t)
t = title('Marked Series')
file('Marked.Series.a.2000.1080p.mkv', t); file('Marked.Series.b.2000.1080p.mkv', t)
ignore(f'series:tt{t:07d}')
t = title('Not Series')
file('Not.Series.S01E01.1080p.mkv', t); file('Not.Series.S01E02.720p.mkv', t)
ignore(f'noseries:tt{t:07d}')
t = title('Single Series', type='tvSeries')
file('Single.Series.2000.1080p.mkv', t)
t = title('Two Folders')
file('Two.Folders.2000.1080p.mkv', t)
disk('Two.Folders.2000.1080p.mkv', r'D:\m\two.mkv', 4 * GB); disk('Two.Folders.2000.1080p.mkv', r'E:\m\two.mkv', 3 * GB); disk('Two.Folders.2000.1080p.mkv', r'F:\m\two.mkv', GB)
t = title('Copies Plus Dup')
file('Copies.Plus.Dup.2000.1080p.mkv', t); file('Copies.Plus.Dup.2000.720p.mkv', t)
disk('Copies.Plus.Dup.2000.720p.mkv', r'D:\m\cpd.mkv', GB); disk('Copies.Plus.Dup.2000.720p.mkv', r'E:\m\cpd.mkv', GB)
t = title('Ignored Dup')
a = file('Ignored.Dup.2000.1080p.mkv', t); b = file('Ignored.Dup.2000.720p.mkv', t)
disk('Ignored.Dup.2000.720p.mkv', r'D:\m\ign.mkv', 9 * GB)
ignore(f'dupes:tt{t:07d}:{a}-{b}')
t = title('Equal Everything')
file('Equal.Everything.2000.1080p.BluRay.a.mkv', t); file('Equal.Everything.2000.1080p.BluRay.b.mkv', t); file('Equal.Everything.2000.1080p.BluRay.c.mkv', t)

# ---------- quality ----------
t = title('Popular Low', votes=900000); file('Popular.Low.2000.480p.mkv', t)
t = title('popular low two', votes=900000); file('Popular.Low.Two.2000.576p.mkv', t)
t = title('Rare Low', votes=None, rating=None); file('Rare.Low.2000.SD.mkv', t, resolution='SD')
t = title('Mixed Low', votes=7); file('Mixed.Low.2000.480p.mkv', t); file('Mixed.Low.2000.576p.DVDRip.mkv', t)
t = title('Partly Known'); file('Partly.Known.2000.480p.mkv', t); file('Partly.Known.2000.mkv', t)
t = title('No Res'); file('No.Res.2000.mkv', t)
t = title('Dub Only', votes=30); file('Dub.Only.2000.1080p.Farsi.Dubbed.mkv', t, farsi_dubbed=1)
t = title('Dub Pair', votes=31); file('Dub.Pair.2000.1080p.Dubbed.mkv', t, farsi_dubbed=1); file('Dub.Pair.2000.720p.Dubbed.mkv', t, farsi_dubbed=1)
t = title('Name Vs Picture'); file('Name.Vs.Picture.2000.1080p.mkv', t)
disk('Name.Vs.Picture.2000.1080p.mkv', r'D:\m\nvp.mkv', GB); probe(r'D:\m\nvp.mkv', 1280, 544, 6000)
t = title('Real Fine'); file('Real.Fine.2000.1080p.mkv', t)
disk('Real.Fine.2000.1080p.mkv', r'D:\m\fine.mkv', GB); probe(r'D:\m\fine.mkv', 1920, 800, 6000)
t = title('Probe Failed'); file('Probe.Failed.2000.2160p.mkv', t)
disk('Probe.Failed.2000.2160p.mkv', r'D:\m\fail.mkv', GB); probe(r'D:\m\fail.mkv', 720, 576, 60, ok=0)
t = title('Small 4K'); file('Small.4K.2000.2160p.mkv', t); file('Small.4K.2000.1080p.mkv', t)
disk('Small.4K.2000.2160p.mkv', r'D:\m\s4k.mkv', GB); probe(r'D:\m\s4k.mkv', 1920, 1080, 6000)
disk('Small.4K.2000.1080p.mkv', r'D:\m\s1k.mkv', GB); probe(r'D:\m\s1k.mkv', 640, 360, 6000)
t = title('Second Path'); file('Second.Path.2000.1080p.mkv', t)
disk('Second.Path.2000.1080p.mkv', r'D:\m\sp1.mkv', GB); disk('Second.Path.2000.1080p.mkv', r'D:\m\sp2.mkv', GB); probe(r'D:\m\sp2.mkv', 800, 600, 6000)

# ---------- length ----------
t = title('Cut Short', runtime=120); file('Cut.Short.2000.1080p.mkv', t)
disk('Cut.Short.2000.1080p.mkv', r'D:\m\cs.mkv', GB); probe(r'D:\m\cs.mkv', 1920, 1080, 3600)
t = title('Long Cut', runtime=100); file('Long.Cut.2000.1080p.mkv', t)
disk('Long.Cut.2000.1080p.mkv', r'D:\m\lc.mkv', GB); probe(r'D:\m\lc.mkv', 1920, 1080, 9000)
t = title('Half Minute', runtime=120); file('Half.Minute.2000.1080p.mkv', t)
disk('Half.Minute.2000.1080p.mkv', r'D:\m\hm.mkv', GB); probe(r'D:\m\hm.mkv', 1920, 1080, 4770)
t = title('Half Minute Odd', runtime=120); file('Half.Minute.Odd.2000.1080p.mkv', t)
disk('Half.Minute.Odd.2000.1080p.mkv', r'D:\m\hmo.mkv', GB); probe(r'D:\m\hmo.mkv', 1920, 1080, 4830)
t = title('Short Film Close', runtime=20); file('Short.Film.Close.2000.1080p.mkv', t)
disk('Short.Film.Close.2000.1080p.mkv', r'D:\m\sf.mkv', GB); probe(r'D:\m\sf.mkv', 1920, 1080, 600)
t = title('No Runtime', runtime=None); file('No.Runtime.2000.1080p.mkv', t)
disk('No.Runtime.2000.1080p.mkv', r'D:\m\nr.mkv', GB); probe(r'D:\m\nr.mkv', 1920, 1080, 600)

# ---------- year ----------
t = title('Year Off', year=2000); file('Year.Off.2003.1080p.mkv', t)
t = title('Year Near', year=2000); file('Year.Near.2001.1080p.mkv', t)
t = title('Year Review', year=2000); file('Year.Review.2005.1080p.mkv', t, conf='review')
t = title('Year Manual', year=2000); file('Year.Manual.2005.1080p.mkv', t, conf='manual', manual=1)
t = title('Year Second File', year=2000); file('Year.Second.File.1080p.mkv', t, year=None); file('Year.Second.File.1995.720p.mkv', t)

# ---------- type, poster, Persian ----------
t = title('Tv Special', type='tvSpecial'); file('Tv.Special.2000.1080p.mkv', t)
t = title('Tv Short', type='tvShort'); file('Tv.Short.2000.1080p.mkv', t)
t = title('Tv Episode', type='tvEpisode'); file('Tv.Episode.2000.1080p.mkv', t)
t = title('No Poster', poster=None); file('No.Poster.2000.1080p.mkv', t)
t = title('Empty Poster', poster=''); file('Empty.Poster.2000.1080p.mkv', t)
t = title('Latin Fa', fa='Salam Donya'); file('Latin.Fa.2000.1080p.mkv', t)
t = title('Good Fa', fa='سلام دنیا'); file('Good.Fa.2000.1080p.mkv', t)
t = title('Edited Fa', fa='Old Latin'); file('Edited.Fa.2000.1080p.mkv', t)
db.execute("INSERT INTO fa_titles(tconst,title,source) VALUES (?,?,'user')", (t, 'نام درست'))
t = title('Own Latin Fa', fa='خوب'); file('Own.Latin.Fa.2000.1080p.mkv', t)
db.execute("INSERT INTO fa_titles(tconst,title,source) VALUES (?,?,'user')", (t, 'Latin Again'))

# ---------- names ----------
t = title('The Godfather'); file('Godfather.2000.1080p.mkv', t)
t = title('Spirited Away', orig='Sen to Chihiro no kamikakushi'); file('Chihiro.2000.1080p.mkv', t)
t = title('Alternate', orig='Otro'); file('Tercero.aka.Other.2000.1080p.mkv', t, title='Tercero', alt_title='Other')
t = title('Edition Words'); file('Edition.Words.Directors.Cut.2000.1080p.mkv', t, title="Edition Words Director's Cut")
t = title('Punctuation: Fine!'); file('Punctuation.Fine.2000.1080p.mkv', t)
t = title('Amelie Accent', en='Amélie Accent'); file('Amelie.Accent.2000.1080p.mkv', t)
t = title('Unsure Name'); file('Totally.Different.2000.1080p.mkv', t, conf='fuzzy')
t = title('Manual Name'); file('Other.Thing.2000.1080p.mkv', t, conf='fuzzy', manual=1)
# two names and two IMDb names at one score: the tuple order decides which pair is quoted
t = title('abcd', orig='abce'); file('Tie.Names.2000.1080p.mkv', t, title='abxx', alt_title='xxcd')
t = title('wxyz', orig='wxyq'); file('Tie.Other.2000.1080p.mkv', t, title='wxab', alt_title='wxcd')
t = title('Seventy Case', en='abcdefghij'); file('Seventy.Case.2000.1080p.mkv', t, title='abcdefgxyz')      # similarity exactly 70
t = title('Thirty', en='abcdefghijklmnopqrstuvwxyzabcd'); file('Thirty.Case.2000.1080p.mkv', t, title='abcdefghijklmnopqrstuXXXXXXXXX')
t = title('Worst File', en='Worst File'); file('Worst.File.2000.1080p.mkv', t, title='Worst Filez'); file('Worst.File.2000.720p.mkv', t, title='Quite Another Thing')
t = title('Emoji 😀 Film'); file('Emoji.Film.2000.1080p.mkv', t, title='Emoji Film X')
t = title('Fa Name', fa='فیلم'); file('Fa.Name.2000.1080p.mkv', t, title='فلم')
t = title('', en=None, orig='Only Orig'); file('Empty.Title.2000.1080p.mkv', t, title='Blank Title')
t = title('İstanbul Hatırası'); file('Istanbul.2000.1080p.mkv', t, title='Istanbul')
t = title('Ñandú'); file('Nandu.2000.1080p.mkv', t, title='Nando')
t = title('zebra sort'); file('Zebra.2000.1080p.mkv', t, title='Zebrra')
t = title('Apple Sort'); file('Apple.2000.1080p.mkv', t, title='Appel')

# ---------- no video ----------
file('Empty.Folder.2000.1080p', None, conf='none')
junk(r'D:\m\Empty.Folder.2000.1080p', 'Empty.Folder.2000.1080p', 0, 0, [])
t = title('Junk Folder')
file('Junk.Folder.2000.1080p.mkv', t)
junk(r'D:\m\Junk.Folder.2000.1080p.mkv', 'Junk.Folder.2000.1080p.mkv', 8, 123456, ['a.nfo', 'b.jpg', 'c.url', 'd.txt', 'e.txt', 'f.txt'])
t = title('Two Junk')
file('Two.Junk.2000.1080p.mkv', t); file('Two.Junk.2000.720p.mkv', t)
junk(r'D:\m\Two.Junk 1', 'Two.Junk.2000.1080p.mkv', 2, 9, ['a.nfo', 'b.jpg'])
junk(r'E:\m\Two.Junk 2', 'Two.Junk.2000.1080p.mkv', 0, 0, [])
junk(r'F:\m\Two.Junk 3', 'Two.Junk.2000.720p.mkv', 1, 3, ['a.nfo'])
t = title('Long Junk')
file('Long.Junk.2000.1080p.mkv', t)
junk(r'D:\m\Long.Junk', 'Long.Junk.2000.1080p.mkv', 3, 3, ['x' * 90 + '.nfo', 'y' * 90 + '.jpg', '😀' * 10 + '.url'])
file('Ignored.Junk.2000.1080p', None, conf='none')
junk(r'D:\m\Ignored.Junk', 'Ignored.Junk.2000.1080p', 0, 0, [])
ignore(f'novideo:f{db.execute("SELECT id FROM files WHERE raw=?", ("Ignored.Junk.2000.1080p",)).fetchone()[0]}')
junk(r'D:\m\Gone', 'Not.In.Any.List', 1, 1, ['a.nfo'])

# ---------- unmatched and gone ----------
file('Unmatched.Name.2000', None, conf='none', title='Unmatched Name', resolution=None)
file('.2000', None, conf='none', title='')
file('Gone.Line.2000.1080p.mkv', None, present=0, conf='none')
file('Missing.Title.2000.1080p.mkv', 999999, conf='exact')
db.execute("INSERT INTO fileinfo(path,raw,size,mtime) VALUES (?,?,?,1.5)", (r'D:\m\gone.mkv', 'Gone.Line.2000.1080p.mkv', 5))
db.commit()

rep = issues.build(lib)
rows = lib.problems()


# the two CSV endpoints of server.py; the handlers are closures, so their few lines are repeated here
def issues_csv(kind, sub):
    rs = rep.get(kind) or []
    if sub != 'all':
        rs = [r for r in rs if r.get('sub') == sub]
    o = io.StringIO()
    w = csv.writer(o)
    w.writerow(['Title', 'Year', 'IMDb', 'Rating', 'Votes', 'Problem', 'Files', 'Ignored'])
    for r in rs:
        w.writerow([r['title'], r['year'], r['tt'] or '', r['rating'], r['votes'], r['note'],
                    ' | '.join(f"{f['raw']} [{f['quality']}{', ' + f['verdict'] if f['verdict'] else ''}]" for f in r['files']),
                    'yes' if r['ignored'] else ''])
    return '\ufeff' + o.getvalue()


csvs = {f'{k}/all': issues_csv(k, 'all') for k in issues.KINDS}
for s in ('same', 'lower', 'versions', 'mixed', 'copies', 'series'):
    csvs[f'dupes/{s}'] = issues_csv('dupes', s)
counts = {'duplicates': issues.open_duplicates(lib), 'novideo': issues.open_novideo(lib)}

# the folder scan: the same tree is built by the test, paths are relative to the movie folder
tree = [('Heat.1995.1080p/Heat.1995.1080p.mkv', 40_000_000),        # a real video in a folder named like an entry
        ('Junk.Only.1999/info.nfo', 10), ('Junk.Only.1999/cover.jpg', 20), ('Junk.Only.1999/sub/more.url', 5),
        ('Sample.Only.1999/Sample.Only.1999-sample.mkv', 40_000_000),   # a sample is not the movie
        ('Tiny.Clip.1999/clip.mp4', 1000),
        ('Other.Place.2001/readme.txt', 3), ('elsewhere/Other.Place.2001.mkv', 40_000_000),     # the entry has its video in another folder
        ('Nested.2002/CD1/movie.mkv', 40_000_000),                      # a video below the folder counts
        ('Group/Inner.2003/cover.jpg', 4),
        ('.hidden/Hidden.2004/x.nfo', 1), ('$RECYCLE/Bin.2005/x.nfo', 1)]
empty = ['Empty.2000']
lists = ['Heat.1995.1080p', 'Junk.Only.1999', 'Sample.Only.1999', 'Tiny.Clip.1999', 'Other.Place.2001', 'Nested.2002', 'Inner.2003', 'Hidden.2004', 'Bin.2005',
         'Empty.2000', 'Gone.2006']
scan_root = tempfile.mkdtemp()
media = os.path.join(scan_root, 'movies')
for p, size in tree:
    full = os.path.join(media, p.replace('/', os.sep))
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, 'wb') as f:
        f.truncate(size)
for d in empty:
    os.makedirs(os.path.join(media, d))
os.makedirs(os.path.join(scan_root, 'data'))
os.makedirs(os.path.join(scan_root, 'lists'))
with open(os.path.join(scan_root, 'lists', 'a.txt'), 'w', encoding='utf-8') as f:
    f.write('\n'.join(lists))
scan = Library(scan_root)
scan.save_settings({'list_dir': os.path.join(scan_root, 'lists'), 'media_dirs': [media]})
scan.scan()
scan.scan_media()
found = [{'path': os.path.relpath(r['path'], media), 'raw': r['raw'], 'nfiles': r['nfiles'], 'size': r['size'], 'names': json.loads(r['names'])}
         for r in scan.db.execute('SELECT * FROM novideo ORDER BY path')]
scan.db.close()
shutil.rmtree(scan_root, ignore_errors=True)

with open(os.path.join(out, 'issues.json'), 'w', encoding='utf-8') as f:
    json.dump({'report': rep, 'csv': csvs, 'counts': counts, 'scan': {'tree': tree, 'empty': empty, 'lists': lists, 'novideo': found}}, f, ensure_ascii=False, indent=0)
db.execute('PRAGMA wal_checkpoint(TRUNCATE)')
db.close()
shutil.copy(os.path.join(root, 'data', 'library.db'), os.path.join(out, 'issues.db'))
shutil.rmtree(root, ignore_errors=True)
print({k: len(v) if isinstance(v, list) else v for k, v in rep.items()}, counts, found)
