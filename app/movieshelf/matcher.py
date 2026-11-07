"""Match parsed release names to IMDb titles: the offline index first (exact title keys,
fuzzy spelling within the year, English + original names glued together), then the
IMDb online suggestion search as a fallback."""
import math
import sqlite3
import time

from rapidfuzz import fuzz, process

from . import imdb_index, online
from .parse import key, norm, query_keys

# match confidences that need a human check
UNSURE = ('review', 'fuzzy', 'online', 'noyear', 'none')


def reason(conf, fyear, tyear):
    if conf == 'none':
        return 'Not found on IMDb — the name may be misspelled, or the year in the name is wrong'
    if conf == 'fuzzy':
        return 'Name is spelled differently on IMDb'
    if conf == 'noyear':
        return 'No year in the name — several movies may share this title'
    if conf == 'online':
        return 'Only found by online search'
    if conf == 'review':
        if fyear and tyear and fyear != tyear:
            return f'Year in the name ({fyear}) differs from IMDb ({tyear})'
        return 'Several titles share this name and year'
    return ''


class Matcher:
    def __init__(self, index_path):
        self.index_path = index_path
        self._fuzzy_cache = {}  # year -> (keys, ids) of that year's titles

    def reset(self):
        """Forget cached index data (after the index is rebuilt)."""
        self._fuzzy_cache = {}

    def open(self):
        """A connection to the offline IMDb index (close it when done)."""
        c = sqlite3.connect(self.index_path)
        c.row_factory = sqlite3.Row
        return c

    @staticmethod
    def _score(row, year, src):
        s = 0.0
        if year and row['year']:
            d = abs(year - row['year'])
            s += {0: 60, 1: 35, 2: 5}.get(d, -200)
        elif year and not row['year']:
            s -= 20
        s += {0: 25, 1: 15, 2: 8, 3: 8, 4: 0, 5: 0, 6: 2, 7: 0}.get(row['type'], 0)
        if row['type'] == 4 and year and year < 1915:
            s += 25  # early cinema is mostly shorts
        s += 12 if src == 0 else 0
        s += math.log10((row['votes'] or 0) + 1) * 6
        return s

    def candidates(self, ix, p, limit=8):
        keys = []
        for t in (p['title'], p['alt_title']):
            if t:
                keys += [k for k in query_keys(t) if k not in keys]
        if not keys:
            return []
        full = {query_keys(t)[0] for t in (p['title'], p['alt_title']) if t and query_keys(t)}
        q = ','.join('?' * len(keys))
        rows = ix.execute(
            f'SELECT t.*, min(k.src) AS src, group_concat(k.k) AS hit FROM keys k JOIN titles t ON t.id = k.id '
            f'WHERE k.k IN ({q}) GROUP BY t.id', keys).fetchall()
        # the exact full title beats a variant (article dropped, edition words removed)
        out = [(self._score(r, p['year'], r['src']) + (15 if full & set(r['hit'].split(',')) else 0), r, 'key')
               for r in rows]
        out.sort(key=lambda x: -x[0])
        return out[:limit]

    def split_candidates(self, ix, p):
        """Names like 'Great.Freedom.Grosse.Freiheit.2021' glue the English and original
        title together: try word prefixes / suffixes / ':' parts with an exact year."""
        if not p['year'] or not p['title']:
            return []
        parts = [s for s in p['title'].split(':') if s.strip()]
        words = norm(p['title']).split()
        keys = {key(s) for s in parts if len(parts) > 1}
        for i in range(1, len(words)):
            pre, suf = words[:i], words[i:]
            if len(''.join(pre)) >= 5:
                keys.add(''.join(pre))
            if len(''.join(suf)) >= 5:
                keys.add(''.join(suf))
        keys.discard('')
        if not keys:
            return []
        q = ','.join('?' * len(keys))
        # '+t.year' makes SQLite look up by key, not scan the whole year
        rows = ix.execute(
            f'SELECT t.*, min(k.src) AS src, max(length(k.k)) AS klen FROM keys k JOIN titles t ON t.id = k.id '
            f'WHERE k.k IN ({q}) AND +t.year = ? AND t.type <= 6 GROUP BY t.id', list(keys) + [p['year']]).fetchall()
        total = len(''.join(words))
        # prefer the candidate that covers most of the name
        out = [(self._score(r, p['year'], r['src']) - 30 * (1 - r['klen'] / total), r, 'split') for r in rows]
        out.sort(key=lambda x: -x[0])
        return out[:5]

    def _fuzzy_bucket(self, ix, year):
        if year not in self._fuzzy_cache:
            rows = ix.execute('SELECT k.k, k.id FROM titles t JOIN keys k ON k.id = t.id '
                              'WHERE t.year = ? AND t.type <= 6', (year,)).fetchall()
            self._fuzzy_cache[year] = ([r[0] for r in rows], [r[1] for r in rows])
            if len(self._fuzzy_cache) > 400:
                self._fuzzy_cache.pop(next(iter(self._fuzzy_cache)))
        return self._fuzzy_cache[year]

    def fuzzy(self, ix, p, limit=5, cutoff=84):
        if not p['year']:
            return []
        qkeys = [k for t in (p['title'], p['alt_title']) if t for k in query_keys(t)[:1]]
        found = {}
        for y in (p['year'], p['year'] - 1, p['year'] + 1):
            keys, ids = self._fuzzy_bucket(ix, y)
            if not keys:
                continue
            for qk in qkeys:
                if len(qk) < 4:
                    continue
                for _, sc, idx in process.extract(qk, keys, scorer=fuzz.ratio, limit=limit, score_cutoff=cutoff):
                    found[ids[idx]] = max(found.get(ids[idx], 0), sc)
        out = []
        for i, sc in found.items():
            r = ix.execute('SELECT *, 1 AS src FROM titles WHERE id=?', (i,)).fetchone()
            out.append((self._score(r, p['year'], 1) + (sc - 100) * 2, r, f'fuzzy {sc:.0f}'))
        out.sort(key=lambda x: -x[0])
        return out[:limit]

    def pick(self, ix, p):
        c = self.candidates(ix, p)
        if c and (not p['year'] or c[0][0] > 0):
            best = c[0]
            r = best[1]
            if not p['year']:
                conf = 'noyear'
            elif r['year'] == p['year'] and r['src'] == 0:
                conf = 'exact'
            else:
                conf = 'good'
            # an ambiguous tie between two strong candidates needs review
            if len(c) > 1 and c[1][0] > best[0] - 3 and c[1][1]['year'] == r['year']:
                conf = 'review'
            return r['id'], conf, best[0]
        f = self.fuzzy(ix, p)
        if f and f[0][0] > 0:
            return f[0][1]['id'], 'fuzzy', f[0][0]
        s = self.split_candidates(ix, p)
        if s and s[0][0] > 40:
            return s[0][1]['id'], 'fuzzy', s[0][0]
        # exact title but IMDb's year differs a lot (IMDb dates some films by production year)
        near = [x for x in c if x[1]['year'] and abs(x[1]['year'] - p['year']) <= 6 and (x[1]['votes'] or 0) >= 1000]
        if near:
            near.sort(key=lambda x: -(x[1]['votes'] or 0))
            return near[0][1]['id'], 'review', near[0][0]
        return None, 'none', None

    @staticmethod
    def online_find(f):
        """IMDb suggestion search for one parsed name; raises when offline."""
        tries = []
        for t in (f['title'], f['alt_title']):
            if t:
                if f['year']:
                    tries.append((t, f'{t} {f["year"]}'))
                tries.append((t, t))
        for t, q in tries:
            for r in online.imdb_suggest(q):
                if r['kind'] in ('TV episode', 'video game', 'podcastSeries'):
                    continue
                rk, tk = key(r['title'] or ''), key(t)
                sim = fuzz.ratio(rk, tk)
                prefix = len(tk) >= 8 and rk.startswith(tk)  # 'Lady and the Tramp II' -> '...: Scamp's Adventure'
                if f['year']:
                    ok = bool(r['year']) and abs(r['year'] - f['year']) <= 1 and (sim >= 82 or prefix)
                else:
                    ok = sim >= 90
                if ok:
                    return r
            time.sleep(0.15)
        return None

    @staticmethod
    def cand_dicts(rows, skip=()):
        out, seen = [], set(skip)
        for sc, r, how in rows:
            if r['id'] in seen:
                continue
            seen.add(r['id'])
            out.append({'tconst': r['id'], 'title': r['title'], 'orig': r['orig'] if r['orig'] != r['title'] else None,
                        'year': r['year'], 'type': imdb_index.TYPE_NAMES.get(r['type']), 'votes': r['votes'],
                        'rating': r['rating']})
        return out

    def all_cands(self, ix, p, skip=(), n=4):
        rows = self.candidates(ix, p) + self.split_candidates(ix, p)
        if p.get('conf') in (None, 'none', 'fuzzy'):  # fuzzy search is the slow part: only where spelling is the issue
            rows += self.fuzzy(ix, p, cutoff=78)
        rows.sort(key=lambda x: -x[0])
        return self.cand_dicts(rows, skip)[:n]
