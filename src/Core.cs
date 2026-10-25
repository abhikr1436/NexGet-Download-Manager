// NexGet core: paths, config, download item model, JSON, formatting, logging.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace NexGet
{
    static class App
    {
        public const string Version = "2.0.0";
        public static readonly string Dir = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string Data = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexGet");
        public static readonly bool IsWindows = Path.DirectorySeparatorChar == '\\';

        static App() { Directory.CreateDirectory(Data); }

        /// <summary>Bundled tool: tools\name.exe, tools\name\name.exe (yt-dlp), or PATH (dev on Linux).</summary>
        public static string Tool(string name)
        {
            string exe = IsWindows ? name + ".exe" : name;
            foreach (var p in new[] { Path.Combine(Dir, "tools", exe), Path.Combine(Dir, "tools", name, exe) })
                if (File.Exists(p)) return p;
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try { var p = Path.Combine(d, exe); if (File.Exists(p)) return p; } catch (ArgumentException) { }
            }
            return null;
        }
    }

    static class Log
    {
        public static readonly string File_ = Path.Combine(App.Data, "logs", "nexget.log");
        static readonly object Lock = new object();

        public static void Write(string msg)
        {
            lock (Lock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(File_));
                    var fi = new FileInfo(File_);
                    if (fi.Exists && fi.Length > 1000000) File.Copy(File_, File_ + ".old", true);
                    if (fi.Exists && fi.Length > 1000000) File.Delete(File_);
                    File.AppendAllText(File_, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + msg.TrimEnd() + Environment.NewLine);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    static class Json
    {
        static readonly JavaScriptSerializer S = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        public static string Write(object o) { return S.Serialize(o); }
        public static Dictionary<string, object> Obj(string s) { return S.DeserializeObject(s) as Dictionary<string, object>; }
        public static object Any(string s) { return S.DeserializeObject(s); }

        // tolerant getters for loosely-typed JSON (python-written files, extension payloads)
        public static string Str(this IDictionary<string, object> d, string k, string def = "")
        {
            object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : def;
        }
        public static double Num(this IDictionary<string, object> d, string k, double def = 0)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch (FormatException) { return def; } catch (InvalidCastException) { return def; }
        }
        public static bool Bool(this IDictionary<string, object> d, string k, bool def = false)
        {
            object v; return d != null && d.TryGetValue(k, out v) && v is bool ? (bool)v : def;
        }
        public static Dictionary<string, object> Sub(this IDictionary<string, object> d, string k)
        {
            object v; return d != null && d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null;
        }
        public static IEnumerable<object> List(this IDictionary<string, object> d, string k)
        {
            object v; return d != null && d.TryGetValue(k, out v) && v is IEnumerable && !(v is string) ? ((IEnumerable)v).Cast<object>() : Enumerable.Empty<object>();
        }
        public static Dictionary<string, string> StrMap(this IDictionary<string, object> d, string k)
        {
            var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var s = d.Sub(k);
            if (s != null) foreach (var kv in s) if (kv.Value is string && kv.Value.ToString().Length > 0) r[kv.Key] = (string)kv.Value;
            return r;
        }
    }

    static class Fmt
    {
        public static string Size(long n)
        {
            if (n < 0) return "";
            if (n < 1024) return n + " B";
            string[] u = { "KB", "MB", "GB", "TB" }; double v = n; int i = -1;
            do { v /= 1024; i++; } while (v >= 1024 && i < u.Length - 1);
            return v.ToString(v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + " " + u[i];
        }
        public static string Speed(long bps) { return bps > 0 ? Size(bps) + "/s" : ""; }
        public static string Time(double sec)
        {
            if (sec < 0 || double.IsNaN(sec) || double.IsInfinity(sec)) return "";
            var t = TimeSpan.FromSeconds(Math.Ceiling(sec));
            if (t.TotalDays >= 1) return string.Format("{0}d {1}h", (int)t.TotalDays, t.Hours);
            if (t.TotalHours >= 1) return string.Format("{0}h {1}m", (int)t.TotalHours, t.Minutes);
            if (t.TotalMinutes >= 1) return string.Format("{0}m {1:00}s", (int)t.TotalMinutes, t.Seconds);
            return t.Seconds + "s";
        }
        public static double Now() { return (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds; }
        public static DateTime Local(double unix) { return new DateTime(1970, 1, 1).AddSeconds(unix).ToLocalTime(); }

        public static string SafeName(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in name ?? "") sb.Append(c < 32 || "\\/:*?\"<>|".IndexOf(c) >= 0 ? '_' : c);
            var s = sb.ToString().Trim(' ', '.');
            if (s.Length > 180) s = s.Substring(0, 180);
            return s.Length == 0 ? "download" : s;
        }
        public static string NameFromUrl(string url)
        {
            try { return SafeName(Uri.UnescapeDataString(Path.GetFileName(new Uri(url).AbsolutePath))); }
            catch (UriFormatException) { return "download"; }
        }
        public static string UniquePath(string folder, string name)
        {
            string b = Path.GetFileNameWithoutExtension(name), e = Path.GetExtension(name);
            string p = Path.Combine(folder, name);
            for (int n = 2; File.Exists(p) || File.Exists(p + ".aria2") || File.Exists(p + ".part"); n++)
                p = Path.Combine(folder, b + " (" + n + ")" + e);
            return p;
        }
    }

    static class Cats
    {
        public static readonly string[] All = { "Compressed", "Documents", "Music", "Programs", "Video", "General" };
        static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static Cats()
        {
            Add("Compressed", "zip rar 7z gz bz2 xz tar tgz zst arj ace cab lzh z r00 r01");
            Add("Documents", "pdf doc docx xls xlsx ppt pptx pps txt rtf odt ods odp epub csv md");
            Add("Music", "mp3 wav wma aac m4a flac ogg opus aif aiff mpa ra");
            Add("Programs", "exe msi msu msix appx apk dmg iso img bin deb rpm");
            Add("Video", "mp4 mkv avi mov wmv flv webm m4v mpg mpeg mpe 3gp ts ogv rm rmvb asf qt");
        }
        static void Add(string c, string exts) { foreach (var e in exts.Split(' ')) Map[e] = c; }
        public static string For(string name)
        {
            string c; return Map.TryGetValue(Path.GetExtension(name ?? "").TrimStart('.'), out c) ? c : "General";
        }
    }

    sealed class Config
    {
        public const string DefaultCapture = "3GP 7Z AAC ACE AIF APK ARJ ASF AVI BIN BZ2 DEB DMG EXE FLAC GZ IMG ISO LZH M4A M4V MKV MOV MP3 MP4 MPA MPE MPEG MPG MSI MSIX MSU OGG OGV OPUS PDF PPS PPT PPTX QT R0* R1* RA RAR RM RMVB RPM SEA SIT SITX TAR TGZ TIF TIFF WAV WEBM WMA WMV XZ Z ZIP";
        static readonly string FilePath = Path.Combine(App.Data, "config.json");

        public int Connections = 8, MaxActive = 3, SpeedLimitKb;
        public string BaseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        public Dictionary<string, string> CatDirs = new Dictionary<string, string>();
        public string CaptureExts = DefaultCapture;
        public bool Clipboard = true, ShowProgress = true, ShowComplete = true, ExtSeen;
        public string ScheduleAt = "";           // "HH:mm" -> start queue daily
        public string WhenQueueDone = "nothing"; // nothing | sleep | shutdown
        public double YtdlpChecked;

        public static Config Load()
        {
            var c = new Config();
            try
            {
                var d = Json.Obj(File.ReadAllText(FilePath));
                c.Connections = (int)d.Num("connections", 8); c.MaxActive = (int)d.Num("max_active", 3);
                c.SpeedLimitKb = (int)d.Num("speed_limit_kb", 0); c.BaseDir = d.Str("base_dir", c.BaseDir);
                c.CatDirs = d.StrMap("cat_dirs"); c.CaptureExts = d.Str("capture_exts", DefaultCapture);
                c.Clipboard = d.Bool("clipboard", true); c.ShowProgress = d.Bool("show_progress", true);
                c.ShowComplete = d.Bool("show_complete", true); c.ExtSeen = d.Bool("ext_seen");
                c.ScheduleAt = d.Str("schedule_at"); c.WhenQueueDone = d.Str("when_queue_done", "nothing");
                c.YtdlpChecked = d.Num("ytdlp_checked");
            }
            catch (IOException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            c.Connections = Math.Max(1, Math.Min(16, c.Connections));
            return c;
        }

        public void Save()
        {
            var d = new Dictionary<string, object> {
                {"connections", Connections}, {"max_active", MaxActive}, {"speed_limit_kb", SpeedLimitKb},
                {"base_dir", BaseDir}, {"cat_dirs", CatDirs}, {"capture_exts", CaptureExts}, {"clipboard", Clipboard},
                {"show_progress", ShowProgress}, {"show_complete", ShowComplete}, {"ext_seen", ExtSeen},
                {"schedule_at", ScheduleAt}, {"when_queue_done", WhenQueueDone}, {"ytdlp_checked", YtdlpChecked} };
            AtomicWrite(FilePath, Json.Write(d));
        }

        public string Folder(string cat)
        {
            string d;
            if (CatDirs.TryGetValue(cat, out d) && d.Length > 0) return d;
            return cat == "General" ? BaseDir : Path.Combine(BaseDir, cat);
        }

        public bool Captures(string fileName)
        {
            var ext = Path.GetExtension(fileName ?? "").TrimStart('.').ToUpperInvariant();
            if (ext.Length == 0) return false;
            foreach (var p in CaptureExts.ToUpperInvariant().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (p.EndsWith("*") ? ext.StartsWith(p.TrimEnd('*')) : ext == p) return true;
            return false;
        }

        public static void AtomicWrite(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }
    }

    enum State { Queued, Active, Paused, Complete, Error }

    sealed class Item
    {
        public string Id = Guid.NewGuid().ToString("N"), Url = "", Path = "", Kind = "http", Format, Category = "General";
        public string Description = "", Error = "", CookieFile;
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public long Size = -1, Done, Speed;
        public State Status = State.Queued;
        public double Added = Fmt.Now(), LastTry;
        public bool Resumable = true, QueuedStart;
        public int LimitKb, Connections;
        // runtime only
        public string Gid, Bitfield;
        public int NumPieces;
        public long VideoBase, VideoLast;

        public string FileName { get { return System.IO.Path.GetFileName(Path); } }
        public double Percent { get { return Size > 0 ? Math.Min(100.0, Done * 100.0 / Size) : 0; } }
        public double Eta { get { return Status == State.Active && Speed > 0 && Size > 0 ? (Size - Done) / (double)Speed : -1; } }

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object> {
                {"id", Id}, {"url", Url}, {"path", Path}, {"filename", FileName}, {"kind", Kind}, {"headers", Headers},
                {"size", Size}, {"done", Done}, {"status", Status.ToString().ToLowerInvariant()}, {"fmt", Format},
                {"category", Category}, {"added", Added}, {"last_try", LastTry}, {"description", Description},
                {"error", Error}, {"resumable", Resumable}, {"cookiefile", CookieFile}, {"limit_kb", LimitKb},
                {"queued_start", QueuedStart} };
        }

        public static Item FromDict(Dictionary<string, object> d)
        {
            var it = new Item {
                Id = d.Str("id", Guid.NewGuid().ToString("N")), Url = d.Str("url"), Path = d.Str("path"),
                Kind = d.Str("kind", "http"), Format = d.Str("fmt", null), Category = d.Str("category", "General"),
                Headers = d.StrMap("headers"), Size = (long)d.Num("size", -1), Done = (long)d.Num("done"),
                Added = d.Num("added", Fmt.Now()), LastTry = d.Num("last_try"), Description = d.Str("description"),
                Error = d.Str("error"), Resumable = d.Bool("resumable", true), CookieFile = d.Str("cookiefile", null),
                LimitKb = (int)d.Num("limit_kb") };
            State s;
            it.Status = Enum.TryParse(d.Str("status", "paused"), true, out s) ? s : State.Paused;
            if (it.Status == State.Active) it.Status = State.Paused;   // nothing runs after a restart
            if (it.Format == "") it.Format = null;
            return it;
        }
    }
}
