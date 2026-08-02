using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

internal sealed record AnalysisFieldSample(
    int Index,
    string Label,
    double X,
    double Y,
    double Hx,
    double Hy,
    double Coordinate);

internal sealed record SpotRayData(double X, double Y, double Intensity);

internal sealed record SpotWavelengthData(Wavelength Wavelength, IReadOnlyList<SpotRayData> Rays);

internal sealed record SpotFieldData(
    double Hx,
    double Hy,
    IReadOnlyList<SpotWavelengthData> Wavelengths);

internal sealed record SpotAnalysisResult(
    IReadOnlyList<SpotFieldData> Fields,
    int RayCount,
    int VignettedRayCount);

internal static class MtfPresentation
{
    public static string FieldName(
        Optic optic,
        (double Hx, double Hy) normalizedField)
    {
        var definedFields = SpotAnalysisEngine.DefinedFields(optic);
        var matchedIndex = definedFields
            .Select((field, index) => (
                Index: index,
                Distance: Math.Pow(field.Hx - normalizedField.Hx, 2)
                    + Math.Pow(field.Hy - normalizedField.Hy, 2)))
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Index)
            .FirstOrDefault();
        var declaredField = matchedIndex.Distance <= 1e-18
            && matchedIndex.Index >= 0
            && matchedIndex.Index < optic.Fields.Count
                ? optic.Fields[matchedIndex.Index]
                : null;
        var actual = FieldCoordinates.Denormalize(
            optic.Fields,
            normalizedField.Hx,
            normalizedField.Hy);
        var fieldName = !string.IsNullOrWhiteSpace(declaredField?.Label)
            ? declaredField.Label
            : $"Field {Math.Max(1, matchedIndex.Index + 1)}";
        var unit = optic.FieldDefinition == FieldDefinitionKind.Angle ? "°" : "mm";
        var coordinates = Math.Abs(actual.X) <= 1e-12
            ? $"Y={actual.Y:0.###} {unit}"
            : Math.Abs(actual.Y) <= 1e-12
                ? $"X={actual.X:0.###} {unit}"
                : $"X={actual.X:0.###}, Y={actual.Y:0.###} {unit}";
        return $"{fieldName} ({coordinates})";
    }

    public static string SeriesName(
        Optic optic,
        (double Hx, double Hy) normalizedField,
        string direction)
    {
        return $"{FieldName(optic, normalizedField)}, {direction}";
    }
}

internal static class SpotAnalysisEngine
{
    public static IReadOnlyList<(double Hx, double Hy)> DefinedFields(Optic optic)
    {
        var maxField = FieldCoordinates.MaximumRadius(optic.Fields);
        return optic.Fields.Select(field => (
            Hx: maxField <= 1e-12 ? 0 : field.X / maxField,
            Hy: maxField <= 1e-12 ? 0 : field.Y / maxField)).ToArray();
    }

    public static SpotAnalysisResult Generate(
        Optic optic,
        IEnumerable<(double Hx, double Hy)> fields,
        IEnumerable<Wavelength> wavelengths,
        int sampleParameter,
        string distribution,
        double imagePlaneOffset = 0,
        int surfaceNumber = -1,
        bool directionCosines = false,
        string reference = "centroid",
        bool usePolarization = false,
        bool ignoreLateralColor = false,
        bool aimAtStop = false)
    {
        var fieldArray = fields.ToArray();
        var wavelengthArray = wavelengths.ToArray();
        var pupilSamples = CreatePupilSamples(sampleParameter, distribution);
        var rawFields = new List<SpotFieldData>(fieldArray.Length);
        var rayCount = 0;
        var vignettedRayCount = 0;

        foreach (var field in fieldArray)
        {
            var waveData = new List<SpotWavelengthData>(wavelengthArray.Length);
            foreach (var wavelength in wavelengthArray)
            {
                var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                    field.Hx,
                    field.Hy,
                    wavelength.Micrometers,
                    pupilSamples,
                    aimAtStop: aimAtStop);
                if (usePolarization)
                {
                    bundle = WithPolarization(bundle);
                }

                var selectedSamples = SelectSamples(
                    optic,
                    bundle,
                    surfaceNumber,
                    directionCosines);
                var valid = selectedSamples
                    .Where(sample => sample.Intensity > 0)
                    .Select(sample =>
                    {
                        if (directionCosines)
                        {
                            return new SpotRayData(
                                sample.Direction.X,
                                sample.Direction.Y,
                                sample.Intensity);
                        }

                        var position = sample.Position;
                        if (Math.Abs(imagePlaneOffset) > 1e-12
                            && Math.Abs(sample.Direction.Z) > 1e-12)
                        {
                            position += sample.Direction * (imagePlaneOffset / sample.Direction.Z);
                        }

                        return new SpotRayData(position.X, position.Y, sample.Intensity);
                    })
                    .ToArray();
                rayCount += selectedSamples.Length;
                vignettedRayCount += selectedSamples.Length - valid.Length;
                waveData.Add(new SpotWavelengthData(wavelength, valid));
            }

            rawFields.Add(new SpotFieldData(field.Hx, field.Hy, waveData));
        }

        var referenceIndex = Array.FindIndex(wavelengthArray, wavelength => wavelength.IsPrimary);
        referenceIndex = referenceIndex < 0 ? 0 : referenceIndex;
        var centeredFields = rawFields.Select(field =>
        {
            if (string.Equals(reference, "absolute", StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }

            var referenceRays = field.Wavelengths.Count == 0
                ? Array.Empty<SpotRayData>()
                : field.Wavelengths[Math.Min(referenceIndex, field.Wavelengths.Count - 1)].Rays;
            var useChiefRay = string.Equals(reference, "chief", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reference, "主光线", StringComparison.Ordinal);
            var referencePoint = useChiefRay
                    ? ChiefRayReference(
                        optic,
                        (field.Hx, field.Hy),
                        wavelengthArray.ElementAtOrDefault(referenceIndex),
                        surfaceNumber,
                        directionCosines,
                        usePolarization,
                        imagePlaneOffset)
                    : null;
            var centroidX = referencePoint?.X
                ?? referenceRays.Select(ray => ray.X).DefaultIfEmpty(0).Average();
            var centroidY = referencePoint?.Y
                ?? referenceRays.Select(ray => ray.Y).DefaultIfEmpty(0).Average();
            var centeredWavelengths = field.Wavelengths.Select(wavelength =>
            {
                var wavelengthReference = ignoreLateralColor && useChiefRay
                    ? ChiefRayReference(
                        optic,
                        (field.Hx, field.Hy),
                        wavelength.Wavelength,
                        surfaceNumber,
                        directionCosines,
                        usePolarization,
                        imagePlaneOffset)
                    : null;
                var wavelengthCenterX = ignoreLateralColor
                    ? wavelengthReference?.X
                        ?? wavelength.Rays.Select(ray => ray.X).DefaultIfEmpty(0).Average()
                    : centroidX;
                var wavelengthCenterY = ignoreLateralColor
                    ? wavelengthReference?.Y
                        ?? wavelength.Rays.Select(ray => ray.Y).DefaultIfEmpty(0).Average()
                    : centroidY;
                return new SpotWavelengthData(
                    wavelength.Wavelength,
                    wavelength.Rays.Select(ray => new SpotRayData(
                        ray.X - wavelengthCenterX,
                        ray.Y - wavelengthCenterY,
                        ray.Intensity)).ToArray());
            }).ToArray();
            return new SpotFieldData(field.Hx, field.Hy, centeredWavelengths);
        }).ToArray();
        return new SpotAnalysisResult(centeredFields, rayCount, vignettedRayCount);
    }

    private static RayTraceSample[] SelectSamples(
        Optic optic,
        RealRayBundle bundle,
        int surfaceNumber,
        bool directionCosines)
    {
        var surfaceIndex = surfaceNumber < 0
            ? optic.SurfaceGroup.Items.Count - 1
            : optic.SurfaceGroup.Items
                .Select((surface, index) => (surface, index))
                .Where(item => item.surface.Number == surfaceNumber)
                .Select(item => item.index)
                .DefaultIfEmpty(-1)
                .First();
        if (surfaceIndex < 0)
        {
            return Array.Empty<RayTraceSample>();
        }

        using var trace = optic.SequentialRayTracer.Trace(
            bundle,
            TraceRequest.Selected(new[] { surfaceIndex }));
        return trace.GetSurfaceSamples(surfaceIndex)
            .Where(sample => sample.HasValue)
            .Select(sample => sample!.Value.ToRayTraceSample())
            .ToArray();
    }

    private static SpotRayData? ChiefRayReference(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength? wavelength,
        int surfaceNumber,
        bool directionCosines,
        bool usePolarization,
        double imagePlaneOffset)
    {
        if (wavelength is null)
        {
            return null;
        }

        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
            field.Hx,
            field.Hy,
            0,
            0,
            wavelength.Micrometers);
        if (usePolarization)
        {
            bundle = WithPolarization(bundle);
        }

        var sample = SelectSamples(optic, bundle, surfaceNumber, directionCosines)
            .FirstOrDefault();
        if (sample is null || sample.Intensity <= 0)
        {
            return null;
        }

        if (directionCosines)
        {
            return new SpotRayData(sample.Direction.X, sample.Direction.Y, sample.Intensity);
        }

        var position = sample.Position;
        if (Math.Abs(imagePlaneOffset) > 1e-12 && Math.Abs(sample.Direction.Z) > 1e-12)
        {
            position += sample.Direction * (imagePlaneOffset / sample.Direction.Z);
        }

        return new SpotRayData(position.X, position.Y, sample.Intensity);
    }

    private static RealRayBundle WithPolarization(RealRayBundle bundle)
    {
        return new RealRayBundle(bundle.Rays.Select(ray => ray with
        {
            PolarizationMatrix = Matrix3x3.Identity
        }));
    }

    public static double RmsRadius(IReadOnlyList<SpotRayData> rays)
    {
        if (rays.Count == 0)
        {
            throw new AnalysisDataUnavailableException(
                "RMS spot",
                "no valid rays reached the selected surface");
        }

        var totalWeight = rays.Sum(ray => Math.Max(0, ray.Intensity));
        if (!(totalWeight > 0) || !double.IsFinite(totalWeight))
        {
            throw new AnalysisDataUnavailableException(
                "RMS spot",
                "valid rays have no finite positive weight");
        }

        return Math.Sqrt(rays.Sum(ray =>
        {
            var weight = Math.Max(0, ray.Intensity);
            return weight * ((ray.X * ray.X) + (ray.Y * ray.Y));
        }) / totalWeight);
    }

    public static IReadOnlyList<PupilSample> CreatePupilSamples(int sampleParameter, string distribution)
    {
        if (string.Equals(distribution, "hexapolar", StringComparison.OrdinalIgnoreCase))
        {
            return ApertureSampler.GenerateHexapolarRings(sampleParameter);
        }

        if (string.Equals(distribution, "uniform", StringComparison.OrdinalIgnoreCase))
        {
            var axis = Enumerable.Range(0, sampleParameter)
                .Select(index => sampleParameter == 1 ? 0 : -1 + (2.0 * index / (sampleParameter - 1)))
                .ToArray();
            return axis.SelectMany(y => axis.Select(x => new PupilSample(x, y, 1)))
                .Where(sample => (sample.X * sample.X) + (sample.Y * sample.Y) <= 1)
                .ToArray();
        }

        return ApertureSampler.Generate(sampleParameter, RayGenerator.ParseSampling(distribution));
    }
}
