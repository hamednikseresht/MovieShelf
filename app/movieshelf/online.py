"""Online helpers: IMDb public search suggestions (posters, fallback matching)
and the optional TMDB API (plot, countries, languages, cast photos)."""
import http.client
import json
import threading
import time
import urllib.error
import urllib.parse
import urllib.request

UA = 'MovieShelf/1.0 (personal collection manager)'
SUGGEST = 'https://v3.sg.media-imdb.com/suggestion/{c}/{q}.json'
TMDB = 'https://api.themoviedb.org/3'
TMDB_IMG = 'https://image.tmdb.org/t/p/'


def get_json(url, headers=None, timeout=20, retries=2):
    h = {'User-Agent': UA, 'Accept': 'application/json'}
    h.update(headers or {})
    for attempt in range(retries + 1):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=h), timeout=timeout) as r:
                return json.load(r)
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return None
            if e.code == 429 and attempt < retries:
                time.sleep(2 + attempt * 3)
                continue
            if attempt == retries:
                raise
        except Exception:
            if attempt == retries:
                raise
        time.sleep(1 + attempt)


_local = threading.local()


def _fetch(url, headers, timeout=30):
    """GET over a connection this thread keeps open per host, so a burst of requests pays for one TLS handshake, not one each.
    Returns (status, body)."""
    u = urllib.parse.urlsplit(url)
    path = u.path + ('?' + u.query if u.query else '')
    conns = getattr(_local, 'conns', None)
    if conns is None:
        conns = _local.conns = {}
    for attempt in (0, 1):
        c = conns.get(u.netloc)
        fresh = c is None
        if fresh:
            c = conns[u.netloc] = http.client.HTTPSConnection(u.netloc, timeout=timeout)
        try:
            c.request('GET', path, headers=headers)
            r = c.getresponse()
            return r.status, r.read()
        except (http.client.HTTPException, OSError):
            c.close()
            conns.pop(u.netloc, None)
            if fresh or attempt:  # a kept connection the server closed is retried once on a new one
                raise


def get_bytes(url, timeout=30):
    status, body = _fetch(url, {'User-Agent': UA}, timeout)
    if status >= 400:
        raise urllib.error.HTTPError(url, status, 'HTTP %d' % status, None, None)
    return body


def imdb_suggest(query):
    q = urllib.parse.quote(query.strip().lower()[:80])
    if not q:
        return []
    d = get_json(SUGGEST.format(c=q[0] if q[0].isalnum() else 'x', q=q)) or {}
    out = []
    for x in d.get('d', []):
        if not str(x.get('id', '')).startswith('tt'):
            continue
        out.append({'tconst': int(x['id'][2:]), 'title': x.get('l'), 'year': x.get('y'),
                    'kind': x.get('q'), 'stars': x.get('s'),
                    'image': (x.get('i') or {}).get('imageUrl')})
    return out


def imdb_poster_url(tconst):
    tt = f'tt{tconst:07d}'
    d = get_json(SUGGEST.format(c='t', q=tt)) or {}
    for x in d.get('d', []):
        if x.get('id') == tt and x.get('i'):
            return x['i'].get('imageUrl')
    return None


def sized_amazon(url, width=400):
    """m.media-amazon.com images can be resized via the URL suffix."""
    if url and '._V1_' in url:
        return url.split('._V1_')[0] + f'._V1_QL80_UX{width}_.jpg'
    return url


class Tmdb:
    def __init__(self, key):
        key = (key or '').strip()
        self.bearer = key if len(key) > 40 else None
        self.key = None if self.bearer else key

    def _get(self, path, **params):
        if self.key:
            params['api_key'] = self.key
        url = f'{TMDB}{path}?{urllib.parse.urlencode(params)}'
        hdr = {'User-Agent': UA, 'Accept': 'application/json'}
        if self.bearer:
            hdr['Authorization'] = f'Bearer {self.bearer}'
        for attempt in range(3):
            self._pace()
            status, body = _fetch(url, hdr, 20)
            if status == 404:
                return None
            if status == 429 or status >= 500:
                if attempt == 2:
                    raise urllib.error.HTTPError(url, status, 'HTTP %d' % status, None, None)
                time.sleep(1 + attempt * 2)
                continue
            if status >= 400:
                raise urllib.error.HTTPError(url, status, 'HTTP %d' % status, None, None)
            return json.loads(body)

    _gate = threading.Lock()
    _next = [0.0]

    @classmethod
    def _pace(cls):
        """All threads together stay under about 50 requests a second, TMDB's limit."""
        with cls._gate:
            t = time.monotonic()
            wait = cls._next[0] - t
            cls._next[0] = max(t, cls._next[0]) + 0.02
        if wait > 0:
            time.sleep(wait)

    def check(self):
        return self._get('/configuration') is not None

    # roles of a person, from TMDB's credit lists, in the order they are shown
    @staticmethod
    def _role(job, dept):
        j = (job or '').lower()
        if j == 'director':
            return 'Director'
        if dept == 'Writing' or j in ('writer', 'screenplay', 'story', 'novel', 'author'):
            return 'Writer'
        if 'producer' in j:
            return 'Producer'
        if 'composer' in j or j in ('music', 'songs', 'original music composer'):
            return 'Composer'
        if 'photography' in j or 'cinematograph' in j:
            return 'Cinematographer'
        if 'editor' in j:
            return 'Editor'
        return job or dept or 'Other'

    def person(self, nconst):
        """A person from TMDB by IMDb id: photo, biography (English and Persian), dates and every movie credit.
        Returns None when TMDB does not know the person."""
        found = self._get(f'/find/nm{nconst:07d}', external_source='imdb_id') or {}
        res = found.get('person_results')
        if not res:
            return None
        pid = res[0]['id']
        d = self._get(f'/person/{pid}', language='en-US', append_to_response='combined_credits') or {}
        if not d:
            return None
        credits, seen = [], set()
        cc = d.get('combined_credits') or {}
        for c in cc.get('cast') or []:
            if c.get('media_type') == 'movie':
                credits.append(self._credit(c, 'Actor', c.get('character')))
        for c in cc.get('crew') or []:
            if c.get('media_type') == 'movie':
                credits.append(self._credit(c, self._role(c.get('job'), c.get('department')), c.get('job')))
        out = []
        for c in credits:  # one row per movie and role
            k = (c['id'], c['role'])
            if k not in seen:
                seen.add(k)
                out.append(c)
        out.sort(key=lambda c: (c['year'] or 0), reverse=True)
        return {'tmdb_id': pid, 'name': d.get('name'), 'biography': d.get('biography') or None,
                'birthday': d.get('birthday'), 'deathday': d.get('deathday'), 'place': d.get('place_of_birth'),
                'department': d.get('known_for_department'), 'aka': (d.get('also_known_as') or [])[:6],
                'homepage': d.get('homepage'), 'photo_path': d.get('profile_path'), 'credits': out}

    @staticmethod
    def _credit(c, role, detail):
        date = c.get('release_date') or ''
        return {'id': c.get('id'), 'title': c.get('title') or c.get('original_title'), 'year': int(date[:4]) if date[:4].isdigit() else None,
                'role': role, 'detail': detail or None, 'poster': c.get('poster_path'), 'votes': c.get('vote_count') or 0}

    def search_collection(self, name):
        """TMDB id of the collection with this name, or None."""
        res = (self._get('/search/collection', query=name) or {}).get('results') or []
        exact = [r for r in res if (r.get('name') or '').lower() == name.lower()]
        return (exact or res or [{}])[0].get('id')

    def collection(self, cid):
        """The parts of a collection: [{title, orig, year}] in release order."""
        d = self._get(f'/collection/{cid}') or {}
        parts = [{'title': p.get('title'), 'orig': p.get('original_title'),
                  'year': int(p['release_date'][:4]) if p.get('release_date') else None} for p in d.get('parts') or []]
        return sorted(parts, key=lambda p: p['year'] or 9999)

    def details(self, tconst, lang='en-US'):
        tt = f'tt{tconst:07d}'
        found = self._get(f'/find/{tt}', external_source='imdb_id') or {}
        for kind, res in (('movie', found.get('movie_results')), ('tv', found.get('tv_results'))):
            if res:
                tm_id = res[0]['id']
                d = self._get(f'/{kind}/{tm_id}', language=lang,
                              append_to_response='credits,translations')
                if not d:
                    return None
                return self._shape(kind, d)
        return {'tmdb_id': None}

    def _shape(self, kind, d):
        fa = None
        for t in (d.get('translations') or {}).get('translations', []):
            if t.get('iso_639_1') == 'fa':
                fa = (t.get('data') or {}).get('title') or (t.get('data') or {}).get('name')
                break
        cast = []
        for c in ((d.get('credits') or {}).get('cast') or [])[:20]:
            cast.append({'name': c.get('name'), 'ch': c.get('character'),
                         'img': TMDB_IMG + 'w185' + c['profile_path'] if c.get('profile_path') else None})
        countries = [c.get('name') for c in d.get('production_countries') or []] or d.get('origin_country') or []
        return {
            'tmdb_id': d.get('id'), 'kind': kind,
            'plot': d.get('overview') or None,
            'tagline': d.get('tagline') or None,
            'orig_lang': d.get('original_language'),
            'languages': [l.get('english_name') or l.get('name') for l in d.get('spoken_languages') or []],
            'countries': countries,
            'fa_title': fa,
            'poster_url': TMDB_IMG + 'w500' + d['poster_path'] if d.get('poster_path') else None,
            'backdrop_url': TMDB_IMG + 'w1280' + d['backdrop_path'] if d.get('backdrop_path') else None,
            'tmdb_cast': cast,
            'collection': (d.get('belongs_to_collection') or {}).get('name'),
            'budget': d.get('budget') or None,
            'revenue': d.get('revenue') or None,
        }
