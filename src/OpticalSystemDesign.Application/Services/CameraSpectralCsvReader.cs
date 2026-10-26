using System.Globalization;
using System.Text;

namespace OpticalSystemDesign.Application.Services;

/// <summary>Shared strict UTF-8, unquoted numeric CSV reader; units and curve validation stay with each importer.</summary>
internal static class CameraSpectralCsvReader
{
    internal sealed record Row(int LineNumber, string[] Cells)
    {
        internal string Header => string.Join(',', Cells).ToLowerInvariant();

        internal bool TryNumbers(out double[] values, bool requireFinite)
        {
            values = new double[Cells.Length];
            for (var i = 0; i < Cells.Length; i++)
                if (!double.TryParse(Cells[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])
                    || requireFinite && !double.IsFinite(values[i])) return false;
            return true;
        }
    }

    internal static IEnumerable<Row> Read(byte[] bytes, CancellationToken token)
    {
        var lines = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            yield return new(index + 1, line.Split(',').Select(value => value.Trim()).ToArray());
        }
    }
}
