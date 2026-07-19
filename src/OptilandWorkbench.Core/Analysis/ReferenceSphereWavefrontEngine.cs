using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Analysis;

public enum ReferenceSphereStrategy
{
    CentroidSphere,
    BestFitSphere
}

public sealed record ReferenceSphereWavefrontResult(
    IReadOnlyList<WavefrontSample> Samples,
    double CenterX,
    double CenterY,
    double CenterZ,
    double Radius,
    double MeanReferenceOpticalPath,
    int VignettedRayCount)
{
    public double Rms => Samples.Where(sample => sample.Intensity > 0)
        .Select(sample => sample.OpdWaves * sample.OpdWaves)
        .DefaultIfEmpty(0)
        .Average() is var mean ? Math.Sqrt(mean) : 0;
}

public static class ReferenceSphereWavefrontEngine
{
    public static ReferenceSphereWavefrontResult Generate(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int numRings,
        ReferenceSphereStrategy strategy,
        double robustTrimStandardDeviations = 3)
    {
        var pupilSamples = SpotAnalysisEngine.CreatePupilSamples(Math.Max(1, numRings), "hexapolar");
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            field.Hx,
            field.Hy,
            wavelength.Micrometers,
            pupilSamples);
        var trace = optic.SequentialRayTracer.Trace(bundle);
        var imageIndex = optic.SurfaceGroup.Items[^1].MaterialAfter
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
        var rays = new List<PreparedRay>(pupilSamples.Count);
        for (var index = 0; index < pupilSamples.Count; index++)
        {
            var history = trace.RayHistories[index];
            if (history.Count == 0)
            {
                continue;
            }

            var sample = history[^1];
            var pupil = pupilSamples[index];
            var tilt = (ux * pupil.X * entrancePupilRadius) + (uy * pupil.Y * entrancePupilRadius);
            rays.Add(new PreparedRay(
                pupil,
                sample,
                sample.CumulativeOpticalPathLength + tilt,
                sample.Intensity > 0
                    && IsFinite(sample.Position)
                    && IsFinite(sample.Direction)
                    && double.IsFinite(sample.CumulativeOpticalPathLength)));
        }

        var valid = rays.Where(ray => ray.IsValid).ToArray();
        if (valid.Length < 4)
        {
            throw new InvalidOperationException("Need at least four valid rays for a reference sphere.");
        }

        var wavefrontPoints = valid.Select(ray =>
            ray.Sample.Position - (ray.Sample.Direction * (ray.CorrectedOpticalPath / imageIndex))).ToArray();
        var sphere = strategy == ReferenceSphereStrategy.BestFitSphere
            ? FitSphere(wavefrontPoints)
            : CentroidSphere(valid, wavefrontPoints, robustTrimStandardDeviations);
        var propagated = rays.Select(ray =>
        {
            var imagePath = ImageToReferenceSphere(ray.Sample, sphere.CenterX, sphere.CenterY, sphere.CenterZ, sphere.Radius, imageIndex);
            return new PropagatedRay(ray, imagePath, ray.CorrectedOpticalPath - imagePath);
        }).ToArray();
        var meanReferencePath = propagated.Where(ray => ray.Ray.IsValid)
            .Select(ray => ray.ReferenceOpticalPath)
            .Average();
        var samples = propagated.Select(ray =>
        {
            var t = imageIndex <= 1e-30 ? 0 : ray.ImagePath / imageIndex;
            var pupilPosition = ray.Ray.Sample.Position - (ray.Ray.Sample.Direction * t);
            return new WavefrontSample(
                ray.Ray.Pupil.X,
                ray.Ray.Pupil.Y,
                pupilPosition.X,
                pupilPosition.Y,
                pupilPosition.Z,
                (meanReferencePath - ray.ReferenceOpticalPath) / (wavelength.Micrometers * 1e-3),
                ray.Ray.Sample.Intensity);
        }).ToArray();

        return new ReferenceSphereWavefrontResult(
            samples,
            sphere.CenterX,
            sphere.CenterY,
            sphere.CenterZ,
            sphere.Radius,
            meanReferencePath,
            pupilSamples.Count - valid.Length);
    }

    private static Sphere CentroidSphere(
        IReadOnlyList<PreparedRay> rays,
        IReadOnlyList<Vector3D> wavefrontPoints,
        double robustTrimStandardDeviations)
    {
        var imagePoints = rays.Select(ray => ray.Sample.Position).ToArray();
        var included = Enumerable.Repeat(true, imagePoints.Length).ToArray();
        var center = Mean(imagePoints, included);
        if (robustTrimStandardDeviations > 0)
        {
            var distances = imagePoints.Select(point => (point - center).Length).ToArray();
            var mean = distances.Average();
            var standardDeviation = Math.Sqrt(distances.Select(value => (value - mean) * (value - mean)).Average());
            if (standardDeviation > 0)
            {
                included = distances.Select(distance => distance <= mean + (robustTrimStandardDeviations * standardDeviation)).ToArray();
                if (included.Count(value => value) >= 4)
                {
                    center = Mean(imagePoints, included);
                }
                else
                {
                    included = Enumerable.Repeat(true, imagePoints.Length).ToArray();
                }
            }
        }

        var radius = wavefrontPoints.Select((point, index) => (Point: point, Included: included[index]))
            .Where(item => item.Included)
            .Average(item => (item.Point - center).Length);
        return new Sphere(center.X, center.Y, center.Z, radius);
    }

    private static Sphere FitSphere(IReadOnlyList<Vector3D> points)
    {
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
        var radius = Math.Sqrt(Math.Max(0, parameters[3]
            + (centerX * centerX)
            + (centerY * centerY)
            + (centerZ * centerZ)));
        return new Sphere(centerX, centerY, centerZ, radius);
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

    private static double ImageToReferenceSphere(
        RayTraceSample ray,
        double centerX,
        double centerY,
        double centerZ,
        double radius,
        double imageIndex)
    {
        var direction = ray.Direction * -1;
        var relative = ray.Position - new Vector3D(centerX, centerY, centerZ);
        var a = Dot(direction, direction);
        var b = 2 * Dot(direction, relative);
        var c = Dot(relative, relative) - (radius * radius);
        var discriminant = Math.Max(0, (b * b) - (4 * a * c));
        var root = Math.Sqrt(discriminant);
        var t = (-b - root) / (2 * a);
        if (t < 0)
        {
            t = (-b + root) / (2 * a);
        }

        return imageIndex * t;
    }

    private static Vector3D Mean(IReadOnlyList<Vector3D> points, IReadOnlyList<bool> included)
    {
        var count = included.Count(value => value);
        var sum = points.Select((point, index) => (Point: point, Included: included[index]))
            .Where(item => item.Included)
            .Aggregate(Vector3D.Zero, (current, item) => current + item.Point);
        return sum / count;
    }

    private static double Dot(Vector3D left, Vector3D right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private static bool IsFinite(Vector3D value)
    {
        return double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
    }

    private sealed record PreparedRay(PupilSample Pupil, RayTraceSample Sample, double CorrectedOpticalPath, bool IsValid);

    private sealed record PropagatedRay(PreparedRay Ray, double ImagePath, double ReferenceOpticalPath);

    private sealed record Sphere(double CenterX, double CenterY, double CenterZ, double Radius);
}
