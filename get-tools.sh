#!/bin/sh
# Download the Windows engines NexGet bundles into dist/tools (checked against upstream checksums where published).
set -e
cd "$(dirname "$0")"; mkdir -p dist/tools && cd dist/tools
YTDLP=2026.10.21; QJS=v0.17.0; ARIA2=1.37.0
curl -fsSLo yt-dlp_win.zip "https://github.com/yt-dlp/yt-dlp/releases/download/$YTDLP/yt-dlp_win.zip"
curl -fsSL "https://github.com/yt-dlp/yt-dlp/releases/download/$YTDLP/SHA2-256SUMS" | grep ' yt-dlp_win.zip$' | sha256sum -c -
rm -rf yt-dlp && mkdir yt-dlp && unzip -q yt-dlp_win.zip -d yt-dlp && rm yt-dlp_win.zip
curl -fsSLo qjs.exe "https://github.com/quickjs-ng/quickjs/releases/download/$QJS/qjs-windows-x86_64.exe"
curl -fsSLo aria2.zip "https://github.com/aria2/aria2/releases/download/release-$ARIA2/aria2-$ARIA2-win-64bit-build1.zip"
unzip -qj aria2.zip '*/aria2c.exe' && rm aria2.zip
echo "Now build ffmpeg.exe/ffprobe.exe with ../../tools-ffmpeg-build.sh and copy them here."
