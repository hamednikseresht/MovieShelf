// The Help viewer, shared by the Help window in the app and the stand-alone page (help.html).
// The text lives in help/en.html and help/fa.html: one <section id="..."> with an <h2> per topic.
const HELP_CACHE = {};
async function helpText(lang) {
  if (!HELP_CACHE[lang]) {
    const r = await fetch(`/static/help/${lang}.html`);
    if (!r.ok) throw new Error('help not found');
    HELP_CACHE[lang] = await r.text();
  }
  return HELP_CACHE[lang];
}

// fills `el` with a contents list, a search box and the document; scrolls to `sectionId` when given
async function helpMount(el, lang, sectionId) {
  const tt = typeof tr === 'function' ? tr : s => s;
  let html;
  try { html = await helpText(lang); } catch { el.innerHTML = `<p class="hint">${tt('The help could not be loaded.')}</p>`; return; }
  el.innerHTML = `<div class="helpwrap" data-rtl>
    <nav class="helpnav" aria-label="${tt('Contents')}">
      <input type="search" id="h-q" placeholder="${tt('Search the help')}" aria-label="${tt('Search the help')}" autocomplete="off">
      <ol id="h-toc"></ol>
    </nav>
    <article class="helpdoc" id="h-doc" tabindex="-1">${html}<p class="hint" id="h-none" hidden>${tt('Nothing found in the help.')}</p></article>
  </div>`;
  const doc = el.querySelector('#h-doc'), toc = el.querySelector('#h-toc');
  const secs = [...doc.querySelectorAll('section')];
  toc.innerHTML = secs.map(s => `<li><a href="#${s.id}" data-hs="${s.id}">${s.querySelector('h2').textContent}</a></li>`).join('');
  const dirOf = () => { doc.parentElement.dir = lang === 'fa' ? 'rtl' : 'ltr'; };
  dirOf();
  const mark = id => toc.querySelectorAll('a').forEach(a => a.classList.toggle('on', a.dataset.hs === id));
  const go = id => { const s = doc.querySelector('#' + CSS.escape(id)); if (s) { s.scrollIntoView({ block: 'start' }); mark(id); } };
  toc.addEventListener('click', e => { const a = e.target.closest('[data-hs]'); if (!a) return; e.preventDefault(); go(a.dataset.hs); });
  doc.addEventListener('scroll', () => {  // the topic being read stays marked in the contents
    const top = doc.getBoundingClientRect().top + 24;
    const cur = secs.filter(s => !s.hidden).reverse().find(s => s.getBoundingClientRect().top <= top);
    if (cur) mark(cur.id);
  }, { passive: true });
  el.querySelector('#h-q').addEventListener('input', e => {
    const q = e.target.value.trim().toLowerCase();
    let shown = 0;
    secs.forEach(s => {
      const hit = !q || s.textContent.toLowerCase().includes(q);
      s.hidden = !hit;
      toc.querySelector(`[data-hs="${s.id}"]`).parentElement.hidden = !hit;
      shown += hit;
    });
    el.querySelector('#h-none').hidden = shown > 0;
  });
  if (sectionId) go(sectionId); else mark(secs[0]?.id);
}
