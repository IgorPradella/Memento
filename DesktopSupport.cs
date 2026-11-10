using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Memento
{
    // Explorer may raise its desktop in the topmost band. A normal HWND_TOP
    // window cannot cover it, so our notes temporarily join that band.
    internal sealed class DesktopSupport : IDisposable
    {
        private delegate void WinEventProc(IntPtr hook, uint eventId, IntPtr window, int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        private readonly WinEventProc callback;
        private readonly IntPtr hook;
        private readonly Timer timer;
        private readonly Func<NoteForm[]> windows;
        private readonly Control dispatcher;
        private bool disposed, desktopLayer, queued;
        public bool DesktopLayerActive { get { return desktopLayer; } }
        public DesktopSupport(Control dispatcher, Func<NoteForm[]> windows)
        {
            this.dispatcher = dispatcher; this.windows = windows;
            callback = delegate { Schedule(); };
            hook = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 2);
            timer = new Timer { Interval = 150 };
            timer.Tick += delegate { Refresh(); }; timer.Start();
        }
        public static void Configure(IntPtr window)
        {
            int value = 1;
            try { DwmSetWindowAttribute(window, 12, ref value, sizeof(int)); } catch (DllNotFoundException) { }
        }
        public static bool IsDesktopClass(string name) { return name == "Progman" || name == "WorkerW"; }
        public static bool IsTopmost(IntPtr window) { return (GetWindowLong(window, -20) & 8) != 0; }
        public static bool IsMinimized(IntPtr window) { return IsIconic(window); }
        private static string ClassName(IntPtr window)
        { StringBuilder name = new StringBuilder(256); GetClassName(window, name, name.Capacity); return name.ToString(); }
        private static bool SameProcess(IntPtr a, IntPtr b)
        { uint x, y; Native.GetWindowThreadProcessId(a, out x); Native.GetWindowThreadProcessId(b, out y); return x != 0 && x == y; }
        private static IntPtr DesktopHost(IntPtr shell)
        {
            if (shell == IntPtr.Zero) return IntPtr.Zero;
            if (FindWindowEx(shell, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return shell;
            IntPtr candidate = IntPtr.Zero;
            while ((candidate = FindWindowEx(IntPtr.Zero, candidate, "WorkerW", null)) != IntPtr.Zero)
                if (SameProcess(shell, candidate) && FindWindowEx(candidate, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return candidate;
            return shell;
        }
        internal static bool ShouldUseDesktopLayer(bool desktopForeground, bool desktopTopmost, bool taskbarForeground, bool noteForeground, bool previous)
        { return desktopForeground || (desktopTopmost && taskbarForeground) || (noteForeground && previous); }
        private void Schedule()
        {
            if (disposed || queued || dispatcher.IsDisposed) return;
            queued = true;
            try { dispatcher.BeginInvoke((Action)delegate { queued = false; Refresh(); }); } catch (InvalidOperationException) { queued = false; }
        }
        private static bool HasWindowAbove(IntPtr window, IntPtr reference)
        {
            IntPtr current = GetWindow(window, 3);
            for (int i = 0; current != IntPtr.Zero && i < 2048; i++, current = GetWindow(current, 3))
                if (current == reference) return true;
            return false;
        }
        private void Refresh()
        {
            if (disposed) return;
            IntPtr shell = GetShellWindow(), host = DesktopHost(shell), foreground = GetForegroundWindow();
            IntPtr root = GetAncestor(foreground, 2);
            if (root == IntPtr.Zero) root = foreground;
            NoteForm[] notes = windows().Where(f => !f.IsDisposed && f.IsHandleCreated && f.TopLevel).ToArray();
            bool noteForeground = notes.Any(f => f.Handle == foreground || f.Handle == root);
            string rootClass = ClassName(root);
            bool desktopForeground = shell != IntPtr.Zero && (root == shell || root == host || (IsDesktopClass(rootClass) && SameProcess(shell, root)));
            bool taskbarForeground = rootClass == "Shell_TrayWnd" || rootClass == "Shell_SecondaryTrayWnd";
            bool next = ShouldUseDesktopLayer(desktopForeground, host != IntPtr.Zero && IsTopmost(host), taskbarForeground, noteForeground, desktopLayer);
            bool transition = next != desktopLayer; desktopLayer = next;
            foreach (NoteForm form in notes)
            {
                if (form.Note.Hidden) continue;
                if (IsIconic(form.Handle))
                {
                    ShowWindowAsync(form.Handle, 4);
                    form.BeginInvoke((Action)delegate { if (!form.IsDisposed) form.RecoverLayout(); });
                }
                bool raise = next && (transition || !IsTopmost(form.Handle) || HasWindowAbove(form.Handle, host));
                bool restore = !next && transition;
                if (raise || restore) ApplyWindowLayer(form, next, root);
            }
        }
        internal static void ApplyWindowLayer(NoteForm form, bool desktop, IntPtr activeWindow)
        {
            const uint flags = 0x1 | 0x2 | 0x10 | 0x200;
            bool topmost = desktop || form.Note.TopMost;
            SetWindowPos(form.Handle, new IntPtr(topmost ? -1 : -2), 0, 0, 0, 0, flags | (desktop && form.Visible ? 0x40u : 0u));
            if (!topmost && activeWindow != IntPtr.Zero && activeWindow != form.Handle && !IsTopmost(activeWindow) && IsWindowVisible(activeWindow))
                SetWindowPos(form.Handle, activeWindow, 0, 0, 0, 0, flags);
        }
        public void Dispose()
        {
            if (disposed) return;
            foreach (NoteForm form in windows()) if (!form.IsDisposed && form.IsHandleCreated && form.TopLevel) ApplyWindowLayer(form, false, IntPtr.Zero);
            disposed = true; timer.Stop(); timer.Dispose(); if (hook != IntPtr.Zero) UnhookWinEvent(hook);
        }
    }
}
