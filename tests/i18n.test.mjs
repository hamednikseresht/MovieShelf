// Help and Persian menu checks. Run from the MovieShelf folder:  node --test tests/i18n.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const read = p => fs.readFileSync(new URL('../app/web/' + p, import.meta.url), 'utf8');
const sectionIds = html => [...html.matchAll(/<section id="([^"]+)">/g)].map(m => m[1]);
const headings = html => [...html.matchAll(/<h[23]>/g)].length;

test('the English and Persian help have the same topics in the same order', () => {
  const en = read('help/en.html'), fa = read('help/fa.html');
  assert.deepEqual(sectionIds(fa), sectionIds(en));
  assert.ok(sectionIds(en).length >= 12);
  assert.equal(headings(fa), headings(en), 'every heading is translated');
});

test('both help files are balanced and have no leftovers', () => {
  for (const f of ['help/en.html', 'help/fa.html']) {
    const h = read(f);
    for (const tag of ['section', 'table', 'ul', 'ol', 'dl', 'tr']) {
      const open = (h.match(new RegExp(`<${tag}[ >]`, 'g')) || []).length, close = (h.match(new RegExp(`</${tag}>`, 'g')) || []).length;
      assert.equal(open, close, `${f}: <${tag}> balanced`);
    }
    assert.ok(!/TODO|lorem/i.test(h), f);
  }
});

test('code and shortcut examples are identical in both languages', () => {
  const codes = h => [...h.matchAll(/<code>([^<]*)<\/code>/g)].map(m => m[1]).sort();
  const en = codes(read('help/en.html')), fa = codes(read('help/fa.html'));
  const missing = en.filter(c => !fa.includes(c));
  assert.deepEqual(missing, [], 'every command or file name in the English help also appears in the Persian help');
});

test('every marked menu text has a Persian translation', () => {
  const ctx = {};
  vm.createContext(ctx);
  const FA = vm.runInContext(read('i18n.js') + '\n;FA', ctx);
  const html = read('index.html');
  const need = new Set();
  for (const m of html.matchAll(/<[^<>]*?data-i18n(?:\s[^<>]*)?>\s*(?:<svg.*?<\/svg>\s*)?([^<>]+?)\s*</gs)) need.add(m[1].trim());
  for (const m of html.matchAll(/<[^<>]*?data-i18n-attr="([^"]+)"[^<>]*>/g)) {
    for (const a of m[1].split(' ')) { const v = new RegExp(`\\s${a}="([^"]*)"`).exec(m[0]); if (v) need.add(v[1].replace(/&gt;/g, '>')); }
  }
  const missing = [...need].filter(k => k && !(k in FA));
  assert.deepEqual(missing, []);
  assert.ok(need.size > 60);
});

test('every translation is used by the page or the app', () => {
  const ctx = {};
  vm.createContext(ctx);
  const FA = vm.runInContext(read('i18n.js') + '\n;FA', ctx);
  const source = (read('index.html') + read('help.html')).replace(/&gt;/g, '>').replace(/&amp;/g, '&') + read('app.js') + read('help.js');
  const unused = Object.keys(FA).filter(k => !source.includes(k) && !['English', 'Persian', 'Poster'].includes(k));
  assert.deepEqual(unused, [], 'remove translations nobody asks for');
});
