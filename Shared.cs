using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Memento
{
    internal static class Product
    {
        public const string Name = "Memento";
        public const string Version = "2.1.0";
        public const string Credit = "Criado por Igor Pradella";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Memento";
        public static string InstallDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Memento"); } }
        public static string DataDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Memento"); } }
        public static string LegacyDataDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pingo"); } }
        public static string LegacyInstallDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Pingo"); } }
        public static string DefaultDataDirectory
        {
            get { return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Memento.portable")) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dados") : DataDirectory; }
        }
        public static string Channel(string directory)
        {
            using (SHA256 hash = SHA256.Create())
                return "Memento.IPC." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).TrimEnd('\\').ToLowerInvariant()))).Replace("-", "").Substring(0, 24);
        }
        public static Icon LoadIcon()
        {
            using (Stream stream = typeof(Product).Assembly.GetManifestResourceStream("Memento.ico"))
                return stream == null ? (Icon)SystemIcons.Application.Clone() : new Icon(stream);
        }
        public static string Quote(string value) { return "\"" + value + "\""; }
        public static string StartupCommand(string executable, string directory)
        {
            return Quote(executable) + " --data-dir " + Quote(directory) + " --startup";
        }
        public static bool StartupEnabled(string executable, string directory)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && String.Equals(key.GetValue(Name) as string, StartupCommand(executable, directory), StringComparison.OrdinalIgnoreCase);
        }
        public static bool LegacyStartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && String.Equals(key.GetValue("Pingo") as string, StartupCommand(Path.Combine(LegacyInstallDirectory, "Pingo.exe"), LegacyDataDirectory), StringComparison.OrdinalIgnoreCase);
        }
        public static void RemoveLegacyStartup()
        {
            if (!LegacyStartupEnabled()) return;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (key != null) key.DeleteValue("Pingo", false);
        }
        public static void SetStartup(bool enabled, string executable, string directory)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(Name, StartupCommand(executable, directory), RegistryValueKind.String);
                else if (StartupEnabled(executable, directory)) key.DeleteValue(Name, false);
            }
        }
    }

    internal static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        public static readonly uint AppMessage = RegisterWindowMessage("Memento.Notes.Command.v1");
        public static void EnableDpi()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
        }
        public static int DpiAt(Point point)
        {
            try
            {
                uint x, y;
                if (GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out x, out y) == 0) return Math.Max(96, (int)x);
            }
            catch (EntryPointNotFoundException) { } catch (DllNotFoundException) { }
            return 96;
        }
        public static bool SendCommand(string directory, int command)
        {
            IntPtr handle = FindWindow(null, Product.Channel(directory));
            return handle != IntPtr.Zero && PostMessage(handle, AppMessage, new IntPtr(command), IntPtr.Zero);
        }
        public static bool CloseApplication(string directory)
        { return CloseChannel(Product.Channel(directory), AppMessage); }
        public static bool CloseLegacyApplication()
        { return CloseChannel(Product.Channel(Product.LegacyDataDirectory).Replace("Memento.IPC.", "Pingo.IPC."), RegisterWindowMessage("Pingo.Notes.Command.v1")); }
        private static bool CloseChannel(string name, uint commandMessage)
        {
            IntPtr handle = FindWindow(null, name);
            if (handle == IntPtr.Zero) return true;
            uint pid;
            GetWindowThreadProcessId(handle, out pid);
            if (!PostMessage(handle, commandMessage, new IntPtr(2), IntPtr.Zero)) return false;
            try { using (Process process = Process.GetProcessById((int)pid)) return process.WaitForExit(8000); }
            catch (ArgumentException) { return true; }
        }
    }
}
