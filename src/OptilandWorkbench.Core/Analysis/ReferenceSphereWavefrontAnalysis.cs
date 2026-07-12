namespace OptilandWorkbench.Core.Analysis;

public sealed class ReferenceSphereWavefrontAnalysis : BaseAnalysis
{
    private readonly ReferenceSphereStrategy _strategy;
    private readonly int _numRings;
    private readonly int _mapSize;
    private readonly double _robustTrimStandardDeviations;

    public ReferenceSphereWavefrontAnalysis(
        Optic optic,
        ReferenceSphereStrategy strategy,
        int numRings = 15,
        int mapSize = 65,
        double robustTrimStandardDeviations = 3) : base(optic)
    {
        _strategy = strategy;
        _numRings = Math.Max(2, numRings);
        _mapSize = Math.Max(17, mapSize);
        _robustTrimStandardDeviations = robustTrimStandardDeviations;
    }

    public override string Name => _strategy == ReferenceSphereStrategy.CentroidSphere
        ? "Centroid Sphere Wavefront"
        : "Best Fit Sphere Wavefront";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var field = SpotAnalysisEngine.DefinedFields(Optic).LastOrDefault();
        var wavefront = ReferenceSphereWavefrontEngine.Generate(
            Optic,
            field,
            wavelength,
            _numRings,
            _strategy,
            _robustTrimStandardDeviations);
        var valid = wavefront.Samples.Where(sample => sample.Intensity > 0).ToArray();
        var mean = valid.Select(sample => sample.OpdWaves).DefaultIfEmpty(0).Average();
        var minimum = valid.Select(sample => sample.OpdWaves).DefaultIfEmpty(0).Min();
        var maximum = valid.Select(sample => sample.OpdWaves).DefaultIfEmpty(0).Max();
        var series = new AnalysisSeries(
            "Pupil X",
            "Pupil Y",
            WavefrontAnalysis.BuildWavefrontMap(valid, _mapSize),
            AnalysisSeriesKind.Heatmap,
            ValueLabel: "OPD (waves)");
        var reference = _strategy == ReferenceSphereStrategy.CentroidSphere
            ? "centroid_sphere"
            : "best_fit_sphere";
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["RayCount"] = wavefront.Samples.Count,
            ["VignettedRayCount"] = wavefront.VignettedRayCount,
            ["ReferenceOpticalPathLength"] = wavefront.MeanReferenceOpticalPath,
            ["MeanOpticalPathDifference"] = mean * wavelength.Micrometers * 1e-3,
            ["RmsOpticalPathDifference"] = wavefront.Rms * wavelength.Micrometers * 1e-3,
            ["PeakToValleyOpticalPathDifference"] = (maximum - minimum) * wavelength.Micrometers * 1e-3,
            ["RmsWaves"] = wavefront.Rms,
            ["ReferenceSphereCenter"] = $"({wavefront.CenterX:R}, {wavefront.CenterY:R}, {wavefront.CenterZ:R})",
            ["ReferenceSphereRadius"] = wavefront.Radius,
            ["FieldHx"] = field.Hx,
            ["FieldHy"] = field.Hy,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["Reference"] = reference
        }, series, new[] { series }, new AnalysisPlotOptions(
            Title: $"OPD Map: RMS={wavefront.Rms:0.000} waves",
            EqualAspect: true,
            XMinimum: -1,
            XMaximum: 1,
            YMinimum: -1,
            YMaximum: 1));
    }
}
