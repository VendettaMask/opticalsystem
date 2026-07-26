using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class EncircledEnergyAnalysis : BaseAnalysis
{
    private readonly int _numRays;
    private readonly string _distribution;
    private readonly int _numPoints;

    public EncircledEnergyAnalysis(
        Optic optic,
        int numRays = 10_000,
        string distribution = "sobol",
        int numPoints = 256) : base(optic)
    {
        _numRays = Math.Max(1, numRays);
        _distribution = distribution;
        _numPoints = Math.Max(2, numPoints);
    }

    public override string Name => "Encircled Energy";

    public override AnalysisData GenerateData()
    {
        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var primary = Optic.Wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (primary is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var result = SpotAnalysisEngine.Generate(Optic, fields, new[] { primary }, _numRays, _distribution);
        var fieldRadii = result.Fields.Select(field => field.Wavelengths[0].Rays
            .Select(ray => (
                Radius: Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y)),
                Weight: ray.Intensity))
            .OrderBy(item => item.Radius)
            .ToArray()).ToArray();
        var geometricRadius = fieldRadii
            .SelectMany(radii => radii)
            .Select(item => item.Radius)
            .DefaultIfEmpty(0)
            .Max();
        var radiusMaximum = geometricRadius * 1.2;
        var series = result.Fields.Select((field, fieldIndex) =>
        {
            var radii = fieldRadii[fieldIndex];
            var cumulativeWeights = CumulativeWeights(radii);
            var points = Enumerable.Range(0, _numPoints).Select(index =>
            {
                var radius = radiusMaximum * index / (_numPoints - 1.0);
                var energy = EnergyWithinRadius(radii, cumulativeWeights, radius);
                return new AnalysisPoint(radius, energy);
            }).ToArray();
            return new AnalysisSeries(
                "Radius (mm)",
                "Encircled Energy (-)",
                points,
                Name: MtfPresentation.FieldName(Optic, (field.Hx, field.Hy)),
                ColorIndex: fieldIndex);
        }).ToArray();
        var weightedRadii = fieldRadii
            .SelectMany(radii => radii)
            .OrderBy(item => item.Radius)
            .ToArray();
        var totalWeight = weightedRadii.Sum(item => item.Weight);
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["RayCount"] = result.RayCount,
            ["VignettedRayCount"] = result.VignettedRayCount,
            ["FieldCount"] = result.Fields.Count,
            ["WavelengthMicrometers"] = primary.Micrometers,
            ["NumRays"] = _numRays,
            ["Distribution"] = _distribution,
            ["PlotPointCount"] = _numPoints,
            ["MaximumGeometricSpotRadius"] = geometricRadius,
            ["TotalWeight"] = totalWeight,
            ["Radius50"] = RadiusAtEnergy(weightedRadii, totalWeight, 0.50),
            ["Radius80"] = RadiusAtEnergy(weightedRadii, totalWeight, 0.80),
            ["Radius95"] = RadiusAtEnergy(weightedRadii, totalWeight, 0.95)
        }, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            Title: $"Wavelength: {primary.Micrometers:0.0000} \u00B5m",
            XMinimum: 0,
            YMinimum: 0,
            ShowLegend: true));
    }

    private static double[] CumulativeWeights(IReadOnlyList<(double Radius, double Weight)> radii)
    {
        var cumulative = new double[radii.Count];
        var total = 0.0;
        for (var index = 0; index < radii.Count; index++)
        {
            total += radii[index].Weight;
            cumulative[index] = total;
        }

        return cumulative;
    }

    private static double EnergyWithinRadius(
        IReadOnlyList<(double Radius, double Weight)> radii,
        IReadOnlyList<double> cumulativeWeights,
        double radius)
    {
        var lower = 0;
        var upper = radii.Count;
        while (lower < upper)
        {
            var middle = lower + ((upper - lower) / 2);
            if (radii[middle].Radius <= radius)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle;
            }
        }

        return lower == 0 ? 0 : cumulativeWeights[lower - 1];
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
}

public sealed class PupilAberrationAnalysis : BaseAnalysis
{
    private readonly int _numPoints;

    public PupilAberrationAnalysis(Optic optic, int numPoints = 256) : base(optic)
    {
        _numPoints = Math.Max(3, numPoints % 2 == 0 ? numPoints + 1 : numPoints);
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
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
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
                var realX = TraceAtSurface(Optic, field, wavelength, pupil, stopIndex, xFan: true);
                var realY = TraceAtSurface(Optic, field, wavelength, pupil, stopIndex, xFan: false);
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
        var yMinimum = finite.DefaultIfEmpty(-1).Min();
        var yMaximum = finite.DefaultIfEmpty(1).Max();
        ExpandRange(ref yMinimum, ref yMaximum);
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
                ColorIndex: wavelengthIndex);
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
        int surfaceIndex,
        bool xFan)
    {
        var pupilSamples = pupil.Select(value => new PupilSample(xFan ? value : 0, xFan ? 0 : value, 1));
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            field.Hx,
            field.Hy,
            wavelength.Micrometers,
            pupilSamples);
        return optic.SequentialRayTracer.Trace(bundle).RayHistories.Select(history =>
        {
            var sample = history.FirstOrDefault(item => item.SurfaceNumber == surfaceIndex);
            return sample is null
                ? new RayFanSample(double.NaN, 0)
                : new RayFanSample(xFan ? sample.Position.X : sample.Position.Y, sample.Intensity);
        }).ToArray();
    }

    private static void ExpandRange(ref double minimum, ref double maximum)
    {
        if (Math.Abs(maximum - minimum) < 1e-12)
        {
            minimum -= 1;
            maximum += 1;
            return;
        }

        var padding = (maximum - minimum) * 0.05;
        minimum -= padding;
        maximum += padding;
    }

    private sealed record RayFanSample(double Value, double Intensity);

    private sealed record PupilWave(
        Wavelength Wavelength,
        IReadOnlyList<RayFanSample> X,
        IReadOnlyList<RayFanSample> Y);
}
