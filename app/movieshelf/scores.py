"""Critic scores: Rotten Tomatoes (Tomatometer) and Metacritic (Metascore).

Primary source is Wikidata (free, no key, bulk queries by IMDb id). The optional
OMDb key (omdbapi.com, free tier 1000 requests/day) fills titles Wikidata lacks."""
import json
import re
import urllib.error
import urllib.parse
import urllib.request

from .online import UA, get_json

WIKIDATA = 'https://query.wikidata.org/sparql'
RT, MC = 'Q105584', 'Q150248'          # review score by: Rotten Tomatoes, Metacritic
TOMATOMETER = 'Q108403393'             # determination method: Tomatometer score
OMDB = 'https://www.omdbapi.com/'


class LimitReached(Exception):
    pass


def wikidata_scores(tconsts):
    """{tconst: {'rt': int %, 'mc': int /100}} for a batch (keep batches <= 300)."""
    ids = ' '.join(f'"tt{t:07d}"' for t in tconsts)
    q = f'''SELECT ?imdb ?score ?by ?method ?date WHERE {{
      VALUES ?imdb {{ {ids} }}
      ?item wdt:P345 ?imdb ; p:P444 ?st .
      ?st ps:P444 ?score ; pq:P447 ?by .
      OPTIONAL {{ ?st pq:P459 ?method }}
      OPTIONAL {{ ?st pq:P585 ?date }}
      FILTER(?by IN (wd:{RT}, wd:{MC}))
    }}'''
    body = urllib.parse.urlencode({'query': q, 'format': 'json'}).encode()
    req = urllib.request.Request(WIKIDATA, data=body, headers={
        'User-Agent': UA, 'Accept': 'application/sparql-results+json',
        'Content-Type': 'application/x-www-form-urlencoded'})
    with urllib.request.urlopen(req, timeout=90) as r:
        rows = json.load(r)['results']['bindings']
    best = {}  # (tconst, kind) -> (date, value)
    for b in rows:
        v = lambda k: b[k]['value'] if k in b else ''
        t = int(v('imdb')[2:])
        by = v('by').rsplit('/', 1)[-1]
        method = v('method').rsplit('/', 1)[-1]
        score = v('score').strip()
        if by == RT:
            m = re.fullmatch(r'(\d{1,3})\s*%', score)
            if not m or (method and method != TOMATOMETER):
                continue  # skip audience score / average rating
            kind, val = 'rt', int(m.group(1))
        else:
            m = re.fullmatch(r'(\d{1,3})\s*/\s*100', score) or re.fullmatch(r'(\d{1,3})', score)
            if not m:
                continue
            kind, val = 'mc', int(m.group(1))
        if val > 100:
            continue
        date = v('date')
        if (t, kind) not in best or date > best[(t, kind)][0]:
            best[(t, kind)] = (date, val)
    out = {}
    for (t, kind), (_, val) in best.items():
        out.setdefault(t, {})[kind] = val
    return out


def omdb_scores(key, tconst):
    try:
        d = get_json(f'{OMDB}?' + urllib.parse.urlencode({'i': f'tt{tconst:07d}', 'apikey': key}), retries=1) or {}
    except urllib.error.HTTPError as e:
        if e.code == 401:  # OMDb answers 401 for a bad key and for the daily limit
            body = e.read().decode('utf-8', 'replace').lower()
            raise LimitReached(body) if 'limit' in body else PermissionError('Invalid OMDb API key')
        raise
    if d.get('Response') == 'False':
        err = d.get('Error', '')
        if 'limit' in err.lower():
            raise LimitReached(err)
        if 'invalid api key' in err.lower():
            raise PermissionError(err)
        return {}
    out = {}
    for r in d.get('Ratings') or []:
        if r.get('Source') == 'Rotten Tomatoes':
            m = re.match(r'(\d+)%', r.get('Value', ''))
            if m:
                out['rt'] = int(m.group(1))
    ms = d.get('Metascore')
    if ms and ms.isdigit():
        out['mc'] = int(ms)
    return out


def omdb_check(key):
    try:
        d = get_json(f'{OMDB}?' + urllib.parse.urlencode({'i': 'tt0111161', 'apikey': key}), retries=0) or {}
    except urllib.error.HTTPError as e:
        if e.code == 401:
            return False, 'Invalid API key'
        raise
    if d.get('Response') == 'True':
        return True, ''
    return False, d.get('Error', 'Key rejected')
