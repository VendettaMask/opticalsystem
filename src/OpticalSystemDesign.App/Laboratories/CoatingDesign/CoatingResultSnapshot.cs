using System.Globalization;
using System.Text;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed record CoatingResultRow(double[] Values);
public sealed record CoatingResultSnapshot(string[] Headers, string[] ColumnIds, CoatingQuantity?[] Quantities, CoatingResultRow[] Rows)
{
    public string Delimited(char delimiter)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Join(delimiter, ColumnIds));
        foreach (var row in Rows) text.AppendLine(string.Join(delimiter, row.Values.Select(v => v.ToString("G17", CultureInfo.InvariantCulture))));
        return text.ToString();
    }
}
