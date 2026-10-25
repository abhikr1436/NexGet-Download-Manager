// Main window: IDM layout (menu, big toolbar, category tree, download list, status bar) with a calmer, sharper look.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NexGet
{
    sealed class MainForm : Form
    {
        public readonly Manager Mgr;
        public readonly Config Cfg;
        public Bridge Bridge;
        readonly ListView list = new ListView();
        readonly TreeView tree = new TreeView();
        readonly ToolStrip bar = new ToolStrip();
        readonly Label stLeft = new Label(), stRight = new Label();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Timer tick = new Timer { Interval = 500 };
        readonly Dictionary<string, ToolStripItem> acts = new Dictionary<string, ToolStripItem>();
        readonly Dictionary<string, ProgressDialog> progress = new Dictionary<string, ProgressDialog>();
        List<Item> rows = new List<Item>();
        string filter = "all";
        int sortCol = 5; bool sortDesc = true;
        bool quitting, trayHintShown;
        string lastClip = "", lastScheduleRun = "";

        static readonly string[] Cols = { "File name", "Size", "Status", "Time left", "Transfer rate", "Last try", "Description" };
        static readonly int[] Widths = { 330, 84, 132, 84, 100, 140, 200 };

        public MainForm(Config cfg, Manager mgr)
        {
            Cfg = cfg; Mgr = mgr;
            Text = "NexGet Download Manager"; Icon = Theme.AppIcon; Font = Theme.UiFont;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None; MinimumSize = new Size(Dpi(760), Dpi(420)); Size = new Size(Dpi(1180), Dpi(660));
            StartPosition = FormStartPosition.CenterScreen; BackColor = Theme.Surface;
            BuildBody(); BuildStatus(); BuildToolbar(); BuildMenu(); BuildTray();   // dock order: last added docks outermost
            Mgr.Changed += Refresh_; Mgr.Completed += OnCompleted; Mgr.Failed += it => Notify("Download failed", it.FileName + "\n" + it.Error, ToolTipIcon.Warning);
            Mgr.QueueFinished += OnQueueFinished;
            int ticks = 0;
            tick.Tick += (s, e) => { Mgr.Poll(); CheckSchedule(); if (++ticks % 6 == 0) Refresh_(); };   // status bar / browser state every 3 s
            tick.Start();
            Refresh_();
        }

        readonly float scale = 1;
        int Dpi(int px) { return (int)Math.Round(px * scale); }

        // ------------------------------------------------------------------ layout
        void BuildMenu()
        {
            var ms = new MenuStrip { Renderer = new NgRenderer(), BackColor = Theme.Surface, Padding = new Padding(6, 2, 0, 2) };
            var tasks = new ToolStripMenuItem("&Tasks");
            tasks.DropDownItems.AddRange(new ToolStripItem[] {
                Mi("Add URL...", AddUrl, Keys.Control | Keys.N), Mi("Add links from clipboard", BatchFromClipboard),
                new ToolStripSeparator(), Mi("Browser integration...", () => new IntegrationDialog(this).ShowDialog(this)),
                new ToolStripSeparator(), Mi("Exit", Quit) });
            var dl = new ToolStripMenuItem("&Downloads");
            dl.DropDownItems.AddRange(new ToolStripItem[] {
                Mi("Resume", ResumeSel), Mi("Stop", StopSel), Mi("Stop all", StopAll), Mi("Delete...", DeleteSel), Mi("Delete completed", DeleteCompleted),
                new ToolStripSeparator(), Mi("Start queue", StartQueue), Mi("Stop queue", StopQueue), Mi("Scheduler...", () => ShowOptions(3)),
                new ToolStripSeparator(), Mi("Options...", () => ShowOptions(0)) });
            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.AddRange(new ToolStripItem[] {
                Mi("Update video support now", () => Updater.UpdateYtdlp(this, false)), Mi("Open log file", () => Open(Log.File_)),
                new ToolStripSeparator(), Mi("About NexGet", () => new AboutDialog().ShowDialog(this)) });
            ms.Items.AddRange(new ToolStripItem[] { tasks, dl, help });
            Controls.Add(ms); MainMenuStrip = ms;
        }

        static ToolStripMenuItem Mi(string text, Action a, Keys keys = Keys.None)
        {
            var m = new ToolStripMenuItem(text, null, (s, e) => a());
            if (keys != Keys.None) m.ShortcutKeys = keys;
            return m;
        }

        void BuildToolbar()
        {
            bar.Renderer = new NgRenderer(); bar.GripStyle = ToolStripGripStyle.Hidden; bar.Padding = new Padding(Dpi(6), Dpi(4), 0, Dpi(4));
            bar.ImageScalingSize = new Size(Dpi(28), Dpi(28)); bar.BackColor = Theme.Surface; bar.AutoSize = false; bar.Height = Dpi(74);
            Action<string, string, string, Action> btn = (key, text, icon, act) => {
                var b = new ToolStripButton(text, Icons.Get(icon, Dpi(28), Theme.Ink), (s, e) => act()) {
                    TextImageRelation = TextImageRelation.ImageAboveText, AutoSize = false,
                    Size = new Size(Math.Max(Dpi(72), TextRenderer.MeasureText(text, Theme.UiFont).Width + Dpi(20)), Dpi(62)),
                    ToolTipText = text, ImageScaling = ToolStripItemImageScaling.None };
                acts[key] = b; bar.Items.Add(b);
            };
            btn("add", "Add URL", "add", AddUrl);
            btn("resume", "Resume", "resume", ResumeSel);
            btn("stop", "Stop", "stop", StopSel);
            btn("stopall", "Stop all", "stopall", StopAll);
            btn("delete", "Delete", "delete", DeleteSel);
            btn("clean", "Delete completed", "clean", DeleteCompleted);
            btn("options", "Options", "options", () => ShowOptions(0));
            bar.Items.Add(new ToolStripSeparator { AutoSize = false, Size = new Size(Dpi(14), Dpi(62)) });
            btn("schedule", "Scheduler", "schedule", () => ShowOptions(3));
            btn("qstart", "Start queue", "qstart", StartQueue);
            btn("qstop", "Stop queue", "qstop", StopQueue);
            Controls.Add(bar);
        }

        void BuildBody()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 1, BackColor = Theme.Line };
            split.Panel1.BackColor = Theme.Wash; split.Panel2.BackColor = Theme.Surface;

            var imgs = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(Dpi(18), Dpi(18)) };
            foreach (var n in new[] { "all", "Compressed", "Documents", "Music", "Programs", "Video", "General", "unfinished", "finished", "queue" })
                imgs.Images.Add(n, Icons.Get(n, Dpi(18), n == "all" ? Theme.Brand : Theme.Muted));
            tree.ImageList = imgs; tree.BorderStyle = BorderStyle.None; tree.BackColor = Theme.Wash; tree.ForeColor = Theme.Ink;
            tree.ShowLines = false; tree.FullRowSelect = true; tree.HotTracking = true; tree.ItemHeight = Dpi(28); tree.Indent = Dpi(18);
            tree.Dock = DockStyle.Fill; tree.HideSelection = false; tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
            tree.DrawNode += (s, e) => {   // brand selection instead of the system highlight
                if (e.Bounds.IsEmpty) return;
                bool sel = (e.State & TreeNodeStates.Selected) != 0;
                var r = new Rectangle(e.Bounds.X - 2, e.Bounds.Y + 1, Math.Max(e.Bounds.Width + 8, tree.ClientSize.Width - e.Bounds.X - 6), e.Bounds.Height - 2);
                using (var b = new SolidBrush(sel ? Theme.BrandSoft : tree.BackColor)) e.Graphics.FillRectangle(b, r);
                TextRenderer.DrawText(e.Graphics, e.Node.Text, sel ? Theme.UiBold : Theme.UiFont, r, sel ? Theme.BrandDark : Theme.Ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            };
            var all = Node("All downloads", "all");
            foreach (var c in Cats.All) all.Nodes.Add(Node(c, "cat:" + c, c));
            tree.Nodes.AddRange(new[] { all, Node("Unfinished", "unfinished"), Node("Finished", "finished"), Node("Queue", "queue") });
            tree.ExpandAll(); tree.SelectedNode = all;
            tree.AfterSelect += (s, e) => { filter = (string)e.Node.Tag; Refresh_(); };
            split.Panel1.Padding = new Padding(Dpi(6), Dpi(8), 0, 0);
            split.Panel1.Controls.Add(tree);

            list.View = View.Details; list.FullRowSelect = true; list.VirtualMode = true; list.OwnerDraw = true; list.HideSelection = false;
            list.BorderStyle = BorderStyle.None; list.Dock = DockStyle.Fill; list.HeaderStyle = ColumnHeaderStyle.Clickable;
            list.SmallImageList = new ImageList { ImageSize = new Size(1, Dpi(30)) };   // row height
            for (int i = 0; i < Cols.Length; i++) list.Columns.Add(Cols[i], Dpi(Widths[i]), i == 1 || i == 3 || i == 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
            list.RetrieveVirtualItem += (s, e) => {
                var it = rows[e.ItemIndex];
                e.Item = new ListViewItem(new[] { it.FileName, "", "", "", "", "", "" });
            };
            list.DrawColumnHeader += DrawHeader;
            list.DrawItem += (s, e) => { };
            list.DrawSubItem += DrawCell;
            list.ColumnClick += (s, e) => { if (sortCol == e.Column) sortDesc = !sortDesc; else { sortCol = e.Column; sortDesc = e.Column == 5; } Refresh_(); };
            list.DoubleClick += (s, e) => { var it = Selected().FirstOrDefault(); if (it == null) return; if (it.Status == State.Complete) Open(it.Path); else ShowProgress(it); };
            list.SelectedIndexChanged += (s, e) => UpdateActions();
            list.VirtualItemsSelectionRangeChanged += (s, e) => UpdateActions();
            list.KeyDown += ListKeys;
            list.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right && Selected().Count > 0) RowMenu().Show(list, e.Location); };
            split.Panel2.Controls.Add(list);
            Controls.Add(split);
            HandleCreated += (s, e) => { Native.Explorer(list); Native.Explorer(tree); };
            Load += (s, e) => { split.Panel1MinSize = Dpi(150); split.SplitterDistance = Dpi(208); };
        }

        static TreeNode Node(string text, string tag, string img = null)
        {
            var key = img ?? (tag == "all" ? "all" : tag);
            return new TreeNode(text) { Tag = tag, ImageKey = key, SelectedImageKey = key };
        }

        void BuildStatus()
        {
            var p = new Panel { Dock = DockStyle.Bottom, Height = Dpi(28), BackColor = Theme.Wash, Padding = new Padding(Dpi(10), 0, Dpi(12), 0) };
            p.Paint += (s, e) => { using (var pen = new Pen(Theme.Line)) e.Graphics.DrawLine(pen, 0, 0, p.Width, 0); };
            stLeft.Dock = DockStyle.Fill; stLeft.TextAlign = ContentAlignment.MiddleLeft; stLeft.ForeColor = Theme.Muted;
            stRight.Dock = DockStyle.Right; stRight.AutoSize = true; stRight.TextAlign = ContentAlignment.MiddleRight; stRight.ForeColor = Theme.Muted;
            stRight.Padding = new Padding(Dpi(16), Dpi(6), 0, 0); stRight.Cursor = Cursors.Hand;
            stRight.Click += (s, e) => new IntegrationDialog(this).ShowDialog(this);
            new ToolTip().SetToolTip(stRight, "Set up browser integration");
            p.Controls.Add(stLeft); p.Controls.Add(stRight);
            Controls.Add(p);
        }

        void BuildTray()
        {
            tray.Icon = Theme.AppIcon; tray.Text = "NexGet Download Manager"; tray.Visible = true;
            var m = new ContextMenuStrip { Renderer = new NgRenderer() };
            m.Items.AddRange(new ToolStripItem[] { Mi("Open NexGet", ShowMain), Mi("Add URL...", AddUrl), new ToolStripSeparator(),
                Mi("Start queue", StartQueue), Mi("Stop all", StopAll), new ToolStripSeparator(), Mi("Exit", Quit) });
            tray.ContextMenuStrip = m;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
        }

        // ------------------------------------------------------------------ list drawing
        void DrawHeader(object s, DrawListViewColumnHeaderEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.Wash)) g.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.Line)) { g.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1); g.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + 6, e.Bounds.Right - 1, e.Bounds.Bottom - 7); }
            var text = Cols[e.ColumnIndex] + (e.ColumnIndex == sortCol ? (sortDesc ? "  ↓" : "  ↑") : "");
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
            TextRenderer.DrawText(g, text, Theme.UiFont, Rectangle.Inflate(e.Bounds, -Dpi(8), 0), Theme.Muted, flags);
        }

        void DrawCell(object s, DrawListViewSubItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= rows.Count) return;
            var it = rows[e.ItemIndex]; var g = e.Graphics; var r = e.Bounds;
            bool sel = list.SelectedIndices.Contains(e.ItemIndex);
            using (var b = new SolidBrush(sel ? Theme.BrandSoft : Theme.Surface)) g.FillRectangle(b, r);
            if (sel && e.ColumnIndex == 0) using (var b = new SolidBrush(Theme.Brand)) g.FillRectangle(b, r.Left, r.Top + Dpi(5), Dpi(3), r.Height - Dpi(10));
            var inner = Rectangle.Inflate(r, -Dpi(8), 0);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            Color fg = it.Status == State.Error ? Theme.Error : Theme.Ink;
            switch (e.ColumnIndex)
            {
                case 0:
                    var ic = Native.FileIcon(it.FileName); int sz = Dpi(16);
                    g.DrawImage(ic, inner.Left, r.Top + (r.Height - sz) / 2, sz, sz);
                    TextRenderer.DrawText(g, it.FileName, Theme.UiFont, new Rectangle(inner.Left + sz + Dpi(8), r.Top, inner.Width - sz - Dpi(8), r.Height), fg, flags);
                    break;
                case 1: TextRenderer.DrawText(g, Fmt.Size(it.Size), Theme.UiFont, inner, Theme.Muted, flags | TextFormatFlags.Right); break;
                case 2:
                    string t; Color c;
                    switch (it.Status)
                    {
                        case State.Complete: t = "Complete"; c = Theme.Done; break;
                        case State.Error: t = "Error"; c = Theme.Error; break;
                        case State.Queued: t = "Queued"; c = Theme.Muted; break;
                        case State.Paused: t = it.Size > 0 && it.Done > 0 ? "Paused · " + it.Percent.ToString("0.0") + "%" : "Paused"; c = Theme.Paused; break;
                        default: t = it.Size > 0 ? it.Percent.ToString("0.0") + "%" : "Connecting..."; c = Theme.Ink; break;
                    }
                    var textR = new Rectangle(inner.Left, r.Top - Dpi(3), inner.Width, r.Height);
                    TextRenderer.DrawText(g, t, it.Status == State.Active ? Theme.UiBold : Theme.UiFont, textR, c, flags);
                    if ((it.Status == State.Active || it.Status == State.Paused) && it.Size > 0)
                    {
                        Theme.Prepare(g);
                        var track = new RectangleF(inner.Left, r.Bottom - Dpi(8), inner.Width, Dpi(3));
                        using (var tb = new SolidBrush(Theme.Line)) using (var tp = Theme.Round(track, track.Height / 2)) g.FillPath(tb, tp);
                        var fillW = Math.Max(track.Height, (float)(track.Width * it.Percent / 100));
                        using (var fb = new SolidBrush(it.Status == State.Paused ? Theme.Paused : Theme.Brand))
                        using (var fp = Theme.Round(new RectangleF(track.X, track.Y, fillW, track.Height), track.Height / 2)) g.FillPath(fb, fp);
                    }
                    break;
                case 3: TextRenderer.DrawText(g, Fmt.Time(it.Eta), Theme.UiFont, inner, Theme.Muted, flags | TextFormatFlags.Right); break;
                case 4: TextRenderer.DrawText(g, it.Status == State.Active ? Fmt.Speed(it.Speed) : "", Theme.UiFont, inner, Theme.Ink, flags | TextFormatFlags.Right); break;
                case 5: TextRenderer.DrawText(g, Fmt.Local(it.LastTry > 0 ? it.LastTry : it.Added).ToString("MMM d, HH:mm"), Theme.UiFont, inner, Theme.Muted, flags); break;
                case 6: TextRenderer.DrawText(g, it.Status == State.Error ? it.Error : it.Description, Theme.UiFont, inner, it.Status == State.Error ? Theme.Error : Theme.Muted, flags); break;
            }
        }

        // ------------------------------------------------------------------ list state
        bool Match(Item it)
        {
            switch (filter)
            {
                case "all": return true;
                case "unfinished": return it.Status != State.Complete;
                case "finished": return it.Status == State.Complete;
                case "queue": return it.Status == State.Queued;
                default: return filter.StartsWith("cat:") && it.Category == filter.Substring(4);
            }
        }

        static IComparable Key(Item it, int col)
        {
            switch (col)
            {
                case 0: return it.FileName.ToLowerInvariant();
                case 1: return it.Size;
                case 2: return (int)it.Status * 1000 + it.Percent;
                case 3: return it.Eta;
                case 4: return it.Speed;
                case 6: return it.Description;
                default: return it.LastTry > 0 ? it.LastTry : it.Added;
            }
        }

        void Refresh_()
        {
            var sel = new HashSet<string>(Selected().Select(i => i.Id));
            var next = Mgr.Items.Where(Match).ToList();
            next = (sortDesc ? next.OrderByDescending(i => Key(i, sortCol)) : next.OrderBy(i => Key(i, sortCol))).ToList();
            bool sameOrder = next.Count == rows.Count && next.Select(i => i.Id).SequenceEqual(rows.Select(i => i.Id));
            rows = next;
            if (!sameOrder)
            {
                list.BeginUpdate();
                list.VirtualListSize = rows.Count;
                list.SelectedIndices.Clear();
                for (int i = 0; i < rows.Count; i++) if (sel.Contains(rows[i].Id)) list.SelectedIndices.Add(i);
                list.EndUpdate();
            }
            list.Invalidate();
            var active = Mgr.Items.Where(i => i.Status == State.Active).ToList();
            stLeft.Text = active.Count == 0 ? (Mgr.Items.Count == 0 ? "Add a link or download something in your browser." : Mgr.Items.Count + (Mgr.Items.Count == 1 ? " download" : " downloads"))
                : active.Count + " downloading  ·  " + Fmt.Size(active.Sum(i => i.Speed)) + "/s" + (Mgr.QueueRunning ? "  ·  queue running" : "");
            bool on = Bridge != null && Fmt.Now() - Bridge.LastPing < 150;
            stRight.Text = on ? "●  Browser connected" : "●  Browser not connected";
            stRight.ForeColor = on ? Theme.Done : Theme.Paused;
            var tip = active.Count > 0 ? "NexGet · " + active.Count + " downloading · " + Fmt.Speed(active.Sum(i => i.Speed)) : "NexGet Download Manager";
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            UpdateActions();
        }

        List<Item> Selected()
        {
            var r = new List<Item>();
            foreach (int i in list.SelectedIndices) if (i < rows.Count) r.Add(rows[i]);
            return r;
        }

        void UpdateActions()
        {
            var sel = Selected();
            acts["resume"].Enabled = sel.Any(i => i.Status != State.Active && i.Status != State.Complete);
            acts["stop"].Enabled = sel.Any(i => i.Status == State.Active || i.Status == State.Queued);
            acts["delete"].Enabled = sel.Count > 0;
            acts["stopall"].Enabled = Mgr.Items.Any(i => i.Status == State.Active);
            acts["clean"].Enabled = Mgr.Items.Any(i => i.Status == State.Complete);
            acts["qstart"].Enabled = !Mgr.QueueRunning && Mgr.Items.Any(i => i.Status == State.Queued);
            acts["qstop"].Enabled = Mgr.QueueRunning;
            foreach (var k in new[] { "resume", "stop", "stopall", "delete", "clean", "qstart", "qstop" })
                ((ToolStripButton)acts[k]).Image = Icons.Get(k, Dpi(28), acts[k].Enabled ? Theme.Ink : Color.FromArgb(0xC2, 0xC1, 0xCF));
        }

        void ListKeys(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete) DeleteSel();
            else if (e.KeyCode == Keys.Enter) { var it = Selected().FirstOrDefault(); if (it != null && it.Status == State.Complete) Open(it.Path); else if (it != null) ShowProgress(it); }
            else if (e.Control && e.KeyCode == Keys.A) { list.BeginUpdate(); for (int i = 0; i < rows.Count; i++) list.SelectedIndices.Add(i); list.EndUpdate(); }
        }

        ContextMenuStrip RowMenu()
        {
            var sel = Selected(); var it = sel[0];
            var m = new ContextMenuStrip { Renderer = new NgRenderer() };
            if (it.Status == State.Complete)
            {
                m.Items.Add(Mi("Open", () => Open(it.Path))); m.Items.Add(Mi("Open with...", () => OpenWith(it.Path)));
            }
            m.Items.Add(Mi("Open folder", () => OpenFolder(it.Path)));
            m.Items.Add(new ToolStripSeparator());
            if (it.Status != State.Complete) { m.Items.Add(Mi("Resume", ResumeSel)); m.Items.Add(Mi("Stop", StopSel)); m.Items.Add(Mi("Show progress", () => ShowProgress(it))); }
            m.Items.Add(Mi("Download again", () => Mgr.Redownload(it)));
            if (it.Kind == "http" && it.Status != State.Complete) m.Items.Add(Mi("Refresh download address...", () => RefreshAddress(it)));
            if (sel.Any(i => i.Status == State.Paused || i.Status == State.Error))
                m.Items.Add(Mi("Move to queue", () => { foreach (var i in sel.Where(x => x.Status == State.Paused || x.Status == State.Error)) i.Status = State.Queued; Mgr.Save(); Refresh_(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Mi("Copy address", () => Clipboard.SetText(it.Url)));
            m.Items.Add(Mi("Delete...", DeleteSel));
            return m;
        }

        // ------------------------------------------------------------------ actions
        void AddUrl()
        {
            string clip = "";
            try { clip = Clipboard.GetText().Trim(); } catch (System.Runtime.InteropServices.ExternalException) { }
            using (var d = new AddUrlDialog(Regex.IsMatch(clip, @"^(https?|ftp)://\S+$") ? clip : ""))
                if (d.ShowDialog(this) == DialogResult.OK) NewDownload(d.Url, null, null, -1);
        }

        void ResumeSel()
        {
            foreach (var it in Selected())
            {
                if (it.Status == State.Error && it.Kind == "http") it.Status = State.Paused;
                Mgr.Resume(it);
                if (Cfg.ShowProgress && it.Status == State.Active) ShowProgress(it);
            }
        }
        void StopSel() { foreach (var it in Selected()) Mgr.Pause(it); }
        void StopAll() { foreach (var it in Mgr.Items.ToList()) Mgr.Pause(it); }
        void StartQueue() { Mgr.StartQueue(); Refresh_(); }
        void StopQueue() { Mgr.StopQueue(); Refresh_(); }

        void DeleteSel()
        {
            var sel = Selected();
            if (sel.Count == 0) return;
            using (var d = new ConfirmDeleteDialog(sel.Count))
                if (d.ShowDialog(this) == DialogResult.OK) foreach (var it in sel) Mgr.Remove(it, d.DeleteFiles);
        }

        void DeleteCompleted()
        {
            var done = Mgr.Items.Where(i => i.Status == State.Complete).ToList();
            if (done.Count > 0 && MessageBox.Show(this, "Remove " + done.Count + " completed downloads from the list?\nThe files stay on your disk.",
                    "Delete completed", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                foreach (var it in done) Mgr.Remove(it, false);
        }

        void RefreshAddress(Item it)
        {
            using (var d = new AddUrlDialog("", "Refresh download address", "Paste a fresh link for " + it.FileName + ". The download continues where it stopped."))
                if (d.ShowDialog(this) == DialogResult.OK) Mgr.ChangeUrl(it, d.Url);
        }

        void ShowOptions(int tab)
        {
            using (var d = new OptionsDialog(this, tab))
                if (d.ShowDialog(this) == DialogResult.OK) { Cfg.Save(); Mgr.ApplyGlobalLimit(); }
        }

        public void ShowProgress(Item it)
        {
            ProgressDialog d;
            if (!progress.TryGetValue(it.Id, out d) || d.IsDisposed)
            {
                d = progress[it.Id] = new ProgressDialog(this, it);
                d.FormClosed += (s, e) => progress.Remove(it.Id);
            }
            d.Show(); d.Activate();
        }

        void BatchFromClipboard()
        {
            string t = "";
            try { t = Clipboard.GetText(); } catch (System.Runtime.InteropServices.ExternalException) { }
            AddLinks(Regex.Matches(t, @"(?:https?|ftp)://\S+").Cast<Match>().Select(m => m.Value), null);
        }

        // ------------------------------------------------------------------ new downloads
        public void NewDownload(string url, Dictionary<string, string> headers, string filename, long size, string desc = "")
        {
            if (!Regex.IsMatch(url ?? "", @"^(https?|ftp)://", RegexOptions.IgnoreCase)) { MessageBox.Show(this, "That isn't a web address NexGet can download:\n" + url, "NexGet"); return; }
            headers = headers ?? new Dictionary<string, string>();
            stLeft.Text = "Connecting to " + new Uri(url).Host + "...";
            Task.Run(() => { try { return Manager.Probe(url, headers); } catch (Exception e) when (e is System.Net.WebException || e is IOException || e is NotSupportedException || e is UriFormatException) { return null; } })
                .ContinueWith(t => {
                    if (t.Result != null && t.Result.Mime.StartsWith("text/html") && string.IsNullOrEmpty(filename))
                    {   // a web page, not a file: treat it as a video page (YouTube, Vimeo, 1800+ sites)
                        NewVideo(new Dictionary<string, object> { { "pageUrl", url }, { "title", "" }, { "referrer", url } });
                        return;
                    }
                    var info = t.Result ?? new Manager.ProbeResult { Url = url, FileName = Fmt.NameFromUrl(url), Size = size, Resumable = true };
                    if (!string.IsNullOrEmpty(filename) && filename != "download") info.FileName = Fmt.SafeName(filename);
                    if (info.Mime.StartsWith("text/html") && string.IsNullOrEmpty(filename) && !Path.HasExtension(info.FileName)) info.FileName += ".html";
                    using (var d = new FileInfoDialog(Cfg, info.Url, info.FileName, info.Size, desc))
                    {
                        d.PopToFront = true;
                        if (d.ShowDialog(this) != DialogResult.OK) { Refresh_(); return; }
                        var it = Mgr.Add(d.Url, d.SavePath, headers: headers, size: info.Size, start: d.StartNow, description: d.Description, resumable: info.Resumable);
                        if (d.StartNow && Cfg.ShowProgress) ShowProgress(it);
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        public void NewVideo(Dictionary<string, object> p, string preselect = null)
        {
            Log.Write("video request: " + p.Str("pageUrl") + "  sniffed=" + string.Join(", ", p.List("media").OfType<Dictionary<string, object>>().Select(m => m.Str("type") + " " + m.Str("url").Substring(0, Math.Min(140, m.Str("url").Length)))));
            stLeft.Text = "Finding video qualities...";
            Video.Get(p).ContinueWith(t => {
                Refresh_();
                var a = t.Status == TaskStatus.RanToCompletion ? (Analysis)t.Result : null;
                if (a == null || a.Choices.Count == 0)
                {
                    var msg = a != null && a.Error.Length > 0 ? a.Error : t.Exception != null ? t.Exception.GetBaseException().Message : "The page hasn't loaded a video stream yet. Play the video for a few seconds, then try again.";
                    Notify("No downloadable video found", msg, ToolTipIcon.Warning);
                    return;
                }
                ShowVideoDialog(a, preselect);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        public void ShowVideoDialog(Analysis a, string choiceId)
        {
            using (var d = new VideoDialog(Cfg, a, choiceId))
            {
                d.PopToFront = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var c = d.Choice;
                var it = Mgr.Add(c.Url, d.SavePath, c.Kind, c.Headers, c.Size > 0 ? c.Size : -1, d.StartNow, a.Title, c.Fmt, true, c.Kind == "video" ? a.CookieFile : null);
                if (d.StartNow && Cfg.ShowProgress) ShowProgress(it);
            }
        }

        public void AddLinks(IEnumerable<string> links, Dictionary<string, string> headers)
        {
            var urls = links.Where(u => Regex.IsMatch(u, @"^(https?|ftp)://", RegexOptions.IgnoreCase)).Distinct().ToList();
            if (urls.Count == 0) { Notify("No links found", "There were no downloadable links.", ToolTipIcon.Info); return; }
            using (var d = new LinksDialog(urls))
            {
                d.PopToFront = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                foreach (var u in d.Selected)
                {
                    var name = Fmt.NameFromUrl(u);
                    Mgr.Add(u, Fmt.UniquePath(Cfg.Folder(Cats.For(name)), name), headers: headers, start: false);
                }
                if (d.StartNow) Mgr.StartQueue();
            }
        }

        public void OnBridge(string kind, Dictionary<string, object> p)
        {
            var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Referer", p.Str("referrer") }, { "Cookie", p.Str("cookies") }, { "User-Agent", p.Str("ua") } };
            switch (kind)
            {
                case "show": ShowMain(); if (p.Bool("setup")) new IntegrationDialog(this) { PopToFront = true }.Show(); break;
                case "download": if (p.Str("url").Length > 0) NewDownload(p.Str("url"), h, p.Str("filename"), (long)p.Num("size", -1)); break;
                case "video": if (p.Str("pageUrl").Length > 0) NewVideo(p); break;
                case "grab":
                    Choice c; var a = Video.Find(p.Str("pageUrl"), p.Str("id"), out c);
                    if (c != null) ShowVideoDialog(a, c.Id); else if (p.Str("pageUrl").Length > 0) NewVideo(p);
                    break;
                case "links": AddLinks(p.List("links").OfType<string>(), h); break;
            }
        }

        // ------------------------------------------------------------------ events
        void OnCompleted(Item it)
        {
            ProgressDialog pd;
            if (progress.TryGetValue(it.Id, out pd) && !pd.IsDisposed && pd.OpenWhenDone) Open(it.Path);
            if (Cfg.ShowComplete) { var d = new CompleteDialog(this, it) { PopToFront = true }; d.Show(); }
            else Notify("Download complete", it.FileName, ToolTipIcon.Info);
        }

        void OnQueueFinished()
        {
            Notify("Queue finished", "All queued downloads are done.", ToolTipIcon.Info);
            if (Cfg.WhenQueueDone != "nothing") new PowerDialog(Cfg.WhenQueueDone).Show();
        }

        void CheckSchedule()
        {
            if (Cfg.ScheduleAt.Length == 0) return;
            var now = DateTime.Now;
            var stamp = now.ToString("yyyy-MM-dd");
            if (now.ToString("HH:mm") == Cfg.ScheduleAt && lastScheduleRun != stamp)
            {
                lastScheduleRun = stamp;
                if (Mgr.Items.Any(i => i.Status == State.Queued)) { Mgr.StartQueue(); Notify("Scheduled queue started", "Downloading the queue now.", ToolTipIcon.Info); }
            }
        }

        public void Notify(string title, string text, ToolTipIcon icon)
        {
            tray.ShowBalloonTip(4000, title, string.IsNullOrEmpty(text) ? " " : text.Length > 240 ? text.Substring(0, 240) : text, icon);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (App.IsWindows) try { Native.AddClipboardFormatListener(Handle); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            Native.RoundCorners(this);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == Native.WM_CLIPBOARDUPDATE && Cfg.Clipboard) BeginInvoke(new Action(CheckClipboard));
        }

        void CheckClipboard()
        {
            string t;
            try { t = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : ""; } catch (System.Runtime.InteropServices.ExternalException) { return; }
            if (t == lastClip || !Regex.IsMatch(t, @"^(https?|ftp)://\S+$")) return;
            lastClip = t;
            if (Cfg.Captures(Fmt.NameFromUrl(t))) NewDownload(t, null, null, -1);
        }

        public void ShowMain() { Show(); Native.Front(this); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!quitting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; Hide();
                if (!trayHintShown) { trayHintShown = true; Notify("NexGet is still running", "It keeps catching downloads from the tray. Right-click the icon to exit.", ToolTipIcon.Info); }
                return;
            }
            base.OnFormClosing(e);
        }

        public void Quit()
        {
            quitting = true; tick.Stop();
            Mgr.Shutdown(); tray.Visible = false; tray.Dispose();
            Application.Exit();
        }

        // ------------------------------------------------------------------ shell helpers
        public static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception e) { MessageBox.Show("Couldn't open " + path + "\n" + e.Message, "NexGet"); }
        }
        public static void OpenWith(string path)
        {
            if (App.IsWindows) Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + path); else Open(path);
        }
        public static void OpenFolder(string path)
        {
            if (App.IsWindows) Process.Start("explorer.exe", File.Exists(path) ? "/select,\"" + path + "\"" : "\"" + Path.GetDirectoryName(path) + "\"");
            else Open(Path.GetDirectoryName(path));
        }
    }
}
