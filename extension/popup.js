const $ = id => document.getElementById(id);
chrome.storage.local.get('capture', ({ capture = true }) => { $('cap').checked = capture; });
$('cap').onchange = () => chrome.storage.local.set({ capture: $('cap').checked });
chrome.runtime.sendMessage({ type: 'ping' }, r => {
  $('st').textContent = r && r.ok ? '● connected' : '● NexGet not running';
  $('st').style.color = r && r.ok ? '#1e8e3e' : '#c0392b';
});
chrome.tabs.query({ active: true, currentWindow: true }, ([tab]) => {
  $('vid').onclick = () => chrome.runtime.sendMessage({ type: 'video', pageUrl: tab.url, title: tab.title }, () => window.close());
  chrome.runtime.sendMessage({ type: 'media', tabId: tab.id }, ({ media = [] } = {}) => {
    for (const m of media) {
      const row = document.createElement('div'); row.className = 'row';
      const s = document.createElement('span'); s.className = 'm'; s.title = m.url;
      s.textContent = m.type.toUpperCase() + ' · ' + decodeURIComponent(m.url.split('?')[0].split('/').pop() || m.url);
      const b = document.createElement('button'); b.textContent = 'Download';
      b.onclick = () => chrome.runtime.sendMessage({ type: 'mediaOne', url: m.url, mtype: m.type }, () => window.close());
      row.append(s, b); $('list').append(row);
    }
  });
});
