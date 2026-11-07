using System.Text.Json;

namespace OpticalSystemDesign.ZemaxComparison;

/// <summary>Align a full native ray report with an import that folds coordinate breaks into surface poses.</summary>
public static class NativeRaySurfaceMap
{
    public static double[][] ToImportedSurfaces(double[][] rows, JsonElement model, int importedSurfaceCount,
        IReadOnlyList<int>? ignored = null, IReadOnlyList<int>? sourceSurfaceNumbers = null)
    {
        var surfaces = model.GetProperty("surfaces").EnumerateArray().ToArray();
        if (sourceSurfaceNumbers is null && ignored is { Count: > 0 } && importedSurfaceCount != surfaces.Length)
            throw new InvalidDataException("Ignored native rows with folded surfaces require explicit source numbering");
        var expected = Enumerable.Range(1, surfaces.Length - 1).Except(ignored ?? []).ToArray();
        if (rows.Length != expected.Length || rows.Any(row => row.Length != 12)
            || !rows.Select(row => row[0]).SequenceEqual(expected.Select(i => (double)i)))
            throw new InvalidDataException("Incomplete or unordered native real-ray surface table");
        var nativeNumbers = Enumerable.Range(0, surfaces.Length).ToArray();
        if (sourceSurfaceNumbers is not null)
        {
            if (sourceSurfaceNumbers.Count != importedSurfaceCount || sourceSurfaceNumbers.Count < 2
                || sourceSurfaceNumbers[0] != 0 || sourceSurfaceNumbers[^1] != surfaces.Length - 1
                || !sourceSurfaceNumbers.SequenceEqual(nativeNumbers.Intersect(sourceSurfaceNumbers))
                || nativeNumbers.Except(sourceSurfaceNumbers).Any(i => !IsCoordinateBreak(i))
                || (ignored ?? []).Any(i => !sourceSurfaceNumbers.Contains(i)))
                throw new InvalidDataException("Imported source surface numbers do not match the native model");
        }
        if (importedSurfaceCount == surfaces.Length) return rows;

        // Do not guess which reference planes were retained by a merit operand.
        // Nonzero-thickness breaks remain transparent reference planes so their
        // ordered paraxial propagation is retained. Verify the complete count.
        bool IsCoordinateBreak(int i) => surfaces[i].GetProperty("Type").GetString() == "CoordinateBreak";
        bool Retain(int i) => !IsCoordinateBreak(i)
            || surfaces[i].TryGetProperty("Thickness", out var thickness) && thickness.ValueKind == JsonValueKind.Number && thickness.GetDouble() != 0;
        var retained = sourceSurfaceNumbers?.ToArray() ?? nativeNumbers.Where(Retain).ToArray();
        // Historical requests used the fully collapsed import. Keep their map.
        if (retained.Length != importedSurfaceCount)
            retained = nativeNumbers.Where(i => !IsCoordinateBreak(i)).ToArray();
        if (retained.Length != importedSurfaceCount || retained[0] != 0 || retained[^1] != surfaces.Length - 1)
            throw new InvalidDataException("Native/imported surface mapping is ambiguous; explicit source mapping required");

        var mapped = new List<double[]>();
        double distance = 0;
        foreach (var row in rows)
        {
            distance += row[11];
            var importedNumber = Array.IndexOf(retained, (int)row[0]);
            if (importedNumber < 0) continue;
            var copy = (double[])row.Clone();
            copy[0] = importedNumber;
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
