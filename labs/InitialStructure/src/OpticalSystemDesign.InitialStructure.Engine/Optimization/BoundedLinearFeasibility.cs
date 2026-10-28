namespace OpticalSystemDesign.InitialStructure.Engine.Optimization;

/// <summary>Bounded, managed phase-I feasibility fallback with an L1 preference objective. No optical calls.</summary>
internal static class BoundedLinearFeasibility
{
    internal static double[]? Solve(IReadOnlyList<(double Offset, double[] Normal)> constraints,
        IReadOnlyList<double> preferred, CancellationToken token = default)
    {
        var dimension = preferred.Count;
        if (dimension is < 1 or > 12 || preferred.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("A finite preferred point with one to twelve coordinates is required.");
        var rows = new List<(double Offset, double[] Normal)>(constraints);
        for (var i = 0; i < dimension; i++)
        {
            var normal = new double[dimension]; normal[i] = 1; rows.Add((-1, normal));
            rows.Add((-1, normal.Select(v => -v).ToArray()));
        }
        var normalized = new List<(double Offset, double[] Normal)>();
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            if (row.Normal.Length != dimension || !double.IsFinite(row.Offset) || row.Normal.Any(v => !double.IsFinite(v)))
                throw new ArgumentException("Finite halfspaces must match the preferred point.");
            var norm = row.Normal.Aggregate(0.0, double.Hypot);
            if (!double.IsFinite(norm)) throw new ArithmeticException("Non-finite halfspace norm.");
            if (norm <= 1e-14) { if (row.Offset > 1e-10) return null; continue; }
            normalized.Add((row.Offset / norm, row.Normal.Select(v => v / norm).ToArray()));
        }
        // p = preferred + positive - negative; minimize sum(positive + negative).
        // The unit box and every caller-supplied original bound remain explicit rows.
        var variables = 2 * dimension; var count = normalized.Count;
        var table = new double[count + 2, variables + 2];
        var basic = new int[count]; var nonbasic = Enumerable.Range(0, variables).Append(-1).ToArray();
        for (var i = 0; i < count; i++)
        {
            var row = normalized[i]; basic[i] = variables + i;
            for (var j = 0; j < dimension; j++) { table[i, j] = row.Normal[j]; table[i, j + dimension] = -row.Normal[j]; }
            table[i, variables] = -1;
            table[i, variables + 1] = -row.Offset - row.Normal.Zip(preferred, (a, b) => a * b).Sum();
        }
        for (var j = 0; j < variables; j++) table[count, j] = 1;
        table[count + 1, variables] = 1;
        const double epsilon = 1e-11;
        var pivots = 0;
        var initial = Enumerable.Range(0, count).MinBy(i => table[i, variables + 1]);
        if (table[initial, variables + 1] < -epsilon)
        {
            if (!Pivot(initial, variables) || !Optimize(phaseOne: true) || Math.Abs(table[count + 1, variables + 1]) > epsilon) return null;
            for (var i = 0; i < count; i++)
                if (basic[i] == -1)
                {
                    var column = Enumerable.Range(0, variables + 1).Where(j => nonbasic[j] != -1 && Math.Abs(table[i, j]) > epsilon)
                        .OrderBy(j => nonbasic[j]).DefaultIfEmpty(-1).First();
                    if (column >= 0 && !Pivot(i, column)) return null;
                }
        }
        if (!Optimize(phaseOne: false)) return null;
        var split = new double[variables];
        for (var i = 0; i < count; i++) if (basic[i] >= 0 && basic[i] < variables) split[basic[i]] = table[i, variables + 1];
        var point = Enumerable.Range(0, dimension).Select(i => Math.Clamp(preferred[i] + split[i] - split[i + dimension], -1, 1)).ToArray();
        token.ThrowIfCancellationRequested();
        return point.All(double.IsFinite) && rows.All(row => row.Offset + row.Normal.Zip(point, (a, b) => a * b).Sum() <= 1e-10)
            ? point : null;

        bool Optimize(bool phaseOne)
        {
            var objective = phaseOne ? count + 1 : count;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                // Bland ordering keeps degeneracy deterministic without randomized restarts.
                var column = -1;
                for (var j = 0; j <= variables; j++)
                    if ((phaseOne || nonbasic[j] != -1) && table[objective, j] < -epsilon
                        && (column < 0 || nonbasic[j] < nonbasic[column])) column = j;
                if (column < 0) return true;
                var row = -1; var ratio = double.PositiveInfinity;
                for (var i = 0; i < count; i++)
                    if (table[i, column] > epsilon)
                    {
                        var next = table[i, variables + 1] / table[i, column];
                        if (next < ratio - epsilon || Math.Abs(next - ratio) <= epsilon && (row < 0 || basic[i] < basic[row]))
                        { row = i; ratio = next; }
                    }
                if (row < 0 || !Pivot(row, column)) return false;
            }
        }

        bool Pivot(int row, int column)
        {
            token.ThrowIfCancellationRequested();
            if (++pivots > 2048) return false;
            var inverse = 1 / table[row, column];
            if (!double.IsFinite(inverse)) return false;
            for (var i = 0; i < count + 2; i++)
                if (i != row)
                {
                    var factor = table[i, column] * inverse;
                    for (var j = 0; j < variables + 2; j++) if (j != column) table[i, j] -= table[row, j] * factor;
                }
            for (var j = 0; j < variables + 2; j++) if (j != column) table[row, j] *= inverse;
            for (var i = 0; i < count + 2; i++) if (i != row) table[i, column] *= -inverse;
            table[row, column] = inverse;
            (basic[row], nonbasic[column]) = (nonbasic[column], basic[row]);
            return true;
        }
    }
}
