"""Persian titles: spelling clean-up, a reviewed list that replaces titles written in Latin letters, and your own edits.

TMDB's Persian titles are written by volunteers. Roughly one in seven is Persian written in Latin letters ("Serghate Bozorge
Ghatar"), and many have Arabic letter forms, stray spaces before punctuation, or missing half-spaces. `normalize` fixes the
mechanical problems. `fa_reviewed.json` holds Persian-script versions of the Latin-letter titles. An edit made in the movie
window is stored as an override and always wins."""
import json
import os
import re
import sys

ZW = '‌'
PERSIAN = re.compile(r'[؀-ۿ]')
LATIN = re.compile(r'[A-Za-z]')
_DIGITS = str.maketrans('0123456789٠١٢٣٤٥٦٧٨٩', '۰۱۲۳۴۵۶۷۸۹۰۱۲۳۴۵۶۷۸۹')
_LETTERS = str.maketrans({'ي': 'ی', 'ى': 'ی', 'ك': 'ک', 'ة': 'ه', 'ە': 'ه', 'ۀ': 'ه' + ZW + 'ی'})
REVIEW_VERSION = 2  # bump when fa_reviewed.json changes so it is applied again


def has_persian(t):
    return bool(PERSIAN.search(t or ''))


def is_latin_only(t):
    return bool(t) and bool(LATIN.search(t)) and not PERSIAN.search(t)


def normalize(t):
    """Standard Persian spelling for a title: Persian letters and digits, punctuation, spaces and half-spaces."""
    if not t or not PERSIAN.search(t):
        return (t or '').strip() or None
    t = t.translate(_LETTERS)
    t = re.sub(r'[ً-ْـ]', '', t)                    # vowel marks and tatweel
    t = re.sub(r'(?<=\d),(?=\d)', '\u0001', t)                      # keep 1,000 style numbers
    t = t.replace(',', '،').replace('\u0001', ',').replace('?', '؟').replace(';', '؛')
    t = t.translate(_DIGITS) if not LATIN.search(t) else t           # a title that mixes in Latin words keeps its digits
    t = re.sub(r'\s*([،؛؟!])\s*', r'\1 ', t)                        # no space before, one after
    t = re.sub(r'\s*:\s*', ': ', t)
    t = re.sub(r'\(\s+', '(', t)
    t = re.sub(r'\s+\)', ')', t)
    # half-spaces: plural and comparative endings, the verb prefix mi- / nemi-, a trailing ye
    t = re.sub(r'(?<=[؀-ۿ]) (?=(?:ها|های|هایی|ترین|تر)(?:\s|$|[،؛؟!:.)]))', ZW, t)
    t = re.sub(r'(?<![؀-ۿ])(ن?می) (?=[؀-ۿ])', r'\1' + ZW, t)
    t = re.sub(r'(?<=ه) (?=ی(?:\s|$|[،؛؟!:.)]))', ZW, t)
    t = re.sub(ZW + r'{2,}', ZW, t)
    t = re.sub(r'\s*' + ZW + r'\s*', lambda m: ZW if re.search(r'[؀-ۿ]', m.string[max(0, m.start() - 1):m.start()]) else ' ', t)
    t = re.sub(r'\s{2,}', ' ', t).strip(' ' + ZW + ':-–')
    return t or None


def reviewed():
    """{tconst: Persian title} from fa_reviewed.json (next to this file, or inside the packaged program)."""
    base = os.path.join(getattr(sys, '_MEIPASS', os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), 'movieshelf')
    path = os.path.join(base, 'fa_reviewed.json')
    if not os.path.exists(path):
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'fa_reviewed.json')
    with open(path, encoding='utf-8') as f:
        return {int(k): v for k, v in json.load(f).items()}


def apply(lib):
    """Run once per REVIEW_VERSION: clean the spelling of every stored Persian title and add the reviewed replacements.
    Titles you edited yourself are never touched. Returns (cleaned, replaced)."""
    s = lib.settings()
    if s.get('fa_review_version') == REVIEW_VERSION:
        return 0, 0
    cleaned = replaced = 0
    with lib.lock:
        for r in lib.db.execute("SELECT tconst, fa_title FROM titles WHERE fa_title IS NOT NULL AND fa_title != ''").fetchall():
            new = normalize(r['fa_title'])
            if new != r['fa_title']:
                lib.db.execute('UPDATE titles SET fa_title=? WHERE tconst=?', (new, r['tconst']))
                cleaned += 1
        have = {r[0] for r in lib.db.execute('SELECT tconst FROM titles')}
        for tconst, title in reviewed().items():
            if tconst in have:
                cur = lib.db.execute("INSERT OR IGNORE INTO fa_titles(tconst, title, source) VALUES (?,?,'review')", (tconst, normalize(title)))
                replaced += cur.rowcount
        lib.db.commit()
    lib.save_settings({'fa_review_version': REVIEW_VERSION})
    lib.version += 1
    lib.log(f'Persian titles: {cleaned} respelled, {replaced} replaced by the reviewed list')
    return cleaned, replaced
