using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

public static class AutomaticSemiDiameterSolver
{
    private static readonly (double X, double Y)[] PupilSamples = BuildPupilSamples();

    public static void Update(Optic optic)
    {
        ArgumentNullException.ThrowIfNull(optic);
        var surfaces = optic.SurfaceGroup.Items;
        if (surfaces.Count == 0 || surfaces.All(surface => surface.SemiDiameterFixed))
        {
            return;
        }

        var maxima = new double[surfaces.Count];
        var surfaceIndexes = surfaces
            .Select((surface, index) => (surface.Number, index))
            .ToDictionary(item => item.Number, item => item.index);
        var fields = NormalizedFields(optic);
        var wavelengths = optic.Wavelengths.Count == 0
            ? new[] { 0.5875618 }
            : optic.Wavelengths.Select(wavelength => wavelength.Micrometers).ToArray();

        foreach (var field in fields)
        {
            foreach (var wavelength in wavelengths)
            {
                foreach (var pupil in PupilSamples)
                {
                    try
                    {
                        var history = optic.TraceGeneric(field.X, field.Y, pupil.X, pupil.Y, wavelength)
                            .RayHistories
                            .Single();
                        foreach (var sample in history)
                        {
                            if (!surfaceIndexes.TryGetValue(sample.SurfaceNumber, out var surfaceIndex))
                            {
                                continue;
                            }

                            var local = surfaces[surfaceIndex].CoordinateSystem.ToLocalPoint(sample.Position);
                            var radius = Math.Sqrt((local.X * local.X) + (local.Y * local.Y));
                            if (double.IsFinite(radius))
                            {
                                maxima[surfaceIndex] = Math.Max(maxima[surfaceIndex], radius);
                            }
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                    }
                }
            }
        }

        for (var index = 0; index < surfaces.Count; index++)
        {
            if (!surfaces[index].SemiDiameterFixed && maxima[index] > 1e-12)
            {
                surfaces[index].SemiDiameter = maxima[index];
            }
        }
    }

    private static IReadOnlyList<(double X, double Y)> NormalizedFields(Optic optic)
    {
        if (optic.Fields.Count == 0)
        {
            return new[] { (0.0, 0.0) };
        }

        return optic.Fields
            .Select(field => FieldCoordinates.Normalize(optic.Fields, field.X, field.Y))
            .Distinct()
            .ToArray();
    }

    private static (double X, double Y)[] BuildPupilSamples()
    {
        var samples = new List<(double X, double Y)> { (0, 0) };
        const int perimeterSamples = 16;
        for (var index = 0; index < perimeterSamples; index++)
        {
            var angle = 2 * Math.PI * index / perimeterSamples;
            samples.Add((Math.Cos(angle), Math.Sin(angle)));
        }

        return samples.ToArray();
    }
}
