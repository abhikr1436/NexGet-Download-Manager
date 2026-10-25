// IDM-style "Download this video" panel: shows over any playing (or hovered) video; clicking it lists the
// available qualities right on the page (prefetched by NexGet when playback starts), picking one opens NexGet.
(() => {
  let btn, label, menu, mx = -1, my = -1, target = null, hideTimer;
  const dismissed = new WeakSet(), prefetched = new Set();
  const send = msg => new Promise(res => chrome.runtime.sendMessage(msg, r => res(chrome.runtime.lastError ? { error: 'NexGet extension reloaded - refresh this page' } : r || {})));
  const css = (el, s) => Object.assign(el.style, s);

  function make() {
    btn = document.createElement('div'); btn.setAttribute('data-nexget', '');
    css(btn, { position: 'fixed', zIndex: 2147483647, display: 'none', flexDirection: 'column', alignItems: 'stretch',
               font: '600 13px Segoe UI, Arial, sans-serif', color: '#fff', userSelect: 'none', pointerEvents: 'auto' });
    const bar = document.createElement('div');
    css(bar, { display: 'flex', alignItems: 'center', gap: '6px', background: 'linear-gradient(135deg,#6E4BFF,#4326D9)',
               border: '1px solid #3A20B8', borderRadius: '4px', boxShadow: '0 2px 8px rgba(0,0,0,.45)',
               padding: '5px 6px 5px 10px', cursor: 'pointer', alignSelf: 'flex-end' });
    label = document.createElement('span'); label.textContent = '⬇ Download this video';
    const x = document.createElement('span'); x.textContent = '✕'; x.title = 'Hide for this video';
    css(x, { padding: '0 4px', opacity: '.8' });
    bar.append(label, x);
    menu = document.createElement('div');
    css(menu, { display: 'none', marginTop: '4px', background: '#fff', color: '#1C1B29', border: '1px solid #D9D5EE',
                borderRadius: '4px', boxShadow: '0 4px 14px rgba(0,0,0,.35)', minWidth: '260px', maxHeight: '320px', overflowY: 'auto',
                font: '13px Segoe UI, Arial, sans-serif' });
    btn.append(bar, menu);
    x.addEventListener('click', e => { e.stopPropagation(); if (target) dismissed.add(target); close(); hide(); });
    bar.addEventListener('click', e => { e.stopPropagation(); e.preventDefault(); menu.style.display === 'none' ? open() : close(); });
    btn.addEventListener('mouseenter', () => clearTimeout(hideTimer));
    document.addEventListener('click', e => { if (btn && !btn.contains(e.target)) close(); }, true);
  }

  function row(text, onClick, muted) {
    const r = document.createElement('div'); r.textContent = text;
    css(r, { padding: '8px 12px', cursor: onClick ? 'pointer' : 'default', color: muted ? '#6B6A80' : '#1C1B29',
             borderBottom: '1px solid #EFEDF6', whiteSpace: 'pre-wrap' });
    if (onClick) {
      r.onmouseenter = () => (r.style.background = '#F4F1FF'); r.onmouseleave = () => (r.style.background = '');
      r.onclick = e => { e.stopPropagation(); onClick(); };
    }
    menu.append(r);
  }

  async function open() {
    menu.replaceChildren(); menu.style.display = 'block';
    row('⏳ Finding available qualities...', null, true);
    const page = location.href;
    const r = await send({ type: 'formats', pageUrl: page });
    if (menu.style.display === 'none') return;
    menu.replaceChildren();
    if (r.error && !(r.choices || []).length) {
      row(/Failed to fetch|NexGet \d|timed out|aborted/i.test(r.error) ? '⚠ NexGet is not running - start it and try again' : '⚠ ' + r.error.slice(0, 300), null, true);
      return;
    }
    if (!(r.choices || []).length) { row('No downloadable stream found yet. Play the video for a few seconds, then try again.', null, true); return; }
    for (const c of r.choices) row(c.label, () => { close(); send({ type: 'grab', pageUrl: page, id: c.id }); flash('✔ Opening NexGet...'); });
  }
  const close = () => menu && (menu.style.display = 'none');
  const flash = t => { label.textContent = t; setTimeout(() => (label.textContent = '⬇ Download this video'), 2500); };
  const hide = () => { if (btn && menu.style.display === 'none') btn.style.display = 'none'; };

  function visibleRect(v) {
    const r = v.getBoundingClientRect();
    if (r.width < 200 || r.height < 110 || r.bottom < 30 || r.top > innerHeight - 30 || r.right < 0 || r.left > innerWidth) return null;
    return r;
  }

  function pick() {
    let best = null, bestArea = 0;
    for (const v of document.querySelectorAll('video')) {
      if (dismissed.has(v)) continue;
      const r = visibleRect(v); if (!r) continue;
      const playing = !v.paused && !v.ended && v.readyState > 2;
      const hovered = mx >= r.left && mx <= r.right && my >= r.top && my <= r.bottom;
      if (!playing && !hovered) continue;
      const area = r.width * r.height * (hovered ? 2 : 1);
      if (area > bestArea) { best = v; bestArea = area; }
    }
    return best;
  }

  function update() {
    if (btn && menu.style.display !== 'none') return;          // keep menu where it is while open
    const v = pick();
    if (!v) return hide();
    if (!btn) make();
    const host = document.fullscreenElement || document.body || document.documentElement;
    if (btn.parentNode !== host) host.appendChild(btn);
    target = v;
    const r = v.getBoundingClientRect();
    btn.style.display = 'flex';
    btn.style.top = Math.max(4, r.top + 10) + 'px';
    btn.style.right = Math.max(4, document.documentElement.clientWidth - r.right + 12) + 'px';   // grows leftward
    // start analysing in NexGet as soon as something plays, so the menu opens instantly
    if (!v.paused && !prefetched.has(location.href)) { prefetched.add(location.href); send({ type: 'prefetch', pageUrl: location.href }); }
  }

  addEventListener('mousemove', e => { mx = e.clientX; my = e.clientY; }, { passive: true, capture: true });
  addEventListener('scroll', update, { passive: true, capture: true });
  document.addEventListener('play', update, true);
  document.addEventListener('fullscreenchange', update);
  setInterval(update, 700);

  chrome.runtime.onMessage.addListener((msg, _s, reply) => {
    if (msg.type !== 'getLinks') return;
    let anchors = [...document.querySelectorAll('a[href]')];
    const sel = getSelection();
    if (msg.selection && sel.rangeCount) anchors = anchors.filter(a => sel.containsNode(a, true));
    const urls = anchors.map(a => a.href);
    document.querySelectorAll('video[src], audio[src], source[src]').forEach(m => urls.push(m.src));
    reply([...new Set(urls)]);
  });
})();
