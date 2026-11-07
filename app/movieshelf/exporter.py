"""Export movies to Excel (.xlsx, written with the standard library only) or CSV.

The page sends the movie keys (in the order it shows them), the wanted columns and the layout;
this module turns them into a file. One row per movie, or one row per file."""
import csv
import io
import re
import secrets
import time
import zipfile
from xml.sax.saxutils import escape

TYPE_LABEL = {'movie': 'Movie', 'tvMovie': 'TV movie', 'video': 'Video', 'short': 'Short', 'tvShort': 'TV short',
              'tvSpecial': 'TV special', 'tvMiniSeries': 'Mini-series', 'tvSeries': 'TV series'}
CONF_LABEL = {'exact': 'Exact match', 'good': 'Good match', 'manual': 'Set by you', 'online': 'Online match (check)',
              'noyear': 'No year in name (check)', 'review': 'Ambiguous (check)', 'fuzzy': 'Fuzzy match (check)',
              'none': 'Not found on IMDb'}


def _names(lst, i=1, limit=None):
    return ', '.join(str(p[i]) for p in (lst or [])[:limit] if len(p) > i and p[i])


def _uniq(files, k):
    seen = []
    for f in files:
        v = f.get(k)
        if v and v not in seen:
            seen.append(v)
    return ', '.join(str(v) for v in seen)


def _yn(v):
    return 'Yes' if v else 'No'


def _quality(f):
    bits = [f.get('res'), f.get('src'), f.get('codec'), '10-bit' if f.get('b10') else '', 'HDR' if f.get('hdr') else '',
            f.get('audio'), 'Farsi dubbed' if f.get('fa') else '', 'Dual audio' if f.get('dual') else '',
            'Softsub' if f.get('sub') else '', f.get('ed')]
    return ' '.join(str(b) for b in bits if b)


def _fval(key):
    """A file field: that file's value per file row, the distinct values joined per movie row."""
    return lambda m, f: f.get(key) if f else _uniq(m['files'], key)


def _fflag(key):
    return lambda m, f: _yn(f.get(key)) if f else _yn(any(x.get(key) for x in m['files']))


def _minutes(sec):
    return round(sec / 60) if sec else None


def _picture(f):
    return f'{f["w"]}x{f["h"]}' if f.get('w') and f.get('h') else None


def _langs(m, f, key):
    files = [f] if f else m['files']
    out = []
    for x in files:
        for v in x.get(key) or []:
            if v not in out:
                out.append(v)
    return ', '.join(out) or None


def _gb(n):
    return round(n / 1e9, 2) if n else None


def _tt_link(m, f):
    return m['tt'] if str(m.get('tt', '')).startswith('tt') else None


# key, label, group, width, kind, on by default, getter(movie, file or None)
COLUMNS = [
    ('en', 'Title', 'Basic', 34, 'text', 1, lambda m, f: m.get('en') or m.get('title')),
    ('year', 'Year', 'Basic', 8, 'int', 1, lambda m, f: m.get('year')),
    ('orig', 'Original title', 'Basic', 30, 'text', 1, lambda m, f: m.get('orig')),
    ('fa_t', 'Persian title', 'Basic', 26, 'text', 0, lambda m, f: m.get('fa_t')),
    ('imdb_title', 'IMDb title', 'Basic', 30, 'text', 0, lambda m, f: m.get('title')),
    ('imdb', 'IMDb id (link)', 'Basic', 13, 'link', 1, _tt_link),
    ('type', 'Type', 'Basic', 12, 'text', 0, lambda m, f: TYPE_LABEL.get(m.get('type'), m.get('type'))),
    ('runtime', 'Runtime (min)', 'Basic', 10, 'int', 0, lambda m, f: m.get('runtime')),
    ('genres', 'Genres', 'Basic', 28, 'text', 1, lambda m, f: ', '.join(m.get('genres') or [])),
    ('countries', 'Countries', 'Basic', 24, 'text', 0, lambda m, f: ', '.join(m.get('countries') or [])),
    ('langs', 'Languages', 'Basic', 20, 'text', 0, lambda m, f: ', '.join(m.get('langs') or [])),
    ('coll', 'Collection', 'Basic', 26, 'text', 0, lambda m, f: m.get('coll')),
    ('plot', 'Plot', 'Basic', 70, 'wrap', 0, lambda m, f: m.get('plot')),
    ('tagline', 'Tagline', 'Basic', 36, 'text', 0, lambda m, f: m.get('tagline')),
    ('rating', 'IMDb rating', 'Ratings', 11, 'dec', 1, lambda m, f: m.get('rating')),
    ('votes', 'IMDb votes', 'Ratings', 12, 'int', 0, lambda m, f: m.get('votes')),
    ('rt', 'Rotten Tomatoes %', 'Ratings', 12, 'int', 0, lambda m, f: m.get('rt')),
    ('mc', 'Metacritic', 'Ratings', 11, 'int', 0, lambda m, f: m.get('mc')),
    ('dir', 'Directors', 'People', 28, 'text', 1, lambda m, f: _names(m.get('dir'))),
    ('wri', 'Writers', 'People', 28, 'text', 0, lambda m, f: _names(m.get('wri'))),
    ('cast', 'Cast (top 10)', 'People', 46, 'text', 0, lambda m, f: _names(m.get('cast'), 1, 10)),
    ('res', 'Resolution', 'Files and quality', 11, 'text', 1, lambda m, f: f.get('res') if f else m.get('res')),
    ('quality', 'Quality details', 'Files and quality', 34, 'wrap', 0,
     lambda m, f: _quality(f) if f else '\n'.join(_quality(x) for x in m['files'])),
    ('src', 'Source', 'Files and quality', 12, 'text', 0, _fval('src')),
    ('codec', 'Codec', 'Files and quality', 9, 'text', 0, _fval('codec')),
    ('b10', '10-bit', 'Files and quality', 8, 'text', 0, _fflag('b10')),
    ('hdr', 'HDR', 'Files and quality', 7, 'text', 0, _fflag('hdr')),
    ('audio', 'Audio', 'Files and quality', 9, 'text', 0, _fval('audio')),
    ('fa', 'Farsi dubbed', 'Files and quality', 12, 'text', 0, _fflag('fa')),
    ('dual', 'Dual audio', 'Files and quality', 11, 'text', 0, _fflag('dual')),
    ('sub', 'Softsub', 'Files and quality', 9, 'text', 0, _fflag('sub')),
    ('ed', 'Edition', 'Files and quality', 14, 'text', 0, _fval('ed')),
    ('grp', 'Release group', 'Files and quality', 16, 'text', 0, _fval('grp')),
    ('nfiles', 'Files', 'Files and quality', 7, 'int', 0, lambda m, f: len(m['files'])),
    ('fname', 'File name', 'Files and quality', 60, 'wrap', 1,
     lambda m, f: f.get('raw') if f else '\n'.join(x['raw'] for x in m['files'])),
    ('size', 'Size (GB)', 'Files and quality', 10, 'gb', 0,
     lambda m, f: _gb(f.get('size') if f else sum(x.get('size') or 0 for x in m['files']))),
    ('path', 'Folder path', 'Files and quality', 60, 'wrap', 0,
     lambda m, f: '\n'.join(f.get('paths') or []) if f else '\n'.join(p for x in m['files'] for p in x.get('paths') or [])),
    ('dur', 'Length (min)', 'Files and quality', 11, 'int', 0, lambda m, f: _minutes(f.get('dur') if f else max((x.get('dur') or 0 for x in m['files']), default=0))),
    ('real', 'Picture size', 'Files and quality', 12, 'text', 0, lambda m, f: _picture(f if f else next((x for x in m['files'] if x.get('w')), {}))),
    ('audio_l', 'Audio languages', 'Files and quality', 22, 'text', 0, lambda m, f: _langs(m, f, 'alang')),
    ('subs_l', 'Subtitles', 'Files and quality', 22, 'text', 0, lambda m, f: _langs(m, f, 'slang')),
    ('listfile', 'List file', 'Files and quality', 22, 'text', 0, _fval('list')),
    ('fav', 'Favorite', 'Personal', 9, 'text', 0, lambda m, f: _yn(m.get('fav'))),
    ('watched', 'Watched', 'Personal', 9, 'text', 0, lambda m, f: _yn(m.get('watched'))),
    ('myrating', 'My rating', 'Personal', 10, 'int', 0, lambda m, f: m.get('myrating') or None),
    ('notes', 'Notes', 'Personal', 40, 'wrap', 0, lambda m, f: m.get('notes') or None),
    ('conf', 'Match', 'Matching', 22, 'text', 0, lambda m, f: CONF_LABEL.get(m.get('conf'), m.get('conf'))),
]
BY_KEY = {c[0]: c for c in COLUMNS}


def columns_info():
    return [{'key': k, 'label': lbl, 'group': g, 'default': bool(d)} for k, lbl, g, w, kind, d, _ in COLUMNS]


def build_rows(movies, keys, per_file=False):
    """The movies in the order of `keys`, as (movie, file or None) pairs."""
    by = {m['k']: m for m in movies}
    pairs = []
    for k in keys:
        m = by.get(k)
        if not m:
            continue
        if per_file:
            pairs.extend((m, f) for f in m['files'])
        else:
            pairs.append((m, None))
    return pairs


def cell_values(pairs, cols):
    return [[BY_KEY[c][6](m, f) for c in cols] for m, f in pairs]


# ---------------------------------------------------------------- csv
def to_csv(headers, rows):
    out = io.StringIO()
    w = csv.writer(out)
    w.writerow(headers)
    for r in rows:
        w.writerow(['' if v is None else v for v in r])
    return ('﻿' + out.getvalue()).encode('utf-8')  # BOM so Excel reads Persian / accented titles


# ---------------------------------------------------------------- xlsx
_BAD = re.compile('[\x00-\x08\x0b\x0c\x0e-\x1f]')


def _col(i):
    s, i = '', i + 1
    while i:
        i, r = divmod(i - 1, 26)
        s = chr(65 + r) + s
    return s


def _text(v):
    return escape(_BAD.sub('', str(v))[:32000])


# cell styles (see STYLES): 0 default, 1 header, 2 wrapped text, 3 link, 4 integer, 5 one decimal, 6 text
_S = {'text': 6, 'wrap': 2, 'link': 3, 'int': 4, 'dec': 5, 'gb': 7}

STYLES = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
<numFmts count="1"><numFmt numFmtId="164" formatCode="0.0"/></numFmts>
<fonts count="3"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font><font><u/><sz val="11"/><color rgb="FF1D4ED8"/><name val="Calibri"/></font></fonts>
<fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFF4F4F5"/><bgColor indexed="64"/></patternFill></fill></fills>
<borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left/><right/><top/><bottom style="thin"><color rgb="FFA1A1AA"/></bottom><diagonal/></border></borders>
<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
<cellXfs count="8">
<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
<xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center"/></xf>
<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
<xf numFmtId="0" fontId="2" fillId="0" borderId="0" xfId="0" applyFont="1" applyAlignment="1"><alignment vertical="top"/></xf>
<xf numFmtId="3" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment vertical="top" horizontal="right"/></xf>
<xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment vertical="top" horizontal="right"/></xf>
<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="top"/></xf>
<xf numFmtId="2" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment vertical="top" horizontal="right"/></xf>
</cellXfs>
<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
</styleSheet>'''


def _sheet_xml(headers, rows, kinds, widths):
    out = ['<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
           '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">']
    n = len(headers)
    last = f'{_col(max(n, 1) - 1)}{len(rows) + 1}'
    out.append(f'<dimension ref="A1:{last}"/>')
    out.append('<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/>'
               '<selection pane="bottomLeft"/></sheetView></sheetViews><sheetFormatPr defaultRowHeight="15"/>')
    if n:
        out.append('<cols>' + ''.join(f'<col min="{i + 1}" max="{i + 1}" width="{w}" customWidth="1"/>' for i, w in enumerate(widths)) + '</cols>')
    out.append('<sheetData>')
    out.append('<row r="1" ht="22" customHeight="1">' + ''.join(
        f'<c r="{_col(i)}1" t="inlineStr" s="1"><is><t>{_text(h)}</t></is></c>' for i, h in enumerate(headers)) + '</row>')
    for r, row in enumerate(rows, start=2):
        cells = []
        for i, v in enumerate(row):
            if v is None or v == '':
                continue
            ref, kind = f'{_col(i)}{r}', kinds[i]
            if kind in ('int', 'dec', 'gb'):
                try:
                    num = int(v) if kind == 'int' else float(v)
                    cells.append(f'<c r="{ref}" s="{_S[kind]}"><v>{num}</v></c>')
                    continue
                except (TypeError, ValueError):
                    kind = 'text'
            if kind == 'link':
                url = f'https://www.imdb.com/title/{v}/'
                cells.append(f'<c r="{ref}" t="str" s="3"><f>HYPERLINK("{url}","{_text(v)}")</f><v>{_text(v)}</v></c>')
                continue
            cells.append(f'<c r="{ref}" t="inlineStr" s="{_S.get(kind, 6)}"><is><t xml:space="preserve">{_text(v)}</t></is></c>')
        out.append(f'<row r="{r}">{"".join(cells)}</row>')
    out.append('</sheetData>')
    if n and rows:
        out.append(f'<autoFilter ref="A1:{last}"/>')
    out.append('</worksheet>')
    return ''.join(out)


def _sheet_names(names):
    used, out = set(), []
    for i, n in enumerate(names):
        base = re.sub(r'[\[\]:*?/\\]', '-', str(n)).strip().strip("'")[:31] or f'Sheet{i + 1}'
        name, k = base, 2
        while name.lower() in used:
            suffix = f' ({k})'
            name, k = base[:31 - len(suffix)] + suffix, k + 1
        used.add(name.lower())
        out.append(name)
    return out


def to_xlsx(headers, kinds, widths, sheets, about=None):
    """sheets: [(name, rows)]. about: optional [(label, value)] shown on a last 'Export info' sheet."""
    names = _sheet_names([n for n, _ in sheets] + (['Export info'] if about else []))
    xmls = [_sheet_xml(headers, rows, kinds, widths) for _, rows in sheets]
    if about:
        xmls.append(_sheet_xml(['What', 'Value'], [[a, b] for a, b in about], ['text', 'wrap'], [22, 90]))
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr('[Content_Types].xml', '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
                   '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                   '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
                   '<Default Extension="xml" ContentType="application/xml"/>'
                   '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
                   '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>'
                   + ''.join(f'<Override PartName="/xl/worksheets/sheet{i + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
                             for i in range(len(xmls))) + '</Types>')
        z.writestr('_rels/.rels', '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
                   '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                   '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>')
        z.writestr('xl/workbook.xml', '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
                   '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
                   'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>'
                   + ''.join(f'<sheet name="{escape(n, {chr(34): "&quot;"})}" sheetId="{i + 1}" r:id="rId{i + 1}"/>' for i, n in enumerate(names))
                   + '</sheets></workbook>')
        z.writestr('xl/_rels/workbook.xml.rels', '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
                   '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                   + ''.join(f'<Relationship Id="rId{i + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i + 1}.xml"/>'
                             for i in range(len(xmls)))
                   + f'<Relationship Id="rId{len(xmls) + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>')
        z.writestr('xl/styles.xml', STYLES)
        for i, x in enumerate(xmls):
            z.writestr(f'xl/worksheets/sheet{i + 1}.xml', x)
    return buf.getvalue()


# ---------------------------------------------------------------- entry point
STORE = {}  # token -> (file name, content type, bytes); the last few exports, until they are saved


def build(movies, req):
    """req: {keys, columns, rows: 'movie'|'file', format: 'xlsx'|'csv', sheets: [{name, keys}], info: {...}}.
    Returns (file name, content type, bytes, row count)."""
    cols = [c for c in (req.get('columns') or []) if c in BY_KEY] or [c[0] for c in COLUMNS if c[5]]
    per_file = req.get('rows') == 'file'
    headers = [BY_KEY[c][1] for c in cols]
    stamp = time.strftime('%Y-%m-%d')
    keys = list(req.get('keys') or [m['k'] for m in movies])
    if req.get('format') == 'csv':
        rows = cell_values(build_rows(movies, keys, per_file), cols)
        return f'MovieShelf export {stamp}.csv', 'text/csv; charset=utf-8', to_csv(headers, rows), len(rows)
    groups = [(s.get('name') or 'Movies', s.get('keys') or []) for s in (req.get('sheets') or [])] or [('Movies', keys)]
    sheets = [(name, cell_values(build_rows(movies, ks, per_file), cols)) for name, ks in groups]
    total = sum(len(r) for _, r in sheets)
    info = req.get('info') or {}
    about = [('Exported', time.strftime('%Y-%m-%d %H:%M')), ('Rows', f'{total:,} ({"one per file" if per_file else "one per movie"})'),
             ('Scope', info.get('scope') or ''), ('Filters', '\n'.join(info.get('filters') or []) or 'none'),
             ('Columns', ', '.join(headers))]
    data = to_xlsx(headers, [BY_KEY[c][4] for c in cols], [BY_KEY[c][3] for c in cols], sheets, about)
    return (f'MovieShelf export {stamp}.xlsx', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', data, total)


def keep(name, ctype, data):
    token = secrets.token_urlsafe(12)
    STORE[token] = (name, ctype, data)
    while len(STORE) > 4:
        STORE.pop(next(iter(STORE)))
    return token
