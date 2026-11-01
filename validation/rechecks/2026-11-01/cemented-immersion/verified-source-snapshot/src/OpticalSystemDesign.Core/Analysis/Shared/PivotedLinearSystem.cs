using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

/// <summary>Bounded dense square solve with scaled partial pivoting and a residual check.
/// This is a shared numerical helper; it has no optical or calibration conventions.</summary>
internal static class PivotedLinearSystem
{
    internal static double[] Solve(double[,] matrix, double[] target)
    {
        var n = target.Length;
        if (n is < 1 or > 260 || matrix.GetLength(0) != n || matrix.GetLength(1) != n) throw new ArgumentException("Square solve budget or dimensions invalid.");
        var a = (double[,])matrix.Clone(); var b = (double[])target.Clone(); var scale = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (!double.IsFinite(b[i])) throw new InvalidDataException("Nonfinite linear-system target.");
            for (int j = 0; j < n; j++) { if (!double.IsFinite(a[i,j])) throw new InvalidDataException("Nonfinite linear-system coefficient."); scale[i] = Math.Max(scale[i], Math.Abs(a[i,j])); }
            if (scale[i] == 0) throw new InvalidDataException("Singular linear system.");
        }
        for (int k = 0; k < n; k++)
        {
            ComputationCancellation.ThrowIfCancellationRequested(); var pivot = k;
            for (int i = k + 1; i < n; i++) if (Math.Abs(a[i,k]) / scale[i] > Math.Abs(a[pivot,k]) / scale[pivot]) pivot = i;
            if (Math.Abs(a[pivot,k]) <= 1e-14 * scale[pivot]) throw new InvalidDataException("Ill-conditioned local interpolation system.");
            if (pivot != k) { for (int j = k; j < n; j++) (a[k,j],a[pivot,j]) = (a[pivot,j],a[k,j]); (b[k],b[pivot]) = (b[pivot],b[k]); (scale[k],scale[pivot]) = (scale[pivot],scale[k]); }
            for (int i = k + 1; i < n; i++)
            {
                var multiplier = a[i,k] / a[k,k]; a[i,k] = 0;
                for (int j = k + 1; j < n; j++) a[i,j] -= multiplier * a[k,j];
                b[i] -= multiplier * b[k];
            }
        }
        var result = new double[n];
        for (int i = n - 1; i >= 0; i--) { double value = b[i]; for (int j = i + 1; j < n; j++) value -= a[i,j] * result[j]; result[i] = value / a[i,i]; if (!double.IsFinite(result[i])) throw new InvalidDataException("Linear-system solution overflow."); }
        var reference = Math.Max(1, target.Max(Math.Abs));
        for (int i = 0; i < n; i++) { double value = 0; for (int j = 0; j < n; j++) value += matrix[i,j] * result[j]; if (Math.Abs(value - target[i]) > 1e-8 * reference) throw new InvalidDataException("Local interpolation solve failed its residual check."); }
        return result;
    }
}
