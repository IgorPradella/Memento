using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Memento
{
    public sealed class NoteData
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public string Color { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public float FontSize { get; set; }
        public bool TopMost { get; set; }
        public bool Hidden { get; set; }
        public string Monitor { get; set; }
        public string Updated { get; set; }
        public bool IsTitle { get; set; }
        public int Column { get; set; }
        public int Order { get; set; }
        public NoteData()
        {
            Id = Guid.NewGuid().ToString("N"); Text = ""; Color = "sol";
            Width = 320; Height = 300; FontSize = 13; Monitor = ""; Column = -1; Order = -1;
            Updated = DateTime.UtcNow.ToString("o");
        }
        [ScriptIgnore]
        public Rectangle Bounds { get { return new Rectangle(X, Y, Width, Height); } }
        [ScriptIgnore]
        public string Title
        {
            get
            {
                string first = (Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (String.IsNullOrWhiteSpace(first)) return "Nota sem título";
                return first.Length > 42 ? first.Substring(0, 42) + "…" : first;
            }
        }
    }

    public sealed class AppData
    {
        public int Version { get; set; }
        public List<NoteData> Notes { get; set; }
        public List<NoteData> Trash { get; set; }
        public UserSettings Settings { get; set; }
        public AppData() { Version = 2; Notes = new List<NoteData>(); Trash = new List<NoteData>(); Settings = new UserSettings(); }
    }

    public sealed class UserSettings
    {
        public string DefaultTheme { get; set; }
        public Dictionary<string, string> ThemeNames { get; set; }
        public UserSettings() { DefaultTheme = "sol"; ThemeNames = new Dictionary<string, string>(); }
        public string ThemeName(string key)
        {
            string name;
            return ThemeNames != null && ThemeNames.TryGetValue(key, out name) && !String.IsNullOrWhiteSpace(name) ? name : Palette.Find(key).Name;
        }
        public void Normalize()
        {
            DefaultTheme = Palette.Find(DefaultTheme).Key;
            Dictionary<string, string> names = new Dictionary<string, string>();
            foreach (Palette palette in Palette.All)
            {
                string text = ThemeName(palette.Key).Trim();
                names[palette.Key] = text.Length > 40 ? text.Substring(0, 40) : text;
            }
            ThemeNames = names;
        }
    }

    public sealed class Palette
    {
        public readonly string Key, Name;
        public readonly Color Background, Foreground, Header;
        private Palette(string key, string name, string bg, string fg, string header)
        { Key = key; Name = name; Background = ColorTranslator.FromHtml(bg); Foreground = ColorTranslator.FromHtml(fg); Header = ColorTranslator.FromHtml(header); }
        public static readonly Palette[] All = {
            new Palette("sol", "Sol · amarelo", "#FFF2B3", "#3C3423", "#F7E69A"),
            new Palette("rosa", "Pétala · rosa", "#FCE0E6", "#53333F", "#F3CCD7"),
            new Palette("menta", "Menta · verde", "#D9EEDC", "#2C4733", "#C8E3CC"),
            new Palette("ceu", "Céu · azul", "#DCECFB", "#2C4159", "#CBDEF2"),
            new Palette("lavanda", "Lavanda · lilás", "#E8DFF9", "#413655", "#DACEF0"),
            new Palette("papel", "Papel · branco", "#F8F7F2", "#343631", "#EAE9E1"),
            new Palette("noite", "Noite · preto e branco", "#171819", "#FFFFFF", "#27292B")
        };
        public static Palette Find(string key) { return All.FirstOrDefault(p => p.Key == key) ?? All[0]; }
    }

    public static class BoundsPolicy
    {
        public static Rectangle Fit(Rectangle desired, Rectangle[] workingAreas)
        {
            if (workingAreas == null || workingAreas.Length == 0) return desired;
            Rectangle best = workingAreas[0];
            long bestScore = -1;
            foreach (Rectangle screen in workingAreas)
            {
                Rectangle intersection = Rectangle.Intersect(desired, screen);
                long area = (long)Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height);
                if (area > bestScore) { best = screen; bestScore = area; }
            }
            int width = Math.Min(best.Width, Math.Max(230, Math.Min(2400, desired.Width)));
            int height = Math.Min(best.Height, Math.Max(100, Math.Min(4000, desired.Height)));
            return new Rectangle(Math.Max(best.Left, Math.Min(desired.X, best.Right - width)),
                Math.Max(best.Top, Math.Min(desired.Y, best.Bottom - height)), width, height);
        }
    }

    public sealed class NoteStore
    {
        public readonly string DirectoryPath;
        public string FilePath { get { return Path.Combine(DirectoryPath, "notas.json"); } }
        public string BackupPath { get { return FilePath + ".bak"; } }
        public bool Recovered { get; private set; }
        public string Warning { get; private set; }
        public NoteStore(string directory) { DirectoryPath = Path.GetFullPath(directory); }
        private static JavaScriptSerializer Serializer()
        { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 64 }; }
        public static AppData Read(string path)
        {
            FileInfo info = new FileInfo(path);
            if (info.Length > 32 * 1024 * 1024) throw new InvalidDataException("O arquivo de notas excede 32 MB.");
            AppData data = Serializer().Deserialize<AppData>(File.ReadAllText(path, Encoding.UTF8));
            if (data == null || (data.Version != 1 && data.Version != 2) || data.Notes == null) throw new InvalidDataException("Formato de notas não reconhecido.");
            data.Version = 2;
            if (data.Settings == null) data.Settings = new UserSettings();
            data.Settings.Normalize();
            if (data.Trash == null) data.Trash = new List<NoteData>();
            HashSet<string> ids = new HashSet<string>();
            foreach (NoteData note in data.Notes.Concat(data.Trash))
            {
                if (note == null) throw new InvalidDataException("Há uma nota inválida no arquivo.");
                if (String.IsNullOrWhiteSpace(note.Id) || !ids.Add(note.Id)) { note.Id = Guid.NewGuid().ToString("N"); ids.Add(note.Id); }
                note.Text = note.Text ?? "";
                note.Color = Palette.Find(note.Color).Key;
                if (Single.IsNaN(note.FontSize) || Single.IsInfinity(note.FontSize)) note.FontSize = 13;
                note.FontSize = Math.Max(9, Math.Min(32, note.FontSize));
                note.Width = GridMetrics.LogicalWidth;
                note.Height = Math.Max(note.IsTitle ? 150 : 300, Math.Min(4000, note.Height));
                note.Column = Math.Max(-1, Math.Min(40, note.Column));
                note.X = Math.Max(-100000, Math.Min(100000, note.X));
                note.Y = Math.Max(-100000, Math.Min(100000, note.Y));
            }
            return data;
        }
        public bool MigrateLegacy(string legacyDirectory)
        {
            if (File.Exists(FilePath) || File.Exists(BackupPath)) return false;
            NoteStore old = new NoteStore(legacyDirectory);
            string source = File.Exists(old.FilePath) ? old.FilePath : old.BackupPath;
            if (!File.Exists(source)) return false;
            AppData legacy;
            try { legacy = Read(source); }
            catch { if (source == old.BackupPath || !File.Exists(old.BackupPath)) throw; source = old.BackupPath; legacy = Read(source); }
            Directory.CreateDirectory(DirectoryPath);
            // Preserve the source as-is; migration never rewrites the Pingo directory.
            File.Copy(source, Path.Combine(DirectoryPath, "importado-do-Pingo-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"), false);
            Save(legacy);
            return true;
        }
        public AppData Load()
        {
            if (!File.Exists(FilePath))
            {
                if (File.Exists(BackupPath)) { AppData backup = Read(BackupPath); Recovered = true; Warning = "As notas foram recuperadas da cópia de segurança."; return backup; }
                return new AppData();
            }
            try { return Read(FilePath); }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException)) throw;
                // Keep the original bytes before allowing the next automatic save.
                string preserved = Path.Combine(DirectoryPath, "notas-recuperacao-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json");
                File.Copy(FilePath, preserved, false);
                if (File.Exists(BackupPath))
                {
                    AppData backup = Read(BackupPath);
                    Recovered = true; Warning = "Uma cópia de segurança foi recuperada. O arquivo original foi preservado em:\n" + preserved;
                    File.Delete(FilePath); // Avoid replacing the good backup with the damaged primary.
                    return backup;
                }
                throw new InvalidDataException("Não foi possível ler as notas. O original foi preservado em:\n" + preserved, error);
            }
        }
        public void Save(AppData data)
        {
            Directory.CreateDirectory(DirectoryPath);
            string temporary = Path.Combine(DirectoryPath, "notas-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(Serializer().Serialize(data));
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, BackupPath, true);
                else File.Move(temporary, FilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public void Export(AppData data, string destination)
        {
            // Use the same atomic writer; exporting never changes the live data directory.
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, Serializer().Serialize(data), new UTF8Encoding(false));
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
