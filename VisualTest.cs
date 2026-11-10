using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Memento
{
    // Compile only with /define:VISUALTEST and /main:Memento.VisualTest.
    internal static class VisualTest
    {
        [STAThread]
        public static void Main()
        {
            Native.EnableDpi(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DadosVisuais");
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            { Directory.CreateDirectory(directory); File.AppendAllText(Path.Combine(directory, "visual-error.log"), e.Exception.ToString() + "\n\n"); Application.Exit(); };
            NoteStore store = new NoteStore(directory);
            if (!File.Exists(store.FilePath))
            {
                AppData data = new AppData();
                data.Settings.DefaultTheme = "noite"; data.Settings.ThemeNames["noite"] = "Trabalho"; data.Settings.ThemeNames["menta"] = "Pessoal";
                data.Notes.Add(new NoteData { Text = "Trabalho", IsTitle = true, Color = "noite", FontSize = 18, Column = 0, Order = 0 });
                data.Notes.Add(new NoteData { Text = "Ideias para hoje\n\nEsta nota cresce com o texto.\nAs de baixo acompanham.", Color = "noite", Column = 0, Order = 1 });
                data.Notes.Add(new NoteData { Text = "Próximos passos\n\nUma coisa de cada vez.", Color = "noite", Column = 0, Order = 2 });
                data.Notes.Add(new NoteData { Text = "Pessoal", IsTitle = true, Color = "menta", FontSize = 18, Column = 1, Order = 0 });
                data.Notes.Add(new NoteData { Text = "Lembrar do que importa.\n\nTemas também podem ser assuntos.", Color = "menta", Column = 1, Order = 1 });
                store.Save(data);
            }
            using (NotesContext context = new NotesContext(directory))
            using (Form host = new Form { Text = "Memento · revisão visual", ClientSize = new Size(1180, 860), StartPosition = FormStartPosition.CenterScreen, BackColor = Color.FromArgb(235, 235, 230), Icon = Product.LoadIcon() })
            {
                Panel bar = new Panel { Height = 50, Dock = DockStyle.Bottom, BackColor = Color.FromArgb(249, 248, 244) }; host.Controls.Add(bar);
                Action embed = delegate
                {
                    foreach (NoteForm form in context.NoteWindows)
                    {
                        if (!form.TopLevel) continue;
                        form.Hide(); form.TopLevel = false; host.Controls.Add(form); if (!form.Note.Hidden) form.Show();
                    }
                    bar.BringToFront();
                };
                context.PreviewArea = new Rectangle(0, 0, 1180, 810); embed(); context.Relayout(null);
                Button themes = new Button { Text = "Temas e assuntos", Left = 22, Top = 10, Width = 155, Height = 30 };
                themes.Click += delegate { context.EditThemes(); }; bar.Controls.Add(themes);
                Button add = new Button { Text = "Nova nota", Left = 190, Top = 10, Width = 110, Height = 30 };
                add.Click += delegate { context.CreateNote(null); embed(); context.Relayout(null); }; bar.Controls.Add(add);
                Button title = new Button { Text = "Novo título", Left = 315, Top = 10, Width = 110, Height = 30 };
                title.Click += delegate { context.CreateNote(null, true); embed(); context.Relayout(null); }; bar.Controls.Add(title);
                Button growth = new Button { Text = "Texto longo", Left = 440, Top = 10, Width = 110, Height = 30 };
                growth.Click += delegate
                {
                    NoteForm form = context.NoteWindows.First(f => !f.Note.IsTitle && f.Note.Column == 0);
                    form.Note.Text = "Texto de teste\n\n" + String.Join("\n", Enumerable.Range(1, 32).Select(i => "Linha " + i + ": café, ideia e ação.")); form.RefreshText(); context.Relayout(form.Note.Id);
                }; bar.Controls.Add(growth);
                Button menu = new Button { Text = "Menu da bandeja", Left = 566, Top = 10, Width = 140, Height = 30 };
                menu.Click += delegate
                {
                    typeof(NotesContext).GetMethod("BuildTrayMenu", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(context, null);
                    ContextMenuStrip tray = (ContextMenuStrip)typeof(NotesContext).GetField("trayMenu", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(context);
                    tray.Show(menu, new Point(0, 0));
                }; bar.Controls.Add(menu);
                host.FormClosed += delegate { context.Flush(false); };
                Application.Run(host);
            }
        }
    }
}
