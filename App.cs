using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Memento")]
[assembly: System.Reflection.AssemblyDescription("Notas de desktop com gestão pela bandeja do sistema")]
[assembly: System.Reflection.AssemblyVersion("2.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.1.0.0")]

namespace Memento
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Native.EnableDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string directory = Product.DefaultDataDirectory;
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--data-dir" && i + 1 < args.Length) directory = Path.GetFullPath(args[++i]);
            if (args.Contains("--shutdown")) return Native.CloseApplication(directory) ? 0 : 1;
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                try { Directory.CreateDirectory(directory); File.AppendAllText(Path.Combine(directory, "erro.log"), DateTime.Now.ToString("o") + "\n" + e.Exception.ToString() + "\n\n"); } catch { }
                MessageBox.Show("O Memento encontrou um erro.\n\n" + e.Exception.Message + "\n\nSe possível, exporte uma cópia das notas pela bandeja antes de sair.", "Memento", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\" + Product.Channel(directory), out created))
            {
                if (!created)
                {
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        if (Native.SendCommand(directory, 1)) return 0;
                        Thread.Sleep(100);
                    }
                    MessageBox.Show("O Memento já está abrindo. Aguarde um instante e use o ícone da bandeja.", "Memento");
                    return 0;
                }
                try
                {
                    using (NotesContext context = new NotesContext(directory)) Application.Run(context);
                    return 0;
                }
                catch (Exception error)
                {
                    MessageBox.Show("O Memento não conseguiu abrir suas notas.\n\n" + error.Message + "\n\nPasta das notas:\n" + directory, "Memento", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }

    internal sealed class CommandWindow : Form
    {
        private readonly NotesContext context;
        public CommandWindow(NotesContext context, string directory)
        {
            this.context = context;
            Text = Product.Channel(directory); ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            IntPtr unused = Handle;
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Native.AppMessage)
            {
                if (message.WParam.ToInt32() == 2) context.Quit();
                else context.ShowAll();
                return;
            }
            base.WndProc(ref message);
        }
    }

    internal sealed class NotesContext : ApplicationContext
    {
        public readonly NoteStore Store;
        public readonly AppData Data;
        private readonly Dictionary<string, NoteForm> forms = new Dictionary<string, NoteForm>();
        private readonly NotifyIcon tray;
        private readonly Icon icon;
        private readonly ContextMenuStrip trayMenu;
        private readonly CommandWindow channel;
        private readonly System.Windows.Forms.Timer saveTimer;
        private readonly DesktopSupport desktop;
        private bool dirty, exiting, saveWarningShown, layingOut, layoutQueued, loading = true, disposed;
        private string changedNote;
        private string lastActiveNote;
        public bool Exiting { get { return exiting; } }
        public bool IsDirty { get { return dirty; } }
        internal NoteForm[] NoteWindows { get { return forms.Values.ToArray(); } }
#if VISUALTEST
        internal Rectangle? PreviewArea;
#endif
        public NotesContext(string directory)
        {
            Store = new NoteStore(directory);
            if (String.Equals(Path.GetFullPath(directory), Product.DataDirectory, StringComparison.OrdinalIgnoreCase)) Store.MigrateLegacy(Product.LegacyDataDirectory);
            Data = Store.Load(); Data.Settings.Normalize();
            icon = Product.LoadIcon();
            channel = new CommandWindow(this, directory);
            trayMenu = new ContextMenuStrip { Font = new Font("Segoe UI", 10) };
            trayMenu.Opening += delegate { BuildTrayMenu(); };
            tray = new NotifyIcon { Icon = icon, Text = "Memento · suas notas, no seu lugar", ContextMenuStrip = trayMenu, Visible = true };
            tray.DoubleClick += delegate { ShowAll(); };
            desktop = new DesktopSupport(channel, delegate { return forms.Values.ToArray(); });
            saveTimer = new System.Windows.Forms.Timer { Interval = 750 };
            saveTimer.Tick += delegate { if (dirty) Flush(false); };
            saveTimer.Start();
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            SystemEvents.SessionEnding += SessionEnding;
            bool firstRun = !File.Exists(Store.FilePath) && !Store.Recovered;
            if (firstRun && Data.Notes.Count == 0)
            {
                NoteData welcome = CreateNote(null);
                if (welcome != null) { welcome.Text = "Um lugar para lembrar.\n\nDigite seu lembrete. A nota cresce com o texto.\n\n+ cria outra nota\n••• escolhe tema e opções\n× guarda na bandeja"; forms[welcome.Id].RefreshText(); }
                Changed(); Flush(false);
                tray.ShowBalloonTip(7000, "O Memento fica aqui", "Use o ícone amarelo junto ao relógio para mostrar, criar ou guardar suas notas.", ToolTipIcon.Info);
            }
            else foreach (NoteData note in Data.Notes) OpenForm(note, !note.Hidden);
            loading = false; Relayout(null);
            if (!String.IsNullOrEmpty(Store.Warning))
                channel.BeginInvoke((Action)delegate { MessageBox.Show(Store.Warning, "Memento · recuperação", MessageBoxButtons.OK, MessageBoxIcon.Information); });
        }
        private void OpenForm(NoteData note, bool show)
        {
            NoteForm form = new NoteForm(this, note, icon); forms.Add(note.Id, form);
            if (show) form.Show();
        }
        public NoteData CreateNote(NoteForm source) { return CreateNote(source, false); }
        public NoteData CreateNote(NoteForm source, bool title)
        {
            if (source == null)
            {
                NoteData previous = Data.Notes.FirstOrDefault(n => !n.Hidden && n.Id == lastActiveNote) ?? Data.Notes.LastOrDefault(n => !n.Hidden);
                if (previous != null && forms.ContainsKey(previous.Id)) source = forms[previous.Id];
            }
            Screen screen = source == null ? Screen.FromPoint(Cursor.Position) : ScreenFor(source.Note);
            GridMetrics grid = Metrics(screen);
            NoteData[] active = NotesOn(screen).Where(n => !n.Hidden).ToArray();
            int column = GridLayout.FindSpace(grid, active, title, source == null ? -1 : source.Note.Column);
            if (column < 0) { FullScreenMessage(); return null; }
            NoteData note = new NoteData { X = grid.X(column), Y = grid.Top, Width = grid.Width, Height = title ? grid.TitleHeight : grid.NormalHeight,
                Monitor = screen.DeviceName, Column = column, Color = Data.Settings.DefaultTheme, IsTitle = title, FontSize = title ? 18 : 13 };
            NoteData[] siblings = active.Where(n => n.Column == column).OrderBy(n => n.Order).ToArray();
            if (title) { foreach (NoteData n in siblings) n.Order++; note.Order = 0; }
            else
            {
                note.Order = source != null && source.Note.Column == column ? source.Note.Order + 1 : siblings.Length == 0 ? 0 : siblings.Max(n => n.Order) + 1;
                foreach (NoteData n in siblings.Where(n => n.Order >= note.Order)) n.Order++;
            }
            Data.Notes.Add(note); OpenForm(note, true); Relayout(note.Id); forms[note.Id].Activate(); Changed();
            lastActiveNote = note.Id;
            return note;
        }
        public void MarkActive(NoteData note) { lastActiveNote = note.Id; }
        internal bool DesktopLayerActive { get { return desktop.DesktopLayerActive; } }
        private void FullScreenMessage()
        { tray.ShowBalloonTip(5000, "Esta tela está cheia", "Guarde uma nota na bandeja ou mova uma nota para outra tela para abrir espaço na grade.", ToolTipIcon.Info); }
        public Screen ScreenFor(NoteData note)
        { return Screen.AllScreens.FirstOrDefault(s => s.DeviceName == note.Monitor) ?? Screen.PrimaryScreen; }
        private NoteData[] NotesOn(Screen screen)
        { return Data.Notes.Where(n => ScreenFor(n).DeviceName == screen.DeviceName).ToArray(); }
        private GridMetrics Metrics(Screen screen)
        {
#if VISUALTEST
            if (PreviewArea.HasValue) return new GridMetrics(PreviewArea.Value, 96);
#endif
            return new GridMetrics(screen.WorkingArea, Native.DpiAt(new Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2)));
        }
        public void RequestLayout(NoteForm source)
        {
            if (loading || layingOut || exiting || channel.IsDisposed) return;
            if (source != null) changedNote = source.Note.Id;
            if (layoutQueued) return; layoutQueued = true;
            channel.BeginInvoke((Action)delegate { layoutQueued = false; string id = changedNote; changedNote = null; Relayout(id); });
        }
        internal void Relayout(string focus)
        {
            if (loading || layingOut || exiting) return;
            layingOut = true;
            try
            {
                bool overflow = false;
                foreach (Screen screen in Screen.AllScreens)
                {
                    GridMetrics grid = Metrics(screen);
                    NoteData[] notes = NotesOn(screen).Where(n => !n.Hidden).OrderBy(n => n.Order < 0 ? Int32.MaxValue : n.Order).ThenBy(n => n.Y).ToArray();
                    List<NoteData> assigned = new List<NoteData>();
                    foreach (NoteData n in notes)
                    {
                        int column = n.Column < 0 ? grid.NearestColumn(n.X) : Math.Min(n.Column, grid.Columns - 1);
                        if (!GridLayout.CanAdd(grid, assigned, column, n.IsTitle)) column = GridLayout.FindSpace(grid, assigned, n.IsTitle, -1);
                        if (column < 0) { n.Hidden = true; forms[n.Id].Hide(); overflow = true; Changed(); continue; }
                        n.Column = column;
                        if (n.Order < 0) n.Order = assigned.Count(a => a.Column == column);
                        if (String.IsNullOrEmpty(n.Monitor)) n.Monitor = screen.DeviceName;
                        assigned.Add(n);
                    }
                    List<LayoutRequest> requests = new List<LayoutRequest>();
                    foreach (NoteData n in assigned) requests.Add(new LayoutRequest(n, forms[n.Id].MeasureHeight(grid.Width)));
                    foreach (LayoutPlacement p in GridLayout.Arrange(grid, requests, focus))
                    {
                        NoteForm form = forms[p.Note.Id];
                        if (form.ApplyLayout(p.Bounds, p.Scroll)) Changed();
                    }
                }
                if (overflow) FullScreenMessage();
            }
            finally { layingOut = false; }
        }
        public void MoveNote(NoteForm form, Screen screen, int x, int y)
        {
            GridMetrics grid = Metrics(screen); NoteData note = form.Note;
            NoteData[] other = NotesOn(screen).Where(n => n.Id != note.Id && !n.Hidden).ToArray();
            int column = GridLayout.FindSpace(grid, other, note.IsTitle, grid.NearestColumn(x));
            if (column < 0) { FullScreenMessage(); Relayout(null); return; }
            List<NoteData> siblings = other.Where(n => n.Column == column).OrderBy(n => n.Order).ToList();
            int index = note.IsTitle ? 0 : siblings.FindIndex(n => forms[n.Id].Top + forms[n.Id].Height / 2 > y);
            if (index < 0) index = siblings.Count;
            if (!note.IsTitle && index == 0 && siblings.Count > 0 && siblings[0].IsTitle) index = 1;
            siblings.Insert(index, note); note.Column = column; note.Monitor = screen.DeviceName;
            for (int i = 0; i < siblings.Count; i++) siblings[i].Order = i;
            Relayout(note.Id); Changed(); Flush(false);
        }
        public void ChangeKind(NoteForm form)
        {
            NoteData note = form.Note; Screen screen = ScreenFor(note); GridMetrics grid = Metrics(screen);
            NoteData[] others = NotesOn(screen).Where(n => !n.Hidden && n.Id != note.Id).ToArray();
            bool title = !note.IsTitle;
            int column = GridLayout.FindSpace(grid, others, title, note.Column);
            if (column < 0) { FullScreenMessage(); return; }
            note.IsTitle = title; note.FontSize = title ? 18 : 13; form.RefreshKind();
            MoveNote(form, screen, grid.X(column), grid.Top);
        }
        public void Changed()
        {
            dirty = true;
            foreach (NoteForm form in forms.Values) form.SetSaveState(false);
        }
        public bool Flush(bool interactive)
        {
            if (!dirty) return true;
            try
            {
                Store.Save(Data); dirty = false; saveWarningShown = false;
                foreach (NoteForm form in forms.Values) form.SetSaveState(true);
                return true;
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException || error is InvalidOperationException || error is ArgumentException)) throw;
                foreach (NoteForm form in forms.Values) form.SetSaveError();
                if (interactive || !saveWarningShown)
                {
                    saveWarningShown = true;
                    MessageBox.Show("Não foi possível salvar as notas. Elas continuam abertas; tente salvar novamente ou exporte uma cópia pelo menu da bandeja.\n\n" + error.Message, "Memento · falha ao salvar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return false;
            }
        }
        public void ShowNote(NoteData note)
        {
            if (note.Hidden)
            {
                Screen screen = ScreenFor(note); GridMetrics grid = Metrics(screen);
                int column = GridLayout.FindSpace(grid, NotesOn(screen), note.IsTitle, note.Column);
                if (column < 0) { FullScreenMessage(); return; }
                note.Column = column;
                NoteData[] siblings = NotesOn(screen).Where(n => !n.Hidden && n.Id != note.Id && n.Column == column).ToArray();
                if (note.IsTitle) { foreach (NoteData n in siblings) n.Order++; note.Order = 0; }
                else note.Order = siblings.Length == 0 ? 0 : siblings.Max(n => n.Order) + 1;
            }
            note.Hidden = false;
            NoteForm form = forms[note.Id]; Relayout(note.Id); form.Show(); form.Activate(); Changed();
        }
        public void ShowAll()
        {
            if (Data.Notes.Count == 0) { CreateNote(null); return; }
            foreach (NoteData note in Data.Notes) ShowNote(note);
        }
        public void HideNote(NoteData note)
        { note.Hidden = true; forms[note.Id].Hide(); Relayout(null); Changed(); Flush(false); }
        public void DeleteNote(NoteData note)
        {
            if (MessageBox.Show("Mover esta nota para a lixeira?\n\nVocê pode recuperá-la pelo menu da bandeja.", "Memento", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Data.Notes.Remove(note); Data.Trash.Add(note);
            NoteForm form = forms[note.Id]; forms.Remove(note.Id); form.Dispose(); Relayout(null); Changed(); Flush(false);
        }
        public void RestoreNote(NoteData note)
        {
            Data.Trash.Remove(note); Data.Notes.Add(note); note.Hidden = true; OpenForm(note, false); ShowNote(note); Changed(); Flush(false);
        }
        public void MoveToScreen(NoteForm form, Screen screen)
        {
            GridMetrics grid = Metrics(screen);
            MoveNote(form, screen, grid.X(form.Note.Column), grid.Top + form.Note.Order * (grid.NormalHeight + grid.Gap));
        }
        private ToolStripMenuItem Item(string text, Action action)
        { ToolStripMenuItem item = new ToolStripMenuItem(text); item.Click += delegate { action(); }; return item; }
        private void BuildTrayMenu()
        {
            ClearMenu(trayMenu.Items);
            ToolStripMenuItem title = new ToolStripMenuItem("MEMENTO  ·  " + Data.Notes.Count + (Data.Notes.Count == 1 ? " nota" : " notas")) { Enabled = false };
            trayMenu.Items.Add(title);
            trayMenu.Items.Add(Item("＋  Nova nota", delegate { CreateNote(null); }));
            trayMenu.Items.Add(Item("Novo título de coluna", delegate { CreateNote(null, true); }));
            trayMenu.Items.Add(Item("Mostrar todas as notas", ShowAll));
            trayMenu.Items.Add(Item("Guardar todas na bandeja", delegate { foreach (NoteData n in Data.Notes) HideNote(n); }));
            ToolStripMenuItem notes = new ToolStripMenuItem("Minhas notas") { Enabled = Data.Notes.Count > 0 };
            foreach (NoteData note in Data.Notes)
            {
                NoteData captured = note;
                ToolStripMenuItem item = Item((note.Hidden ? "○  " : "●  ") + note.Title.Replace("&", "&&"), delegate { ShowNote(captured); });
                item.Image = ColorDot(Palette.Find(note.Color).Background); notes.DropDownItems.Add(item);
            }
            trayMenu.Items.Add(notes);
            ToolStripMenuItem trash = new ToolStripMenuItem("Recuperar da lixeira") { Enabled = Data.Trash.Count > 0 };
            foreach (NoteData note in Data.Trash.AsEnumerable().Reverse())
            { NoteData captured = note; trash.DropDownItems.Add(Item(note.Title.Replace("&", "&&"), delegate { RestoreNote(captured); })); }
            trayMenu.Items.Add(trash);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(Item("Trazer todas para a tela principal", delegate
            {
                foreach (NoteData note in Data.Notes) { note.Monitor = Screen.PrimaryScreen.DeviceName; note.Column = -1; }
                Relayout(null); Changed();
            }));
            trayMenu.Items.Add(Item("Organizar notas na grade", delegate { Relayout(null); Changed(); }));
            trayMenu.Items.Add(Item("Temas e assuntos…", EditThemes));
            bool startup = Product.StartupEnabled(Application.ExecutablePath, Store.DirectoryPath);
            ToolStripMenuItem start = Item("Iniciar com o Windows", delegate
            {
                try { Product.SetStartup(!startup, Application.ExecutablePath, Store.DirectoryPath); }
                catch (Exception error) { MessageBox.Show("Não foi possível alterar a inicialização.\n\n" + error.Message, "Memento"); }
            });
            start.Checked = startup; trayMenu.Items.Add(start);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(Item("Exportar cópia das notas…", Export));
            trayMenu.Items.Add(Item("Importar notas…", Import));
            trayMenu.Items.Add(Item("Como usar o Memento", Help));
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(new ToolStripMenuItem(Product.Credit) { Enabled = false, Font = new Font("Segoe UI", 8) });
            trayMenu.Items.Add(Item("Sair do Memento", Quit));
        }
        public void EditThemes()
        {
            using (ThemeSettingsForm dialog = new ThemeSettingsForm(Data.Settings))
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                Data.Settings = dialog.Result;
                foreach (NoteForm form in forms.Values) form.RefreshTheme();
                Changed(); Flush(true);
            }
        }
        internal static void ClearMenu(ToolStripItemCollection items)
        {
            while (items.Count > 0)
            {
                ToolStripItem item = items[0]; items.RemoveAt(0);
                ToolStripDropDownItem dropdown = item as ToolStripDropDownItem;
                if (dropdown != null && dropdown.HasDropDownItems) ClearMenu(dropdown.DropDownItems);
                Image image = item.Image; item.Image = null;
                if (image != null) image.Dispose();
                item.Dispose();
            }
        }
        public static Bitmap ColorDot(Color color)
        {
            Bitmap image = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(image))
            using (Brush b = new SolidBrush(color))
            using (Pen p = new Pen(Color.FromArgb(120, 120, 120)))
            { g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.FillEllipse(b, 2, 2, 12, 12); g.DrawEllipse(p, 2, 2, 12, 12); }
            return image;
        }
        private void Export()
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "Cópia do Memento (*.json)|*.json", FileName = "Memento-notas-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json" })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                if (String.Equals(Path.GetFullPath(dialog.FileName), Store.FilePath, StringComparison.OrdinalIgnoreCase) || String.Equals(Path.GetFullPath(dialog.FileName), Store.BackupPath, StringComparison.OrdinalIgnoreCase))
                { MessageBox.Show("Escolha outro nome para a cópia, fora dos arquivos de salvamento automático.", "Memento"); return; }
                try { Store.Export(Data, dialog.FileName); MessageBox.Show("Cópia salva com todas as notas e a lixeira.", "Memento"); }
                catch (Exception error) { MessageBox.Show("Não foi possível exportar.\n\n" + error.Message, "Memento"); }
            }
        }
        private void Import()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Cópia do Memento (*.json)|*.json" })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                try
                {
                    AppData imported = NoteStore.Read(dialog.FileName);
                    if (imported.Notes.Count == 0) { MessageBox.Show("Esta cópia não tem notas ativas.", "Memento"); return; }
                    if (MessageBox.Show("Adicionar " + imported.Notes.Count + " nota(s) desta cópia?\nAs notas atuais serão mantidas.", "Memento", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                    foreach (NoteData note in imported.Notes) { note.Id = Guid.NewGuid().ToString("N"); note.Hidden = false; note.Column = -1; note.Order = -1; Data.Notes.Add(note); OpenForm(note, true); }
                    foreach (NoteData note in imported.Trash) { note.Id = Guid.NewGuid().ToString("N"); Data.Trash.Add(note); }
                    Relayout(null); Changed(); Flush(true);
                }
                catch (Exception error) { MessageBox.Show("Não foi possível importar.\n\n" + error.Message, "Memento"); }
            }
        }
        private void Help()
        {
            MessageBox.Show("MEMENTO  " + Product.Version + "\nSuas notas, no seu lugar.\n\n• Largura fixa; a altura acompanha o texto.\n• A grade organiza as notas e mantém o espaço entre elas.\n• Arraste a faixa superior para trocar de coluna ou tela.\n• A nota empurra as de baixo até o limite da coluna.\n• A rolagem só aparece quando o espaço acaba.\n• Crie títulos de coluna com metade da altura inicial.\n• Temas e assuntos, na bandeja, escolhe o padrão e os nomes.\n• × guarda a nota; a lixeira fica no menu •••.\n• Win+D mantém as notas visíveis na área de trabalho.\n• Ctrl+N: nova nota · Ctrl+W: guardar · Ctrl+S: salvar.\n\n" + Product.Credit + "\n\nSeus dados ficam somente neste computador:\n" + Store.DirectoryPath + "\n\nA inicialização com o Windows é opcional.", "Memento · como usar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void DisplayChanged(object sender, EventArgs args)
        {
            if (exiting || channel.IsDisposed) return;
            try { channel.BeginInvoke((Action)delegate { if (!exiting) Relayout(null); }); }
            catch (InvalidOperationException) { }
        }
        private void SessionEnding(object sender, SessionEndingEventArgs args)
        {
            if (channel.IsDisposed) return;
            try { channel.Invoke((Action)delegate { Flush(false); }); } catch (InvalidOperationException) { }
        }
        public void Quit()
        {
            if (exiting || !Flush(true)) return;
            exiting = true; ExitThread();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true; exiting = true;
                desktop.Dispose();
                SystemEvents.DisplaySettingsChanged -= DisplayChanged;
                SystemEvents.SessionEnding -= SessionEnding;
                saveTimer.Stop(); saveTimer.Dispose();
                tray.Visible = false; tray.Dispose();
                foreach (NoteForm form in forms.Values.ToArray()) form.Dispose();
                forms.Clear(); ClearMenu(trayMenu.Items); trayMenu.Dispose(); channel.Dispose(); icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
