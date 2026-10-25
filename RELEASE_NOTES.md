## NexGet 2.0.0

A complete rewrite as a native Windows app: about 18 MB to download (down from 181 MB), and it starts instantly.

**New**
- Native C# app on the .NET Framework built into Windows 10/11
- Quality menu on the page ("Download this video"), ready as soon as the video starts playing
- Works with video players that need their own request headers (many course and streaming sites)
- Video downloads use 16 parallel connections or segments
- Scheduler (daily start time) and sleep or shut down when the queue finishes
- Refresh download address for expired links
- Browser integration for Chrome, Edge, Brave, Opera, Vivaldi and Firefox
- Video support (yt-dlp) updates itself weekly, verified against yt-dlp's published checksums

**Install:** run `NexGet-Setup-2.0.0.exe`. Your download list from 1.x is kept.
The installer isn't code-signed yet; if Windows warns, choose **More info → Run anyway**.

**Files**
- `NexGet-Setup-2.0.0.exe`: Windows installer (64-bit, Windows 10/11)
- `NexGet-addon-chrome-edge-opera.zip` / `NexGet-addon-firefox.zip`: the browser add-on packages for the stores

SHA-256 `NexGet-Setup-2.0.0.exe`: `4de83c884a075ef3f77917e44804f80ed848fc21c23bd79d9ef81ed97871ab95`
