#!/bin/sh
# Build NexGet on Linux: C# app (Mono mcs against .NET Framework 4.7.2), Windows resources, installer (NSIS).
# Needs: mono-devel, mingw-w64 (windres), nsis. Put the Windows tools in dist/tools first (./get-tools.sh, and
# ffmpeg.exe + ffprobe.exe from tools-ffmpeg-build.sh, an LGPL remux-only build).
set -e
cd "$(dirname "$0")"
x86_64-w64-mingw32-windres res/app.rc -O res -o res/app.res
mcs -sdk:4.7.2 -langversion:Experimental -target:winexe -optimize+ -win32res:res/app.res \
    -r:System.Web.Extensions.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll \
    -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -out:dist/NexGet.exe src/*.cs
cp res/NexGet.exe.config dist/
mkdir -p dist/licenses && cp licenses/* dist/licenses/
[ -f dist/store.json ] || echo '{"chrome_id":"","edge_id":"","firefox_url":"","opera_url":""}' > dist/store.json
rm -rf dist/extension && cp -r extension dist/extension
( cd installer && mkdir -p out && makensis -V2 installer.nsi )
# browser add-on store packages (Chromium build excludes YouTube per Chrome Web Store policy)
rm -rf store/chromium store/firefox && cp -r extension store/chromium && cp -r extension store/firefox
python3 - <<'PY'
import json
m = json.load(open('store/chromium/manifest.json'))
m['content_scripts'][0]['exclude_matches'] = ["*://*.youtube.com/*", "*://youtube.com/*", "*://*.youtube-nocookie.com/*", "*://youtu.be/*"]
m['background'] = {'service_worker': 'background.js'}; m.pop('browser_specific_settings', None)
json.dump(m, open('store/chromium/manifest.json', 'w'), indent=2)
m = json.load(open('store/firefox/manifest.json')); m['background'] = {'scripts': ['background.js']}
json.dump(m, open('store/firefox/manifest.json', 'w'), indent=2)
PY
( cd store/chromium && zip -qr ../NexGet-addon-chrome-edge-opera.zip . ) && ( cd store/firefox && zip -qr ../NexGet-addon-firefox.zip . )
ls -la installer/out
