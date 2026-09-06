using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class EncircledEnergyAnalysis : BaseAnalysis
{
    public bool ZemaxCompatibleOutput { get; init; }
    private readonly int _numRays;
    private readonly string _distribution;
    private readonly int _numPoints;
    private readonly int _wavelengthNumber;
    private readonly string _reference;
    private readonly double _maximumDistanceMicrometers;
    private readonly bool _multiplyByDiffractionLimit;

    public EncircledEnergyAnalysis(
        Optic optic,
        int numRays = 10_000,
        string distribution = "sobol",
        int numPoints = 256,
        int wavelengthNumber = 0,
        string reference = "centroid",
        double maximumDistanceMicrometers = 0,
        bool multiplyByDiffractionLimit = true) : base(optic)
    {
        _numRays = Math.Max(1, numRays);
        _distribution = distribution;
        _numPoints = Math.Max(2, numPoints);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _reference = reference;
        _maximumDistanceMicrometers = Math.Max(0, maximumDistanceMicrometers);
        _multiplyByDiffractionLimit = multiplyByDiffractionLimit;
    }

    public override string Name => "Encircled Energy";

    public override AnalysisData GenerateData()
    {
        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        IReadOnlyList<Wavelength> wavelengths = AnalysisTrace.SelectWavelengths(Optic, _wavelengthNumber);
        if (wavelengths.Count == 0)
        {
            return AnalysisData.Unavailable(Name, "No wavelengths");
        }

        var result = SpotAnalysisEngine.Generate(
            Optic,
            fields,
            wavelengths,
            _numRays,
            ZemaxCompatibleOutput && _distribution.Equals("uniform", StringComparison.OrdinalIgnoreCase) ? "uniform-intervals" : _distribution,
            reference: "absolute",
            aimAtStop: Optic.RayAimingEnabled, includeSurfaceTransmission: false);
        var curves = result.Fields.Select((field, fieldIndex) =>
        {
            var samples = field.Wavelengths
                .SelectMany(wavelength => wavelength.Rays.Select(ray => new EnergySample(
                    ray.X * 1000,
                    ray.Y * 1000,
                    ray.Intensity * EnergyCurveSupport.WavelengthWeight(wavelength.Wavelength))))
                .Where(sample => sample.Weight > 0)
                .ToArray();
            var center = EnergyCurveSupport.ReferencePoint(
                Optic,
                (field.Hx, field.Hy),
                wavelengths,
                samples,
                _reference);
            var radii = samples
                .Select(sample => (
                    Radius: Math.Sqrt(
                        Math.Pow(sample.X - center.X, 2)
                        + Math.Pow(sample.Y - center.Y, 2)),
                    sample.Weight))
                .Where(item => double.IsFinite(item.Radius))
                .OrderBy(item => item.Radius)
                .ToArray();
            return new FieldEnergyCurve(fieldIndex, field.Hx, field.Hy, samples, center, radii);
        }).ToArray();
        if (curves.Length == 0 || curves.All(curve => curve.Radii.Length == 0))
        {
            return EnergyCurveSupport.Empty(Name);
        }

        var geometricMaximumMicrometers = curves.SelectMany(curve => curve.Radii)
            .Select(item => item.Radius)
            .DefaultIfEmpty(0)
            .Max();
        var radiusMaximumMicrometers = _maximumDistanceMicrometers > 0
            ? _maximumDistanceMicrometers
            : geometricMaximumMicrometers * 1.05;
        var airyComponents = _multiplyByDiffractionLimit
            ? wavelengths.Select(wavelength => (
                    Wavelength: wavelength.Micrometers,
                    Weight: EnergyCurveSupport.WavelengthWeight(wavelength),
                    FNumber: DiffractionEngine.WorkingFNumber(Optic, (0, 0), wavelength, aimAtStop: Optic.RayAimingEnabled)))
                .Where(component => component.Wavelength > 0
                    && component.Weight > 0
                    && double.IsFinite(component.FNumber)
                    && component.FNumber > 0)
                .ToArray()
            : Array.Empty<(double Wavelength, double Weight, double FNumber)>();
        var series = curves.Select(curve =>
        {
            var data = EnergyCurveSupport.CreateCurve(
                Name,
                curve.Samples,
                curve.Center,
                "encircled",
                radiusMaximumMicrometers,
                ZemaxCompatibleOutput ? 100 : _numPoints,
                new Dictionary<string, object>());
            var points = data.Series?.Points ?? Array.Empty<AnalysisPoint>();
            if (_multiplyByDiffractionLimit)
            {
                points = points.Select(point => new AnalysisPoint(
                    point.X,
                    point.Y * PolychromaticAiryEnergy(point.X, airyComponents))).ToArray();
            }
            if (ZemaxCompatibleOutput) points = EnergyPlotSampling.Geometric(points);
            return new AnalysisSeries(
                "Radius (µm)",
                "Fraction of Energy",
                points,
                Name: MtfPresentation.FieldName(Optic, (curve.Hx, curve.Hy)),
                ColorIndex: curve.FieldIndex,
                XQuantity: AnalysisAxisQuantity.Radius,
                XUnit: AnalysisAxisUnit.Micrometer,
                YQuantity: AnalysisAxisQuantity.EnergyFraction,
                YUnit: AnalysisAxisUnit.Dimensionless);
        }).ToArray();
        var weightedRadii = curves
            .SelectMany(curve => curve.Radii)
            .OrderBy(item => item.Radius)
            .ToArray();
        var totalWeight = weightedRadii.Sum(item => item.Weight);
        var primary = wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
            ?? wavelengths.First();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["RayCount"] = result.RayCount,
            ["VignettedRayCount"] = result.VignettedRayCount,
            ["FieldCount"] = result.Fields.Count,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["WavelengthCount"] = wavelengths.Count,
            ["WavelengthMicrometers"] = primary.Micrometers,
            ["NumRays"] = _numRays,
            ["Distribution"] = _distribution,
            ["Reference"] = _reference,
            ["MultiplyByDiffractionLimit"] = _multiplyByDiffractionLimit,
            ["PlotPointCount"] = series[0].Points.Count,
            ["ZemaxCompatibleOutput"] = ZemaxCompatibleOutput,
            ["PupilGridConvention"] = ZemaxCompatibleOutput && _distribution.Equals("uniform", StringComparison.OrdinalIgnoreCase) ? "N intervals, N+1 axis nodes, inclusive disk boundary" : _distribution,
            ["MaximumDistanceMicrometers"] = radiusMaximumMicrometers,
            ["MaximumGeometricSpotRadius"] = geometricMaximumMicrometers / 1000,
            ["TotalWeight"] = totalWeight,
            ["Radius50"] = RadiusAtEnergy(weightedRadii, totalWeight, 0.50) / 1000,
            ["Radius80"] = RadiusAtEnergy(weightedRadii, totalWeight, 0.80) / 1000,
            ["Radius95"] = RadiusAtEnergy(weightedRadii, totalWeight, 0.95) / 1000
        }, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            Title: wavelengths.Count == 1
                ? $"Wavelength: {primary.Micrometers:0.0000} \u00B5m"
                : "Wavelength: Polychromatic",
            XMinimum: 0,
            XMaximum: radiusMaximumMicrometers,
            YMinimum: 0,
            YMaximum: 1,
            ShowLegend: true));
    }

    private static double PolychromaticAiryEnergy(
        double radiusMicrometers,
        IReadOnlyList<(double Wavelength, double Weight, double FNumber)> components)
    {
        var totalWeight = components.Sum(component => component.Weight);
        return totalWeight <= 0
            ? 0
            : components.Sum(component => component.Weight
                * DiffractionEncircledEnergyAnalysis.IdealAiryEncircledEnergy(
                    radiusMicrometers,
                    component.Wavelength,
                    component.FNumber)) / totalWeight;
    }

    private static double RadiusAtEnergy(
        IReadOnlyList<(double Radius, double Weight)> radii,
        double totalWeight,
        double fraction)
    {
        var target = totalWeight * fraction;
        var cumulative = 0.0;
        foreach (var item in radii)
        {
            cumulative += item.Weight;
            if (cumulative >= target)
            {
                return item.Radius;
            }
        }

        return radii.Count == 0 ? 0 : radii[^1].Radius;
    }

    private sealed record FieldEnergyCurve(
        int FieldIndex,
        double Hx,
        double Hy,
        IReadOnlyList<EnergySample> Samples,
        (double X, double Y) Center,
        (double Radius, double Weight)[] Radii);
}

public sealed class PupilAberrationAnalysis : BaseAnalysis
{
    private readonly int _numberOfRaysEachSide;
    private readonly int _numPoints;

    public PupilAberrationAnalysis(Optic optic, int numPoints = 256) : base(optic)
    {
        _numPoints = Math.Clamp(numPoints, 3, 8193);
        _numPoints = _numPoints % 2 == 0 ? _numPoints + 1 : _numPoints;
        _numberOfRaysEachSide = (_numPoints - 1) / 2;
    }

    public override string Name => "Pupil Aberration";

    public override AnalysisData GenerateData()
    {
        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var wavelengths = Optic.Wavelengths.ToArray();
        var primary = wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
            ?? wavelengths.FirstOrDefault();
        if (primary is null)
        {
            return AnalysisData.Unavailable(Name, "No wavelengths");
        }

        var stopIndex = Optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        stopIndex = stopIndex < 0 ? 0 : stopIndex;
        var pupil = Enumerable.Range(0, _numPoints)
            .Select(index => -1 + (2.0 * index / (_numPoints - 1.0)))
            .ToArray();
        var paraxial = Optic.Paraxial.TraceNormalizedPupil(0, pupil, primary.Micrometers);
        var paraxialReference = paraxial.Heights[stopIndex].ToArray();
        var stopRadius = Optic.Paraxial.TraceNormalizedPupil(0, new[] { 1.0 }, primary.Micrometers).Heights[stopIndex][0];
        var fieldData = new List<(double Hx, double Hy, List<PupilWave> Waves)>();
        foreach (var field in fields)
        {
            var waves = new List<PupilWave>();
            foreach (var wavelength in wavelengths)
            {
                var realX = TraceAtSurface(
                    Optic,
                    field,
                    wavelength,
                    pupil,
                    paraxialReference,
                    stopIndex,
                    xFan: true);
                var realY = TraceAtSurface(
                    Optic,
                    field,
                    wavelength,
                    pupil,
                    paraxialReference,
                    stopIndex,
                    xFan: false);
                var errorX = realX.Select((sample, index) => new RayFanSample(
                    Math.Abs(stopRadius) <= 1e-30 ? 0 : (paraxialReference[index] - sample.Value) / stopRadius * 100,
                    sample.Intensity)).ToArray();
                var errorY = realY.Select((sample, index) => new RayFanSample(
                    Math.Abs(stopRadius) <= 1e-30 ? 0 : (paraxialReference[index] - sample.Value) / stopRadius * 100,
                    sample.Intensity)).ToArray();
                waves.Add(new PupilWave(wavelength, errorX, errorY));
            }

            fieldData.Add((field.Hx, field.Hy, waves));
        }

        var finite = fieldData.SelectMany(field => field.Waves)
            .SelectMany(wave => wave.X.Concat(wave.Y))
            .Where(point => point.Intensity > 0 && double.IsFinite(point.Value))
            .Select(point => point.Value)
            .ToArray();
        var displayScale = PupilDisplayScale(
            finite.Select(Math.Abs).DefaultIfEmpty(0).Max());
        var yMinimum = -displayScale;
        var yMaximum = displayScale;
        var panes = new List<AnalysisPlotPane>();
        for (var fieldIndex = 0; fieldIndex < fieldData.Count; fieldIndex++)
        {
            var field = fieldData[fieldIndex];
            var title = MtfPresentation.FieldName(Optic, (field.Hx, field.Hy));
            panes.Add(PupilPane(field.Waves, pupil, title, yMinimum, yMaximum, yFan: true));
            panes.Add(PupilPane(field.Waves, pupil, title, yMinimum, yMaximum, yFan: false));
        }

        var firstSeries = panes.FirstOrDefault()?.Series.FirstOrDefault();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["NumberOfRaysEachSide"] = _numberOfRaysEachSide,
            ["Samples"] = _numPoints,
            ["FieldCount"] = fields.Count,
            ["WavelengthCount"] = wavelengths.Length,
            ["ParaxialStopRadius"] = stopRadius,
            ["MinimumPupilAberration"] = finite.DefaultIfEmpty(0).Min(),
            ["MaximumPupilAberration"] = finite.DefaultIfEmpty(0).Max()
        }, firstSeries, firstSeries is null ? null : new[] { firstSeries }, PlotPanes: panes, PlotPaneColumns: 2);
    }

    private static AnalysisPlotPane PupilPane(
        IReadOnlyList<PupilWave> waves,
        IReadOnlyList<double> pupil,
        string title,
        double yMinimum,
        double yMaximum,
        bool yFan)
    {
        var series = waves.Select((wave, wavelengthIndex) =>
        {
            var samples = yFan ? wave.Y : wave.X;
            return new AnalysisSeries(
                yFan ? "P_y" : "P_x",
                "Pupil Aberration (%)",
                samples.Select((sample, index) => new AnalysisPoint(
                    pupil[index],
                    sample.Intensity > 0 ? sample.Value : double.NaN)).ToArray(),
                Name: $"{wave.Wavelength.Micrometers:0.0000} \u00B5m",
                ColorIndex: wavelengthIndex,
                XQuantity: AnalysisAxisQuantity.PupilCoordinate,
                XUnit: AnalysisAxisUnit.Dimensionless,
                YQuantity: AnalysisAxisQuantity.Distortion,
                YUnit: AnalysisAxisUnit.Percent);
        }).ToArray();
        return new AnalysisPlotPane(title, series, new AnalysisPlotOptions(
            Title: title,
            ShowVerticalZeroLine: true,
            ShowHorizontalZeroLine: true,
            XMinimum: -1,
            XMaximum: 1,
            YMinimum: yMinimum,
            YMaximum: yMaximum));
    }

    private static IReadOnlyList<RayFanSample> TraceAtSurface(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        IReadOnlyList<double> pupil,
        IReadOnlyList<double> paraxialReference,
        int surfaceIndex,
        bool xFan)
    {
        var pupilSamples = pupil.Select(value => new PupilSample(xFan ? value : 0, xFan ? 0 : value, 1));
        var stopTargets = paraxialReference
            .Select(value => xFan ? (X: value, Y: 0.0) : (X: 0.0, Y: value))
            .ToArray();
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            field.Hx,
            field.Hy,
            wavelength.Micrometers,
            pupilSamples,
            aimAtStop: optic.RayAimingEnabled,
            stopTargets: optic.RayAimingEnabled ? stopTargets : null);
        var targetIndex = optic.SurfaceGroup.Items
            .Select((surface, index) => (surface, index))
            .Where(item => item.surface.Number == surfaceIndex)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
        if (targetIndex < 0)
        {
            return pupil
                .Select(_ => new RayFanSample(double.NaN, 0))
                .ToArray();
        }

        using var trace = optic.SequentialRayTracer.Trace(
            bundle,
            TraceRequest.Selected(new[] { targetIndex }));
        return trace.GetSurfaceSamples(targetIndex).Select(sampleValue =>
        {
            return sampleValue is not { } sample
                ? new RayFanSample(double.NaN, 0)
                : new RayFanSample(xFan ? sample.Position.X : sample.Position.Y, sample.Intensity);
        }).ToArray();
    }

    private static double PupilDisplayScale(double maximumAbsoluteAberration)
    {
        const double minimumScale = 1e-5;
        if (!double.IsFinite(maximumAbsoluteAberration)
            || maximumAbsoluteAberration <= minimumScale)
        {
            return minimumScale;
        }

        var baseScale = Math.Pow(10, Math.Floor(Math.Log10(maximumAbsoluteAberration)));
        return maximumAbsoluteAberration <= baseScale * (1 + 1e-12)
            ? baseScale
            : baseScale * 10;
    }

    private sealed record RayFanSample(double Value, double Intensity);

    private sealed record PupilWave(
        Wavelength Wavelength,
        IReadOnlyList<RayFanSample> X,
        IReadOnlyList<RayFanSample> Y);
}
