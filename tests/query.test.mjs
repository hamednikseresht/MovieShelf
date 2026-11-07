// Search language tests. Run from the MovieShelf folder:  node --test tests/query.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

// query.js is a plain browser script: run it with the three things it borrows from app.js
const code = fs.readFileSync(new URL('../app/web/query.js', import.meta.url), 'utf8');
const ctx = {
  fold: s => String(s ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[’'`´]/g, ''),
  TYPE_LABEL: { movie: 'Movie', tvMiniSeries: 'Mini-series' },
  NEEDS_REVIEW: new Set(['fuzzy', 'none']),
};
vm.createContext(ctx);
const { parseQuery, numSpec } = vm.runInContext(code + '\n;({ parseQuery, numSpec })', ctx);

const fold = ctx.fold;
const movie = (o = {}) => {
  const m = { k: 'tt0000001', tt: 'tt0000001', title: 'The Godfather', year: 1972, rating: 9.2, votes: 2_300_000, rt: 97, mc: 100,
    genres: ['Crime', 'Drama'], files: [{ res: '1080p', src: 'BluRay', raw: 'The.Godfather.1972.1080p', fa: 1 }], conf: 'exact', type: 'movie',
    dir: [[1, 'Francis Ford Coppola']], cast: [[2, 'Marlon Brando', 'Don']], wri: [], countries: ['USA'], langs: ['English'], ...o };
  m.hay = fold([m.title, m.year, m.tt, m.files.map(f => f.raw).join(' ')].join(' | '));
  m.ftitle = fold(m.title); m.ffile = fold(m.files.map(f => f.raw).join(' '));
  m.fdir = fold(m.dir.map(p => p[1]).join(' | ')); m.fcast = fold(m.cast.map(p => p[1]).join(' | ')); m.fwri = '';
  return m;
};
const hit = (q, m = movie()) => parseQuery(q).test(m);

test('plain words must all match', () => {
  assert.ok(hit('godfather'));
  assert.ok(hit('the godfather 1972'));
  assert.ok(!hit('godfather 1999'));
});

test('quotes and exclusion', () => {
  assert.ok(hit('"the godfather"'));
  assert.ok(!hit('"godfather the"'));
  assert.ok(!hit('godfather -1972'));
  assert.ok(hit('godfather -zombie'));
});

test('numeric fields', () => {
  assert.ok(hit('year:1972') && hit('year:1970-1979') && hit('year:>1960') && hit('year:<=1972'));
  assert.ok(!hit('year:>1972') && !hit('year:1980-1990'));
  assert.ok(hit('rating:9') && hit('rating:>9') && !hit('rating:9.5'), 'a bare score means at least');
  assert.ok(hit('votes:>1m') && hit('votes:2m') === true && !hit('votes:>5m'));
  assert.ok(hit('rt:>=95 mc:>=90') && !hit('rt:>=99'));
});

test('missing numbers never match', () => {
  assert.ok(!hit('rt:>0', movie({ rt: null })));
  assert.ok(!hit('year:>1900', movie({ year: undefined })));
});

test('text fields', () => {
  assert.ok(hit('genre:crime') && hit('genre:drama') && !hit('genre:comedy'));
  assert.ok(hit('dir:coppola') && hit('actor:"marlon brando"') && !hit('dir:nolan'));
  assert.ok(hit('res:1080p') && hit('res:1080') && !hit('res:720p'));
  assert.ok(hit('res:4k', movie({ files: [{ res: '2160p', raw: 'x' }] })));
  assert.ok(hit('country:usa') && hit('lang:english') && hit('type:movie'));
});

test('flags', () => {
  assert.ok(hit('is:dubbed') && hit('is:unwatched') && !hit('is:fav') && !hit('is:review'));
  assert.ok(hit('is:fav', movie({ fav: 1 })) && hit('is:review', movie({ conf: 'fuzzy' })));
  assert.ok(hit('has:poster', movie({ poster: true })) && !hit('has:poster'));
});

test('an unknown field is plain text', () => {
  const m = movie({ title: 'Mission: Impossible' });
  m.hay = fold('Mission: Impossible | 1996');
  assert.ok(hit('mission:impossible', m));
});

test('imdb ids', () => {
  assert.ok(hit('tt0000001') && hit('tt1') && !hit('tt0000002'));
});

test('operators are listed for the tags above the results', () => {
  const q = parseQuery('godfather year:>1960 -zombie');
  assert.equal(JSON.stringify(q.ops.map(o => o.raw)), JSON.stringify(['year:>1960', '-zombie']));
  assert.equal(q.plain, 'godfather');
});

test('numSpec', () => {
  assert.equal(numSpec('abc', false), null);
  assert.ok(numSpec('1k-2k', false)(1500) && !numSpec('1k-2k', false)(2500));
  assert.ok(numSpec('5', true)(5) && numSpec('5', true)(6) && !numSpec('5', false)(6));
});
