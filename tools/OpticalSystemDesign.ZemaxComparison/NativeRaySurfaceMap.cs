using System.Text.Json;

namespace OpticalSystemDesign.ZemaxComparison;

/// <summary>Align a full native ray report with an import that folds coordinate breaks into surface poses.</summary>
public static class NativeRaySurfaceMap
{
    public static double[][] ToImportedSurfaces(double[][] rows, JsonElement model, int importedSurfaceCount)
    {
        var surfaces = model.GetProperty("surfaces").EnumerateArray().ToArray();
        if (rows.Length != surfaces.Length - 1 || rows.Any(row => row.Length != 12)
            || !rows.Select(row => row[0]).SequenceEqual(Enumerable.Range(1, surfaces.Length - 1).Select(i => (double)i)))
            throw new InvalidDataException("Incomplete or unordered native real-ray surface table");
        if (importedSurfaceCount == surfaces.Length) return rows;

        // Do not guess which reference planes were retained by a merit operand.
        // This mapping is valid only when every coordinate break was folded and
        // every other source surface was retained, as proved by both model counts.
        var retained = Enumerable.Range(0, surfaces.Length)
            .Where(i => surfaces[i].GetProperty("Type").GetString() != "CoordinateBreak").ToArray();
        if (retained.Length != importedSurfaceCount || retained[0] != 0 || retained[^1] != surfaces.Length - 1)
            throw new InvalidDataException("Native/imported surface mapping is ambiguous; explicit source mapping required");

        var mapped = new List<double[]>();
        double distance = 0;
        foreach (var row in rows)
        {
            distance += row[11];
            if (surfaces[(int)row[0]].GetProperty("Type").GetString() == "CoordinateBreak") continue;
            var copy = (double[])row.Clone();
            copy[0] = mapped.Count + 1;
            // Distances have already been converted to physical propagation.
            // A folded coordinate plane still divides a real segment in the
            // native report, so its length belongs to the next physical hit.
            copy[11] = distance;
            distance = 0;
            mapped.Add(copy);
        }
        return mapped.ToArray();
    }
}
