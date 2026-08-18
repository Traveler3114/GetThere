using System.Globalization;
using System.Text;

namespace TransitInfoAPI.Routing.Export;

/// <summary>
/// A minimal, correct GTFS CSV writer: RFC 4180 quoting, invariant-culture numeric formatting, and
/// GTFS's <c>HH:MM:SS</c> time encoding (which may exceed 24:00:00 for trips past midnight). Kept
/// tiny and dependency-free so the exporter stays easy to reason about and test.
/// </summary>
public sealed class GtfsCsvWriter(Stream stream) : IDisposable
{
    // Not leaveOpen: this writer owns its zip entry stream, and ZipArchive (Create mode) refuses to
    // open the next entry until the current one is closed. Disposing the writer closes it.
    private readonly StreamWriter _writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    public void WriteHeader(params string[] columns) => WriteRow(columns);

    /// <summary>Writes one row. Each cell is escaped; a null becomes an empty field.</summary>
    public void WriteRow(params string?[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0)
                _writer.Write(',');
            _writer.Write(Escape(cells[i]));
        }
        _writer.Write("\r\n"); // GTFS/CSV convention is CRLF
    }

    public void Flush() => _writer.Flush();

    // Flushes and closes the zip entry stream so the next entry can be created.
    public void Dispose() => _writer.Dispose();

    /// <summary>Formats a coordinate with enough precision (~1cm) and no thousands separators.</summary>
    public static string Coord(double value) => value.ToString("0.0#######", CultureInfo.InvariantCulture);

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>GTFS date is <c>YYYYMMDD</c>.</summary>
    public static string Date(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>
    /// GTFS time is seconds-after-midnight rendered <c>H:MM:SS</c>, and is allowed to exceed 24 hours
    /// so a 00:30 departure on a service that began the previous day reads 24:30:00.
    /// </summary>
    public static string Time(int secondsAfterMidnight)
    {
        var h = secondsAfterMidnight / 3600;
        var m = secondsAfterMidnight % 3600 / 60;
        var s = secondsAfterMidnight % 60;
        return string.Create(CultureInfo.InvariantCulture, $"{h:D2}:{m:D2}:{s:D2}");
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuoting)
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
