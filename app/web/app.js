'use strict';
const $ = (s, el = document) => el.querySelector(s);
const $$ = (s, el = document) => [...el.querySelectorAll(s)];
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const fold = s => String(s ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
  .replace(/[’'`´]/g, '').replace(/ي/g, 'ی').replace(/ك/g, 'ک')
  .replace(/\u200c/g, '').replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d));
const api = (u, body) => fetch(u, body === undefined ? {} : { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }).then(r => r.json());
const fmtVotes = v => !v ? '' : v >= 1e6 ? (v / 1e6).toFixed(1) + 'M' : v >= 1e3 ? Math.round(v / 1e3) + 'k' : String(v);
const fmtRun = m => !m ? '' : m >= 60 ? `${Math.floor(m / 60)}h ${m % 60}m` : `${m}m`;
const ttUrl = k => `https://www.imdb.com/title/${k}/`;
// the magnifier next to a title in the problem list: the title and year searched on Google
const googleLink = (title, year) => { const q = [title, year, 'movie'].filter(Boolean).join(' '); return `<a class="iconlink" href="https://www.google.com/search?q=${encodeURIComponent(q)}" target="_blank" rel="noopener" title="Search Google" aria-label="Search Google for ${esc(q)}">${I('search', 'sm')}</a>`; };
const showInFolder = paths => (paths || []).map(p => `<span class="fpath"><span title="${esc(p)}">${esc(p)}</span><button type="button" class="link" data-reveal="${esc(p)}">Show in folder</button></span>`).join('');

// outline icon set (one family, 24px grid); decorative unless the caller adds a label
const ICONS = {
  heart: '<path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78L12 21.23l8.84-8.84a5.5 5.5 0 0 0 0-7.78z"/>',
  check: '<path d="M20 6 9 17l-5-5"/>',
  alert: '<path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0zM12 9v4M12 17h.01"/>',
  x: '<path d="M18 6 6 18M6 6l12 12"/>',
  user: '<path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>',
  star: '<path d="m12 2 3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01z"/>',
  info: '<circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/>',
  ext: '<path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6M15 3h6v6M10 14 21 3"/>',
  file: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6M8 13h8M8 17h5"/>',
  folder: '<path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/>',
  chev: '<path d="m6 9 6 6 6-6"/>',
  search: '<circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/>',
  clock: '<circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/>',
  sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41"/>',
  moon: '<path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9z"/>',
  copy: '<rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>',
  download: '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M7 10l5 5 5-5M12 15V3"/>',
  monitor: '<rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/>',
};
const I = (n, cls = '') => `<svg class="i ${cls}" viewBox="0 0 24 24" aria-hidden="true">${ICONS[n]}</svg>`;

// ---------------- feedback: toasts and confirmation ----------------
// short confirmations that leave by themselves; errors stay longer and are announced as alerts
function toast(msg, kind = 'ok') {
  const box = $('#toasts');
  const el = document.createElement('div');
  el.className = `toast ${kind}`;
  el.innerHTML = `${I(kind === 'err' ? 'alert' : kind === 'info' ? 'info' : 'check')}<span>${esc(msg)}</span>`;
  if (kind === 'err') $('#alerts').textContent = msg;
  box.append(el);
  while (box.children.length > 3) box.firstChild.remove();
  setTimeout(() => { el.classList.add('out'); setTimeout(() => el.remove(), 200); }, kind === 'err' ? 7000 : 4000);
}

let cfmResolve = null, cfmFocus = null;
function confirmBox({ title, text, ok = 'Continue', danger = false }) {
  cfmFocus = document.activeElement;
  $('#cfm-t').textContent = title;
  $('#cfm-d').textContent = text;
  const yes = $('#cfm-yes');
  yes.textContent = ok;
  yes.className = 'btn' + (danger ? ' danger' : '');
  $('#cfm').classList.remove('hidden');
  (danger ? $('#cfm-no') : yes).focus();  // risky actions default to Cancel
  return new Promise(r => { cfmResolve = r; });
}
function closeConfirm(answer) {
  if (!cfmResolve) return;
  $('#cfm').classList.add('hidden');
  cfmResolve(answer); cfmResolve = null;
  cfmFocus?.focus();
}
$('#cfm-yes').addEventListener('click', () => closeConfirm(true));
$('#cfm-no').addEventListener('click', () => closeConfirm(false));
$('#cfm').addEventListener('click', e => { if (e.target === $('#cfm')) closeConfirm(false); });

const RT_FRESH = '<svg class="ico" viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="14" r="8.5" fill="#fa320a"/><path d="M12 4.6l1.5 2.3 2.6-1-1.2 2.5 2.4 1.3-2.7.3-.4 2.7L12 10.8l-2.2 1.9-.4-2.7-2.7-.3 2.4-1.3-1.2-2.5 2.6 1z" fill="#2eb82e"/></svg>';
const RT_ROTTEN = '<svg class="ico" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2.8c1.6 0 2.1 2.2 3.8 2.6 1.8.4 4.1-.2 4.8 1.6.7 1.7-1.3 2.8-1.2 4.5.1 1.8 2.2 2.8 1.2 4.5-1 1.7-3.1.7-4.5 1.7-1.4 1.1-1.5 3.6-3.4 3.6-2 0-1.9-2.6-3.3-3.6-1.4-1-3.4 0-4.4-1.7-1-1.6 1.1-2.7 1.2-4.5.1-1.8-1.9-2.8-1.2-4.5.7-1.8 3-1.2 4.8-1.6C10 5 10.4 2.8 12 2.8z" fill="#6bb52b"/><circle cx="21" cy="19.5" r="1.4" fill="#6bb52b"/><circle cx="3.4" cy="18.6" r="1.1" fill="#6bb52b"/></svg>';
const MC_ICON = '<svg class="ico" viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="11" fill="#111"/><path d="M6.6 16.6V8l5.4 5.1L17.4 8v8.6" fill="none" stroke="#ffcc34" stroke-width="2.3" stroke-linecap="round" stroke-linejoin="round"/></svg>';
const rtIcon = v => v >= 60 ? RT_FRESH : RT_ROTTEN;
const rtHtml = v => v == null ? '' : `<span class="sc rt${v >= 60 ? ' fresh' : ''}" title="Rotten Tomatoes: ${v}% ${v >= 60 ? 'fresh' : 'rotten'}">${rtIcon(v)}<b>${v}%</b></span>`;
const mcHtml = v => v == null ? '' : `<span class="sc mc" title="Metacritic Metascore: ${v}">${MC_ICON}<span class="box ${v >= 61 ? 'mcg' : v >= 40 ? 'mcy' : 'mcr'}">${v}</span></span>`;
const pad7 = t => 'tt' + String(t).padStart(7, '0');

const RES_ORDER =['2160p', '1080p', '720p', '576p', '480p', 'SD'];
const TYPE_LABEL = { movie: 'Movie', tvMovie: 'TV movie', video: 'Video', short: 'Short', tvShort: 'TV short', tvSpecial: 'TV special', tvMiniSeries: 'Mini-series', tvSeries: 'TV series' };
const CONF_LABEL = { exact: ['Exact match', 'good'], good: ['Good match', 'good'], manual: ['Set by you', 'good'], online: ['Online match – check', 'warn'], noyear: ['No year in name – check', 'warn'], review: ['Ambiguous – check', 'warn'], fuzzy: ['Fuzzy match – check', 'warn'], none: ['Not found on IMDb', 'bad'] };
const NEEDS_REVIEW = new Set(['online', 'noyear', 'review', 'fuzzy', 'none']);

let ALL = [], VIEW = [], ROWS = [], version = -1, rendered = 0, viewMode = localStorage.getItem('view') || 'grid';
let GROUP = localStorage.getItem('group') || 'none', REV = localStorage.getItem('rev') === '1';
// the section: everything, films only, or TV series (and mini-series) only. The filters below work inside the section.
const isSeries = m => m.type === 'tvSeries' || m.type === 'tvMiniSeries';
let KIND = ['all', 'movies', 'series'].includes(localStorage.getItem('kind')) ? localStorage.getItem('kind') : 'all', POOL = [];
const inKind = m => KIND === 'all' || (KIND === 'series') === isSeries(m);
const yearSpan = m => !m.year ? '' : isSeries(m) && m.endyear && m.endyear !== m.year ? `${m.year}–${m.endyear}` : String(m.year);
const COLLAPSED = new Set();  // 'group:label' of collapsed group headers
const SET_KEYS = ['status', 'genre', 'decade', 'runtime', 'res', 'src', 'tech', 'type'];
const F = { q: '', status: new Set(), genre: new Set(), decade: new Set(), yMin: null, yMax: null, rMin: 0, vMin: 0, rtMin: 0, mcMin: 0, runtime: new Set(), res: new Set(), src: new Set(), tech: new Set(), type: new Set(), country: '', lang: '', person: null };

// ---------------- data ----------------
async function load(keepScroll) {
  const scroll = $('#scroll').scrollTop, n = rendered;
  const d = await fetch('/api/movies').then(r => r.json());
  version = d.version;
  ALL = d.movies;
  ALL.forEach((m, i) => {
    m.idx = i;
    const people = [...(m.dir || []), ...(m.wri || []), ...(m.cast || [])].map(p => p[1]).join(' ');
    const chars = (m.cast || []).map(p => p[2] || '').join(' ');
    m.hay = fold([m.title, m.en, m.orig, m.fa_t, (m.akas || []).join(' '), m.ptitle, m.palt, m.year, m.tt,
      people, chars, (m.tcast || []).map(c => c.name).join(' '), m.files.map(f => f.raw).join(' '),
      (m.genres || []).join(' '), (m.countries || []).join(' '), m.coll].join(' | '));
    m.tkey = fold(m.en || m.title);
    m.fdir = joinNames(m.dir); m.fwri = joinNames(m.wri);
    m.fcast = fold([...(m.cast || []).map(p => p[1]), ...(m.tcast || []).map(c => c.name)].join(' | '));
    m.ftitle = fold([m.title, m.en, m.orig, m.fa_t, ...(m.akas || [])].join(' | '));
    m.ffile = fold(m.files.map(f => f.raw).join(' | '));
    m.faudio = fold(m.files.flatMap(f => f.alang || []).join(' | ')); m.fsubs = fold(m.files.flatMap(f => f.slang || []).join(' | '));
    m.decade = m.year ? Math.floor(m.year / 10) * 10 : null;
    m.pids = new Set([...(m.dir || []), ...(m.wri || []), ...(m.cast || []), ...(m.crew || [])].map(p => p[0]));
  });
  buildFacets();
  apply(keepScroll ? Math.max(n, 120) : 0);
  if (keepScroll) $('#scroll').scrollTop = scroll;
}

// ---------------- facets ----------------
function count(fn) { const c = new Map(); POOL.forEach(m => [].concat(fn(m) ?? []).forEach(v => v != null && v !== '' && c.set(v, (c.get(v) || 0) + 1))); return c; }

const chipBtn = (key, v, label, n) => `<button type="button" class="chip${F[key].has(String(v)) ? ' on' : ''}" aria-pressed="${F[key].has(String(v))}" data-k="${key}" data-v="${esc(v)}">${label}${n != null ? `<small>${n}</small>` : ''}</button>`;
function chipGroup(el, key, items, labelFn = v => v) {
  el.innerHTML = items.map(([v, n]) => chipBtn(key, v, esc(labelFn(v)), n)).join('');
}

function buildFacets() {
  POOL = ALL.filter(inKind);
  const nSeries = ALL.filter(isSeries).length;
  $$('#kind button').forEach(b => {
    const on = b.dataset.kind === KIND;
    b.classList.toggle('on', on); b.setAttribute('aria-pressed', on);
    $('small', b).textContent = (b.dataset.kind === 'all' ? ALL.length : b.dataset.kind === 'series' ? nSeries : ALL.length - nSeries).toLocaleString();
  });
  const st =[['fav', I('heart', 'sm') + tr('Favorites')], ['watched', I('check', 'sm') + tr('Watched')], ['unwatched', tr('Unwatched')], ['review', I('alert', 'sm') + tr('Needs review')], ['dupes', tr('Multiple files')], ['noposter', tr('No poster')], ['top', tr('Top rated 8+')]];
  const sc = { fav: POOL.filter(m => m.fav).length, watched: POOL.filter(m => m.watched).length, unwatched: POOL.filter(m => !m.watched).length, review: POOL.filter(m => NEEDS_REVIEW.has(m.conf)).length, dupes: POOL.filter(m => m.files.length > 1).length, noposter: POOL.filter(m => !m.poster).length, top: POOL.filter(m => m.rating >= 8).length };
  $('#f-status').innerHTML = st.map(([v, l]) => chipBtn('status', v, l, sc[v])).join('');
  chipGroup($('#f-genre'), 'genre', [...count(m => m.genres)].sort((a, b) => b[1] - a[1]));
  chipGroup($('#f-decade'), 'decade', [...count(m => m.decade)].sort((a, b) => a[0] - b[0]), v => v + 's');
  chipGroup($('#f-runtime'), 'runtime', [['short', null], ['90', null], ['120', null], ['150', null], ['long', null]],
    v => ({ short: '< 60m', 90: '60–90m', 120: '90–120m', 150: '2–2.5h', long: '> 2.5h' }[v]));
  chipGroup($('#f-res'), 'res', RES_ORDER.map(r => [r, POOL.filter(m => m.files.some(f => f.res === r)).length]).filter(x => x[1]));
  chipGroup($('#f-src'), 'src', [...count(m => [...new Set(m.files.map(f => f.src))])].sort((a, b) => b[1] - a[1]));
  const tech = [['fa', 'Farsi dubbed', f => f.fa], ['dual', 'Dual audio', f => f.dual], ['sub', 'Softsub', f => f.sub], ['x265', 'x265/HEVC', f => f.codec === 'x265'], ['b10', '10-bit', f => f.b10], ['hdr', 'HDR', f => f.hdr], ['51', '5.1+ audio', f => f.audio === '5.1' || f.audio === '7.1'], ['ed', 'Special edition', f => f.ed]];
  TECH = Object.fromEntries(tech.map(t => [t[0], t[2]]));
  $('#f-tech').innerHTML = tech.map(([v, l, fn]) => chipBtn('tech', v, tr(l), POOL.filter(m => m.files.some(fn)).length)).join('');
  chipGroup($('#f-type'), 'type', [...count(m => m.type || 'unknown')].sort((a, b) => b[1] - a[1]), v => tr(TYPE_LABEL[v] || 'Unmatched'));
  const cs = [...count(m => m.countries)].sort((a, b) => b[1] - a[1]);
  const ls = [...count(m => m.langs)].sort((a, b) => b[1] - a[1]);
  $('#sec-country').classList.toggle('hidden', !cs.length);
  $('#f-country').innerHTML = `<option value="">${tr('Any')}</option>` + cs.map(([v, n]) => `<option value="${esc(v)}"${F.country === v ? ' selected' : ''}>${esc(v)} (${n})</option>`).join('');
  $('#f-lang').innerHTML = `<option value="">${tr('Any')}</option>` + ls.map(([v, n]) => `<option value="${esc(v)}"${F.lang === v ? ' selected' : ''}>${esc(v)} (${n})</option>`).join('');
}
let TECH = {};

// ---------------- filtering ----------------
function runtimeOk(m) {
  const r = m.runtime; if (!r) return false;
  return [...F.runtime].some(v => v === 'short' ? r < 60 : v === '90' ? r >= 60 && r < 90 : v === '120' ? r >= 90 && r < 120 : v === '150' ? r >= 120 && r <= 150 : r > 150);
}

function saveFilters() {
  const o = {};
  for (const [k, v] of Object.entries(F)) if (k !== 'q') o[k] = v instanceof Set ? [...v] : v;
  try { localStorage.setItem('filters', JSON.stringify(o)); } catch { }
}
function restoreFilters() {
  let o; try { o = JSON.parse(localStorage.getItem('filters') || 'null'); } catch { }
  if (o) applyFilterObject(o);
}
function applyFilterObject(o) {
  for (const k of SET_KEYS) if (Array.isArray(o[k])) F[k] = new Set(o[k].map(String));
  for (const k of ['yMin', 'yMax', 'rMin', 'vMin', 'rtMin', 'mcMin', 'country', 'lang', 'person']) if (k in o) F[k] = o[k];
  $('#yMin').value = F.yMin || ''; $('#yMax').value = F.yMax || '';
  $('#rMin').value = F.rMin; $('#rMinLbl').textContent = F.rMin || tr('any');
  $('#vMin').value = F.vMin; $('#vMinLbl').textContent = F.vMin ? fmtVotes(Math.pow(10, F.vMin)) : tr('any');
  $('#rtMin').value = F.rtMin; $('#rtMinLbl').textContent = F.rtMin ? F.rtMin + '%' : tr('any');
  $('#mcMin').value = F.mcMin; $('#mcMinLbl').textContent = F.mcMin || tr('any');
}

function apply(keep = 0) {
  saveFilters();
  const Q = parseQuery(F.q);
  QOPS = Q.ops;
  const qn = Q.plain;
  VIEW = POOL.filter(m => {
    if (!Q.test(m)) return false;
    if (F.person && !m.pids.has(F.person.id)) return false;
    for (const s of F.status) {
      if (s === 'fav' && !m.fav) return false;
      if (s === 'watched' && !m.watched) return false;
      if (s === 'unwatched' && m.watched) return false;
      if (s === 'review' && !NEEDS_REVIEW.has(m.conf)) return false;
      if (s === 'dupes' && m.files.length < 2) return false;
      if (s === 'noposter' && m.poster) return false;
      if (s === 'top' && !(m.rating >= 8)) return false;
    }
    if (F.genre.size && ![...F.genre].every(g => m.genres.includes(g))) return false;
    if (F.decade.size && !F.decade.has(String(m.decade))) return false;
    if (F.yMin && !(m.year >= F.yMin)) return false;
    if (F.yMax && !(m.year <= F.yMax)) return false;
    if (F.rMin && !(m.rating >= F.rMin)) return false;
    if (F.vMin && !(m.votes >= Math.pow(10, F.vMin))) return false;
    if (F.rtMin && !(m.rt >= F.rtMin)) return false;
    if (F.mcMin && !(m.mc >= F.mcMin)) return false;
    if (F.runtime.size && !runtimeOk(m)) return false;
    if (F.res.size && !m.files.some(f => F.res.has(f.res))) return false;
    if (F.src.size && !m.files.some(f => F.src.has(String(f.src)))) return false;
    if (F.tech.size && ![...F.tech].every(t => m.files.some(TECH[t]))) return false;
    if (F.type.size && !F.type.has(m.type || 'unknown')) return false;
    if (F.country && !(m.countries || []).includes(F.country)) return false;
    if (F.lang && !(m.langs || []).includes(F.lang)) return false;
    return true;
  });
  const sort = $('#sort').value;
  const by = {
    title: (a, b) => a.tkey.localeCompare(b.tkey),
    year: (a, b) => (a.year || 9999) - (b.year || 9999) || a.tkey.localeCompare(b.tkey),
    'year-desc': (a, b) => (b.year || 0) - (a.year || 0) || a.tkey.localeCompare(b.tkey),
    rating: (a, b) => (b.rating || 0) - (a.rating || 0) || (b.votes || 0) - (a.votes || 0),
    votes: (a, b) => (b.votes || 0) - (a.votes || 0),
    rt: (a, b) => (b.rt ?? -1) - (a.rt ?? -1) || (b.votes || 0) - (a.votes || 0),
    mc: (a, b) => (b.mc ?? -1) - (a.mc ?? -1) || (b.votes || 0) - (a.votes || 0),
    runtime: (a, b) => (b.runtime || 0) - (a.runtime || 0),
    added: (a, b) => b.files[0].id - a.files[0].id,
    myrating: (a, b) => (b.myrating || 0) - (a.myrating || 0) || (b.rating || 0) - (a.rating || 0),
  }[sort];
  VIEW.sort(by);
  if (REV) VIEW.reverse();
  $$('#table th[data-sort]').forEach(th => {
    th.dataset.sort === sort ? th.setAttribute('aria-sort', (sort === 'title') !== REV ? 'ascending' : 'descending') : th.removeAttribute('aria-sort');
  });
  $('#sortDir').setAttribute('aria-pressed', REV);
  if (qn) { // titles that match the query float to the top
    const tm = m => [m.title, m.en, m.orig, m.fa_t, ...(m.akas || [])].some(t => fold(t).startsWith(qn)) ? 0 : [m.title, m.en, m.orig, m.fa_t].some(t => fold(t).includes(qn)) ? 1 : 2;
    VIEW = VIEW.map(m => [tm(m), m]).sort((a, b) => a[0] - b[0]).map(x => x[1]);
  }
  buildRows();
  render(keep);
  renderActive();
}

// ---------------- rendering ----------------
function posterHtml(m) {
  return m.poster ? `<img loading="lazy" decoding="async" src="/poster/${m.tt}.jpg" alt="">`
    : `<div class="ph">${esc(m.en || m.title)}<small>${m.year || ''}</small></div>`;
}

function cardHtml(m) {
  const sub = m.orig && fold(m.orig) !== fold(m.en || m.title) ? m.orig : m.fa_t || '';
  const anyFa = m.files.some(f => f.fa);
  const label = [m.en || m.title, m.year, m.rating && `rated ${m.rating.toFixed(1)}`, m.rt != null && `Rotten Tomatoes ${m.rt}%`, m.mc != null && `Metacritic ${m.mc}`, m.res, anyFa && 'Farsi dubbed',
    m.files.length > 1 && `${m.files.length} files`, m.fav && 'favorite', m.watched && 'watched', NEEDS_REVIEW.has(m.conf) && 'needs review'].filter(Boolean).join(', ');
  return `<article class="card${SEL.has(m.k) ? ' selected' : ''}" data-i="${m.idx}" data-k="${esc(m.k)}" tabindex="0" role="button" aria-label="${esc(label)}">
    <div class="pw" aria-hidden="true">${posterHtml(m)}
      ${m.rating ? `<span class="badge b-rate">${I('star', 'sm fill')}${m.rating.toFixed(1)}</span>` : ''}
      ${m.res ? `<span class="badge b-res${m.res === '2160p' ? ' r4k' : ''}">${m.res === '2160p' ? '4K' : m.res}</span>` : ''}
      ${NEEDS_REVIEW.has(m.conf) ? `<span class="badge b-flag" title="${CONF_LABEL[m.conf][0]}">${I('alert', 'sm')}</span>` : ''}
      ${anyFa ? '<span class="badge b-fa">FA</span>' : ''}
      ${m.rt != null || m.mc != null ? `<span class="badge b-sc">${m.rt != null ? `<span class="rt${m.rt >= 60 ? ' fresh' : ''}" title="Rotten Tomatoes">${rtIcon(m.rt)}${m.rt}%</span>` : ''}${m.mc != null ? `<span title="Metacritic">${MC_ICON}${m.mc}</span>` : ''}</span>` : ''}
      ${m.files.length > 1 ? `<span class="badge b-n">${m.files.length} files</span>` : ''}
      <div class="uflags">${m.fav ? `<span class="fav">${I('heart', 'sm fill')}</span>` : ''}${m.watched ? `<span class="seen">${I('check', 'sm')}</span>` : ''}</div>
    </div>
    <div class="t" title="${esc(m.en || m.title)}">${esc(m.en || m.title)}</div>
    ${sub ? `<div class="o" dir="auto" title="${esc(sub)}">${esc(sub)}</div>` : ''}
    <div class="m">${[yearSpan(m), isSeries(m) && m.runtime ? fmtRun(m.runtime) + ' / ep' : fmtRun(m.runtime), (m.genres || []).slice(0, 2).join(', ')].filter(Boolean).join(' · ')}</div>
  </article>`;
}

function rowHtml(m) {
  const q = qualityHtml(m.files[0]);
  return `<tr data-i="${m.idx}" data-k="${esc(m.k)}" tabindex="0"${SEL.has(m.k) ? ' class="selected"' : ''}>
    <td>${m.poster ? `<img loading="lazy" src="/poster/${m.tt}.jpg" alt="">` : '<div class="thumb"></div>'}</td>
    <td><b>${esc(m.en || m.title)}</b>${NEEDS_REVIEW.has(m.conf) ? ` <span class="q warn" title="Needs review">${I('alert', 'sm')}<span class="sr-only">needs review</span></span>` : ''}${m.fav ? ` <span style="color:#ff6b81">${I('heart', 'sm fill')}</span><span class="sr-only">favorite</span>` : ''}${m.watched ? ` <span style="color:var(--good)">${I('check', 'sm')}</span><span class="sr-only">watched</span>` : ''}</td>
    <td class="dim" dir="auto">${esc(m.orig && m.orig !== (m.en || m.title) ? m.orig : '')}</td>
    <td>${yearSpan(m)}</td><td>${m.rating ? m.rating.toFixed(1) : ''}</td><td class="dim">${fmtVotes(m.votes)}</td>
    <td>${m.rt != null ? m.rt + '%' : ''}</td><td>${m.mc ?? ''}</td>
    <td class="dim">${m.runtime || ''}</td><td class="dim">${esc((m.genres || []).join(', '))}</td>
    <td>${esc((m.dir || []).map(d => d[1]).join(', '))}</td><td>${q}${m.files.length > 1 ? `<span class="q">+${m.files.length - 1}</span>` : ''}</td></tr>`;
}

function qualityHtml(f, full) {
  const q = [];
  if (f.res) q.push(`<span class="q hi">${f.res}</span>`);
  if (f.src) q.push(`<span class="q">${esc(f.src)}</span>`);
  if (f.codec) q.push(`<span class="q">${f.codec}</span>`);
  if (f.b10) q.push('<span class="q">10-bit</span>');
  if (f.hdr) q.push('<span class="q hi">HDR</span>');
  if (f.audio) q.push(`<span class="q">${f.audio}</span>`);
  if (f.fa) q.push('<span class="q fa">Farsi dubbed</span>');
  if (f.dual) q.push('<span class="q fa">Dual audio</span>');
  if (f.sub) q.push('<span class="q">Softsub</span>');
  if (f.ed) q.push(`<span class="q hi">${esc(f.ed)}</span>`);
  if (f.grp) q.push(`<span class="q">${esc(f.grp)}</span>`);
  if (full) {  // facts read from the real file with ffprobe
    if (f.dur) q.push(`<span class="q">${fmtRun(Math.round(f.dur / 60))}</span>`);
    if (f.w) q.push(`<span class="q">${f.w}×${f.h}</span>`);
    if (f.alang?.length) q.push(`<span class="q fa" title="Audio languages">Audio: ${esc(f.alang.join(', '))}</span>`);
    if (f.slang?.length) q.push(`<span class="q" title="Subtitles">Subs: ${esc(f.slang.join(', '))}</span>`);
  }
  return q.join('');
}

const PAGE = 150;
function render(keep = 0) {
  rendered = 0;
  $('#grid').innerHTML = '';
  $('#table tbody').innerHTML = '';
  $('#grid').classList.toggle('hidden', viewMode !== 'grid');
  $('#table').classList.toggle('hidden', viewMode !== 'list');
  $('#grid').setAttribute('aria-busy', 'false');
  $('#empty').classList.toggle('hidden', VIEW.length > 0);
  $('#empty').innerHTML = !ALL.length
    ? (LAST_JOB && !LAST_JOB.running && /No \.txt|no movie folder/.test(LAST_JOB.error || '')
      ? `<b>No movies yet</b>Give MovieShelf your movie folder and it finds the movies by itself, or load a list of movie names (a .txt or .csv file, one name per line).<br><button class="btn" id="emptySettings">Choose the movie folder</button> <button class="btn" id="emptyLoad">Load list</button> <button class="btn outline" data-emptyhelp>How to prepare a list</button>`
      : `<b>Building your library…</b>The first run downloads IMDb data (about 1.9 GB) and matches your list. This takes around 10 minutes; progress shows at the top.`)
    : !POOL.length && KIND === 'series'
      ? `<b>No series in your library yet.</b>A series shows up here when a name in your list matches an IMDb TV series or mini-series, for example <code>Breaking.Bad.2008</code>.`
      : `<b>No ${KIND === 'series' ? 'series' : 'movies'} match these filters.</b><button class="btn ghost" id="emptyReset">Reset filters</button>`;
  const noun = KIND === 'series' ? 'series' : VIEW.length === 1 ? 'movie' : 'movies';
  $('#count').innerHTML = `${VIEW.length.toLocaleString()} ${tr(noun)}` + (VIEW.length !== POOL.length ? ` <small>${tr('of')} ${POOL.length.toLocaleString()}</small>` : '');
  more(Math.max(keep, PAGE));
  if (!keep) $('#scroll').scrollTop = 0;
}
function more(n = PAGE) {
  const slice = ROWS.slice(rendered, rendered + n);
  if (!slice.length) return;
  rendered += slice.length;
  const one = viewMode === 'grid' ? r => r.h !== undefined ? grpHtml(r) : cardHtml(r) : r => r.h !== undefined ? grpRowHtml(r) : rowHtml(r);
  (viewMode === 'grid' ? $('#grid') : $('#table tbody')).insertAdjacentHTML('beforeend', slice.map(one).join(''));
  if (SELMODE) paintSel();
}
new IntersectionObserver(e => e[0].isIntersecting && more(), { root: $('#scroll'), rootMargin: '1200px' }).observe($('#more'));

function renderActive() {
  const tags = [];
  QOPS.forEach(o => tags.push(['q:' + o.raw, o.raw, I('search', 'sm')]));
  if (F.person) tags.push(['person', F.person.name, I('user', 'sm')]);
  const chipText = (k, v) => { const c = $(`.chip[data-k="${k}"][data-v="${CSS.escape(v)}"]`); return c ? [...c.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent).join('').trim() : v; };
  for (const k of ['status', 'genre', 'decade', 'runtime', 'res', 'src', 'tech', 'type']) F[k].forEach(v => tags.push([k + ':' + v, chipText(k, v) || v]));
  if (F.yMin || F.yMax) tags.push(['year', `${F.yMin || '…'}–${F.yMax || '…'}`]);
  if (F.rMin) tags.push(['rMin', `IMDb ≥ ${F.rMin}`]);
  if (F.vMin) tags.push(['vMin', `votes ≥ ${fmtVotes(Math.pow(10, F.vMin))}`]);
  if (F.rtMin) tags.push(['rtMin', `Rotten Tomatoes ≥ ${F.rtMin}%`]);
  if (F.mcMin) tags.push(['mcMin', `Metacritic ≥ ${F.mcMin}`]);
  if (F.country) tags.push(['country', F.country]);
  if (F.lang) tags.push(['lang', F.lang]);
  ACTIVE_LABELS = tags.map(t => t[1]);
  const cnt = {};
  const bump = (sec, n = 1) => n && (cnt[sec] = (cnt[sec] || 0) + n);
  for (const [k, sec] of Object.entries({ status: 'library', genre: 'genre', decade: 'year', runtime: 'runtime', res: 'quality', src: 'quality', tech: 'quality', type: 'type' })) bump(sec, F[k].size);
  bump('year', +!!(F.yMin || F.yMax)); bump('scores', +!!F.rMin + +!!F.vMin + +!!F.rtMin + +!!F.mcMin); bump('region', +!!F.country + +!!F.lang);
  $$('.n[data-n]').forEach(el => { el.textContent = cnt[el.dataset.n] || ''; });
  $('#active').innerHTML = tags.map(([k, l, ic]) => `<span class="tag">${ic || ''}${esc(l)}<button type="button" data-rm="${esc(k)}" aria-label="Remove filter ${esc(l)}">${I('x', 'sm')}</button></span>`).join('');
}

// ---------------- detail ----------------
function personChip(p, extra) { return `<button type="button" class="person" data-pid="${p[0]}" data-pname="${esc(p[1])}" title="Open the profile of ${esc(p[1])}">${esc(p[1])}${extra ? ` <small>· ${esc(extra)}</small>` : ''}</button>`; }

// ---------------- dialog ----------------
let lastFocus = null;
function showModal(labelId) {
  if ($('#modal').classList.contains('hidden')) lastFocus = document.activeElement;
  const d = $('#dlg');
  labelId ? d.setAttribute('aria-labelledby', labelId) : d.removeAttribute('aria-labelledby');
  $('#modal').classList.remove('hidden');
  d.scrollTop = 0;
  ($('[data-close]', d) || d).focus();
}
function closeModal() {
  $('#modal').classList.add('hidden');
  $('#dlg').classList.remove('wide', 'help');
  delete $('#dlg').dataset.mode;
  clearTimeout(ckTimer);
  if (!lastFocus) return;
  // a background reload may have re-rendered the card that opened the dialog
  const el = document.contains(lastFocus) ? lastFocus
    : lastFocus.dataset?.i != null ? $(`${viewMode === 'grid' ? '.card' : 'tr'}[data-i="${lastFocus.dataset.i}"]`) : null;
  (el || $('#q')).focus();
}
const closeBtn = `<button type="button" class="close" data-close aria-label="Close">${I('x')}</button>`;

function openMovie(m) {
  const imgs = new Map((m.tcast || []).map(c => [fold(c.name), c.img]));
  const bg = m.backdrop || (m.poster ? `/poster/${m.tt}.jpg` : '');
  const conf = CONF_LABEL[m.conf] || ['', ''];
  const showOrig = m.orig && fold(m.orig) !== fold(m.en || m.title);
  const showPrim = m.title && fold(m.title) !== fold(m.en || m.title) && (!m.orig || fold(m.title) !== fold(m.orig));
  const crewGroups = {};
  (m.crew || []).forEach(c => (crewGroups[c[2]] = crewGroups[c[2]] || []).push(c));
  $('#dlg').innerHTML = `
    ${closeBtn}
    <div class="hero${m.backdrop ? ' bd' : ''}">
      <div class="bgimg" style="background-image:url('${esc(bg)}')"></div>
      <div class="poster">${m.poster ? `<img src="/poster/${m.tt}.jpg" alt="Poster of ${esc(m.en || m.title)}">` : posterHtml(m)}</div>
      <div class="info">
        <h1 id="dlg-title">${esc(m.en || m.title)}</h1>
        ${showOrig ? `<div class="alt" dir="auto"><span>Original</span>${esc(m.orig)}</div>` : ''}
        ${showPrim ? `<div class="alt" dir="auto"><span>IMDb</span>${esc(m.title)}</div>` : ''}
        ${m.tt ? faRow(m) : ''}
        <div class="meta">
          ${m.rating ? `<span class="stars">${I('star', 'fill')} ${m.rating.toFixed(1)} <small>/10 IMDb · ${fmtVotes(m.votes)} votes</small></span>` : ''}
          ${rtHtml(m.rt)}${mcHtml(m.mc)}
          ${m.year ? `<span>${m.year}${m.endyear ? '–' + m.endyear : ''}</span>` : ''}
          ${m.runtime ? `<span>${fmtRun(m.runtime)}${isSeries(m) ? ' / ep' : ''}</span>` : ''}
          ${m.type && m.type !== 'movie' ? `<span class="q">${TYPE_LABEL[m.type] || m.type}</span>` : ''}
          ${(m.genres || []).map(g => `<button type="button" class="chip" data-genre="${esc(g)}" title="Show all ${esc(g)} movies">${esc(g)}</button>`).join('')}
        </div>
        ${m.tagline ? `<div class="tagline">${esc(m.tagline)}</div>` : ''}
        ${m.plot ? `<div class="plot">${esc(m.plot)}</div>` : (m.tt ? '<div class="plot dim" style="color:var(--muted-foreground)">Add a free TMDB key in Settings to get plots, countries and cast photos.</div>' : '')}
        ${m.dir?.length ? `<h3 class="hh">Director</h3><div class="people">${m.dir.map(p => personChip(p)).join('')}</div>` : ''}
        <div class="ubar">
          <button type="button" class="tog${m.fav ? ' on' : ''}" data-u="fav" aria-pressed="${!!m.fav}">${I('heart')}Favorite</button>
          <button type="button" class="tog${m.watched ? ' on' : ''}" data-u="watched" aria-pressed="${!!m.watched}">${I('check')}Watched</button>${m.watched && m.watched_at ? `<small class="wdate">on ${m.watched_at.slice(0, 10)}</small>` : ''}
          <span class="myr" role="group" aria-label="My rating">${Array.from({ length: 10 }, (_, i) => `<button type="button" data-r="${i + 1}" class="${m.myrating > i ? 'on' : ''}" aria-label="Rate ${i + 1} of 10" aria-pressed="${m.myrating === i + 1}">${I('star', 'fill')}</button>`).join('')}<span class="lbl">${m.myrating ? m.myrating + '/10' : 'My rating'}</span></span>
        </div>
        <div class="links">
          ${m.tt ? `<a class="btn small" href="${ttUrl(m.tt)}" target="_blank">IMDb ${m.tt}</a>
          <a class="btn ghost small" href="https://letterboxd.com/imdb/${m.tt}/" target="_blank">Letterboxd</a>
          <a class="btn ghost small" href="https://www.themoviedb.org/redirect?external_source=imdb_id&external_id=${m.tt}" target="_blank">TMDB</a>
          <a class="btn ghost small" href="https://www.youtube.com/results?search_query=${encodeURIComponent((m.en || m.title) + ' ' + (m.year || '') + ' trailer')}" target="_blank">Trailer</a>
          <button type="button" class="btn ghost small" data-padd="${+m.tt.slice(2)}">${I('download', 'sm')}${m.poster ? 'Change poster' : 'Add poster'}</button>` : ''}
          <button type="button" class="btn danger small" data-rmall="1" title="Take this movie out of your library. Your list files and movie files are not changed.">Remove from list</button>
        </div>
        ${m.tt ? `<div data-pbox="${+m.tt.slice(2)}"></div>` : ''}
      </div>
    </div>
    <div class="body">
      <div>
        ${m.cast?.length ? `<h3>Cast</h3><div class="castgrid">${m.cast.map(p => {
          const img = imgs.get(fold(p[1]));
          return `<button type="button" class="castm" data-pid="${p[0]}" data-pname="${esc(p[1])}" title="Open the profile of ${esc(p[1])}"><span class="ph2" aria-hidden="true" style="${img ? `background-image:url('${esc(img)}')` : ''}">${img ? '' : esc(p[1].split(' ').map(w => w[0]).slice(0, 2).join(''))}</span><span style="min-width:0"><b>${esc(p[1])}</b><small>${esc(p[2] || '')}</small></span></button>`;
        }).join('')}</div>` : ''}
        ${m.wri?.length ? `<h3>Writers</h3><div class="people">${m.wri.map(p => personChip(p)).join('')}</div>` : ''}
        ${Object.keys(crewGroups).length ? `<h3>Crew</h3><div class="people">${Object.entries(crewGroups).flatMap(([job, ps]) => ps.map(p => personChip(p, job))).join('')}</div>` : ''}
        ${m.akas?.length ? `<h3>Also known as</h3><div class="akas" dir="auto">${m.akas.map(esc).join(' · ')}</div>` : ''}
        <h3><label for="notes">Notes</label></h3>
        <textarea id="notes" placeholder="Your notes… (saved automatically)" dir="auto">${esc(m.notes || '')}</textarea>
      </div>
      <div>
        <h3>Details</h3>
        <dl class="kv">
          ${m.countries?.length ? `<dt>Country</dt><dd>${esc(m.countries.join(', '))}</dd>` : ''}
          ${m.langs?.length ? `<dt>Language</dt><dd>${esc(m.langs.join(', '))}</dd>` : ''}
          ${m.coll ? `<dt>Collection</dt><dd><a href="#" data-search="${esc(m.coll)}">${esc(m.coll)}</a></dd>` : ''}
          <dt>Match</dt><dd><span class="q ${conf[1]}">${conf[0]}</span></dd>
        </dl>
        <h3>Your files (${m.files.length})</h3>
        <div class="files">${m.files.map(f => `<div class="file"><code>${esc(f.raw)}</code>${qualityHtml(f, true)}${f.size ? `<span class="q">${fmtSize(f.size)}</span>` : ''}${(f.paths || []).map(p => `<div class="fpath"><span title="${esc(p)}">${esc(p)}</span><button type="button" class="link" data-reveal="${esc(p)}">Show in folder</button></div>`).join('')}
          <div style="margin-top:6px"><button class="btn ghost small" data-fix="${f.id}">${m.tt ? 'Wrong movie? Fix match' : 'Find on IMDb'}</button> <button type="button" class="btn ghost small" data-rm="${f.id}">Remove from list</button></div>
          <div class="fixbox" data-fixbox="${f.id}"></div></div>`).join('')}</div>
      </div>
    </div>`;
  $('#dlg').dataset.k = m.k;
  $('#dlg').dataset.mode = 'movie';
  showModal('dlg-title');
  if (!m.tt) openFix(m.files[0].id);
}

const faRow = m => `<div class="alt farow" data-tc="${m.tt.slice(2)}"><span>Persian</span><span class="fa-t" dir="rtl">${m.fa_t ? esc(m.fa_t) : '<i>none</i>'}</span> <button type="button" class="link" data-fa-edit>${tr('Edit')}</button></div>`;
function faEdit(row, m) {
  row.innerHTML = `<span>Persian</span><input type="text" class="fa-in" dir="rtl" value="${esc(m.fa_t || '')}" placeholder="عنوان فارسی" aria-label="Persian title" lang="fa">
    <button type="button" class="btn small" data-fa-save>Save</button><button type="button" class="btn outline small" data-fa-reset title="Go back to the title from TMDB">Reset</button><button type="button" class="link" data-fa-cancel>Cancel</button>`;
  $('.fa-in', row).focus();
}
async function setUser(m, data) {
  Object.assign(m, data);
  if ('watched' in data) m.watched_at = data.watched ? (m.watched_at || new Date().toISOString().slice(0, 19).replace('T', ' ')) : null;
  await api('/api/user/' + m.k, data);
}

async function openFix(fid, q) {
  const box = $(`[data-fixbox="${fid}"]`);
  const f = ALL.flatMap(m => m.files).find(x => x.id === fid);
  box.innerHTML = `<div class="fix"><div class="row"><input type="text" aria-label="Search IMDb" placeholder="Title and year, or IMDb link / tt-number" value="${esc(q || '')}" data-fq="${fid}"><button class="btn small" data-fgo="${fid}">Search</button></div><div class="cands" aria-live="polite">Searching IMDb…</div></div>`;
  const input = $(`[data-fq="${fid}"]`, box);
  input.focus();
  const val = input.value.trim();
  if (/tt\d+/.test(val)) { box.querySelector('.cands').innerHTML = `<div class="cand"><div>${val.match(/tt\d+/)[0]}</div><button class="btn small" data-use="${val.match(/tt\d+/)[0]}" data-f="${fid}">Use this</button></div>`; return; }
  const res = await api(`/api/lookup/${fid}` + (val ? `?q=${encodeURIComponent(val)}` : ''));
  box.querySelector('.cands').innerHTML = (res.length ? res.map(c => `<div class="cand">
      ${c.image ? `<img src="${esc(c.image.replace('._V1_.', '._V1_QL50_UX80_.'))}" alt="">` : '<div class="ni"></div>'}
      <div><b>${esc(c.title)}</b> ${c.year || ''}<small>${esc([TYPE_LABEL[c.type] || c.type, c.orig && c.orig !== c.title ? c.orig : '', c.rating ? 'IMDb ' + c.rating : '', c.votes ? fmtVotes(c.votes) + ' votes' : '', c.stars || '', 'tt' + String(c.tconst).padStart(7, '0'), c.how].filter(Boolean).join(' · '))}</small></div>
      <a class="btn ghost small" href="${ttUrl('tt' + String(c.tconst).padStart(7, '0'))}" target="_blank">View</a>
      <button class="btn small" data-use="${c.tconst}" data-f="${fid}">Use this</button></div>`).join('') : '<div class="dim">Nothing found. Try another spelling or paste the IMDb link.</div>')
    + `<div style="margin-top:6px"><button class="btn ghost small" data-use="" data-f="${fid}">Mark as not on IMDb</button></div>`;
}

// ---------------- stats ----------------
function openStats() {
  const top = (map, n = 12) => [...map].sort((a, b) => b[1] - a[1]).slice(0, n);
  const bars = (rows, onclick) => {
    const max = Math.max(...rows.map(r => r[1]), 1);
    return `<div class="bars">${rows.map(([l, n, extra]) => {
      const a = onclick(l, extra), tag = a ? 'button' : 'div';
      return `<${tag} ${a ? 'type="button"' : ''} class="barrow" ${a}><span title="${esc(l)}">${esc(l)}</span><span class="tr" aria-hidden="true"><i style="width:${n / max * 100}%"></i></span><span>${n}</span></${tag}>`;
    }).join('')}</div>`;
  };
  const people = (key) => { const c = new Map(), names = {}; ALL.forEach(m => (m[key] || []).forEach(p => { c.set(p[0], (c.get(p[0]) || 0) + 1); names[p[0]] = p[1]; })); return top(c, 15).map(([id, n]) => [names[id], n, id]); };
  const rated = ALL.filter(m => m.rating);
  const mins = ALL.reduce((s, m) => s + (m.runtime || 0), 0);
  const files = ALL.reduce((s, m) => s + m.files.length, 0);
  $('#dlg').innerHTML = `${closeBtn}<div class="pad">
    <h2 id="dlg-title" dir="auto">${tr('Collection statistics')}</h2>
    <div class="statgrid">
      <div class="stat"><b>${ALL.length.toLocaleString()}</b><span>movies</span></div>
      <div class="stat"><b>${files.toLocaleString()}</b><span>files</span></div>
      <div class="stat"><b>${(rated.reduce((s, m) => s + m.rating, 0) / (rated.length || 1)).toFixed(2)}</b><span>average IMDb rating</span></div>
      <div class="stat"><b>${Math.round(mins / 60).toLocaleString()} h</b><span>total runtime (${Math.round(mins / 60 / 24)} days)</span></div>
      <div class="stat"><b>${ALL.filter(m => m.watched).length}</b><span>watched</span></div>
      <div class="stat"><b>${ALL.filter(m => NEEDS_REVIEW.has(m.conf)).length}</b><span>need review</span></div>
    </div>
    <div class="cols">
      <div><h3>By decade</h3>${bars([...count(m => m.decade)].sort((a, b) => a[0] - b[0]).map(([d, n]) => [d + 's', n, d]), (l, d) => `data-sf="decade" data-sv="${d}"`)}</div>
      <div><h3>Genres</h3>${bars(top(count(m => m.genres), 15), l => `data-sf="genre" data-sv="${esc(l)}"`)}</div>
      <div><h3>Top directors</h3>${bars(people('dir'), (l, id) => `data-sp="${id}" data-spn="${esc(l)}"`)}</div>
      <div><h3>Top actors</h3>${bars(people('cast'), (l, id) => `data-sp="${id}" data-spn="${esc(l)}"`)}</div>
      <div><h3>Quality</h3>${bars(RES_ORDER.map(r => [r, ALL.filter(m => m.files.some(f => f.res === r)).length]).filter(r => r[1]), l => `data-sf="res" data-sv="${l}"`)}</div>
      <div><h3>IMDb rating</h3>${bars([9, 8, 7, 6, 5, 4].map(r => [`${r}+`, ALL.filter(m => m.rating >= r && m.rating < r + 1).length]), () => '')}</div>
      ${ALL.some(m => m.countries?.length) ? `<div><h3>Countries</h3>${bars(top(count(m => m.countries), 15), l => `data-sc="${esc(l)}"`)}</div>` : ''}
    </div></div>`;
  $('#dlg').dataset.mode = 'stats';
  showModal('dlg-title');
}

// ---------------- settings ----------------
let STAB = 'general';
const STABS = [['general', 'General'], ['appearance', 'Appearance'], ['online', 'Online data'], ['maintenance', 'Maintenance'], ['log', 'Activity log']];
const prefBtn = (pref, val, label, cur) => `<button type="button" data-pref="${pref}" data-val="${val}" class="${cur === val ? 'on' : ''}" aria-pressed="${cur === val}">${label}</button>`;
async function openSettings(tab) {
  if (typeof tab === 'string') STAB = tab;
  const st = await api('/api/status');
  const bks = await api('/api/backups');
  const pr = await api('/api/probe');
  const pe = await api('/api/people/status');
  const s = st.settings, x = st.stats;
  const pane = (k, html) => `<div class="spane" id="sp-${k}" role="tabpanel" aria-labelledby="st-${k}"${STAB === k ? '' : ' hidden'}>${html}</div>`;
  const theme = themePref(), psize = document.documentElement.dataset.psize || 'm';
  $('#dlg').innerHTML = `${closeBtn}<div class="pad">
    <h2 id="dlg-title" dir="auto">${tr('Settings')}</h2>
    <div class="stabs" role="tablist" aria-label="Settings sections">${STABS.map(([k, l]) => `<button type="button" role="tab" id="st-${k}" aria-selected="${STAB === k}" aria-controls="sp-${k}" tabindex="${STAB === k ? 0 : -1}" data-stab="${k}">${tr(l)}</button>`).join('')}</div>
    ${pane('general', `
      <section class="setsec"><h3>Library</h3>
        <div class="statgrid">
          <div class="stat"><b>${x.files.toLocaleString()}</b><span>list entries</span></div>
          <div class="stat"><b>${x.matched.toLocaleString()}</b><span>matched to IMDb</span></div>
          <div class="stat"><b>${x.titles.toLocaleString()}</b><span>unique titles</span></div>
          <div class="stat"><b>${x.posters.toLocaleString()}</b><span>posters</span></div>
          <div class="stat"><b>${x.enriched.toLocaleString()}</b><span>with cast &amp; crew</span></div>
          <div class="stat"><b>${x.scores.toLocaleString()}</b><span>with RT / Metacritic</span></div>
        </div>
        <div class="setrow"><label for="s-media">Movie folders<small>One folder per line. The app goes through them by itself, finds every movie and lists it; it saves and reads them when you leave the box. To bring in a list of names, use the Load list button at the top.</small></label>
          <textarea id="s-media" rows="3" dir="ltr" placeholder="D:\\Movies">${esc((s.media_dirs || []).join('\n'))}</textarea>
          <div class="btns" style="margin-top:8px"><button type="button" class="btn outline small" id="s-media-add">Add a folder…</button></div></div>
      </section>
      <section class="setsec"><h3>Automatic updates</h3>
        <p class="sub">What the app does on its own in the background.</p>
        <label class="check"><input type="checkbox" id="s-posters" ${s.online_posters ? 'checked' : ''}><span>Download posters from IMDb</span></label>
        <label class="check"><input type="checkbox" id="s-online" ${s.online_fallback ? 'checked' : ''}><span>Search IMDb online for titles not found offline</span></label>
        <label class="check"><input type="checkbox" id="s-auto" ${s.auto_refresh ? 'checked' : ''}><span>Keep IMDb ratings fresh (weekly) and critic scores (monthly)<br><small style="color:var(--muted-foreground)">IMDb ratings are ${x.ratings_age == null ? 'not downloaded yet' : x.ratings_age < 1 ? 'up to date today' : Math.round(x.ratings_age) + ' days old'}.</small></span></label>
        <p class="sub">The IMDb data (about 2 GB) is downloaded to <span dir="ltr">${esc(x.imdb_dir)}</span>; the search index built from it is <span dir="ltr">imdb_index.db</span> in the folder above it.</p>
      </section>`)}
    ${pane('appearance', `
      <section class="setsec"><h3>Appearance</h3>
        <p class="sub">Saved on this computer.</p>
        <div class="prefrow"><div><b>Theme</b><span>Light, dark, or follow Windows.</span></div>
          <div class="seg" role="group" aria-label="Theme">${prefBtn('theme', 'light', 'Light', theme)}${prefBtn('theme', 'dark', 'Dark', theme)}${prefBtn('theme', 'system', 'System', theme)}</div></div>
        <div class="prefrow"><div><b>Poster size</b><span>How large movie cards are in the grid.</span></div>
          <div class="seg" role="group" aria-label="Poster size">${prefBtn('psize', 's', 'Small', psize)}${prefBtn('psize', 'm', 'Medium', psize)}${prefBtn('psize', 'l', 'Large', psize)}</div></div>
        <div class="prefrow"><div><b>Language / زبان</b><span>Menus and Help can be shown in English or Persian (فارسی).</span></div>
          <div class="seg" role="group" aria-label="Language">${prefBtn('lang', 'en', 'English', LANG)}${prefBtn('lang', 'fa', 'فارسی', LANG)}</div></div>
        <div class="prefrow"><div><b>Default layout</b><span>Posters or a table.</span></div>
          <div class="seg" role="group" aria-label="Default layout">${prefBtn('view', 'grid', 'Posters', viewMode)}${prefBtn('view', 'list', 'Table', viewMode)}</div></div>
      </section>`)}
    ${pane('online', `
      <section class="setsec"><h3>Online data</h3>
        <p class="sub">Both keys are optional and free. Without them the app still works from the IMDb data.</p>
        <div class="setrow"><label for="s-tmdb">TMDB API key<small>Adds plot, tagline, countries, languages, Persian titles, cast photos and backdrops.</small></label>
          <div class="line"><input type="password" id="s-tmdb" placeholder="${s.tmdb_key ? 'Key saved – paste a new one to replace' : 'API key (v3) or Read Access Token (v4)'}"><button class="btn outline" id="s-tmdb-save">Test &amp; save</button></div>
          <div class="hint">Get one at themoviedb.org → Settings → API. <span id="s-tmdb-msg" role="status"></span></div></div>
        <div class="setrow"><label for="s-omdb">OMDb API key<small>Fills Rotten Tomatoes and Metacritic scores that Wikidata does not have.</small></label>
          <div class="line"><input type="password" id="s-omdb" placeholder="${s.omdb_key ? 'Key saved – paste a new one to replace' : 'OMDb key'}"><button class="btn outline" id="s-omdb-save">Test &amp; save</button></div>
          <div class="hint">Get one at omdbapi.com/apikey.aspx (free: 1,000 titles a day; the rest continue on the next update). <span id="s-omdb-msg" role="status"></span></div></div>
      </section>`)}
    ${pane('maintenance', `
      <section class="setsec"><h3>Update</h3>
        <p class="sub">“Scan list &amp; update” adds new lines from your list files. Manual matches are never overwritten.</p>
        <div class="btns">
          <button class="btn" data-act="update">Scan list &amp; update</button>
          <button class="btn outline" data-act="ratings">Update IMDb ratings</button>
          <button class="btn outline" data-act="scores">Update RT &amp; Metacritic</button>
          <button class="btn outline" data-act="posters">Retry missing posters</button>
        </div>
        <div class="hint">“Update IMDb ratings” downloads only the ratings file (about 8 MB).</div>
      </section>
      <section class="setsec"><h3>Export</h3>
        <p class="sub">Pick the movies and the columns, then save an Excel or CSV file.</p>
        <div class="btns"><button type="button" class="btn outline" data-act="export">${I('download')}Export to Excel…</button><button type="button" class="btn outline" data-act="renameplan">Rename plan…</button></div>
      </section>
      <section class="setsec"><h3>People</h3>
        <p class="sub">Photos, biographies and full filmographies of directors, writers and actors come from TMDB. Profiles are fetched when you open them; scanning fetches them in advance, so they open at once and work offline. ${pe.fetched.toLocaleString()} profiles fetched so far (${pe.photos.toLocaleString()} with a photo).</p>
        ${pe.tmdb ? '' : '<div class="err-box" style="background:var(--warn-bg);border-color:var(--warn);color:var(--warn)">Add a free TMDB key in Online data first.</div>'}
        <div class="setrow"><label for="s-pscope">Whom to scan</label>
          <div class="line"><div class="sel" style="flex:1"><select id="s-pscope">${[['directors', 'Directors and writers'], ['main', 'Directors, writers and the first five actors'], ['all', 'Everyone with a credit (slow: tens of thousands)']].map(([v, l]) => `<option value="${v}"${(s.people_scope || 'main') === v ? ' selected' : ''}>${l} (${pe.todo[v].toLocaleString()} to fetch)</option>`).join('')}</select></div>
            <button type="button" class="btn outline" data-act="peoplescan"${pe.tmdb ? '' : ' disabled'}>Scan people now</button></div></div>
        <label class="check"><input type="checkbox" id="s-pauto" ${s.people_auto ? 'checked' : ''}><span>Scan people automatically after each update</span></label>
      </section>
      <section class="setsec"><h3>File details (ffprobe)</h3>
        <label class="check"><input type="checkbox" id="s-probe"${s.probe_media ? ' checked' : ''}><span>Read audio languages, subtitles, length and real picture size with ffprobe<br><small style="color:var(--muted-foreground)">${pr.ffprobe ? `ffprobe found. ${pr.read.toLocaleString()} of ${pr.files.toLocaleString()} files read${pr.failed ? `, ${pr.failed} unreadable` : ''}. Only new or changed files are read on later updates.` : 'ffprobe not found. Install ffmpeg (winget install Gyan.FFmpeg), then restart the app.'}</small></span></label>
        <div class="btns"><button type="button" class="btn outline small" id="s-probe-now"${pr.ffprobe && pr.files ? '' : ' disabled'}>Read files now</button></div>
      </section>
      <section class="setsec"><h3>Full backup</h3>
        <p class="sub">Everything in one .zip file: the whole library, all settings and API keys, your list files, the posters and, if you want, the IMDb data. Use it for a complete safety copy or to move to another PC.</p>
        <div class="btns"><button type="button" class="btn" data-act="fullsave">${I('download')}Save full backup…</button><button type="button" class="btn outline" data-act="fullload">Load full backup…</button></div>
      </section>
      <section class="setsec"><h3>Quick backup (your own data only)</h3>
        <p class="sub">Your favorites, watched flags, ratings, notes, manual matches, ignored problems, saved views, wishlist and the names in your movie lists. A copy is also saved automatically once a day in <code>data/backups</code> (the last 14 are kept).</p>
        <div class="btns"><button type="button" class="btn outline" data-act="backup">${I('download')}Export backup…</button><button type="button" class="btn outline" data-act="import">Import backup…</button></div>
        <input type="file" id="s-import" accept=".json,application/json" hidden>
        ${bks.length ? `<div class="setrow" style="margin-top:14px"><label for="s-bk">Restore an automatic backup</label><div class="line"><div class="sel" style="flex:1"><select id="s-bk">${bks.map(b => `<option value="${esc(b.name)}">${esc(b.name.replace('personal-', '').replace('.json', ''))}</option>`).join('')}</select></div><button type="button" class="btn outline" data-act="restorebk">Restore</button></div></div>` : ''}
      </section>
      <section class="setsec"><h3>Rebuild</h3>
        <p class="sub">Only needed if matches look wrong or the IMDb data is damaged.</p>
        <div class="btns">
          <button class="btn outline" data-act="rematch">Re-match everything</button>
          <button class="btn outline" data-act="refresh">Re-download IMDb data</button>
        </div>
        <div class="hint">“Re-download IMDb data” refreshes everything (about 1.9 GB).</div>
      </section>`)}
    ${pane('log', `
      <section class="setsec"><h3>Activity log</h3>
        <p class="sub">What the last updates did.</p>
        <div class="log" id="s-log" tabindex="0" aria-label="Activity log">${esc((st.job.log || []).join('\n')) || 'No activity yet.'}</div>
      </section>`)}
  </div>`;
  $('#dlg').dataset.mode = 'settings';
  showModal('dlg-title');
}

// ---------------- list check ----------------
const CK_STATUS = {
  new: ['New', 'Not in your library yet'], better: ['Better quality', 'You have this movie in lower quality'],
  version: ['Other version', 'You have this movie, but the audio (dubbed / original) differs'],
  worse: ['Lower quality', 'You already have a better copy'], duplicate: ['Already have', 'Same movie, same quality'],
  have: ['Same file', 'This exact name is already in your list'], unmatched: ['Not found', 'Could not be found on IMDb'],
};
const CK_TABS = [['all', 'All'], ['new', 'New'], ['better', 'Better quality'], ['version', 'Other version'], ['worse', 'Lower quality'],
  ['duplicate', 'Already have'], ['have', 'Same file'], ['unmatched', 'Not found'], ['unsure', 'Needs a check'], ['twins', 'Twice in this list'], ['missing', 'Only in library']];
let CK = { res: null, tab: 'all', sel: new Set(), choice: {}, shown: 200 }, ckTimer;
const ckAddable = it => it.status !== 'have';

let foldersBusy = false;
async function saveFolders() {  // the movie folders box saves by itself: save, then read the folders
  const box = $('#s-media'); if (!box || foldersBusy) return;
  foldersBusy = true;
  try {
    const dirs = box.value.split('\n').map(x => x.trim().replace(/^"|"$/g, '')).filter(Boolean);
    const cur = (await api('/api/status')).settings.media_dirs || [];
    if (JSON.stringify(dirs) === JSON.stringify(cur)) return;
    await api('/api/settings', { media_dirs: dirs }); await api('/api/media/scan', {});
    toast(dirs.length ? 'Movie folders saved. Reading them…' : 'Movie folders cleared', 'info'); poll();
  } finally { foldersBusy = false; }
}
async function pickNative(kind) {
  // native Windows dialogs through pywebview; undefined = not available (plain browser)
  const a = window.pywebview?.api;
  if (!a) return undefined;
  try { return (await (kind === 'folder' ? a.pick_folder() : a.pick_file())) || null; } catch { return undefined; }
}
// ---------------- Load list: the one way to bring movie names in ----------------
// A list file (.txt/.csv), a folder of lists or a folder of movies. The page first asks what loading would do (duplicates
// inside the file, names already in the library, similar movies), then asks: add to the current list or start a new one.
let LL = { src: null, pv: null };
function llMsg(html, err) { const el = $('#ll-msg'); if (el) el.innerHTML = html ? `<div class="${err ? 'err-box' : 'hint'}" role="${err ? 'alert' : 'status'}">${html}</div>` : ''; }
function openLoad() {
  LL = { src: null, pv: null };
  $('#dlg').classList.add('wide');
  $('#dlg').dataset.mode = 'loadlist';
  llPickView();
  showModal('dlg-title');
}
function llPickView() {
  $('#dlg').innerHTML = `${closeBtn}<div class="pad">
    <h2 id="dlg-title" dir="auto">${tr('Load list')}</h2>
    <p style="margin:-6px 0 14px;color:var(--dim)">Bring in a list of movie names. Before anything is added you see which names are repeated or already in your library, and you choose whether they join your current list or start a new one.</p>
    <div class="src-pick">
      <button type="button" class="opt" data-ll="file">${I('file')}<span><b>A list file</b><small>.txt with one name per line, or .csv with a name, title + year, or IMDb id column</small></span></button>
      <button type="button" class="opt" data-ll="folder">${I('folder')}<span><b>A folder</b><small>A folder of list files, or a folder of movies: the names of the video files inside it are loaded. Files are only read, never moved.</small></span></button>
    </div>
    <div class="setrow"><label for="ll-path">Or type the path of a file or folder</label>
      <div class="line"><input type="text" id="ll-path" placeholder="D:\\Movies   or   C:\\lists\\new movies.txt" dir="ltr"><button type="button" class="btn" id="ll-go">Read</button></div></div>
    <input type="file" id="ll-file" accept=".txt,.csv,.tsv" hidden>
    <div id="ll-msg"></div>
  </div>`;
}
async function llPreview(src) {
  llMsg('Reading…');
  const pv = await api('/api/lists/preview', src);
  if (pv.error) return llMsg(esc(pv.error), true);
  LL = { src, pv };
  llChoose();
}
function llWarn(n, what, names) {
  if (!n) return '';
  return `<details class="ll-warn" style="margin:8px 0"><summary><b>${n.toLocaleString()}</b> ${what}</summary>
    <div class="sub" dir="ltr" style="margin-top:6px">${names.map(x => `<code>${esc(x)}</code>`).join('<br>')}${n > names.length ? `<br>… and ${(n - names.length).toLocaleString()} more` : ''}</div></details>`;
}
function llChoose() {
  const pv = LL.pv;
  const warns = llWarn(pv.repeated_n, 'repeated inside this list (each is loaded once)', pv.repeated)
    + llWarn(pv.existing_n, 'already in your library (not added again)', pv.existing)
    + llWarn(pv.similar_n, 'look like a movie you already have (same title and year, other file name): check before you add', pv.similar);
  $('#dlg').innerHTML = `${closeBtn}<div class="pad">
    <h2 id="dlg-title" dir="auto">${tr('Load list')}</h2>
    <p style="margin:-6px 0 12px"><b>${pv.names.toLocaleString()}</b> movie names found${pv.lists > 1 ? ` in ${pv.lists} lists` : ''}, <b>${pv.new_n.toLocaleString()}</b> of them new to your library.</p>
    ${warns ? `<div class="why">${I('alert', 'sm')} Duplicates found. Nothing has been added yet.</div>${warns}` : `<div class="hint">No duplicates: every name is new.</div>`}
    <div class="setrow" style="margin-top:14px"><label>Where should these names go?<small><b>Add to current list</b>: they join the lists you already have. <b>Start a new list</b>: your current list files are moved to a <code>previous lists</code> folder (nothing is deleted) and this list takes their place. Movies found in your movie folders stay either way.</small></label></div>
    <div id="ll-msg"></div>
    <div class="bar-actions"><button type="button" class="btn outline" data-ll="back">Back</button><span class="grow"></span>
      <button type="button" class="btn outline" data-ll="new">Start a new list</button><button type="button" class="btn" data-ll="add">Add to current list</button></div>
  </div>`;
}
async function llLoad(mode) {
  if (mode === 'new' && !await confirmBox({ title: 'Start a new list?', text: 'Your current list files are moved to the “previous lists” folder inside your list folder. Nothing is deleted. The new list takes their place. Movies from your movie folders stay.', ok: 'Start a new list', danger: true })) return;
  llMsg('Loading…');
  const r = await api('/api/lists/load', { ...LL.src, mode });
  if (r.error) return llMsg(esc(r.error), true);
  closeModal();
  toast(`${r.names.toLocaleString()} names loaded${r.moved ? `. ${r.moved} old list ${r.moved === 1 ? 'file' : 'files'} put aside in “previous lists”` : ''}. Updating…`, 'info');
  load(true); poll();
}
async function llClick(t) {
  const k = t.dataset.ll;
  if (k === 'file') {
    const path = await pickNative('file');
    if (path) llPreview({ path }); else if (path === undefined) $('#ll-file').click();
    return;
  }
  if (k === 'folder') {
    const path = await pickNative('folder');
    if (path) llPreview({ path });
    else if (path === undefined) { $('#ll-path').focus(); llMsg('Type the folder path (for example D:\\Movies) and press Read.'); }
    return;
  }
  if (t.id === 'll-go') { const v = $('#ll-path').value.trim().replace(/^"|"$/g, ''); if (v) llPreview({ path: v }); else $('#ll-path').focus(); return; }
  if (k === 'back') return llPickView();
  if (k === 'add' || k === 'new') return llLoad(k);
}
function ckMsg(html, err) { const el = $('#ck-msg'); if (el) el.innerHTML = html ? `<div class="${err ? 'err-box' : 'hint'}" role="${err ? 'alert' : 'status'}">${html}</div>` : ''; }

async function openCheck() {
  $('#dlg').classList.add('wide');
  $('#dlg').dataset.mode = 'check';
  const st = await api('/api/check');
  if (st.running) { ckProgressView(st); showModal('dlg-title'); return ckPoll(); }
  if (st.has_result && !CK.res) await ckLoad();
  CK.res ? ckResults() : ckPickView();
  showModal('dlg-title');
}

function ckPickView() {
  $('#dlg').innerHTML = `${closeBtn}<div class="pad">
    <h2 id="dlg-title" dir="auto">${tr('Check a list against your library')}</h2>
    <p style="margin:-6px 0 14px;color:var(--dim)">See which movies are new, which you already have, and whether a copy is better or worse quality. Nothing changes until you click “Add”.</p>
    <div class="src-pick">
      <button type="button" class="opt" data-ck="file">${I('file')}<span><b>A list file</b><small>.txt with one name per line, or .csv with a name, title + year, or IMDb id column</small></span></button>
      <button type="button" class="opt" data-ck="folder">${I('folder')}<span><b>A folder of movies</b><small>Reads the video file names inside it and its sub-folders. Files are only read, never moved.</small></span></button>
    </div>
    <div class="setrow"><label for="ck-path">Or type the path of a file or folder</label>
      <div class="line"><input type="text" id="ck-path" placeholder="D:\\Movies   or   C:\\lists\\new movies.txt" dir="ltr"><button type="button" class="btn" id="ck-go">Check</button></div></div>
    <div class="setrow"><label for="ck-text">Or paste names, one per line</label>
      <textarea id="ck-text" rows="4" placeholder="Heat.1995.1080p.BluRay&#10;In the Mood for Love 2000" dir="auto"></textarea>
      <div style="margin-top:6px"><button type="button" class="btn ghost" id="ck-paste">Check pasted names</button></div></div>
    <input type="file" id="ck-file" accept=".txt,.csv,.tsv" hidden>
    <div id="ck-msg"></div>
  </div>`;
}

function ckProgressView(st) {
  $('#dlg').innerHTML = `${closeBtn}<div class="pad">
    <h2 id="dlg-title">Checking your list…</h2>
    <div class="progress" aria-hidden="true"><i id="ck-bar"></i></div>
    <div id="ck-stage" role="status" style="color:var(--dim)">${esc(st?.stage || 'Starting')}</div>
    <p style="color:var(--faint);font-size:12.5px">Big folders take a minute. You can close this window; the result waits here.</p>
  </div>`;
}

async function ckStart(body) {
  ckMsg('Starting…');
  const r = await api('/api/check', body);
  if (!r.started) return ckMsg(esc(r.error || 'A check is already running.'), true);
  CK = { res: null, tab: 'all', sel: new Set(), choice: {}, shown: 200 };
  ckProgressView();
  ckPoll();
}

async function ckPoll() {
  clearTimeout(ckTimer);
  const st = await api('/api/check');
  if ($('#dlg').dataset.mode !== 'check') return;
  if (st.running) {
    const bar = $('#ck-bar'); if (bar) bar.style.width = st.total ? `${Math.max(5, st.done / st.total * 100)}%` : '5%';
    const sg = $('#ck-stage'); if (sg) sg.textContent = st.stage + (st.total ? ` — ${st.done} / ${st.total}` : '');
    ckTimer = setTimeout(ckPoll, 600);
    return;
  }
  if (st.error) { ckPickView(); return ckMsg(`<b>Could not check that list.</b><br>${esc(st.error)}<br>Tip: pick a .txt / .csv file, or a folder that contains video files.`, true); }
  await ckLoad();
  ckResults();
  $('#dlg').focus();
}

async function ckLoad() {
  CK.res = await api('/api/check/result');
  CK.sel = new Set(CK.res.items.map((it, i) => ['new', 'better', 'version'].includes(it.status) ? i : -1).filter(i => i >= 0));
}

function ckVisible() {
  const t = CK.tab, items = CK.res.items.map((it, i) => ({ it, i }));
  if (t === 'all') return items;
  if (t === 'unsure') return items.filter(x => x.it.reason);
  if (t === 'twins') return items.filter(x => x.it.twins);
  return items.filter(x => x.it.status === t);
}
function ckCount() { const b = $('#ck-add'); if (b) { b.textContent = `Add ${CK.sel.size} selected to library`; b.disabled = !CK.sel.size; } }

function ckItemHtml({ it, i }) {
  const im = it.imdb;
  const [lbl, tip] = CK_STATUS[it.status] || [it.status, ''];
  const choices = it.reason && (it.cands?.length || it.tconst)
    ? [...(it.tconst ? [{ tconst: it.tconst, title: im?.title, year: im?.year, votes: im?.votes, type: im?.type, cur: 1 }] : []), ...(it.cands || [])] : [];
  const picked = String(CK.choice[i] ?? it.tconst ?? '');
  const opts = choices.length ? `<fieldset class="opts" style="border:0;padding:0;margin:6px 0 0"><legend class="sub" style="padding:0">Which movie is it?</legend>${
    choices.map(c => `<label><input type="radio" name="ckc${i}" value="${c.tconst}" data-ckc="${i}" ${picked === String(c.tconst) ? 'checked' : ''}>
        <span>${esc(c.title)} (${c.year || '?'}) <small style="color:var(--dim)">${[c.type && c.type !== 'movie' ? TYPE_LABEL[c.type] || c.type : '', c.votes ? fmtVotes(c.votes) + ' votes' : '', c.cur ? 'best guess' : ''].filter(Boolean).join(' · ')}</small>
        <a class="iconlink" href="${ttUrl(pad7(c.tconst))}" target="_blank" aria-label="Open ${esc(c.title)} on IMDb">${I('ext', 'sm')}</a></span></label>`).join('')}
      <label><input type="radio" name="ckc${i}" value="" data-ckc="${i}" ${picked === '' ? 'checked' : ''}><span>None of these (add without IMDb data)</span></label></fieldset>` : '';
  return `<div class="ck">
    ${ckAddable(it) ? `<input type="checkbox" data-cksel="${i}" ${CK.sel.has(i) ? 'checked' : ''} aria-label="Select ${esc(it.raw)}">` : '<span></span>'}
    <div style="min-width:0">
      <div class="nm">${esc(it.raw)}</div>
      <div class="im">${im ? `<a href="${ttUrl(pad7(it.tconst))}" target="_blank">${esc(im.title)}</a> ${im.year || ''} <small>${[im.orig, im.rating ? 'IMDb ' + im.rating : '', im.votes ? fmtVotes(im.votes) + ' votes' : '', im.type && im.type !== 'movie' ? TYPE_LABEL[im.type] || im.type : ''].filter(Boolean).map(esc).join(' · ')}</small>` : '<span style="color:var(--bad)">No IMDb match</span>'}</div>
      <div class="sub">${it.quality ? `<span class="q hi">${esc(it.quality)}</span>` : ''}${it.size ? `<span class="q">${(it.size / 1e9).toFixed(1)} GB</span>` : ''}${it.copies > 1 ? `<span class="q warn">${it.copies}× in this list</span>` : ''}${it.path ? `<span class="q" title="${esc(it.path)}" style="max-width:420px;overflow:hidden;text-overflow:ellipsis;vertical-align:bottom">${esc(it.path)}</span>` : ''}</div>
      ${it.reason ? `<div class="why">${I('alert', 'sm')} ${esc(it.reason)}</div>` : ''}
      ${opts}
      ${it.lib?.length ? `<div class="lib">In your library${it.lib_title ? ` as <b>${esc(it.lib_title)}</b>` : ''}:<br>${it.lib.map(f => `<span class="q">${esc(f.quality || '?')}</span> <code>${esc(f.raw)}</code>`).join('<br>')}</div>` : ''}
      ${it.twins ? `<div class="lib">Also in this list: ${it.twins.map(esc).join(', ')}</div>` : ''}
    </div>
    <span class="st ${it.status}" title="${esc(tip)}">${esc(lbl)}</span></div>`;
}

function ckResults() {
  const r = CK.res, c = r.counts;
  const n = k => k === 'all' ? r.items.length : k === 'missing' ? r.missing_count : c[k] || 0;
  const vis = CK.tab === 'missing' ? [] : ckVisible();
  const list = CK.tab === 'missing'
    ? `<p style="color:var(--dim)">Movies in your library that are not in the checked list (useful when you checked a whole drive).</p><div class="ck-list">${r.missing.slice(0, CK.shown).map(m => `<div class="ck" style="grid-template-columns:1fr auto"><div style="min-width:0"><div class="im">${m.title ? `<a href="${ttUrl(pad7(m.tconst))}" target="_blank">${esc(m.title)}</a> ${m.year || ''}` : ''}</div><div class="nm">${m.files.map(esc).join('<br>')}</div></div><span class="st missing">Only in library</span></div>`).join('')}</div>`
    : `<div class="ck-list">${vis.slice(0, CK.shown).map(ckItemHtml).join('') || '<div style="color:var(--dim)">Nothing here.</div>'}</div>`;
  const total = CK.tab === 'missing' ? r.missing.length : vis.length;
  $('#dlg').innerHTML = `${closeBtn}<div class="pad">
    <h2 id="dlg-title">List check: ${r.entries.toLocaleString()} ${r.entries === 1 ? 'entry' : 'entries'}</h2>
    <div style="color:var(--dim);font-size:12.5px;margin:-8px 0 12px;word-break:break-all">${esc(r.source)} · ${esc(r.at)}</div>
    <div class="statgrid">
      <div class="stat"><b>${n('new')}</b><span>new movies</span></div>
      <div class="stat"><b>${n('better')}</b><span>better than your copy</span></div>
      <div class="stat"><b>${n('version')}</b><span>other audio version</span></div>
      <div class="stat"><b>${n('worse') + n('duplicate') + n('have')}</b><span>already have (same or better)</span></div>
      <div class="stat"><b>${n('unmatched')} / ${n('unsure')}</b><span>not found / need a check</span></div>
    </div>
    <div class="tabs" role="group" aria-label="Show">${CK_TABS.filter(([k]) => k === 'all' || n(k)).map(([k, l]) => `<button type="button" class="chip${CK.tab === k ? ' on' : ''}" aria-pressed="${CK.tab === k}" data-cktab="${k}">${l}<small>${n(k)}</small></button>`).join('')}</div>
    ${CK.tab !== 'missing' && vis.some(x => ckAddable(x.it)) ? `<div class="btns" style="margin-bottom:8px"><button type="button" class="btn ghost small" data-ckall="1">Select all shown</button><button type="button" class="btn ghost small" data-ckall="0">Select none shown</button></div>` : ''}
    ${list}
    ${total > CK.shown ? `<div style="margin-top:10px"><button type="button" class="btn ghost" id="ck-more">Show more (${(total - CK.shown).toLocaleString()} left)</button></div>` : ''}
    <div class="bar-actions">
      <span class="grow" id="ck-note" role="status">Selected names are added to <b>added.txt</b> in your list folder, then matched like the rest.</span>
      <a class="btn ghost" href="/api/check.csv" data-csv="MovieShelf list check.csv">Export report (CSV)</a>
      <button type="button" class="btn ghost" id="ck-wish">Add selected to wishlist</button>
      <button type="button" class="btn ghost" id="ck-again">Check another</button>
      <button type="button" class="btn" id="ck-add"></button>
    </div>
  </div>`;
  ckCount();
}

async function ckAdd(btn) {
  // 'choice' only when the user picked a movie (or 'none') for an unsure item
  const items = [...CK.sel].map(i => {
    const it = CK.res.items[i], ch = CK.choice[i];
    return ch === undefined ? { raw: it.raw } : { raw: it.raw, choice: ch === '' ? null : +ch };
  });
  btn.disabled = true; btn.textContent = 'Adding…';
  const r = await api('/api/check/add', { items });
  CK.res.items.forEach((it, i) => { if (CK.sel.has(i)) it.status = 'have'; });
  CK.sel.clear();
  ckResults();
  $('#ck-note').innerHTML = r.added ? `<b style="color:var(--good)">${r.added} added.</b> They are being matched now; progress shows at the top.` : 'Those names were already in your list.';
  toast(r.added ? `${r.added} added to your library` : 'Those names were already in your list', r.added ? 'ok' : 'info');
  poll();
}

// reads the list files and the movie folders again; a long job that is running (a people scan, for example) can be stopped first
async function recheckNow() {
  let now = false;
  if (LAST_JOB?.running) {
    if (!await confirmBox({ title: 'Another job is running', text: `${LAST_JOB.stage || 'An update'} is still running, so the check would wait for it. Stop it and check your lists and movie folders now? What it already finished is kept.`, ok: 'Stop it and check now' })) return false;
    now = true;
  }
  const r = await api('/api/rescan', { now });
  toast(r.started || now ? 'Checking your lists and movie folders…' : 'Queued: runs after the current job', 'info');
  return true;
}

// ---------------- remove from list, poster by hand ----------------
let POSTER_DONE = null;
async function removeFromList(ids, after) {
  if (!ids.length) return false;
  const n = ids.length;
  if (!await confirmBox({ title: n === 1 ? 'Remove this title from your list?' : `Remove ${n.toLocaleString()} titles from your list?`,
    text: 'They are hidden from the library and no update brings them back. Your list files and your movie files are not changed.', ok: 'Remove from list', danger: true })) return false;
  const r = await api('/api/remove', { ids });
  toast(`${r.removed.toLocaleString()} removed from your list`);
  await after?.(r);
  await load(true); poll();
  return true;
}
const posterBoxHtml = (tc, has) => `<div class="fix postbox">
  <div class="row"><input type="text" aria-label="Poster: image address or file path" placeholder="Image address (https://…) or a file path" data-purl="${tc}" dir="ltr"><button type="button" class="btn small" data-pgo="${tc}">Use</button></div>
  <div class="row"><button type="button" class="btn outline small" data-ppick="${tc}">Choose an image file…</button>${has ? `<button type="button" class="btn ghost small" data-pclear="${tc}">Remove poster</button>` : ''}<button type="button" class="link" data-pcancel="${tc}">Cancel</button></div>
  <input type="file" accept="image/jpeg,image/png,image/webp,image/gif" data-pfile="${tc}" hidden>
  <div class="dim" data-pmsg="${tc}" role="status">JPEG, PNG, WebP or GIF, up to 15 MB. A poster you add is kept: updates never replace it.</div></div>`;
function posterMsg(tc, text, bad) {
  const el = $(`[data-pmsg="${tc}"]`);
  if (el) { el.textContent = text; el.style.color = bad ? 'var(--destructive)' : ''; }
}
async function posterSave(tc, body, done) {
  posterMsg(tc, 'Saving…');
  const r = await api('/api/poster/' + tc, body);
  if (r.error) { posterMsg(tc, r.error, true); return toast(r.error, 'err'); }
  toast(body.clear ? 'Poster removed' : 'Poster saved');
  await done?.();
}
// the poster buttons of a movie window or a problem row; true when the click was one of them
async function posterClick(t, done) {
  const d = t.dataset;
  if (d.padd) {
    POSTER_DONE = done;
    const box = $(`[data-pbox="${d.padd}"]`);
    if (!box) return true;
    if (box.firstElementChild) { box.innerHTML = ''; return true; }
    box.innerHTML = posterBoxHtml(+d.padd, !!ALL.find(m => m.tt === pad7(+d.padd))?.poster);
    $('[data-purl]', box).focus();
    return true;
  }
  if (d.pcancel) { $(`[data-pbox="${d.pcancel}"]`).innerHTML = ''; return true; }
  if (d.ppick) { POSTER_DONE = done; $(`[data-pfile="${d.ppick}"]`).click(); return true; }
  if (d.pgo) {
    const v = $(`[data-purl="${d.pgo}"]`).value.trim();
    if (!v) { posterMsg(d.pgo, 'Paste an image address or a file path first.', true); return true; }
    await posterSave(+d.pgo, { source: v }, done);
    return true;
  }
  if (d.pclear) { await posterSave(+d.pclear, { clear: true }, done); return true; }
  return false;
}

// ---------------- problems ----------------
let PR = { rows: [], tab: 'all', done: {}, iss: null, area: 'match', sub: { dupes: 'all', quality: 'lowq', other: 'namediff' }, showIgnored: false, shown: 100, hasFolders: false };
const PR_TABS = [['all', 'All'], ['none', 'Not found'], ['review', 'Ambiguous'], ['fuzzy', 'Spelling differs'], ['noyear', 'No year'], ['online', 'Online match']];
const PR_AREAS = [['match', 'Matching'], ['dupes', 'Duplicates'], ['quality', 'Quality'], ['other', 'Other']];
const PR_SUBS = {
  dupes: [['all', 'All'], ['same', 'Same quality'], ['lower', 'Lower copy'], ['versions', 'Dubbed + original'], ['mixed', 'Different movies?'], ['copies', 'Same file twice'], ['series', 'Series']],
  quality: [['lowq', 'Low quality'], ['unknownq', 'Unknown quality'], ['realq', 'Name vs picture'], ['dubonly', 'Dubbed only']],
  other: [['novideo', 'No video file'], ['namediff', 'Name differs'], ['yeardiff', 'Year differs'], ['runtime', 'Length differs'], ['fatitle', 'Persian title'], ['nonfilm', 'TV, not a film'], ['noposter', 'No poster']],
};
const PR_HELP = {
  all: 'Movies you have more than once. Each copy is marked Keep, Lower quality or Same quality. The app never deletes files: copy the names of the extra copies and remove them yourself, or ignore the movie if you want to keep both. If the copies are really different movies (Vol. 1 and Vol. 2, for example), use Fix match on the file that is the wrong movie.',
  same: 'Two or more copies of the same quality, so one is redundant. Often the same movie listed under two different names.',
  lower: 'A better copy is already in your library next to a lower-quality one. The lower one can usually go.',
  versions: 'One dubbed and one original-audio copy. Not redundant, so nothing to remove; ignore them once you have seen them.',
  mixed: 'Files matched to one IMDb title whose names differ by a number (Vol. 1 / Vol. 2, Star Wars 4 / 6) or that look like bonus material. These are probably different movies: press Fix match on the file that is the wrong movie.',
  series: 'Episodes of a TV series (file names with S01E02, or IMDb says it is a series). They are not copies and are not counted as problems. If one of them is a movie after all, press Not a series.',
  copies: 'The same file name found in more than one movie folder (needs your movie folders in Settings). Keep one, remove the others.',
  lowq: 'Movies where your best copy is SD, 480p or 576p. The most popular come first: the best candidates for an upgrade.',
  unknownq: 'No resolution appears in the file name, so quality cannot be compared or checked.',
  dubonly: 'Movies where every copy is Farsi-dubbed. Useful if you also want the original audio.',
  yeardiff: 'The year in the file name is two or more years from IMDb’s year. The match may be a different movie.',
  namediff: 'The name of the file is not the real name of the film on IMDb: a typo, a missing word, a word that is not part of the name, or another language. The most different names come first. Fix the file name, or use Fix match if it is the wrong film. Files already in Matching (unsure matches) are not repeated here.',
  nonfilm: 'IMDb lists these as TV specials or TV shorts, not films. TV series and mini-series are not problems: they are in the Series section of the main list.',
  realq: 'The file name says one resolution but ffprobe sees a smaller picture: a mislabelled or upscaled file. Needs ffprobe (Settings → General → Movie folders).',
  runtime: 'The length of the file is far from IMDb\u2019s runtime: a cut-short download, a different cut, or the wrong movie. Needs ffprobe.',
  fatitle: 'The Persian title from TMDB is written in Latin letters. Open the movie and use Edit next to Persian to type the right one; your edit is kept.',
  noposter: 'No poster was found for these. Retry downloads the missing ones from IMDb, or use Add a poster to choose an image file, or paste an image address.',
  novideo: 'The folder of this title (in your movie folders) has no video file: it is empty, or holds only junk such as .nfo, .jpg or .url files, or only a sample. Remove the title from your list. This hides it in the library; your list files and folders are not changed, and it stays out of every update. Needs your movie folders in Settings.',
};
const VERDICT = { keep: ['Keep', 'good'], lower: ['Lower quality', 'warn'], same: ['Same quality', 'bad'], unsure: ['Cannot compare', ''] };
const prSub = () => PR.area === 'match' ? null : PR.sub[PR.area];
const prOpen = kind => (PR.iss?.[kind] || []).filter(r => !r.ignored);
const prDupes = () => prOpen('dupes').filter(r => r.sub !== 'series');  // series are listed in their own tab, not counted
const prCount = a => a === 'match' ? PR.rows.length : a === 'dupes' ? prDupes().length
  : a === 'quality' ? prOpen('lowq').length + prOpen('unknownq').length + prOpen('realq').length
  : prOpen('novideo').length + prOpen('namediff').length + prOpen('yeardiff').length + prOpen('runtime').length + prOpen('fatitle').length + prOpen('nonfilm').length + prOpen('noposter').length;
function prIssueRows() {
  const sub = prSub(), kind = PR.area === 'dupes' ? 'dupes' : sub;
  let rows = PR.iss?.[kind] || [];
  if (PR.area === 'dupes') rows = sub === 'all' ? rows.filter(r => r.sub !== 'series') : rows.filter(r => r.sub === sub);
  return { kind, all: rows, rows: PR.showIgnored ? rows : rows.filter(r => !r.ignored) };
}

async function openProblems() {
  $('#dlg').classList.add('wide');
  $('#dlg').dataset.mode = 'problems';
  $('#dlg').innerHTML = `${closeBtn}<div class="pad"><h2 id="dlg-title" dir="auto">${tr('Problems')}</h2><div class="progress" aria-hidden="true"><i style="width:40%"></i></div><p role="status" style="color:var(--dim)">Looking for problems…</p></div>`;
  showModal('dlg-title');
  const [rows, iss, st] = await Promise.all([api('/api/problems'), api('/api/issues'), api('/api/status')]);
  const keep = { area: PR.area, sub: PR.sub };
  PR = { rows, iss, tab: 'all', done: {}, showIgnored: false, shown: 100, hasFolders: !!(st.settings.media_dirs || []).length, ...keep };
  if (keep.area === 'match' && !rows.length && prOpen('novideo').length) { PR.area = 'other'; PR.sub.other = 'novideo'; }  // nothing to match: show what needs removing
  if ($('#dlg').dataset.mode === 'problems') probList();
}

// Fix match is offered on a row when its match may be wrong. With several files the user picks which file is the wrong one
// (the app cannot know: Keep / Lower quality only compares quality), so each file gets its own button and box.
const issueCanFix = r => (PR.area === 'other' && ['namediff', 'yeardiff', 'runtime'].includes(PR.sub.other)) || (PR.area === 'dupes' && r.sub !== 'versions' && r.sub !== 'copies');
const issueHtml = r => `<div class="ck iss${r.ignored ? ' done' : ''}" data-ik="${esc(r.ikey)}">
  <div style="min-width:0">
    <div class="im"><b>${esc(r.title)}</b> ${r.year || ''} <small>${[r.rating ? 'IMDb ' + r.rating : '', r.votes ? fmtVotes(r.votes) + ' votes' : ''].filter(Boolean).join(' · ')}</small>
      ${r.tt ? `<a class="iconlink" href="${ttUrl(r.tt)}" target="_blank" aria-label="Open ${esc(r.title)} on IMDb">${I('ext', 'sm')}</a>` : ''}${googleLink(r.title, r.year)}</div>
    <div class="why${r.sub === 'versions' ? ' calm' : ''}">${r.sub === 'versions' ? '' : I('alert', 'sm') + ' '}${esc(r.note)}</div>
    ${(r.junk || []).map(j => `<div class="ifile" style="margin-top:6px"><span class="q bad">${j.nfiles ? `${j.nfiles} ${j.nfiles === 1 ? 'file' : 'files'}, no video` : 'Empty folder'}</span>${j.size ? `<span class="q">${fmtSize(j.size)}</span>` : ''}<code>${esc(j.path)}</code><button type="button" class="link" data-reveal="${esc(j.path)}">Open folder</button>${j.names.length ? `<span class="dim" dir="auto">${esc(j.names.join(', '))}</span>` : ''}</div>`).join('')}
    <div class="ifiles">${r.files.map(f => `<div class="ifile">${f.verdict ? `<span class="q ${VERDICT[f.verdict][1]}">${VERDICT[f.verdict][0]}</span>` : ''}<span class="q">${esc(f.quality)}</span>${f.size ? `<span class="q">${fmtSize(f.size)}</span>` : ''}<code>${esc(f.raw)}</code>${r.files.length > 1 ? `<button type="button" class="link" data-rmfile="${f.id}">Remove from list</button>` : ''}${r.files.length > 1 && issueCanFix(r) ? `<button type="button" class="link" data-fix="${f.id}">${PR.area === 'dupes' ? 'Not this movie? Fix match' : 'Wrong movie? Fix match'}</button>` : ''}
      ${showInFolder(f.paths)}</div>${r.files.length > 1 ? `<div class="fixbox" data-fixbox="${f.id}"></div>` : ''}`).join('')}</div>
    ${r.files.length > 1 ? '' : `<div class="fixbox" data-fixbox="${r.files[0].id}"></div>`}
    ${r.tt && PR.area === 'other' && PR.sub.other === 'noposter' ? `<div data-pbox="${+r.tt.slice(2)}"></div>` : ''}
  </div>
  <div class="isbtn">
    ${PR.area === 'dupes' && r.sub === 'series' ? `<button type="button" class="btn outline small" data-series="0" data-sk="${esc(r.k)}">Not a series</button>` : PR.area === 'dupes' ? `<button type="button" class="btn outline small" data-series="1" data-sk="${esc(r.k)}">This is a series</button>` : ''}
    <button type="button" class="btn danger small" data-rmrow="${esc(r.ikey)}">${r.files.length > 1 ? 'Remove all from list' : 'Remove from list'}</button>
    ${r.tt && PR.area === 'other' && PR.sub.other === 'noposter' ? `<button type="button" class="btn small" data-padd="${+r.tt.slice(2)}">Add a poster…</button>` : ''}
    ${ALL.some(m => m.k === r.k) ? `<button type="button" class="btn outline small" data-open="${esc(r.k)}">Open movie</button>` : ''}
    ${r.sub !== 'mixed' && r.sub !== 'copies' && r.files.some(f => f.verdict === 'lower' || f.verdict === 'same') ? `<button type="button" class="btn outline small" data-copy="${esc(r.ikey)}">${I('copy', 'sm')}Copy ${r.files.some(f => f.paths?.length) ? 'paths' : 'names'} to remove</button>` : ''}
    ${r.sub === 'copies' ? `<button type="button" class="btn outline small" data-copy="${esc(r.ikey)}">${I('copy', 'sm')}Copy extra paths</button>` : ''}
    ${PR.area === 'quality' && PR.sub.quality === 'lowq' ? `<button type="button" class="btn outline small" data-pwish="${esc(r.ikey)}">Add to wishlist</button>` : ''}
    ${r.files.length === 1 && issueCanFix(r) ? `<button type="button" class="btn outline small" data-fix="${r.files[0].id}">${PR.area === 'dupes' ? 'Not the same movie? Fix match' : 'Wrong movie? Fix match'}</button>` : ''}
    ${r.ignored ? `<button type="button" class="btn ghost small" data-restore="${esc(r.ikey)}">Restore</button>` : `<button type="button" class="btn ghost small" data-ign="${esc(r.ikey)}">Ignore</button>`}
  </div></div>`;

function probMatching() {
  const n = k => k === 'all' ? PR.rows.length : PR.rows.filter(r => r.conf === k).length;
  const rows = PR.rows.filter(r => PR.tab === 'all' || r.conf === PR.tab);
  const cand = (c, fid) => `<div class="cand"><div><b>${esc(c.title)}</b> ${c.year || ''}<small>${esc([TYPE_LABEL[c.type] || c.type, c.orig, c.rating ? 'IMDb ' + c.rating : '', c.votes ? fmtVotes(c.votes) + ' votes' : '', pad7(c.tconst)].filter(Boolean).join(' · '))}</small></div>
      <a class="btn ghost small" href="${ttUrl(pad7(c.tconst))}" target="_blank">View</a><button type="button" class="btn small" data-use="${c.tconst}" data-f="${fid}">Use this</button></div>`;
  return `<p class="pr-help">These names could not be matched for sure: a different name on IMDb, a wrong year, or several movies with the same title. Confirm the current match, pick another, or search.</p>
    <div class="tabs" role="group" aria-label="Show">${PR_TABS.filter(([k]) => k === 'all' || n(k)).map(([k, l]) => `<button type="button" class="chip${PR.tab === k ? ' on' : ''}" aria-pressed="${PR.tab === k}" data-ptab="${k}">${l}<small>${n(k)}</small></button>`).join('')}</div>
    <div class="ck-list">${rows.map(r => `<div class="ck${PR.done[r.id] ? ' done' : ''}" style="grid-template-columns:1fr auto" data-prow="${r.id}">
      <div style="min-width:0">
        <div class="nm">${esc(r.raw)} ${googleLink(r.title || r.raw, r.year)}</div>
        ${showInFolder(r.paths) ? `<div class="ifile" style="margin-top:4px">${showInFolder(r.paths)}</div>` : ''}
        <div class="why">${I('alert', 'sm')} ${esc(r.reason)}</div>
        ${PR.done[r.id] ? `<div class="im" style="color:var(--good)">${esc(PR.done[r.id])}</div>` : `
        ${r.current ? `<div class="im" style="margin-top:6px">Now matched to: <a href="${ttUrl(pad7(r.current.tconst))}" target="_blank">${esc(r.current.title)}</a> ${r.current.year || ''} <small>${[r.current.rating ? 'IMDb ' + r.current.rating : '', r.current.votes ? fmtVotes(r.current.votes) + ' votes' : ''].filter(Boolean).join(' · ')}</small>
          <button type="button" class="btn small" style="margin-left:6px" data-accept="${r.id}">${I('check', 'sm')}Looks right</button></div>` : ''}
        ${r.cands.length ? `<div class="sub" style="margin-top:6px">${r.current ? 'Or maybe' : 'Possible matches'}:</div>${r.cands.map(c => cand(c, r.id)).join('')}` : ''}
        <div style="margin-top:6px"><button type="button" class="btn ghost small" data-fix="${r.id}">Search IMDb…</button> <button type="button" class="btn danger small" data-rmmatch="${r.id}">Remove from list</button></div>
        <div class="fixbox" data-fixbox="${r.id}"></div>`}
      </div>
      <span class="st ${r.conf === 'none' ? 'unmatched' : 'better'}">${esc((CONF_LABEL[r.conf] || [r.conf])[0].replace(/ –.*/, ''))}</span></div>`).join('') || '<div style="color:var(--good)">Nothing to check. Every title is matched.</div>'}</div>`;
}

function probIssues() {
  const sub = prSub(), { kind, all, rows } = prIssueRows();
  const subCount = s => (PR.area === 'dupes' ? (s === 'all' ? prDupes() : prOpen('dupes').filter(r => r.sub === s)) : prOpen(s)).length;
  const hidden = all.length - all.filter(r => !r.ignored).length;
  return `<p class="pr-help">${esc(PR_HELP[sub])}</p>
    ${PR.area === 'dupes' && PR.iss.wasted ? `<p class="pr-help"><b>${fmtSize(PR.iss.wasted)}</b> of disk space is taken by redundant copies (from your movie folders).</p>` : ''}
    <div class="tabs" role="group" aria-label="Show">${PR_SUBS[PR.area].map(([k, l]) => `<button type="button" class="chip${sub === k ? ' on' : ''}" aria-pressed="${sub === k}" data-psub="${k}">${l}<small>${subCount(k).toLocaleString()}</small></button>`).join('')}</div>
    ${hidden || PR.showIgnored ? `<label class="check prign"><input type="checkbox" id="pr-ign-toggle"${PR.showIgnored ? ' checked' : ''}><span>Show ignored (${hidden})</span></label>` : ''}
    ${kind === 'novideo' && rows.length ? `<div class="btns" style="margin-bottom:10px"><button type="button" class="btn danger" id="pr-rmall">Remove all ${rows.length.toLocaleString()} from list</button></div>` : ''}
    ${kind === 'noposter' && rows.length ? `<div class="btns" style="margin-bottom:10px"><button type="button" class="btn" id="pr-posters">${I('download', 'sm')}Retry missing posters</button></div>` : ''}
    <div class="ck-list">${rows.slice(0, PR.shown).map(issueHtml).join('') || `<div style="color:var(--good)">${all.length ? 'Everything here is ignored.' : 'Nothing here. All good.'}</div>`}</div>
    ${rows.length > PR.shown ? `<div style="margin-top:10px"><button type="button" class="btn ghost" id="pr-more">Show more (${(rows.length - PR.shown).toLocaleString()} left)</button></div>` : ''}`;
}

function probList() {
  const top = $('#dlg').scrollTop;
  const area = PR.area;
  $('#dlg').innerHTML = `${closeBtn}<div class="pad">
    <h2 id="dlg-title" dir="auto">${tr('Problems')}</h2>
    <div class="stabs" role="tablist" aria-label="Kinds of problems">${PR_AREAS.map(([k, l]) => `<button type="button" role="tab" aria-selected="${area === k}" tabindex="${area === k ? 0 : -1}" data-parea="${k}">${tr(l)}<span class="n" aria-label="${prCount(k)} items">${prCount(k).toLocaleString()}</span></button>`).join('')}</div>
    ${area === 'match' ? probMatching() : probIssues()}
    ${PR.hasFolders ? '' : '<p class="pr-help">Your movie folder is not set, so only your list is checked. Set it in Settings → General to also find folders with no movie.</p>'}
    <div class="bar-actions"><button type="button" class="btn outline" id="pr-rescan" title="Read your list files and movie folders again and rebuild this list">Check again</button><span class="grow">${area === 'match' ? 'Fixes are saved right away and never overwritten by updates.'  : area === 'other' && PR.sub.other === 'novideo' ? 'Removing only hides a title in the library. Your list files and folders are not changed.' : 'Nothing here changes your files. Ignored items stay ignored until their files change.'}</span>
      ${area === 'match' ? '<a class="btn ghost" href="/api/problems.csv" data-csv="MovieShelf problems.csv">Export list (CSV)</a>'  : `<a class="btn ghost" href="/api/issues.csv?kind=${prIssueRows().kind}${PR.area === 'dupes' ? '&sub=' + prSub() : ''}" data-csv="MovieShelf problems ${prIssueRows().kind}.csv">Export list (CSV)</a>`}</div>
  </div>`;
  $('#dlg').scrollTop = top;
}

function probDone(fid, msg) {
  PR.done[fid] = msg;
  const row = $(`[data-prow="${fid}"]`);
  if (!row) return;
  row.classList.add('done');
  $('.fixbox', row)?.remove();
  $$('button', row).forEach(b => b.disabled = true);
  row.querySelector('div').insertAdjacentHTML('beforeend', `<div class="im" role="status" style="color:var(--good)">${esc(msg)}</div>`);
}

async function copyText(text) {
  try { await navigator.clipboard.writeText(text); return true; } catch { }
  const ta = Object.assign(document.createElement('textarea'), { value: text });
  document.body.append(ta); ta.select();
  const ok = document.execCommand('copy'); ta.remove();
  return ok;
}

// ---------------- job polling ----------------
let LAST_JOB = null, lastLoad = 0, pollTimer, wasRunning = null, dismissedErr = localStorage.getItem('dismissedErr');
async function poll() {
  clearTimeout(pollTimer);  // poll() may be called early after an action: keep a single loop
  try {
    const st = await api('/api/status');
    const j = st.job;
    LAST_JOB = j;
    if (!ALL.length && !j.running && j.error) render(0);  // the empty screen explains what to do
    const el = $('#job');
    const showErr = j.error && !j.running && j.error !== dismissedErr;
    if (j.running || showErr) {
      el.classList.remove('hidden');
      el.classList.toggle('err', !!showErr);
      $('.bar', el).classList.toggle('hidden', !!showErr);
      $('.bar i', el).style.width = j.total ? `${Math.min(100, j.done / j.total * 100)}%` : '5%';
      $('.msg', el).textContent = showErr ? `The last update stopped: ${j.error}` : j.stopping ? 'Stopping…' : `${j.stage}${j.total ? ` — ${j.done.toLocaleString()} / ${j.total.toLocaleString()}` : ''}`;
      $('#jobStop').classList.toggle('hidden', !!showErr || !j.stoppable || j.stopping);
      $('#jobX').classList.toggle('hidden', !showErr);
      $('#jobLog').classList.toggle('hidden', !showErr);
    } else el.classList.add('hidden');
    if (wasRunning && !j.running && !QUIET) j.error ? toast('Update stopped with an error. See the log in Settings.', 'err') : toast(j.stopped ? 'Update stopped' : 'Update finished');
    if (wasRunning && !j.running && PR.recheck && $('#dlg').dataset.mode === 'problems') { PR.recheck = false; await load(true); openProblems(); }
    wasRunning = j.running;
    const log = $('#s-log'); if (log && j.log?.length) log.textContent = j.log.join('\n');
    const pc = st.stats.problems + (st.stats.duplicates || 0) + (st.stats.novideo || 0);
    $('#probCount').textContent = pc > 999 ? '999+' : pc;
    $('#probCount').classList.toggle('hidden', !pc);
    $('#wishCount').textContent = st.stats.wishlist > 99 ? '99+' : st.stats.wishlist;
    $('#wishCount').classList.toggle('hidden', !st.stats.wishlist);
    $('#probBtn').setAttribute('aria-label', `Problems: ${pc} to look at`);
    if (st.version !== version) {
      // during long jobs reload at most every 20s so scrolling isn't disturbed
      if (!j.running || Date.now() - lastLoad > 20000) { lastLoad = Date.now(); await load(true); }
    }
    pollTimer = setTimeout(poll, j.running ? 1500 : 4000);
  } catch { pollTimer = setTimeout(poll, 5000); }
}
$('#jobX').addEventListener('click', () => {
  dismissedErr = $('#job .msg').textContent.replace('The last update stopped: ', '');
  try { localStorage.setItem('dismissedErr', dismissedErr); } catch { }
  $('#job').classList.add('hidden');
});
$('#jobLog').addEventListener('click', () => openSettings('log'));
$('#jobStop').addEventListener('click', async () => {
  $('#jobStop').classList.add('hidden');
  await api('/api/job/stop', {});
  poll();
});

// ---------------- events ----------------
let qTimer;
$('#q').addEventListener('input', e => { clearTimeout(qTimer); qTimer = setTimeout(() => { F.q = e.target.value; apply(); }, 120); });
function setView(v) { viewMode = v; $$('#view button').forEach(b => { b.classList.toggle('on', b.dataset.v === v); b.setAttribute('aria-pressed', b.dataset.v === v); }); }
$('#view').addEventListener('click', e => { const b = e.target.closest('[data-v]'); if (!b) return; setView(b.dataset.v); localStorage.setItem('view', viewMode); render(); });
$('#side').addEventListener('click', e => {
  const c = e.target.closest('.chip'); if (!c) return;
  const set = F[c.dataset.k], v = c.dataset.v;
  set.has(v) ? set.delete(v) : set.add(v);
  c.classList.toggle('on', set.has(v));
  c.setAttribute('aria-pressed', set.has(v));
  apply();
});
function toggleSide(open) {
  $('#side').classList.toggle('open', open);
  $('#scrim').classList.toggle('hidden', !open);
  $('#filtersBtn').setAttribute('aria-expanded', open);
  if (open) $('#side button, #side input')?.focus();
}
$('#filtersBtn').addEventListener('click', () => toggleSide(!$('#side').classList.contains('open')));
$('#scrim').addEventListener('click', () => toggleSide(false));
$('#yMin').addEventListener('change', e => { F.yMin = +e.target.value || null; apply(); });
$('#yMax').addEventListener('change', e => { F.yMax = +e.target.value || null; apply(); });
$('#rMin').addEventListener('input', e => { F.rMin = +e.target.value; $('#rMinLbl').textContent = F.rMin ? F.rMin : tr('any'); apply(); });
$('#vMin').addEventListener('input', e => { F.vMin = +e.target.value; $('#vMinLbl').textContent = F.vMin ? fmtVotes(Math.pow(10, F.vMin)) : tr('any'); apply(); });
for (const k of ['rtMin', 'mcMin']) $('#' + k).addEventListener('input', e => { F[k] = +e.target.value; $(`#${k}Lbl`).textContent = F[k] ? F[k] + (k === 'rtMin' ? '%' : '') : tr('any'); apply(); });
$('#f-country').addEventListener('change', e => { F.country = e.target.value; apply(); });
$('#f-lang').addEventListener('change', e => { F.lang = e.target.value; apply(); });
function resetFilters() {
  for (const k of ['status', 'genre', 'decade', 'runtime', 'res', 'src', 'tech', 'type']) F[k].clear();
  Object.assign(F, { q: '', yMin: null, yMax: null, rMin: 0, vMin: 0, rtMin: 0, mcMin: 0, country: '', lang: '', person: null });
  $('#q').value = ''; $('#yMin').value = ''; $('#yMax').value = ''; $('#rMin').value = 0; $('#vMin').value = 0; $('#rtMin').value = 0; $('#mcMin').value = 0;
  $$('#rMinLbl, #vMinLbl, #rtMinLbl, #mcMinLbl').forEach(e => e.textContent = tr('any'));
  buildFacets(); apply();
}
$('#reset').addEventListener('click', resetFilters);
$('#kind').addEventListener('click', e => {
  const b = e.target.closest('[data-kind]');
  if (!b || b.dataset.kind === KIND) return;
  KIND = b.dataset.kind;
  try { localStorage.setItem('kind', KIND); } catch { }
  buildFacets(); apply();
});
$('#empty').addEventListener('click', e => {
  if (e.target.id === 'emptyReset') resetFilters();
  if (e.target.id === 'emptySettings') openSettings('general');
  if (e.target.id === 'emptyLoad') openLoad();
  if (e.target.closest('[data-emptyhelp]')) openHelp('welcome');
});
$('#active').addEventListener('click', e => {
  const k = e.target.closest('[data-rm]')?.dataset.rm; if (!k) return;
  if (k.startsWith('q:')) { F.q = F.q.replace(k.slice(2), '').replace(/\s+/g, ' ').trim(); $('#q').value = F.q; return apply(); }
  if (k === 'person') F.person = null;
  else if (k === 'year') { F.yMin = F.yMax = null; $('#yMin').value = $('#yMax').value = ''; }
  else if (k === 'rMin') { F.rMin = 0; $('#rMin').value = 0; $('#rMinLbl').textContent = tr('any'); }
  else if (k === 'vMin') { F.vMin = 0; $('#vMin').value = 0; $('#vMinLbl').textContent = tr('any'); }
  else if (k === 'rtMin' || k === 'mcMin') { F[k] = 0; $('#' + k).value = 0; $(`#${k}Lbl`).textContent = tr('any'); }
  else if (k === 'country' || k === 'lang') F[k] = '';
  else { const [kk, ...v] = k.split(':'); F[kk].delete(v.join(':')); }
  buildFacets(); apply();
});
const openFromList = el => {
  if (SELMODE) return selClick(el); if (window.innerWidth <= 900) toggleSide(false); openMovie(ALL[+el.dataset.i]); };
document.addEventListener('click', e => {
  const gh = e.target.closest('.grp button, .grp-row button');
  if (gh) return toggleGroup(gh.closest('[data-g]').dataset.g);
  const card = e.target.closest('.card, tr[data-i]');
  if (card && !e.target.closest('.modal')) return SELMODE ? selClick(card, e) : openFromList(card);
});
document.addEventListener('keydown', e => {
  if ((e.key === 'Enter' || e.key === ' ') && e.target.matches?.('.card, tr[data-i]')) { e.preventDefault(); SELMODE ? selClick(e.target, e) : openFromList(e.target); }
});
$('#table thead').addEventListener('click', e => {
  const th = e.target.closest('th[data-sort]'); if (!th) return;
  const v = th.dataset.sort;
  if (v === $('#sort').value) REV = !REV;  // second click on the same column: other direction
  else { $('#sort').value = v; REV = false; }
  saveSort(); apply();
});
$('#scroll').addEventListener('scroll', () => $('#toTop').classList.toggle('hidden', $('#scroll').scrollTop < 1500), { passive: true });
$('#toTop').addEventListener('click', () => {
  $('#scroll').scrollTo({ top: 0, behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
  $('#q').focus({ preventScroll: true });
});
$('#random').addEventListener('click', openWatchNext);
$('#statsBtn').addEventListener('click', openStats);
$('#wishBtn').addEventListener('click', openWishlist);
$('#collBtn').addEventListener('click', openCollections);
$('#exportBtn').addEventListener('click', openExport);
$('#checkBtn').addEventListener('click', openCheck);
$('#loadBtn').addEventListener('click', openLoad);
$('#probBtn').addEventListener('click', openProblems);
$('#settingsBtn').addEventListener('click', () => openSettings());

// ---------------- dialog clicks: one handler per dialog (#dlg.dataset.mode) ----------------
// "Fix match" boxes appear in the movie details and in the problem list
async function fixClick(t, onSaved) {
  if (t.dataset.fix) { openFix(+t.dataset.fix); return true; }
  if (t.dataset.fgo) { openFix(+t.dataset.fgo, $(`[data-fq="${t.dataset.fgo}"]`).value); return true; }
  if (t.dataset.use !== undefined && t.dataset.f) {
    if (!t.dataset.use && !await confirmBox({ title: 'Mark as not on IMDb?', text: 'This file keeps no IMDb data (no poster, cast or ratings). You can fix it again later.', ok: 'Mark as not on IMDb' })) return true;
    t.disabled = true; t.textContent = 'Saving…';
    await api('/api/match/' + t.dataset.f, { imdb: t.dataset.use ? 'tt' + String(t.dataset.use).replace(/^tt/, '') : '' });
    onSaved(t);
    return true;
  }
  return false;
}

async function testKey(kind, okMsg) {
  const key = $(`#s-${kind}`).value.trim(); if (!key) return;
  const msg = $(`#s-${kind}-msg`);
  msg.textContent = 'Testing…';
  const r = await api(`/api/${kind}/test`, { key });
  if (r.ok) { await api('/api/settings', { [`${kind}_key`]: key }); msg.textContent = okMsg; toast('Key saved'); $(`#s-${kind}`).value = ''; }
  else msg.textContent = 'Key rejected' + (r.error ? ': ' + r.error : '');
}

const DIALOG_CLICKS = {
  async help(t) {
    if (t.dataset.hlang !== undefined) return setLang(isFa() ? 'en' : 'fa');
  },

  async wn(t) {
    if (t.id === 'wn-shuffle') return wnView(true);
    if (t.dataset.wopen) { const m = ALL.find(x => x.k === t.dataset.wopen); if (m) { closeModal(); openMovie(m); } return; }
    if (t.dataset.wseen) {
      const m = ALL.find(x => x.k === t.dataset.wseen);
      await setUser(m, { watched: 1 });
      WN.picks = WN.picks.filter(x => x !== m); toast(`Marked watched: ${m.en || m.title}`);
      if (WN.unwatched && wnPool().length > WN.picks.length) { const more = wnPool().filter(x => !WN.picks.includes(x)); if (more.length) WN.picks.push(more[Math.floor(Math.random() * more.length)]); }
      wnView(false); buildFacets();
    }
  },

  async movie(t, e) {
    const faRowEl = t.closest?.('.farow');
    if (faRowEl) {
      const m = ALL.find(x => x.k === $('#dlg').dataset.k), tc = +faRowEl.dataset.tc;
      if (t.dataset.faEdit !== undefined) return faEdit(faRowEl, m);
      if (t.dataset.faCancel !== undefined) { faRowEl.outerHTML = faRow(m); return; }
      const send = async title => { await api('/api/fatitle', { tconst: tc, title }); toast('Persian title saved'); await load(true); const m2 = ALL.find(x => x.k === m.k); if (m2) openMovie(m2); };
      if (t.dataset.faSave !== undefined) return send($('.fa-in', faRowEl).value);
      if (t.dataset.faReset !== undefined) return send(null);
    }
    if (t.dataset.reveal) return reveal(t.dataset.reveal);
    if (t.dataset.rm) return removeFromList([+t.dataset.rm], () => closeModal());
    if (t.dataset.rmall) { const mv = ALL.find(x => x.k === $('#dlg').dataset.k); return mv && removeFromList(mv.files.map(f => f.id), () => closeModal()); }
    if (await posterClick(t, async () => { const k = $('#dlg').dataset.k; await load(true); const m2 = ALL.find(x => x.k === k); if (m2) openMovie(m2); })) return;
    const m = ALL.find(x => x.k === $('#dlg').dataset.k);
    const p = t.closest('[data-pid]');
    if (p) { PERSON_FROM = $('#dlg').dataset.k; return openPerson(+p.dataset.pid, p.dataset.pname); }
    if (t.dataset.genre) { F.genre.clear(); F.genre.add(t.dataset.genre); closeModal(); buildFacets(); apply(); return; }
    if (t.dataset.search) { e.preventDefault(); $('#q').value = F.q = t.dataset.search; closeModal(); apply(); return; }
    if (t.dataset.u && m) { const v = m[t.dataset.u] ? 0 : 1; t.classList.toggle('on', !!v); t.setAttribute('aria-pressed', !!v); await setUser(m, { [t.dataset.u]: v }); buildFacets(); return; }
    if (t.dataset.r && m) {
      const v = m.myrating === +t.dataset.r ? null : +t.dataset.r;
      $$('.myr button').forEach((s, i) => { s.classList.toggle('on', v > i); s.setAttribute('aria-pressed', v === i + 1); });
      $('.myr .lbl').textContent = v ? v + '/10' : 'My rating';
      await setUser(m, { myrating: v }); return;
    }
    await fixClick(t, b => { closeModal(); toast(b.dataset.use ? 'Match saved' : 'Marked as not on IMDb'); setTimeout(() => load(true), 1200); });
  },

  stats(t) {
    const sf = t.closest('[data-sf]'); if (sf) { resetFilters(); F[sf.dataset.sf].add(sf.dataset.sv); closeModal(); buildFacets(); apply(); return; }
    const sp = t.closest('[data-sp]'); if (sp) { PERSON_FROM = null; return openPerson(+sp.dataset.sp, sp.dataset.spn); }
    const sc = t.closest('[data-sc]'); if (sc) { F.country = sc.dataset.sc; closeModal(); buildFacets(); apply(); }
  },

  async wish(t) {
    if (t.dataset.wtab) { WTAB = t.dataset.wtab; return wishView(); }
    if (t.id === 'w-add') return wishAddFromInput();
    if (t.dataset.wset) { await api('/api/wishlist/set', { id: +t.dataset.wset, status: t.dataset.st }); WISH = await api('/api/wishlist'); wishView(); return poll(); }
    if (t.dataset.wdel) { await api('/api/wishlist/set', { id: +t.dataset.wdel, delete: true }); WISH = await api('/api/wishlist'); wishView(); return poll(); }
    if (t.dataset.wcopy !== undefined) { const w = WISH.filter(x => x.status === 'wanted'); return toast(w.length && await copyText(wishText(w)) ? `Copied ${w.length} titles` : 'Nothing to copy', w.length ? 'ok' : 'info'); }
    if (t.dataset.wsave) {
      const w = WISH.filter(x => x.status === 'wanted');
      if (t.dataset.wsave === 'txt') return stash('MovieShelf wishlist.txt', wishText(w) + '\n');
      const q = v => '"' + String(v ?? '').replace(/"/g, '""') + '"';
      return stash('MovieShelf wishlist.csv', 'Title,Year,Status,Source,Note,Added\r\n' + WISH.map(x => [x.title, x.year, x.status, x.source, x.note, x.added].map(q).join(',')).join('\r\n') + '\r\n');
    }
  },

  async coll(t) {
    if (t.dataset.ctab) { CTAB = t.dataset.ctab; return collView(); }
    if (t.id === 'c-fetch') { await api('/api/collections/fetch', {}); toast('Checking collections on TMDB…', 'info'); closeModal(); return poll(); }
    if (t.dataset.copen) { const m = ALL.find(x => x.k === t.dataset.copen); if (m) { closeModal(); openMovie(m); } return; }
    if (t.dataset.csearch) { closeModal(); resetFilters(); $('#q').value = F.q = `collection:"${t.dataset.csearch}"`; return apply(); }
    if (t.dataset.cwish) {
      const c = COLL.collections[+t.dataset.cwish];
      return wishAdd(c.missing.map(m => ({ title: m.title, year: m.year, source: 'Collection', note: c.name })));
    }
  },

  async rename(t) {
    if (t.dataset.rnadd) { const i = $('#rn-pattern'); i.value = i.value + t.dataset.rnadd; return i.focus(); }
    if (t.dataset.rnpreview !== undefined) return openRename($('#rn-pattern').value.trim());
    if (t.dataset.rnreset !== undefined) return openRename('{title} ({year}) [{res} {src}]');
    if (t.dataset.rplan) { const r = await api('/api/rename-plan/file', { kind: t.dataset.rplan, pattern: $('#rn-pattern')?.value.trim() }); return deliver(r); }
  },

  async export(t) {
    if (t.id === 'ex-go') return exGo(t);
    const info = key => EXCOLS.find(x => x.key === key);
    if (t.dataset.expreset) { const k = t.dataset.expreset; $$('#ex [data-excol]').forEach(c => { c.checked = k === 'all' || (k === 'essential' && info(c.dataset.excol).default); }); return exUpdate(); }
    if (t.dataset.exgrp) { const boxes = $$('#ex [data-excol]').filter(c => info(c.dataset.excol).group === t.dataset.exgrp); const all = boxes.every(c => c.checked); boxes.forEach(c => { c.checked = !all; }); return exUpdate(); }
  },

  async settings(t) {
    if (t.dataset.act === 'export') return openExport();
    if (t.id === 's-media-add') { const p = await pickNative('folder'); if (p) { const a = $('#s-media'); a.value = (a.value.trim() ? a.value.trim() + '\n' : '') + p; return saveFolders(); } else if (p === undefined) $('#s-media').focus(); return; }
    if (t.id === 's-probe-now') { await api('/api/probe/start', {}); toast('Reading the files with ffprobe…', 'info'); return poll(); }
    if (t.dataset.act === 'peoplescan') { const r = await api('/api/people/scan', { scope: $('#s-pscope').value }); toast(r.started ? 'Scanning people… progress shows at the top.' : 'Queued: it runs after the current job.', 'info'); return poll(); }
    if (t.dataset.act === 'renameplan') return openRename();
    if (t.dataset.act === 'fullsave') return openFullSave();
    if (t.dataset.act === 'fullload') return openFullLoad();
    if (t.dataset.act === 'backup') return deliver(await api('/api/backup/make', {}));
    if (t.dataset.act === 'import') return $('#s-import').click();
    if (t.dataset.act === 'restorebk') {
      if (!await confirmBox({ title: 'Restore this backup?', text: 'Favorites, ratings, notes, views and list names from the backup are merged into your library. Nothing is deleted. Names missing from your lists are written back to a list file.', ok: 'Restore' })) return;
      const r = await api('/api/backup/import', { name: $('#s-bk').value });
      if (r.error) return toast(r.error, 'err');
      toast(r.restored?.lists ? `Backup restored. ${r.restored.lists} list names came back.` : 'Backup restored'); await loadViews(); poll(); return setTimeout(() => load(true), 800);
    }
    if (t.dataset.stab) {
      STAB = t.dataset.stab;
      $$('[data-stab]').forEach(b => { const on = b.dataset.stab === STAB; b.setAttribute('aria-selected', on); b.tabIndex = on ? 0 : -1; });
      $$('.spane').forEach(p => { p.hidden = p.id !== 'sp-' + STAB; });
      return;
    }
    if (t.dataset.pref) return setPref(t.dataset.pref, t.dataset.val);
    if (t.id === 's-tmdb-save') return testKey('tmdb', 'Key works and is saved. Click “Scan list & update” to fetch details.');
    if (t.id === 's-omdb-save') return testKey('omdb', 'Key works and is saved. Click “Update RT & Metacritic now”.');
    if (t.dataset.act) {
      const url = { update: '/api/update', posters: '/api/posters', rematch: '/api/rematch', refresh: '/api/update', ratings: '/api/ratings', scores: '/api/scores' }[t.dataset.act];
      if (t.dataset.act === 'rematch' && !await confirmBox({ title: 'Re-match everything?', text: 'Every automatically matched file is matched again. Matches you set yourself are kept. This takes a few minutes.', ok: 'Re-match' })) return;
      if (t.dataset.act === 'refresh' && !await confirmBox({ title: 'Download all IMDb data again?', text: 'About 1.9 GB is downloaded and the index is rebuilt (10–20 minutes). Your matches and notes are kept.', ok: 'Download' })) return;
      const r = await api(url, { force_download: t.dataset.act === 'refresh' });
      toast(r.started ? `${t.textContent.trim()}: started` : `${t.textContent.trim()}: queued, runs after the current job`, 'info');
      poll();
    }
  },

  loadlist: t => llClick(t),

  async check(t) {
    if (t.dataset.ck === 'file') {
      const path = await pickNative('file');
      if (path) ckStart({ path }); else if (path === undefined) $('#ck-file').click();
      return;
    }
    if (t.dataset.ck === 'folder') {
      const path = await pickNative('folder');
      if (path) ckStart({ path });
      else if (path === undefined) { $('#ck-path').focus(); ckMsg('Type the folder path (for example D:\\Movies) and press Check.'); }
      return;
    }
    if (t.id === 'ck-go') { const v = $('#ck-path').value.trim(); if (v) ckStart({ path: v }); else $('#ck-path').focus(); return; }
    if (t.id === 'ck-paste') { const v = $('#ck-text').value.trim(); if (v) ckStart({ text: v, name: 'pasted list' }); else $('#ck-text').focus(); return; }
    if (t.id === 'ck-wish') {
      const items = [...CK.sel].map(i => CK.res.items[i]).filter(it => ['new', 'better', 'version'].includes(it.status))
        .map(it => ({ title: it.imdb?.title || it.raw, year: it.imdb?.year || null, tconst: it.tconst || null, source: 'List check', note: CK_STATUS[it.status][0] }));
      return items.length ? wishAdd(items) : toast('Select movies that are new or better quality first', 'info');
    }
    if (t.id === 'ck-again') { CK.res = null; return ckPickView(); }
    if (t.dataset.cktab) { CK.tab = t.dataset.cktab; CK.shown = 200; return ckResults(); }
    if (t.id === 'ck-more') { CK.shown += 300; return ckResults(); }
    if (t.dataset.ckall !== undefined) { ckVisible().forEach(({ it, i }) => t.dataset.ckall === '1' && ckAddable(it) ? CK.sel.add(i) : CK.sel.delete(i)); return ckResults(); }
    if (t.id === 'ck-add') return ckAdd(t);
  },

  async problems(t) {
    if (t.dataset.parea) { PR.area = t.dataset.parea; PR.shown = 100; return probList(); }
    if (t.dataset.psub) { PR.sub[PR.area] = t.dataset.psub; PR.shown = 100; return probList(); }
    if (t.dataset.ptab) { PR.tab = t.dataset.ptab; return probList(); }
    if (t.id === 'pr-more') { PR.shown += 200; return probList(); }
    if (t.id === 'pr-posters') { await api('/api/posters', {}); toast('Looking for the missing posters…', 'info'); poll(); return; }
    if (t.dataset.reveal) return reveal(t.dataset.reveal);
    if (t.dataset.pwish) {
      const r = prIssueRows().rows.find(x => x.ikey === t.dataset.pwish);
      return wishAdd([{ title: r.title, year: r.year, tconst: r.tt ? +r.tt.slice(2) : null, source: 'Upgrade', note: r.note }]);
    }
    if (t.id === 'pr-rescan') {
      if (!await recheckNow()) return;
      PR.recheck = true; wasRunning = true; t.disabled = true; t.textContent = 'Checking…';
      return poll();
    }
    if (t.dataset.series) {
      const mark = t.dataset.series === '1', k = t.dataset.sk;
      await api('/api/issue/dismiss', { ikey: `${mark ? 'series' : 'noseries'}:${k}` });
      await api('/api/issue/dismiss', { ikey: `${mark ? 'noseries' : 'series'}:${k}`, undo: true });
      PR.iss = await api('/api/issues'); probList(); poll();
      return toast(mark ? 'Marked as a series: it is not a duplicate any more' : 'Not a series: it is checked for duplicates again', 'info');
    }
    if (t.dataset.rmrow || t.dataset.rmfile || t.dataset.rmmatch || t.id === 'pr-rmall') {
      const ids = t.dataset.rmfile ? [+t.dataset.rmfile] : t.dataset.rmmatch ? [+t.dataset.rmmatch]
        : prIssueRows().rows.filter(r => t.id === 'pr-rmall' ? r.junk_ids : r.ikey === t.dataset.rmrow).flatMap(r => r.junk_ids || r.files.map(f => f.id));
      const ok = await removeFromList(ids, async () => { [PR.iss, PR.rows] = await Promise.all([api('/api/issues'), api('/api/problems')]); });
      if (ok) probList();
      return;
    }
    if (await posterClick(t, async () => { PR.iss = await api('/api/issues'); probList(); poll(); })) return;
    if (t.dataset.open) { const m = ALL.find(x => x.k === t.dataset.open); if (m) { closeModal(); openMovie(m); } return; }
    if (t.dataset.ign !== undefined || t.dataset.restore !== undefined) {
      const ikey = t.dataset.ign ?? t.dataset.restore, undo = t.dataset.restore !== undefined;
      await api('/api/issue/dismiss', { ikey, undo });
      for (const k of Object.keys(PR.iss)) if (Array.isArray(PR.iss[k])) PR.iss[k].forEach(r => { if (r.ikey === ikey) r.ignored = !undo; });
      probList(); poll();
      return toast(undo ? 'Restored' : 'Ignored. Show it again with "Show ignored".', 'info');
    }
    if (t.dataset.copy) {
      const r = PR.iss.dupes.find(x => x.ikey === t.dataset.copy);
      const pick = r?.sub === 'copies' ? (r.files || []).filter(f => f.paths?.length > 1).flatMap(f => f.paths.slice(1))
        : (r?.files || []).filter(f => f.verdict === 'lower' || f.verdict === 'same').flatMap(f => f.paths?.length ? f.paths : [f.raw]);
      const names = pick;
      return toast(names.length && await copyText(names.join('\n')) ? `Copied ${names.length} file ${names.length === 1 ? 'name' : 'names'}` : 'Could not copy', names.length ? 'ok' : 'err');
    }
    if (t.dataset.accept) {
      t.disabled = true;
      await api('/api/accept/' + t.dataset.accept, {});
      return probDone(+t.dataset.accept, 'Confirmed');
    }
    await fixClick(t, b => {
      if (PR.area !== 'match') { b.textContent = 'Used'; b.closest('[data-ik]')?.classList.add('done'); toast('Match updated'); setTimeout(() => load(true), 1500); return; }
      b.textContent = b.dataset.use ? 'Used' : 'Done';
      probDone(+b.dataset.f, b.dataset.use ? 'Fixed' : 'Marked as not on IMDb');
      setTimeout(() => load(true), 1500);
    });
  },
};

$('#modal').addEventListener('click', async e => {
  const t0 = e.target;
  if (t0 === $('#modal') || t0.closest('[data-close]')) return closeModal();
  // act on the control that was clicked, not an icon inside it
  const t = t0.closest('button, a, input, [data-pid]') || t0;
  await DIALOG_CLICKS[$('#dlg').dataset.mode]?.(t, e);
});
$('#modal').addEventListener('input', e => { if (e.target.closest?.('#ex')) exUpdate(); });
$('#modal').addEventListener('change', async e => {
  if (e.target.closest?.('#ex')) exUpdate();
  if (e.target.id === 'notes') { const m = ALL.find(x => x.k === $('#dlg').dataset.k); if (m) await setUser(m, { notes: e.target.value }); }
  const opt = { 's-posters': 'online_posters', 's-online': 'online_fallback', 's-auto': 'auto_refresh', 's-probe': 'probe_media', 's-pauto': 'people_auto' }[e.target.id];
  if (opt) { await api('/api/settings', { [opt]: e.target.checked }); toast('Setting saved'); }
  if (e.target.id === 's-pscope') await api('/api/settings', { people_scope: e.target.value });
  if (e.target.id === 's-media') saveFolders();
  if (e.target.dataset.pfile) {
    const f = e.target.files[0], tc = +e.target.dataset.pfile; e.target.value = '';
    if (!f) return;
    if (f.size > 15e6) return posterMsg(tc, 'The image is larger than 15 MB.', true);
    posterMsg(tc, 'Reading the image…');
    const data = await new Promise((ok, no) => { const rd = new FileReader(); rd.onload = () => ok(rd.result); rd.onerror = no; rd.readAsDataURL(f); }).catch(() => null);
    return data ? posterSave(tc, { data }, POSTER_DONE) : posterMsg(tc, 'The image could not be read.', true);
  }
  if (e.target.id === 's-import' && e.target.files[0]) { importBackup(e.target.files[0]); e.target.value = ''; }
  if (e.target.id === 'pr-ign-toggle') { PR.showIgnored = e.target.checked; PR.shown = 100; probList(); }
  if (e.target.dataset.cksel !== undefined) { const i = +e.target.dataset.cksel; e.target.checked ? CK.sel.add(i) : CK.sel.delete(i); ckCount(); }
  if (e.target.dataset.ckc !== undefined) CK.choice[+e.target.dataset.ckc] = e.target.value;
  if (e.target.id === 'll-file') {
    const f = e.target.files[0]; if (!f) return;
    llPreview({ text: await f.text(), name: f.name });
  }
  if (e.target.id === 'ck-file') {
    const f = e.target.files[0]; if (!f) return;
    ckStart({ text: await f.text(), name: f.name });
  }
});
$('#modal').addEventListener('keydown', e => {
  if (e.key === 'Enter' && e.target.dataset.fq) openFix(+e.target.dataset.fq, e.target.value);
  if (e.key === 'Enter' && e.target.id === 'ck-path') $('#ck-go').click();
  if (e.key === 'Enter' && e.target.id === 'll-path') $('#ll-go').click();
  if (e.target.dataset.stab && (e.key === 'ArrowRight' || e.key === 'ArrowLeft')) {
    const tabs = $$('[data-stab]'), i = tabs.indexOf(e.target);
    const next = tabs[(i + (e.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length];
    next.click(); next.focus();
  }
});
document.addEventListener('keydown', e => {
  if (cfmResolve) {  // the confirmation sits above everything and keeps focus
    if (e.key === 'Escape') { e.preventDefault(); return closeConfirm(false); }
    if (e.key === 'Tab') { e.preventDefault(); (document.activeElement === $('#cfm-no') ? $('#cfm-yes') : $('#cfm-no')).focus(); }
    return;
  }
  if (e.key === 'Escape') {
    if (!$('#themeMenu').classList.contains('hidden')) { themeMenu(false); $('#themeBtn').focus(); }
    else if (!$('#qpanel').classList.contains('hidden')) qPanel(false);
    else if (!$('#modal').classList.contains('hidden')) closeModal();
    else if (SELMODE) setSelMode(false);
    else if ($('#side').classList.contains('open')) toggleSide(false);
    else if (F.q) { $('#q').value = F.q = ''; apply(); }
  }
  // keep keyboard focus inside the open dialog
  if (e.key === 'Tab' && !$('#modal').classList.contains('hidden')) {
    const f = $$('button, a[href], input, textarea, select', $('#dlg')).filter(x => !x.disabled && x.offsetParent);
    if (!f.length) return;
    if (e.shiftKey && document.activeElement === f[0]) { e.preventDefault(); f.at(-1).focus(); }
    else if (!e.shiftKey && document.activeElement === f.at(-1)) { e.preventDefault(); f[0].focus(); }
  }
  if (e.key === '/' && !/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)) { e.preventDefault(); $('#q').focus(); }
});

let QOPS = [], ACTIVE_LABELS = [];

const Q_EXAMPLES = [
  ['year:1990-1999', 'Released in the 90s'], ['rating:>8', 'IMDb rating above 8'], ['genre:horror', 'One genre'],
  ['dir:nolan', 'A director'], ['actor:"tom hanks"', 'A cast member'], ['res:1080p', 'A quality'],
  ['rt:>=90 mc:>=75', 'Critics agree'], ['is:unwatched', 'Not watched yet'], ['-zombie', 'Leave a word out'], ['"love story"', 'Exact phrase'],
];
const recentQ = () => { try { return JSON.parse(localStorage.getItem('recentQ') || '[]'); } catch { return []; } };
function rememberQ(q) {
  q = q.trim(); if (q.length < 2) return;
  try { localStorage.setItem('recentQ', JSON.stringify([q, ...recentQ().filter(x => x !== q)].slice(0, 6))); } catch { }
}
function qPanel(open) {
  const p = $('#qpanel');
  if (open) {
    const rec = recentQ();
    p.innerHTML = `${rec.length ? `<div class="qsec"><h3>${tr('Recent searches')}</h3><div class="qrecent">${rec.map(r => `<button type="button" data-qr="${esc(r)}">${I('clock', 'sm')}<span>${esc(r)}</span></button>`).join('')}</div></div>` : ''}
      <div class="qsec"><h3>${tr('Narrow a search (click to add)')}</h3><div class="qex">${Q_EXAMPLES.map(([e, d]) => `<button type="button" data-qx="${esc(e)}"><code>${esc(e)}</code><small>${tr(d)}</small></button>`).join('')}</div></div>
      <p class="qnote">All words must match. Numbers take <code>&gt;8</code>, <code>&lt;=1980</code> or <code>1990-1999</code>. More fields: <code>writer:</code> <code>country:</code> <code>lang:</code> <code>type:</code> <code>src:</code> <code>codec:</code> <code>file:</code> <code>votes:</code> <code>runtime:</code> <code>has:poster</code> <code>is:fav</code></p>`;
  }
  p.classList.toggle('hidden', !open);
  $('#q').setAttribute('aria-expanded', open);
  $('#qhelp').setAttribute('aria-expanded', open);
}
$('#qpanel').addEventListener('mousedown', e => e.preventDefault());  // keep focus in the search box
$('#qpanel').addEventListener('click', e => {
  const r = e.target.closest('[data-qr]'), x = e.target.closest('[data-qx]');
  if (!r && !x) return;
  const q = r ? r.dataset.qr : (F.q.trim() ? F.q.trim() + ' ' : '') + x.dataset.qx;
  $('#q').value = F.q = q; apply(); qPanel(false); $('#q').focus();
});
$('#qhelp').addEventListener('click', () => { $('#qpanel').classList.contains('hidden') ? (qPanel(true), $('#q').focus()) : qPanel(false); });
$('#q').addEventListener('focus', () => { if (!$('#q').value) qPanel(true); });
$('#q').addEventListener('input', () => { if ($('#q').value) qPanel(false); else qPanel(true); });
$('#q').addEventListener('change', () => rememberQ($('#q').value));
$('#q').addEventListener('keydown', e => { if (e.key === 'Enter') { rememberQ($('#q').value); qPanel(false); } });
document.addEventListener('click', e => { if (!e.target.closest('.search')) qPanel(false); });

// ---------------- grouping ----------------
// each grouper returns [label, order key]; the order key decides where the group sits
const GROUPS = {
  decade: m => m.decade ? [m.decade + 's', -m.decade] : ['Unknown year', 1e9],
  year: m => m.year ? [String(m.year), -m.year] : ['Unknown year', 1e9],
  genre: m => m.genres?.[0] ? [m.genres[0], m.genres[0]] : ['No genre', 'zzzzzz'],
  rating: m => { const r = m.rating; if (!r) return ['Unrated', 99]; const b = Math.min(Math.max(Math.floor(r), 5), 9); return b >= 9 ? ['9.0 and up', -9] : b <= 5 ? ['Below 6.0', -5] : [`${b}.0 – ${b}.9`, -b]; },
  res: m => m.res ? [m.res === '2160p' ? '4K (2160p)' : m.res, RES_ORDER.indexOf(m.res)] : ['Unknown quality', 99],
  type: m => [TYPE_LABEL[m.type] || 'Unmatched', TYPE_LABEL[m.type] || 'zzzzzz'],
  letter: m => { const c = (m.tkey[0] || '#').toUpperCase(); return /[A-Z]/.test(c) ? [c, c] : ['#', '0']; },
  seen: m => m.watched ? ['Watched', 1] : ['Not watched yet', 0],
};
// the groups of a list, in display order (collapsed state is not applied here)
function groupList(list) {
  const g = GROUPS[GROUP];
  if (!g) return [{ label: 'Movies', items: list }];
  const map = new Map();
  for (const m of list) { const [label, ord] = g(m); let e = map.get(label); if (!e) map.set(label, e = { label, ord, items: [] }); e.items.push(m); }
  const sort = $('#sort').value;
  const flip = REV && ((sort === 'year-desc' && (GROUP === 'decade' || GROUP === 'year')) || (sort === 'title' && GROUP === 'letter'));
  const gs = [...map.values()].sort((a, b) => typeof a.ord === 'number' ? a.ord - b.ord : String(a.ord).localeCompare(String(b.ord)));
  if (flip) gs.reverse();
  const tail = e => e.ord === 1e9 || e.ord === 99 || e.ord === 'zzzzzz';  // unknown / unrated groups stay at the end
  return [...gs.filter(e => !tail(e)), ...gs.filter(tail)];
}
function buildRows() {
  if (!GROUPS[GROUP]) { ROWS = VIEW; return; }
  ROWS = [];
  for (const e of groupList(VIEW)) {
    const closed = COLLAPSED.has(GROUP + ':' + e.label);
    ROWS.push({ h: e.label, n: e.items.length, closed });
    if (!closed) for (const m of e.items) ROWS.push(m);
  }
}
const grpInner = r => `<h2><button type="button" aria-expanded="${!r.closed}">${I('chev', 'sm chev')}<span>${esc(r.h)}</span> <span class="cnt">${r.n.toLocaleString()}</span></button></h2>`;
const grpHtml = r => `<div class="grp${r.closed ? ' closed' : ''}" data-g="${esc(r.h)}">${grpInner(r)}<span class="rule"></span></div>`;
const grpRowHtml = r => `<tr class="grp-row${r.closed ? ' closed' : ''}" data-g="${esc(r.h)}"><td colspan="12">${grpInner(r)}</td></tr>`;
function toggleGroup(label) {
  const k = GROUP + ':' + label, top = $('#scroll').scrollTop;
  COLLAPSED.has(k) ? COLLAPSED.delete(k) : COLLAPSED.add(k);
  buildRows(); render(Math.max(rendered, 1));
  $('#scroll').scrollTop = top;
  $(`[data-g="${CSS.escape(label)}"] button`)?.focus();
}

// ---------------- theme ----------------
const THEME_OPTS = [['light', 'Light', 'sun'], ['dark', 'Dark', 'moon'], ['system', 'System', 'monitor']];
const themePref = () => { try { return localStorage.getItem('theme') || 'system'; } catch { return 'system'; } };
function applyTheme(p) {
  try { localStorage.setItem('theme', p); } catch { }
  const dark = p === 'dark' || (p === 'system' && matchMedia('(prefers-color-scheme: dark)').matches);
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  $('#themeBtn').innerHTML = I(dark ? 'moon' : 'sun');
  $('#themeBtn').setAttribute('aria-label', `Theme: ${p}`);
  $('#themeMenu').innerHTML = THEME_OPTS.map(([v, l, ic]) => `<button type="button" role="menuitemradio" aria-checked="${p === v}" data-theme="${v}">${I(ic)}${tr(l)}${I('check', 'sm chk')}</button>`).join('');
  $$('[data-pref="theme"]').forEach(b => { const on = b.dataset.val === p; b.classList.toggle('on', on); b.setAttribute('aria-pressed', on); });
}
function themeMenu(open) {
  $('#themeMenu').classList.toggle('hidden', !open);
  $('#themeBtn').setAttribute('aria-expanded', open);
  if (open) ($('[aria-checked="true"]', $('#themeMenu')) || $('#themeMenu button')).focus();
}
$('#themeBtn').addEventListener('click', () => themeMenu($('#themeMenu').classList.contains('hidden')));
$('#themeMenu').addEventListener('click', e => { const b = e.target.closest('[data-theme]'); if (!b) return; applyTheme(b.dataset.theme); themeMenu(false); $('#themeBtn').focus(); });
$('#themeMenu').addEventListener('keydown', e => {
  const items = $$('button', $('#themeMenu')), i = items.indexOf(document.activeElement);
  if (e.key === 'ArrowDown') { e.preventDefault(); items[(i + 1) % items.length].focus(); }
  if (e.key === 'ArrowUp') { e.preventDefault(); items[(i - 1 + items.length) % items.length].focus(); }
});
document.addEventListener('click', e => { if (!e.target.closest('.menu-wrap')) themeMenu(false); });
matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => { if (themePref() === 'system') applyTheme('system'); });

function setPref(pref, val) {
  if (pref === 'lang') return setLang(val);
  if (pref === 'theme') applyTheme(val);
  if (pref === 'psize') { document.documentElement.dataset.psize = val; try { localStorage.setItem('psize', val); } catch { } }
  if (pref === 'view') { setView(val); try { localStorage.setItem('view', val); } catch { } render(); }
  $$(`[data-pref="${pref}"]`).forEach(b => { const on = b.dataset.val === val; b.classList.toggle('on', on); b.setAttribute('aria-pressed', on); });
}

// ---------------- sort / group controls ----------------
function saveSort() { try { localStorage.setItem('sort', $('#sort').value); localStorage.setItem('rev', REV ? '1' : ''); } catch { } }
$('#sort').addEventListener('change', () => { REV = false; saveSort(); apply(); });
$('#sortDir').addEventListener('click', () => { REV = !REV; saveSort(); apply(); });
$('#group').addEventListener('change', () => { GROUP = $('#group').value; try { localStorage.setItem('group', GROUP); } catch { } apply(); });

// ---------------- filter sections remember if they are open ----------------
(function () {
  let saved = {}; try { saved = JSON.parse(localStorage.getItem('secs') || '{}'); } catch { }
  $$('details.sec').forEach(d => {
    if (d.dataset.sec in saved) d.open = saved[d.dataset.sec];
    d.addEventListener('toggle', () => { saved[d.dataset.sec] = d.open; try { localStorage.setItem('secs', JSON.stringify(saved)); } catch { } });
  });
})();

// ---------------- export to Excel / CSV ----------------
let EXCOLS = null;
const EX_OPTS = { scope: 'view', rows: 'movie', fmt: 'xlsx', split: false };
const exPrefs = () => { try { return JSON.parse(localStorage.getItem('exportPrefs') || 'null') || {}; } catch { return {}; } };

async function openExport() {
  if (!EXCOLS) EXCOLS = await api('/api/export/columns');
  const p = exPrefs(), o = { ...EX_OPTS, ...p.opts };
  if (SEL.size) o.scope = 'sel'; else if (o.scope === 'sel') o.scope = 'view';
  const cols = new Set(p.cols || EXCOLS.filter(c => c.default).map(c => c.key));
  const groups = [...new Set(EXCOLS.map(c => c.group))];
  const filtered = VIEW.length !== ALL.length;
  const radio = (name, val, label, sub, on) => `<label class="opt"><input type="radio" name="${name}" value="${val}"${on ? ' checked' : ''}><span><b>${label}</b><small>${sub}</small></span></label>`;
  const genres = [...count(m => m.genres)].sort((a, b) => a[0].localeCompare(b[0]));
  $('#dlg').classList.add('wide');
  $('#dlg').innerHTML = `${closeBtn}<div class="pad" id="ex">
    <h2 id="dlg-title" dir="auto">${tr('Export movies')}</h2>
    <div class="exgrid">
      <div>
        <fieldset class="exset"><legend>Which movies</legend>
          ${SEL.size ? radio('exscope', 'sel', 'Selected movies', `${SEL.size.toLocaleString()} movies you selected`, o.scope === 'sel') : ''}
          ${radio('exscope', 'view', 'Current results', `${VIEW.length.toLocaleString()} movies${filtered ? ', with your filters and search, in the order shown' : ', no filters applied'}`, o.scope === 'view')}
          ${filtered && ACTIVE_LABELS.length ? `<div class="exfilters" aria-label="Active filters">${ACTIVE_LABELS.map(l => `<span class="tag static">${esc(l)}</span>`).join('')}</div>` : ''}
          ${radio('exscope', 'all', 'Whole library', `${ALL.length.toLocaleString()} movies`, o.scope === 'all')}
          <div class="exnarrow"><b>Narrow down further</b>
            <div class="setrow"><label for="ex-q">Search<small>Same as the search box: <code>genre:horror year:&gt;2000 -zombie</code></small></label><input type="text" id="ex-q" autocomplete="off" dir="auto"></div>
            <div class="exrow">
              <div><label for="ex-ymin">Year from</label><input type="number" id="ex-ymin" inputmode="numeric" placeholder="Any"></div>
              <div><label for="ex-ymax">Year to</label><input type="number" id="ex-ymax" inputmode="numeric" placeholder="Any"></div>
              <div><label for="ex-rmin">IMDb rating at least</label><input type="number" id="ex-rmin" inputmode="decimal" step="0.1" min="0" max="10" placeholder="Any"></div>
            </div>
            <div class="exrow">
              <div><label for="ex-genre">Genre</label><div class="sel"><select id="ex-genre"><option value="">Any</option>${genres.map(([g, n]) => `<option value="${esc(g)}">${esc(g)} (${n})</option>`).join('')}</select></div></div>
              <div><label for="ex-res">Quality</label><div class="sel"><select id="ex-res"><option value="">Any</option>${RES_ORDER.filter(r => ALL.some(m => m.files.some(f => f.res === r))).map(r => `<option>${r}</option>`).join('')}</select></div></div>
              <div><label for="ex-status">Show</label><div class="sel"><select id="ex-status"><option value="">All</option><option value="fav">Favorites</option><option value="watched">Watched</option><option value="unwatched">Not watched</option><option value="review">Needs review</option></select></div></div>
            </div>
          </div>
        </fieldset>
        <fieldset class="exset"><legend>Layout</legend>
          ${radio('exrows', 'movie', 'One row per movie', 'Files of the same movie are joined in one cell', o.rows === 'movie')}
          ${radio('exrows', 'file', 'One row per file', 'Movies with several files get several rows', o.rows === 'file')}
          <label class="check ex-split${GROUP === 'none' ? ' hidden' : ''}"><input type="checkbox" id="ex-split"${o.split ? ' checked' : ''}><span>One Excel sheet per group<br><small>Using your current grouping (${esc($('#group').selectedOptions[0].textContent.replace('Group: ', ''))})</small></span></label>
        </fieldset>
        <fieldset class="exset"><legend>File type</legend>
          <div class="seg" role="group" aria-label="File type"><label class="segopt"><input type="radio" name="exfmt" value="xlsx"${o.fmt === 'xlsx' ? ' checked' : ''}><span>Excel (.xlsx)</span></label><label class="segopt"><input type="radio" name="exfmt" value="csv"${o.fmt === 'csv' ? ' checked' : ''}><span>CSV</span></label></div>
        </fieldset>
      </div>
      <div>
        <fieldset class="exset"><legend>Columns <span class="exn" id="ex-ncols"></span></legend>
          <div class="btns exbtns"><button type="button" class="btn outline small" data-expreset="essential">Essentials</button><button type="button" class="btn outline small" data-expreset="all">Everything</button><button type="button" class="btn outline small" data-expreset="none">Clear</button></div>
          ${groups.map(g => `<div class="exgroup"><div class="exgh"><b>${esc(g)}</b><button type="button" class="link" data-exgrp="${esc(g)}">All / none</button></div>
            <div class="excols">${EXCOLS.filter(c => c.group === g).map(c => `<label class="check"><input type="checkbox" data-excol="${c.key}"${cols.has(c.key) ? ' checked' : ''}><span>${esc(c.label)}</span></label>`).join('')}</div></div>`).join('')}
        </fieldset>
      </div>
    </div>
    <div class="bar-actions"><span class="grow" id="ex-sum" role="status"></span><button type="button" class="btn outline" data-close>Cancel</button><button type="button" class="btn" id="ex-go">${I('download')}Export</button></div>
  </div>`;
  $('#dlg').dataset.mode = 'export';
  showModal('dlg-title');
  exUpdate();
}

function exState() {
  const v = n => $(`#ex [name="${n}"]:checked`)?.value;
  return {
    scope: v('exscope'), rows: v('exrows'), fmt: v('exfmt'), split: $('#ex-split').checked && !$('.ex-split').classList.contains('hidden'),
    cols: $$('#ex [data-excol]:checked').map(c => c.dataset.excol),
    q: $('#ex-q').value.trim(), yMin: +$('#ex-ymin').value || 0, yMax: +$('#ex-ymax').value || 0, rMin: +$('#ex-rmin').value || 0,
    genre: $('#ex-genre').value, res: $('#ex-res').value, status: $('#ex-status').value,
  };
}
function exExtra(st) {  // the extra filters as readable lines (they go on the "Export info" sheet)
  const L = [];
  if (st.q) L.push(`search: ${st.q}`);
  if (st.yMin || st.yMax) L.push(`year ${st.yMin || '…'}–${st.yMax || '…'}`);
  if (st.rMin) L.push(`IMDb rating ≥ ${st.rMin}`);
  if (st.genre) L.push(`genre: ${st.genre}`);
  if (st.res) L.push(`quality: ${st.res}`);
  if (st.status) L.push({ fav: 'favorites', watched: 'watched', unwatched: 'not watched', review: 'needs review' }[st.status]);
  return L;
}
function exList(st) {
  const Q = st.q ? parseQuery(st.q) : null;
  const sf = { fav: m => m.fav, watched: m => m.watched, unwatched: m => !m.watched, review: m => NEEDS_REVIEW.has(m.conf) }[st.status];
  return (st.scope === 'sel' ? ALL.filter(m => SEL.has(m.k)) : st.scope === 'all' ? ALL : VIEW).filter(m => (!Q || Q.test(m)) && (!st.yMin || m.year >= st.yMin) && (!st.yMax || m.year <= st.yMax)
    && (!st.rMin || m.rating >= st.rMin) && (!st.genre || m.genres.includes(st.genre)) && (!st.res || m.files.some(f => f.res === st.res)) && (!sf || sf(m)));
}
function exUpdate() {
  if (!$('#ex')) return;
  const st = exState(), list = exList(st);
  const rows = st.rows === 'file' ? list.reduce((n, m) => n + m.files.length, 0) : list.length;
  $('#ex-ncols').textContent = `${st.cols.length} selected`;
  $('#ex-sum').textContent = `${list.length.toLocaleString()} movies · ${rows.toLocaleString()} rows × ${st.cols.length} columns`;
  $('#ex-go').disabled = !rows || !st.cols.length;
  $('.ex-split input').disabled = st.fmt === 'csv';
}
async function exGo(btn) {
  const st = exState(), list = exList(st);
  try { localStorage.setItem('exportPrefs', JSON.stringify({ cols: st.cols, opts: { scope: st.scope, rows: st.rows, fmt: st.fmt, split: $('#ex-split').checked } })); } catch { }
  const req = {
    keys: list.map(m => m.k), columns: st.cols, rows: st.rows, format: st.fmt,
    info: { scope: st.scope === 'all' ? 'Whole library' : st.scope === 'sel' ? 'Selected movies' : 'Current results', filters: [...(st.scope === 'view' ? ACTIVE_LABELS : []), ...exExtra(st)] },
  };
  if (st.split && st.fmt === 'xlsx') req.sheets = groupList(list).map(g => ({ name: g.label, keys: g.items.map(m => m.k) }));
  btn.disabled = true; btn.textContent = 'Preparing…';
  const r = await api('/api/export', req);
  const reset = () => { btn.disabled = false; btn.innerHTML = `${I('download')}Export`; };
  if (r.error) { reset(); return toast(r.error, 'err'); }
  if (await deliver(r)) closeModal(); else reset();
}

// ---------------- helpers shared by the newer dialogs ----------------
const fmtSize = n => !n ? '' : n >= 1e9 ? (n / 1e9).toFixed(n >= 1e10 ? 1 : 2) + ' GB' : Math.max(1, Math.round(n / 1e6)) + ' MB';

// hands a file the server prepared ({token, name, rows}) to the user: a native "Save as" in the app window, a download in a browser
async function deliver(r) {
  const a = window.pywebview?.api;
  if (a?.save_export) {
    try {
      const path = await a.save_export(r.token, r.name);
      if (!path) { toast('Cancelled', 'info'); return false; }
      toast(`Saved to ${path}`);
      return true;
    } catch (e) { toast(`Could not save: ${e?.message || e}`, 'err'); return false; }
  }
  const link = Object.assign(document.createElement('a'), { href: '/api/export/file/' + r.token, download: r.name });
  document.body.append(link); link.click(); link.remove();
  toast(`Saved ${r.name}`);
  return true;
}
async function stash(name, text, bom = true) {
  const r = await api('/api/stash', { name, text, bom });
  return deliver(r);
}
// CSV links: the app window cannot download a link, so the page reads the file and offers it through "Save as"
document.addEventListener('click', async e => {
  const a = e.target.closest?.('a[data-csv]');
  if (!a) return;
  e.preventDefault();
  try {
    const res = await fetch(a.getAttribute('href'));
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    await stash(a.dataset.csv, await res.text());
  } catch (err) { toast(`Could not export: ${err.message || err}`, 'err'); }
});
async function reveal(path) {
  const r = await api('/api/reveal', { path });
  if (r.error) toast(r.error, 'err');
}

// ---------------- saved views ----------------
let VIEWS = [];
const filterObject = () => { const o = {}; for (const [k, v] of Object.entries(F)) o[k] = v instanceof Set ? [...v] : v; return o; };
async function loadViews() { VIEWS = await api('/api/views'); renderViews(); }
function renderViews() {
  $('#views-list').innerHTML = VIEWS.map(v => `<div class="vrow"><button type="button" class="vbtn" data-view="${v.id}" title="Apply this view">${esc(v.name)}</button><button type="button" class="vdel" data-vdel="${v.id}" aria-label="Delete the view ${esc(v.name)}">${I('x', 'sm')}</button></div>`).join('')
    || `<p class="hint">${tr('Search and filter, then save the combination here.')}</p>`;
}
function applyView(v) {
  const s = v.state;
  for (const k of SET_KEYS) F[k].clear();
  Object.assign(F, { q: '', yMin: null, yMax: null, rMin: 0, vMin: 0, rtMin: 0, mcMin: 0, country: '', lang: '', person: null });
  applyFilterObject({ ...s.F, q: s.F?.q || '' });
  F.q = s.F?.q || ''; $('#q').value = F.q;
  if (s.sort && [...$('#sort').options].some(o => o.value === s.sort)) $('#sort').value = s.sort;
  REV = !!s.rev; GROUP = GROUPS[s.group] ? s.group : 'none'; $('#group').value = GROUP;
  saveSort(); try { localStorage.setItem('group', GROUP); } catch { }
  buildFacets(); apply();
  toast(`View: ${v.name}`, 'info');
}
$('#views-list').addEventListener('click', async e => {
  const b = e.target.closest('[data-view]'), d = e.target.closest('[data-vdel]');
  if (b) return applyView(VIEWS.find(v => v.id === +b.dataset.view));
  if (d) {
    const v = VIEWS.find(x => x.id === +d.dataset.vdel);
    if (!await confirmBox({ title: `Delete the view “${v.name}”?`, text: 'Only the saved view goes; your movies are not touched.', ok: 'Delete', danger: true })) return;
    await api('/api/views/delete', { id: v.id }); await loadViews();
  }
});
$('#viewSave').addEventListener('click', () => { $('#vsave').classList.toggle('hidden'); $('#vname').focus(); });
async function saveCurrentView() {
  const name = $('#vname').value.trim(); if (!name) return $('#vname').focus();
  const r = await api('/api/views', { name, state: { F: { ...filterObject(), q: F.q }, sort: $('#sort').value, rev: REV, group: GROUP } });
  if (r.error) return toast(r.error, 'err');
  $('#vname').value = ''; $('#vsave').classList.add('hidden');
  toast(`Saved view “${name}”`); await loadViews();
}
$('#vok').addEventListener('click', saveCurrentView);
$('#vname').addEventListener('keydown', e => { if (e.key === 'Enter') saveCurrentView(); if (e.key === 'Escape') { $('#vsave').classList.add('hidden'); e.stopPropagation(); } });

// ---------------- wishlist ----------------
let WISH = [], WTAB = 'wanted';
async function wishAdd(items) {
  const r = await api('/api/wishlist/add', { items });
  const dup = items.length - r.added;
  toast(r.added ? `${r.added} added to the wishlist${dup ? ` (${dup} already there)` : ''}` : 'Already on the wishlist', r.added ? 'ok' : 'info');
  poll();
}
const wishText = list => list.map(w => w.title + (w.year ? ` (${w.year})` : '')).join('\n');
async function openWishlist() {
  WISH = await api('/api/wishlist');
  wishView();
  $('#dlg').dataset.mode = 'wish';
  showModal('dlg-title');
}
function wishView() {
  const n = s => WISH.filter(w => w.status === s).length;
  const rows = WISH.filter(w => WTAB === 'all' || w.status === WTAB);
  $('#dlg').innerHTML = `${closeBtn}<div class="pad">
    <h2 id="dlg-title" dir="auto">${tr('Wishlist')}</h2>
    <p class="pr-help">Movies you want to get or replace with a better copy. Add them from the list check, from Problems → Quality, from Collections, or here.</p>
    <div class="line wline"><input type="text" id="w-new" placeholder="Title (Year), for example Heat (1995)" aria-label="Add a movie to the wishlist" dir="auto"><button type="button" class="btn" id="w-add">Add</button></div>
    <div class="tabs" role="group" aria-label="Show">${[['wanted', 'Wanted'], ['found', 'Found'], ['all', 'All']].map(([k, l]) => `<button type="button" class="chip${WTAB === k ? ' on' : ''}" aria-pressed="${WTAB === k}" data-wtab="${k}">${l}<small>${k === 'all' ? WISH.length : n(k)}</small></button>`).join('')}</div>
    <div class="ck-list">${rows.map(w => `<div class="ck iss${w.status === 'found' ? ' done' : ''}">
      <div style="min-width:0"><div class="im"><b>${esc(w.title)}</b> ${w.year || ''}</div><div class="sub">${esc([w.source, w.note].filter(Boolean).join(' · '))}</div></div>
      <div class="isbtn" style="min-width:140px"><button type="button" class="btn outline small" data-wset="${w.id}" data-st="${w.status === 'found' ? 'wanted' : 'found'}">${w.status === 'found' ? 'Wanted again' : 'Mark found'}</button><button type="button" class="btn ghost small" data-wdel="${w.id}">Remove</button></div></div>`).join('')
      || `<div class="hint">${WISH.length ? 'Nothing here.' : 'The wishlist is empty.'}</div>`}</div>
    <div class="bar-actions"><span class="grow">${n('wanted')} wanted, ${n('found')} found</span>
      <button type="button" class="btn ghost" data-wcopy>${I('copy', 'sm')}Copy wanted</button>
      <button type="button" class="btn ghost" data-wsave="txt">Save as text</button>
      <button type="button" class="btn ghost" data-wsave="csv">Save as CSV</button></div></div>`;
}
async function wishAddFromInput() {
  const m = /^(.*?)(?:\s*\((\d{4})\))?\s*$/.exec($('#w-new').value.trim());
  if (!m || !m[1]) return $('#w-new').focus();
  await wishAdd([{ title: m[1], year: m[2] ? +m[2] : null, source: 'Added by hand' }]);
  WISH = await api('/api/wishlist'); WTAB = 'wanted'; wishView(); $('#w-new').focus();
}

// ---------------- collections ----------------
let COLL = { collections: [], tmdb: false }, CTAB = 'incomplete';
async function openCollections() {
  COLL = await api('/api/collections');
  collView();
  $('#dlg').classList.add('wide');
  $('#dlg').dataset.mode = 'coll';
  showModal('dlg-title');
}
function collView() {
  const cs = COLL.collections, inc = cs.filter(c => c.missing.length), fetched = cs.filter(c => c.fetched).length;
  const list = CTAB === 'incomplete' ? inc : CTAB === 'complete' ? cs.filter(c => c.fetched && !c.missing.length) : cs;
  $('#dlg').innerHTML = `${closeBtn}<div class="pad">
    <h2 id="dlg-title" dir="auto">${tr('Collections')}</h2>
    <p class="pr-help">Franchises in your library, and the movies of each that you do not have yet. The list of parts comes from TMDB.</p>
    ${!COLL.tmdb ? `<div class="err-box" style="background:var(--warn-bg);border-color:var(--warn);color:var(--warn)">Collections need a free TMDB key. Add one in Settings → Online data, then run <b>Scan list &amp; update</b>.</div>` : ''}
    ${COLL.tmdb && cs.length && fetched < cs.length ? `<div class="btns" style="margin-bottom:12px"><button type="button" class="btn" id="c-fetch">${I('download', 'sm')}Check for missing movies (${cs.length - fetched} not checked yet)</button></div>` : ''}
    <div class="tabs" role="group" aria-label="Show">${[['incomplete', `Missing movies`, inc.length], ['complete', 'Complete', cs.filter(c => c.fetched && !c.missing.length).length], ['all', 'All', cs.length]].map(([k, l, n]) => `<button type="button" class="chip${CTAB === k ? ' on' : ''}" aria-pressed="${CTAB === k}" data-ctab="${k}">${l}<small>${n}</small></button>`).join('')}</div>
    <div class="ck-list">${list.slice(0, 120).map((c, i) => `<div class="ck iss"><div style="min-width:0">
        <div class="im"><b>${esc(c.name)}</b> <small>${c.fetched ? `you have ${c.have.length} of ${c.total}` : `${c.have.length} in your library, parts not checked`}</small></div>
        <div class="ifiles">${c.have.map(m => `<div class="ifile"><span class="q good">Have</span><button type="button" class="link" data-copen="${m.k}">${esc(m.title)}${m.year ? ` (${m.year})` : ''}</button></div>`).join('')}
        ${c.missing.map(m => `<div class="ifile"><span class="q warn">Missing</span><span>${esc(m.title)}${m.year ? ` (${m.year})` : ''}</span></div>`).join('')}</div></div>
        <div class="isbtn">${c.missing.length ? `<button type="button" class="btn outline small" data-cwish="${cs.indexOf(c)}">Add ${c.missing.length} missing to wishlist</button>` : ''}
        <button type="button" class="btn ghost small" data-csearch="${esc(c.name)}">Show in library</button></div></div>`).join('')
      || `<div class="hint">${cs.length ? 'Nothing in this list.' : 'No collections known yet.'}</div>`}</div>
    ${list.length > 120 ? `<p class="hint">Showing the first 120 of ${list.length}.</p>` : ''}</div>`;
}

// ---------------- rename plan ----------------
async function openRename(pattern) {
  const r = await api('/api/rename-plan' + (pattern ? '?pattern=' + encodeURIComponent(pattern) : ''));
  $('#dlg').classList.add('wide');
  $('#dlg').dataset.mode = 'rename';
  const none = !r.error && !r.total;
  const chips = Object.entries(r.fields || {}).map(([k, d]) => `<button type="button" class="btn ghost small" data-rnadd="{${k}}" title="${esc(d)}">{${k}}</button>`).join(' ');
  const kind = x => x.kind === 'folder' ? 'Folder' : x.kind === 'side' ? 'Subtitle' : 'File';
  $('#dlg').innerHTML = `${closeBtn}<div class="pad">
    <h2 id="dlg-title" dir="auto">${tr('Rename plan')}</h2>
    <p class="pr-help">A tidy name for every movie file, its subtitles, and the folder of the movie. You choose the pattern. The app does not rename anything. It writes a script (or a list) that you read first and run yourself.</p>
    <div class="setrow"><label for="rn-pattern">Name pattern<small>Type your own pattern, or press a field to add it. Empty fields vanish with their brackets. The pattern is kept.</small></label>
      <div><input type="text" id="rn-pattern" dir="ltr" value="${esc(r.pattern)}" spellcheck="false">
      <div class="btns" style="margin-top:6px">${chips}</div>
      <div class="btns" style="margin-top:6px"><button type="button" class="btn small" data-rnpreview>Preview</button><button type="button" class="btn ghost small" data-rnreset>Default pattern</button></div></div></div>
    ${r.error ? `<div class="err-box" role="alert">${esc(r.error)}</div>` : ''}
    ${none ? `<div class="err-box" style="background:var(--warn-bg);border-color:var(--warn);color:var(--warn)">The app does not know where your movies are. Add your movie folders in Settings → General first.</div>` : r.error ? '' : `
    <div class="statgrid"><div class="stat"><b>${r.todo.toLocaleString()}</b><span>to rename${r.folders ? ` (${r.folders.toLocaleString()} folders)` : ''}</span></div><div class="stat"><b>${r.same.toLocaleString()}</b><span>already named like this</span></div><div class="stat"><b>${r.conflicts.toLocaleString()}</b><span>skipped: name taken</span></div></div>
    <div class="ck-list">${r.sample.slice(0, 40).map(x => `<div class="ck iss"><div style="min-width:0"><div class="nm"><small class="dim">${kind(x)}</small> ${esc(x.old)}</div><div class="im">${I('chev', 'sm')} <b>${esc(x.new)}</b></div></div></div>`).join('')}</div>
    ${r.todo > 40 ? `<p class="hint">Showing 40 of ${r.todo.toLocaleString()}. The files contain all of them.</p>` : ''}
    <p class="hint">After renaming, put the new names in your list (the “new names” text file has them, use Load list). The app finds movies by name, so the old names will not match any more.</p>
    <div class="bar-actions"><span class="grow"></span><button type="button" class="btn ghost" data-rplan="list">New names (.txt)</button><button type="button" class="btn ghost" data-rplan="csv">Plan (.csv)</button><button type="button" class="btn" data-rplan="bat">${I('download', 'sm')}Rename script (.bat)</button></div>`}
  </div>`;
  showModal('dlg-title');
}

// ---------------- backup ----------------
async function importBackup(file) {
  let data; try { data = JSON.parse(await file.text()); } catch { return toast('That file is not a MovieShelf backup.', 'err'); }
  const r = await api('/api/backup/import', { data });
  if (r.error) return toast(r.error, 'err');
  const n = r.restored;
  toast(`Restored ${n.user} movie notes and flags, ${n.manual} manual matches, ${n.views} views, ${n.wishlist} wishlist items` + (n.lists ? `, ${n.lists} list names` : ''));
  await loadViews(); poll(); setTimeout(() => load(true), 800);
}

// ---------------- select mode and bulk actions ----------------
let SELMODE = false, SELLAST = -1;
const SEL = new Set();
function setSelMode(on) {
  SELMODE = on;
  document.body.classList.toggle('selecting', on);
  $('#selBtn').setAttribute('aria-pressed', on);
  $('#selBtn').classList.toggle('on', on);
  if (!on) SEL.clear();
  paintSel();
}
function paintSel() {
  $$('.card[data-k], tr[data-k]').forEach(el => { const on = SEL.has(el.dataset.k); el.classList.toggle('selected', on); el.setAttribute('aria-selected', on); });
  const n = SEL.size;
  $('#bulk').classList.toggle('hidden', !SELMODE);
  $('#bulk-n').textContent = n ? `${n.toLocaleString()} ${tr('selected')}` : tr('Click movies to select them');
  $$('#bulk [data-bulk], #bulk-rate').forEach(b => { b.disabled = !n; });
}
function selClick(el, e) {
  const m = ALL[+el.dataset.i];
  const at = VIEW.indexOf(m);
  if (e?.shiftKey && SELLAST >= 0 && at >= 0) {  // shift-click: everything between the last click and this one
    const [a, b] = [Math.min(SELLAST, at), Math.max(SELLAST, at)];
    for (let i = a; i <= b; i++) SEL.add(VIEW[i].k);
  } else SEL.has(m.k) ? SEL.delete(m.k) : SEL.add(m.k);
  SELLAST = at;
  paintSel();
}
async function bulkSet(data, msg) {
  const keys = [...SEL]; if (!keys.length) return;
  await api('/api/user/bulk', { keys, data });
  const when = new Date().toISOString().slice(0, 19).replace('T', ' ');
  for (const m of ALL) if (SEL.has(m.k)) { Object.assign(m, data); if ('watched' in data) m.watched_at = data.watched ? (m.watched_at || when) : null; }
  buildFacets(); apply(Math.max(rendered, 150));
  toast(`${msg}: ${keys.length.toLocaleString()} ${keys.length === 1 ? 'movie' : 'movies'}`);
}
$('#selBtn').addEventListener('click', () => setSelMode(!SELMODE));
$('#bulk').addEventListener('click', async e => {
  const b = e.target.closest('[data-bulk]'); if (!b) return;
  const a = b.dataset.bulk;
  if (a === 'all') { VIEW.forEach(m => SEL.add(m.k)); return paintSel(); }
  if (a === 'none') { SEL.clear(); return paintSel(); }
  if (a === 'done') return setSelMode(false);
  if (a === 'fav') return bulkSet({ fav: 1 }, 'Added to favorites');
  if (a === 'unfav') return bulkSet({ fav: 0 }, 'Removed from favorites');
  if (a === 'seen') return bulkSet({ watched: 1 }, 'Marked watched');
  if (a === 'unseen') return bulkSet({ watched: 0 }, 'Marked not watched');
  if (a === 'wish') return wishAdd(ALL.filter(m => SEL.has(m.k)).map(m => ({ title: m.en || m.title, year: m.year, tconst: m.tt ? +m.tt.slice(2) : null, source: 'Selection' })));
  if (a === 'export') return openExport();
  if (a === 'remove') {
    const ok = await removeFromList(ALL.filter(m => SEL.has(m.k)).flatMap(m => m.files.map(f => f.id)));
    if (ok) { SEL.clear(); paintSel(); }
  }
});
$('#bulk-rate').addEventListener('change', async e => {
  const v = +e.target.value; e.target.value = '';
  if (v) await bulkSet({ myrating: v }, `Rated ${v}/10`);
});

// ---------------- watch next ----------------
const WN = { pool: 'view', unwatched: true, maxrt: 0, minr: 0, mode: 'rated', picks: [] };
function wnPool() {
  return (WN.pool === 'all' ? ALL : VIEW).filter(m => (!WN.unwatched || !m.watched) && (!WN.maxrt || (m.runtime && m.runtime <= WN.maxrt)) && (!WN.minr || m.rating >= WN.minr));
}
function wnShuffle(list) { for (let i = list.length - 1; i > 0; i--) { const j = Math.floor(Math.random() * (i + 1)); [list[i], list[j]] = [list[j], list[i]]; } return list; }
function wnPick(n = 6) {
  let pool = wnPool();
  if (WN.mode === 'rated') pool = [...pool].sort((a, b) => (b.rating || 0) - (a.rating || 0) || (b.votes || 0) - (a.votes || 0)).slice(0, 40);
  if (WN.mode === 'gems') pool = pool.filter(m => m.rating >= 7.2 && (m.votes || 0) < 60000);
  WN.picks = wnShuffle([...pool]).slice(0, n);
}
function wnCard(m) {
  return `<article class="wn-card"><button type="button" class="wn-open" data-wopen="${esc(m.k)}" aria-label="Open ${esc(m.en || m.title)}"><div class="pw">${posterHtml(m)}</div></button>
    <div class="t">${esc(m.en || m.title)}</div><div class="m">${[m.year, fmtRun(m.runtime), m.rating ? 'IMDb ' + m.rating.toFixed(1) : ''].filter(Boolean).join(' · ')}</div>
    <div class="m">${esc((m.genres || []).slice(0, 3).join(', '))}</div>
    <button type="button" class="btn outline small" data-wseen="${esc(m.k)}">${I('check', 'sm')}Watched</button></article>`;
}
function wnView(repick = true) {
  if (repick) wnPick();
  const hist = ALL.filter(m => m.watched && m.watched_at).sort((a, b) => b.watched_at.localeCompare(a.watched_at)).slice(0, 8);
  const poolN = wnPool().length;
  const sel = (id, label, opts, cur) => `<div><label for="${id}">${label}</label><div class="sel"><select id="${id}">${opts.map(([v, l]) => `<option value="${v}"${String(cur) === String(v) ? ' selected' : ''}>${l}</option>`).join('')}</select></div></div>`;
  $('#dlg').innerHTML = `${closeBtn}<div class="pad" id="wn">
    <h2 id="dlg-title" dir="auto">${tr('Watch next')}</h2>
    <div class="wn-controls">
      ${sel('wn-pool', 'Choose from', [['view', `Current results (${VIEW.length.toLocaleString()})`], ['all', `Whole library (${ALL.length.toLocaleString()})`]], WN.pool)}
      ${sel('wn-mode', 'Kind of pick', [['rated', 'Best rated'], ['random', 'Surprise me'], ['gems', 'Hidden gems']], WN.mode)}
      ${sel('wn-rt', 'Length', [[0, 'Any'], [100, 'Up to 100 min'], [120, 'Up to 2 h'], [150, 'Up to 2.5 h']], WN.maxrt)}
      ${sel('wn-r', 'IMDb rating', [[0, 'Any'], [6, '6 and up'], [7, '7 and up'], [8, '8 and up']], WN.minr)}
      <label class="check"><input type="checkbox" id="wn-unseen"${WN.unwatched ? ' checked' : ''}><span>Only movies I have not watched</span></label>
    </div>
    <p class="hint">${poolN.toLocaleString()} movies match.${WN.mode === 'gems' ? ' Hidden gems: rated 7.2 or more with fewer than 60,000 votes.' : ''}</p>
    ${WN.picks.length ? `<div class="wn-grid">${WN.picks.map(wnCard).join('')}</div>` : '<div class="hint">Nothing matches. Loosen a setting above.</div>'}
    <div class="btns" style="margin:14px 0 4px"><button type="button" class="btn" id="wn-shuffle">${I('copy', 'sm')}Pick again</button></div>
    ${hist.length ? `<h3 class="wn-h">Recently watched</h3><div class="wn-hist">${hist.map(m => `<button type="button" class="link" data-wopen="${esc(m.k)}">${esc(m.en || m.title)}${m.year ? ` (${m.year})` : ''}</button><small>${m.watched_at.slice(0, 10)}</small>`).join('')}</div>` : ''}
  </div>`;
}
function openWatchNext() {
  wnView(true);
  $('#dlg').dataset.mode = 'wn';
  showModal('dlg-title');
}
$('#modal').addEventListener('change', e => {
  if (!e.target.closest?.('#wn')) return;
  const id = e.target.id;
  if (id === 'wn-pool') WN.pool = e.target.value;
  if (id === 'wn-mode') WN.mode = e.target.value;
  if (id === 'wn-rt') WN.maxrt = +e.target.value;
  if (id === 'wn-r') WN.minr = +e.target.value;
  if (id === 'wn-unseen') WN.unwatched = e.target.checked;
  wnView(true);
});

// ---------------- help and language ----------------
let HELP_SECTION = null;
async function openHelp(id) {
  HELP_SECTION = id || null;
  const d = $('#dlg');
  d.classList.add('wide', 'help');
  d.dataset.mode = 'help';
  d.innerHTML = `${closeBtn}<div class="pad helppad"><div class="helphead"><h2 id="dlg-title" dir="auto">${tr('Help')}</h2>
    <div class="btns"><button type="button" class="btn outline small" data-hlang>${isFa() ? 'English' : 'فارسی'}</button>
    <a class="btn outline small" href="/static/help.html${id ? '#' + id : ''}" target="_blank">${tr('Open as a page')}</a></div></div><div id="helpbox"></div></div>`;
  showModal('dlg-title');
  await helpMount($('#helpbox'), LANG, id);
}
function langBtn() {
  $('#langBtn .lang-t').textContent = isFa() ? 'EN' : 'FA';
  $('#langBtn').setAttribute('aria-label', isFa() ? 'زبان' : 'Language');
}
function setLang(l) {
  LANG = l === 'fa' ? 'fa' : 'en';
  try { localStorage.setItem('lang', LANG); } catch { }
  applyI18n(); langBtn();
  buildFacets(); renderViews(); applyTheme(themePref()); render(Math.max(rendered, 150)); renderActive(); paintSel();
  if ($('#modal').classList.contains('hidden')) return;
  const mode = $('#dlg').dataset.mode;  // reopen the window that is showing so its menu text follows
  if (mode === 'help') openHelp(HELP_SECTION);
  else if (mode === 'settings') openSettings(STAB);
  else if (mode === 'problems') probList();
  else if (mode === 'wish') wishView();
  else if (mode === 'coll') collView();
  else if (mode === 'wn') wnView(false);
  else if (mode === 'stats') openStats();
}
// a "?" in the corner of the windows that have a Help topic; the window text is rebuilt often, so it is re-added when needed
const HELP_FOR = { loadlist: 'check', settings: 'settings', problems: 'problems', export: 'export', check: 'check', wn: 'collection', wish: 'collection', coll: 'collection', rename: 'files', stats: 'collection' };
new MutationObserver(() => {
  const d = $('#dlg'), id = HELP_FOR[d.dataset.mode];
  if (id && !d.querySelector(':scope > .helplink')) d.insertAdjacentHTML('afterbegin', `<button type="button" class="helplink" data-help="${id}" aria-label="${tr('Help')}" title="${tr('Help')}">?</button>`);
}).observe($('#dlg'), { childList: true });
$('#helpBtn').addEventListener('click', () => openHelp());
$('#langBtn').addEventListener('click', () => setLang(isFa() ? 'en' : 'fa'));
$('#modal').addEventListener('click', e => { const h = e.target.closest('[data-help]'); if (h) { e.stopPropagation(); openHelp(h.dataset.help); } }, true);

// ---------------- full backup ----------------
let QUIET = false;  // while a backup job is watched here, poll() does not add its generic "Update finished" toast
async function watchJob(okMsg) {
  QUIET = true;
  try {
    for (; ;) {
      await new Promise(r => setTimeout(r, 1500));
      const st = await api('/api/status');
      if (!st.job.running) { st.job.error ? toast(st.job.error, 'err') : toast(okMsg); return !st.job.error; }
    }
  } finally { QUIET = false; poll(); }
}
const mb = n => fmtSize(n) || '0 MB';

async function openFullSave() {
  const i = await api('/api/backup/full/info');
  const d = $('#dlg');
  d.dataset.mode = 'fullbackup';
  d.innerHTML = `${closeBtn}<div class="pad" id="fb">
    <h2 id="dlg-title">Save a full backup</h2>
    <p class="pr-help">One .zip file with what you tick below. Restoring it on this or another PC brings everything back.</p>
    <label class="check"><input type="checkbox" checked disabled><span>The library: every movie, match, detail, score, your favorites, watched flags, ratings, notes, saved views, wishlist <small>(${mb(i.db)})</small></span></label>
    <label class="check"><input type="checkbox" checked disabled><span>All settings (list folder, movie folders, update options, theme choices of the app)</span></label>
    <label class="check"><input type="checkbox" id="fb-keys" ${i.keys ? 'checked' : 'disabled'}><span>API keys (TMDB, OMDb)${i.keys ? '' : ' <small>(none set)</small>'}<br><small style="color:var(--muted-foreground)">Stored in the file as plain text. Keep the backup private.</small></span></label>
    <label class="check"><input type="checkbox" id="fb-lists" ${i.lists.n ? 'checked' : 'disabled'}><span>Your list files <small>(${i.lists.n} files, ${mb(i.lists.bytes)})</small></span></label>
    <label class="check"><input type="checkbox" id="fb-posters" ${i.posters.n ? 'checked' : 'disabled'}><span>Posters <small>(${i.posters.n.toLocaleString()} files, ${mb(i.posters.bytes)})</small></span></label>
    <label class="check"><input type="checkbox" id="fb-imdb"><span>IMDb data <small>(${mb(i.imdb.bytes)})</small><br><small style="color:var(--muted-foreground)">Can be downloaded again, so it is off by default. Tick it to restore without downloading 1.9 GB.</small></span></label>
    <div class="bar-actions"><span class="grow" id="fb-sum" role="status"></span><button type="button" class="btn outline" data-close>Cancel</button><button type="button" class="btn" id="fb-go">${I('download')}Save backup…</button></div>
  </div>`;
  d._fb = i;
  showModal('dlg-title');
  fbSum();
}
function fbSum() {
  const i = $('#dlg')._fb; if (!i || !$('#fb')) return;
  const on = id => $(id)?.checked && !$(id).disabled;
  const total = i.db + (on('#fb-lists') ? i.lists.bytes : 0) + (on('#fb-posters') ? i.posters.bytes : 0) + (on('#fb-imdb') ? i.imdb.bytes : 0);
  $('#fb-sum').textContent = `About ${mb(total)} before compression`;
}
async function fbSave() {
  const opts = { keys: $('#fb-keys').checked, lists: $('#fb-lists').checked, posters: $('#fb-posters').checked, imdb: $('#fb-imdb').checked };
  const a = window.pywebview?.api;
  let path = null;
  if (a?.save_backup) {
    try { path = await a.save_backup(`MovieShelf full backup ${new Date().toISOString().slice(0, 10)}.zip`); } catch { }
    if (!path) return toast('Cancelled', 'info');
  }
  const r = await api('/api/backup/full/save', { path, options: opts });
  if (r.error) return toast(r.error, 'err');
  if (!r.started) return toast('Another update is running. Try again when it has finished.', 'err');
  closeModal(); poll();
  toast('Saving the backup. Progress shows at the top.', 'info');
  if (await watchJob(`Full backup saved: ${r.path}`)) { /* the path is also in the activity log */ }
}

async function openFullLoad(first) {
  const d = $('#dlg');
  d.dataset.mode = 'fullbackup';
  d._first = !!first;  // first run: nothing to replace, so no safety question, and the missing parts are fetched afterwards
  d.innerHTML = `${first ? '' : closeBtn}<div class="pad" id="fb">
    <h2 id="dlg-title">Load a full backup</h2>
    <p class="pr-help">${first ? 'Pick a backup .zip made by Save a full backup. It brings back your library, settings, API keys, list files and posters. Anything the backup does not hold (such as the IMDb data) is downloaded afterwards.'
      : 'Pick a backup .zip made by Save a full backup. What you choose below replaces what you have now. A copy of your current library, settings and list files is saved first in <code>data\\backups\\before-restore-…</code>.'}</p>
    <div class="setrow"><label for="fb-path">Backup file</label>
      <div class="line"><input type="text" id="fb-path" dir="ltr" placeholder="C:\\Backups\\MovieShelf full backup.zip"><button type="button" class="btn outline" id="fb-browse">Browse…</button><button type="button" class="btn outline" id="fb-check">Check</button></div></div>
    <div id="fb-info"></div>
    <div class="bar-actions"><span class="grow" id="fb-sum" role="status"></span>${first ? '<button type="button" class="btn outline" id="fb-back">Back</button>' : '<button type="button" class="btn outline" data-close>Cancel</button>'}<button type="button" class="btn${first ? '' : ' danger'}" id="fb-restore" disabled>${first ? 'Restore' : 'Restore…'}</button></div>
  </div>`;
  showModal('dlg-title');
}
async function fbCheck() {
  const path = $('#fb-path').value.trim().replace(/^"|"$/g, ''); if (!path) return $('#fb-path').focus();
  const m = await api('/api/backup/full/inspect', { path });
  const box = $('#fb-info');
  if (m.error) { $('#fb-restore').disabled = true; box.innerHTML = `<div class="err-box" role="alert">${esc(m.error)}</div>`; return; }
  const has = m.has || {};
  box.innerHTML = `<div class="stat" style="margin-bottom:12px"><b style="font-size:16px">Made ${esc(m.created)}</b><span>${mb(m.size)} · ${has.lists || 0} list files · ${(has.posters || 0).toLocaleString()} posters · ${has.imdb ? 'IMDb data included' : 'no IMDb data'} · API keys ${m.has_keys ? 'included' : 'not included'}</span></div>
    <label class="check"><input type="checkbox" checked disabled><span>The library (replaces your current one)</span></label>
    <label class="check"><input type="checkbox" id="fr-settings" ${m.has_settings ? 'checked' : 'disabled'}><span>Settings</span></label>
    <label class="check"><input type="checkbox" id="fr-keys" ${m.has_keys ? 'checked' : 'disabled'}><span>API keys</span></label>
    <label class="check"><input type="checkbox" id="fr-lists" ${has.lists ? 'checked' : 'disabled'}><span>List files</span></label>
    <label class="check"><input type="checkbox" id="fr-posters" ${has.posters ? 'checked' : 'disabled'}><span>Posters</span></label>
    <label class="check"><input type="checkbox" id="fr-imdb" ${has.imdb ? 'checked' : 'disabled'}><span>IMDb data (large; replaces the downloaded data)</span></label>`;
  $('#fb-restore').disabled = false;
}
async function fbRestore() {
  const path = $('#fb-path').value.trim().replace(/^"|"$/g, '');
  const on = id => !!$(id) && $(id).checked && !$(id).disabled;
  const options = { settings: on('#fr-settings'), keys: on('#fr-keys'), lists: on('#fr-lists'), posters: on('#fr-posters'), imdb: on('#fr-imdb') };
  const first = $('#dlg')._first;
  if (!first && !await confirmBox({ title: 'Replace your library with this backup?', text: 'Your current library and the parts you ticked are replaced. A safety copy is saved first, so this can be undone.', ok: 'Restore', danger: true })) return;
  const r = await api('/api/backup/full/restore', { path, options, update: first });
  if (r.error) return toast(r.error, 'err');
  if (!r.started) return toast('Another update is running. Try again when it has finished.', 'err');
  closeModal(); poll();
  toast('Restoring. Progress shows at the top.', 'info');
  if (!await watchJob('Backup restored')) return;
  const res = await api('/api/backup/full/result');
  if (res.warnings?.length) await confirmBox({ title: 'Restored, with notes', text: res.warnings.join('\n'), ok: 'OK' });
  location.reload();
}
$('#modal').addEventListener('click', async e => {
  if ($('#dlg').dataset.mode !== 'fullbackup') return;
  const t = e.target.closest('button'); if (!t) return;
  if (t.id === 'fb-go') return fbSave();
  if (t.id === 'fb-check') return fbCheck();
  if (t.id === 'fb-restore') return fbRestore();
  if (t.id === 'fb-back') return openWelcome();
  if (t.id === 'fb-browse') {
    const a = window.pywebview?.api;
    if (!a?.pick_backup) return $('#fb-path').focus();
    let p = null; try { p = await a.pick_backup(); } catch { }
    if (p) { $('#fb-path').value = p; fbCheck(); }
  }
});
$('#modal').addEventListener('change', e => { if (e.target.closest?.('#fb')) fbSum(); });
$('#modal').addEventListener('keydown', e => { if (e.key === 'Enter' && e.target.id === 'fb-path') fbCheck(); if (e.key === 'Enter' && e.target.id === 'rn-pattern') openRename(e.target.value.trim()); });

// ---------------- first run ----------------
// Setup only installs the program. The first start asks how to begin: load a backup, or start fresh (downloads IMDb data
// and builds the library from the list).
function openWelcome() {
  const d = $('#dlg');
  d.dataset.mode = 'welcome';
  d.innerHTML = `<div class="pad" id="wl">
    <h2 id="dlg-title">Welcome to MovieShelf</h2>
    <p class="pr-help">How do you want to start?</p>
    <div class="wl-grid">
      <button type="button" class="wl-opt" id="wl-backup"><b>Load a backup</b><span>Bring back a full backup made on this or another PC: library, settings, API keys, list files and posters.</span></button>
      <button type="button" class="wl-opt" id="wl-fresh" aria-pressed="false"><b>Start with the defaults</b><span>Read your movie folder or list, download the IMDb data (about 2 GB, 10 to 20 minutes) and build a new library.</span></button>
    </div>
    <div id="wl-box" hidden>
      <div class="setrow"><label for="wl-media">Movie folder<small>The app goes through it by itself and lists every movie it finds. More folders can be added later in Settings.</small></label>
        <div class="line"><input type="text" id="wl-media" dir="ltr" placeholder="D:\Movies"><button type="button" class="btn outline" id="wl-browse-media">Browse…</button></div></div>
      <div class="setrow"><label for="wl-list">Movie list (optional)<small>A .txt or .csv file with movie names, or a folder of them. More can be loaded later with the Load list button.</small></label>
        <div class="line"><input type="text" id="wl-list" dir="ltr" placeholder="D:\Lists\movies.txt"><button type="button" class="btn outline" id="wl-browse-list">Choose a file…</button></div></div>
      <div id="wl-err"></div>
      <div class="bar-actions"><span class="grow"></span><button type="button" class="btn" id="wl-go">Start</button></div>
    </div>
  </div>`;
  showModal('dlg-title');
}
$('#modal').addEventListener('click', async e => {
  if ($('#dlg').dataset.mode !== 'welcome') return;
  const t = e.target.closest('button'); if (!t) return;
  if (t.id === 'wl-backup') return openFullLoad(true);
  if (t.id === 'wl-fresh') {
    t.setAttribute('aria-pressed', 'true');
    $('#wl-box').hidden = false;
    return $('#wl-media').focus();
  }
  if (t.id === 'wl-browse-list') {
    const p = await pickNative('file');
    if (p) $('#wl-list').value = p; else if (p === undefined) $('#wl-list').focus();
    return;
  }
  if (t.id === 'wl-browse-media') {
    const a = window.pywebview?.api; if (!a?.pick_folder) return $('#wl-media').focus();
    let p = null; try { p = await a.pick_folder(); } catch { }
    if (p) $('#wl-media').value = p;
    return;
  }
  if (t.id === 'wl-go') {
    const r = await api('/api/firstrun/fresh', { list_path: $('#wl-list').value.trim().replace(/^"|"$/g, ''), media_dir: $('#wl-media').value.trim() });
    if (r.error) { $('#wl-err').innerHTML = `<div class="err-box" role="alert">${esc(r.error)}</div>`; return; }
    closeModal(); poll();
    toast('Building your library. Progress shows at the top.', 'info');
  }
});

// ---------------- person profile ----------------
// photo, facts and biography come from IMDb's offline data and (with a TMDB key) TMDB; the works are grouped by job,
// those in your library first, then the other movies of the person (which can go on the wishlist)
let PERSON = null, PERSON_FROM = null;
const ROLE_ORDER = ['Actor', 'Director', 'Writer', 'Producer', 'Composer', 'Cinematographer', 'Editor'];
const cap = s => (s || '').replace(/_/g, ' ').replace(/^./, c => c.toUpperCase());
function roleOfJob(job) {
  const j = (job || '').toLowerCase().replace(/_/g, ' ');
  if (j.includes('producer')) return 'Producer';
  if (j.includes('composer')) return 'Composer';
  if (j.includes('photography') || j.includes('cinematograph')) return 'Cinematographer';
  if (j.includes('editor')) return 'Editor';
  if (j.startsWith('casting')) return 'Casting director';
  if (j.startsWith('archive')) return 'Archive footage';
  return cap(j) || 'Other';
}
const pad7n = n => 'nm' + String(n).padStart(7, '0');

// the person's movies in the library, by job: Map(job -> [{m, note}])
function libraryRoles(pid) {
  const roles = new Map();
  const add = (role, m, note) => {
    const list = roles.get(role) || roles.set(role, []).get(role);
    if (!list.some(x => x.m === m)) list.push({ m, note });
  };
  for (const m of ALL) {
    if (!m.pids.has(pid)) continue;
    if ((m.dir || []).some(p => p[0] === pid)) add('Director', m);
    if ((m.wri || []).some(p => p[0] === pid)) add('Writer', m);
    for (const c of m.cast || []) if (c[0] === pid) add('Actor', m, c[2]);
    for (const c of m.crew || []) if (c[0] === pid) add(roleOfJob(c[2]), m, c[2] && roleOfJob(c[2]) !== cap(c[2]) ? cap(c[2]) : '');
  }
  return roles;
}
function libraryIndex() {  // folded title -> movies, to recognise TMDB credits that are in the library
  const idx = new Map();
  for (const m of ALL) for (const t of new Set([m.en, m.title, m.orig].filter(Boolean).map(fold))) (idx.get(t) || idx.set(t, []).get(t)).push(m);
  return idx;
}
const inLibrary = (idx, c) => (idx.get(fold(c.title)) || []).find(m => !c.year || !m.year || Math.abs(m.year - c.year) <= 1);

async function openPerson(pid, name) {
  const d = $('#dlg');
  d.classList.add('wide');
  d.dataset.mode = 'person';
  d.innerHTML = `${closeBtn}<div class="pad"><h2 id="dlg-title" dir="auto">${esc(name || '')}</h2><div class="progress" aria-hidden="true"><i style="width:40%"></i></div><p role="status" class="hint">Loading the profile…</p></div>`;
  showModal('dlg-title');
  const r = await api(`/api/person/${pid}`);
  if ($('#dlg').dataset.mode !== 'person') return;
  const roles = libraryRoles(pid), idx = libraryIndex();
  const others = new Map();  // job -> credits that are not in the library
  for (const c of r.profile?.credits || []) {
    const role = ROLE_ORDER.includes(c.role) ? c.role : roleOfJob(c.role);
    const m = inLibrary(idx, c);
    if (m) { const list = roles.get(role) || roles.set(role, []).get(role); if (!list.some(x => x.m === m)) list.push({ m, note: c.detail }); }
    else (others.get(role) || others.set(role, []).get(role)).push(c);
  }
  const order = [...new Set([...roles.keys(), ...others.keys()])].sort((a, b) => {
    const ia = ROLE_ORDER.indexOf(a), ib = ROLE_ORDER.indexOf(b);
    return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib) || a.localeCompare(b);
  });
  for (const list of roles.values()) list.sort((a, b) => (b.m.year || 0) - (a.m.year || 0));
  PERSON = { pid, name: r.name || name, r, roles, others, order, tab: 'all', more: new Set(), bio: false };
  personView();
}

function personFacts(P) {
  const r = P.r, pr = r.profile || {}, f = [];
  const born = pr.birthday || (r.birth ? String(r.birth) : '');
  const died = pr.deathday || (r.death ? String(r.death) : '');
  if (born) {
    let age = '';
    const by = +born.slice(0, 4), dy = died ? +died.slice(0, 4) : new Date().getFullYear();
    if (by) age = died ? ` (died at ${dy - by})` : ` (${dy - by} years old)`;
    f.push(['Born', `${esc(born)}${pr.place ? ' in ' + esc(pr.place) : ''}${died ? '' : age}`]);
  } else if (pr.place) f.push(['Born in', esc(pr.place)]);
  if (died) f.push(['Died', esc(died) + (born ? age_(born, died) : '')]);
  if (pr.aka?.length) f.push(['Also known as', pr.aka.map(esc).join(' · ')]);
  return f;
}
const age_ = (b, d) => (+b.slice(0, 4) && +d.slice(0, 4)) ? ` (aged ${+d.slice(0, 4) - +b.slice(0, 4)})` : '';

function personView() {
  const P = PERSON, r = P.r, pr = r.profile || {};
  const count = role => (P.roles.get(role)?.length || 0) + (P.others.get(role)?.length || 0);
  const lib = new Set(); P.roles.forEach(l => l.forEach(x => lib.add(x.m)));
  const profs = r.prof.length ? r.prof.map(cap) : (pr.department ? [pr.department] : []);
  const bio = pr.biography;
  const link = (href, label) => `<a class="btn outline small" href="${href}" target="_blank" rel="noopener">${I('ext', 'sm')}${label}</a>`;
  const tabs = P.order.length > 1 ? `<div class="tabs" role="group" aria-label="Show">${[['all', 'All jobs', P.order.reduce((n, k) => n + count(k), 0)], ...P.order.map(k => [k, k, count(k)])]
    .map(([k, l, n]) => `<button type="button" class="chip${P.tab === k ? ' on' : ''}" aria-pressed="${P.tab === k}" data-ptab="${esc(k)}">${esc(l)}<small>${n}</small></button>`).join('')}</div>` : '';
  const sections = P.order.filter(k => P.tab === 'all' || P.tab === k).map(role => {
    const mine = P.roles.get(role) || [], other = P.others.get(role) || [];
    const shown = P.more.has(role) ? other : other.slice(0, 8);
    return `<section class="psec"><h3>${esc(role)} <small>${mine.length ? `${mine.length} in your library` : 'none in your library'}${other.length ? ` · ${other.length} not in it` : ''}</small></h3>
      ${mine.length ? `<div class="pgrid">${mine.map(({ m, note }) => `<button type="button" class="pcard" data-pmovie="${esc(m.k)}" title="${esc(m.en || m.title)}">
        <span class="pw">${posterHtml(m)}</span><b>${esc(m.en || m.title)}</b><small>${[m.year, note].filter(Boolean).map(esc).join(' · ')}</small></button>`).join('')}</div>` : ''}
      ${other.length ? `<div class="porows">${shown.map(c => `<div class="porow"><span class="y">${c.year || ''}</span><span class="t">${esc(c.title)}${c.detail ? `<small> · ${esc(c.detail)}</small>` : ''}</span>
        <button type="button" class="btn ghost small" data-pwish="${esc(JSON.stringify([c.title, c.year, role]))}">${I('check', 'sm')}Wishlist</button></div>`).join('')}</div>
        ${other.length > shown.length ? `<button type="button" class="link" data-pmore="${esc(role)}">Show all ${other.length}</button>` : ''}` : ''}</section>`;
  }).join('');
  $('#dlg').innerHTML = `${closeBtn}<div class="pad person">
    <div class="phead">
      <div class="pphoto">${r.photo ? `<img src="/person/${pad7n(P.pid)}.jpg" alt="Photo of ${esc(P.name)}">` : `<span class="pinit" aria-hidden="true">${esc((P.name || '?').split(' ').map(w => w[0]).slice(0, 2).join(''))}</span>`}</div>
      <div class="pinfo">
        <h2 id="dlg-title" dir="auto">${esc(P.name)}</h2>
        ${profs.length ? `<div class="chips">${profs.map(p => `<span class="chip">${esc(p)}</span>`).join('')}</div>` : ''}
        <dl class="kv pfacts">${personFacts(P).map(([k, v]) => `<dt>${k}</dt><dd>${v}</dd>`).join('')}<dt>In your library</dt><dd>${lib.size.toLocaleString()} ${lib.size === 1 ? 'movie' : 'movies'}</dd></dl>
        <div class="btns">
          ${lib.size ? `<button type="button" class="btn small" data-pfilter>Show ${lib.size === 1 ? 'it' : 'all ' + lib.size} in the library</button>` : ''}
          ${link(`https://www.imdb.com/name/${pad7n(P.pid)}/`, 'IMDb')}
          ${pr.tmdb_id ? link(`https://www.themoviedb.org/person/${pr.tmdb_id}`, 'TMDB') : ''}
          ${pr.homepage ? link(esc(pr.homepage), 'Website') : ''}
          ${r.tmdb ? `<button type="button" class="btn outline small" data-prefresh>Refresh</button>` : ''}
          ${PERSON_FROM ? `<button type="button" class="btn ghost small" data-pback>Back to the movie</button>` : ''}
        </div>
      </div>
    </div>
    ${r.error ? `<div class="err-box" role="alert">Could not reach TMDB: ${esc(r.error)}</div>` : ''}
    ${!r.tmdb ? `<div class="err-box" style="background:var(--warn-bg);border-color:var(--warn);color:var(--warn)">Photo, biography and the full filmography come from TMDB. Add a free key in Settings → Online data. <button type="button" class="link" data-psettings>Open settings</button></div>` : ''}
    ${r.tmdb && !pr.name && !r.error ? `<p class="hint">TMDB has no profile for this person.</p>` : ''}
    ${bio ? `<div class="pbio${P.bio ? '' : ' clamp'}" dir="auto">${esc(bio)}</div>${bio.length > 420 ? `<button type="button" class="link" data-pbio>${P.bio ? 'Show less' : 'Read more'}</button>` : ''}` : ''}
    ${tabs}
    ${sections || '<p class="hint">No movies found for this person.</p>'}
  </div>`;
}

DIALOG_CLICKS.person = async function (t) {
  const P = PERSON; if (!P) return;
  if (t.dataset.ptab) { P.tab = t.dataset.ptab; return personView(); }
  if (t.dataset.pmore) { P.more.add(t.dataset.pmore); return personView(); }
  if (t.dataset.pbio !== undefined) { P.bio = !P.bio; return personView(); }
  if (t.dataset.pmovie) { const m = ALL.find(x => x.k === t.dataset.pmovie); if (m) { PERSON_FROM = null; closeModal(); openMovie(m); } return; }
  if (t.dataset.pfilter !== undefined) { resetFilters(); F.person = { id: P.pid, name: P.name }; PERSON_FROM = null; closeModal(); buildFacets(); return apply(); }
  if (t.dataset.prefresh !== undefined) { t.disabled = true; t.textContent = 'Refreshing…'; await api(`/api/person/${P.pid}?refresh=1`); return openPerson(P.pid, P.name); }
  if (t.dataset.pback !== undefined) { const m = ALL.find(x => x.k === PERSON_FROM); PERSON_FROM = null; if (m) openMovie(m); return; }
  if (t.dataset.psettings !== undefined) return openSettings('online');
  if (t.dataset.pwish) { const [title, year, role] = JSON.parse(t.dataset.pwish); return wishAdd([{ title, year, source: 'Person', note: `${P.name}: ${role}` }]); }
};

// ---------------- init ----------------
{
  let sv = localStorage.getItem('sort') || 'rating';
  if (sv === 'year') { sv = 'year-desc'; REV = true; }  // older saved choice: oldest first
  $('#sort').value = [...$('#sort').options].some(o => o.value === sv) ? sv : 'rating';
}
$('#group').value = GROUPS[GROUP] ? GROUP : (GROUP = 'none');
applyTheme(themePref());
$$('[data-ico]').forEach(e => { e.innerHTML = { rt: RT_FRESH, mc: MC_ICON }[e.dataset.ico]; });
applyI18n(); langBtn();
loadViews();
restoreFilters();
setView(viewMode);
$('#grid').innerHTML = '<article class="card skel" aria-hidden="true"><div class="pw"></div><div class="t"></div><div class="m"></div></article>'.repeat(18);
load().then(poll);
api('/api/status').then(st => { if (st.first_run) openWelcome(); });
