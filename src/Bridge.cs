// Localhost bridge for the browser add-on (127.0.0.1:9614). Raw sockets: HttpListener needs admin URL ACLs.
// Only browser-extension origins may POST, so websites cannot push downloads into NexGet.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace NexGet
{
    sealed class Bridge
    {
        public const int Port = 9614;
        static readonly string[] ExtOrigins = { "chrome-extension://", "moz-extension://", "extension://" };
        public double LastPing;
        readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> pingBy = new System.Collections.Concurrent.ConcurrentDictionary<string, double>();
        public bool SeenFrom(string browser) { double t; return pingBy.TryGetValue(browser, out t) && Fmt.Now() - t < 150; }
        public event Action<string, Dictionary<string, object>> Request = delegate { };
        readonly Config cfg;
        readonly SynchronizationContext ui = SynchronizationContext.Current;
        readonly TcpListener listener = new TcpListener(IPAddress.Loopback, Port);

        public Bridge(Config cfg)
        {
            this.cfg = cfg;
            listener.Start();
            new Thread(Loop) { IsBackground = true, Name = "bridge" }.Start();
        }

        void Loop()
        {
            while (true)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); } catch (SocketException) { return; } catch (ObjectDisposedException) { return; }
                ThreadPool.QueueUserWorkItem(_ => { using (c) try { Handle(c); } catch (IOException) { } catch (SocketException) { } });
            }
        }

        static string ReadLine(Stream s)
        {
            var b = new List<byte>();
            int ch;
            while ((ch = s.ReadByte()) >= 0 && ch != '\n') { if (b.Count > 8192) throw new IOException("header too long"); b.Add((byte)ch); }
            return Encoding.ASCII.GetString(b.ToArray()).TrimEnd('\r');
        }

        void Handle(TcpClient c)
        {
            c.ReceiveTimeout = 10000; c.SendTimeout = 10000;
            var s = c.GetStream();
            var first = ReadLine(s).Split(' ');
            if (first.Length < 2) return;
            string method = first[0], path = first[1];
            var hdr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (string line; (line = ReadLine(s)).Length > 0;)
            {
                int i = line.IndexOf(':');
                if (i > 0) hdr[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }
            string origin; hdr.TryGetValue("Origin", out origin); origin = origin ?? "";
            bool fromExt = ExtOrigins.Any(o => origin.StartsWith(o));

            if (method == "OPTIONS") { Send(s, 204, null, origin, fromExt); return; }
            if (method == "GET" && path.StartsWith("/ping"))
            {
                LastPing = Fmt.Now();
                var b = System.Text.RegularExpressions.Regex.Match(path, @"[?&]b=(\w+)");
                if (b.Success) pingBy[b.Groups[1].Value] = LastPing;
                Send(s, 200, new Dictionary<string, object> { { "app", "NexGet" }, { "version", App.Version },
                    { "exts", cfg.CaptureExts.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries) } }, origin, fromExt);
                return;
            }
            if (method != "POST") { Send(s, 404, null, origin, fromExt); return; }

            string len; int n = 0;
            if (hdr.TryGetValue("Content-Length", out len) && (!int.TryParse(len, out n) || n < 0 || n > 8000000)) { Send(s, 413, null, origin, fromExt); return; }
            var buf = new byte[n];
            for (int got = 0; got < n;) { int r = s.Read(buf, got, n - got); if (r <= 0) return; got += r; }
            Dictionary<string, object> data;
            try { data = n > 0 ? Json.Obj(Encoding.UTF8.GetString(buf)) : new Dictionary<string, object>(); }
            catch (ArgumentException) { data = null; }
            catch (InvalidOperationException) { data = null; }
            if (data == null) { Send(s, 400, null, origin, fromExt); return; }

            var kind = path.Trim('/');
            if (kind == "show" && origin.Length == 0) { Raise("show", data); Send(s, 200, Ok, origin, false); return; }   // 2nd NexGet launch
            if (!fromExt) { Send(s, 403, new Dictionary<string, object> { { "error", "extension origin required" } }, origin, false); return; }

            if (kind == "formats")   // quality list for the in-page menu; analysis runs on this connection's thread
            {
                var job = Video.Get(data);
                if (data.Bool("prefetch")) { Send(s, 200, Ok, origin, true); return; }
                try
                {
                    var a = (Analysis)job.Result;
                    Send(s, 200, new Dictionary<string, object> { { "title", a.Title }, { "error", a.Error },
                        { "choices", a.Choices.Select(ch => new Dictionary<string, object> { { "id", ch.Id }, { "label", ch.Label } }).ToList() } }, origin, true);
                }
                catch (AggregateException e)
                {
                    Send(s, 200, new Dictionary<string, object> { { "title", "" }, { "choices", new object[0] }, { "error", e.GetBaseException().Message } }, origin, true);
                }
                return;
            }
            if (new[] { "download", "video", "links", "grab" }.Contains(kind)) { Raise(kind, data); Send(s, 200, Ok, origin, true); return; }
            Send(s, 404, null, origin, true);
        }

        static readonly Dictionary<string, object> Ok = new Dictionary<string, object> { { "ok", true } };

        void Raise(string kind, Dictionary<string, object> d)
        {
            if (ui != null) ui.Post(_ => Request(kind, d), null); else Request(kind, d);
        }

        static void Send(Stream s, int code, object body, string origin, bool cors)
        {
            var b = body == null ? new byte[0] : Encoding.UTF8.GetBytes(Json.Write(body));
            var h = new StringBuilder();
            h.Append("HTTP/1.1 ").Append(code).Append(code == 200 ? " OK" : code == 204 ? " No Content" : " Error").Append("\r\n");
            h.Append("Content-Type: application/json\r\nConnection: close\r\nContent-Length: ").Append(b.Length).Append("\r\n");
            if (cors) h.Append("Access-Control-Allow-Origin: ").Append(origin).Append("\r\nAccess-Control-Allow-Headers: Content-Type\r\n");
            h.Append("\r\n");
            var hb = Encoding.ASCII.GetBytes(h.ToString());
            s.Write(hb, 0, hb.Length); s.Write(b, 0, b.Length); s.Flush();
        }

        /// <summary>Ask an already running NexGet to show its window (single instance).</summary>
        public static bool AskRunningToShow(bool setup)
        {
            try
            {
                using (var c = new TcpClient()) {
                    c.Connect(IPAddress.Loopback, Port);
                    var body = Encoding.UTF8.GetBytes(setup ? "{\"setup\":true}" : "{}");
                    var req = Encoding.ASCII.GetBytes("POST /show HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\n\r\n");
                    var s = c.GetStream(); s.Write(req, 0, req.Length); s.Write(body, 0, body.Length);
                    var buf = new byte[64]; return s.Read(buf, 0, buf.Length) > 0;
                }
            }
            catch (SocketException) { return false; }
            catch (IOException) { return false; }
        }
    }
}
