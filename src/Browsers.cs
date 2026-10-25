// Browser detection + add-on installation, and the weekly yt-dlp updater.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NexGet
{
    sealed class BrowserInfo
    {
        public string Key, Name, Exe, ExtPage, ExtRegKey;   // ExtRegKey: HKCU key for store "external extensions" (Chromium)
        public bool Firefox { get { return Key == "firefox"; } }
    }

    static class Browsers
    {
        static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static readonly string Pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        static readonly string Pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        static readonly BrowserInfo[] Known = {
            new BrowserInfo { Key = "chrome", Name = "Google Chrome", Exe = "chrome.exe", ExtPage = "chrome://extensions", ExtRegKey = @"Software\Google\Chrome\Extensions" },
            new BrowserInfo { Key = "edge", Name = "Microsoft Edge", Exe = "msedge.exe", ExtPage = "edge://extensions", ExtRegKey = @"Software\Microsoft\Edge\Extensions" },
            new BrowserInfo { Key = "brave", Name = "Brave", Exe = "brave.exe", ExtPage = "brave://extensions", ExtRegKey = @"Software\BraveSoftware\Brave-Browser\Extensions" },
            new BrowserInfo { Key = "opera", Name = "Opera", Exe = "opera.exe", ExtPage = "opera://extensions" },
            new BrowserInfo { Key = "vivaldi", Name = "Vivaldi", Exe = "vivaldi.exe", ExtPage = "vivaldi://extensions" },
            new BrowserInfo { Key = "firefox", Name = "Mozilla Firefox", Exe = "firefox.exe", ExtPage = "about:debugging#/runtime/this-firefox" } };

        static IEnumerable<string> Candidates(BrowserInfo b)
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                using (var k = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + b.Exe))
                    if (k != null && k.GetValue(null) is string) yield return ((string)k.GetValue(null)).Trim('"');
            var rel = new Dictionary<string, string[]> {
                { "chrome", new[] { @"Google\Chrome\Application\chrome.exe" } },
                { "edge", new[] { @"Microsoft\Edge\Application\msedge.exe" } },
                { "brave", new[] { @"BraveSoftware\Brave-Browser\Application\brave.exe" } },
                { "opera", new[] { @"Programs\Opera\opera.exe", @"Programs\Opera GX\opera.exe", @"Opera\opera.exe" } },
                { "vivaldi", new[] { @"Vivaldi\Application\vivaldi.exe" } },
                { "firefox", new[] { @"Mozilla Firefox\firefox.exe" } } }[b.Key];
            foreach (var root in new[] { Local, Pf, Pf86 }) foreach (var r in rel) yield return Path.Combine(root, r);
        }

        public static List<BrowserInfo> Detect()
        {
            var found = new List<BrowserInfo>();
            if (!App.IsWindows) return found;
            foreach (var b in Known)
            {
                var path = Candidates(b).FirstOrDefault(p => { try { return File.Exists(p); } catch (ArgumentException) { return false; } });
                if (path != null) found.Add(new BrowserInfo { Key = b.Key, Name = b.Name, Exe = path, ExtPage = b.ExtPage, ExtRegKey = b.ExtRegKey });
            }
            return found;
        }

        /// <summary>store.json next to NexGet.exe: ids/urls of the published add-on (empty until published).</summary>
        public static Dictionary<string, string> Store()
        {
            try { return Json.Obj(File.ReadAllText(Path.Combine(App.Dir, "store.json"))).ToDictionary(k => k.Key, k => Convert.ToString(k.Value)); }
            catch (IOException) { return new Dictionary<string, string>(); }
            catch (ArgumentException) { return new Dictionary<string, string>(); }
        }

        public static string StoreUrl(BrowserInfo b)
        {
            var s = Store(); string v;
            if (b.Firefox) return s.TryGetValue("firefox_url", out v) && v.Length > 0 ? v : null;
            if (b.Key == "edge") return s.TryGetValue("edge_id", out v) && v.Length > 0 ? "https://microsoftedge.microsoft.com/addons/detail/" + v : null;
            if (b.Key == "opera" && s.TryGetValue("opera_url", out v) && v.Length > 0) return v;
            return s.TryGetValue("chrome_id", out v) && v.Length > 0 ? "https://chromewebstore.google.com/detail/" + v : null;   // Chrome, Brave, Opera, Vivaldi use the Chrome Web Store
        }

        /// <summary>Chromium "external extension" registration: the browser offers to enable the store add-on on next start.</summary>
        public static bool Register(BrowserInfo b)
        {
            if (b.ExtRegKey == null) return false;
            var s = Store(); string id; string update;
            if (b.Key == "edge") { if (!s.TryGetValue("edge_id", out id) || id.Length == 0) return false; update = "https://edge.microsoft.com/extensionwebstorebase/v1/crx"; }
            else { if (!s.TryGetValue("chrome_id", out id) || id.Length == 0) return false; update = "https://clients2.google.com/service/update2/crx"; }
            using (var k = Registry.CurrentUser.CreateSubKey(b.ExtRegKey + "\\" + id)) k.SetValue("update_url", update);
            return true;
        }

        public static void UnregisterAll()
        {
            var s = Store();
            foreach (var b in Known.Where(k => k.ExtRegKey != null))
                foreach (var key in new[] { "chrome_id", "edge_id" })
                {
                    string id;
                    if (s.TryGetValue(key, out id) && id.Length > 0) try { Registry.CurrentUser.DeleteSubKeyTree(b.ExtRegKey + "\\" + id, false); } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
                }
        }

        public static void RegisterAll() { foreach (var b in Detect()) try { Register(b); } catch (UnauthorizedAccessException) { } catch (IOException) { } }

        public static void Launch(BrowserInfo b, string url)
        {
            try { Process.Start(new ProcessStartInfo(b.Exe, url) { UseShellExecute = false }); }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }

    sealed class IntegrationDialog : NgDialog
    {
        public IntegrationDialog(MainForm main) : base("Browser integration", 680)
        {
            var bridge = main.Bridge;
            AddWide(new Label { Text = "Connect NexGet to your browsers", Font = Theme.Title, AutoSize = true });
            AddWide(Note("With the NexGet add-on, downloads are taken over automatically and a \"Download this video\" button appears on videos."));
            var browsers = Browsers.Detect();
            if (browsers.Count == 0) AddWide(Note("No supported browser was found (Chrome, Edge, Brave, Opera, Vivaldi, Firefox)."));
            var extDir = Path.Combine(App.Dir, "extension");
            foreach (var b in browsers)
            {
                var store = Browsers.StoreUrl(b);
                bool on = bridge != null && bridge.SeenFrom(b.Key);
                var name = new Label { Text = b.Name, AutoSize = true, Font = Theme.UiBold, Margin = new Padding(0, 10, 18, 0) };
                var state = new Label { Text = on ? "Connected" : "Not connected", AutoSize = true, ForeColor = on ? Theme.Done : Theme.Muted, Margin = new Padding(0, 10, 12, 0) };
                var btn = new NgButton(on ? "Open add-on page" : store != null ? "Add to " + b.Name.Split(' ').Last() : "Set up", !on);
                btn.Click += (s, e) => {
                    if (store != null) { Browsers.Register(b); Browsers.Launch(b, store); return; }
                    Clipboard.SetText(b.Firefox ? Path.Combine(extDir, "manifest.json") : extDir);
                    Browsers.Launch(b, b.ExtPage);
                    MessageBox.Show(this, b.Firefox
                        ? "Firefox opened its debugging page.\n\nClick \"Load Temporary Add-on...\" and paste the path that is now on your clipboard (NexGet's manifest.json).\n\nFirefox removes temporary add-ons when it closes; the permanent version comes from addons.mozilla.org once published."
                        : b.Name + " opened its extensions page.\n\n1. Turn on Developer mode.\n2. Click \"Load unpacked\".\n3. Paste the folder path that is now on your clipboard and press Enter.",
                        "Set up " + b.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                };
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(state); row.Controls.Add(btn);
                Body.Controls.Add(name); Body.Controls.Add(row);
            }
            AddWide(Note(Browsers.Store().Values.Any(v => v.Length > 0) ? "" :
                "The add-on isn't in the browser stores yet, so it's added in developer mode (about 30 seconds per browser). Once it's published, this page installs it with one click."));
            var refresh = new NgButton("Check again"); refresh.Click += (s, e) => { Close(); new IntegrationDialog(main).ShowDialog(main.Visible ? (IWin32Window)main : null); };
            var done = new NgButton("Done", true) { DialogResult = DialogResult.OK };
            done.Click += (s, e) => Close();
            Footer.Controls.Add(done); AcceptButton = done; CancelButton = done;
            Footer.Controls.Add(refresh);
            main.Cfg.ExtSeen = true; main.Cfg.Save();
        }
    }

    static class Updater
    {
        static string Dir { get { return Path.Combine(App.Dir, "tools"); } }

        /// <summary>Swap in an update downloaded last time (yt-dlp can't be replaced while a download uses it).</summary>
        public static void ApplyPending()
        {
            string cur = Path.Combine(Dir, "yt-dlp"), next = Path.Combine(Dir, "yt-dlp.new"), old = Path.Combine(Dir, "yt-dlp.old");
            try
            {
                if (Directory.Exists(old)) Directory.Delete(old, true);
                if (!Directory.Exists(next)) return;
                if (Directory.Exists(cur)) Directory.Move(cur, old);
                Directory.Move(next, cur);
                Directory.Delete(old, true);
                Log.Write("yt-dlp updated");
            }
            catch (IOException e) { Log.Write("yt-dlp swap failed: " + e.Message); }
            catch (UnauthorizedAccessException e) { Log.Write("yt-dlp swap failed: " + e.Message); }
        }

        static string Get(string url)
        {
            using (var w = new WebClient()) { w.Headers[HttpRequestHeader.UserAgent] = "NexGet/" + App.Version; return w.DownloadString(url); }
        }

        public static void UpdateYtdlp(MainForm f, bool silent)
        {
            var exe = App.Tool("yt-dlp");
            Task.Run(() => {
                string o, e;
                var current = exe != null && Proc.Run(exe, new[] { "--version" }, 30000, out o, out e) == 0 ? o.Trim() : "";
                var tag = Json.Obj(Get("https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest")).Str("tag_name");
                if (tag.Length == 0 || tag == current) return "current";
                var baseUrl = "https://github.com/yt-dlp/yt-dlp/releases/download/" + tag + "/";
                var sums = Get(baseUrl + "SHA2-256SUMS");
                var want = sums.Split('\n').Select(l => l.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).FirstOrDefault(p => p.Length == 2 && p[1] == "yt-dlp_win.zip");
                if (want == null) throw new InvalidDataException("checksum list has no yt-dlp_win.zip");
                var zip = Path.Combine(Path.GetTempPath(), "nexget-yt-dlp-" + tag + ".zip");
                using (var w = new WebClient()) { w.Headers[HttpRequestHeader.UserAgent] = "NexGet/" + App.Version; w.DownloadFile(baseUrl + "yt-dlp_win.zip", zip); }
                string got;
                using (var s = File.OpenRead(zip)) got = BitConverter.ToString(SHA256.Create().ComputeHash(s)).Replace("-", "").ToLowerInvariant();
                if (got != want[0].ToLowerInvariant()) { File.Delete(zip); throw new InvalidDataException("download checksum mismatch"); }
                var next = Path.Combine(Dir, "yt-dlp.new");
                if (Directory.Exists(next)) Directory.Delete(next, true);
                ZipFile.ExtractToDirectory(zip, next);
                File.Delete(zip);
                return tag;
            }).ContinueWith(t => {
                f.Cfg.YtdlpChecked = Fmt.Now(); f.Cfg.Save();
                if (t.IsFaulted) { Log.Write("yt-dlp update failed: " + t.Exception.GetBaseException().Message); if (!silent) MessageBox.Show(f, "Couldn't update video support:\n" + t.Exception.GetBaseException().Message, "NexGet"); return; }
                if (t.Result == "current") { if (!silent) MessageBox.Show(f, "Video support is up to date.", "NexGet"); return; }
                if (!f.Mgr.Items.Any(i => i.Kind == "video" && i.Status == State.Active)) ApplyPending();
                Log.Write("yt-dlp " + t.Result + " downloaded");
                if (!silent) MessageBox.Show(f, "Video support updated to " + t.Result + ".", "NexGet");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
