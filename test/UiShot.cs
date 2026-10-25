// Renders the main window and dialogs with sample data, capturing each to PNG (visual QA on Linux/Mono).
using System; using System.Collections.Generic; using System.Diagnostics; using System.Drawing; using System.IO; using System.Linq; using System.Threading; using System.Windows.Forms;
using NexGet;
static class UiShot {
    static string outDir;
    static void Shot(Form f, string name) {
        Application.DoEvents(); Thread.Sleep(400); Application.DoEvents();
        var b = f.Bounds;
        var p = Process.Start(new ProcessStartInfo("sh", "-c \"xwd -root -silent | convert xwd:- -crop " + b.Width + "x" + b.Height + "+" + b.X + "+" + b.Y + " +repage " + outDir + "/" + name + ".png\"") { UseShellExecute = false });
        p.WaitForExit();
    }
    static void Show(Form f, string name, int w = 0, int h = 0) {
        f.StartPosition = FormStartPosition.Manual; f.Location = new Point(10, 10); if (w > 0) f.Size = new Size(w, h);
        f.Show(); Shot(f, name); f.Close();
    }
    [STAThread] static void Main(string[] a) {
        outDir = a[0];
        Application.EnableVisualStyles(); SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var cfg = new Config(); var mgr = new Manager(cfg);
        double now = Fmt.Now();
        Func<string, long, long, State, long, Item> mk = (n, size, done, st, speed) => { var it = new Item { Path = "/tmp/dl/" + n, Url = "https://downloads.example.org/files/" + n, Size = size, Done = done, Status = st, Speed = speed, Category = Cats.For(n), LastTry = now - mgr.Items.Count * 2400, Connections = 8 }; mgr.Items.Add(it); return it; };
        var act = mk("ubuntu-25.10-desktop-amd64.iso", 6114770944, 2568204160, State.Active, 12600000);
        act.Bitfield = "ffffe0ff00fffc0ff8001ffffe00ff8"; act.NumPieces = 124;
        mk("Lecture 12 - Graph Algorithms.mp4", 412090368, 288463257, State.Active, 4350000).Description = "Lecture 12 - Graph Algorithms";
        mk("vlc-3.0.21-win64.exe", 44040192, 44040192, State.Complete, 0);
        mk("Annual Report 2026.pdf", 2411724, 2411724, State.Complete, 0);
        mk("dataset_full_v3.zip", 1932735283, 812345678, State.Paused, 0);
        mk("album_flac_remastered.rar", 734003200, 0, State.Queued, 0);
        var err = mk("setup_tool.msi", 8123456, 0, State.Error, 0); err.Error = "403 Forbidden - the link expired";
        mk("podcast-episode-88.mp3", 58123456, 58123456, State.Complete, 0);
        var main = new MainForm(cfg, mgr);
        main.StartPosition = FormStartPosition.Manual; main.Location = new Point(10, 10); main.Size = new Size(1180, 600);
        main.Show(); typeof(MainForm).GetMethod("Refresh_", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(main, null);
        Shot(main, "main");
        var pd = new ProgressDialog(main, act); Show(pd, "progress");
        Show(new FileInfoDialog(cfg, "https://downloads.example.org/files/setup-4.2.exe", "setup-4.2.exe", 73400320, ""), "fileinfo");
        var an = new Analysis { Title = "Lecture 12 - Graph Algorithms" };
        foreach (var l in new[] { "MP4  1080p   412 MB", "MP4  720p   210 MB", "MP4  480p   118 MB", "Audio only  (M4A)   18.2 MB" }) an.Choices.Add(new Choice { Label = l });
        Show(new VideoDialog(cfg, an, an.Choices[1].Id), "video");
        Show(new OptionsDialog(main, 1), "options");
        Show(new CompleteDialog(main, mgr.Items[2]), "complete");
        Show(new AboutDialog(), "about");
        Show(new AddUrlDialog("https://downloads.example.org/big-file.zip"), "addurl");
        mgr.Shutdown();
    }
}
