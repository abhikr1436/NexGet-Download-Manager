// Dialogs: Add URL, Download file info, Video quality, Progress, Complete, Options, Links, Delete, Power, About.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NexGet
{
    /// <summary>Flat rounded button; Primary = brand violet.</summary>
    sealed class NgButton : Button
    {
        public bool Primary;
        bool hover;
        public NgButton(string text, bool primary = false)
        {
            Text = text; Primary = primary; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
            Font = primary ? Theme.UiBold : Theme.UiFont; AutoSize = false; Height = 32; Width = Math.Max(96, TextRenderer.MeasureText(text, Font).Width + 32);
            Margin = new Padding(8, 0, 0, 0); UseVisualStyleBackColor = false; BackColor = Color.Transparent;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prepare(g);
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Surface)) g.FillRectangle(bg, ClientRectangle);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill = !Enabled ? Theme.Wash : Primary ? (hover ? Theme.BrandDark : Theme.Brand) : (hover ? Theme.BrandSofter : Theme.Surface);
            using (var p = Theme.Round(r, 6))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                if (!Primary) using (var pen = new Pen(Focused ? Theme.Brand : Theme.Line)) g.DrawPath(pen, p);
                else if (Focused) using (var pen = new Pen(Theme.BrandDark, 2)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, !Enabled ? Theme.Muted : Primary ? Color.White : Theme.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    class NgDialog : Form
    {
        public bool PopToFront;
        protected readonly TableLayoutPanel Body = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20, 18, 20, 8) };
        protected readonly FlowLayoutPanel Footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(20, 10, 20, 16), BackColor = Theme.Surface };

        public NgDialog(string title, int width = 560)
        {
            Text = title; Icon = Theme.AppIcon; Font = Theme.UiFont; BackColor = Theme.Surface; ForeColor = Theme.Ink;
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = true; ClientSize = new Size(width, 200);
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(Body); Controls.Add(Footer);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Native.RoundCorners(this); if (PopToFront) Native.Front(this); }

        /// <summary>Fit the window height to its content (the width is fixed per dialog).</summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            int w = ClientSize.Width;
            ClientSize = new Size(w, Body.GetPreferredSize(new Size(w, 0)).Height + Footer.GetPreferredSize(new Size(w, 0)).Height);
            if (StartPosition == FormStartPosition.CenterScreen) CenterToScreen();
        }

        protected Label AddRow(string label, Control c, int height = 0)
        {
            var l = new Label { Text = label, AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 9, 14, 0) };
            c.Anchor = AnchorStyles.Left | AnchorStyles.Right; c.Margin = new Padding(0, 4, 0, 4);
            if (height > 0) c.Height = height;
            Body.Controls.Add(l); Body.Controls.Add(c);
            return l;
        }

        protected void AddWide(Control c) { c.Margin = new Padding(0, 6, 0, 6); if (!c.AutoSize) c.Anchor = AnchorStyles.Left | AnchorStyles.Right; Body.Controls.Add(c); Body.SetColumnSpan(c, 2); }

        protected NgButton AddButton(string text, bool primary, DialogResult result, Action onClick = null)
        {
            var b = new NgButton(text, primary);
            b.Click += (s, e) => { if (onClick != null) onClick(); if (result != DialogResult.None) { DialogResult = result; Close(); } };
            Footer.Controls.Add(b);
            if (primary) AcceptButton = b;
            if (result == DialogResult.Cancel) CancelButton = b;
            return b;
        }

        protected static Label Note(string text) { return new Label { Text = text, AutoSize = true, ForeColor = Theme.Muted, MaximumSize = new Size(520, 0) }; }

        protected TextBox PathBox(string value, bool folder, string pickTitle, out Control row)
        {
            var box = new TextBox { Text = value, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 4, 0, 0) };
            var browse = new NgButton("Browse...") { Width = 92, Margin = new Padding(8, 0, 0, 0) };
            var t = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), Padding = new Padding(0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.Controls.Add(box, 0, 0); t.Controls.Add(browse, 1, 0);
            browse.Click += (s, e) => {
                if (folder) { using (var d = new FolderBrowserDialog { SelectedPath = box.Text, Description = pickTitle }) if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath; }
                else using (var d = new SaveFileDialog { FileName = Path.GetFileName(box.Text), InitialDirectory = SafeDir(box.Text), Title = pickTitle, OverwritePrompt = false })
                        if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.FileName;
            };
            row = t; return box;
        }

        static string SafeDir(string path) { try { var d = Path.GetDirectoryName(path); return Directory.Exists(d) ? d : ""; } catch (ArgumentException) { return ""; } }
    }

    sealed class AddUrlDialog : NgDialog
    {
        readonly TextBox box = new TextBox();
        public string Url { get { return box.Text.Trim(); } }
        public AddUrlDialog(string initial, string title = "Add URL", string hint = "Paste the address of the file you want to download.") : base(title, 600)
        {
            AddWide(Note(hint));
            box.Text = initial; AddRow("Address", box);
            AddButton("Cancel", false, DialogResult.Cancel);
            AddButton("Download", true, DialogResult.None, () => {
                if (System.Text.RegularExpressions.Regex.IsMatch(Url, @"^(https?|ftp)://\S+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) { DialogResult = DialogResult.OK; Close(); }
                else { box.Focus(); box.SelectAll(); }
            });
            Shown += (s, e) => { box.Focus(); box.SelectAll(); };
        }
    }

    sealed class FileInfoDialog : NgDialog
    {
        readonly TextBox url = new TextBox(), desc = new TextBox(), save;
        readonly ComboBox cat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly CheckBox remember = new CheckBox { AutoSize = true };
        readonly Config cfg;
        public bool StartNow;
        public string Url { get { return url.Text.Trim(); } }
        public string SavePath { get { return save.Text.Trim(); } }
        public string Description { get { return desc.Text; } }

        public FileInfoDialog(Config cfg, string address, string fileName, long size, string description) : base("Download file info", 640)
        {
            this.cfg = cfg;
            var head = new Label { Text = fileName, Font = Theme.Title, AutoSize = true, MaximumSize = new Size(580, 0), Margin = new Padding(0, 0, 0, 2) };
            AddWide(head);
            AddWide(Note(size > 0 ? Fmt.Size(size) + "  ·  " + SafeHost(address) : "Size unknown  ·  " + SafeHost(address)));
            url.Text = address; AddRow("Address", url);
            cat.Items.AddRange(Cats.All); var c0 = Cats.For(fileName); cat.SelectedItem = c0; AddRow("Category", cat);
            Control saveRow; save = PathBox(Fmt.UniquePath(cfg.Folder(c0), fileName), false, "Save as", out saveRow); AddRow("Save as", saveRow);
            remember.Text = "Always save " + c0 + " files in this folder"; AddRow("", remember);
            desc.Text = description; AddRow("Description", desc);
            cat.SelectedIndexChanged += (s, e) => {
                save.Text = Fmt.UniquePath(cfg.Folder((string)cat.SelectedItem), Path.GetFileName(save.Text));
                remember.Text = "Always save " + cat.SelectedItem + " files in this folder";
            };
            AddButton("Cancel", false, DialogResult.Cancel);
            AddButton("Download later", false, DialogResult.OK, () => Finish(false));
            AddButton("Start download", true, DialogResult.OK, () => Finish(true));
        }

        static string SafeHost(string u) { try { return new Uri(u).Host; } catch (UriFormatException) { return u; } }

        void Finish(bool start)
        {
            StartNow = start;
            if (remember.Checked) { cfg.CatDirs[(string)cat.SelectedItem] = Path.GetDirectoryName(SavePath); cfg.Save(); }
        }
    }

    sealed class VideoDialog : NgDialog
    {
        readonly ListBox box = new ListBox { IntegralHeight = false, Height = 210, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 34, BorderStyle = BorderStyle.FixedSingle };
        readonly TextBox save;
        readonly Analysis a;
        readonly Config cfg;
        public bool StartNow;
        public Choice Choice { get { return a.Choices[Math.Max(0, box.SelectedIndex)]; } }
        public string SavePath { get { return save.Text.Trim(); } }

        public VideoDialog(Config cfg, Analysis a, string choiceId) : base("Download video", 620)
        {
            this.cfg = cfg; this.a = a;
            AddWide(new Label { Text = a.Title, Font = Theme.Title, AutoSize = true, MaximumSize = new Size(560, 0) });
            AddWide(Note("Choose a quality."));
            foreach (var c in a.Choices) box.Items.Add(c.Label);
            box.DrawItem += DrawChoice;
            AddWide(box);
            Control row; save = PathBox("", false, "Save video as", out row); AddRow("Save as", row);
            box.SelectedIndexChanged += (s, e) => UpdatePath();
            box.SelectedIndex = Math.Max(0, a.Choices.FindIndex(c => c.Id == choiceId));
            box.DoubleClick += (s, e) => { StartNow = true; DialogResult = DialogResult.OK; Close(); };
            AddButton("Cancel", false, DialogResult.Cancel);
            AddButton("Download later", false, DialogResult.OK, () => StartNow = false);
            AddButton("Start download", true, DialogResult.OK, () => StartNow = true);
        }

        void UpdatePath()
        {
            var c = Choice;
            var name = c.FileName ?? Fmt.SafeName(a.Title) + "." + (c.Ext.Length > 0 ? c.Ext : "mp4");
            save.Text = Fmt.UniquePath(cfg.Folder(Cats.For(name)), name);
        }

        void DrawChoice(object s, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.BrandSoft : Theme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
            var c = a.Choices[e.Index];
            var icon = Icons.Get(c.Label.StartsWith("Audio") ? "Music" : "Video", 20, sel ? Theme.Brand : Theme.Muted);
            e.Graphics.DrawImage(icon, e.Bounds.Left + 10, e.Bounds.Top + (e.Bounds.Height - 20) / 2);
            var parts = c.Label.Split(new[] { "   " }, 2, StringSplitOptions.None);
            var r = new Rectangle(e.Bounds.Left + 40, e.Bounds.Top, e.Bounds.Width - 50, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, parts[0], sel ? Theme.UiBold : Theme.UiFont, r, Theme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (parts.Length > 1) TextRenderer.DrawText(e.Graphics, parts[1], Theme.UiFont, r, Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }

    /// <summary>Per-connection map: which parts of the file are already downloaded (aria2 bitfield).</summary>
    sealed class SegmentBar : Control
    {
        public Item Item;
        public SegmentBar() { DoubleBuffered = true; Height = 18; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(Theme.Wash)) g.FillRectangle(b, r);
            if (Item != null)
            {
                using (var b = new SolidBrush(Theme.Brand))
                {
                    var bits = Item.Bitfield ?? ""; int n = Item.NumPieces;
                    if (bits.Length > 0 && n > 0)
                    {
                        float w = (float)(Width - 2) / n;
                        int start = -1;
                        for (int i = 0; i <= n; i++)
                        {
                            bool on = i < n && i / 4 < bits.Length && ((Convert.ToInt32(bits[i / 4].ToString(), 16) >> (3 - i % 4)) & 1) == 1;
                            if (on && start < 0) start = i;
                            if (!on && start >= 0) { g.FillRectangle(b, 1 + start * w, 1, Math.Max(1, (i - start) * w), Height - 2); start = -1; }
                        }
                    }
                    else if (Item.Size > 0) g.FillRectangle(b, 1, 1, (float)((Width - 2) * Item.Percent / 100), Height - 2);
                }
            }
            using (var p = new Pen(Theme.Line)) g.DrawRectangle(p, r);
        }
    }

    sealed class ProgressBarX : Control
    {
        public double Value; public Color Fill = Theme.Brand;
        public ProgressBarX() { DoubleBuffered = true; Height = 12; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prepare(g);
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Surface)) g.FillRectangle(b, ClientRectangle);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(Theme.Line)) using (var p = Theme.Round(r, r.Height / 2)) g.FillPath(b, p);
            var w = (float)(r.Width * Math.Max(0, Math.Min(100, Value)) / 100);
            if (w > 0) using (var b = new SolidBrush(Fill)) using (var p = Theme.Round(new RectangleF(0, 0, Math.Max(w, r.Height), r.Height), r.Height / 2)) g.FillPath(b, p);
        }
    }

    sealed class ProgressDialog : Form
    {
        readonly MainForm main; readonly Item it;
        readonly Label lUrl = Val(), lStatus = Val(), lSize = Val(), lDone = Val(), lRate = Val(), lLeft = Val(), lResume = Val();
        readonly ProgressBarX bar = new ProgressBarX { Dock = DockStyle.Fill };
        readonly SegmentBar seg = new SegmentBar { Dock = DockStyle.Top };
        readonly ListView conns = new ListView { View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, Height = 140, Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle };
        readonly Panel details = new Panel { Dock = DockStyle.Bottom, Height = 196, Padding = new Padding(18, 4, 18, 0) };
        readonly NgButton toggle = new NgButton("Hide details"), pause = new NgButton("Pause", true), cancel = new NgButton("Cancel");
        readonly CheckBox showDone = new CheckBox { Text = "Show the download complete window", AutoSize = true }, openDone = new CheckBox { Text = "Open the file when it's done", AutoSize = true };
        readonly Timer t = new Timer { Interval = 1000 };
        public bool OpenWhenDone { get { return openDone.Checked; } }

        static Label Val() { return new Label { AutoSize = true, AutoEllipsis = true, Margin = new Padding(0, 4, 0, 4), ForeColor = Theme.Ink }; }

        public ProgressDialog(MainForm main, Item it)
        {
            this.main = main; this.it = it;
            Icon = Theme.AppIcon; Font = Theme.UiFont; BackColor = Theme.Surface; AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(580, 560);

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
            var st = new TabPage("Download status") { BackColor = Theme.Surface, Padding = new Padding(14) };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Action<string, Label> row = (k, v) => { grid.Controls.Add(new Label { Text = k, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 4, 0, 4) }); v.MaximumSize = new Size(360, 0); grid.Controls.Add(v); };
            row("Address", lUrl); row("Status", lStatus); row("File size", lSize); row("Downloaded", lDone);
            row("Transfer rate", lRate); row("Time left", lLeft); row("Resume support", lResume);
            var barHost = new Panel { Height = 26, Dock = DockStyle.Top, Padding = new Padding(0, 8, 0, 6) }; barHost.Controls.Add(bar);
            grid.Controls.Add(barHost); grid.SetColumnSpan(barHost, 2);
            st.Controls.Add(grid); tabs.TabPages.Add(st);

            var lim = new TabPage("Speed limiter") { BackColor = Theme.Surface, Padding = new Padding(18) };
            var limOn = new CheckBox { Text = "Limit this download's speed", AutoSize = true, Checked = it.LimitKb > 0, Top = 10, Left = 4 };
            var limVal = new NumericUpDown { Minimum = 10, Maximum = 1000000, Value = it.LimitKb > 0 ? it.LimitKb : 500, Width = 110, Top = 42, Left = 4 };
            var limLbl = new Label { Text = "KB/s", AutoSize = true, Top = 46, Left = 122 };
            var apply = new NgButton("Apply") { Top = 80, Left = 4 };
            apply.Click += (s, e) => main.Mgr.SetLimit(it, limOn.Checked ? (int)limVal.Value : 0);
            lim.Controls.AddRange(new Control[] { limOn, limVal, limLbl, apply }); tabs.TabPages.Add(lim);

            var done = new TabPage("When it's done") { BackColor = Theme.Surface, Padding = new Padding(18) };
            showDone.Checked = main.Cfg.ShowComplete; showDone.Top = 10; showDone.Left = 4; openDone.Top = 40; openDone.Left = 4;
            showDone.CheckedChanged += (s, e) => { main.Cfg.ShowComplete = showDone.Checked; main.Cfg.Save(); };
            done.Controls.AddRange(new Control[] { showDone, openDone }); tabs.TabPages.Add(done);

            conns.Columns.Add("N.", 44); conns.Columns.Add("Speed", 100); conns.Columns.Add("Info", 340);
            details.Controls.Add(conns);
            details.Controls.Add(new Panel { Height = 8, Dock = DockStyle.Top });
            details.Controls.Add(seg);
            details.Controls.Add(new Label { Text = "Parts of the file already downloaded, and live connections", Dock = DockStyle.Top, Height = 24, ForeColor = Theme.Muted });

            var foot = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 58, Padding = new Padding(14, 12, 18, 12) };
            foot.Controls.AddRange(new Control[] { cancel, pause });
            toggle.Margin = new Padding(0, 0, 0, 0);
            var left = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 140, Padding = new Padding(18, 12, 0, 0) }; left.Controls.Add(toggle);
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 58 }; footer.Controls.Add(foot); footer.Controls.Add(left);
            var tabHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 14, 14, 4) }; tabHost.Controls.Add(tabs);
            Controls.Add(tabHost); Controls.Add(details); Controls.Add(footer);

            toggle.Click += (s, e) => { details.Visible = !details.Visible; toggle.Text = details.Visible ? "Hide details" : "Show details"; ClientSize = new Size(ClientSize.Width, details.Visible ? 560 : 364); };
            pause.Click += (s, e) => { if (it.Status == State.Active) main.Mgr.Pause(it); else main.Mgr.Resume(it); };
            cancel.Click += (s, e) => { main.Mgr.Pause(it); Close(); };
            main.Mgr.Changed += Update_;
            t.Tick += (s, e) => Conns(); t.Start();
            Shown += (s, e) => Native.Explorer(conns);
            Update_(); Conns();
        }

        void Update_()
        {
            if (IsDisposed) return;
            if (!main.Mgr.Items.Contains(it)) { Close(); return; }
            Text = (it.Size > 0 ? it.Percent.ToString("0") + "% " : "") + it.FileName;
            lUrl.Text = it.Url;
            lStatus.Text = it.Status == State.Active ? (it.Speed > 0 ? "Receiving data..." : "Connecting...") : it.Status == State.Error ? "Error: " + it.Error : it.Status.ToString();
            lStatus.ForeColor = it.Status == State.Error ? Theme.Error : Theme.Ink;
            lSize.Text = it.Size > 0 ? Fmt.Size(it.Size) : "Unknown";
            lDone.Text = (it.Done > 0 ? Fmt.Size(it.Done) : "0 B") + (it.Size > 0 ? "  (" + it.Percent.ToString("0.00") + "%)" : "");
            lRate.Text = it.Status == State.Active ? Fmt.Speed(it.Speed) : "";
            lLeft.Text = Fmt.Time(it.Eta);
            lResume.Text = it.Resumable ? "Yes" : "No";
            bar.Value = it.Percent; bar.Fill = it.Status == State.Paused ? Theme.Paused : Theme.Brand; bar.Invalidate();
            seg.Item = it; seg.Invalidate();
            pause.Text = it.Status == State.Active ? "Pause" : "Resume";
            if (it.Status == State.Complete) Close();
        }

        void Conns()
        {
            if (!details.Visible || it.Kind != "http") return;
            var servers = main.Mgr.Servers(it);
            conns.BeginUpdate(); conns.Items.Clear();
            for (int i = 0; i < servers.Count; i++)
                conns.Items.Add(new ListViewItem(new[] { (i + 1).ToString(), Fmt.Speed((long)servers[i].Num("downloadSpeed")), "Receiving data from " + servers[i].Str("currentUri") }));
            if (servers.Count == 0 && it.Status == State.Active) conns.Items.Add(new ListViewItem(new[] { "", "", it.Connections + " connections" }));
            conns.EndUpdate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { main.Mgr.Changed -= Update_; t.Dispose(); base.OnFormClosed(e); }
    }

    sealed class CompleteDialog : NgDialog
    {
        public CompleteDialog(MainForm main, Item it) : base("Download complete", 560)
        {
            ShowInTaskbar = true; TopMost = false;
            AddWide(new Label { Text = it.FileName, Font = Theme.Title, AutoSize = true, MaximumSize = new Size(500, 0) });
            AddWide(Note(Fmt.Size(it.Size) + "  ·  saved in " + Path.GetDirectoryName(it.Path)));
            var never = new CheckBox { Text = "Don't show this again", AutoSize = true };
            never.CheckedChanged += (s, e) => { main.Cfg.ShowComplete = !never.Checked; main.Cfg.Save(); };
            AddWide(never);
            AddButton("Close", false, DialogResult.Cancel);
            AddButton("Open folder", false, DialogResult.OK, () => MainForm.OpenFolder(it.Path));
            AddButton("Open with...", false, DialogResult.OK, () => MainForm.OpenWith(it.Path));
            AddButton("Open", true, DialogResult.OK, () => MainForm.Open(it.Path));
        }
    }

    sealed class ConfirmDeleteDialog : NgDialog
    {
        readonly CheckBox files = new CheckBox { Text = "Also delete the files from disk", AutoSize = true };
        public bool DeleteFiles { get { return files.Checked; } }
        public ConfirmDeleteDialog(int n) : base("Delete downloads", 460)
        {
            AddWide(new Label { Text = n == 1 ? "Remove this download from the list?" : "Remove " + n + " downloads from the list?", AutoSize = true, Font = Theme.UiBold });
            AddWide(files);
            AddButton("Cancel", false, DialogResult.Cancel);
            AddButton("Delete", true, DialogResult.OK);
        }
    }

    sealed class LinksDialog : NgDialog
    {
        readonly ListView lv = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Height = 340, BorderStyle = BorderStyle.FixedSingle };
        public bool StartNow;
        public List<string> Selected { get { return lv.CheckedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToList(); } }
        public LinksDialog(List<string> urls) : base("Download links", 820)
        {
            AddWide(Note("Tick the files to download. Web pages are unticked."));
            lv.Columns.Add("File name", 260); lv.Columns.Add("Type", 70); lv.Columns.Add("Address", 440);
            foreach (var u in urls)
            {
                var name = Fmt.NameFromUrl(u); var ext = Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
                lv.Items.Add(new ListViewItem(new[] { name, ext, u }) { Tag = u, Checked = ext.Length > 0 && !new[] { "HTML", "HTM", "PHP", "ASP", "ASPX", "JSP" }.Contains(ext) });
            }
            AddWide(lv);
            Shown += (s, e) => Native.Explorer(lv);
            AddButton("Cancel", false, DialogResult.Cancel);
            AddButton("Add to queue", false, DialogResult.OK, () => StartNow = false);
            AddButton("Download now", true, DialogResult.OK, () => StartNow = true);
            var all = new NgButton("Tick all"); all.Click += (s, e) => { foreach (ListViewItem i in lv.Items) i.Checked = true; };
            var none = new NgButton("Untick all"); none.Click += (s, e) => { foreach (ListViewItem i in lv.Items) i.Checked = false; };
            Footer.Controls.Add(none); Footer.Controls.Add(all);
        }
    }

    sealed class OptionsDialog : NgDialog
    {
        public OptionsDialog(MainForm main, int tab) : base("Options", 640)
        {
            var c = main.Cfg;
            var tabs = new TabControl { Height = 380, Anchor = AnchorStyles.Left | AnchorStyles.Right, Padding = new Point(14, 6) };
            Func<string, TableLayoutPanel> page = title => {
                var p = new TabPage(title) { BackColor = Theme.Surface, Padding = new Padding(16) };
                var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
                t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                p.Controls.Add(t); tabs.TabPages.Add(p); return t;
            };
            Action<TableLayoutPanel, string, Control> row = (t, k, v) => {
                t.Controls.Add(new Label { Text = k, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 14, 0) });
                v.Margin = new Padding(0, 4, 0, 4); t.Controls.Add(v);
            };
            Action<TableLayoutPanel, Control> wide = (t, v) => { v.Margin = new Padding(0, 6, 0, 6); t.Controls.Add(v); t.SetColumnSpan(v, 2); };

            var gen = page("General");
            var clip = new CheckBox { Text = "Catch download links I copy", AutoSize = true, Checked = c.Clipboard };
            var prog = new CheckBox { Text = "Show the progress window when a download starts", AutoSize = true, Checked = c.ShowProgress };
            var comp = new CheckBox { Text = "Show the download complete window", AutoSize = true, Checked = c.ShowComplete };
            var startup = new CheckBox { Text = "Start NexGet when I sign in to Windows", AutoSize = true, Checked = Startup.Enabled };
            wide(gen, clip); wide(gen, prog); wide(gen, comp); wide(gen, startup);

            var con = page("Connection");
            var conns = new NumericUpDown { Minimum = 1, Maximum = 16, Value = c.Connections, Width = 80 };
            var maxa = new NumericUpDown { Minimum = 1, Maximum = 20, Value = c.MaxActive, Width = 80 };
            var lim = new NumericUpDown { Minimum = 0, Maximum = 10000000, Value = c.SpeedLimitKb, Width = 110 };
            row(con, "Connections per download", conns);
            row(con, "Downloads at once in the queue", maxa);
            row(con, "Total speed limit (KB/s, 0 = none)", lim);
            wide(con, Note("More connections split each file into more parts that download in parallel. 8 suits most sites; some servers limit connections."));

            var sv = page("Save to");
            Control r0; var baseDir = PathBox(c.BaseDir, true, "Default download folder", out r0); row(sv, "Default folder", r0);
            var dirs = new Dictionary<string, TextBox>();
            foreach (var cat in Cats.All.Where(x => x != "General")) { Control r; dirs[cat] = PathBox(c.Folder(cat), true, cat + " folder", out r); row(sv, cat, r); }

            var sch = page("Scheduler");
            var schOn = new CheckBox { Text = "Start the queue every day at", AutoSize = true, Checked = c.ScheduleAt.Length > 0 };
            var at = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 90 };
            DateTime parsed; at.Value = DateTime.TryParse(c.ScheduleAt, out parsed) ? parsed : DateTime.Today.AddHours(2);
            row(sch, "", schOn); row(sch, "Time", at);
            var after = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            after.Items.AddRange(new object[] { "Do nothing", "Put the computer to sleep", "Shut down the computer" });
            after.SelectedIndex = c.WhenQueueDone == "sleep" ? 1 : c.WhenQueueDone == "shutdown" ? 2 : 0;
            row(sch, "When the queue finishes", after);
            wide(sch, Note("Add downloads to the queue with \"Download later\". Sleep and shut down give you 60 seconds to cancel."));

            var br = page("Browser");
            var exts = new TextBox { Multiline = true, Height = 110, Text = c.CaptureExts, ScrollBars = ScrollBars.Vertical };
            wide(br, Note("Downloads of these file types are taken over from your browser:"));
            wide(br, exts); exts.Dock = DockStyle.Fill;
            var reset = new NgButton("Reset to defaults"); reset.Click += (s, e) => exts.Text = Config.DefaultCapture;
            var setup = new NgButton("Set up browser integration...", true); setup.Click += (s, e) => new IntegrationDialog(main).ShowDialog(this);
            var fl = new FlowLayoutPanel { AutoSize = true }; reset.Margin = new Padding(0); fl.Controls.Add(reset); fl.Controls.Add(setup); wide(br, fl);

            tabs.SelectedIndex = Math.Max(0, Math.Min(tab, tabs.TabCount - 1));
            Body.Controls.Add(tabs); Body.SetColumnSpan(tabs, 2); Body.Padding = new Padding(14, 14, 14, 0);

            AddButton("Cancel", false, DialogResult.Cancel);
            AddButton("Save", true, DialogResult.OK, () => {
                c.Clipboard = clip.Checked; c.ShowProgress = prog.Checked; c.ShowComplete = comp.Checked;
                c.Connections = (int)conns.Value; c.MaxActive = (int)maxa.Value; c.SpeedLimitKb = (int)lim.Value;
                c.BaseDir = baseDir.Text.Trim();
                c.CatDirs = dirs.Where(d => d.Value.Text.Trim().Length > 0 && d.Value.Text.Trim() != Path.Combine(c.BaseDir, d.Key)).ToDictionary(d => d.Key, d => d.Value.Text.Trim());
                c.ScheduleAt = schOn.Checked ? at.Value.ToString("HH:mm") : "";
                c.WhenQueueDone = new[] { "nothing", "sleep", "shutdown" }[after.SelectedIndex];
                c.CaptureExts = string.Join(" ", exts.Text.ToUpperInvariant().Split(new[] { ' ', '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries));
                Startup.Enabled = startup.Checked;
            });
        }
    }

    /// <summary>Sleep / shut down after the queue, with a 60-second chance to cancel.</summary>
    sealed class PowerDialog : NgDialog
    {
        int left = 60;
        public PowerDialog(string action) : base(action == "sleep" ? "Going to sleep" : "Shutting down", 440)
        {
            TopMost = true;
            var msg = new Label { AutoSize = true, Font = Theme.UiBold };
            AddWide(msg);
            Func<string> text = () => "The queue has finished. " + (action == "sleep" ? "Sleeping" : "Shutting down") + " in " + left + " seconds.";
            msg.Text = text();
            var t = new Timer { Interval = 1000 };
            Action go = () => {
                t.Stop(); Close();
                if (action == "sleep") Application.SetSuspendState(PowerState.Suspend, false, false);
                else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
            };
            t.Tick += (s, e) => { left--; msg.Text = text(); if (left <= 0) go(); };
            AddButton("Cancel", false, DialogResult.Cancel, () => t.Stop());
            AddButton(action == "sleep" ? "Sleep now" : "Shut down now", true, DialogResult.None, go);
            FormClosed += (s, e) => t.Dispose();
            t.Start();
        }
    }

    sealed class AboutDialog : NgDialog
    {
        public AboutDialog() : base("About NexGet", 480)
        {
            var logo = new PictureBox { Width = 72, Height = 72, Margin = new Padding(0, 0, 16, 0) };
            var bmp = new Bitmap(72, 72); using (var g = Graphics.FromImage(bmp)) Theme.DrawLogo(g, new RectangleF(0, 0, 72, 72)); logo.Image = bmp;
            var text = new Label { AutoSize = true, Text = "NexGet Download Manager\nVersion " + App.Version, Font = Theme.Title };
            Body.Controls.Add(logo); Body.Controls.Add(text);
            AddWide(Note("Multi-part downloads by aria2, video by yt-dlp, joining by FFmpeg, QuickJS for YouTube. All open source; licences are in the licenses folder."));
            AddButton("Close", true, DialogResult.OK);
        }
    }

    static class Startup
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool Enabled
        {
            get { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key)) return k != null && k.GetValue("NexGet") != null; }
            set
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key))
                    if (value) k.SetValue("NexGet", "\"" + Application.ExecutablePath + "\" --tray"); else k.DeleteValue("NexGet", false);
            }
        }
    }
}
