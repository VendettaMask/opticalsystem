using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class RmsVsFieldAnalysis : BaseAnalysis
{
    private readonly int _fieldDensity;
    private readonly int _numRings;
    private readonly string _distribution;
    private readonly string _method;
    private readonly string _data;
    private readonly string _reference;
    private readonly int _wavelengthNumber;
    private readonly bool _showDiffractionLimit;
    private readonly bool _usePolarization;
    private readonly bool _removeVignetting;
    private readonly string _scanDirection;

    public RmsVsFieldAnalysis(
        Optic optic,
        int numFields = 64,
        int numRings = 6,
        string distribution = "hexapolar",
        string method = "GQ",
        string data = "spot",
        string reference = "centroid",
        int wavelengthNumber = 0,
        bool showDiffractionLimit = false,
        bool usePolarization = false,
        bool removeVignetting = true,
        int fieldDensity = 0,
        string scanDirection = "+y") : base(optic)
    {
        _fieldDensity = Math.Clamp(fieldDensity > 0 ? fieldDensity : numFields - 1, 1, 200);
        _numRings = Math.Max(1, numRings);
        _distribution = distribution;
        _method = RmsScanSupport.NormalizeMethod(method);
        _data = RmsScanSupport.NormalizeData(data);
        _reference = RmsScanSupport.NormalizeReference(reference);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _showDiffractionLimit = showDiffractionLimit;
        _usePolarization = usePolarization;
        _removeVignetting = removeVignetting;
        _scanDirection = AnalysisTrace.NormalizeScanDirection(scanDirection);
    }

    public override string Name => "RMS vs Field";

    public override AnalysisData GenerateData()
    {
        if (_data == "wavefront")
        {
            var wavefront = new RmsWavefrontVsFieldAnalysis(
                Optic,
                numFields: _fieldDensity + 1,
                numRings: _numRings,
                fieldDensity: _fieldDensity,
                method: _method,
                reference: _reference,
                wavelengthNumber: _wavelengthNumber,
                scanType: _scanDirection,
                removeVignettingFactors: _removeVignetting,
                zemaxCompatibleOutput: true).GenerateData();
            var wavefrontSeries = wavefront.PlotSeries.ToList();
            IReadOnlyList<Wavelength> wavelengthSelection = AnalysisTrace.SelectWavelengths(Optic, _wavelengthNumber);
            var wavefrontDiffractionLimit = RmsScanSupport.DiffractionLimitValue(Optic, wavelengthSelection, _data);
            if (_showDiffractionLimit && wavefrontDiffractionLimit > 0 && wavefrontSeries.Count > 0)
            {
                wavefrontSeries.Add(new AnalysisSeries(
                    wavefrontSeries[0].XAxisLabel,
                    wavefrontSeries[0].YAxisLabel,
                    wavefrontSeries[0].Points.Select(point => new AnalysisPoint(point.X, wavefrontDiffractionLimit)).ToArray(),
                    Name: "Diffraction Limit",
                    LineStyle: AnalysisLineStyle.Dashed,
                    ColorIndex: wavefrontSeries.Count,
                    XQuantity: wavefrontSeries[0].XQuantity,
                    XUnit: wavefrontSeries[0].XUnit,
                    YQuantity: wavefrontSeries[0].YQuantity,
                    YUnit: wavefrontSeries[0].YUnit));
            }

            var wavefrontValues = wavefront.Values.ToDictionary(item => item.Key, item => item.Value);
            wavefrontValues["Data"] = _data;
            wavefrontValues["Distribution"] = RmsScanSupport.EffectiveDistribution(_method, _distribution);
            wavefrontValues["ShowDiffractionLimit"] = _showDiffractionLimit;
            wavefrontValues["DiffractionLimitValue"] = wavefrontDiffractionLimit;
            wavefrontValues["DiffractionLimitUnit"] = RmsScanSupport.DiffractionLimitUnit(_data);
            wavefrontValues["UsePolarization"] = _usePolarization;
            return new AnalysisData(
                Name,
                wavefrontValues,
                wavefrontSeries.FirstOrDefault(),
                wavefrontSeries,
                wavefront.PlotOptions,
                wavefront.PlotPanes,
                wavefront.PlotPaneColumns,
                wavefront.Table,
                wavefront.ReportText);
        }

        var workingOptic = AnalysisTrace.PrepareVignettingFactors(Optic, _removeVignetting);
        var fields = AnalysisTrace.ScanFieldSamples(
            workingOptic,
            _scanDirection,
            _fieldDensity + 1);
        IReadOnlyList<Wavelength> wavelengths = AnalysisTrace.SelectWavelengths(workingOptic, _wavelengthNumber);
        if (fields.Count == 0 || wavelengths.Count == 0)
        {
            return RmsScanSupport.Empty(Name);
        }

        var effectiveDistribution = RmsScanSupport.EffectiveDistribution(_method, _distribution);
        var yAxisLabel = RmsScanSupport.AxisLabel(_data);
        var series = wavelengths.Select((wavelength, wavelengthIndex) => new AnalysisSeries(
            AnalysisTrace.FieldAxisLabel(workingOptic),
            yAxisLabel,
            fields.Select(field => new AnalysisPoint(
                field.Coordinate,
                RmsScanSupport.Metric(
                    workingOptic,
                    (field.Hx, field.Hy),
                    new[] { wavelength },
                    _numRings,
                    effectiveDistribution,
                    _data,
                    _reference,
                    usePolarization: _usePolarization,
                    removeVignetting: _removeVignetting),
                Label: field.Label)).ToArray(),
            Name: $"{wavelength.Micrometers:0.0000} \u00B5m",
            ColorIndex: wavelengthIndex,
            XQuantity: AnalysisTrace.FieldAxisQuantity(workingOptic),
            XUnit: AnalysisTrace.FieldAxisUnit(workingOptic),
            YQuantity: RmsScanSupport.MetricQuantity(_data),
            YUnit: RmsScanSupport.MetricUnit(_data))).ToList();
        var diffractionLimit = RmsScanSupport.DiffractionLimitValue(Optic, wavelengths, _data);
        if (_showDiffractionLimit && diffractionLimit > 0)
        {
            series.Add(new AnalysisSeries(
                AnalysisTrace.FieldAxisLabel(workingOptic),
                yAxisLabel,
                fields.Select(field => new AnalysisPoint(field.Coordinate, diffractionLimit, Label: field.Label)).ToArray(),
                Name: "Diffraction Limit",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: series.Count,
                XQuantity: AnalysisTrace.FieldAxisQuantity(workingOptic),
                XUnit: AnalysisTrace.FieldAxisUnit(workingOptic),
                YQuantity: RmsScanSupport.MetricQuantity(_data),
                YUnit: RmsScanSupport.MetricUnit(_data)));
        }

        var seriesArray = series.ToArray();
        var maximum = seriesArray.SelectMany(item => item.Points).Select(point => point.Y).DefaultIfEmpty(0).Max();
        var fieldMetrics = fields.Select(field => (
            Field: field,
            Rms: RmsScanSupport.Metric(
                workingOptic,
                (field.Hx, field.Hy),
                wavelengths,
                _numRings,
                effectiveDistribution,
                _data,
                _reference,
                usePolarization: _usePolarization,
                removeVignetting: _removeVignetting))).ToArray();
        var values = new Dictionary<string, object>
        {
            ["FieldCount"] = fields.Count,
            ["FieldDensity"] = _fieldDensity,
            ["ScanDirection"] = _scanDirection,
            ["WavelengthCount"] = wavelengths.Count,
            ["NumRings"] = _numRings,
            ["Method"] = _method,
            ["Data"] = _data,
            ["Distribution"] = effectiveDistribution,
            ["Reference"] = _reference,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["ShowDiffractionLimit"] = _showDiffractionLimit,
            ["DiffractionLimitMillimeters"] = _data == "spot" ? diffractionLimit : 0,
            ["DiffractionLimitValue"] = diffractionLimit,
            ["DiffractionLimitUnit"] = RmsScanSupport.DiffractionLimitUnit(_data),
            ["UsePolarization"] = _usePolarization,
            ["RemoveVignetting"] = _removeVignetting,
            [RmsScanSupport.MaximumValueKey(_data)] = maximum
        };
        foreach (var item in fieldMetrics)
        {
            values[$"Field {item.Field.Label}"] = item.Rms;
        }

        var includedWeight = fields.Count;
        values["IncludedFieldWeight"] = includedWeight;
        values["WeightedMean"] = includedWeight <= 1e-12
            ? 0
            : fieldMetrics.Sum(item => item.Rms) / includedWeight;
        return new AnalysisData(Name, values, seriesArray.FirstOrDefault(), seriesArray, new AnalysisPlotOptions(
            XMinimum: fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Min(),
            XMaximum: fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Max(),
            YMinimum: 0,
            ShowLegend: true));
    }
}

public sealed class RmsWavefrontVsFieldAnalysis : BaseAnalysis
{
    private readonly int _rayDensity;
    private readonly int _fieldDensity;
    private readonly string _method;
    private readonly string _reference;
    private readonly int _wavelengthNumber;
    private readonly string _scanType;
    private readonly bool _removeVignettingFactors;
    private readonly bool _zemaxCompatibleOutput;

    public RmsWavefrontVsFieldAnalysis(
        Optic optic,
        int numFields = 32,
        int numRings = 12,
        int fieldDensity = 0,
        string method = "GQ",
        string reference = "chief",
        int wavelengthNumber = 0,
        string scanType = "+y",
        bool removeVignettingFactors = true,
        bool zemaxCompatibleOutput = false) : base(optic)
    {
        _rayDensity = Math.Clamp(numRings, 1, 32);
        _fieldDensity = Math.Clamp(fieldDensity > 0 ? fieldDensity : numFields - 1, 1, 200);
        _method = string.Equals(method, "RA", StringComparison.OrdinalIgnoreCase) ? "RA" : "GQ";
        _reference = string.Equals(reference, "centroid", StringComparison.OrdinalIgnoreCase)
            ? "centroid"
            : "chief";
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _scanType = AnalysisTrace.NormalizeScanDirection(scanType);
        _removeVignettingFactors = removeVignettingFactors;
        _zemaxCompatibleOutput = zemaxCompatibleOutput;
    }

    public override string Name => "RMS Wavefront vs Field";

    public override AnalysisData GenerateData()
    {
        var workingOptic = _zemaxCompatibleOutput
            ? AnalysisTrace.PrepareVignettingFactors(Optic, _removeVignettingFactors)
            : Optic;
        var fields = _zemaxCompatibleOutput
            ? AnalysisTrace.ScanFieldSamples(workingOptic, _scanType, _fieldDensity + 1)
            : AnalysisTrace.DefinedFieldSamples(workingOptic);
        var wavelengths = AnalysisTrace.SelectWavelengths(workingOptic, _wavelengthNumber);
        var pupilSamples = _method == "GQ"
            ? ApertureSampler.GenerateGaussianQuadrature(_rayDensity, 6)
            : ApertureSampler.Generate(_rayDensity * _rayDensity, PupilSampling.UniformGrid);
        var pupilCoordinates = pupilSamples.Select(sample => (sample.X, sample.Y)).ToArray();
        var referenceWavelength = _wavelengthNumber == 0
            ? wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary) ?? wavelengths.FirstOrDefault()
            : wavelengths.FirstOrDefault();
        var fieldResults = fields.Select(field =>
        {
            var useSharedReferenceSphere = !workingOptic.ImageSpaceAfocal
                && _zemaxCompatibleOutput
                && _wavelengthNumber == 0
                && wavelengths.Length > 1;
            var wavelengthReferenceSpheres = useSharedReferenceSphere
                ? wavelengths.Select(wavelength =>
                    WavefrontEngine.CreateChiefRayReferenceSphere(
                        workingOptic,
                        (field.Hx, field.Hy),
                        wavelength,
                        aimAtStop: workingOptic.RayAimingEnabled)).ToArray()
                : Array.Empty<WavefrontReferenceSphere>();
            var primaryReferenceSphere = !useSharedReferenceSphere || referenceWavelength is null
                ? null
                : wavelengthReferenceSpheres[Array.IndexOf(wavelengths, referenceWavelength)];
            var monochromaticWavefronts = wavelengths.Select(wavelength => WavefrontEngine.GenerateChiefRaySamples(
                workingOptic,
                (field.Hx, field.Hy),
                wavelength,
                pupilCoordinates,
                aimAtStop: workingOptic.RayAimingEnabled)).ToArray();
            var polychromaticWavefronts = useSharedReferenceSphere
                ? wavelengths.Select((wavelength, wavelengthIndex) => WavefrontEngine.GenerateChiefRaySamples(
                    workingOptic,
                    (field.Hx, field.Hy),
                    wavelength,
                    pupilCoordinates,
                    aimAtStop: workingOptic.RayAimingEnabled,
                    referenceSphere: primaryReferenceSphere is null
                        ? null
                        : new WavefrontReferenceSphere(
                            primaryReferenceSphere.CenterX,
                            primaryReferenceSphere.CenterY,
                            primaryReferenceSphere.CenterZ,
                            wavelengthReferenceSpheres[wavelengthIndex].Radius))).ToArray()
                : monochromaticWavefronts;
            return new
            {
                Monochromatic = monochromaticWavefronts.Select(wavefront =>
                    RmsScanSupport.WeightedWavefrontRms(wavefront.Samples, pupilSamples, _reference)).ToArray(),
                Polychromatic = WeightedPolychromaticWavefrontRms(
                    polychromaticWavefronts,
                    wavelengths,
                    pupilSamples,
                    _reference)
            };
        }).ToArray();
        var wavelengthSeries = wavelengths.Select((wavelength, wavelengthIndex) => new AnalysisSeries(
            _zemaxCompatibleOutput ? ScanAxisLabel(workingOptic, _scanType) : AnalysisTrace.FieldAxisLabel(workingOptic),
            "RMS Wavefront Error (waves)",
            fields.Select((field, fieldIndex) => new AnalysisPoint(
                field.Coordinate,
                fieldResults[fieldIndex].Monochromatic[wavelengthIndex],
                Label: field.Label)).ToArray(),
            Name: $"{wavelength.Micrometers:0.0000} \u00B5m",
            ColorIndex: wavelengthIndex + (_wavelengthNumber == 0 ? 1 : 0),
            XQuantity: AnalysisTrace.FieldAxisQuantity(workingOptic),
            XUnit: AnalysisTrace.FieldAxisUnit(workingOptic),
            YQuantity: AnalysisAxisQuantity.WavefrontError,
            YUnit: AnalysisAxisUnit.Wave)).ToArray();
        var series = new List<AnalysisSeries>();
        if (_zemaxCompatibleOutput && _wavelengthNumber == 0 && wavelengthSeries.Length > 1)
        {
            series.Add(new AnalysisSeries(
                wavelengthSeries[0].XAxisLabel,
                wavelengthSeries[0].YAxisLabel,
                fields.Select((field, index) => new AnalysisPoint(
                    field.Coordinate,
                    fieldResults[index].Polychromatic,
                    Label: field.Label)).ToArray(),
                Name: "Poly",
                ColorIndex: 0,
                XQuantity: wavelengthSeries[0].XQuantity,
                XUnit: wavelengthSeries[0].XUnit,
                YQuantity: wavelengthSeries[0].YQuantity,
                YUnit: wavelengthSeries[0].YUnit));
        }
        series.AddRange(wavelengthSeries);
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["FieldCount"] = fields.Count,
            ["WavelengthCount"] = wavelengths.Length,
            ["RayDensity"] = _rayDensity,
            ["FieldDensity"] = _fieldDensity,
            ["Method"] = _method,
            ["Reference"] = _reference,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["ScanType"] = _scanType,
            ["RemoveVignettingFactors"] = _removeVignettingFactors,
            ["MaximumRmsWavefrontError"] = series.SelectMany(item => item.Points).Select(point => point.Y).DefaultIfEmpty(0).Max()
        }, series.FirstOrDefault(), series.ToArray(), new AnalysisPlotOptions(
            XMinimum: fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Min(),
            XMaximum: fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Max(),
            YMinimum: 0,
            ShowLegend: true,
            GridOpacity: 0.25));
    }

    private static double WeightedPolychromaticWavefrontRms(
        IReadOnlyList<WavefrontResult> wavefronts,
        IReadOnlyList<Wavelength> wavelengths,
        IReadOnlyList<PupilSample> pupil,
        string reference)
    {
        var totalWeight = wavelengths.Sum(wavelength => Math.Max(0, wavelength.Weight));
        if (totalWeight <= 1e-30)
        {
            return 0;
        }

        var meanSquare = wavefronts.Select((wavefront, wavelengthIndex) =>
        {
            var rms = RmsScanSupport.WeightedWavefrontRms(wavefront.Samples, pupil, reference);
            return Math.Max(0, wavelengths[wavelengthIndex].Weight) * rms * rms;
        }).Sum() / totalWeight;
        return Math.Sqrt(Math.Max(0, meanSquare));
    }

    private static string ScanAxisLabel(Optic optic, string scanType)
    {
        var unit = optic.FieldDefinition == FieldDefinitionKind.Angle ? "deg" : "mm";
        return $"{scanType.ToUpperInvariant()} Field ({unit})";
    }
}

public sealed class ZernikeVsFieldAnalysis : BaseAnalysis
{
    private readonly int _fieldDensity;
    private readonly int _numRings;
    private readonly int _requestedNumTerms;
    private readonly int _actualNumTerms;
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
        _requestedNumTerms = numTerms;
        _actualNumTerms = ZernikeFitEngine.ResolveFringeTermCount(numTerms);
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
            return AnalysisData.Unavailable(Name, "No optical data");
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
                    _requestedNumTerms);
                return (Coordinate: coordinate, Coefficients: coefficients);
            })
            .ToArray();
        var axisUnit = Optic.FieldDefinition == FieldDefinitionKind.Angle ? "度" : "毫米";
        var series = Enumerable.Range(1, _actualNumTerms)
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
                LineWidth: 1.2,
                XQuantity: AnalysisTrace.FieldAxisQuantity(Optic),
                XUnit: AnalysisTrace.FieldAxisUnit(Optic),
                YQuantity: AnalysisAxisQuantity.WavefrontError,
                YUnit: AnalysisAxisUnit.Wave))
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
                ["ZernikeTerms"] = _actualNumTerms,
                ["RequestedZernikeTerms"] = _requestedNumTerms,
                ["ActualZernikeTerms"] = _actualNumTerms,
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
            return AnalysisData.Unavailable(Name, "No optical data");
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
        var edgeCoordinate = axis == 0 ? edgeField.X : edgeField.Y;
        var orientation = edgeCoordinate < 0 ? -1.0 : 1.0;
        var rayDefinitions = new[]
        {
            (Pupil: -orientation, Name: "较小光瞳点光线", ColorIndex: 0),
            (Pupil: 0.0, Name: "主光线", ColorIndex: 2),
            (Pupil: orientation, Name: "较大光瞳点光线", ColorIndex: 3)
        };
        var fieldSamples = Enumerable.Range(0, _fieldDensity + 1)
            .Select(index =>
            {
                var fraction = (double)index / _fieldDensity;
                var hx = fieldX * fraction;
                var hy = fieldY * fraction;
                var chief = Optic.TraceGenericSurfaceSample(
                    hx,
                    hy,
                    0,
                    0,
                    wavelength.Micrometers,
                    surfaceIndex,
                    aimAtStop: true);
                var localChiefPosition = chief is null
                    ? default
                    : Optic.SurfaceGroup.Items[surfaceIndex].CoordinateSystem.ToLocalPoint(chief.Position);
                var imageHeight = chief is null
                    ? double.NaN
                    : axis == 0 ? localChiefPosition.X : localChiefPosition.Y;
                return (Hx: hx, Hy: hy, ImageHeight: orientation * Math.Abs(imageHeight));
            })
            .ToArray();

        var series = rayDefinitions.Select(ray =>
        {
            var points = new List<AnalysisPoint>(_fieldDensity + 1);
            foreach (var fieldSample in fieldSamples)
            {
                var px = axis == 0 ? ray.Pupil : 0;
                var py = axis == 1 ? ray.Pupil : 0;
                var sample = Optic.TraceGenericSurfaceSample(
                    fieldSample.Hx,
                    fieldSample.Hy,
                    px,
                    py,
                    wavelength.Micrometers,
                    surfaceIndex,
                    aimAtStop: true);
                if (sample is null)
                {
                    points.Add(new AnalysisPoint(double.NaN, double.NaN));
                    continue;
                }
                var localDirection = Optic.SurfaceGroup.Items[surfaceIndex]
                    .CoordinateSystem
                    .ToLocalDirection(sample.Direction);
                var directionCosine = axis == 0 ? localDirection.X : localDirection.Y;
                var incidentAngle = orientation
                    * Math.Asin(Math.Clamp(directionCosine, -1, 1))
                    * 180
                    / Math.PI;
                points.Add(new AnalysisPoint(fieldSample.ImageHeight, incidentAngle));
            }

            return new AnalysisSeries(
                "像高：毫米",
                "入射角（度）",
                points,
                Name: ray.Name,
                ColorIndex: ray.ColorIndex,
                LineWidth: 1.5,
                XQuantity: AnalysisAxisQuantity.ImageHeight,
                XUnit: AnalysisAxisUnit.Millimeter,
                YQuantity: AnalysisAxisQuantity.IncidentAngle,
                YUnit: AnalysisAxisUnit.Degree);
        }).ToArray();

        var finitePoints = series.SelectMany(item => item.Points)
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
            .ToArray();
        var minimumImageHeight = finitePoints.Select(point => point.X).DefaultIfEmpty(0).Min();
        var maximumImageHeight = finitePoints.Select(point => point.X).DefaultIfEmpty(1).Max();
        if (maximumImageHeight - minimumImageHeight <= 1e-12)
        {
            maximumImageHeight = minimumImageHeight + 1;
        }
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
                ["AimAtStop"] = true,
                ["RayCount"] = series.Length,
                ["PointCountPerRay"] = _fieldDensity + 1
            },
            series[0],
            series,
            new AnalysisPlotOptions(
                Title: "入射角 vs. 像高",
                XMinimum: minimumImageHeight,
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
            return AnalysisData.Unavailable(Name, "No optical data");
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
            var sample = Optic.TraceGenericSurfaceSample(hx, hy, px, py, wavelength.Micrometers, surfaceIndex);
            if (sample is null)
            {
                points.Add(new AnalysisPoint(double.NaN, double.NaN, coordinate.Label, coordinate.Value));
                continue;
            }
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
            ValueLabel: valueLabel,
            XQuantity: AnalysisAxisQuantity.ImageHeight,
            XUnit: AnalysisAxisUnit.Millimeter,
            YQuantity: AnalysisAxisQuantity.IncidentAngle,
            YUnit: AnalysisAxisUnit.Degree,
            ValueQuantity: _mode == AngleScanMode.ThroughPupil
                ? AnalysisAxisQuantity.PupilCoordinate
                : AnalysisTrace.FieldAxisQuantity(Optic),
            ValueUnit: _mode == AngleScanMode.ThroughPupil
                ? AnalysisAxisUnit.Dimensionless
                : AnalysisTrace.FieldAxisUnit(Optic));
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
