using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Memento
{
    internal static class Tests
    {
        [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect bounds);
        private static int passed;
        private static string root;
        private static void Check(bool condition, string name)
        { if (!condition) throw new Exception("FALHOU: " + name); passed++; Console.WriteLine("OK: " + name); }
        private static NoteStore Store(string name) { return new NoteStore(Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"))); }
        [STAThread]
        public static int Main(string[] args)
        {
            root = Path.GetFullPath(args.Length == 0 ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data") : args[0]);
            Directory.CreateDirectory(root);
            try
            {
                NoteStore store = Store("roundtrip");
                Check(store.Load().Notes.Count == 0, "primeira abertura começa sem dados inventados");
                AppData data = new AppData();
                NoteData note = new NoteData { Text = "Café ☕\nSegunda tela: ação, coração. 中文 🎉", Color = "noite", X = -1800, Y = -250, Width = 420, Height = 330, FontSize = 18, Hidden = true, TopMost = true };
                data.Notes.Add(note); data.Trash.Add(new NoteData { Text = "Uma nota na lixeira" });
                store.Save(data);
                AppData loaded = store.Load();
                Check(loaded.Notes[0].Text == note.Text, "conteúdo Unicode e quebras de linha preservados");
                Check(loaded.Notes[0].X == -1800 && loaded.Notes[0].Y == -250 && loaded.Notes[0].Width == 320 && loaded.Notes[0].Height == 330, "posição negativa e altura preservadas com largura fixa");
                Check(loaded.Notes[0].Color == "noite" && loaded.Notes[0].FontSize == 18 && loaded.Notes[0].Hidden && loaded.Notes[0].TopMost, "cor, fonte, visibilidade e alfinete preservados");
                Check(loaded.Trash.Count == 1, "lixeira persiste após reabrir");
                Check(!File.ReadAllText(store.FilePath).Contains("\"Bounds\""), "JSON contém apenas o modelo persistente");
                data.Notes[0].Text = "Segunda versão"; store.Save(data);
                Check(File.Exists(store.BackupPath), "backup criado pelo salvamento atômico");
                Check(NoteStore.Read(store.BackupPath).Notes[0].Text.StartsWith("Café"), "backup contém o salvamento anterior");
                Check(!Directory.EnumerateFiles(store.DirectoryPath, "*.tmp").Any(), "salvamento não deixa arquivos temporários");
                File.WriteAllText(store.FilePath, "{ danificado", Encoding.UTF8);
                NoteStore recovery = new NoteStore(store.DirectoryPath);
                AppData recovered = recovery.Load();
                Check(recovery.Recovered && recovered.Notes[0].Text.StartsWith("Café"), "arquivo corrompido recuperado do backup");
                Check(Directory.EnumerateFiles(store.DirectoryPath, "notas-recuperacao-*.json").Count() == 1, "original corrompido preservado para recuperação");
                recovery.Save(recovered);
                Check(NoteStore.Read(recovery.BackupPath).Notes[0].Text.StartsWith("Café"), "recuperação mantém o backup válido");
                NoteStore missing = Store("missing"); missing.Save(data); missing.Save(data); File.Delete(missing.FilePath);
                Check(new NoteStore(missing.DirectoryPath).Load().Notes.Count == 1, "backup recuperado se arquivo principal faltar");
                NoteStore bad = Store("bad"); Directory.CreateDirectory(bad.DirectoryPath); File.WriteAllText(bad.FilePath, "não é JSON");
                bool refused = false; try { bad.Load(); } catch (InvalidDataException) { refused = true; }
                Check(refused && File.ReadAllText(bad.FilePath) == "não é JSON", "dados ilegíveis sem backup não são sobrescritos");
                NoteStore invalid = Store("invalid"); Directory.CreateDirectory(invalid.DirectoryPath);
                File.WriteAllText(invalid.FilePath, "{\"Version\":1,\"Notes\":[{\"Id\":\"x\",\"Color\":\"inexistente\",\"Width\":0,\"Height\":99999,\"FontSize\":500},{\"Id\":\"x\"}],\"Trash\":null}");
                AppData normalized = invalid.Load();
                Check(normalized.Notes[0].Color == "sol" && normalized.Notes[0].Width == 320 && normalized.Notes[0].Height == 4000 && normalized.Notes[0].FontSize == 32, "valores de arquivo importado são normalizados");
                Check(normalized.Notes[0].Id != normalized.Notes[1].Id && normalized.Trash != null, "IDs repetidos e lixeira ausente são corrigidos");
                string exported = Path.Combine(root, "export.json"); store.Export(recovered, exported); store.Export(recovered, exported);
                Check(NoteStore.Read(exported).Notes[0].Text == recovered.Notes[0].Text && NoteStore.Read(exported).Trash.Count == 1, "exportação pode ser reaberta e substituída");
                Rectangle primary = new Rectangle(0, 0, 1920, 1040), left = new Rectangle(-1920, -200, 1920, 1080);
                Rectangle target = new Rectangle(-1800, -150, 320, 300);
                Check(BoundsPolicy.Fit(target, new[] { primary, left }) == target, "posição em monitor à esquerda e acima é mantida");
                Check(primary.Contains(BoundsPolicy.Fit(target, new[] { primary })), "monitor removido retorna nota a uma tela acessível");
                Check(BoundsPolicy.Fit(target, new[] { primary, left }) == target, "reconectar monitor restaura coordenadas originais");
                Check(primary.Contains(BoundsPolicy.Fit(new Rectangle(100000, 100000, 6000, 6000), new[] { primary })), "notas fora da tela e grandes demais são limitadas");
                Check(BoundsPolicy.Fit(new Rectangle(0, 0, 300, 300), new[] { new Rectangle(0, 0, 200, 150) }) == new Rectangle(0, 0, 200, 150), "telas pequenas são respeitadas");
                Palette night = Palette.Find("noite");
                Check(night.Background.R < 30 && night.Background.G < 30 && night.Foreground == ColorTranslator.FromHtml("#FFFFFF"), "tema preto usa texto branco");
                Check(Palette.All.Select(p => p.Key).Distinct().Count() == 7, "sete cores independentes disponíveis");
                Check(Product.StartupCommand(@"C:\Um diretório\Memento.exe", @"C:\Meus dados") == "\"C:\\Um diretório\\Memento.exe\" --data-dir \"C:\\Meus dados\" --startup", "inicialização aceita caminhos com espaços");
                Check(Product.Channel(@"C:\Memento\Dados") == Product.Channel(@"c:\memento\dados\"), "uma única instância por pasta, com normalização de caminho");
                Check(Product.Channel(@"C:\Memento\Dados") != Product.Channel(@"C:\Memento\Outra"), "pastas portáteis distintas têm instâncias separadas");
                UserSettings settings = new UserSettings { DefaultTheme = "noite" };
                settings.ThemeNames["noite"] = "Trabalho"; settings.Normalize(); data.Settings = settings; store.Save(data);
                Check(store.Load().Settings.DefaultTheme == "noite" && store.Load().Settings.ThemeName("noite") == "Trabalho", "tema padrão e nomes de assuntos persistem");
                NoteStore migration = Store("migration");
                Check(migration.MigrateLegacy(store.DirectoryPath) && migration.Load().Notes[0].Text == data.Notes[0].Text, "migração copia conteúdo da versão anterior");
                Check(!migration.MigrateLegacy(store.DirectoryPath) && File.Exists(store.FilePath), "migração não substitui novos dados nem remove os antigos");
                Check(NoteStore.Read(invalid.FilePath).Version == 2, "formato v1 é aceito e atualizado para v2");
                GridMetrics grid = new GridMetrics(primary, 96);
                Check(grid.Columns == 5 && grid.Width == 320 && grid.Gap == 20, "grade Full HD usa cinco colunas de largura fixa e espaço de 20 px");
                Check(grid.TitleHeight * 2 == grid.NormalHeight, "título tem metade da altura inicial de uma nota");
                NoteData a = new NoteData { Column = 0, Order = 0 }, b = new NoteData { Column = 0, Order = 1 }, c = new NoteData { Column = 1, Order = 0 };
                var requests = new[] { new LayoutRequest(a, 220), new LayoutRequest(b, 220), new LayoutRequest(c, 220) };
                var initial = GridLayout.Arrange(grid, requests, null);
                Check(initial[1].Bounds.Top == initial[0].Bounds.Bottom + 20 && initial[2].Bounds.Left == initial[0].Bounds.Right + 20, "grade mantém a distância horizontal e vertical");
                foreach (var p in initial) p.Note.Height = p.Bounds.Height;
                requests[0].DesiredHeight = 520;
                var grown = GridLayout.Arrange(grid, requests, a.Id);
                Check(grown[0].Bounds.Height == 520 && grown[1].Bounds.Top == grown[0].Bounds.Bottom + 20 && !grown[0].Scroll, "nota cresce sem barra e empurra a que está embaixo");
                foreach (var p in grown) p.Note.Height = p.Bounds.Height;
                requests[0].DesiredHeight = 5000;
                var limited = GridLayout.Arrange(grid, requests, a.Id);
                Check(limited[0].Scroll && limited[1].Bounds.Bottom == grid.Top + grid.Height && limited[1].Bounds.Height == 300, "no limite da tela, crescimento vira rolagem sem esconder a nota inferior");
                requests[0].DesiredHeight = 220;
                var shrunk = GridLayout.Arrange(grid, requests, a.Id);
                Check(shrunk[0].Bounds.Height == 300 && !shrunk[0].Scroll && shrunk[1].Bounds.Top == shrunk[0].Bounds.Bottom + 20, "apagar texto reduz altura, recolhe as notas e remove a barra");
                NoteData heading = new NoteData { IsTitle = true, Column = 2, Order = 0 };
                var header = GridLayout.Arrange(grid, new[] { new LayoutRequest(heading, 400) }, null).Single();
                Check(header.Bounds.Height == 150 && header.Scroll, "título conserva meia altura mesmo com texto longo");
                Check(GridLayout.FindSpace(grid, new[] { a, b }, true, 0) == 0, "título pode ser inserido antes das notas de uma coluna");
                heading.Column = 0;
                Check(GridLayout.FindSpace(grid, new[] { heading }, true, 0) == 1, "novo título procura uma coluna que ainda não tem título");
                Check(GridLayout.FindSpace(grid, new[] { a, b, new NoteData { Column = 0 }, new NoteData { Column = 0 } }, false, 0) == 1, "coluna cheia leva a próxima nota para a coluna seguinte");
                var negativeGrid = new GridMetrics(left, 96);
                Check(left.Contains(GridLayout.Arrange(negativeGrid, new[] { new LayoutRequest(a, 220) }, null).Single().Bounds), "grade funciona em monitor com coordenadas negativas");
                GridMetrics scaled = new GridMetrics(primary, 144);
                Check(scaled.Width == 480 && scaled.Gap == 30 && scaled.Columns == 3, "grade adapta a escala de 150% sem permitir largura manual");
                Check(DesktopSupport.IsDesktopClass("Progman") && DesktopSupport.IsDesktopClass("WorkerW") && !DesktopSupport.IsDesktopClass("Notepad"), "proteção de área de trabalho reconhece somente o shell");
                Check(DesktopSupport.ShouldUseDesktopLayer(true, true, false, false, false), "área de trabalho elevada usa camada temporária acima do Explorer");
                Check(DesktopSupport.ShouldUseDesktopLayer(false, true, true, false, false), "Mostrar área de trabalho pela barra também eleva as notas");
                Check(DesktopSupport.ShouldUseDesktopLayer(false, false, false, true, true), "editar uma nota conserva a camada da área de trabalho");
                Check(!DesktopSupport.ShouldUseDesktopLayer(false, true, false, false, true), "abrir outro aplicativo retira a camada temporária");
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (Icon icon = Product.LoadIcon())
                using (NoteForm form = new NoteForm(null, new NoteData { Text = "Teste", Color = "noite", X = 60, Y = 60 }, icon))
                {
                    Check(!form.ShowInTaskbar && form.FormBorderStyle == FormBorderStyle.None, "nota não cria botão de janela na barra de tarefas");
                    PropertyInfo property = typeof(NoteForm).GetProperty("CreateParams", BindingFlags.Instance | BindingFlags.NonPublic);
                    CreateParams cp = (CreateParams)property.GetValue(form, null);
                    Check((cp.ExStyle & 0x80) != 0 && (cp.ExStyle & 0x40000) == 0, "estilo de janela também exclui a nota do Alt+Tab");
                    Check((cp.Style & (0x20000 | 0x10000 | 0x40000)) == 0 && !form.MinimizeBox, "nota não permite minimização nem redimensionamento manual");
                    form.Width = 900;
                    Check(form.Width == 320, "tentativa de alterar a largura é bloqueada");
                    PlainEditor control = form.Controls.OfType<Panel>().SelectMany(p => p.Controls.OfType<PlainEditor>()).Single();
                    Check(control.Text == "Teste" && control.ForeColor.ToArgb() == Color.White.ToArgb() && control.BackColor.R < 30, "editor apresenta o tema escuro e o conteúdo da nota");
                    Check(control.ScrollBars == RichTextBoxScrollBars.None, "editor começa sem barra de rolagem");
                    Check(form.MeasureHeight(320) > 60, "medição do texto usa fonte válida após inicializar a nota");
                    form.RefreshKind();
                    Check(form.MeasureHeight(320) > 60, "reaplicar o mesmo tipo não descarta a fonte em uso");
                    form.ApplyLayout(new Rectangle(60, 60, 320, 220), true);
                    Check(control.ScrollBars == RichTextBoxScrollBars.Vertical, "rolagem vertical pode ser ativada no limite de altura");
                    form.ApplyLayout(new Rectangle(60, 60, 320, 300), false);
                    Check(control.ScrollBars == RichTextBoxScrollBars.None && form.Width == 320, "mais espaço volta a remover a barra e mantém a largura");
                    Check(form.Height == 300 && form.HeaderButtonsFit && form.EditorBounds.Height >= 220, "nota normal mantém corpo de edição e botões inteiros");
                    Check(!form.Controls.OfType<Label>().Any(l => l.Text.Contains("salvo neste computador")), "rodapé de confirmação foi removido");
                    IntPtr handle = form.Handle;
                    form.ApplyLayout(new Rectangle(60, 60, 320, 300), false);
                    WindowRect nativeBounds; GetWindowRect(handle, out nativeBounds);
                    Check(nativeBounds.Right - nativeBounds.Left == 320 && nativeBounds.Bottom - nativeBounds.Top == 300, "janela nativa de primeiro nível mede 320 por 300 e não vira uma faixa");
                    form.Height = 1;
                    Check(form.Height == 300 && form.HeaderButtonsFit, "tentativa de colapsar a altura é bloqueada e mantém os botões");
                    form.ApplyLayout(new Rectangle(60, 60, 320, 520), false);
                    GetWindowRect(handle, out nativeBounds);
                    Check(nativeBounds.Bottom - nativeBounds.Top == 520 && form.EditorBounds.Height >= 440, "crescimento altera a altura real da janela nativa");
                    DesktopSupport.ApplyWindowLayer(form, true, IntPtr.Zero);
                    Check(DesktopSupport.IsTopmost(handle) && !form.Note.TopMost, "camada temporária é aplicada pelo Windows sem mudar a preferência do alfinete");
                    DesktopSupport.ApplyWindowLayer(form, false, IntPtr.Zero);
                    Check(!DesktopSupport.IsTopmost(handle) && !form.Visible, "sair da área de trabalho desfaz a camada sem mostrar nota guardada");
                }
                using (Icon icon = Product.LoadIcon())
                using (NoteForm titleForm = new NoteForm(null, new NoteData { Text = "Trabalho", IsTitle = true, FontSize = 18 }, icon))
                {
                    Check(titleForm.MeasureHeight(320) <= 150, "título curto cabe na meia nota sem rolagem");
                    titleForm.Note.Text = "Título longo\ncom várias linhas\npara testar o limite"; titleForm.RefreshText();
                    Check(titleForm.MeasureHeight(320) > 150, "título com texto demais pode usar rolagem na meia nota");
                }
                Console.WriteLine("PASSOU: " + passed + " verificações."); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
        }
    }
}
