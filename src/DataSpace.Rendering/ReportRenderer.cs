using DataSpace.Core;
using SkiaSharp;

namespace DataSpace.Rendering;

public sealed class ReportPageLayout
{
    public float Width { get; init; } = 595;
    public float Height { get; init; } = 842;
    public float Margin { get; init; } = 36;
    public float HeaderHeight { get; init; } = 98;
    public float FooterHeight { get; init; } = 38;
    public float RowHeight { get; init; } = 24;
    public int RecordsPerPage => Math.Max(1, (int)((Height - Margin * 2 - HeaderHeight - FooterHeight - 28) / RowHeight));
    public static ReportPageLayout For(ReportDefinition definition) => definition.Landscape ? new() { Width = 842, Height = 595 } : new();
    public int PageCount(int count) => Math.Max(1, (count + RecordsPerPage - 1) / RecordsPerPage);
}

/// <summary>The same measured report renderer targets interactive previews, bitmap exports and vector PDF pages.</summary>
public sealed class ReportRenderer : IDisposable
{
    public OfficeTheme Theme { get; }
    private readonly DrawingResources _drawing;
    public ReportRenderer(OfficeTheme? theme = null) { Theme = theme ?? OfficeTheme.Default; _drawing = new(Theme); }
    public void DrawPage(SKCanvas canvas, ReportDefinition report, IReadOnlyList<FieldDefinition> sourceFields, IReadOnlyList<Record> records, int page, DateTime generatedAt)
    {
        var layout = ReportPageLayout.For(report);
        if (page < 0 || page >= layout.PageCount(records.Count)) throw new ArgumentOutOfRangeException(nameof(page));
        var fields = report.Fields.Count == 0 ? sourceFields.ToList() : report.Fields.Select(name => sourceFields.FirstOrDefault(f => Names.Equal(f.Name, name)) ?? throw new DataSpaceException($"Report field '{name}' does not exist.")).ToList();
        canvas.Clear(SKColors.White);
        var left = layout.Margin; var right = layout.Width - layout.Margin;
        _drawing.Text(canvas, "NORTHWIND  /  DATASPACE", left, 39, Theme.MutedText, 9, true);
        _drawing.Text(canvas, report.Title, left, 76, Theme.Accent, 25);
        _drawing.Text(canvas, $"{records.Count:N0} records   ·   {generatedAt:dd MMM yyyy}", left, 99, Theme.MutedText, 10);
        _drawing.Line(canvas, left, 113, right, 113, Theme.Accent, 1.7f);
        var top = layout.Margin + layout.HeaderHeight;
        var usable = right - left;
        var sumWidths = Math.Max(1, fields.Sum(f => f.Width));
        var columnWidths = fields.Select(f => (float)(f.Width / sumWidths * usable)).ToArray();
        _drawing.Fill(canvas, new(left, top, right, top + 28), SKColor.Parse("E9EEF1"));
        var x = left;
        for (var i = 0; i < fields.Count; i++)
        {
            _drawing.CellText(canvas, fields[i].DisplayName, new(x, top, x + columnWidths[i], top + 28), Theme.Text, 9.5f, true);
            x += columnWidths[i];
        }
        top += 28;
        var first = page * layout.RecordsPerPage; var last = Math.Min(records.Count, first + layout.RecordsPerPage);
        for (var row = first; row < last; row++)
        {
            var y = top + (row - first) * layout.RowHeight;
            if ((row - first) % 2 != 0) _drawing.Fill(canvas, new(left, y, right, y + layout.RowHeight), SKColor.Parse("F4F6F8"));
            x = left;
            for (var column = 0; column < fields.Count; column++)
            {
                var field = fields[column];
                _drawing.CellText(canvas, FieldValues.Display(field, records[row][field.Name]), new(x, y, x + columnWidths[column], y + layout.RowHeight), Theme.Text, 9.5f,
                    right: field.Type is FieldType.Currency or FieldType.Decimal or FieldType.Integer or FieldType.AutoNumber);
                x += columnWidths[column];
            }
            _drawing.Line(canvas, left, y + layout.RowHeight, right, y + layout.RowHeight, SKColor.Parse("E5E9EC"), .5f);
        }
        var footer = layout.Height - layout.Margin;
        _drawing.Line(canvas, left, footer - 20, right, footer - 20, Theme.GridLine);
        _drawing.Text(canvas, "DataSpace  ·  " + report.Name, left, footer, Theme.MutedText, 9);
        var text = $"Page {page + 1} of {layout.PageCount(records.Count)}";
        _drawing.Text(canvas, text, right - _drawing.Font(9).MeasureText(text), footer, Theme.MutedText, 9);
    }
    public byte[] ExportPdf(ReportDefinition report, IReadOnlyList<FieldDefinition> fields, IReadOnlyList<Record> records, DateTime? generatedAt = null)
    {
        var layout = ReportPageLayout.For(report);
        using var stream = new MemoryStream();
        using (var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata { Title = report.Title, Author = "DataSpace", Creator = "DataSpace / SkiaSharp" }))
        {
            if (document is null) throw new DataSpaceException("The Skia PDF backend is unavailable on this platform.");
            for (var page = 0; page < layout.PageCount(records.Count); page++)
            {
                var canvas = document.BeginPage(layout.Width, layout.Height);
                DrawPage(canvas, report, fields, records, page, generatedAt ?? DateTime.Now); document.EndPage();
            }
            document.Close();
        }
        return stream.ToArray();
    }
    public void Dispose() => _drawing.Dispose();
}
