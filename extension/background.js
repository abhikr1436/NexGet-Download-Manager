const APP = 'http://127.0.0.1:9614';
const FIREFOX = typeof browser !== 'undefined' && /Firefox\//.test(navigator.userAgent);
const BROWSER = FIREFOX ? 'firefox' : /Edg\//.test(navigator.userAgent) ? 'edge' : /OPR\//.test(navigator.userAgent) ? 'opera'
  : /Vivaldi/.test(navigator.userAgent) ? 'vivaldi' : navigator.brave ? 'brave' : 'chrome';
// tabId -> [{url, type}], kept in storage.session because Chrome stops this worker after ~30 s idle
const mediaGet = async id => ((await chrome.storage.session.get('m' + id))['m' + id]) || [];
const mediaSet = (id, list) => chrome.storage.session.set({ ['m' + id]: list });
const mediaDel = id => chrome.storage.session.remove('m' + id);
let lock = Promise.resolve();          // serialize read-modify-write per event

// heartbeat: lets the app show "browser integration connected"
// Chrome Web Store builds exclude YouTube (store policy); the page button is off there and video requests are refused.
const STORE_NO_YT = ((chrome.runtime.getManifest().content_scripts || [])[0]?.exclude_matches || []).length > 0;
const isYouTube = u => { try { return /(^|\.)(youtube\.com|youtu\.be|youtube-nocookie\.com)$/.test(new URL(u).hostname); } catch { return false; } };
const ytRefused = { error: 'YouTube downloads are not available in the store version of this add-on. Copy the video link and use Add URL in NexGet.' };

const beat = () => app('/ping?b=' + BROWSER).catch(() => {});
chrome.alarms.create('nexget-beat', { periodInMinutes: 1 });
chrome.alarms.onAlarm.addListener(beat);
chrome.runtime.onStartup.addListener(beat);
beat();

async function app(path, body, timeout = 1500) {
  const r = await fetch(APP + path, {
    method: body ? 'POST' : 'GET', headers: { 'Content-Type': 'application/json' },
    body: body && JSON.stringify(body), signal: AbortSignal.timeout(timeout)
  });
  if (!r.ok) throw new Error('NexGet ' + r.status);
  return r.json();
}
async function cookies(url) {
  try { return (await chrome.cookies.getAll({ url })).map(c => c.name + '=' + c.value).join('; '); } catch { return ''; }
}
// full cookie records (page + media hosts) so yt-dlp gets a proper cookie jar, e.g. for logged-in course sites
async function cookieJar(urls) {
  const seen = new Map();
  for (const url of new Set(urls.filter(u => /^https?:/.test(u)))) {
    try {
      for (const c of await chrome.cookies.getAll({ url }))
        seen.set(c.domain + c.path + c.name, { domain: c.domain, hostOnly: c.hostOnly, path: c.path, secure: c.secure,
                                               expires: Math.floor(c.expirationDate || 0), name: c.name, value: c.value });
    } catch {}
  }
  return [...seen.values()];
}
const extOf = name => (name.split(/[?#]/)[0].split('.').pop() || '').toUpperCase();
const matches = (pats, ext) => pats.some(p => new RegExp('^' + p.replace(/[.+?^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '.*') + '$').test(ext));

// ---- capture browser downloads (Chromium decides before saving; Firefox has no such hook, so cancel on create)
if (chrome.downloads.onDeterminingFilename) {
  chrome.downloads.onDeterminingFilename.addListener((item, suggest) => {
    capture(item).then(taken => { if (!taken) suggest(); }).catch(() => suggest());
    return true;
  });
} else {
  chrome.downloads.onCreated.addListener(item => { capture(item).catch(() => {}); });
}
async function capture(item) {
  const url = item.finalUrl || item.url;
  if (!/^(https?|ftp):/i.test(url)) return false;
  const { capture = true } = await chrome.storage.local.get('capture');
  if (!capture) return false;
  let cfg; try { cfg = await app('/ping'); } catch { return false; }   // app not running -> let browser download
  const name = (item.filename || '').split(/[\\/]/).pop() || url.split(/[?#]/)[0].split('/').pop();
  if (!matches(cfg.exts, extOf(name))) return false;
  await chrome.downloads.cancel(item.id).catch(() => {});
  chrome.downloads.erase({ id: item.id }).catch(() => {});
  await app('/download', { url, referrer: item.referrer || '', filename: name, size: item.fileSize || item.totalBytes || -1,
                           cookies: await cookies(url), ua: navigator.userAgent });
  return true;
}

// ---- sniff media streams per tab, remembering the exact request headers the player used (Referer/Origin/auth...)
const sent = new Map();   // requestId -> headers (short-lived)
const KEEP = /^(referer|origin|user-agent|authorization|x-[\w-]+)$/i;
chrome.webRequest.onSendHeaders.addListener(d => {
  const h = {};
  for (const { name, value } of d.requestHeaders || []) if (KEEP.test(name) && value) h[name] = value;
  sent.set(d.requestId, h);
  if (sent.size > 500) sent.delete(sent.keys().next().value);
}, { urls: ['<all_urls>'], types: ['media', 'xmlhttprequest', 'other'] }, FIREFOX ? ['requestHeaders'] : ['requestHeaders', 'extraHeaders']);

chrome.webRequest.onHeadersReceived.addListener(d => {
  if (d.tabId < 0) {        // players like hls.js fetch from a web worker: map back to the tab by origin
    const origin = d.initiator || (d.originUrl && new URL(d.originUrl).origin);
    if (!origin || !origin.startsWith('http')) return;
    chrome.tabs.query({ url: origin + '/*' }).then(tabs => {
      const t = tabs.find(t => t.active) || tabs[0];
      if (t) sniff({ ...d, tabId: t.id });
    });
    return;
  }
  sniff(d);
}, { urls: ['<all_urls>'], types: ['media', 'xmlhttprequest', 'other'] }, ['responseHeaders']);

function sniff(d) {
  const hdr = n => ((d.responseHeaders || []).find(h => h.name.toLowerCase() === n) || {}).value || '';
  const ct = hdr('content-type'), u = d.url, path = u.split('?')[0];
  const headers = sent.get(d.requestId) || {}; sent.delete(d.requestId);
  if (d.statusCode >= 400) return;
  let type = null;
  if (/mpegurl/i.test(ct) || /\.m3u8$/i.test(path)) type = 'hls';
  else if (/dash\+xml/i.test(ct) || /\.mpd$/i.test(path)) type = 'dash';
  else if (/^(video|audio)\//i.test(ct) && !/\.(ts|m4s|aac)$/i.test(path) && !/googlevideo\.com|youtube\.com\/s\//.test(u))
    type = ct.split('/')[0].toLowerCase();
  if (!type) return;
  const size = +(hdr('content-range').split('/')[1] || hdr('content-length') || 0) || 0;
  lock = lock.then(async () => {
    const list = await mediaGet(d.tabId);
    const old = list.find(m => m.url.split('?')[0] === path);
    if (old) { old.url = u; old.headers = headers; await mediaSet(d.tabId, list); return; }   // keep freshest signed URL
    if (list.length >= 30) list.shift();
    list.push({ url: u, type, size, headers }); await mediaSet(d.tabId, list);
    chrome.action.setBadgeText({ tabId: d.tabId, text: String(list.length) });
    chrome.action.setBadgeBackgroundColor({ color: '#5B3DF5' });
  }).catch(() => {});
}
chrome.tabs.onUpdated.addListener((id, info) => {
  // only a real page change clears the list; SPA/hash changes on the same page keep it
  if (info.status === 'loading' && info.url) { mediaDel(id); chrome.action.setBadgeText({ tabId: id, text: '' }); }
});
chrome.tabs.onRemoved.addListener(mediaDel);

async function payload(tab, pageUrl, title) {
  const media = await mediaGet(tab.id);
  return { pageUrl: pageUrl || tab.url, title: title || tab.title, media, referrer: tab.url, ua: navigator.userAgent,
           cookieJar: await cookieJar([pageUrl, tab.url, ...media.map(m => m.url)]) };
}
const sendVideo = async (tab, pageUrl, title) => app('/video', await payload(tab, pageUrl, title));

// ---- messages from content script / popup
chrome.runtime.onMessage.addListener((msg, sender, reply) => {
  (async () => {
    const tab = sender.tab || (await chrome.tabs.query({ active: true, currentWindow: true }))[0];
    if (STORE_NO_YT && ['video', 'prefetch', 'formats', 'grab'].includes(msg.type) && (isYouTube(tab.url) || isYouTube(msg.pageUrl || ''))) return reply(ytRefused);
    if (msg.type === 'video') await sendVideo(tab, msg.pageUrl, msg.title);
    else if (msg.type === 'prefetch') await app('/formats', { ...(await payload(tab, msg.pageUrl)), prefetch: true });
    else if (msg.type === 'formats') return reply(await app('/formats', await payload(tab, msg.pageUrl), 240000));
    else if (msg.type === 'grab') await app('/grab', { pageUrl: msg.pageUrl || tab.url, id: msg.id });
    else if (msg.type === 'media') reply({ media: await mediaGet(msg.tabId) });
    else if (msg.type === 'ping') reply({ ok: await app('/ping').then(() => true, () => false) });
    else if (msg.type === 'mediaOne') await app('/video', { pageUrl: msg.url, title: tab.title, media: [{ url: msg.url, type: msg.mtype }],
                                                           referrer: tab.url, cookieJar: await cookieJar([msg.url, tab.url]), ua: navigator.userAgent });
    if (!['media', 'ping'].includes(msg.type)) reply({ ok: true });
  })().catch(e => reply({ error: String(e) }));
  return true;
});

// ---- context menus
chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({ id: 'ng-link', title: 'Download with NexGet', contexts: ['link', 'video', 'audio', 'image'] });
  chrome.contextMenus.create({ id: 'ng-all', title: 'Download all links with NexGet', contexts: ['page', 'selection'] });
  chrome.contextMenus.create({ id: 'ng-video', title: 'Download this video with NexGet', contexts: ['page', 'video'] });
});
chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  try {
    if (info.menuItemId === 'ng-link') {
      const url = info.linkUrl || info.srcUrl;
      await app('/download', { url, referrer: tab.url, filename: '', cookies: await cookies(url), ua: navigator.userAgent });
    } else if (info.menuItemId === 'ng-all') {
      const links = await chrome.tabs.sendMessage(tab.id, { type: 'getLinks', selection: !!info.selectionText });
      await app('/links', { links, referrer: tab.url, cookies: await cookies(tab.url), ua: navigator.userAgent });
    } else if (info.menuItemId === 'ng-video') {
      if (STORE_NO_YT && isYouTube(tab.url)) return;
      await sendVideo(tab, info.frameUrl || tab.url);
    }
  } catch (e) { console.warn('NexGet:', e); }
});
