// Headless self-check for the engine (run with mono on Linux; Windows builds use the same code).
using System; using System.Collections.Generic; using System.IO; using System.Linq; using System.Net; using System.Security.Cryptography; using System.Text; using System.Threading;
using NexGet;
static class EngineTest {
    static void Wait(Func<bool> cond, int sec, Manager m) { var end = DateTime.Now.AddSeconds(sec); while (!cond() && DateTime.Now < end) { m.Poll(); Thread.Sleep(200); } }
    static int Main(string[] a) {
        string url = a[0], sha = a[1], hlsPage = a[2];
        var cfg = new Config { Connections = 8 };
        var m = new Manager(cfg);
        var p = Manager.Probe(url, null);
        Console.WriteLine("probe: " + p.FileName + " " + p.Size + " resumable=" + p.Resumable);
        if (p.FileName != "test file.bin" || !p.Resumable) return 1;
        var dir = Path.Combine(Path.GetTempPath(), "ngtest" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var it = m.Add(url, Path.Combine(dir, p.FileName), size: p.Size);
        Wait(() => it.Done > p.Size / 5, 30, m);
        Console.WriteLine("connections: " + it.Connections + ", at " + it.Percent.ToString("0") + "%");
        m.Pause(it);
        if (!File.Exists(it.Path + ".aria2")) { Console.WriteLine("no resume file"); return 1; }
        m.Resume(it);
        Wait(() => it.Status == State.Complete, 60, m);
        var h = BitConverter.ToString(SHA256.Create().ComputeHash(File.ReadAllBytes(it.Path))).Replace("-", "").ToLowerInvariant();
        Console.WriteLine("status " + it.Status + " hash ok=" + (h == sha));
        if (h != sha) return 1;
        // bridge: websites refused, extensions accepted
        var b = new Bridge(cfg); var got = new List<string>(); b.Request += (k, d) => got.Add(k);
        Func<string, string, int> post = (path, origin) => {
            var r = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:9614" + path); r.Method = "POST"; r.Proxy = null;
            if (origin != null) r.Headers["Origin"] = origin; var body = Encoding.UTF8.GetBytes("{\"url\":\"" + url + "\"}");
            r.ContentType = "application/json"; using (var s = r.GetRequestStream()) s.Write(body, 0, body.Length);
            try { using (var resp = (HttpWebResponse)r.GetResponse()) return (int)resp.StatusCode; } catch (WebException e) { return (int)((HttpWebResponse)e.Response).StatusCode; } };
        Console.WriteLine("evil origin -> " + post("/download", "https://evil.example") + ", extension -> " + post("/download", "chrome-extension://x"));
        if (got.Count != 1) return 1;
        // protected HLS (like the course site): headers captured from the player must be replayed
        var payload = Json.Obj("{\"pageUrl\":\"" + hlsPage + "\",\"title\":\"Lecture 7\",\"referrer\":\"http://127.0.0.1:8765/course.html\",\"ua\":\"Mozilla/5.0\"," +
            "\"media\":[{\"type\":\"hls\",\"url\":\"http://127.0.0.1:8767/drm/abc/master.m3u8?token=xyz\",\"headers\":{\"Referer\":\"http://localhost:8766/\",\"Origin\":\"http://localhost:8766\"}}],\"cookieJar\":[]}");
        var t0 = DateTime.Now; var an = (Analysis)Video.Get(payload).Result;
        Console.WriteLine("analysis " + (DateTime.Now - t0).TotalSeconds.ToString("0.0") + "s: " + string.Join(" | ", an.Choices.Select(c => c.Label)) + (an.Error.Length > 0 ? "  (err: " + an.Error + ")" : ""));
        t0 = DateTime.Now; Video.Get(payload).Wait(); Console.WriteLine("cached: " + (DateTime.Now - t0).TotalMilliseconds.ToString("0") + " ms");
        var best = an.Choices.First(c => c.Label.Contains("720"));
        var v = m.Add(best.Url, Path.Combine(dir, "lecture.mp4"), kind: "video", headers: best.Headers, fmt: best.Fmt, cookieFile: an.CookieFile);
        Wait(() => v.Status != State.Active, 90, m);
        Console.WriteLine("video: " + v.Status + " " + v.Error + " " + (File.Exists(v.Path) ? new FileInfo(v.Path).Length + " bytes" : "missing"));
        m.Shutdown();
        Console.WriteLine(v.Status == State.Complete ? "ALL TESTS PASSED" : "FAILED");
        return v.Status == State.Complete ? 0 : 1;
    }
}
