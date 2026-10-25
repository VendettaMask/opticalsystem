using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    private const string ContinuousScope = "CameraLabV3-Continuous: absolute ideal-white image-plane illuminant spectral irradiance (W/m^2/nm) "
        + "times one material reflectance and linear grayscale spatial mask (.2126R+.7152G+.0722B). "
        + "Piecewise-linear spectra; merged input knots; three-point Gauss-Legendre on each bounded interval. "
        + "Each quadrature wavelength uses shared Core PSF/distortion/relative illumination, or explicit ideal optics. "
        + "Absolute CFA QE and wavelength-dependent photon energy; no spectral peak normalization. "
        + "Legacy per-band irradiance and scalar QE are inactive. Optical preview is integrated reflected energy divided by input illuminant energy. "
        + "No RGB-to-spectrum reconstruction, per-pixel material library, 3D source geometry/BRDF, absolute lens throughput calibration, angular QE, pixel-area integration or vendor ISP. "
        + "Not an EMVA, Zemax or ImSym/Speos certification.";

    private static SpectralIntegrationPlan ContinuousPlan(CameraImagingRequest request)
    {
        if (request.Illuminant is null || request.Reflectance is null || request.SpectralResponse is null)
            throw new ArgumentException("连续光谱需要光源光谱、场景反射率和绝对 QE 曲线，请先导入三种数据。");
        var settings = request.Integration!;
        return CameraContinuousSpectrum.BuildPlan(ToCore(request.Illuminant), ToCore(request.Reflectance), ToCore(request.SpectralResponse)!,
            new(settings.MinimumNanometers, settings.MaximumNanometers, settings.MaximumIntervalNanometers));
    }
    private static CameraIntegrationDiagnostics Diagnostics(SpectralIntegrationPlan plan) => new(plan.Intervals,
        Array.AsReadOnly(plan.Nodes.Select(n => new CameraIntegrationNode(n.WavelengthNanometers, n.WeightNanometers,
            n.IrradianceWattsPerSquareMeterPerNanometer, n.Reflectance, n.RedQe, n.GreenQe, n.BlueQe)).ToArray()),
        plan.IlluminantIrradiance, plan.ReflectedIrradiance);

    private static (RgbImage Image, string Mode, int Fallbacks) Propagate(Optic optic, RgbImage source,
        CameraImagingRequest request, CameraSensorSettings sensor, IReadOnlyList<double> wavelengths)
    {
        var image = ImageSimulationEngine.Simulate(optic, source, new ImageSimulationConfig
        {
            ImageWidth = request.Width, ImageHeight = request.Height,
            OutputWidth = request.Width + 16, OutputHeight = request.Height + 16,
            PixelSizeMillimeters = sensor.PixelPitchMicrometers * .001,
            FieldHeight = request.Height * sensor.PixelPitchMicrometers * .001,
            Padding = 8, PsfSize = 16, PsfGridColumns = 3, PsfGridRows = 3,
            NumRays = 12, Components = 3, DistortionGridSize = 5, DistortionPolynomialDegree = 3,
            WavelengthsMicrometers = wavelengths.Select(w => w / 1000).ToArray(),
            AberrationMode = request.Aberration.ToString(), UseRelativeIllumination = true
        });
        var active = new double[wavelengths.Count, request.Height, request.Width];
        for (var c = 0; c < wavelengths.Count; c++) for (var y = 0; y < request.Height; y++) for (var x = 0; x < request.Width; x++)
            active[c, y, x] = image.Simulated[c, y + 8, x + 8];
        return (new(active), image.EffectiveAberrationMode, image.GeometricFallbackCount);
    }
}
