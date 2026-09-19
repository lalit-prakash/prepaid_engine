using System.Text;

namespace PrepaidEngine.Api.Reports.ReportJobs;

/// <summary>Writes RFC 4180 CSV. Cells that a spreadsheet would run as a formula are defused, because report cells hold text
/// people typed (names, references) and an export is often opened in Excel.</summary>
public static class Csv
{
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        // A cell starting with = + - @ (or a tab or carriage return) can be run as a formula: prefix a quote to keep it text.
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static async Task WriteRowAsync(StreamWriter writer, IEnumerable<string?> cells)
        => await writer.WriteAsync(string.Join(',', cells.Select(Cell)) + "\r\n");

    /// <summary>UTF-8 with a byte-order mark, so Excel reads the file as UTF-8 (rupee signs, names).</summary>
    public static StreamWriter Open(Stream stream) => new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024, leaveOpen: false);
}
