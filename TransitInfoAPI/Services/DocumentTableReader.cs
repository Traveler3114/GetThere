using System.Globalization;
using System.Text;

using ClosedXML.Excel;

using CsvHelper;
using CsvHelper.Configuration;

using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace TransitInfoAPI.Services;

/// <summary>
/// Turns an uploaded spreadsheet, CSV or PDF into the same flat rows the HTTP engine produces, so
/// mappings work identically whichever way the data arrived.
/// </summary>
public static class DocumentTableReader
{
    /// <summary>
    /// First sheet, first row as headers — the shape a small operator's timetable export has.
    /// <para>
    /// <b>Unbounded in memory relative to the upload size.</b> An .xlsx is a zip, and
    /// <c>XLWorkbook</c> materialises the whole model — every sheet, not just the first one this
    /// reads. The upload endpoint caps the <em>compressed</em> bytes at 64 MB
    /// (<c>CustomSourcesController.Upload</c>), which says nothing about what they expand to: a
    /// deliberately-crafted or merely enormous workbook expands far past that, and the failure is an
    /// <c>OutOfMemoryException</c> rather than a rejected upload.
    /// </para>
    /// <para>
    /// Not treated as a live vulnerability because reaching it needs <c>CustomSourcesManage</c> —
    /// an admin can already run arbitrary imports — but it is a foot-gun on a legitimate large
    /// workbook, not only a hostile one. Fixing it properly means a streaming reader
    /// (<c>OpenXml</c>'s <c>SAX</c> path) rather than a size check, since the compressed size is not
    /// the quantity that matters.
    /// </para>
    /// </summary>
    public static List<Dictionary<string, object?>> ReadXlsx(byte[] content, List<string> warnings)
    {
        var rows = new List<Dictionary<string, object?>>();

        using var stream = new MemoryStream(content);
        using var workbook = new XLWorkbook(stream);

        var sheet = workbook.Worksheets.FirstOrDefault();
        if (sheet is null)
        {
            warnings.Add("Workbook has no sheets");
            return rows;
        }

        var used = sheet.RangeUsed();
        if (used is null)
        {
            warnings.Add($"Sheet '{sheet.Name}' is empty");
            return rows;
        }

        var allRows = used.RowsUsed().ToList();
        if (allRows.Count < 2)
        {
            warnings.Add($"Sheet '{sheet.Name}' has no data rows below its header");
            return rows;
        }

        var headers = allRows[0].Cells().Select(c => c.GetString().Trim()).ToList();

        foreach (var row in allRows.Skip(1))
        {
            var record = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var cells = row.Cells().ToList();
            for (var i = 0; i < cells.Count && i < headers.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(headers[i]))
                    record[headers[i]] = cells[i].GetString().Trim();
            }
            if (record.Count > 0) rows.Add(record);
        }

        return rows;
    }

    public static List<Dictionary<string, object?>> ReadCsv(byte[] content, List<string> warnings)
    {
        var rows = new List<Dictionary<string, object?>>();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = null
        };

        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8);
        using var csv = new CsvReader(reader, config);

        if (!csv.Read() || !csv.ReadHeader())
        {
            warnings.Add("CSV has no header row");
            return rows;
        }

        while (csv.Read())
        {
            var record = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in csv.HeaderRecord ?? [])
            {
                if (!string.IsNullOrWhiteSpace(header))
                    record[header] = csv.GetField(header);
            }
            rows.Add(record);
        }

        return rows;
    }

    /// <summary>
    /// Recovers a table from a PDF by grouping words into rows and columns by position.
    /// <para>
    /// A PDF has no table structure — only glyphs at coordinates — so this reconstructs one:
    /// words sharing a baseline are a row, and the x-positions of the first row's words define the
    /// column boundaries every later row is snapped to. That holds for the ruled, monospaced grids
    /// operators actually publish and breaks on anything with merged or wrapped cells.
    /// </para>
    /// <para>
    /// It is deliberately a starting point rather than an answer: the extraction is exposed in the
    /// editor's preview so a human can see what came out before anything is imported, and a layout
    /// this cannot read is a case for an <see cref="Core.ICustomExtractor"/>.
    /// </para>
    /// <para>
    /// <b>One page.</b> <c>CustomSourceEngine.ReadUploadedRows</c> is the only caller and never
    /// passes <paramref name="pageNumber"/>, so a 40-page timetable imports its first page and
    /// nothing else. That is a real limit rather than an oversight — the column geometry is derived
    /// per page from that page's header row, and operator PDFs repeat the header, shift the columns,
    /// or split a route across pages often enough that concatenating them blind produces plausible
    /// rows out of the wrong columns. It warns rather than guessing, so the omission is visible in
    /// the preview instead of surfacing later as a feed missing four fifths of its stops.
    /// </para>
    /// </summary>
    public static List<Dictionary<string, object?>> ReadPdf(byte[] content, List<string> warnings, int pageNumber = 1)
    {
        var rows = new List<Dictionary<string, object?>>();

        using var pdf = PdfDocument.Open(content);
        if (pdf.NumberOfPages < pageNumber)
        {
            warnings.Add($"PDF has {pdf.NumberOfPages} page(s); page {pageNumber} was requested");
            return rows;
        }

        if (pdf.NumberOfPages > 1)
        {
            warnings.Add(
                $"PDF has {pdf.NumberOfPages} pages and only page {pageNumber} was read — "
                + "the other pages are not imported. Split the file, or use a named extractor.");
        }

        var page = pdf.GetPage(pageNumber);
        var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        if (words.Count == 0)
        {
            warnings.Add($"Page {pageNumber} has no extractable text — it may be a scan, which needs OCR");
            return rows;
        }

        // Words within half a line-height of each other share a baseline. Using a fraction of the
        // observed glyph height rather than a fixed tolerance keeps this working across font sizes.
        var lineTolerance = Math.Max(words.Average(w => w.BoundingBox.Height) * 0.5, 1.0);

        var lines = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            var line = lines.FirstOrDefault(l =>
                Math.Abs(l[0].BoundingBox.Bottom - word.BoundingBox.Bottom) <= lineTolerance);

            if (line is null) lines.Add([word]);
            else line.Add(word);
        }

        foreach (var line in lines)
            line.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));

        if (lines.Count < 2)
        {
            warnings.Add($"Page {pageNumber} produced {lines.Count} line(s) — not enough to read as a table");
            return rows;
        }

        var headerLine = lines[0];
        var headers = headerLine.Select(w => w.Text.Trim()).ToList();
        var columnStarts = headerLine.Select(w => w.BoundingBox.Left).ToList();

        foreach (var line in lines.Skip(1))
        {
            var record = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var word in line)
            {
                // Snap to the nearest column whose start is at or left of this word.
                var column = 0;
                for (var i = 0; i < columnStarts.Count; i++)
                {
                    if (word.BoundingBox.Left >= columnStarts[i] - lineTolerance) column = i;
                }

                var key = column < headers.Count && headers[column].Length > 0 ? headers[column] : "col" + column;
                record[key] = record.TryGetValue(key, out var existing) && existing is string prior
                    ? prior + " " + word.Text.Trim()
                    : word.Text.Trim();
            }

            if (record.Count > 0) rows.Add(record);
        }

        warnings.Add($"Read {rows.Count} row(s) from PDF page {pageNumber} using {headers.Count} column(s) — check the preview before importing");
        return rows;
    }
}
