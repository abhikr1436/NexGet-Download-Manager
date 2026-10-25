// Visual system: palette, vector icon set (drawn with GDI+, crisp at any DPI), toolbar renderer, native helpers.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NexGet
{
    static class Theme
    {
        public static readonly Color Ink = Color.FromArgb(0x1C, 0x1B, 0x29);
        public static readonly Color Muted = Color.FromArgb(0x6B, 0x6A, 0x80);
        public static readonly Color Surface = Color.White;
        public static readonly Color Wash = Color.FromArgb(0xF6, 0xF5, 0xFB);
        public static readonly Color Line = Color.FromArgb(0xE4, 0xE2, 0xEF);
        public static readonly Color Brand = Color.FromArgb(0x5B, 0x3D, 0xF5);
        public static readonly Color BrandDark = Color.FromArgb(0x43, 0x26, 0xD9);
        public static readonly Color BrandSoft = Color.FromArgb(0xEC, 0xE8, 0xFF);
        public static readonly Color BrandSofter = Color.FromArgb(0xF4, 0xF1, 0xFF);
        public static readonly Color Done = Color.FromArgb(0x1E, 0x9E, 0x6A);
        public static readonly Color Paused = Color.FromArgb(0xC9, 0x8A, 0x12);
        public static readonly Color Error = Color.FromArgb(0xD9, 0x3C, 0x3C);
        public static readonly Font UiFont = new Font("Segoe UI", 9f);
        public static readonly Font UiBold = new Font("Segoe UI Semibold", 9f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 12f);

        static Icon appIcon;
        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (ArgumentException) { }
                    if (appIcon == null) appIcon = SystemIcons.Application;
                }
                return appIcon;
            }
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            if (rad <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        public static void Prepare(Graphics g) { g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; }

        /// <summary>The NexGet mark (N whose right stem drops into a download arrow) on a violet tile.</summary>
        public static void DrawLogo(Graphics g, RectangleF r)
        {
            Prepare(g);
            using (var b = new LinearGradientBrush(r, Color.FromArgb(0x6E, 0x4B, 0xFF), BrandDark, 45f))
            using (var tile = Round(r, r.Width * 0.22f)) g.FillPath(b, tile);
            var s = r.Width / 256f * 0.74f;
            var st = g.Save();
            g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2 + 2 * r.Width / 256f); g.ScaleTransform(s, s); g.TranslateTransform(-132, -128);
            using (var p = new GraphicsPath())
            {
                p.AddPolygon(new[] { new PointF(36, 36), new PointF(80, 36), new PointF(156, 128.52f), new PointF(156, 36), new PointF(200, 36),
                    new PointF(200, 148), new PointF(228, 148), new PointF(178, 220), new PointF(128, 148), new PointF(80, 89.57f),
                    new PointF(80, 220), new PointF(36, 220) });
                g.FillPath(Brushes.White, p);
            }
            g.Restore(st);
        }
    }

    /// <summary>Line icons on a 24-unit grid. One drawing per name; cached per size/colour.</summary>
    static class Icons
    {
        static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

        public static Bitmap Get(string name, int size, Color c)
        {
            var key = name + size + c.ToArgb();
            Bitmap bmp;
            if (Cache.TryGetValue(key, out bmp)) return bmp;
            bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                Theme.Prepare(g);
                g.ScaleTransform(size / 24f, size / 24f);
                using (var pen = new Pen(c, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                using (var fill = new SolidBrush(c))
                    Draw(g, name, pen, fill, c);
            }
            Cache[key] = bmp;
            return bmp;
        }

        static void Draw(Graphics g, string n, Pen p, Brush f, Color c)
        {
            switch (n)
            {
                case "add":   // primary action: filled brand disc with plus
                    using (var b = new SolidBrush(Theme.Brand)) g.FillEllipse(b, 2, 2, 20, 20);
                    using (var w = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) { g.DrawLine(w, 12, 7.5f, 12, 16.5f); g.DrawLine(w, 7.5f, 12, 16.5f, 12); }
                    break;
                case "resume": using (var t = Tri(7, 5, 19, 12, 7, 19)) { g.FillPath(f, t); } break;
                case "stop": g.FillRectangle(f, 6.5f, 5.5f, 4, 13); g.FillRectangle(f, 13.5f, 5.5f, 4, 13); break;
                case "stopall": using (var r = Theme.Round(new RectangleF(5.5f, 5.5f, 13, 13), 2.5f)) g.FillPath(f, r); break;
                case "delete":
                    g.DrawLine(p, 4.5f, 6.5f, 19.5f, 6.5f); g.DrawLines(p, new[] { new PointF(9.5f, 6.5f), new PointF(10, 4), new PointF(14, 4), new PointF(14.5f, 6.5f) });
                    using (var r = Theme.Round(new RectangleF(6.5f, 6.5f, 11, 13.5f), 2)) g.DrawPath(p, r);
                    g.DrawLine(p, 10.5f, 10, 10.5f, 16.5f); g.DrawLine(p, 13.5f, 10, 13.5f, 16.5f); break;
                case "clean":   // delete completed: tray with check
                    g.DrawLines(p, new[] { new PointF(4, 13), new PointF(4, 19), new PointF(20, 19), new PointF(20, 13) });
                    g.DrawLines(p, new[] { new PointF(8, 9), new PointF(11, 12), new PointF(17, 5) }); break;
                case "options":
                    for (int i = 0; i < 8; i++)
                    {
                        var st = g.Save(); g.TranslateTransform(12, 12); g.RotateTransform(i * 45);
                        using (var tooth = Theme.Round(new RectangleF(-1.6f, -10f, 3.2f, 4.2f), 0.8f)) g.FillPath(f, tooth);
                        g.Restore(st);
                    }
                    using (var ring = new Pen(c, 2.4f)) g.DrawEllipse(ring, 5.8f, 5.8f, 12.4f, 12.4f);
                    g.DrawEllipse(p, 9.8f, 9.8f, 4.4f, 4.4f); break;
                case "schedule":
                    g.DrawEllipse(p, 3.5f, 3.5f, 17, 17); g.DrawLines(p, new[] { new PointF(12, 7.5f), new PointF(12, 12), new PointF(15.5f, 14) }); break;
                case "qstart":
                    g.DrawLine(p, 4, 6, 13, 6); g.DrawLine(p, 4, 11, 11, 11); g.DrawLine(p, 4, 16, 9, 16);
                    using (var t = Tri(13, 11, 21, 15.5f, 13, 20)) g.FillPath(f, t); break;
                case "qstop":
                    g.DrawLine(p, 4, 6, 13, 6); g.DrawLine(p, 4, 11, 11, 11); g.DrawLine(p, 4, 16, 9, 16);
                    using (var r = Theme.Round(new RectangleF(13.5f, 11.5f, 7.5f, 7.5f), 1.5f)) g.FillPath(f, r); break;
                case "grab":   // video
                    using (var r = Theme.Round(new RectangleF(3, 6, 13, 12), 2.5f)) g.DrawPath(p, r);
                    using (var t = Tri(16, 12, 21, 8.5f, 21, 15.5f)) g.DrawPath(p, t); break;
                // ---- categories (sidebar)
                case "all": using (var r = Theme.Round(new RectangleF(4, 4, 16, 16), 3)) g.DrawPath(p, r); g.DrawLines(p, new[] { new PointF(12, 8), new PointF(12, 15) }); g.DrawLines(p, new[] { new PointF(9, 12.5f), new PointF(12, 15.5f), new PointF(15, 12.5f) }); break;
                case "Compressed": using (var r = Theme.Round(new RectangleF(4, 7, 16, 13), 2)) g.DrawPath(p, r); g.DrawLine(p, 3, 7, 21, 7); g.DrawLine(p, 10, 11, 14, 11); break;
                case "Documents": g.DrawPolygon(p, new[] { new PointF(6, 3.5f), new PointF(14, 3.5f), new PointF(18.5f, 8), new PointF(18.5f, 20.5f), new PointF(6, 20.5f) }); g.DrawLine(p, 9, 12, 15.5f, 12); g.DrawLine(p, 9, 16, 15.5f, 16); break;
                case "Music": g.DrawLines(p, new[] { new PointF(10, 17), new PointF(10, 5), new PointF(19, 3.5f), new PointF(19, 15) }); g.FillEllipse(f, 5.5f, 14.5f, 5.5f, 5); g.FillEllipse(f, 14.5f, 12.5f, 5.5f, 5); break;
                case "Programs": using (var r = Theme.Round(new RectangleF(3.5f, 4.5f, 17, 15), 2.5f)) g.DrawPath(p, r); g.DrawLine(p, 3.5f, 8.5f, 20.5f, 8.5f); break;
                case "Video": using (var r = Theme.Round(new RectangleF(3.5f, 5, 17, 14), 2.5f)) g.DrawPath(p, r); using (var t = Tri(10, 9, 15, 12, 10, 15)) g.FillPath(f, t); break;
                case "General": g.DrawPolygon(p, new[] { new PointF(6, 3.5f), new PointF(14, 3.5f), new PointF(18.5f, 8), new PointF(18.5f, 20.5f), new PointF(6, 20.5f) }); break;
                case "unfinished": g.DrawArc(p, 4, 4, 16, 16, -90, 270); g.DrawLine(p, 12, 12, 12, 4); break;
                case "finished": g.DrawEllipse(p, 3.5f, 3.5f, 17, 17); g.DrawLines(p, new[] { new PointF(8, 12.5f), new PointF(11, 15.5f), new PointF(16.5f, 9) }); break;
                case "queue": g.DrawLine(p, 5, 7, 19, 7); g.DrawLine(p, 5, 12, 19, 12); g.DrawLine(p, 5, 17, 13, 17); break;
                case "browser": g.DrawEllipse(p, 3.5f, 3.5f, 17, 17); g.DrawLine(p, 3.5f, 12, 20.5f, 12); g.DrawEllipse(p, 8, 3.5f, 8, 17); break;
            }
        }

        static GraphicsPath Tri(float x1, float y1, float x2, float y2, float x3, float y3)
        {
            var p = new GraphicsPath(); p.AddPolygon(new[] { new PointF(x1, y1), new PointF(x2, y2), new PointF(x3, y3) }); return p;
        }
    }

    /// <summary>Flat toolbar: white strip, soft violet hover, hairline bottom border.</summary>
    sealed class NgRenderer : ToolStripProfessionalRenderer
    {
        public NgRenderer() { RoundedEdges = false; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(e.ToolStrip is StatusStrip ? Theme.Wash : Theme.Surface)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown) { using (var p = new Pen(Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1); return; }
            using (var p = new Pen(Theme.Line))
            {
                if (e.ToolStrip is StatusStrip) e.Graphics.DrawLine(p, 0, 0, e.AffectedBounds.Width, 0);
                else e.Graphics.DrawLine(p, 0, e.AffectedBounds.Height - 1, e.AffectedBounds.Width, e.AffectedBounds.Height - 1);
            }
        }
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) { Hover(e); }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled) Hover(e); else if (e.ToolStrip is ToolStripDropDown) using (var b = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(b, 0, 0, e.Item.Width, e.Item.Height);
        }
        static void Hover(ToolStripItemRenderEventArgs e)
        {
            var it = e.Item;
            if (!it.Enabled || !(it.Selected || it.Pressed)) return;
            Theme.Prepare(e.Graphics);
            var r = new RectangleF(2, 1, it.Width - 4, it.Height - 2);
            using (var b = new SolidBrush(it.Pressed ? Theme.BrandSoft : Theme.BrandSofter))
            using (var path = Theme.Round(r, 6)) e.Graphics.FillPath(b, path);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var p = new Pen(Theme.Line))
                if (e.Vertical) e.Graphics.DrawLine(p, e.Item.Width / 2, 8, e.Item.Width / 2, e.Item.Height - 8);
                else e.Graphics.DrawLine(p, 28, e.Item.Height / 2, e.Item.Width - 4, e.Item.Height / 2);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { using (var b = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(b, e.AffectedBounds); }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Color.FromArgb(0xA9, 0xA8, 0xB8) : e.ToolStrip is StatusStrip ? Theme.Muted : Theme.Ink;
            base.OnRenderItemText(e);
        }
    }

    static class Native
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr h);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO fi, uint size, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint attr; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string type; }
        public const int WM_CLIPBOARDUPDATE = 0x031D;

        public static void Explorer(Control c) { if (App.IsWindows) try { SetWindowTheme(c.Handle, "Explorer", null); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { } }

        /// <summary>Windows 11: rounded corners for dialogs (no-op elsewhere).</summary>
        public static void RoundCorners(Form f)
        {
            if (!App.IsWindows) return;
            try { int v = 2; DwmSetWindowAttribute(f.Handle, 33, ref v, 4); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }

        /// <summary>Bring a window above the browser. Windows blocks focus stealing; a synthetic Alt tap is the usual workaround.</summary>
        public static void Front(Form f)
        {
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            f.Show(); f.TopMost = true; f.Activate(); f.BringToFront();
            if (App.IsWindows) try { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); SetForegroundWindow(f.Handle); } catch (DllNotFoundException) { }
            var t = new Timer { Interval = 600 }; t.Tick += (s, e) => { t.Dispose(); if (!f.IsDisposed) f.TopMost = false; }; t.Start();
        }

        static readonly Dictionary<string, Bitmap> FileIcons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The shell's icon for a file type (by extension; the file need not exist).</summary>
        public static Bitmap FileIcon(string name)
        {
            var ext = Path.GetExtension(name ?? "");
            Bitmap b;
            if (FileIcons.TryGetValue(ext, out b)) return b;
            b = null;
            if (App.IsWindows)
                try
                {
                    var fi = new SHFILEINFO();
                    if (SHGetFileInfo("x" + ext, 0x80, ref fi, (uint)Marshal.SizeOf(fi), 0x110 | 0x1) != IntPtr.Zero && fi.hIcon != IntPtr.Zero)   // ICON|USEFILEATTRIBUTES|SMALLICON
                    {
                        using (var ic = Icon.FromHandle(fi.hIcon)) b = ic.ToBitmap();
                        DestroyIcon(fi.hIcon);
                    }
                }
                catch (DllNotFoundException) { }
            if (b == null) b = Icons.Get(Cats.For(name), 16, Theme.Muted);
            FileIcons[ext] = b;
            return b;
        }
    }
}
