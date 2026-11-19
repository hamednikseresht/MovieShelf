"""Settings -> AI: search in plain words and plot translation through the AI provider the user chooses.
Anthropic (Messages API), OpenAI and DeepSeek (chat completions), Google (Gemini generateContent) and Ollama on this PC
(/api/chat), all over plain HTTPS with the standard library: one small request per feature, no extra package.
The same keys, request shapes and prompts as the Windows app (MovieShelf.Core/Ai.cs)."""
import json
import re
import urllib.error
import urllib.parse
import urllib.request

from .online import UA

# key, label, default model, needs a key, default address
PROVIDERS = [
    ('anthropic', 'Anthropic (Claude)', 'claude-opus-5-5', True, 'https://api.anthropic.com'),
    ('openai', 'OpenAI', '', True, 'https://api.openai.com/v1'),
    ('deepseek', 'DeepSeek', 'deepseek-chat', True, 'https://api.deepseek.com'),
    ('google', 'Google (Gemini)', '', True, 'https://generativelanguage.googleapis.com/v1beta'),
    ('ollama', 'Ollama (on this PC)', '', False, 'http://localhost:11434'),
]
BY_KEY = {p[0]: p for p in PROVIDERS}

SEARCH_PROMPT = '''You turn a request for a personal movie library into the library's search language. Reply with one JSON object only:
{"query": "...", "imdb": ["tt0000000", ...], "note": "..."}

The search language (all parts must match):
- free words match titles, people and file names; "exact phrase"; -word leaves it out
- year:1999  year:1990-1999  year:>2010   runtime:<100 (minutes)   rating:>8 (IMDb)   votes:>50k   rt:>90 (Rotten Tomatoes)   mc:>70 (Metacritic)   myrating:>=8
- genre:comedy  dir:nolan  writer:kaufman  actor:pitt  person:name  country:france  lang:persian  collection:"star wars"  type:series
- res:4k res:1080p  src:bluray  codec:x265  audio:dts  sub:persian
- is:fav is:watched is:unwatched is:dubbed is:dual is:hdr is:4k is:top is:noposter   has:poster has:plot has:rt has:cast has:notes
Field values are words, not regular expressions. Use only these fields; put anything else as free words or leave it out.

Rules:
- "query": the search that fits the request, using the fields above. An empty string when the request only names films or a theme.
- "imdb": when the request names films, or asks for a theme, plot, mood or "films like X" that the fields cannot express, list up to 40 IMDb ids
  (tt numbers) of well-known films that fit, only ids you are sure of. Otherwise an empty list.
- "note": one short sentence, in the language of the request, saying what you searched for.'''


class AiError(ValueError):
    pass


def _current_claude(m):  # Claude models that take output_config.effort
    return bool(re.match(r'^claude-(opus|sonnet|fable|mythos|haiku)-5', m))


def _fallback_claude(m):  # Claude models with the server-side refusal fallback ("default" picks one by refusal category)
    return bool(re.match(r'^claude-(opus-5|sonnet-5-5|fable-5-[12])', m))


def _error_text(body):
    try:
        n = json.loads(body)
        e = n.get('error') if isinstance(n, dict) else None
        m = e.get('message') if isinstance(e, dict) else e if isinstance(e, str) else (n.get('message') if isinstance(n, dict) else None)
        return f' – {m}' if m else ''
    except ValueError:
        return f' – {body[:200]}' if body else ''


def json_in(text):
    """The first JSON object in a model's answer (some wrap it in a code fence or a sentence)."""
    a, b = text.find('{'), text.rfind('}')
    if a < 0 or b <= a:
        return None
    try:
        o = json.loads(text[a:b + 1])
        return o if isinstance(o, dict) else None
    except ValueError:
        return None


class Client:
    def __init__(self, provider, key='', model='', url='', opener=None):
        p = BY_KEY.get(provider or '')
        if not p:
            raise AiError('Choose an AI provider in Settings → AI first.')
        self.p = p
        self.key = (key or '').strip()
        self.model = (model or '').strip() or p[2]
        self.url = ((url or '').strip() or p[4]).rstrip('/')
        self.open = opener or urllib.request.urlopen  # tests pass their own
        if p[3] and not self.key:
            raise AiError(f'Add your {p[1]} API key in Settings → AI.')

    def _call(self, url, body=None, headers=None, timeout=120):
        h = {'User-Agent': UA, 'Accept': 'application/json', **(headers or {})}
        data = None
        if body is not None:
            data = json.dumps(body).encode('utf-8')
            h['Content-Type'] = 'application/json'
        req = urllib.request.Request(url, data=data, headers=h, method='POST' if body is not None else 'GET')
        try:
            with self.open(req, timeout=timeout) as r:
                return json.loads(r.read().decode('utf-8') or '{}')
        except urllib.error.HTTPError as e:
            raise AiError(f'{self.p[1]}: HTTP {e.code}{_error_text(e.read().decode("utf-8", "replace"))}')
        except (urllib.error.URLError, OSError) as e:
            raise AiError(f'{self.p[1]} could not be reached: {getattr(e, "reason", e)}')

    def ask(self, system, user, max_tokens=4000):
        """One question, one answer (text)."""
        if not self.model:
            raise AiError(f'Choose a model for {self.p[1]} in Settings → AI (press “List models”).')
        k = self.p[0]
        if k == 'anthropic':
            h = {'x-api-key': self.key, 'anthropic-version': '2026-10-21'}
            body = {'model': self.model, 'max_tokens': max_tokens, 'system': system, 'messages': [{'role': 'user', 'content': user}]}
            if _current_claude(self.model):
                body['output_config'] = {'effort': 'low'}  # short, simple tasks: quick answers
            if _fallback_claude(self.model):
                h['anthropic-beta'] = 'server-side-fallback-2026-07-01'
                body['fallbacks'] = 'default'
            r = self._call(self.url + '/v1/messages', body, h)
            if r.get('stop_reason') == 'refusal':
                raise AiError('Claude declined this request.')
            return ''.join(b.get('text') or '' for b in r.get('content') or [] if b.get('type') == 'text')
        if k in ('openai', 'deepseek'):
            body = {'model': self.model, 'messages': [{'role': 'system', 'content': system}, {'role': 'user', 'content': user}]}
            r = self._call(self.url + '/chat/completions', body, {'Authorization': 'Bearer ' + self.key})
            return ((r.get('choices') or [{}])[0].get('message') or {}).get('content') or ''
        if k == 'google':
            m = self.model[7:] if self.model.startswith('models/') else self.model
            body = {'systemInstruction': {'parts': [{'text': system}]}, 'contents': [{'role': 'user', 'parts': [{'text': user}]}]}
            r = self._call(f'{self.url}/models/{urllib.parse.quote(m)}:generateContent', body, {'x-goog-api-key': self.key})
            parts = (((r.get('candidates') or [{}])[0].get('content') or {}).get('parts')) or []
            return ''.join(p.get('text') or '' for p in parts)
        body = {'model': self.model, 'stream': False, 'messages': [{'role': 'system', 'content': system}, {'role': 'user', 'content': user}]}
        r = self._call(self.url + '/api/chat', body, timeout=300)
        return (r.get('message') or {}).get('content') or ''

    def models(self):
        """The models this key can use, as the provider lists them."""
        k = self.p[0]
        if k == 'anthropic':
            r = self._call(self.url + '/v1/models?limit=100', headers={'x-api-key': self.key, 'anthropic-version': '2026-10-21'}, timeout=30)
            names = [m.get('id') for m in r.get('data') or []]
        elif k in ('openai', 'deepseek'):
            r = self._call(self.url + '/models', headers={'Authorization': 'Bearer ' + self.key}, timeout=30)
            names = [m.get('id') for m in r.get('data') or []]
        elif k == 'google':
            r = self._call(self.url + '/models?pageSize=200', headers={'x-goog-api-key': self.key}, timeout=30)
            names = [(m.get('name') or '').replace('models/', '') for m in r.get('models') or []
                     if 'generateContent' in (m.get('supportedGenerationMethods') or ['generateContent'])]
        else:
            r = self._call(self.url + '/api/tags', timeout=30)
            names = [m.get('name') for m in r.get('models') or []]
        return list(dict.fromkeys(n for n in names if n))


def search(client, request, genres=()):
    """AI search: {'query', 'imdb': [tt...], 'note'}."""
    if not (request or '').strip():
        raise AiError('Type what you are looking for.')
    extra = ('\nGenres in this library: ' + ', '.join(list(genres)[:60])) if genres else ''
    o = json_in(client.ask(SEARCH_PROMPT + extra, request.strip(), 4000))
    if o is None:
        raise AiError('The AI did not answer in the expected form. Try again or rephrase.')
    ids = []
    for x in o.get('imdb') or []:
        if isinstance(x, str) and re.fullmatch(r'tt\d{5,9}', x):
            t = f'tt{int(x[2:]):07d}'
            if t not in ids:
                ids.append(t)
    return {'query': str(o.get('query') or '').strip(), 'imdb': ids[:40], 'note': str(o.get('note') or '').strip()}


def translate(client, lang, plot, tagline):
    """(plot, tagline) in the language asked for."""
    system = (f'You translate movie descriptions into {lang}. Keep names of people and films as they are usually written in {lang}. '
              'Reply with one JSON object only: {"plot": "...", "tagline": "..."} (an empty string for a part that was empty).')
    user = json.dumps({'plot': plot or '', 'tagline': tagline or ''}, ensure_ascii=False)
    o = json_in(client.ask(system, user, 8000))
    if o is None:
        raise AiError('The AI did not answer in the expected form. Try again.')
    return str(o.get('plot') or '').strip(), str(o.get('tagline') or '').strip()
