// The search language of the box above the results. Plain script (no imports) so it can be tested with node:
// it uses fold(), TYPE_LABEL and NEEDS_REVIEW from app.js, but only when a query runs.
// ---------------- advanced search ----------------
// free words must all match · "exact phrase" · -word leaves it out · field:value narrows
// numbers: 8 (at least, for scores) · >8 · <=1980 · 1990-1999 · 5k
const numVal = s => { const m = /^(\d+(?:\.\d+)?)([km])?$/i.exec(s); return m ? parseFloat(m[1]) * ({ k: 1e3, m: 1e6 }[(m[2] || '').toLowerCase()] || 1) : NaN; };
function numSpec(v, bareAtLeast) {
  v = v.replace(/,/g, '');
  let m = /^(>=|<=|>|<|=)(.+)$/.exec(v);
  if (m) {
    const x = numVal(m[2]); if (isNaN(x)) return null;
    return { '>': a => a > x, '>=': a => a >= x, '<': a => a < x, '<=': a => a <= x, '=': a => a === x }[m[1]];
  }
  m = /^([\d.]+[km]?)-([\d.]+[km]?)$/i.exec(v);
  if (m) { const lo = numVal(m[1]), hi = numVal(m[2]); return isNaN(lo) || isNaN(hi) ? null : a => a >= lo && a <= hi; }
  const x = numVal(v);
  return isNaN(x) ? null : bareAtLeast ? a => a >= x : a => a === x;
}
const joinNames = (list, i = 1) => fold((list || []).map(p => p[i]).join(' | '));
const NUM_FIELDS = {
  year: [m => m.year, 0], runtime: [m => m.runtime, 0], files: [m => m.files.length, 0],
  rating: [m => m.rating, 1], imdb: [m => m.rating, 1], votes: [m => m.votes, 1], rt: [m => m.rt, 1], mc: [m => m.mc, 1], myrating: [m => m.myrating, 1],
};
const TXT_FIELDS = {
  genre: m => fold((m.genres || []).join(' | ')),
  dir: m => m.fdir, director: m => m.fdir, writer: m => m.fwri,
  actor: m => m.fcast, cast: m => m.fcast, star: m => m.fcast,
  person: m => m.fdir + ' | ' + m.fwri + ' | ' + m.fcast,
  country: m => fold((m.countries || []).join(' | ')), lang: m => fold((m.langs || []).join(' | ')), language: m => fold((m.langs || []).join(' | ')),
  type: m => fold((TYPE_LABEL[m.type] || 'unmatched') + ' ' + (m.type || '')),
  res: m => fold(m.files.map(f => f.res).join(' ')), quality: m => fold(m.files.map(f => f.res).join(' ')),
  src: m => fold(m.files.map(f => f.src).join(' ')), source: m => fold(m.files.map(f => f.src).join(' ')),
  codec: m => fold(m.files.map(f => f.codec).join(' ')),
  audio: m => m.faudio || '', sub: m => m.fsubs || '', subs: m => m.fsubs || '', subtitle: m => m.fsubs || '',
  title: m => m.ftitle, file: m => m.ffile, path: m => m.ffile,
  collection: m => fold(m.coll || ''), id: m => fold(m.tt || ''),
};
const IS_FLAGS = {
  fav: m => m.fav, favorite: m => m.fav, watched: m => m.watched, seen: m => m.watched, unwatched: m => !m.watched,
  review: m => NEEDS_REVIEW.has(m.conf), multiple: m => m.files.length > 1, dupes: m => m.files.length > 1, noposter: m => !m.poster,
  top: m => m.rating >= 8, dubbed: m => m.files.some(f => f.fa), dual: m => m.files.some(f => f.dual), hdr: m => m.files.some(f => f.hdr), '4k': m => m.res === '2160p',
};
const HAS_FLAGS = { poster: m => m.poster, plot: m => m.plot, rt: m => m.rt != null, mc: m => m.mc != null, cast: m => m.cast?.length, rating: m => m.rating, notes: m => m.notes };

function parseQuery(q) {
  const preds = [], ops = [], plain = [];
  const re = /(-?)(?:([a-z0-9]+):)?(?:"([^"]*)"|(\S+))/gi;
  for (let mt; (mt = re.exec(q));) {
    const neg = mt[1] === '-', key = (mt[2] || '').toLowerCase(), val = mt[3] ?? mt[4] ?? '';
    let fn = null;
    if (key && val) {
      if (NUM_FIELDS[key]) { const [get, bare] = NUM_FIELDS[key], t = numSpec(val, bare); if (t) fn = m => { const v = get(m); return v != null && v !== '' && t(+v); }; }
      else if (TXT_FIELDS[key]) { const get = TXT_FIELDS[key], v = fold(/^(res|quality)$/.test(key) && /^4k$/i.test(val) ? '2160p' : val); fn = m => get(m).includes(v); }
      else if (key === 'is' && IS_FLAGS[val.toLowerCase()]) { const f = IS_FLAGS[val.toLowerCase()]; fn = m => !!f(m); }
      else if (key === 'has' && HAS_FLAGS[val.toLowerCase()]) { const f = HAS_FLAGS[val.toLowerCase()]; fn = m => !!f(m); }
    }
    if (fn) { preds.push(neg ? m => !fn(m) : fn); ops.push({ raw: mt[0] }); continue; }
    // an unknown "key:" is plain text ("mission:impossible" finds Mission: Impossible)
    const phrase = mt[3] !== undefined && !key;  // "exact phrase": the words in this order
    const words = phrase ? [fold(val).trim()].filter(Boolean) : fold(key ? key + ' ' + val : val).split(/\s+/).filter(Boolean);
    if (!words.length) continue;
    fn = m => words.every(w => /^tt\d+$/.test(w) ? m.tt === 'tt' + w.slice(2).padStart(7, '0') : m.hay.includes(w));
    preds.push(neg ? m => !fn(m) : fn);
    neg ? ops.push({ raw: mt[0] }) : plain.push(words.join(' '));
  }
  return { ops, plain: plain.join(' '), test: m => preds.every(p => p(m)) };
}
