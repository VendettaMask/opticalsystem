namespace OptilandWorkbench.Core.Analysis;

public sealed class ReferenceSphereWavefrontAnalysis : BaseAnalysis
{
    private readonly ReferenceSphereStrategy _strategy;
    private readonly int _numRings;
    private readonly int _mapSize;
    private readonly double _robustTrimStandardDeviations;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;

    public ReferenceSphereWavefrontAnalysis(
        Optic optic,
        ReferenceSphereStrategy strategy,
        int numRings = 15,
        int mapSize = 65,
        double robustTrimStandardDeviations = 3,
        int wavelengthNumber = 0,
        int fieldNumber = 1) : base(optic)
    {
        if (!Enum.IsDefined(strategy))
        {
            throw new ArgumentOutOfRangeException(nameof(strategy));
        }
        if (numRings is < 2 or > Raytrace.ApertureSampler.MaximumHexapolarRings)
        {
            throw new ArgumentOutOfRangeException(nameof(numRings));
        }
        if (!double.IsFinite(robustTrimStandardDeviations)
            || robustTrimStandardDeviations is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(robustTrimStandardDeviations));
        }

        _strategy = strategy;
        _numRings = numRings;
        _mapSize = AnalysisResourceLimits.ValidateWavefrontMapSize(mapSize, nameof(mapSize));
        _robustTrimStandardDeviations = robustTrimStandardDeviations;
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(0, fieldNumber);
    }

    public override string Name => _strategy == ReferenceSphereStrategy.CentroidSphere
        ? "Centroid Sphere Wavefront"
        : "Best Fit Sphere Wavefront";

    public override AnalysisData GenerateData()
    {
        var wavelengths = Optic.Wavelengths.ToArray();
        var wavelength = _wavelengthNumber > 0
            ? wavelengths.ElementAtOrDefault(Math.Clamp(
                _wavelengthNumber - 1,
                0,
                Math.Max(0, wavelengths.Length - 1)))
            : wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var field = _fieldNumber > 0
            ? fields[Math.Clamp(_fieldNumber - 1, 0, Math.Max(0, fields.Count - 1))]
            : fields.LastOrDefault();
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
            ValueLabel: "OPD (waves)",
            XQuantity: AnalysisAxisQuantity.PupilCoordinate,
            XUnit: AnalysisAxisUnit.Dimensionless,
            YQuantity: AnalysisAxisQuantity.PupilCoordinate,
            YUnit: AnalysisAxisUnit.Dimensionless,
            ValueQuantity: AnalysisAxisQuantity.WavefrontError,
            ValueUnit: AnalysisAxisUnit.Wave);
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
            ["WavelengthNumber"] = Array.IndexOf(wavelengths, wavelength) + 1,
            ["FieldNumber"] = _fieldNumber <= 0 ? fields.Count : _fieldNumber,
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
