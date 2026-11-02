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
    public static MaterialSnapshot[] AnalysisMaterials(Experiment experiment)
    {
        var used = experiment.Layers.Select(l => l.MaterialId).Append(experiment.IncidentId).Append(experiment.SubstrateId).ToHashSet();
        if (experiment.StackGeometry is { IncludeBackSurface: true } stack) used.Add(stack.ExitMediumId);
        if (experiment.Duplex is not null) used = DesignService.EvaluationMaterialIds(experiment).ToHashSet();
        return experiment.Materials.Where(m => used.Contains(m.Id)).ToArray();
    }

    public static SpectralBand[] ExtrapolatedBands(Experiment experiment, double minimumNm, double maximumNm)
    {
        var materials = AnalysisMaterials(experiment).Where(m => m.HoldTabulatedEndpoints).ToArray();
        if (materials.Length == 0) return [];
        // A wavelength is in-domain only when every participating table covers it.
        // The complement of their common interval is already a union: never stack shade opacity.
        var commonMinimum = materials.Max(m => m.MinimumNm);
        var commonMaximum = materials.Min(m => m.MaximumNm);
        if (commonMinimum >= commonMaximum || maximumNm <= commonMinimum || minimumNm >= commonMaximum)
            return [new(minimumNm, maximumNm)];
        var bands = new List<SpectralBand>();
        if (minimumNm < commonMinimum) bands.Add(new(minimumNm, Math.Min(maximumNm, commonMinimum)));
        if (maximumNm > commonMaximum) bands.Add(new(Math.Max(minimumNm, commonMaximum), maximumNm));
        return bands.ToArray();
    }

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
