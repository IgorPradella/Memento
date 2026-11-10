using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Memento
{
    public sealed class GridMetrics
    {
        public const int LogicalWidth = 320;
        public readonly Rectangle Area;
        public readonly int Width, Gap, Margin, Columns, Left, Top, Height, NormalHeight, TitleHeight;
        public GridMetrics(Rectangle area, int dpi)
        {
            Area = area;
            double scale = Math.Max(96, dpi) / 96.0;
            Width = Math.Min(area.Width, (int)Math.Round(LogicalWidth * scale));
            Gap = (int)Math.Round(20 * scale); Margin = Math.Min((int)Math.Round(24 * scale), Math.Max(0, (area.Height - 150) / 2));
            NormalHeight = (int)Math.Round(300 * scale); TitleHeight = (int)Math.Round(150 * scale);
            Columns = Math.Max(1, (area.Width - 2 * Margin + Gap) / (Width + Gap));
            Left = area.Left + Math.Max(0, (area.Width - (Columns * Width + (Columns - 1) * Gap)) / 2);
            Top = area.Top + Margin; Height = Math.Max(1, area.Height - 2 * Margin);
        }
        public int X(int column) { return Left + Math.Max(0, Math.Min(Columns - 1, column)) * (Width + Gap); }
        public int NearestColumn(int x) { return Math.Max(0, Math.Min(Columns - 1, (int)Math.Round((x - Left) / (double)(Width + Gap)))); }
        public int Minimum(NoteData note) { return Math.Min(Height, note.IsTitle ? TitleHeight : NormalHeight); }
    }

    public sealed class LayoutRequest
    {
        public NoteData Note;
        public int DesiredHeight;
        public LayoutRequest(NoteData note, int desired) { Note = note; DesiredHeight = desired; }
    }
    public sealed class LayoutPlacement
    {
        public NoteData Note;
        public Rectangle Bounds;
        public bool Scroll;
    }
    public static class GridLayout
    {
        public static bool CanAdd(GridMetrics grid, IEnumerable<NoteData> notes, int column, bool title)
        {
            NoteData[] list = notes.Where(n => !n.Hidden && n.Column == column).ToArray();
            return list.Sum(n => grid.Minimum(n)) + list.Length * grid.Gap + Math.Min(grid.Height, title ? grid.TitleHeight : grid.NormalHeight) <= grid.Height;
        }
        public static int FindSpace(GridMetrics grid, IEnumerable<NoteData> notes, bool title, int preferred)
        {
            NoteData[] list = notes.Where(n => !n.Hidden).ToArray();
            if (preferred >= 0 && preferred < grid.Columns && CanAdd(grid, list, preferred, title) && (!title || !list.Any(n => n.Column == preferred && n.IsTitle))) return preferred;
            for (int offset = 0; offset < grid.Columns; offset++)
            {
                int column = preferred < 0 ? offset : (preferred + 1 + offset) % grid.Columns;
                if (CanAdd(grid, list, column, title) && (!title || !list.Any(n => n.Column == column && n.IsTitle))) return column;
            }
            return -1;
        }
        public static List<LayoutPlacement> Arrange(GridMetrics grid, IList<LayoutRequest> requests, string changedId)
        {
            List<LayoutPlacement> result = new List<LayoutPlacement>();
            for (int column = 0; column < grid.Columns; column++)
            {
                LayoutRequest[] list = requests.Where(r => !r.Note.Hidden && r.Note.Column == column).OrderBy(r => r.Note.Order).ThenBy(r => r.Note.Id).ToArray();
                if (list.Length == 0) continue;
                int budget = grid.Height - (list.Length - 1) * grid.Gap;
                int[] minimum = list.Select(r => grid.Minimum(r.Note)).ToArray();
                if (minimum.Sum() > budget) throw new InvalidOperationException("A coluna não tem espaço. Mova uma nota para outra coluna ou guarde-a na bandeja.");
                int[] wanted = list.Select((r, i) => r.Note.IsTitle ? minimum[i] : Math.Max(minimum[i], r.DesiredHeight)).ToArray();
                int[] heights = list.Select((r, i) => Math.Max(minimum[i], Math.Min(wanted[i], r.Note.Height))).ToArray();
                int excess = heights.Sum() - budget;
                // Existing heights have priority. When the screen changes, reduce extra space from the bottom.
                for (int i = heights.Length - 1; i >= 0 && excess > 0; i--)
                { int reduction = Math.Min(excess, heights[i] - minimum[i]); heights[i] -= reduction; excess -= reduction; }
                int remaining = budget - heights.Sum();
                int changed = Array.FindIndex(list, r => r.Note.Id == changedId);
                if (changed >= 0) { int add = Math.Min(remaining, wanted[changed] - heights[changed]); heights[changed] += add; remaining -= add; }
                for (int i = 0; i < list.Length && remaining > 0; i++)
                { int add = Math.Min(remaining, wanted[i] - heights[i]); heights[i] += add; remaining -= add; }
                int y = grid.Top;
                for (int i = 0; i < list.Length; i++)
                {
                    result.Add(new LayoutPlacement { Note = list[i].Note, Bounds = new Rectangle(grid.X(column), y, grid.Width, heights[i]), Scroll = wanted[i] > heights[i] || (list[i].Note.IsTitle && list[i].DesiredHeight > heights[i]) });
                    y += heights[i] + grid.Gap;
                }
            }
            return result;
        }
    }
}
