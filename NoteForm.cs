using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Memento
{
    internal sealed class PlainEditor : RichTextBox
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x302) // WM_PASTE: never import fonts or colors from another application.
            {
                try { if (Clipboard.ContainsText()) SelectedText = Clipboard.GetText(TextDataFormat.UnicodeText); }
                catch (ExternalException) { }
                return;
            }
            base.WndProc(ref m);
        }
    }

    internal sealed class GlyphButton : Button
    {
        public string Glyph;
        public bool Pinned;
        private bool hovered;
        public GlyphButton(string glyph, string label)
        {
            Glyph = glyph; AccessibleName = label; AccessibleDescription = label;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            if (hovered || Focused) using (Brush fill = new SolidBrush(Color.FromArgb(22, ForeColor))) g.FillRectangle(fill, 3, 5, Width - 6, Height - 10);
            float x = Width / 2f, y = Height / 2f;
            g.TranslateTransform(x, y); g.ScaleTransform(Height / 40f, Height / 40f); x = 0; y = 0;
            using (Pen p = new Pen(ForeColor, 1.6f))
            using (Brush b = new SolidBrush(ForeColor))
            {
                if (Glyph == "+") { g.DrawLine(p, x - 5, y, x + 5, y); g.DrawLine(p, x, y - 5, x, y + 5); }
                else if (Glyph == "x") { g.DrawLine(p, x - 4, y - 4, x + 4, y + 4); g.DrawLine(p, x - 4, y + 4, x + 4, y - 4); }
                else if (Glyph == "menu") { for (int i = -1; i <= 1; i++) g.FillEllipse(b, x + i * 5 - 1.3f, y - 1.3f, 2.6f, 2.6f); }
                else
                {
                    PointF[] points = { new PointF(x - 4, y - 6), new PointF(x + 4, y - 6), new PointF(x + 3, y), new PointF(x + 5, y + 3), new PointF(x - 5, y + 3), new PointF(x - 3, y) };
                    if (Pinned) g.FillPolygon(b, points); else g.DrawPolygon(p, points);
                    g.DrawLine(p, x, y + 3, x, y + 8);
                }
            }
        }
    }

    internal sealed class NoteForm : Form
    {
        public readonly NoteData Note;
        private readonly NotesContext context;
        private readonly Panel header, body;
        private readonly PlainEditor editor;
        private readonly Label brand;
        private readonly GlyphButton add, pin, options, close;
        private readonly ToolTip toolTip;
        private readonly ContextMenuStrip menu;
        private bool restoring, initialized, disposed;
        private Palette palette;
        private int fixedWidth = GridMetrics.LogicalWidth;
        private int MinimumNoteHeight { get { return (int)Math.Round((Note.IsTitle ? 150 : 300) * Math.Max(96, DeviceDpi) / 96.0); } }
        public NoteForm(NotesContext context, NoteData note, Icon icon)
        {
            this.context = context; Note = note; restoring = true;
            Text = "Memento · " + note.Title; Icon = icon; ShowInTaskbar = false; ShowIcon = false;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            MinimizeBox = false; MaximizeBox = false;
            MinimumSize = new Size(GridMetrics.LogicalWidth, note.IsTitle ? 150 : 300);
            MaximumSize = new Size(GridMetrics.LogicalWidth, 16384);
            Size = new Size(GridMetrics.LogicalWidth, note.IsTitle ? 150 : 300);
            TopMost = note.TopMost; KeyPreview = true; DoubleBuffered = true;
            header = new Panel { Height = 40, Dock = DockStyle.Top, Cursor = Cursors.SizeAll };
            body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(17, 14, 17, 4) };
            editor = new PlainEditor { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DetectUrls = false, WordWrap = true, ScrollBars = RichTextBoxScrollBars.None, Font = new Font("Segoe UI", note.FontSize, note.IsTitle ? FontStyle.Bold : FontStyle.Regular), AcceptsTab = true, HideSelection = false, AccessibleName = note.IsTitle ? "Título da coluna" : "Conteúdo da nota", AccessibleDescription = "Escreva sua nota. A altura acompanha o texto e o salvamento é automático.", MaxLength = 1000000, Text = note.Text, TabIndex = 0 };
            brand = new Label { Text = "MEMENTO", AutoSize = false, AutoEllipsis = true, Font = new Font("Segoe UI", 8, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.SizeAll, Left = 47, Top = 0, Height = 40, Width = 150 };
            add = new GlyphButton("+", "Nova nota"); pin = new GlyphButton("pin", "Manter acima dos outros aplicativos");
            options = new GlyphButton("menu", "Cores e opções"); close = new GlyphButton("x", "Guardar nota na bandeja");
            toolTip = new ToolTip { InitialDelay = 450, ReshowDelay = 100, AutoPopDelay = 6000 };
            foreach (GlyphButton button in new[] { add, pin, options, close })
            { button.Size = new Size(34, 40); header.Controls.Add(button); toolTip.SetToolTip(button, button.AccessibleName); }
            add.Left = 7;
            pin.Anchor = options.Anchor = close.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            pin.Top = options.Top = close.Top = 0;
            header.Controls.Add(brand); body.Controls.Add(editor);
            Controls.Add(body); Controls.Add(header);
            menu = new ContextMenuStrip { Font = new Font("Segoe UI", 10) };
            options.Click += delegate { BuildMenu(); menu.Show(options, new Point(0, options.Height)); };
            add.Click += delegate { context.CreateNote(this); };
            close.Click += delegate { context.HideNote(Note); };
            pin.Click += delegate { SetPinned(!Note.TopMost); };
            header.MouseDown += DragHeader; brand.MouseDown += DragHeader;
            Activated += delegate { if (context != null) context.MarkActive(Note); };
            editor.TextChanged += delegate
            {
                if (!initialized) return;
                Note.Text = editor.Text; Note.Updated = DateTime.UtcNow.ToString("o"); Text = "Memento · " + Note.Title;
                if (context != null) { context.MarkActive(Note); context.Changed(); context.RequestLayout(this); }
            };
            editor.Leave += delegate { if (context != null) context.Flush(false); };
            ClientSizeChanged += delegate { LayoutButtons(); if (!restoring && initialized && context != null) context.RequestLayout(this); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (context == null) return;
                if (!context.Exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; context.HideNote(Note); }
                else context.Flush(false);
            };
            RefreshKind(); ApplyPalette(); LayoutButtons(); Location = new Point(note.X, note.Y); initialized = true; restoring = false;
            Shown += delegate { ApplyAlignment(); if (context != null) context.RequestLayout(this); };
        }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle = (cp.ExStyle | 0x80) & ~0x40000; // WS_EX_TOOLWINDOW, never WS_EX_APPWINDOW.
                cp.Style &= ~(0x20000 | 0x10000 | 0x40000); // No minimize, maximize or manual resize.
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        private void LayoutButtons()
        {
            if (header == null || close == null || brand == null) return;
            float scale = Math.Max(96, DeviceDpi) / 96f;
            int inset = (int)Math.Round(7 * scale);
            int buttonWidth = (int)Math.Round(34 * scale), headerHeight = (int)Math.Round(40 * scale);
            header.Height = headerHeight;
            foreach (GlyphButton b in new[] { add, pin, options, close }) b.SetBounds(0, 0, buttonWidth, headerHeight);
            close.Left = header.ClientSize.Width - inset - close.Width;
            options.Left = close.Left - options.Width;
            pin.Left = options.Left - pin.Width;
            add.Left = inset;
            brand.SetBounds((int)Math.Round(47 * scale), 0, 100, headerHeight);
            brand.Width = Math.Max(10, pin.Left - brand.Left - inset);
            body.Padding = Note.IsTitle ? new Padding((int)(17 * scale), (int)(10 * scale), (int)(17 * scale), (int)(10 * scale)) : new Padding((int)(17 * scale), (int)(14 * scale), (int)(17 * scale), (int)(14 * scale));
        }
        public int MeasureHeight(int width)
        {
            int textWidth = Math.Max(40, width - body.Padding.Horizontal - 6);
            Size size = TextRenderer.MeasureText(String.IsNullOrEmpty(editor.Text) ? "Ag" : editor.Text + "\u200b", editor.Font,
                new Size(textWidth, 10000000), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl | TextFormatFlags.ExpandTabs);
            return header.Height + body.Padding.Vertical + size.Height + (int)Math.Ceiling(6 * Math.Max(96, DeviceDpi) / 96.0);
        }
        public bool ApplyLayout(Rectangle bounds, bool scroll)
        {
            bool changed = Note.X != bounds.X || Note.Y != bounds.Y || Note.Width != bounds.Width || Note.Height != bounds.Height;
            restoring = true;
            try
            {
                fixedWidth = bounds.Width;
                // Framework 4.x treats a half-zero MaximumSize as a real native height limit.
                // Use explicit, positive dimensions for both the managed and native constraints.
                MaximumSize = new Size(Math.Max(16384, bounds.Width), 16384);
                MinimumSize = new Size(bounds.Width, Math.Min(bounds.Height, MinimumNoteHeight));
                MaximumSize = new Size(bounds.Width, 16384);
                Bounds = bounds;
                Note.X = Left; Note.Y = Top; Note.Width = Width; Note.Height = Height;
                RichTextBoxScrollBars mode = scroll ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.None;
                if (editor.ScrollBars != mode) editor.ScrollBars = mode;
            }
            finally { restoring = false; }
            return changed;
        }
        public void RefreshText() { editor.Text = Note.Text; }
        public void RecoverLayout()
        {
            if (context != null) context.RequestLayout(this);
            else ApplyLayout(new Rectangle(Note.X, Note.Y, fixedWidth, Math.Max(MinimumNoteHeight, Note.Height)), editor.ScrollBars != RichTextBoxScrollBars.None);
        }
        public void SetSaveState(bool isSaved) { if (isSaved) toolTip.SetToolTip(editor, ""); }
        public void SetSaveError() { toolTip.SetToolTip(editor, "Falha ao salvar. Use Ctrl+S para tentar novamente."); }
        private void DragHeader(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Native.ReleaseCapture(); Native.SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
            if (context != null) context.MoveNote(this, Screen.FromPoint(Cursor.Position), Left, Top);
        }
        private void SetPinned(bool value)
        { Note.TopMost = value; TopMost = value; pin.Pinned = value; pin.Invalidate(); context.Changed(); }
        private void SetFontSize(float size)
        {
            Note.FontSize = size; SetEditorFont(size, Note.IsTitle ? FontStyle.Bold : FontStyle.Regular);
            if (context != null) { context.Changed(); context.RequestLayout(this); }
        }
        public void RefreshKind()
        {
            SetEditorFont(Note.FontSize, Note.IsTitle ? FontStyle.Bold : FontStyle.Regular);
            LayoutButtons();
            if (editor.IsHandleCreated) ApplyAlignment();
            editor.AccessibleName = Note.IsTitle ? "Título da coluna" : "Conteúdo da nota";
            RefreshTheme();
        }
        private void SetEditorFont(float size, FontStyle style)
        {
            Font previous = editor.Font;
            if (Math.Abs(previous.Size - size) < 0.01 && previous.Style == style) return;
            editor.Font = new Font("Segoe UI", size, style);
            if (!Object.ReferenceEquals(previous, editor.Font)) previous.Dispose();
        }
        private void ApplyAlignment()
        {
            int start = editor.SelectionStart, length = editor.SelectionLength;
            editor.SelectAll(); editor.SelectionAlignment = Note.IsTitle ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            editor.Select(start, length);
        }
        public void RefreshTheme() { ApplyPalette(); }
        private void ApplyPalette()
        {
            palette = Palette.Find(Note.Color);
            BackColor = body.BackColor = editor.BackColor = palette.Background;
            editor.ForeColor = palette.Foreground; header.BackColor = palette.Header;
            brand.ForeColor = Color.FromArgb(185, palette.Foreground);
            foreach (GlyphButton b in new[] { add, pin, options, close }) { b.BackColor = palette.Header; b.ForeColor = palette.Foreground; }
            pin.Pinned = Note.TopMost;
            string name = context == null ? palette.Name : context.Data.Settings.ThemeName(Note.Color);
            brand.Text = Note.IsTitle ? "TÍTULO · " + name : name;
            toolTip.SetToolTip(brand, name + " · Memento");
            Invalidate(true);
        }
        private static Color Blend(Color a, Color b, float ratio)
        { return Color.FromArgb((int)(a.R * ratio + b.R * (1 - ratio)), (int)(a.G * ratio + b.G * (1 - ratio)), (int)(a.B * ratio + b.B * (1 - ratio))); }
        private ToolStripMenuItem Item(string text, Action action)
        { ToolStripMenuItem item = new ToolStripMenuItem(text); item.Click += delegate { action(); }; return item; }
        private void BuildMenu()
        {
            NotesContext.ClearMenu(menu.Items);
            menu.Items.Add(new ToolStripMenuItem("COR DA NOTA") { Enabled = false });
            foreach (Palette color in Palette.All)
            {
                Palette captured = color;
                ToolStripMenuItem item = Item(context.Data.Settings.ThemeName(color.Key).Replace("&", "&&"), delegate { Note.Color = captured.Key; ApplyPalette(); context.Changed(); });
                item.Checked = Note.Color == color.Key; item.Image = NotesContext.ColorDot(color.Background); menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem font = new ToolStripMenuItem("Tamanho do texto");
            foreach (int size in new[] { 10, 11, 13, 15, 18, 22, 28, 32 })
            { int captured = size; ToolStripMenuItem item = Item(size + " pt", delegate { SetFontSize(captured); }); item.Checked = Math.Abs(Note.FontSize - size) < 0.1; font.DropDownItems.Add(item); }
            menu.Items.Add(font);
            menu.Items.Add(Item("Usar este tema como padrão", delegate { context.Data.Settings.DefaultTheme = Note.Color; context.Changed(); context.Flush(false); }));
            menu.Items.Add(Item("Renomear temas e assuntos…", delegate { context.EditThemes(); }));
            ToolStripMenuItem screens = new ToolStripMenuItem("Mover para outra tela");
            int number = 0;
            foreach (Screen screen in Screen.AllScreens)
            {
                Screen captured = screen; number++;
                screens.DropDownItems.Add(Item("Tela " + number + (screen.Primary ? " · principal" : "") + " (" + screen.Bounds.Width + " × " + screen.Bounds.Height + ")", delegate { context.MoveToScreen(this, captured); }));
            }
            menu.Items.Add(screens);
            ToolStripMenuItem pinned = Item("Manter acima dos outros aplicativos", delegate { SetPinned(!Note.TopMost); }); pinned.Checked = Note.TopMost; menu.Items.Add(pinned);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Nova nota", delegate { context.CreateNote(this); }));
            menu.Items.Add(Item("Novo título de coluna", delegate { context.CreateNote(this, true); }));
            ToolStripMenuItem title = Item("Usar como título de coluna", delegate { context.ChangeKind(this); }); title.Checked = Note.IsTitle; menu.Items.Add(title);
            menu.Items.Add(Item("Guardar na bandeja", delegate { context.HideNote(Note); }));
            menu.Items.Add(Item("Mover para a lixeira…", delegate { context.DeleteNote(Note); }));
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.N)) { context.CreateNote(this); return true; }
            if (keyData == (Keys.Control | Keys.W)) { context.HideNote(Note); return true; }
            if (keyData == (Keys.Control | Keys.S)) { context.Flush(true); return true; }
            if (keyData == (Keys.Control | Keys.Oemcomma)) { options.PerformClick(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.V)) { Native.SendMessage(editor.Handle, 0x302, IntPtr.Zero, IntPtr.Zero); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x112 && (((int)m.WParam & 0xfff0) == 0xF020 || ((int)m.WParam & 0xfff0) == 0xF000)) return;
            base.WndProc(ref m);
            if (m.Msg == 0x2E0 && initialized && context != null) context.RequestLayout(this);
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e); LayoutButtons(); DesktopSupport.Configure(Handle);
        }
        internal Rectangle EditorBounds { get { return new Rectangle(body.Left + editor.Left, body.Top + editor.Top, editor.Width, editor.Height); } }
        internal bool HeaderButtonsFit
        {
            get { return new[] { add, pin, options, close }.All(b => b.Top == 0 && header.ClientRectangle.Contains(b.Bounds)) && brand.Right <= pin.Left; }
        }
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            if (initialized && !restoring && WindowState == FormWindowState.Normal)
            { width = fixedWidth; height = Math.Max(MinimumNoteHeight, height); }
            base.SetBoundsCore(x, y, width, height, specified);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed) { disposed = true; toolTip.Dispose(); NotesContext.ClearMenu(menu.Items); menu.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
