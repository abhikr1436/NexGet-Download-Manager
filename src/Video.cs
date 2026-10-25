// Video analysis for the in-page "Download this video" menu.
// Sources, best first: the page via a real yt-dlp site extractor (YouTube, Vimeo, 1800+ sites);
// HLS/DASH streams the browser loaded, replayed with the player's own request headers; plain media files.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NexGet
{
    sealed class Choice
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8), Label, Url, Kind = "video", Fmt, Ext = "mp4", FileName;
        public long Size;
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    sealed class Analysis
    {
        public string Title = "video", Error = "", CookieFile;
        public List<Choice> Choices = new List<Choice>();
    }

    static class Video
    {
        const double Ttl = 15 * 60;   // signed stream URLs expire; keep results fresh
        sealed class Entry { public double At; public Task<object> Job; }
        static readonly ConcurrentDictionary<string, Entry> Cache = new ConcurrentDictionary<string, Entry>();
        static readonly ConcurrentDictionary<string, Analysis> ByPage = new ConcurrentDictionary<string, Analysis>();

        /// <summary>Run once per key; concurrent callers share the same task. Failures are cached briefly.</summary>
        static Task<object> Once(string key, Func<object> work)
        {
            Entry e;
            if (Cache.TryGetValue(key, out e) && Fmt.Now() - e.At < (e.Job.IsFaulted ? 20 : Ttl)) return e.Job;
            var fresh = new Entry { At = Fmt.Now(), Job = Task.Run(work) };
            return Cache.AddOrUpdate(key, fresh, (k, old) => Fmt.Now() - old.At < (old.Job.IsFaulted ? 20 : Ttl) ? old : fresh).Job;
        }

        public static Task<object> Get(Dictionary<string, object> p)
        {
            var media = string.Join("|", p.List("media").OfType<Dictionary<string, object>>().Select(m => m.Str("url")).OrderBy(u => u));
            var page = p.Str("pageUrl");
            return Once("analysis|" + page + "|" + media, () => { var a = Analyze(p); ByPage[page] = a; return a; });
        }

        public static Analysis Find(string page, string id, out Choice choice)
        {
            Analysis a; choice = null;
            if (ByPage.TryGetValue(page ?? "", out a)) choice = a.Choices.FirstOrDefault(c => c.Id == id);
            return a;
        }

        static Dictionary<string, object> Extract(string url, Dictionary<string, string> headers, string cookieFile)
        {
            var exe = App.Tool("yt-dlp");
            if (exe == null) throw new InvalidOperationException("yt-dlp is missing. Reinstall NexGet.");
            var args = Manager.YtdlpCommon();
            args.AddRange(new[] { "-J", "--no-progress" });
            foreach (var h in headers) if (h.Value.Length > 0 && !h.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)) args.AddRange(new[] { "--add-headers", h.Key + ":" + h.Value });
            if (cookieFile != null) args.AddRange(new[] { "--cookies", cookieFile });
            args.Add("--"); args.Add(url);
            string o, e;
            int code = Proc.Run(exe, args, 180000, out o, out e);
            if (code != 0)
            {
                var err = e.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("ERROR")).LastOrDefault() ?? e.Trim();
                Log.Write("extract failed: " + url + "\n  " + e.Trim());
                throw new InvalidOperationException(err.Replace("ERROR: ", ""));
            }
            return Json.Obj(o);
        }

        static long SizeOf(Dictionary<string, object> f) { return (long)Math.Max(f.Num("filesize"), f.Num("filesize_approx")); }
        static bool NoVideo(Dictionary<string, object> f) { return f.Str("vcodec") == "none"; }

        /// <summary>Quality options from a yt-dlp info dict. Returns (extractor, title, choices).</summary>
        static Tuple<string, string, List<Choice>> Choices(Dictionary<string, object> info, string url, Dictionary<string, string> headers)
        {
            if (info.Str("_type") == "playlist") info = info.List("entries").OfType<Dictionary<string, object>>().FirstOrDefault() ?? info;
            var fmts = info.List("formats").OfType<Dictionary<string, object>>().ToList();
            if (fmts.Count == 0) fmts.Add(info);
            var playable = fmts.Where(f => !f.Bool("has_drm")).ToList();
            if (playable.Count == 0 || playable.All(NoVideo) && fmts.Any(f => f.Bool("has_drm") && !NoVideo(f)))
                throw new InvalidOperationException("This video is DRM-protected (Widevine/PlayReady). No downloader can save it.");
            var audio = playable.Where(f => NoVideo(f) && f.Str("acodec", "none") != "none").OrderByDescending(f => Math.Max(f.Num("abr"), f.Num("tbr"))).FirstOrDefault();
            var list = new List<Choice>();
            foreach (var g in playable.Where(f => f.Num("height") > 0 && !NoVideo(f)).GroupBy(f => (int)f.Num("height")).OrderByDescending(g => g.Key))
            {
                var best = g.OrderByDescending(f => f.Num("tbr")).First();
                int h = g.Key;
                long total = SizeOf(best) + (audio != null && best.Str("acodec", "none") == "none" ? SizeOf(audio) : 0);
                var fps = best.Num("fps") > 30 ? " " + (int)best.Num("fps") + "fps" : "";
                list.Add(new Choice { Label = "MP4  " + h + "p" + fps + (total > 0 ? "   " + Fmt.Size(total) : ""), Size = total,
                    Fmt = string.Format("bv*[height={0}]+ba/b[height={0}]/bv*[height<={0}]+ba/b[height<={0}]", h), Url = url, Headers = headers });
            }
            if (audio != null)
            {
                var ext = playable.Any(f => f.Str("ext") == "m4a") ? "m4a" : audio.Str("ext", "m4a");
                list.Add(new Choice { Label = "Audio only  (" + ext.ToUpperInvariant() + ")" + (SizeOf(audio) > 0 ? "   " + Fmt.Size(SizeOf(audio)) : ""),
                    Fmt = "ba[ext=m4a]/ba", Ext = ext, Size = SizeOf(audio), Url = url, Headers = headers });
            }
            if (list.Count == 0) list.Add(new Choice { Label = "Best quality", Fmt = "bv*+ba/b", Ext = info.Str("ext", "mp4"), Size = SizeOf(info), Url = url, Headers = headers });
            return Tuple.Create(info.Str("extractor_key"), info.Str("title"), list);
        }

        public static string WriteCookieJar(IEnumerable<object> cookies)
        {
            var sb = new StringBuilder("# Netscape HTTP Cookie File\n");
            int n = 0;
            foreach (var c in cookies.OfType<Dictionary<string, object>>())
            {
                Func<string, string> f = k => c.Str(k).Replace('\t', ' ').Replace('\n', ' ');
                if (f("domain").Length == 0 || f("name").Length == 0) continue;
                sb.Append(string.Join("\t", f("domain"), c.Bool("hostOnly") ? "FALSE" : "TRUE", f("path").Length > 0 ? f("path") : "/",
                    c.Bool("secure") ? "TRUE" : "FALSE", ((long)c.Num("expires")).ToString(), f("name"), f("value"))).Append('\n');
                n++;
            }
            if (n == 0) return null;
            var dir = Path.Combine(App.Data, "cookies"); Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        public static string CookieHeader(IEnumerable<object> cookies, string url)
        {
            string host;
            try { host = new Uri(url).Host; } catch (UriFormatException) { return ""; }
            return string.Join("; ", cookies.OfType<Dictionary<string, object>>().Where(c => {
                var d = c.Str("domain").TrimStart('.');
                return d.Length > 0 && (host == d || host.EndsWith("." + d));
            }).Select(c => c.Str("name") + "=" + c.Str("value")));
        }

        static Analysis Analyze(Dictionary<string, object> p)
        {
            string page = p.Str("pageUrl"), referrer = p.Str("referrer", page), ua = p.Str("ua");
            var media = p.List("media").OfType<Dictionary<string, object>>().Where(m => m.Str("url").StartsWith("http")).ToList();
            var jar = p.List("cookieJar").ToList();
            var res = new Analysis { Title = p.Str("title", "video"), CookieFile = WriteCookieJar(jar) };
            var pageHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Referer", referrer }, { "User-Agent", ua } };

            var jobs = new List<Tuple<string, string, Dictionary<string, string>>>();
            if (page.StartsWith("http")) jobs.Add(Tuple.Create("page", page, pageHeaders));
            foreach (var m in Enumerable.Reverse(media).Where(m => m.Str("type") == "hls" || m.Str("type") == "dash").Take(5))
            {
                var h = m.StrMap("headers");
                if (!h.ContainsKey("User-Agent")) h["User-Agent"] = ua;
                if (!h.ContainsKey("Referer")) h["Referer"] = referrer;
                jobs.Add(Tuple.Create(m.Str("type"), m.Str("url"), h));
            }
            var cookie = res.CookieFile;
            var tasks = jobs.Select(j => Once(j.Item1 + "|" + j.Item2, () => Choices(Extract(j.Item2, j.Item3, cookie), j.Item2, j.Item3))).ToList();
            try { Task.WaitAll(tasks.Cast<Task>().ToArray(), 200000); } catch (AggregateException) { }

            var pageChoices = new List<Choice>(); var streamChoices = new List<Choice>(); var errors = new List<string>(); string pageExtractor = "";
            for (int i = 0; i < jobs.Count; i++)
            {
                var t = tasks[i]; var kind = jobs[i].Item1;
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    var r = (Tuple<string, string, List<Choice>>)t.Result;
                    if (kind == "page") { pageChoices = r.Item3; pageExtractor = r.Item1; if (r.Item2.Length > 0 && r.Item1 != "Generic" && r.Item1 != "HTML5MediaEmbed") res.Title = r.Item2; }
                    else streamChoices.AddRange(r.Item3.Select(c => new Choice { Label = kind.ToUpperInvariant() + "  " + c.Label.Replace("MP4  ", ""),
                        Fmt = c.Fmt, Ext = c.Ext, Size = c.Size, Url = c.Url, Headers = c.Headers }));
                }
                else
                {
                    var msg = t.Exception != null ? t.Exception.GetBaseException().Message : "timed out";
                    if (kind == "page") { if (jobs.Count == 1) errors.Add(msg); }
                    else
                    {
                        errors.Add(msg);   // extraction failed: still offer the raw stream
                        streamChoices.Add(new Choice { Label = kind.ToUpperInvariant() + " stream  " + Fmt.NameFromUrl(jobs[i].Item2), Fmt = "bv*+ba/b",
                            Url = jobs[i].Item2, Headers = jobs[i].Item3 });
                    }
                }
            }
            // the generic extractor guesses at arbitrary pages; trust it only when nothing better was found
            bool genericOnly = pageExtractor == "Generic" || pageExtractor == "HTML5MediaEmbed";
            res.Choices.AddRange(genericOnly && streamChoices.Count > 0 ? streamChoices : pageChoices.Concat(streamChoices));

            foreach (var m in media.Where(m => m.Str("type") == "video" || m.Str("type") == "audio"))
            {
                long size = (long)m.Num("size");
                var name = Fmt.NameFromUrl(m.Str("url"));
                if (size < 100000 && !(size == 0 && (Cats.For(name) == "Video" || Cats.For(name) == "Music"))) continue;   // skip UI sounds
                var h = m.StrMap("headers");
                if (!h.ContainsKey("Referer")) h["Referer"] = referrer;
                if (!h.ContainsKey("User-Agent")) h["User-Agent"] = ua;
                h["Cookie"] = CookieHeader(jar, m.Str("url"));
                res.Choices.Add(new Choice { Label = (m.Str("type") == "audio" ? "Audio file  " : "Video file  ") + name + (size > 0 ? "   " + Fmt.Size(size) : ""),
                    Kind = "http", Url = m.Str("url"), Size = size, Ext = Path.GetExtension(name).TrimStart('.'), FileName = name, Headers = h });
            }
            res.Choices = res.Choices.GroupBy(c => c.Label + "|" + c.Url).Select(g => g.First()).ToList();
            res.Error = string.Join("\n", errors.Distinct().Take(2));
            return res;
        }
    }
}
