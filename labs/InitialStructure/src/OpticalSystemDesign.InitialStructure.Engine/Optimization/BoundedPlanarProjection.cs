namespace OpticalSystemDesign.InitialStructure.Engine.Optimization;

/// <summary>Projection onto two-dimensional halfspaces within the caller's unit step box. No optical evaluations.</summary>
internal static class BoundedPlanarProjection
{
    internal static double[]? Solve(IReadOnlyList<(double Offset, double[] Normal)> constraints,
        IReadOnlyList<double> preferred, CancellationToken cancellationToken = default)
    {
        if (preferred.Count != 2 || preferred.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("A finite two-dimensional preferred point is required.");
        var polygon = new List<(double X, double Y)> { (-1, -1), (1, -1), (1, 1), (-1, 1) };
        foreach (var (offset, normal) in constraints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (normal.Length != 2 || normal.Any(v => !double.IsFinite(v)) || !double.IsFinite(offset))
                throw new ArgumentException("Finite two-dimensional halfspaces are required.");
            var norm = double.Hypot(normal[0], normal[1]);
            if (norm <= 1e-14) { if (offset > 1e-10) return null; continue; }
            var nx = normal[0] / norm; var ny = normal[1] / norm; var c = offset / norm;
            var clipped = new List<(double X, double Y)>();
            var previous = polygon[^1]; var previousValue = c + nx * previous.X + ny * previous.Y;
            foreach (var next in polygon)
            {
                var nextValue = c + nx * next.X + ny * next.Y;
                if ((previousValue <= 0) != (nextValue <= 0))
                {
                    var t = Math.Clamp(previousValue / (previousValue - nextValue), 0, 1);
                    clipped.Add((previous.X + t * (next.X - previous.X), previous.Y + t * (next.Y - previous.Y)));
                }
                if (nextValue <= 0) clipped.Add(next);
                previous = next; previousValue = nextValue;
            }
            if (clipped.Count == 0) return null;
            polygon = clipped;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var preferredArray = preferred.ToArray();
        if (Valid(preferredArray)) return preferredArray;
        double[]? best = null; var distance = double.PositiveInfinity;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
            var dx = b.X - a.X; var dy = b.Y - a.Y; var square = dx * dx + dy * dy;
            var t = square == 0 ? 0 : Math.Clamp(((preferred[0] - a.X) * dx + (preferred[1] - a.Y) * dy) / square, 0, 1);
            var candidate = new[] { a.X + t * dx, a.Y + t * dy };
            var d = Math.Pow(candidate[0] - preferred[0], 2) + Math.Pow(candidate[1] - preferred[1], 2);
            if (d < distance && Valid(candidate)) { best = candidate; distance = d; }
        }
        return best;

        bool Valid(double[] point) => point.All(v => double.IsFinite(v) && v >= -1 && v <= 1)
            && constraints.All(row => row.Offset + row.Normal[0] * point[0] + row.Normal[1] * point[1] <= 1e-10);
    }
}
