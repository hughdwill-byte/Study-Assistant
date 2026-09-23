using StudyHud.Core.Services;

namespace StudyHud.Ocr;

/// <summary>
/// Reconstructs a sensible reading order from OCR word boxes (spec §51). The Windows OCR engine emits
/// lines top-to-bottom, which interleaves the rows of a multi-column layout (two columns become
/// left-row, right-row, left-row…). That scrambles which words sit next to which, hurting the exact
/// key-phrase matching the Question Finder relies on.
///
/// This groups words into columns by their horizontal position and reads each column fully, top to
/// bottom, before the next — so words that are genuinely adjacent on the page ("bending stress") stay
/// adjacent in the text. Purely geometric and deterministic; no OCR re-run and no generative AI. When a
/// page is a single column it collapses to ordinary top-to-bottom reading.
/// </summary>
public static class OcrReadingOrder
{
    public static string Reconstruct(IReadOnlyList<OcrWord> words)
    {
        if (words is null || words.Count == 0) return string.Empty;

        var items = words
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .Select(Box.From)
            .ToList();
        if (items.Count == 0) return string.Empty;
        if (items.Count == 1) return items[0].Text;

        double medW = Median(items.Select(i => i.Width));
        double medH = Median(items.Select(i => i.Height));
        double pageWidth = items.Max(i => i.Right) - items.Min(i => i.Left);

        // A real column gutter is wider than a couple of characters and a decent slice of the page.
        double columnGap = Math.Max(medW * 2.5, pageWidth * 0.08);

        var columns = SplitIntoColumns(items, columnGap);

        // Columns left-to-right; within a column, rows top-to-bottom and words left-to-right.
        var sb = new System.Text.StringBuilder();
        foreach (var col in columns.OrderBy(c => c.Min(i => i.CenterX)))
        {
            if (sb.Length > 0) sb.Append("\n\n");
            AppendColumn(sb, col, medH);
        }
        return sb.ToString();
    }

    private static List<List<Box>> SplitIntoColumns(List<Box> items, double columnGap)
    {
        // Cluster by centre-X: a jump larger than the gutter starts a new column.
        var byX = items.OrderBy(i => i.CenterX).ToList();
        var columns = new List<List<Box>>();
        var current = new List<Box> { byX[0] };
        for (int i = 1; i < byX.Count; i++)
        {
            if (byX[i].CenterX - byX[i - 1].CenterX > columnGap)
            {
                columns.Add(current);
                current = new List<Box>();
            }
            current.Add(byX[i]);
        }
        columns.Add(current);
        return columns;
    }

    private static void AppendColumn(System.Text.StringBuilder sb, List<Box> column, double medH)
    {
        double rowTolerance = Math.Max(4, medH * 0.6);
        var rows = new List<List<Box>>();
        foreach (var box in column.OrderBy(b => b.CenterY))
        {
            var row = rows.Count > 0 ? rows[^1] : null;
            if (row != null && Math.Abs(box.CenterY - row.Average(b => b.CenterY)) <= rowTolerance)
                row.Add(box);
            else
                rows.Add(new List<Box> { box });
        }

        bool firstRow = true;
        foreach (var row in rows)
        {
            if (!firstRow) sb.Append('\n');
            firstRow = false;
            sb.Append(string.Join(' ', row.OrderBy(b => b.Left).Select(b => b.Text)));
        }
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 1;
        return sorted[sorted.Count / 2];
    }

    private readonly record struct Box(string Text, double Left, double Right, double Top, double Bottom)
    {
        public double Width => Math.Max(1, Right - Left);
        public double Height => Math.Max(1, Bottom - Top);
        public double CenterX => (Left + Right) / 2.0;
        public double CenterY => (Top + Bottom) / 2.0;

        public static Box From(OcrWord w) => new(
            w.Text, w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Top, w.BoundingBox.Bottom);
    }
}
