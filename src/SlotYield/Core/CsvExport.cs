using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SlotYield.Core;

/// <summary>Minimal RFC-4180 CSV for taking a scan into a spreadsheet.</summary>
public static class CsvExport
{
    public static string Escape(string value)
    {
        if (value.Length == 0)
            return value;

        var needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needsQuotes)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static string Build(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', header.Select(Escape))).Append('\n');

        foreach (var row in rows)
            builder.Append(string.Join(',', row.Select(Escape))).Append('\n');

        return builder.ToString();
    }
}
