using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Instalador do Memento")]
[assembly: AssemblyDescription("Instalação por usuário, sem administrador, do Memento")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

namespace Memento
{
    internal static class Installer
    {
        internal static readonly string[] Payload = { "Memento.exe", "Memento.exe.config", "Memento.ico", "LEIA-ME.txt", "LICENSE.txt" };
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--extract")
            {
                try { Extract(Path.GetFullPath(args[1])); return 0; }
                catch { return 1; }
            }
            Native.EnableDpi(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\Memento.Installer.v1", out created))
            {
                if (!created) { MessageBox.Show("Já existe um instalador do Memento aberto.", "Memento"); return 1; }
                try
                {
                    if (args.Contains("--uninstall")) return Uninstall() ? 0 : 1;
                    using (InstallForm form = new InstallForm()) Application.Run(form);
                    return 0;
                }
                catch (Exception error) { MessageBox.Show(error.Message, "Memento · instalação", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
        }
        internal static void Extract(string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Payload)
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload." + file))
                {
                    if (stream == null) throw new InvalidDataException("Arquivo ausente no instalador: " + file);
                    using (FileStream output = new FileStream(Path.Combine(target, file), FileMode.Create, FileAccess.Write, FileShare.None)) stream.CopyTo(output);
                }
            }
        }
        private static string StartMenuDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Memento"); } }
        internal static string DesktopShortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Memento.lnk"); } }
        private static void Shortcut(string path, string executable, string arguments)
        {
            object shell = null, shortcut = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell", true);
                shell = Activator.CreateInstance(type);
                shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { executable });
                shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(executable) });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(Product.InstallDirectory, "Memento.ico") });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Memento · suas notas, no seu lugar" });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); if (shell != null) Marshal.FinalReleaseComObject(shell); }
        }
        internal static void Install(bool startup, bool desktop)
        {
            if (!Native.CloseApplication(Product.DataDirectory)) throw new IOException("O Memento ainda está aberto ou não conseguiu salvar. Use “Sair do Memento” na bandeja e tente instalar novamente.");
            if (!Native.CloseLegacyApplication()) throw new IOException("O Pingo ainda está aberto ou não conseguiu salvar. Saia dele pela bandeja e tente novamente. Suas notas serão preservadas.");
            // Complete and validate the note migration before replacing the legacy program.
            new NoteStore(Product.DataDirectory).MigrateLegacy(Product.LegacyDataDirectory);
            string directory = Product.InstallDirectory;
            Directory.CreateDirectory(directory);
            string stage = Path.Combine(directory, "instalacao-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            string[] installedFiles = Payload.Concat(new[] { "Desinstalar.exe" }).ToArray();
            List<string> changed = new List<string>();
            try
            {
                Extract(stage);
                File.Copy(Application.ExecutablePath, Path.Combine(stage, "Desinstalar.exe"));
                // Keep previous binaries until the complete payload has been copied.
                foreach (string file in installedFiles)
                {
                    string destination = Path.Combine(directory, file);
                    if (File.Exists(destination)) File.Copy(destination, Path.Combine(stage, file + ".anterior"));
                    changed.Add(file); File.Copy(Path.Combine(stage, file), destination, true);
                }
            }
            catch
            {
                foreach (string file in changed)
                {
                    string previous = Path.Combine(stage, file + ".anterior"), destination = Path.Combine(directory, file);
                    if (File.Exists(previous)) File.Copy(previous, destination, true);
                    else if (File.Exists(destination)) File.Delete(destination);
                }
                throw;
            }
            finally
            {
                foreach (string file in installedFiles)
                {
                    string pending = Path.Combine(stage, file), previous = Path.Combine(stage, file + ".anterior");
                    if (File.Exists(pending)) File.Delete(pending);
                    if (File.Exists(previous)) File.Delete(previous);
                }
                if (Directory.Exists(stage) && !Directory.EnumerateFileSystemEntries(stage).Any()) Directory.Delete(stage);
            }
            string app = Path.Combine(directory, "Memento.exe");
            // Register uninstall before optional shortcuts, so a partial install can still be removed.
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Product.UninstallKey))
            {
                key.SetValue("DisplayName", "Memento · notas de desktop"); key.SetValue("DisplayVersion", Product.Version);
                key.SetValue("Publisher", "Igor Pradella"); key.SetValue("InstallLocation", directory);
                key.SetValue("DisplayIcon", Path.Combine(directory, "Memento.ico"));
                key.SetValue("UninstallString", Product.Quote(Path.Combine(directory, "Desinstalar.exe")) + " --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)(installedFiles.Sum(f => new FileInfo(Path.Combine(directory, f)).Length) / 1024), RegistryValueKind.DWord);
            }
            Directory.CreateDirectory(StartMenuDirectory);
            Shortcut(Path.Combine(StartMenuDirectory, "Memento.lnk"), app, "");
            Shortcut(Path.Combine(StartMenuDirectory, "Desinstalar Memento.lnk"), Path.Combine(directory, "Desinstalar.exe"), "--uninstall");
            if (desktop) Shortcut(DesktopShortcut, app, "");
            Product.SetStartup(startup, app, Product.DataDirectory);
            RetireLegacyVersion();
        }
        private static void RetireLegacyVersion()
        {
            string legacyExe = Path.Combine(Product.LegacyInstallDirectory, "Pingo.exe");
            if (!File.Exists(legacyExe)) return;
            AssemblyName identity = AssemblyName.GetAssemblyName(legacyExe);
            if (identity.Name != "Pingo" || identity.Version.Major != 1) return;
            Product.RemoveLegacyStartup();
            // Remove only the exact program files from our v1 installer; never its note directory.
            foreach (string file in new[] { "Pingo.exe", "Pingo.exe.config", "Pingo.ico", "LEIA-ME.txt", "LICENSE.txt", "Desinstalar.exe" })
            {
                string path = Path.Combine(Product.LegacyInstallDirectory, file);
                try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            string menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Pingo");
            foreach (string path in new[] { Path.Combine(menu, "Pingo.lnk"), Path.Combine(menu, "Desinstalar Pingo.lnk"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Pingo.lnk") })
                if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(menu) && !Directory.EnumerateFileSystemEntries(menu).Any()) Directory.Delete(menu);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Pingo", false);
        }
        private static bool Uninstall()
        {
            if (MessageBox.Show("Desinstalar o Memento deste usuário?\n\nSuas notas e cópias de segurança serão preservadas em:\n" + Product.DataDirectory, "Desinstalar Memento", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;
            string expected = Path.Combine(Product.InstallDirectory, "Desinstalar.exe");
            if (!String.Equals(Path.GetFullPath(Application.ExecutablePath), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use “Desinstalar Memento” no menu Iniciar ou em Aplicativos instalados.");
            if (!Native.CloseApplication(Product.DataDirectory)) throw new IOException("O Memento ainda está aberto ou não conseguiu salvar. Feche-o pela bandeja e tente novamente.");
            Product.SetStartup(false, Path.Combine(Product.InstallDirectory, "Memento.exe"), Product.DataDirectory);
            foreach (string file in Payload)
            {
                string path = Path.Combine(Product.InstallDirectory, file);
                if (File.Exists(path)) File.Delete(path);
            }
            string[] shortcuts = { Path.Combine(StartMenuDirectory, "Memento.lnk"), Path.Combine(StartMenuDirectory, "Desinstalar Memento.lnk"), DesktopShortcut };
            foreach (string file in shortcuts) if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(StartMenuDirectory) && !Directory.EnumerateFileSystemEntries(StartMenuDirectory).Any()) Directory.Delete(StartMenuDirectory);
            Registry.CurrentUser.DeleteSubKeyTree(Product.UninstallKey, false);
            MessageBox.Show("O Memento foi desinstalado. Suas notas foram preservadas.\n\nO pequeno arquivo Desinstalar.exe permanece na pasta do aplicativo e pode ser apagado após fechar esta mensagem.", "Memento", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
    }

    internal sealed class InstallForm : Form
    {
        private readonly CheckBox startup, desktop, open;
        private readonly Button install;
        private readonly Label status;
        private bool installed;
        public InstallForm()
        {
            Text = "Instalar Memento"; Icon = Product.LoadIcon(); StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(540, 500); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            BackColor = Color.FromArgb(249, 248, 244); Font = new Font("Segoe UI", 10);
            Panel hero = new Panel { Dock = DockStyle.Top, Height = 150, BackColor = Color.FromArgb(247, 230, 154) };
            hero.Controls.Add(new Label { Text = "MEMENTO", Font = new Font("Segoe UI", 12, FontStyle.Bold), Location = new Point(30, 23), AutoSize = true });
            hero.Controls.Add(new Label { Text = "Suas notas,\nno seu lugar.", Font = new Font("Segoe UI", 24, FontStyle.Bold), Location = new Point(26, 51), Size = new Size(450, 90) });
            Controls.Add(hero);
            Controls.Add(new Label { Text = "Notas que crescem com suas ideias. Organizadas em colunas.\nSuas notas do Pingo são preservadas na atualização.", Location = new Point(30, 174), Size = new Size(480, 50) });
            startup = new CheckBox { Text = "Iniciar com o Windows", Location = new Point(30, 237), AutoSize = true, Checked = Product.StartupEnabled(Path.Combine(Product.InstallDirectory, "Memento.exe"), Product.DataDirectory) || Product.LegacyStartupEnabled() };
            desktop = new CheckBox { Text = "Criar atalho na área de trabalho", Location = new Point(30, 270), AutoSize = true, Checked = File.Exists(Installer.DesktopShortcut) };
            open = new CheckBox { Text = "Abrir o Memento após instalar", Location = new Point(30, 303), AutoSize = true, Checked = true };
            Controls.Add(startup); Controls.Add(desktop); Controls.Add(open);
            Controls.Add(new Label { Text = "Instalação só para este usuário, sem administrador.\n" + Product.InstallDirectory, Location = new Point(30, 351), Size = new Size(480, 48), ForeColor = Color.FromArgb(95, 98, 92), Font = new Font("Segoe UI", 8.5f) });
            status = new Label { Location = new Point(30, 405), Size = new Size(480, 27), ForeColor = Color.FromArgb(50, 92, 60) }; Controls.Add(status);
            Button cancel = new Button { Text = "Fechar", Location = new Point(285, 440), Size = new Size(90, 34), FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
            install = new Button { Text = "Instalar Memento", Location = new Point(386, 440), Size = new Size(125, 34), BackColor = Color.FromArgb(39, 55, 43), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            install.FlatAppearance.BorderSize = 0; install.Click += InstallClick;
            Controls.Add(cancel); Controls.Add(install); AcceptButton = install; CancelButton = cancel;
        }
        private void InstallClick(object sender, EventArgs args)
        {
            if (installed) { Close(); return; }
            install.Enabled = false; Cursor = Cursors.WaitCursor; status.Text = "Instalando…"; status.Refresh();
            try
            {
                Installer.Install(startup.Checked, desktop.Checked); installed = true;
                startup.Enabled = desktop.Enabled = open.Enabled = false;
                status.Text = "Pronto. Procure o ícone amarelo junto ao relógio."; install.Text = "Concluir";
                if (open.Checked) Process.Start(new ProcessStartInfo(Path.Combine(Product.InstallDirectory, "Memento.exe")) { WorkingDirectory = Product.InstallDirectory, UseShellExecute = true });
            }
            catch (Exception error) { status.Text = "A instalação não foi concluída."; MessageBox.Show(error.Message, "Memento · instalação", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { install.Enabled = true; Cursor = Cursors.Default; }
        }
    }
}
