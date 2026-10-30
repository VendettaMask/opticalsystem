using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

public sealed record SpectralMaterial(int Id, ScalarSpectrum Reflectance);

/// <summary>Categorical source-plane labels. Nearest-neighbor sampling never blends material identities.</summary>
public sealed class SpectralMaterialScene
{
    public const int MaximumMaterials = 16;
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<int> MaterialIds { get; }
    public IReadOnlyList<SpectralMaterial> Materials { get; }

    public SpectralMaterialScene(int width, int height, IReadOnlyList<int> ids, IReadOnlyList<SpectralMaterial> materials)
    {
        ArgumentNullException.ThrowIfNull(ids); ArgumentNullException.ThrowIfNull(materials);
        if (width is < 1 or > 1024 || height is < 1 or > 1024 || (long)width * height > CameraSensorPipeline.MaximumPixels
            || ids.Count != width * height || materials.Count is < 1 or > MaximumMaterials)
            throw new ArgumentException("Invalid material map dimensions, labels or material count (1–16).");
        var frozen = ids.ToArray(); var definitions = materials.ToArray();
        if (definitions.Any(m => m is null || m.Id is < 0 or >= MaximumMaterials
                || m.Reflectance is null || m.Reflectance.Kind != ScalarSpectrumKind.Reflectance)
            || definitions.Select(m => m.Id).Distinct().Count() != definitions.Length
            || !frozen.ToHashSet().SetEquals(definitions.Select(m => m.Id)))
            throw new ArgumentException("Labels must match unique material IDs 0–15, each with a reflectance curve.");
        Width = width; Height = height; MaterialIds = Array.AsReadOnly(frozen); Materials = Array.AsReadOnly(definitions);
    }

    public int SampleId(int x, int y, int targetWidth, int targetHeight)
    {
        if (targetWidth < 1 || targetHeight < 1 || x < 0 || x >= targetWidth || y < 0 || y >= targetHeight)
            throw new ArgumentOutOfRangeException(nameof(x));
        var sx = (int)((2L * x + 1) * Width / (2L * targetWidth));
        var sy = (int)((2L * y + 1) * Height / (2L * targetHeight));
        return MaterialIds[sy * Width + sx];
    }
}

public sealed class MaterialSpectralPlan
{
    public SpectralMaterialScene Scene { get; }
    public SpectralIntegrationPlan Common { get; }
    public IReadOnlyList<double> MaterialReflectedIrradiance { get; }
    internal MaterialSpectralPlan(SpectralMaterialScene scene, SpectralIntegrationPlan common)
    {
        Scene = scene; Common = common;
        MaterialReflectedIrradiance = Array.AsReadOnly(scene.Materials.Select(m => common.Nodes.Sum(n =>
            n.IrradianceWattsPerSquareMeterPerNanometer * n.WeightNanometers * m.Reflectance.Evaluate(n.WavelengthNanometers))).ToArray());
    }
    public MaterialSamplingSummary DescribeSampling(int width, int height)
    {
        CameraSensorPipeline.ValidateDimensions(width, height);
        var counts = new int[SpectralMaterialScene.MaximumMaterials];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) counts[Scene.SampleId(x, y, width, height)]++;
        var fractions = Scene.Materials.Select(m => (double)counts[m.Id] / (width * height)).ToArray();
        var mean = Common.Nodes.Select(n => Scene.Materials.Select((m, i) => fractions[i] * m.Reflectance.Evaluate(n.WavelengthNanometers)).Sum()).ToArray();
        return new(Array.AsReadOnly(counts), Array.AsReadOnly(mean),
            fractions.Select((fraction, i) => fraction * MaterialReflectedIrradiance[i]).Sum());
    }
}
public sealed record MaterialSamplingSummary(IReadOnlyList<int> PixelCountsById, IReadOnlyList<double> MeanReflectanceAtNodes,
    double MeanReflectedIrradiance);

public static partial class CameraContinuousSpectrum
{
    public static MaterialSpectralPlan BuildMaterialPlan(ScalarSpectrum illuminant, SpectralMaterialScene scene,
        SensorSpectralResponse qe, ContinuousSpectrumSettings settings)
    {
        ArgumentNullException.ThrowIfNull(scene); ArgumentNullException.ThrowIfNull(settings);
        foreach (var material in scene.Materials)
        { _ = material.Reflectance.Evaluate(settings.MinimumNanometers); _ = material.Reflectance.Evaluate(settings.MaximumNanometers); }
        var unit = new ScalarSpectrum(ScalarSpectrumKind.Reflectance, new[] { new ScalarSpectrumSample(200, 1), new(20000, 1) });
        var common = BuildPlan(illuminant, unit, qe, settings, scene.Materials.SelectMany(m => m.Reflectance.Samples.Select(s => s.WavelengthNanometers)));
        return new(scene, common);
    }

    public static ContinuousImageResult IntegrateMaterials(RgbImage linearSource, CameraSensorSettings sensor,
        MaterialSpectralPlan plan, Func<RgbImage, double, RgbImage>? propagate = null, CameraPixelSampling? pixelSampling = null)
    {
        ArgumentNullException.ThrowIfNull(linearSource); ArgumentNullException.ThrowIfNull(plan);
        CameraSensorPipeline.Validate(sensor, new());
        var (outputWidth, outputHeight) = CameraPixelAperture.OutputDimensions(linearSource.Width, linearSource.Height, pixelSampling);
        var width = linearSource.Width; var height = linearSource.Height;
        if (linearSource.Channels != 3 || propagate is not null && (long)width * height * plan.Common.Nodes.Count > MaximumPixelNodes)
            throw new ArgumentException("Invalid source or material image exceeds pixel × node budget.");
        var mask = new double[height, width]; var displayMask = new double[3, height, width];
        var preview = new double[3, outputHeight, outputWidth]; var photo = new double[outputWidth * outputHeight]; var labels = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < 3; c++) if (!double.IsFinite(linearSource[c, y, x]) || linearSource[c, y, x] is < 0 or > 1)
                    throw new ArgumentException("Spatial source must be finite linear RGB in [0,1].");
                mask[y, x] = .2126 * linearSource[0, y, x] + .7152 * linearSource[1, y, x] + .0722 * linearSource[2, y, x];
                labels[y * width + x] = plan.Scene.SampleId(x, y, width, height);
                for (var c = 0; c < 3; c++) displayMask[c, y, x] = mask[y, x];
            }
        }
        var coefficients = new double[SpectralMaterialScene.MaximumMaterials, 3];
        var areaTime = Math.Pow(sensor.PixelPitchMicrometers * 1e-6, 2) * sensor.ExposureMilliseconds / 1000
            * CameraPixelAperture.ChargeScale(pixelSampling);
        var materialById = plan.Scene.Materials.ToDictionary(m => m.Id);
        var transferById = new double[SpectralMaterialScene.MaximumMaterials];
        for (var index = 0; index < plan.Scene.Materials.Count; index++)
        {
            var material = plan.Scene.Materials[index];
            transferById[material.Id] = plan.Common.IlluminantIrradiance > 0
                ? plan.MaterialReflectedIrradiance[index] / plan.Common.IlluminantIrradiance : 0;
        }
        foreach (var node in plan.Common.Nodes)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var radiance = propagate is null ? null : new double[1, height, width];
            var reflectances = new double[SpectralMaterialScene.MaximumMaterials];
            foreach (var material in materialById.Values) reflectances[material.Id] = material.Reflectance.Evaluate(node.WavelengthNanometers);
            var energyWeight = node.IrradianceWattsPerSquareMeterPerNanometer * node.WeightNanometers;
            var photonWeight = energyWeight * node.WavelengthNanometers * 1e-9 / Hc * areaTime;
            var efficiencies = new[] { node.RedQe, node.GreenQe, node.BlueQe };
            if (propagate is null)
            {
                foreach (var material in plan.Scene.Materials) for (var c = 0; c < 3; c++)
                    coefficients[material.Id, c] += photonWeight * reflectances[material.Id] * efficiencies[c];
                continue;
            }
            for (var y = 0; y < height; y++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++) radiance![0, y, x] = mask[y, x] * reflectances[labels[y * width + x]];
            }
            // Reflectance is applied in the source plane, before one shared monochromatic optical propagation.
            var optical = propagate(new(radiance!), node.WavelengthNanometers);
            if (optical.Channels != 1 || optical.Width != width || optical.Height != height)
                throw new ArgumentException("Monochromatic optical result must match material source dimensions.");
            optical = CameraPixelAperture.Average(optical, pixelSampling);
            for (var y = 0; y < outputHeight; y++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                for (var x = 0; x < outputWidth; x++)
                {
                    var value = optical[0, y, x];
                    if (!double.IsFinite(value) || value < 0) throw new ArgumentException("Optical transfer must be finite and nonnegative.");
                    var i = y * outputWidth + x; photo[i] += value * photonWeight * efficiencies[CameraSensorPipeline.ColorAt(sensor.Pattern, y, x)];
                    CheckCharge(photo[i]);
                    var energy = plan.Common.IlluminantIrradiance > 0 ? value * energyWeight / plan.Common.IlluminantIrradiance : 0;
                    for (var c = 0; c < 3; c++) preview[c, y, x] += energy;
                }
            }
        }
        if (propagate is null)
        {
            var chargeByCfa = new double[3, height, width]; var finePreview = new double[3, height, width];
            for (var y = 0; y < height; y++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x; var id = labels[i];
                    for (var c = 0; c < 3; c++)
                    {
                        chargeByCfa[c, y, x] = mask[y, x] * coefficients[id, c];
                        finePreview[c, y, x] = mask[y, x] * transferById[id];
                    }
                }
            }
            var charge = CameraPixelAperture.Average(new(chargeByCfa), pixelSampling);
            preview = CameraPixelAperture.Average(new(finePreview), pixelSampling).Values;
            for (var y = 0; y < outputHeight; y++) for (var x = 0; x < outputWidth; x++)
            {
                var i = y * outputWidth + x; photo[i] = charge[CameraSensorPipeline.ColorAt(sensor.Pattern, y, x), y, x]; CheckCharge(photo[i]);
            }
        }
        return new(CameraPixelAperture.Average(new(displayMask), pixelSampling is null ? null : pixelSampling with { FillFactor = 1 }), new(preview), photo);
        static void CheckCharge(double value)
        { if (!double.IsFinite(value) || value > 1e10) throw new ArgumentOutOfRangeException(nameof(plan), "Integrated photocharge exceeds 1e10 electrons."); }
    }
}
