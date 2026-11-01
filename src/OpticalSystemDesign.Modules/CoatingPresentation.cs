using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;

namespace OpticalSystemDesign.Modules;

public sealed record CoatingPower(double Reflectance, double Transmittance, double Absorptance, double OpticalDensity);
public sealed record CoatingSample(double WavelengthNanometers, CoatingPower Power);
public sealed record CoatingPlotData(CoatingSample[] Spectrum, AnalysisSeriesDto[] Series);
public sealed record CoatingAnglePlotData(double AngleDegrees, CoatingSample[] Average, CoatingSample[] S, CoatingSample[] P);
public sealed record CoatingPolarizedPlotData(CoatingSample[] Average, CoatingSample[] S, CoatingSample[] P)
{
    public CoatingAnglePlotData[] Angles { get; init; } = [];
}

/// <summary>Presentation conversion only: spectrum and typed axes come from the shared Core.</summary>
public static class CoatingPresentation
{
    public static CoatingPolarizedPlotData AnalysisPlot(AngleDisplaySpectrum[] spectra)
    {
        var angles = spectra.Select(a => { var p = PolarizedPlot(a.Spectrum); return new CoatingAnglePlotData(a.AngleDegrees, p.Average, p.S, p.P); }).ToArray();
        return new(angles[0].Average, angles[0].S, angles[0].P) { Angles = angles };
    }
    public static CoatingPolarizedPlotData PolarizedPlot(PolarizedDisplaySpectrum spectrum)
    {
        static CoatingSample[] Map(ThinFilmSample[] samples) => samples.Select(sample => new CoatingSample(
            sample.WavelengthNanometers, new(sample.Power.Reflectance, sample.Power.Transmittance,
                sample.Power.Absorptance, sample.Power.OpticalDensity))).ToArray();
        return new(Map(spectrum.Average), Map(spectrum.S), Map(spectrum.P));
    }
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
