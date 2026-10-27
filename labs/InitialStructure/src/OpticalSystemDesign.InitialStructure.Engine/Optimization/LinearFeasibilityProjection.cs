namespace OpticalSystemDesign.InitialStructure.Engine.Optimization;

/// <summary>Dual coordinate projection onto linear halfspaces, including caller-supplied box bounds. No function probes.</summary>
internal static class LinearFeasibilityProjection
{
    internal static double[]? Solve(IReadOnlyList<(double Offset, double[] Normal)> constraints, int dimension,
        int maximumSweeps = 400, CancellationToken cancellationToken = default, IReadOnlyList<double>? preferredPoint = null)
    {
        var rows = new List<(double Offset, double[] Normal)>();
        foreach (var (offset, normal) in constraints)
        {
            var norm = normal.Aggregate(0.0, double.Hypot);
            if (!double.IsFinite(norm) || !double.IsFinite(offset)) throw new ArithmeticException("Non-finite linear constraint.");
            if (norm <= 1e-14) { if (offset > 1e-10) return null; continue; }
            rows.Add((offset / norm, normal.Select(item => item / norm).ToArray()));
        }
        var multipliers = new double[rows.Count];
        if (preferredPoint is not null && (preferredPoint.Count != dimension || preferredPoint.Any(value => !double.IsFinite(value))))
            throw new ArgumentException("A finite preferred point must match the projection dimension.");
        var point = preferredPoint?.ToArray() ?? new double[dimension];
        for (var sweep = 0; sweep < maximumSweeps; sweep++)
        {
            if (sweep % 16 == 0) cancellationToken.ThrowIfCancellationRequested();
            for (var row = 0; row < rows.Count; row++)
            {
                var item = rows[row];
                var violation = item.Offset;
                for (var column = 0; column < dimension; column++) violation += item.Normal[column] * point[column];
                var next = Math.Max(0, multipliers[row] + violation);
                var change = next - multipliers[row];
                for (var column = 0; column < dimension; column++) point[column] -= change * item.Normal[column];
                multipliers[row] = next;
            }
            double maximum;
            if (maximumSweeps == 400)
                maximum = rows.Select(item => item.Offset + item.Normal.Zip(point, (a, b) => a * b).Sum()).DefaultIfEmpty().Max();
            else
            {
                maximum = 0;
                foreach (var item in rows)
                {
                    var violation = item.Offset;
                    for (var column = 0; column < dimension; column++) violation += item.Normal[column] * point[column];
                    maximum = Math.Max(maximum, violation);
                }
            }
            if (maximum <= 1e-8) return point;
        }
        return null;
    }
}
