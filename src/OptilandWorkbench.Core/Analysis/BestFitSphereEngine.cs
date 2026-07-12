using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed record BestFitSphereResult(
    double CenterX,
    double CenterY,
    double CenterZ,
    double Radius,
    int ValidRayCount);

public static class BestFitSphereEngine
{
    public static BestFitSphereResult Calculate(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int numRings = 15)
    {
        var pupilSamples = SpotAnalysisEngine.CreatePupilSamples(Math.Max(1, numRings), "hexapolar");
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            field.Hx,
            field.Hy,
            wavelength.Micrometers,
            pupilSamples);
        var trace = optic.SequentialRayTracer.Trace(bundle);
        var imageIndex = optic.Materials.Resolve(optic.SurfaceGroup.Items[^1].MaterialAfterName)
            .RefractiveIndex(wavelength.Nanometers);
        var maxFieldDegrees = optic.Fields.Select(item => Math.Sqrt(
                (item.XAngleDegrees * item.XAngleDegrees)
                + (item.YAngleDegrees * item.YAngleDegrees)))
            .DefaultIfEmpty(0)
            .Max();
        var tx = Math.Tan(field.Hx * maxFieldDegrees * Math.PI / 180.0);
        var ty = Math.Tan(field.Hy * maxFieldDegrees * Math.PI / 180.0);
        var uz = 1 / Math.Sqrt(1 + (tx * tx) + (ty * ty));
        var ux = tx * uz;
        var uy = ty * uz;
        var entrancePupilRadius = optic.Paraxial.EstimateEntrancePupilDiameter() / 2;
        var points = new List<Vector3D>(pupilSamples.Count);

        for (var index = 0; index < pupilSamples.Count; index++)
        {
            var history = trace.RayHistories[index];
            if (history.Count == 0)
            {
                continue;
            }

            var sample = history[^1];
            if (sample.Intensity <= 0
                || !IsFinite(sample.Position)
                || !IsFinite(sample.Direction)
                || !double.IsFinite(sample.CumulativeOpticalPathLength))
            {
                continue;
            }

            var pupil = pupilSamples[index];
            var tilt = (ux * pupil.X * entrancePupilRadius) + (uy * pupil.Y * entrancePupilRadius);
            var opticalPath = sample.CumulativeOpticalPathLength + tilt;
            points.Add(sample.Position - (sample.Direction * (opticalPath / imageIndex)));
        }

        if (points.Count < 4)
        {
            throw new InvalidOperationException("Need at least four valid rays for a best-fit sphere.");
        }

        var design = new double[points.Count, 4];
        var target = new double[points.Count];
        for (var row = 0; row < points.Count; row++)
        {
            var point = points[row];
            design[row, 0] = point.X;
            design[row, 1] = point.Y;
            design[row, 2] = point.Z;
            design[row, 3] = 1;
            target[row] = (point.X * point.X) + (point.Y * point.Y) + (point.Z * point.Z);
        }

        var parameters = SolveLeastSquares(design, target);
        var centerX = parameters[0] / 2;
        var centerY = parameters[1] / 2;
        var centerZ = parameters[2] / 2;
        var radiusSquared = parameters[3]
            + (centerX * centerX)
            + (centerY * centerY)
            + (centerZ * centerZ);
        return new BestFitSphereResult(
            centerX,
            centerY,
            centerZ,
            Math.Sqrt(Math.Max(0, radiusSquared)),
            points.Count);
    }

    private static double[] SolveLeastSquares(double[,] matrix, double[] target)
    {
        var rows = matrix.GetLength(0);
        var columns = matrix.GetLength(1);
        var q = new double[rows, columns];
        var r = new double[columns, columns];
        var work = (double[,])matrix.Clone();
        for (var column = 0; column < columns; column++)
        {
            var norm = 0.0;
            for (var row = 0; row < rows; row++)
            {
                norm += work[row, column] * work[row, column];
            }

            norm = Math.Sqrt(norm);
            if (norm <= 1e-14)
            {
                continue;
            }

            r[column, column] = norm;
            for (var row = 0; row < rows; row++)
            {
                q[row, column] = work[row, column] / norm;
            }

            for (var next = column + 1; next < columns; next++)
            {
                var projection = 0.0;
                for (var row = 0; row < rows; row++)
                {
                    projection += q[row, column] * work[row, next];
                }

                r[column, next] = projection;
                for (var row = 0; row < rows; row++)
                {
                    work[row, next] -= projection * q[row, column];
                }
            }
        }

        var projected = new double[columns];
        for (var column = 0; column < columns; column++)
        {
            for (var row = 0; row < rows; row++)
            {
                projected[column] += q[row, column] * target[row];
            }
        }

        var result = new double[columns];
        for (var row = columns - 1; row >= 0; row--)
        {
            var value = projected[row];
            for (var column = row + 1; column < columns; column++)
            {
                value -= r[row, column] * result[column];
            }

            result[row] = Math.Abs(r[row, row]) <= 1e-14 ? 0 : value / r[row, row];
        }

        return result;
    }

    private static bool IsFinite(Vector3D value)
    {
        return double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
    }
}
