using System.Globalization;

namespace OpticalSystemDesign.Core.FileIO;

public static class CsvCell
{
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var start = 0;
        while (start < text.Length && (char.IsWhiteSpace(text[start]) || text[start] == '\uFEFF')) start++;
        var candidate = text[start..];
        var numeric = double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number);
        if (!numeric && candidate.Length > 0 && (candidate[0] is '=' or '+' or '-' or '@'
                || text[0] is '\t' or '\r' or '\n'))
            text = "'" + text;
        return '"' + text.Replace("\"", "\"\"") + '"';
    }
}
