using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

public static class CameraImageOptics
{
    public static void ConfigureSensorField(Optic optic, int sensorHeight, double pixelPitchMicrometers)
    {
        optic.FieldDefinition = FieldDefinitionKind.ParaxialImageHeight;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Y = sensorHeight * pixelPitchMicrometers * 0.001 / 2 });
    }

    public static RgbImage ResizeLinear(RgbImage source, int width, int height, bool cellCenters = false)
    {
        var result = new double[3, height, width];
        for (var y = 0; y < height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var sy = cellCenters ? Math.Clamp((y + .5) * source.Height / height - .5, 0, source.Height - 1)
                : (double)y * (source.Height - 1) / (height - 1); var y0 = (int)sy; var y1 = Math.Min(y0 + 1, source.Height - 1);
            for (var x = 0; x < width; x++)
            {
                var sx = cellCenters ? Math.Clamp((x + .5) * source.Width / width - .5, 0, source.Width - 1)
                    : (double)x * (source.Width - 1) / (width - 1); var x0 = (int)sx; var x1 = Math.Min(x0 + 1, source.Width - 1);
                for (var c = 0; c < 3; c++)
                {
                    var top = Linear(y0, x0) * (1 - sx + x0) + Linear(y0, x1) * (sx - x0);
                    var bottom = Linear(y1, x0) * (1 - sx + x0) + Linear(y1, x1) * (sx - x0);
                    result[c, y, x] = top * (1 - sy + y0) + bottom * (sy - y0);
                    double Linear(int yy, int xx) => CameraSensorPipeline.SrgbToLinear(source[c, yy, xx]);
                }
            }
        }
        return new(result);
    }

    public static (RgbImage Image, string Mode, int Fallbacks) Propagate(Optic optic, RgbImage source,
        int width, int height, double pixelPitchMicrometers, string aberrationMode,
        IReadOnlyList<double> wavelengths, int samplesPerAxis)
    {
        var image = ImageSimulationEngine.Simulate(optic, source, new ImageSimulationConfig
        {
            ImageWidth = width,
            ImageHeight = height,
            OutputWidth = width + 16 * samplesPerAxis,
            OutputHeight = height + 16 * samplesPerAxis,
            PixelSizeMillimeters = pixelPitchMicrometers * .001,
            FieldHeight = height * pixelPitchMicrometers * .001,
            Padding = 8 * samplesPerAxis,
            PsfSize = 16 * samplesPerAxis,
            PsfGridColumns = 3,
            PsfGridRows = 3,
            NumRays = 12,
            Components = 3,
            DistortionGridSize = 5,
            DistortionPolynomialDegree = 3,
            WavelengthsMicrometers = wavelengths.Select(w => w / 1000).ToArray(),
            AberrationMode = aberrationMode,
            UseRelativeIllumination = true
        });
        var active = new double[wavelengths.Count, height, width];
        for (var c = 0; c < wavelengths.Count; c++) for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            active[c, y, x] = image.Simulated[c, y + 8 * samplesPerAxis, x + 8 * samplesPerAxis];
        return (new(active), image.EffectiveAberrationMode, image.GeometricFallbackCount);
    }
}
