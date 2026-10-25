// Entry point: single instance, crash logging, startup tasks.
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("NexGet Download Manager")]
[assembly: AssemblyProduct("NexGet Download Manager")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.7.2", FrameworkDisplayName = ".NET Framework 4.7.2")]

namespace NexGet
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;   // TLS 1.2 + 1.3
            ServicePointManager.DefaultConnectionLimit = 64;
            bool setup = args.Contains("--setup-extension"), tray = args.Contains("--tray");
            if (args.Contains("--cleanup")) { Browsers.UnregisterAll(); Startup.Enabled = false; return 0; }   // called by the uninstaller
            bool created;
            using (new Mutex(true, "NexGet-SingleInstance-" + Environment.UserName, out created))
            {
                if (!created) { Bridge.AskRunningToShow(setup); return 0; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Crash(e.Exception, false);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception, true);
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

                Updater.ApplyPending();
                var cfg = Config.Load();
                Manager mgr;
                try { mgr = new Manager(cfg); }
                catch (Exception e) { Log.Write("start failed: " + e); MessageBox.Show("NexGet couldn't start its download engine.\n\n" + e.Message, "NexGet", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
                var main = new MainForm(cfg, mgr);
                var h = main.Handle;   // create the window (clipboard listener, Invoke) even when starting in the tray
                try { main.Bridge = new Bridge(cfg); main.Bridge.Request += main.OnBridge; }
                catch (SocketException e) { Log.Write("bridge: " + e.Message); MessageBox.Show("Browser integration is unavailable: port " + Bridge.Port + " is in use by another program.", "NexGet"); }
                Browsers.RegisterAll();
                if (!tray) main.Show();
                Once(setup ? 600 : 75000, () => { if (setup || (!cfg.ExtSeen && (main.Bridge == null || Fmt.Now() - main.Bridge.LastPing > 150))) new IntegrationDialog(main) { PopToFront = true }.Show(); });   // non-modal: never blocks download dialogs
                if (Fmt.Now() - cfg.YtdlpChecked > 7 * 86400) Once(60000, () => Updater.UpdateYtdlp(main, true));
                Application.Run();
                return 0;
            }
        }

        static void Once(int ms, Action a)
        {
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += (s, e) => { t.Dispose(); a(); };
            t.Start();
        }

        static void Crash(Exception e, bool fatal)
        {
            Log.Write((fatal ? "CRASH " : "ERROR ") + e);
            if (fatal) MessageBox.Show("NexGet hit an error and has to close.\nDetails: " + Log.File_, "NexGet", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
