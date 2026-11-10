using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Memento
{
    // A test controller for genuine independent top-level note windows.
    // It uses an isolated data directory and never embeds notes in a preview.
    internal static class NativeReview
    {
        [STAThread]
        public static void Main()
        {
            Native.EnableDpi(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RevisaoNativa");
            Directory.CreateDirectory(directory);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            { File.AppendAllText(Path.Combine(directory, "erro-teste.log"), e.Exception.ToString()); Application.Exit(); };
            NoteStore store = new NoteStore(directory);
            if (!File.Exists(store.FilePath))
            {
                AppData data = new AppData(); data.Settings.DefaultTheme = "noite";
                data.Notes.Add(new NoteData { Text = "Memento 2.1\n\nUma nota no tamanho normal.\n\nEla cresce quando o texto\nocupa o espaço disponível.", Color = "noite", Column = 0, Order = 0 });
                store.Save(data);
            }
            using (NotesContext context = new NotesContext(directory))
            using (Form controller = new Form { Text = "Memento · conferência da correção", ClientSize = new Size(480, 375), StartPosition = FormStartPosition.Manual, BackColor = Color.FromArgb(248, 247, 242), Font = new Font("Segoe UI", 10), Icon = Product.LoadIcon() })
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                controller.Location = new Point(area.Right - controller.Width - 30, area.Top + 30);
                controller.Controls.Add(new Label { Text = "Memento 2.1 · cópia de teste", Font = new Font("Segoe UI", 16, FontStyle.Bold), Location = new Point(22, 20), AutoSize = true });
                controller.Controls.Add(new Label { Text = "As notas à esquerda são janelas reais e independentes.\nAs suas notas de uso ficam preservadas.", Location = new Point(24, 66), Size = new Size(430, 48) });
                Label status = new Label { Location = new Point(24, 129), Size = new Size(430, 145), AccessibleName = "Estado das notas" }; controller.Controls.Add(status);
                Button add = new Button { Text = "Nova nota", Location = new Point(24, 285), Size = new Size(120, 32) };
                add.Click += delegate { context.CreateNote(null); }; controller.Controls.Add(add);
                Button grow = new Button { Text = "Texto longo", Location = new Point(158, 285), Size = new Size(120, 32) };
                grow.Click += delegate
                {
                    NoteForm form = context.NoteWindows.First(f => !f.Note.Hidden && !f.Note.IsTitle);
                    form.Note.Text = "Teste de crescimento\n\n" + String.Join("\n", Enumerable.Range(1, 30).Select(i => "Linha " + i + ": café, ideia e ação.")); form.RefreshText(); context.Relayout(form.Note.Id);
                }; controller.Controls.Add(grow);
                Button close = new Button { Text = "Concluir teste", Location = new Point(290, 285), Size = new Size(150, 32) };
                close.Click += delegate { controller.Close(); }; controller.Controls.Add(close);
                controller.Controls.Add(new Label { Text = "Para conferir Mostrar área de trabalho, use Win+D\nou o canto inferior direito do Windows.", Location = new Point(24, 332), Size = new Size(430, 40), Font = new Font("Segoe UI", 8) });
                Timer timer = new Timer { Interval = 200 };
                string previous = "";
                Action report = delegate
                {
                    var windows = context.NoteWindows.Where(f => !f.Note.Hidden && !f.IsDisposed).Select(f => new {
                        Id = f.Note.Id, Width = f.Width, Height = f.Height, X = f.Left, Y = f.Top,
                        TextHeight = f.EditorBounds.Height, ButtonsFit = f.HeaderButtonsFit,
                        Minimized = DesktopSupport.IsMinimized(f.Handle), Topmost = DesktopSupport.IsTopmost(f.Handle), Pinned = f.Note.TopMost
                    }).ToArray();
                    status.Text = String.Join("\n", windows.Select((f, i) => "Nota " + (i + 1) + ": " + f.Width + " × " + f.Height + " px · botões " + (f.ButtonsFit ? "OK" : "cortados") + (f.Minimized ? " · minimizada" : " · normal"))) + "\n\nCamada de área de trabalho: " + (context.DesktopLayerActive ? "ativa" : "normal");
                    string json = new JavaScriptSerializer().Serialize(new { Version = Product.Version, DesktopLayer = context.DesktopLayerActive, Windows = windows });
                    if (json != previous) { File.WriteAllText(Path.Combine(directory, "resultado.json"), json); previous = json; }
                };
                timer.Tick += delegate { report(); }; timer.Start(); report();
                controller.FormClosed += delegate { timer.Stop(); timer.Dispose(); context.Flush(false); };
                Application.Run(controller);
            }
        }
    }
}
