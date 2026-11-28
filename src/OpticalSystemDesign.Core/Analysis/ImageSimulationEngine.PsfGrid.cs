using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class ImageSimulationEngine
{
    internal static RgbImage RenderPsfGrid(Optic optic, ImageSimulationConfig config)
    {
        RequireFiniteImageSpace(optic);
        // These dimensions describe the physical field. Only the bounded PSF tiles are allocated.
        if (config.ImageWidth is < 1 or > AnalysisResourceLimits.MaximumImageDimension
            || config.ImageHeight is < 1 or > AnalysisResourceLimits.MaximumImageDimension)
            throw new ArgumentOutOfRangeException(nameof(config), "Invalid source field dimensions.");
        if (config.WavelengthsMicrometers.Count is < 1 or > 3)
            throw new ArgumentException("The PSF grid display requires one to three wavelength channels.");
        AnalysisResourceLimits.ValidatePsfSampling(config, config.WavelengthsMicrometers.Count);

        var working = PrepareOptic(optic, config);
        var (halfX, halfY) = ResolveNormalizedFieldExtent(working, config.FieldHeight, config.ImageWidth, config.ImageHeight);
        var pitch = config.PixelSizeMillimeters > 0 ? config.PixelSizeMillimeters :
            EstimateDetectorPixelPitch(working, ResolveReferenceWavelength(working, config), halfX, halfY,
                config.ImageWidth, config.ImageHeight, config.FieldCenterX, config.FieldCenterY);
        var rows = config.PsfGridRows;
        var cols = config.PsfGridColumns;
        // Bin each normalized kernel for display; preserve integrated intensity in every cell.
        var tile = Math.Min(config.PsfSize, 512 / Math.Max(rows, cols));
        var raster = new double[config.WavelengthsMicrometers.Count, rows * tile, cols * tile];
        double peak = 0;
        for (var channel = 0; channel < raster.GetLength(0); channel++)
        {
            var wavelength = ResolveWavelength(working, config.WavelengthsMicrometers[channel]);
            var weights = config.UsePolarization ? ImageIlluminationGrid(working, wavelength, halfX, halfY,
                Math.Clamp(config.NumRays, 5, 33), config.FieldCenterX, config.FieldCenterY, true, config.UseRelativeIllumination, rows, cols) : null;
            if (weights is not null && !weights.Cast<double>().Any(power => power > 0)) continue;
            var (samples, _, _) = SamplePsfGrid(working, wavelength, config, halfX, halfY, pitch, weights);
            for (var field = 0; field < rows * cols; field++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var power = weights?[field / cols, field % cols] ?? 1;
                for (var y = 0; y < config.PsfSize; y++)
                    for (var x = 0; x < config.PsfSize; x++)
                    {
                        var iy = field / cols * tile + y * tile / config.PsfSize;
                        var ix = field % cols * tile + x * tile / config.PsfSize;
                        raster[channel, iy, ix] += power * samples[field, y * config.PsfSize + x];
                        peak = Math.Max(peak, raster[channel, iy, ix]);
                    }
            }
        }
        if (peak > 0)
            for (var c = 0; c < raster.GetLength(0); c++)
                for (var y = 0; y < raster.GetLength(1); y++)
                    for (var x = 0; x < raster.GetLength(2); x++)
                        raster[c, y, x] /= peak;
        return new RgbImage(raster);
    }
}
