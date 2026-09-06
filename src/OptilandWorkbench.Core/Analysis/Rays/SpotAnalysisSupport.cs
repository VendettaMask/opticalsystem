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
    IReadOnlyList<SpotWavelengthData> Wavelengths)
{
    // Rays retain monochromatic throughput; apply the spectral weight exactly
    // once when pooling wavelengths for a physical statistic.
    public IEnumerable<SpotRayData> WeightedRays => Wavelengths.SelectMany(wave =>
        wave.Rays.Select(ray => ray with { Intensity = ray.Intensity * Math.Max(0, wave.Wavelength.Weight) }))
        .Where(ray => ray.Intensity > 0 && double.IsFinite(ray.Intensity));
}

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
    private const int MaximumUniformAxisSamples = 1_024;

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
        bool aimAtStop = false,
        bool includeSurfaceTransmission = true,
        int gaussianAzimuthalSamples = 6)
    {
        var fieldArray = fields.ToArray();
        var wavelengthArray = wavelengths.ToArray();
        var pupilSamples = CreatePupilSamples(sampleParameter, distribution, gaussianAzimuthalSamples);
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

                var surfaceIndex = ImageSpaceAnalysisSupport.ResolveSurfaceIndex(optic, surfaceNumber);
                var targetSurface = surfaceIndex < 0
                    ? null
                    : optic.SurfaceGroup.Items[surfaceIndex];
                var descriptor = ImageSpaceAnalysisSupport.CoordinateDescriptor(
                    optic,
                    surfaceNumber,
                    directionCosines);
                var selectedSamples = SelectSamples(optic, bundle, surfaceNumber);
                var valid = selectedSamples
                    .Select((sample, index) => (Sample: sample, IncidentIntensity: bundle.Rays[index].Intensity))
                    .Where(item => item.Sample is { Vignetted: false, Intensity: > 0 } && targetSurface is not null)
                    .Select(item =>
                    {
                        var ray = ImageSpaceAnalysisSupport.ToImageSpaceRayData(
                            optic,
                            item.Sample!,
                            targetSurface!,
                            descriptor,
                            imagePlaneOffset);
                        // Non-polarized standard spot diagrams retain pupil/apodization weights,
                        // but do not weight the geometric statistic by surface/bulk transmission.
                        return includeSurfaceTransmission ? ray : ray with { Intensity = item.IncidentIntensity };
                    })
                    .ToArray();
                rayCount += bundle.Rays.Count;
                vignettedRayCount += bundle.Rays.Count - valid.Length;
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
                        imagePlaneOffset,
                        aimAtStop)
                    : null;
            var centroid = Centroid(field.WeightedRays);
            var centroidX = referencePoint?.X ?? centroid.X;
            var centroidY = referencePoint?.Y ?? centroid.Y;
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
                        imagePlaneOffset,
                        aimAtStop)
                    : null;
                var wavelengthCentroid = Centroid(wavelength.Rays);
                var wavelengthCenterX = ignoreLateralColor
                    ? wavelengthReference?.X
                        ?? wavelengthCentroid.X
                    : centroidX;
                var wavelengthCenterY = ignoreLateralColor
                    ? wavelengthReference?.Y
                        ?? wavelengthCentroid.Y
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

    private static RayTraceSample?[] SelectSamples(
        Optic optic,
        RealRayBundle bundle,
        int surfaceNumber)
    {
        var surfaceIndex = ImageSpaceAnalysisSupport.ResolveSurfaceIndex(optic, surfaceNumber);
        if (surfaceIndex < 0)
        {
            return Array.Empty<RayTraceSample>();
        }

        using var trace = optic.SequentialRayTracer.Trace(
            bundle,
            TraceRequest.Selected(new[] { surfaceIndex }));
        return trace.GetSurfaceSamples(surfaceIndex)
            .Select(sample => sample.HasValue ? sample.Value.ToRayTraceSample() : null)
            .ToArray();
    }

    private static SpotRayData? ChiefRayReference(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength? wavelength,
        int surfaceNumber,
        bool directionCosines,
        bool usePolarization,
        double imagePlaneOffset,
        bool aimAtStop)
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
            wavelength.Micrometers,
            aimAtStop);
        if (usePolarization)
        {
            bundle = WithPolarization(bundle);
        }

        var targetSurface = ImageSpaceAnalysisSupport.ResolveSurface(optic, surfaceNumber);
        if (targetSurface is null)
        {
            return null;
        }

        var sample = SelectSamples(optic, bundle, surfaceNumber)
            .FirstOrDefault();
        if (sample is null || sample.Intensity <= 0)
        {
            return null;
        }

        var descriptor = ImageSpaceAnalysisSupport.CoordinateDescriptor(
            optic,
            surfaceNumber,
            directionCosines);
        return ImageSpaceAnalysisSupport.ToImageSpaceRayData(
            optic,
            sample,
            targetSurface,
            descriptor,
            imagePlaneOffset);
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

    internal static (double X, double Y) Centroid(IEnumerable<SpotRayData> rays)
    {
        var total = 0.0;
        var x = 0.0;
        var y = 0.0;
        foreach (var ray in rays)
        {
            if (!(ray.Intensity > 0) || !double.IsFinite(ray.Intensity)) continue;
            total += ray.Intensity;
            x += ray.X * ray.Intensity;
            y += ray.Y * ray.Intensity;
        }
        return total > 0 ? (x / total, y / total) : (0, 0);
    }

    public static IReadOnlyList<PupilSample> CreatePupilSamples(int sampleParameter, string distribution, int gaussianAzimuthalSamples = 6)
    {
        if (string.Equals(distribution, "uniform-intervals", StringComparison.OrdinalIgnoreCase))
        {
            if (sampleParameter is < 1 or >= MaximumUniformAxisSamples)
                throw new ArgumentOutOfRangeException(nameof(sampleParameter));
            var axis = Enumerable.Range(0, sampleParameter + 1).Select(i => -1 + 2d * i / sampleParameter).ToArray();
            return axis.SelectMany(y => axis.Select(x => new PupilSample(x, y, 1)))
                .Where(p => p.X * p.X + p.Y * p.Y <= 1 + 1e-12).ToArray();
        }
        if (string.Equals(distribution, "gaussian", StringComparison.OrdinalIgnoreCase))
            // Angular order is independent of radial order and explicitly chosen.
            return ApertureSampler.GenerateGaussianQuadrature(sampleParameter, gaussianAzimuthalSamples);
        if (string.Equals(distribution, "hexapolar", StringComparison.OrdinalIgnoreCase))
        {
            return ApertureSampler.GenerateHexapolarRings(sampleParameter);
        }

        if (string.Equals(distribution, "uniform", StringComparison.OrdinalIgnoreCase))
        {
            if (sampleParameter is < 1 or > MaximumUniformAxisSamples)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleParameter),
                    $"Uniform pupil axis sampling must be between 1 and {MaximumUniformAxisSamples}.");
            }

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
