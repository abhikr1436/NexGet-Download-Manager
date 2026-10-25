// Download engine: aria2 (segmented HTTP/FTP, up to 16 connections per file) and yt-dlp (video).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace NexGet
{
    sealed class Aria2Exception : Exception { public Aria2Exception(string m) : base(m) { } }

    sealed class Aria2
    {
        Process proc;
        int port;
        readonly string secret = Guid.NewGuid().ToString("N");

        public bool Alive { get { try { return proc != null && !proc.HasExited; } catch (InvalidOperationException) { return false; } } }

        public void Start()
        {
            var exe = App.Tool("aria2c");
            if (exe == null) throw new FileNotFoundException("aria2c was not found in the tools folder. Reinstall NexGet.");
            var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
            proc = Proc.Start(exe, new[] {
                "--enable-rpc", "--rpc-listen-all=false", "--rpc-listen-port=" + port, "--rpc-secret=" + secret,
                "--stop-with-process=" + Process.GetCurrentProcess().Id, "--max-concurrent-downloads=64",
                "--continue=true", "--file-allocation=none", "--auto-file-renaming=false", "--allow-overwrite=false",
                "--max-tries=5", "--retry-wait=3", "--connect-timeout=20", "--timeout=30", "--summary-interval=0",
                "--quiet=true", "--disk-cache=32M", "--piece-length=1M", "--http-accept-gzip=false" }, false);
            for (int i = 0; i < 60; i++)
            {
                try { Call("getVersion"); return; } catch (WebException) { Thread.Sleep(50); }
            }
            throw new IOException("aria2 did not start");
        }

        public object Call(string method, params object[] args)
        {
            var p = new List<object> { "token:" + secret }; p.AddRange(args);
            return Raw(new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", "1" }, { "method", "aria2." + method }, { "params", p } });
        }

        /// <summary>Several calls in one round trip; each entry is either a result or an Aria2Exception.</summary>
        public List<object> Multi(IEnumerable<KeyValuePair<string, object[]>> calls)
        {
            var list = calls.Select(c => (object)new Dictionary<string, object> {
                { "methodName", "aria2." + c.Key }, { "params", new List<object> { "token:" + secret }.Concat(c.Value).ToList() } }).ToList();
            if (list.Count == 0) return new List<object>();
            var res = Raw(new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", "m" }, { "method", "system.multicall" }, { "params", new List<object> { list } } });
            return ((object[])res).Select(r => r is object[] ? ((object[])r)[0] : (object)new Aria2Exception(((Dictionary<string, object>)r).Str("message"))).ToList();
        }

        object Raw(Dictionary<string, object> req)
        {
            var http = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/jsonrpc");
            http.Method = "POST"; http.ContentType = "application/json"; http.Timeout = 4000; http.Proxy = null;
            var body = Encoding.UTF8.GetBytes(Json.Write(req));
            using (var s = http.GetRequestStream()) s.Write(body, 0, body.Length);
            try
            {
                using (var r = (HttpWebResponse)http.GetResponse())
                using (var sr = new StreamReader(r.GetResponseStream())) return Json.Obj(sr.ReadToEnd())["result"];
            }
            catch (WebException e) when (e.Response != null)
            {
                using (var sr = new StreamReader(e.Response.GetResponseStream()))
                    throw new Aria2Exception(Json.Obj(sr.ReadToEnd()).Sub("error").Str("message", e.Message));
            }
        }

        public void Stop() { try { if (Alive) proc.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
    }

    /// <summary>Process helpers: Windows-correct argument quoting, hidden windows, tree kill.</summary>
    static class Proc
    {
        public static string Quote(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            var sb = new StringBuilder("\"");
            for (int i = 0; i < a.Length; i++)
            {
                int bs = 0;
                while (i < a.Length && a[i] == '\\') { bs++; i++; }
                if (i == a.Length) { sb.Append('\\', bs * 2); break; }
                if (a[i] == '"') sb.Append('\\', bs * 2 + 1).Append('"');
                else sb.Append('\\', bs).Append(a[i]);
            }
            return sb.Append('"').ToString();
        }

        public static ProcessStartInfo Info(string exe, IEnumerable<string> args, bool redirect)
        {
            var si = new ProcessStartInfo(exe, string.Join(" ", args.Select(Quote))) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = redirect, RedirectStandardError = redirect };
            if (redirect) { si.StandardOutputEncoding = Encoding.UTF8; si.StandardErrorEncoding = Encoding.UTF8; }
            return si;
        }

        public static Process Start(string exe, IEnumerable<string> args, bool redirect) { return Process.Start(Info(exe, args, redirect)); }

        /// <summary>Run to completion, capturing output (reads both pipes concurrently to avoid deadlock).</summary>
        public static int Run(string exe, IEnumerable<string> args, int timeoutMs, out string stdout, out string stderr)
        {
            using (var p = new Process { StartInfo = Info(exe, args, true) })
            {
                var o = new StringBuilder(); var e = new StringBuilder();
                p.OutputDataReceived += (s, a) => { if (a.Data != null) lock (o) o.AppendLine(a.Data); };
                p.ErrorDataReceived += (s, a) => { if (a.Data != null) lock (e) e.AppendLine(a.Data); };
                p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs)) { KillTree(p); stdout = ""; stderr = "timed out"; return -1; }
                p.WaitForExit();
                lock (o) stdout = o.ToString();
                lock (e) stderr = e.ToString();
                return p.ExitCode;
            }
        }

        public static void KillTree(Process p)
        {
            try
            {
                if (p.HasExited) return;
                if (App.IsWindows)
                    using (var k = Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + p.Id) { CreateNoWindow = true, UseShellExecute = false })) k.WaitForExit(3000);
                if (!p.HasExited) p.Kill();
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }

    sealed class Manager
    {
        public readonly List<Item> Items = new List<Item>();
        public readonly Config Cfg;
        public event Action Changed = delegate { };
        public event Action<Item> Completed = delegate { };
        public event Action<Item> Failed = delegate { };
        public event Action QueueFinished = delegate { };
        public bool QueueRunning { get; private set; }

        readonly Aria2 aria2 = new Aria2();
        readonly Dictionary<string, Process> procs = new Dictionary<string, Process>();
        readonly SynchronizationContext ui;
        readonly string listPath = Path.Combine(App.Data, "downloads.json");
        double lastSave;
        bool dirty;

        public Manager(Config cfg)
        {
            Cfg = cfg;
            ui = SynchronizationContext.Current;
            try
            {
                foreach (var o in (object[])Json.Any(File.ReadAllText(listPath)))
                    if (o is Dictionary<string, object>) Items.Add(Item.FromDict((Dictionary<string, object>)o));
            }
            catch (IOException) { }
            catch (ArgumentException) { }
            catch (InvalidCastException) { }
            catch (InvalidOperationException) { }
            aria2.Start();
            ApplyGlobalLimit();
        }

        void OnUi(Action a) { if (ui != null) ui.Post(_ => a(), null); else lock (Items) a(); }

        public Item Get(string id) { return Items.FirstOrDefault(i => i.Id == id); }

        public void Save(bool force = true)
        {
            if (!force && Fmt.Now() - lastSave < 5) return;
            lastSave = Fmt.Now();
            try { Config.AtomicWrite(listPath, Json.Write(Items.Select(i => i.ToDict()).ToList())); }
            catch (IOException e) { Log.Write("save failed: " + e.Message); }
            catch (UnauthorizedAccessException e) { Log.Write("save failed: " + e.Message); }
        }

        public Item Add(string url, string path, string kind = "http", Dictionary<string, string> headers = null, long size = -1,
                        bool start = true, string description = "", string fmt = null, bool resumable = true, string cookieFile = null)
        {
            var it = new Item { Url = url, Path = path, Kind = kind, Size = size, Description = description ?? "", Format = fmt,
                                Resumable = resumable, CookieFile = cookieFile, Category = Cats.For(path) };
            if (headers != null) foreach (var kv in headers) if (!string.IsNullOrEmpty(kv.Value)) it.Headers[kv.Key] = kv.Value;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            Items.Add(it);
            if (start) Resume(it); else { Save(); Changed(); }
            return it;
        }

        public void Resume(Item it)
        {
            if (it == null || it.Status == State.Active || it.Status == State.Complete) return;
            it.Status = State.Active; it.Error = ""; it.LastTry = Fmt.Now(); it.Speed = 0;
            if (it.Kind == "video") StartVideo(it); else StartHttp(it);
            Save(); Changed();
        }

        public void Pause(Item it, State to = State.Paused)
        {
            if (it == null || (it.Status != State.Active && it.Status != State.Queued)) return;
            Process p;
            if (procs.TryGetValue(it.Id, out p)) { it.Status = to; procs.Remove(it.Id); Proc.KillTree(p); }
            else if (it.Gid != null) { try { aria2.Call("forceRemove", it.Gid); } catch (Exception e) when (e is Aria2Exception || e is WebException) { } }
            it.Status = to; it.Gid = null; it.Speed = 0; it.QueuedStart = false;
            Save(); Changed();
        }

        public void Remove(Item it, bool deleteFile)
        {
            Pause(it);
            Items.Remove(it);
            var files = new List<string>();
            if (deleteFile) files.AddRange(new[] { it.Path, it.Path + ".aria2", it.Path + ".part" });
            if (it.CookieFile != null && !Items.Any(o => o.CookieFile == it.CookieFile)) files.Add(it.CookieFile);
            foreach (var f in files) { try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            Save(); Changed();
        }

        public void Redownload(Item it)
        {
            Pause(it);
            foreach (var f in new[] { it.Path, it.Path + ".aria2", it.Path + ".part" }) try { File.Delete(f); } catch (IOException) { }
            it.Done = 0; it.Status = State.Paused; Resume(it);
        }

        /// <summary>IDM's "refresh download address": a new link for the same file continues where it stopped.</summary>
        public void ChangeUrl(Item it, string url) { Pause(it); it.Url = url; it.Status = State.Paused; Resume(it); }

        public void SetLimit(Item it, int kb)
        {
            it.LimitKb = kb;
            if (it.Gid != null) try { aria2.Call("changeOption", it.Gid, new Dictionary<string, object> { { "max-download-limit", kb > 0 ? kb + "K" : "0" } }); } catch (Aria2Exception) { }
        }

        public void ApplyGlobalLimit()
        {
            try { aria2.Call("changeGlobalOption", new Dictionary<string, object> { { "max-overall-download-limit", Cfg.SpeedLimitKb > 0 ? Cfg.SpeedLimitKb + "K" : "0" } }); }
            catch (Exception e) when (e is Aria2Exception || e is WebException) { }
        }

        public void StartQueue() { QueueRunning = true; Poll(); }

        public void StopQueue()
        {
            QueueRunning = false;
            foreach (var it in Items.Where(i => i.Status == State.Active && i.QueuedStart).ToList()) Pause(it, State.Queued);
            Changed();
        }

        // ---------------------------------------------------------------- HTTP via aria2
        void StartHttp(Item it)
        {
            if (!aria2.Alive) { aria2.Start(); ApplyGlobalLimit(); }
            int n = it.Resumable ? Math.Max(1, Math.Min(16, Cfg.Connections)) : 1;
            var opts = new Dictionary<string, object> {
                { "dir", System.IO.Path.GetDirectoryName(it.Path) }, { "out", it.FileName }, { "split", n.ToString() },
                { "max-connection-per-server", n.ToString() }, { "min-split-size", "1M" },
                { "header", it.Headers.Where(h => h.Value.Length > 0).Select(h => h.Key + ": " + h.Value).ToList() } };
            if (it.LimitKb > 0) opts["max-download-limit"] = it.LimitKb + "K";
            try { it.Gid = (string)aria2.Call("addUri", new List<object> { it.Url }, opts); }
            catch (Exception e) when (e is Aria2Exception || e is WebException) { it.Status = State.Error; it.Error = e.Message; }
        }

        public List<Dictionary<string, object>> Servers(Item it)
        {
            if (it.Gid == null) return new List<Dictionary<string, object>>();
            try
            {
                return ((object[])aria2.Call("getServers", it.Gid)).Cast<Dictionary<string, object>>()
                    .SelectMany(f => f.List("servers").Cast<Dictionary<string, object>>()).ToList();
            }
            catch (Exception e) when (e is Aria2Exception || e is WebException || e is InvalidCastException) { return new List<Dictionary<string, object>>(); }
        }

        // ---------------------------------------------------------------- video via yt-dlp
        public static List<string> YtdlpCommon()
        {
            var a = new List<string> { "--no-playlist", "--no-warnings", "--ignore-config", "--encoding", "utf-8", "--socket-timeout", "20" };
            var qjs = App.Tool("qjs");
            if (qjs != null) a.AddRange(new[] { "--no-js-runtimes", "--js-runtimes", "quickjs:" + qjs });
            var ff = App.Tool("ffmpeg");
            if (ff != null) a.AddRange(new[] { "--ffmpeg-location", System.IO.Path.GetDirectoryName(ff) });
            return a;
        }

        static readonly Regex AriaLine = new Regex(@"\[#\w+ ([\d.]+)(\w+)/([\d.]+)(\w+)\(\d+%\)(?: CN:(\d+))?(?: DL:([\d.]+)(\w+))?");

        void StartVideo(Item it)
        {
            var exe = App.Tool("yt-dlp");
            if (exe == null) { it.Status = State.Error; it.Error = "yt-dlp is missing. Reinstall NexGet."; return; }
            var args = YtdlpCommon();
            args.AddRange(new[] { "--newline", "--progress", "--no-mtime", "-N", "16", "--merge-output-format", "mp4",
                "-f", it.Format ?? "bv*+ba/b", "-o", it.Path.Replace("%", "%%"),
                "--progress-template", "download:NEXGET %(progress.downloaded_bytes)s %(progress.total_bytes)s %(progress.total_bytes_estimate)s %(progress.speed)s",
                "--print", "after_move:NEXGETFILE %(filepath)s" });
            var aria = App.Tool("aria2c");
            if (aria != null) args.AddRange(new[] { "--downloader", aria, "--downloader", "dash,m3u8:native",
                "--downloader-args", "aria2c:-x16 -s16 -k1M --file-allocation=none --summary-interval=1" });
            if (it.CookieFile != null && File.Exists(it.CookieFile)) args.AddRange(new[] { "--cookies", it.CookieFile });
            foreach (var h in it.Headers) if (h.Value.Length > 0 && !h.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)) args.AddRange(new[] { "--add-headers", h.Key + ":" + h.Value });
            int lim = it.LimitKb > 0 ? it.LimitKb : Cfg.SpeedLimitKb;
            if (lim > 0) args.AddRange(new[] { "-r", lim + "K" });
            args.Add("--"); args.Add(it.Url);

            var p = new Process { StartInfo = Proc.Info(exe, args, true), EnableRaisingEvents = true };
            var err = new StringBuilder();
            it.VideoBase = 0; it.VideoLast = 0;
            DataReceivedEventHandler onLine = (s, e) => { if (e.Data != null) OnUi(() => VideoLine(it, e.Data)); };
            p.OutputDataReceived += onLine;
            p.ErrorDataReceived += (s, e) => { if (e.Data == null) return; lock (err) { err.AppendLine(e.Data); if (err.Length > 8000) err.Remove(0, 2000); } OnUi(() => VideoLine(it, e.Data)); };
            p.Exited += (s, e) => { p.WaitForExit(); OnUi(() => VideoDone(it, p, err.ToString())); };
            procs[it.Id] = p;
            p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
        }

        static long Unit(string u) { return u == "KiB" ? 1024 : u == "MiB" ? 1 << 20 : u == "GiB" ? 1 << 30 : 1; }
        static double? Num(string s) { double v; return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : (double?)null; }

        void VideoLine(Item it, string line)
        {
            var m = AriaLine.Match(line);
            if (m.Success)   // aria2 (used by yt-dlp for plain files) prints its own progress
            {
                it.Done = it.VideoBase + (long)(Num(m.Groups[1].Value).GetValueOrDefault() * Unit(m.Groups[2].Value));
                it.Size = it.VideoBase + (long)(Num(m.Groups[3].Value).GetValueOrDefault() * Unit(m.Groups[4].Value));
                if (m.Groups[5].Success) it.Connections = int.Parse(m.Groups[5].Value);
                if (m.Groups[6].Success) it.Speed = (long)(Num(m.Groups[6].Value).GetValueOrDefault() * Unit(m.Groups[7].Value));
                dirty = true; return;
            }
            if (line.StartsWith("NEXGETFILE ")) { it.Path = line.Substring(11).Trim(); return; }
            if (!line.StartsWith("NEXGET ")) return;
            var f = line.Split(' ');
            if (f.Length < 5) return;
            var done = Num(f[1]); var total = Num(f[2]) ?? Num(f[3]); var speed = Num(f[4]);
            if (done.HasValue && done.Value < it.VideoLast) it.VideoBase += it.VideoLast;   // next stream (audio) started
            it.VideoLast = (long)done.GetValueOrDefault();
            it.Done = it.VideoBase + it.VideoLast;
            if (total.HasValue) it.Size = it.VideoBase + (long)total.Value;
            it.Speed = (long)speed.GetValueOrDefault();
            dirty = true;
        }

        void VideoDone(Item it, Process p, string err)
        {
            Process cur;
            if (!procs.TryGetValue(it.Id, out cur) || cur != p) { p.Dispose(); return; }   // paused/removed by user
            procs.Remove(it.Id);
            int code = p.ExitCode; p.Dispose();
            it.Speed = 0;
            if (code == 0)
            {
                it.Status = State.Complete;
                try { it.Size = it.Done = new FileInfo(it.Path).Length; } catch (IOException) { it.Done = Math.Max(it.Done, it.Size); }
                Completed(it);
            }
            else
            {
                var lines = err.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("ERROR")).ToList();
                it.Status = State.Error;
                it.Error = lines.Count > 0 ? lines.Last() : "yt-dlp exited with code " + code;
                Log.Write("video download failed: " + it.Url + " fmt=" + it.Format + "\n" + (err.Length > 4000 ? err.Substring(err.Length - 4000) : err));
                Failed(it);
            }
            Save(); Changed();
        }

        // ---------------------------------------------------------------- polling (UI timer, ~2x/s)
        public void Poll()
        {
            bool changed = dirty; dirty = false;
            if (!aria2.Alive)
            {
                try { aria2.Start(); ApplyGlobalLimit(); } catch (Exception e) when (e is IOException || e is FileNotFoundException) { return; }
                foreach (var it in Items.Where(i => i.Kind == "http" && i.Status == State.Active)) StartHttp(it);
            }
            var live = Items.Where(i => i.Kind == "http" && i.Status == State.Active && i.Gid != null).ToList();
            if (live.Count > 0)
            {
                var keys = new object[] { new List<object> { "status", "totalLength", "completedLength", "downloadSpeed", "connections", "bitfield", "numPieces", "errorMessage" } };
                List<object> res;
                try { res = aria2.Multi(live.Select(i => new KeyValuePair<string, object[]>("tellStatus", new object[] { i.Gid, keys[0] }))); }
                catch (WebException) { res = null; }
                for (int k = 0; res != null && k < live.Count; k++)
                {
                    var it = live[k];
                    if (res[k] is Aria2Exception) { StartHttp(it); continue; }   // aria2 forgot it (restart): resumes from .aria2 file
                    var st = (Dictionary<string, object>)res[k];
                    long total = (long)st.Num("totalLength");
                    if (total > 0) it.Size = total;
                    it.Done = (long)st.Num("completedLength"); it.Speed = (long)st.Num("downloadSpeed");
                    it.Connections = (int)st.Num("connections"); it.Bitfield = st.Str("bitfield"); it.NumPieces = (int)st.Num("numPieces");
                    changed = true;
                    var s = st.Str("status");
                    if (s == "complete")
                    {
                        it.Status = State.Complete; it.Speed = 0; it.Gid = null; it.Done = it.Size;
                        Save(); Completed(it);
                    }
                    else if (s == "error")
                    {
                        it.Status = State.Error; it.Speed = 0; it.Gid = null; it.Error = st.Str("errorMessage", "Download failed");
                        Save(); Failed(it);
                    }
                }
            }
            if (QueueRunning)
            {
                int active = Items.Count(i => i.Status == State.Active);
                foreach (var it in Items.Where(i => i.Status == State.Queued).Take(Math.Max(0, Cfg.MaxActive - active)).ToList())
                { it.QueuedStart = true; Resume(it); }
                if (!Items.Any(i => i.Status == State.Queued || (i.QueuedStart && i.Status == State.Active)))
                {
                    QueueRunning = false; changed = true;
                    foreach (var i in Items) i.QueuedStart = false;
                    QueueFinished();
                }
            }
            if (changed) { Save(false); Changed(); }
        }

        public void Shutdown()
        {
            foreach (var id in procs.Keys.ToList()) Pause(Get(id));
            foreach (var it in Items.Where(i => i.Status == State.Active)) it.Status = State.Paused;
            Save();
            aria2.Stop();
        }

        // ---------------------------------------------------------------- probe a URL before showing "Download file info"
        public sealed class ProbeResult { public string Url, FileName, Mime = ""; public long Size = -1; public bool Resumable; }

        public static ProbeResult Probe(string url, Dictionary<string, string> headers)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.AddRange(0L, 0L); req.Timeout = 15000; req.AllowAutoRedirect = true;
            req.AutomaticDecompression = DecompressionMethods.None;
            foreach (var h in headers ?? new Dictionary<string, string>())
            {
                if (h.Value.Length == 0) continue;
                if (h.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase)) req.Referer = h.Value;
                else if (h.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) req.UserAgent = h.Value;
                else try { req.Headers[h.Key] = h.Value; } catch (ArgumentException) { }
            }
            using (var r = (HttpWebResponse)req.GetResponse())
            {
                var res = new ProbeResult { Url = r.ResponseUri.ToString(), Resumable = r.StatusCode == HttpStatusCode.PartialContent, Mime = r.ContentType ?? "" };
                var cr = r.Headers["Content-Range"] ?? "";
                long n;
                if (res.Resumable && cr.Contains("/") && long.TryParse(cr.Substring(cr.LastIndexOf('/') + 1), out n)) res.Size = n;
                else if (!res.Resumable && r.ContentLength > 0) res.Size = r.ContentLength;
                res.FileName = DispositionName(r.Headers["Content-Disposition"]) ?? Fmt.NameFromUrl(res.Url);
                return res;
            }
        }

        static string DispositionName(string cd)
        {
            if (string.IsNullOrEmpty(cd)) return null;
            var m = Regex.Match(cd, @"filename\*\s*=\s*([\w-]+)''([^;]+)", RegexOptions.IgnoreCase);
            if (m.Success) try { return Fmt.SafeName(Uri.UnescapeDataString(m.Groups[2].Value.Trim().Trim('"'))); } catch (UriFormatException) { }
            m = Regex.Match(cd, @"filename\s*=\s*""?([^"";]+)""?", RegexOptions.IgnoreCase);
            return m.Success ? Fmt.SafeName(m.Groups[1].Value.Trim()) : null;
        }
    }
}
