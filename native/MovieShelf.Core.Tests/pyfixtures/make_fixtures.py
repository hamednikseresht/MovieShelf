import json, os, sys, shutil, tempfile
import os, sys; sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'app'))
from movieshelf.library import Library
from movieshelf import backup, fullbackup
out = sys.argv[1]
root = tempfile.mkdtemp()
os.makedirs(os.path.join(root, 'data'))
lib = Library(root)
db = lib.db
db.execute("INSERT INTO titles(tconst,type,title,en_title,year,rating,votes,poster,genres,runtime) VALUES (1,'movie','Amélie','Amélie',2001,8.3,800000,'x','Comedy,Romance',122)")
db.execute("INSERT INTO titles(tconst,type,title,en_title,year,rating,votes,poster,genres,runtime) VALUES (2,'movie','Heat','Heat',1995,8.3,700000,'x','Crime',170)")
for i, (raw, tc, man) in enumerate([('Amelie.2001.1080p.BluRay', 1, 0), ('Heat.1995.720p', 2, 1), ('Unknown.Thing.1999.720p', None, 0)], 1):
    db.execute("INSERT INTO files(raw,list_file,present,added,title,year,resolution,tconst,conf,manual,matched) VALUES (?,?,?,?,?,?,?,?,?,?,1)",
               (raw, 't.txt', 1, 'x', raw.split('.')[0], int([p for p in raw.split('.') if p.isdigit()][0]), '1080p', tc, 'manual' if man else 'exact', man))
db.commit()
lib.set_user('tt0000001', {'fav': 1, 'watched': 1, 'myrating': 9, 'notes': 'Très bon \u2013 "quoted", café'})
lib.set_user('tt0000002', {'watched': 1})
lib.save_view('Dramas 90s', {'F': {'q': 'heat', 'genre': ['Drama']}, 'sort': 'title', 'rev': False, 'group': 'none'})
lib.save_view('پدرخوانده', {'F': {'q': ''}})
lib.wish_add([{'title': 'Alien', 'year': 1979, 'source': 'check', 'note': 'blu-ray'}, {'title': 'Ça', 'year': 2017, 'tconst': 1396484}])
lib.wish_set(2, 'found')
lib.dismiss('dupes:tt0000001:1-2')
db.execute("INSERT INTO forced(raw,tconst) VALUES ('Future.Movie.2030', 5)")
db.execute("INSERT INTO fa_titles(tconst,title,source) VALUES (1,'آملی','user')")
db.execute("INSERT INTO fa_titles(tconst,title,source) VALUES (2,'گرما','reviewed')")
db.commit()
snap = backup.snapshot(lib)
with open(os.path.join(out, 'python_backup.json'), 'w', encoding='utf-8') as f:
    json.dump(snap, f, ensure_ascii=False, indent=1)
lists = os.path.join(root, 'lists'); os.makedirs(lists)
with open(os.path.join(lists, 'a.txt'), 'w', encoding='utf-8') as f: f.write('Heat.1995.720p\n')
with open(os.path.join(lib.poster_dir, 'tt0000001.jpg'), 'wb') as f: f.write(b'JPEGDATA')
with open(os.path.join(lib.people_dir, 'nm0000001.jpg'), 'wb') as f: f.write(b'PERSON')
lib.save_settings({'list_dir': lists, 'tmdb_key': 'SECRET-T', 'omdb_key': 'SECRET-O', 'media_dirs': [r'Z:\NoSuchMovies']})
# an unknown settings key must survive
s = json.load(open(lib.settings_path)); s['parser_version'] = 2; json.dump(s, open(lib.settings_path, 'w'))
fullbackup.save_full(lib, os.path.join(out, 'python_full.zip'), {'lists': True, 'posters': True, 'keys': True})
print(json.dumps({k: (len(v) if isinstance(v, list) else v) for k, v in snap.items()}))
lib.db.close()
shutil.rmtree(root, ignore_errors=True)
