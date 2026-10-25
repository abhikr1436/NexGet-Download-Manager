# Publishing the NexGet browser add-on

Browsers only install add-ons automatically when they come from their official store. Publish once per store, then put
the IDs into `store.json` (next to NexGet.exe, or tell me and I'll rebuild the installer with them). From then on NexGet
adds the add-on to every installed browser by itself; the browser shows a one-click "Enable" prompt.

| Store | Covers | Cost | Upload this |
|---|---|---|---|
| Chrome Web Store | Chrome, Brave, Opera, Vivaldi | US$5 one-time developer fee | `NexGet-addon-chrome-edge-opera.zip` |
| Microsoft Edge Add-ons | Edge | Free | `NexGet-addon-chrome-edge-opera.zip` |
| Firefox Add-ons (AMO) | Firefox | Free | `NexGet-addon-firefox.zip` |

Review usually takes 1–3 working days. The Chrome package leaves out YouTube because Chrome Web Store policy does not
allow YouTube downloaders (the same reason IDM's Chrome add-on doesn't work on YouTube). YouTube still works in NexGet
via **Add URL** or by copying the link.

## 1. Chrome Web Store
1. Go to https://chrome.google.com/webstore/devconsole, sign in, pay the one-time US$5 fee.
2. **New item** → upload `NexGet-addon-chrome-edge-opera.zip`.
3. Store listing: paste the texts below; category **Productivity**; upload `icon128.png` and 1–5 screenshots (1280×800).
4. **Privacy practices**: single purpose and permission justifications from below; "Does not collect user data"; privacy
   policy URL (host the text below anywhere public, e.g. a GitHub page — I can publish it for you).
5. Submit. After approval, copy the 32-letter item ID from the dashboard URL → `"chrome_id"`.

## 2. Microsoft Edge Add-ons
1. https://partner.microsoft.com/dashboard/microsoftedge → register (free) → **Create new extension**.
2. Upload the same zip, reuse the listing texts, submit. After approval copy the extension ID → `"edge_id"`.

## 3. Firefox Add-ons
1. https://addons.mozilla.org/developers/ → **Submit a New Add-on** → "On this site" (listed).
2. Upload `NexGet-addon-firefox.zip`. It passes Mozilla's validator with 0 errors.
3. When asked for source code: not required (no minified or generated code).
4. After approval copy the add-on page URL → `"firefox_url"`.

## store.json
```json
{ "chrome_id": "abcdefghijklmnopabcdefghijklmnop", "edge_id": "…", "firefox_url": "https://addons.mozilla.org/firefox/addon/nexget/", "opera_url": "" }
```

---

## Listing texts

**Name:** NexGet Download Manager Integration

**Short description (132 chars max):**
Sends your downloads and videos to NexGet Download Manager for faster multi-part downloads, resume, and a video download button.

**Detailed description:**
NexGet Integration connects your browser to NexGet Download Manager, a free Windows download manager.

- Downloads of the file types you choose are handed to NexGet, which splits each file into up to 16 parts for speed and can pause and resume, even after a restart.
- A "Download this video" button appears on videos; pick a quality and NexGet saves it.
- Right-click any link to download it with NexGet, or grab all links on a page.

Requires NexGet Download Manager on Windows. If NexGet isn't running, your browser downloads as usual.
The add-on talks only to NexGet on your own computer (127.0.0.1). It sends nothing to any other server.

**Single purpose:** Hand downloads and videos the user chooses from the browser to the NexGet desktop download manager on the same computer.

**Permission justifications:**
- `downloads`: take over browser downloads of the file types the user selected in NexGet, and let the browser continue when NexGet isn't running.
- `webRequest`: detect video streams a page plays (HLS/DASH/MP4) and the request headers the site's player uses, so NexGet can download the same stream.
- `cookies`: pass the user's existing session cookies for that site to NexGet, so downloads from sites the user is signed in to work.
- `contextMenus`: "Download with NexGet" and "Download all links with NexGet" right-click items.
- `storage`: remember the on/off switch and the streams detected in the current tab.
- `tabs`: know which tab a detected video belongs to and its title.
- `alarms`: a once-a-minute check that NexGet is running (shown as "connected" in NexGet).
- Host permission `<all_urls>`: downloads and videos can come from any website the user visits.

**Remote code:** No. All code is in the package.

## Privacy policy (host this text publicly)

> **NexGet Download Manager Integration — Privacy policy**
>
> The add-on does not collect, store, sell or transmit personal data to the developer or any third party.
> When you start a download or a video download, the add-on sends the link, the page address and title, the request
> headers the page used for that media, and the cookies for those sites to the NexGet application running on your own
> computer at 127.0.0.1 (localhost). This data never leaves your computer through the add-on; NexGet uses it only to
> download the file you chose. The add-on stores only an on/off setting and a short list of media links detected in your
> open tabs, in your browser's local storage, cleared when the tab closes. Contact: <your support email>
