"""Writes what the Python app does, for ParityTests.cs: core_parity.json, python_removed.db, python_reparse.db.
python -B make_core_parity.py <output folder>"""
import csv
import io
import json
import os
import shutil
import sys
import tempfile

sys.dont_write_bytecode = True
import os, sys; sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'app'))
from movieshelf import checker  # noqa: E402
from movieshelf.library import Library, image_type  # noqa: E402
from movieshelf.matcher import reason  # noqa: E402
from movieshelf.parse import parse  # noqa: E402

out = sys.argv[1]


def dialect_of(line):
    try:
        d = csv.Sniffer().sniff(line, delimiters=',;\t')
    except csv.Error:
        d = csv.excel
    return d


def dialect_json(d):
    return {'delimiter': d.delimiter, 'quote': d.quotechar, 'doublequote': bool(d.doublequote), 'skipspace': bool(d.skipinitialspace)}


SNIFF = ['Title,Year', '"Title","Year"', '"Title";"Year"', "'Title';'Year'", 'Title, Year', '"a", "b", "c"', 'a;b,c', 'a\tb;c', 'Name',
         '"Name"', '"a""b",c', 'x|"y"|z,title', '"name",year', 'name,"year"', "don't,name", '"Say ""hi""",Year', ',', ';;', '"a, b";c',
         'file;"size"', 'Title,  Year', 'Title; Year', 'a, b,c', '"a" , "b"', "'a', 'b'", '"a"\t"b"', 'name\t"year"', '"x" title', '""', '"",""',
         'Const,Your Rating,Date Rated,Title', '"Const";"Title"', "it's,'quoted',name", '"unclosed,name', 'نام,سال', '"نام","سال"', 'a,"b ""c"" d",e']

CSV = [
    ('Title,Year\nHeat,1995\n"Amélie, Le fabuleux",2001\n', 'a.csv'),
    ('Title, Year\nHeat, 1995\nSeven,  1995\n', 'a.csv'),                  # a space after each delimiter of the header
    ('"Title"; "Year"\n"Heat"; "1995"\n"Alien"; 1979\n', 'a.csv'),
    ("'Title';'Year'\n'Heat';'1995'\n'It''s';2000\n", 'a.csv'),            # single quotes
    ('Title,Year\n"Say ""hi"" now",2001\n', 'a.csv'),                      # no quotes in the header: no double quoting
    ('"Title","Year"\n"Say ""hi"" now",2001\n', 'a.csv'),
    ('"Say ""hi""",Year\n"Say ""hi"" now",2001\n', 'a.csv'),               # double quoting seen in the header
    ('File\tSize\n"Heat 1995.mkv"\t1\nAlien.1979.avi\t2\n', 'a.tsv'),
    ('name,year\nHeat.1995.srt,1995\nHeat.1995.mkv,1995\nposter.jpg,\n', 'a.csv'),
    ('Heat.1995.720p\nHeat.1995.720p.srt\n"Alien.1979.nfo"\nSeven 1995\n', 'a.txt'),
    ('Const,Title\ntt0113277,Heat\ntt0000003,\n,\n', 'x.csv'),
    ('title;imdb\nA 2000;tt1234567\nB.nfo;tt7654321\n', 'pasted.txt'),
    ('Title,Year\n"open quote,2001\nHeat,1995\n', 'a.csv'),
    ('Title,Year\nHeat "the movie",1995\n', 'a.csv'),
]

ROWS = [('a,b\n"c,d",e\n\nf', ','), ('"a\nb",c\n', ','), ('a,"b""c"\n', ','), ('a,"b"c,d\n', ','), ('a,b"c,d\n', ','), ('"open', ','), ('a;b;\n', ';'), ('a\r\nb\r\n', ',')]

data = {
    'reasons': [{'conf': c, 'fy': f, 'ty': t, 'text': reason(c, f, t)} for c, f, t in [
        ('none', None, None), ('fuzzy', 1999, 1999), ('noyear', None, 1999), ('online', 2000, 2000), ('review', 1999, 2000), ('review', 1999, 1999),
        ('review', None, 1999), ('exact', 1999, 1999), ('good', 1999, 1999)]],
    'sidefiles': [{'name': n, 'side': checker.is_side_file(n)} for n in [
        'Heat.1995.1080p.srt', 'Heat.1995.1080p.SRT', ' "Heat.1995.nfo" ', 'Heat.1995.1080p', 'Heat.1995.mkv', 'poster.jpg', '.srt', 'a.7z', 'readme.txt',
        'Heat 1995', 'Mr. Robot', 'St. Elmo.srt ', 'x.tar.gz', 'dir\\sub.idx', 'dir.srt/name', 'a.sami', 'a.webp', 'a.exe']],
    'imagetypes': [{'hex': h, 'type': image_type(bytes.fromhex(h))} for h in [
        'ffd8ffe0', 'ffd8', '89504e470d0a1a0a00', '89504e470d0a1a', '52494646000000005745425000', '5249464600000000', '474946383761', '474946383961ff',
        '474946383861', '3c68746d6c3e', '']],
    'alt': [{'name': n, 'title': parse(n)['title'], 'alt': parse(n)['alt_title'], 'year': parse(n)['year']} for n in [
        'Foo.().2014.1080p', 'Foo.(...).2014.1080p', 'Foo.(Bar).2014.720p', 'Foo.2014.720p', '().2014.720p', 'Foo ( ) AKA Bar 2014', 'Foo aka Bar.2014']],
    'sniff': [{'line': l, 'dialect': dialect_json(dialect_of(l))} for l in SNIFF],
    'csv': [{'text': t, 'name': n, 'entries': [{'raw': e['raw'], 'tconst': e.get('tconst')} for e in checker.entries_from_text(t, n)]} for t, n in CSV],
    'rows': [{'text': t, 'delim': d, 'rows': list(csv.reader(io.StringIO(t), delimiter=d))} for t, d in ROWS],
}

# ---- a library that went through the Python app
LINES = ['Heat.1995.720p', 'Alien.1979.1080p', 'Seven.1995.1080p', 'پدرخوانده.1972.720p', 'Seven.1995.srt', 'Heat.1995.720p.nfo']
root = tempfile.mkdtemp()
lists = os.path.join(root, 'lists')
os.makedirs(lists)
with open(os.path.join(lists, 'a.txt'), 'w', encoding='utf-8') as f:
    f.write('\n'.join(LINES) + '\n')
lib = Library(root)
lib.save_settings({'list_dir': lists})
lib.scan()
db = lib.db
db.execute("INSERT INTO titles(tconst,type,title,year) VALUES (2,'movie','Alien',1979)")
db.execute("UPDATE files SET tconst=2, conf='exact', matched=1 WHERE raw='Alien.1979.1080p'")
db.commit()
ids = {r['raw']: r['id'] for r in db.execute('SELECT id, raw FROM files')}
lib.remove_from_list([ids['Alien.1979.1080p'], ids['پدرخوانده.1972.720p']])
target = os.path.join(out, 'python_removed.db')
if os.path.exists(target):
    os.remove(target)
db.execute('VACUUM INTO ?', (target,))
items = lib.removed_items()
for r in items:
    r['removed'] = None  # the date is checked by format, not by value
lib.scan()
data['removed'] = {'lines': LINES, 'items': items, 'raws_after_scan': sorted(r['raw'] for r in db.execute('SELECT raw FROM files WHERE present=1')),
                   'all_raws': sorted(r['raw'] for r in db.execute('SELECT raw FROM files'))}
db.close()
shutil.rmtree(root, ignore_errors=True)

# ---- stored names from an older parser, before and after the Python re-read
LINES = ['Heat.1995.720p', 'Alien.1979.1080p.BluRay.x264', 'Seven.1995.1080p', 'Fixed.By.Hand.2000.720p', 'Gone.From.Lists.2001.720p', 'Foo.().2014.1080p']
root = tempfile.mkdtemp()
lists = os.path.join(root, 'lists')
os.makedirs(lists)
with open(os.path.join(lists, 'a.txt'), 'w', encoding='utf-8') as f:
    f.write('\n'.join(l for l in LINES if not l.startswith('Gone')) + '\n')
lib = Library(root)
lib.save_settings({'list_dir': lists})
db = lib.db
for raw in LINES:
    p = parse(raw)
    db.execute('INSERT INTO files(raw, list_file, added, title, alt_title, year, resolution, tconst, conf, matched) VALUES (?,?,?,?,?,?,?,?,?,1)',
               (raw, 'a.txt', 'x', p['title'], p['alt_title'], p['year'], p['resolution'], 5, 'good'))
db.execute("UPDATE files SET title='Old Name', resolution=NULL WHERE raw LIKE 'Alien%'")          # title differs: matched again
db.execute("UPDATE files SET resolution=NULL, source='wrong' WHERE raw LIKE 'Seven%'")            # only quality differs: match kept
db.execute("UPDATE files SET title='Old Hand', year=1, manual=1, conf='manual' WHERE raw LIKE 'Fixed%'")  # a manual match is kept
db.execute("UPDATE files SET title='Old Gone' WHERE raw LIKE 'Gone%'")                            # not in a list any more: read too
db.execute("UPDATE files SET alt_title=NULL WHERE raw LIKE 'Foo%'")                               # '' and NULL are different
db.commit()
target = os.path.join(out, 'python_reparse.db')
if os.path.exists(target):
    os.remove(target)
db.execute('VACUUM INTO ?', (target,))
lib.scan()
cols = ['raw', 'title', 'alt_title', 'year', 'resolution', 'source', 'codec', 'bit10', 'hdr', 'audio', 'farsi_dubbed', 'dual_audio', 'softsub', 'edition', 'grp',
        'matched', 'manual', 'present', 'tconst', 'conf']
data['reparse'] = {'lines': [l for l in LINES if not l.startswith('Gone')], 'cols': cols,
                   'rows': [[r[c] for c in cols] for r in db.execute('SELECT * FROM files ORDER BY id')],
                   'parser_version': json.load(open(lib.settings_path))['parser_version']}
db.close()
shutil.rmtree(root, ignore_errors=True)

with open(os.path.join(out, 'core_parity.json'), 'w', encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False, indent=1)
print({k: len(v) for k, v in data.items()})
