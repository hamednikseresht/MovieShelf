"""Parse release-style movie names like
'13.Minutes.(Elser).2015.720p.Bluray.Ganool' into title / year / quality info."""
import re
import unicodedata

YEAR_RE = re.compile(r'(?<![0-9])(18[89][0-9]|19[0-9]{2}|20[0-4][0-9])(?![0-9]|p\b)')

RES_MAP = [
    (re.compile(r'2160p|4k|uhd', re.I), '2160p'),
    (re.compile(r'1080p?|10800p|fullhd', re.I), '1080p'),
    (re.compile(r'720p?', re.I), '720p'),
    (re.compile(r'576p', re.I), '576p'),
    (re.compile(r'480p', re.I), '480p'),
]
SOURCE_MAP = [
    ('bluray', 'BluRay'), ('blu-ray', 'BluRay'), ('bdrip', 'BluRay'), ('brrip', 'BluRay'),
    ('web-dl', 'WEB-DL'), ('webdl', 'WEB-DL'), ('web.dl', 'WEB-DL'), ('web dl', 'WEB-DL'),
    ('webrip', 'WEBRip'), ('hdtv', 'HDTV'), ('dvdrip', 'DVDRip'), ('dvdscr', 'DVDRip'),
    ('hdrip', 'HDRip'), ('web', 'WEB-DL'),
]
EDITIONS = [
    (r'director\'?s[ .]?cut|\bdc\b', "Director's Cut"), (r'extended', 'Extended'),
    (r'remastered', 'Remastered'), (r'restored', 'Restored'), (r'criterion', 'Criterion'),
    (r'unrated', 'Unrated'), (r'uncut', 'Uncut'), (r'imax', 'IMAX'),
    (r'special[ .]edition', 'Special Edition'), (r'colori[sz]ed', 'Colorized'),
    (r'redux', 'Redux'), (r'theatrical', 'Theatrical'), (r'final[ .]cut', 'Final Cut'),
]
# Tokens that mark the end of the title when no year is present
STOP_TOKENS = {
    '2160p', '1080p', '720p', '576p', '480p', '1080', '720', 'farsi', 'dubbed', 'dvdrip', 'bluray',
    'brrip', 'webrip', 'web-dl', 'webdl', 'hdtv', 'x264', 'x265', 'hevc', '10bit', 'web', 'hdrip',
    'bdrip', 'extended', 'remastered', 'unrated', 'dual', 'softsub', 'yify', 'tinymoviez', 'ganool',
    '512kb', 'repack', 'proper', 'limited', 'fixed', 'subbed',
}


def _clean_title(s):
    s = s.replace('_', ' ')
    s = re.sub(r'\.\.+', ': ', s)               # 'Movie..Subtitle' -> 'Movie: Subtitle'
    # keep dots inside numbers / initials like 10.000 or L.A.
    s = re.sub(r'(?<=\d)\.(?=\d)', '\x00', s)
    s = re.sub(r'(?<=\b[A-Za-z])\.(?=[A-Za-z]\b)', '\x01', s)
    s = s.replace('.', ' ').replace('\x00', '.').replace('\x01', '.')
    s = re.sub(r'\s+', ' ', s).strip(' -:')
    return s


def parse(raw):
    name = raw.strip()
    info = {'raw': name}
    # alt title in parens before the year: Title.(Alt Title).2014
    years = list(YEAR_RE.finditer(name))
    # title must be non-empty, so ignore a year that starts the string unless it's the only one
    years = [m for m in years if m.start() > 0] or years
    year_m = None
    if years:
        # last year-like token that appears before the first quality marker
        low = name.lower()
        qpos = len(name)
        for m in re.finditer(r'(?<![a-z0-9])(2160p|1080p?|720p?|480p|576p|dvdrip|web|bluray|brrip|hdtv|x26[45])', low):
            if m.start() > 0:
                qpos = m.start()
                break
        before = [m for m in years if m.start() < qpos]
        year_m = (before or years)[-1]
    if year_m and year_m.start() > 0:
        head, tail = name[:year_m.start()], name[year_m.end():]
        info['year'] = int(year_m.group(1))
    else:
        head, tail = name, ''
        tokens = re.split(r'(?=[.\s(])', name)
        cut = len(name)
        pos = 0
        for t in tokens:
            tt = t.strip('. (').lower()
            if tt in STOP_TOKENS and pos > 0:
                cut = pos
                break
            pos += len(t)
        head, tail = name[:cut], name[cut:]
        info['year'] = int(year_m.group(1)) if year_m else None

    # quality tags placed before the year: 'Title.Farsi.Dubbed.1998' / 'Title.720p.1957'
    parts = re.split(r'(?<=[.\s])', head)
    while parts and parts[-1].strip('. ').lower() in STOP_TOKENS | {''} and len(parts) > 1:
        parts.pop()
    head = ''.join(parts)
    alts = re.findall(r'\(([^()]*)\)', head)
    head_noparen = re.sub(r'\([^()]*\)', ' ', head)
    info['title'] = _clean_title(head_noparen)
    info['alt_title'] = _clean_title(alts[0]) if alts else None
    aka = re.split(r'\s+(?:AKA|aka|a\.k\.a)\s+', info['title'], maxsplit=1)
    if len(aka) == 2 and aka[0] and aka[1]:
        info['title'], info['alt_title'] = aka[0].strip(), info['alt_title'] or aka[1].strip()
    if not info['title'] and info['alt_title']:
        info['title'], info['alt_title'] = info['alt_title'], None

    q = (name[len(head):] or name).lower()
    qn = ' ' + re.sub(r'[._()\[\]]', ' ', q) + ' '
    info['resolution'] = None
    for rx, label in RES_MAP:
        if rx.search(q):
            info['resolution'] = label
            break
    if not info['resolution'] and re.search(r'dvd|480|xvid|divx|512kb', q):
        info['resolution'] = 'SD'
    info['source'] = None
    for key, label in SOURCE_MAP:
        if key in q:
            info['source'] = label
            break
    info['codec'] = 'x265' if re.search(r'x265|hevc|h\.?265', q) else ('x264' if re.search(r'x264|h\.?264|avc', q) else None)
    info['bit10'] = bool(re.search(r'10bit', q))
    info['hdr'] = bool(re.search(r'\bhdr', qn))
    ch = None
    if re.search(r'8ch|(?<![0-9])7\.1(?![0-9])', q):
        ch = '7.1'
    elif re.search(r'6ch|(?<![0-9])5\.1(?![0-9])|dts|ac3|dd5', q):
        ch = '5.1'
    elif re.search(r'2ch', q):
        ch = '2.0'
    info['audio'] = ch
    info['farsi_dubbed'] = bool(re.search(r'farsi|persian|duble|dooble|\bdubbed', qn)) or 'dubbed' in q
    info['dual_audio'] = bool(re.search(r'dual|2audio', q))
    info['softsub'] = bool(re.search(r'softsub|subbed', q))
    eds = []
    for pat, label in EDITIONS:
        if re.search(pat, q, re.I) and label not in eds:
            eds.append(label)
    info['edition'] = ', '.join(eds) or None
    grp = re.findall(r'[A-Za-z0-9&]+', tail)
    bad = r'\d+p?|\d|\d?ch|x26[45]|dl|web|dvdrip|bluray|brrip|webrip|hdtv|hdrip|bdrip|dubbed|farsi|10bit|hevc|ac3|aac|dts|softsub|subbed|remastered|extended|repack|proper|fixed|cut|edition|unrated|imax|hdr'
    info['group'] = grp[-1] if grp and not re.fullmatch(bad, grp[-1], re.I) else None
    return info


# ---------- normalization for title matching ----------

def strip_accents(s):
    s = unicodedata.normalize('NFKD', s)
    s = ''.join(c for c in s if not unicodedata.combining(c))
    return s.replace('ß', 'ss').replace('æ', 'ae').replace('ø', 'o').replace('œ', 'oe').replace('ł', 'l').replace('đ', 'd')


_APOS = re.compile(r"[’'`´]")
_NUMSEP = re.compile(r'(?<=\d)[.,:](?=\d)')
_NONALNUM = re.compile(r'[^0-9a-z؀-ۿЀ-ӿͰ-Ͽ぀-ヿ一-鿿가-힯]+')


def norm(s):
    """Spaced normalized form: 'Zatoichi's Cane-Sword' -> 'zatoichis cane sword'."""
    if not s:
        return ''
    s = strip_accents(s).lower()
    s = s.replace('&', ' and ')
    s = _APOS.sub('', s)
    s = _NUMSEP.sub('', s)
    s = _NONALNUM.sub(' ', s)
    return ' '.join(s.split())


def key(s):
    """Compact match key without spaces."""
    return norm(s).replace(' ', '')


EDITION_WORDS = re.compile(
    r'\b(theatrical|directors cut|director s cut|dircut|dc|uncut|unrated|redux|extended|remastered|'
    r'special edition|criterion collection|criterion|colorized|final cut|imax|edition|cut)\b')


def query_keys(title):
    """All keys worth trying for a parsed title."""
    keys = []
    n = norm(title)
    if not n:
        return keys
    keys.append(n.replace(' ', ''))
    stripped = ' '.join(EDITION_WORDS.sub(' ', n).split())
    if stripped and stripped != n:
        keys.append(stripped.replace(' ', ''))
        n = stripped
    w = n.split()
    # drop / move leading article ('the', 'a', 'an') which some releases omit
    if w and w[0] in ('the', 'a', 'an', 'le', 'la', 'les', 'il', 'el', 'der', 'die', 'das') and len(w) > 1:
        keys.append(''.join(w[1:]))
    if ' and ' in f' {n} ':
        keys.append(n.replace(' and ', ' ').replace(' ', ''))
    out = []
    for k in keys:
        if k and k not in out:
            out.append(k)
    return out
