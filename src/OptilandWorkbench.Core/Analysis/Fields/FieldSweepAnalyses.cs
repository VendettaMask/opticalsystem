using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class RmsVsFieldAnalysis : BaseAnalysis
{
    private readonly int _numRings;
    private readonly string _distribution;

    public RmsVsFieldAnalysis(
        Optic optic,
        int numFields = 64,
        int numRings = 6,
        string distribution = "hexapolar") : base(optic)
    {
        _numRings = Math.Max(1, numRings);
        _distribution = distribution;
    }

    public override string Name => "RMS vs Field";

    public override AnalysisData GenerateData()
    {
        var fields = AnalysisTrace.DefinedFieldSamples(Optic);
        var result = SpotAnalysisEngine.Generate(
            Optic,
            fields.Select(field => (field.Hx, field.Hy)),
            Optic.Wavelengths,
            _numRings,
            _distribution);
        var series = Optic.Wavelengths.Select((wavelength, wavelengthIndex) => new AnalysisSeries(
            AnalysisTrace.FieldAxisLabel(Optic),
            "RMS Spot Size (mm)",
            result.Fields.Select((field, fieldIndex) => new AnalysisPoint(
                fields[fieldIndex].Coordinate,
                SpotAnalysisEngine.RmsRadius(field.Wavelengths[wavelengthIndex].Rays),
                Label: fields[fieldIndex].Label)).ToArray(),
            Name: $"{wavelength.Micrometers:0.0000} \u00B5m",
            ColorIndex: wavelengthIndex)).ToArray();
        var maximum = series.SelectMany(item => item.Points).Select(point => point.Y).DefaultIfEmpty(0).Max();
        var values = new Dictionary<string, object>
        {
            ["FieldCount"] = fields.Count,
            ["WavelengthCount"] = Optic.Wavelengths.Count,
            ["NumRings"] = _numRings,
            ["Distribution"] = _distribution,
            ["MaximumRmsSpotSize"] = maximum
        };
        var definedFields = new AnalysisRunner(Optic).EvaluateRmsByField();
        foreach (var field in definedFields)
        {
            values[$"Field {field.FieldLabel}"] = field.RmsSpotRadius;
        }

        var includedWeight = definedFields.Where(field => field.FieldWeight > 0).Sum(field => field.FieldWeight);
        values["IncludedFieldWeight"] = includedWeight;
        values["WeightedMean"] = includedWeight <= 1e-12
            ? 0
            : definedFields.Where(field => field.FieldWeight > 0)
                .Sum(field => field.RmsSpotRadius * field.FieldWeight) / includedWeight;
        return new AnalysisData(Name, values, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            XMinimum: fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Min(),
            XMaximum: fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Max(),
            YMinimum: 0,
            ShowLegend: true));
    }
}

public sealed class RmsWavefrontVsFieldAnalysis : BaseAnalysis
{
    private readonly int _numRings;

    public RmsWavefrontVsFieldAnalysis(Optic optic, int numFields = 32, int numRings = 12) : base(optic)
    {
        _numRings = Math.Max(1, numRings);
    }

    public override string Name => "RMS Wavefront vs Field";

    public override AnalysisData GenerateData()
    {
        var fields = AnalysisTrace.DefinedFieldSamples(Optic);
        var series = Optic.Wavelengths.Select((wavelength, wavelengthIndex) => new AnalysisSeries(
            AnalysisTrace.FieldAxisLabel(Optic),
            "RMS Wavefront Error (waves)",
            fields.Select(field =>
            {
                var wavefront = WavefrontEngine.GenerateChiefRay(Optic, (field.Hx, field.Hy), wavelength, _numRings);
                return new AnalysisPoint(field.Coordinate, wavefront.Rms, Label: field.Label);
            }).ToArray(),
            Name: $"{wavelength.Micrometers:0.0000} \u00B5m",
            ColorIndex: wavelengthIndex)).ToArray();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["FieldCount"] = fields.Count,
            ["WavelengthCount"] = Optic.Wavelengths.Count,
            ["NumRings"] = _numRings,
            ["MaximumRmsWavefrontError"] = series.SelectMany(item => item.Points).Select(point => point.Y).DefaultIfEmpty(0).Max()
        }, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            XMinimum: fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Min(),
            XMaximum: fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Max(),
            YMinimum: 0,
            ShowLegend: true,
            GridOpacity: 0.25));
    }
}

public sealed class ZernikeVsFieldAnalysis : BaseAnalysis
{
    private readonly int _fieldDensity;
    private readonly int _numRings;
    private readonly int _numTerms;
    private readonly int _wavelengthNumber;

    public ZernikeVsFieldAnalysis(
        Optic optic,
        int fieldDensity = 20,
        int numRings = 12,
        int numTerms = 8,
        int wavelengthNumber = 0) : base(optic)
    {
        _fieldDensity = Math.Clamp(fieldDensity, 2, 200);
        _numRings = Math.Clamp(numRings, 2, 32);
        _numTerms = Math.Clamp(numTerms, 1, 64);
        _wavelengthNumber = wavelengthNumber;
    }

    public override string Name => "Zernike vs Field";

    public override AnalysisData GenerateData()
    {
        var wavelengths = Optic.Wavelengths.ToArray();
        var wavelength = _wavelengthNumber > 0
            ? wavelengths.ElementAtOrDefault(_wavelengthNumber - 1)
            : wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths.FirstOrDefault();
        if (wavelength is null || Optic.Fields.Count == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object>
            {
                ["Status"] = "No optical data"
            });
        }

        var maximumField = FieldCoordinates.MaximumRadius(Optic.Fields);
        var edgeField = Optic.Fields
            .OrderByDescending(field => Math.Sqrt((field.X * field.X) + (field.Y * field.Y)))
            .First();
        var edgeHx = maximumField <= 1e-12 ? 0 : edgeField.X / maximumField;
        var edgeHy = maximumField <= 1e-12 ? 0 : edgeField.Y / maximumField;
        var samples = Enumerable.Range(0, _fieldDensity + 1)
            .Select(index =>
            {
                var fraction = (double)index / _fieldDensity;
                var coordinate = maximumField * fraction;
                var coefficients = ZernikeFitEngine.FitFringe(
                    WavefrontEngine.GenerateChiefRay(
                        Optic,
                        (edgeHx * fraction, edgeHy * fraction),
                        wavelength,
                        _numRings).Samples,
                    _numTerms);
                return (Coordinate: coordinate, Coefficients: coefficients);
            })
            .ToArray();
        var axisUnit = Optic.FieldDefinition == FieldDefinitionKind.Angle ? "度" : "毫米";
        var series = Enumerable.Range(1, _numTerms)
            .Select(term => new AnalysisSeries(
                $"视场为 {axisUnit}",
                "波前差 (waves)",
                samples.Select(sample =>
                {
                    var coefficient = sample.Coefficients.First(item => item.Number == term);
                    return new AnalysisPoint(sample.Coordinate, coefficient.Value);
                }).ToArray(),
                Name: term.ToString(),
                ColorIndex: term - 1,
                LineWidth: 1.2))
            .ToArray();
        var extrema = series.SelectMany(item => item.Points)
            .Select(point => point.Y)
            .DefaultIfEmpty(0)
            .ToArray();
        var minimum = extrema.Min();
        var maximum = extrema.Max();
        var padding = Math.Max(0.005, (maximum - minimum) * 0.05);
        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["FieldDensity"] = _fieldDensity,
                ["NumRings"] = _numRings,
                ["ZernikeTerms"] = _numTerms,
                ["WavelengthNumber"] = Array.IndexOf(wavelengths, wavelength) + 1,
                ["WavelengthMicrometers"] = wavelength.Micrometers,
                ["MaximumField"] = maximumField,
                ["PolynomialType"] = "Fringe"
            },
            series.FirstOrDefault(),
            series,
            new AnalysisPlotOptions(
                Title: "Zernike Fringe系数项 vs. 视场",
                XMinimum: 0,
                XMaximum: maximumField,
                YMinimum: minimum - padding,
                YMaximum: maximum + padding,
                ShowLegend: true,
                HideTopAndRightAxes: true,
                GridOpacity: 0.25,
                LegendBelow: true));
    }
}

public enum AngleScanMode
{
    ThroughPupil,
    ThroughField
}

public sealed class IncidentAngleVsImageHeightAnalysis : BaseAnalysis
{
    private readonly int _fieldDensity;
    private readonly int _wavelengthNumber;
    private readonly int _surfaceIndex;

    public IncidentAngleVsImageHeightAnalysis(
        Optic optic,
        int fieldDensity = 20,
        int wavelengthNumber = 0,
        int surfaceIndex = -1) : base(optic)
    {
        _fieldDensity = Math.Clamp(fieldDensity, 2, 200);
        _wavelengthNumber = wavelengthNumber;
        _surfaceIndex = surfaceIndex;
    }

    public override string Name => "Angle vs Image Height";

    public override AnalysisData GenerateData()
    {
        var wavelengths = Optic.Wavelengths.ToArray();
        var wavelength = _wavelengthNumber > 0 && wavelengths.Length > 0
            ? wavelengths[Math.Clamp(_wavelengthNumber - 1, 0, wavelengths.Length - 1)]
            : wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths.FirstOrDefault();
        if (wavelength is null || Optic.SurfaceGroup.Items.Count == 0 || Optic.Fields.Count == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object>
            {
                ["Status"] = "No optical data"
            });
        }

        var surfaceIndex = _surfaceIndex < 0
            ? Optic.SurfaceGroup.Items.Count + _surfaceIndex
            : _surfaceIndex;
        surfaceIndex = Math.Clamp(surfaceIndex, 0, Optic.SurfaceGroup.Items.Count - 1);

        var maximumField = FieldCoordinates.MaximumRadius(Optic.Fields);
        var edgeField = Optic.Fields
            .OrderByDescending(field => Math.Sqrt((field.X * field.X) + (field.Y * field.Y)))
            .First();
        var fieldX = maximumField <= 1e-12 ? 0 : edgeField.X / maximumField;
        var fieldY = maximumField <= 1e-12 ? 0 : edgeField.Y / maximumField;
        var axis = Math.Abs(edgeField.X) > Math.Abs(edgeField.Y) ? 0 : 1;
        var rayDefinitions = new[]
        {
            (Pupil: -1.0, Name: "较小光瞳点光线", ColorIndex: 0),
            (Pupil: 0.0, Name: "主光线", ColorIndex: 2),
            (Pupil: 1.0, Name: "较大光瞳点光线", ColorIndex: 3)
        };
        var fieldSamples = Enumerable.Range(0, _fieldDensity + 1)
            .Select(index =>
            {
                var fraction = (double)index / _fieldDensity;
                var hx = fieldX * fraction;
                var hy = fieldY * fraction;
                var chiefHistory = Optic.TraceGeneric(hx, hy, 0, 0, wavelength.Micrometers)
                    .RayHistories
                    .Single();
                var imageHeight = chiefHistory.Count <= surfaceIndex
                    ? double.NaN
                    : axis == 0
                        ? chiefHistory[surfaceIndex].Position.X
                        : chiefHistory[surfaceIndex].Position.Y;
                return (Hx: hx, Hy: hy, ImageHeight: Math.Abs(imageHeight));
            })
            .ToArray();

        var series = rayDefinitions.Select(ray =>
        {
            var points = new List<AnalysisPoint>(_fieldDensity + 1);
            foreach (var fieldSample in fieldSamples)
            {
                var px = axis == 0 ? ray.Pupil : 0;
                var py = axis == 1 ? ray.Pupil : 0;
                var history = Optic.TraceGeneric(
                        fieldSample.Hx,
                        fieldSample.Hy,
                        px,
                        py,
                        wavelength.Micrometers)
                    .RayHistories
                    .Single();
                if (history.Count <= surfaceIndex)
                {
                    points.Add(new AnalysisPoint(double.NaN, double.NaN));
                    continue;
                }

                var sample = history[surfaceIndex];
                var directionCosine = axis == 0 ? sample.Direction.X : sample.Direction.Y;
                var incidentAngle = Math.Asin(Math.Clamp(directionCosine, -1, 1)) * 180 / Math.PI;
                points.Add(new AnalysisPoint(fieldSample.ImageHeight, incidentAngle));
            }

            return new AnalysisSeries(
                "像高：毫米",
                "入射角（度）",
                points,
                Name: ray.Name,
                ColorIndex: ray.ColorIndex,
                LineWidth: 1.5);
        }).ToArray();

        var finitePoints = series.SelectMany(item => item.Points)
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
            .ToArray();
        var maximumImageHeight = finitePoints.Select(point => point.X).DefaultIfEmpty(1).Max();
        var maximumAngle = finitePoints.Select(point => Math.Abs(point.Y)).DefaultIfEmpty(0).Max();
        var angleLimit = Math.Max(25, Math.Ceiling(maximumAngle / 5) * 5);

        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["FieldDensity"] = _fieldDensity,
                ["WavelengthNumber"] = Array.IndexOf(wavelengths, wavelength) + 1,
                ["WavelengthMicrometers"] = wavelength.Micrometers,
                ["SurfaceIndex"] = surfaceIndex,
                ["RayCount"] = series.Length,
                ["PointCountPerRay"] = _fieldDensity + 1
            },
            series[0],
            series,
            new AnalysisPlotOptions(
                Title: "入射角 vs. 像高",
                ShowHorizontalZeroLine: true,
                XMinimum: 0,
                XMaximum: maximumImageHeight,
                YMinimum: -angleLimit,
                YMaximum: angleLimit,
                ShowLegend: true,
                HideTopAndRightAxes: true,
                GridOpacity: 0.25,
                LegendBelow: true));
    }
}

public sealed class IncidentAngleVsHeightAnalysis : BaseAnalysis
{
    private readonly AngleScanMode _mode;
    private readonly int _surfaceIndex;
    private readonly int _axis;
    private readonly int _numPoints;
    private readonly (double X, double Y) _fixedCoordinate;

    public IncidentAngleVsHeightAnalysis(
        Optic optic,
        AngleScanMode mode,
        int surfaceIndex = -1,
        int axis = 1,
        int numPoints = 128,
        (double X, double Y)? fixedCoordinate = null) : base(optic)
    {
        _mode = mode;
        _surfaceIndex = surfaceIndex;
        _axis = axis == 0 ? 0 : 1;
        _numPoints = Math.Max(2, numPoints);
        _fixedCoordinate = fixedCoordinate ?? (0, 0);
    }

    public override string Name => _mode == AngleScanMode.ThroughPupil
        ? "Angle vs Image Height - Through Pupil"
        : "Angle vs Image Height - Through Field";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null || Optic.SurfaceGroup.Items.Count == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No optical data" });
        }

        var surfaceIndex = _surfaceIndex < 0
            ? Optic.SurfaceGroup.Items.Count + _surfaceIndex
            : _surfaceIndex;
        surfaceIndex = Math.Clamp(surfaceIndex, 0, Optic.SurfaceGroup.Items.Count - 1);
        var definedFields = AnalysisTrace.DefinedFieldSamples(Optic);
        var scan = _mode == AngleScanMode.ThroughField
            ? definedFields.Select(field => (field.Hx, field.Hy, Value: field.Coordinate, field.Label)).ToArray()
            : Enumerable.Range(0, _numPoints)
                .Select(index =>
                {
                    var coordinate = -1 + (2.0 * index / (_numPoints - 1));
                    return (
                        Hx: _fixedCoordinate.X,
                        Hy: _fixedCoordinate.Y,
                        Value: coordinate,
                        Label: string.Empty);
                })
                .ToArray();
        var points = new List<AnalysisPoint>(scan.Length);
        foreach (var coordinate in scan)
        {
            var hx = coordinate.Hx;
            var hy = coordinate.Hy;
            var px = _mode == AngleScanMode.ThroughPupil && _axis == 0 ? coordinate.Value : _fixedCoordinate.X;
            var py = _mode == AngleScanMode.ThroughPupil && _axis == 1 ? coordinate.Value : _fixedCoordinate.Y;
            var history = Optic.TraceGeneric(hx, hy, px, py, wavelength.Micrometers).RayHistories.Single();
            if (history.Count <= surfaceIndex)
            {
                points.Add(new AnalysisPoint(double.NaN, double.NaN, coordinate.Label, coordinate.Value));
                continue;
            }

            var sample = history[surfaceIndex];
            var height = _axis == 1 ? sample.Position.Y : sample.Position.X;
            var directionCosine = _axis == 1 ? sample.Direction.Y : sample.Direction.X;
            var angle = Math.Asin(Math.Clamp(directionCosine, -1, 1)) * 180 / Math.PI;
            points.Add(new AnalysisPoint(height, angle, coordinate.Label, coordinate.Value));
        }

        var fixedLabel = _mode == AngleScanMode.ThroughPupil
            ? MtfPresentation.FieldName(Optic, _fixedCoordinate)
            : $"Px={_fixedCoordinate.X:0.####} Py={_fixedCoordinate.Y:0.####}";
        var valueLabel = _mode == AngleScanMode.ThroughPupil
            ? $"Normalized Pupil Coordinate ({(_axis == 0 ? "Px" : "Py")})"
            : AnalysisTrace.FieldAxisLabel(Optic);
        var series = new AnalysisSeries(
            "Image Height in Millimeters",
            "Incident Angle in Degrees",
            points,
            AnalysisSeriesKind.ColoredLine,
            $"{fixedLabel}, {wavelength.Micrometers:0.0000} \u00B5m",
            LineWidth: 3,
            ValueLabel: valueLabel);
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["ScanMode"] = _mode.ToString(),
            ["SurfaceIndex"] = surfaceIndex,
            ["Axis"] = _axis == 0 ? "X" : "Y",
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["PointCount"] = points.Count,
            ["FixedCoordinates"] = fixedLabel
        }, series, new[] { series }, new AnalysisPlotOptions(
            Title: $"Incident Angle vs Image Height{(_axis == 0 ? " (x-axis)" : string.Empty)}",
            GridOpacity: 0.25));
    }
}
