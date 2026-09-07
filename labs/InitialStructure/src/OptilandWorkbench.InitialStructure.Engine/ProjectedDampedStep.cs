namespace OptilandWorkbench.InitialStructure.Engine;

/// <summary>Generic bound handling for a scaled least-squares problem; contains no optical evaluation.</summary>
internal static class ProjectedDampedStep
{
    public static double[] Solve(double[,] jacobian, IReadOnlyList<double> residuals, double damping,
        double[] vector, Func<IReadOnlyList<double>, double[]> project)
    {
        var constrained = (double[,])jacobian.Clone();
        var direction = FlatStartBootstrap.DampedStep(constrained, residuals, damping);
        var blocked = new bool[vector.Length];
        for (var pass = 0; pass < vector.Length; pass++)
        {
            var changed = false;
            for (var column = 0; column < vector.Length; column++)
            {
                if (blocked[column] || Math.Abs(direction[column]) < 1e-15) continue;
                var probe = vector.ToArray();
                probe[column] += Math.CopySign(1e-7, direction[column]);
                if (Math.Abs(project(probe)[column] - vector[column]) > 1e-12) continue;
                for (var row = 0; row < residuals.Count; row++) constrained[row, column] = 0;
                blocked[column] = true;
                changed = true;
            }
            if (!changed) break;
            direction = FlatStartBootstrap.DampedStep(constrained, residuals, damping);
        }
        var projected = project(vector.Select((value, index) => value + direction[index]).ToArray());
        return projected.Select((value, index) => value - vector[index]).ToArray();
    }
}
