using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;

namespace OpticalSystemDesign.Modules;

public sealed record CoatingPower(double Reflectance, double Transmittance, double Absorptance, double OpticalDensity);
public sealed record CoatingSample(double WavelengthNanometers, CoatingPower Power);
public sealed record CoatingPlotData(CoatingSample[] Spectrum, AnalysisSeriesDto[] Series);

/// <summary>Presentation conversion only: spectrum and typed axes come from the shared Core.</summary>
public static class CoatingPresentation
{
    public static CoatingPlotData? Plot(Experiment experiment) => experiment.Result is not { } result ? null : new(
        result.Spectrum.Select(sample => new CoatingSample(sample.WavelengthNanometers,
            new(sample.Power.Reflectance, sample.Power.Transmittance, sample.Power.Absorptance, sample.Power.OpticalDensity))).ToArray(),
        ThinFilmSpectrum.ToSeries(result.Spectrum).Select(WorkbenchMapper.ToSeriesDto).ToArray());

    public static int PolarizationIndex(DesignTarget target) => (int)target.Polarization;

    public static DesignTarget Target(DesignKind kind, double centerNm, double minimumNm, double maximumNm,
        double reflectance, int maximumLayers, double angleDegrees, int polarization, double fwhmNm,
        double peakTransmittance, double blockingOd, SpectralBand[]? stopBands, double centerToleranceNm,
        double fwhmToleranceNm, StructureOptions? structure) => new(kind, centerNm, minimumNm, maximumNm,
            reflectance, maximumLayers, angleDegrees, (ThinFilmPolarization)polarization, fwhmNm,
            peakTransmittance, blockingOd, stopBands, centerToleranceNm, fwhmToleranceNm, structure);
}
