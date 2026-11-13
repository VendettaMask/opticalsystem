using OpticalSystemDesign.Application.Contracts;
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
        + "No RGB-to-spectrum reconstruction, per-pixel material library, 3D source geometry/BRDF, absolute lens throughput calibration, angular QE or vendor ISP. Detector sampling is declared separately. "
        + "Not an EMVA, Zemax or ImSym/Speos certification.";

    private const string MaterialScope = "CameraLabV4-Materials: 1–16 explicitly labeled source-plane reflectance spectra; "
        + "8-bit PNG labels or self-contained JSON, optical-cell-center nearest-neighbor label sampling, linear grayscale spatial modulation. "
        + "At each merged-knot Gauss-Legendre wavelength apply material reflectance BEFORE one shared Core optical propagation, "
        + "allowing PSF/distortion to mix material boundaries; integrate charge before one noise/ADC/ISP operation. "
        + "Absolute ideal-white image-plane W/m^2/nm illuminant and absolute CFA QE; no normalization or RGB-to-spectrum reconstruction. "
        + "Integration metadata reflectance/irradiance are label-area averages, excluding grayscale modulation and optics; per-material values retained. "
        + "No BRDF, 3D geometry, measured material library or vendor ISP; detector sampling is declared separately; not an external numerical certification.";

    private static MaterialSpectralPlan MaterialPlan(CameraImagingRequest request)
    {
        if (request.MaterialScene is null || request.Illuminant is null || request.SpectralResponse is null)
            throw new ArgumentException("材质分区连续成像需要标签场景、各材质反射率、光源和绝对 QE。");
        var settings = request.Integration!;
        return CameraContinuousSpectrum.BuildMaterialPlan(ToCore(request.Illuminant), ToCore(request.MaterialScene), ToCore(request.SpectralResponse)!,
            new(settings.MinimumNanometers, settings.MaximumNanometers, settings.MaximumIntervalNanometers));
    }
    private static CameraIntegrationDiagnostics Diagnostics(CameraMaterialScene scene, MaterialSpectralPlan plan, MaterialSamplingSummary sampling, int samplesPerAxis = 1) => new(
        plan.Common.Intervals, Array.AsReadOnly(plan.Common.Nodes.Select((n, i) => new CameraIntegrationNode(n.WavelengthNanometers, n.WeightNanometers,
            n.IrradianceWattsPerSquareMeterPerNanometer, sampling.MeanReflectanceAtNodes[i], n.RedQe, n.GreenQe, n.BlueQe)).ToArray()),
        plan.Common.IlluminantIrradiance, sampling.MeanReflectedIrradiance,
        Array.AsReadOnly(scene.Materials.Select((m, i) => new CameraMaterialDiagnostics(m.Id, m.Name, sampling.PixelCountsById[m.Id], plan.MaterialReflectedIrradiance[i])).ToArray()),
        "Label-area average, excluding grayscale modulation and optics; categorical source labels applied before propagation; counts are optical sampling cells", samplesPerAxis);

    private static SpectralIntegrationPlan ContinuousPlan(CameraImagingRequest request)
    {
        if (request.Illuminant is null || request.Reflectance is null || request.SpectralResponse is null)
            throw new ArgumentException("连续光谱需要光源光谱、场景反射率和绝对 QE 曲线，请先导入三种数据。");
        var settings = request.Integration!;
        return CameraContinuousSpectrum.BuildPlan(ToCore(request.Illuminant), ToCore(request.Reflectance), ToCore(request.SpectralResponse)!,
            new(settings.MinimumNanometers, settings.MaximumNanometers, settings.MaximumIntervalNanometers));
    }
    private static CameraIntegrationDiagnostics Diagnostics(SpectralIntegrationPlan plan, int samplesPerAxis = 1) => new(plan.Intervals,
        Array.AsReadOnly(plan.Nodes.Select(n => new CameraIntegrationNode(n.WavelengthNanometers, n.WeightNanometers,
            n.IrradianceWattsPerSquareMeterPerNanometer, n.Reflectance, n.RedQe, n.GreenQe, n.BlueQe)).ToArray()),
        plan.IlluminantIrradiance, plan.ReflectedIrradiance, PixelSamplesPerAxis: samplesPerAxis);
}
