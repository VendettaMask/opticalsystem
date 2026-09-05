using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed record ThroughFocusSpotSettings(
    int RayDensity = 6,
    string Pattern = "hexapolar",
    double DefocusStepMicrometers = 50,
    int FocusPlaneCount = 5,
    int WavelengthNumber = 0,
    int FieldNumber = 0,
    int SurfaceNumber = -1,
    string ColorRaysBy = "wavelength",
    string Reference = "centroid",
    bool UsePolarization = false,
    bool ShowAiryDisk = false,
    string DisplayScale = "scale-bar",
    double PlotScaleMicrometers = 0,
    bool ScatterRays = false,
    bool UseSymbols = true);

public sealed class ThroughFocusAnalysis : BaseAnalysis
{
    private readonly ThroughFocusSpotSettings _settings;

    public ThroughFocusAnalysis(
        Optic optic,
        double deltaFocus = 0.1,
        int numSteps = 5,
        int numRings = 6,
        string distribution = "hexapolar") : base(optic)
    {
        if (numSteps < 1 || numSteps > 7 || numSteps % 2 == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numSteps), "Through-focus steps must be an odd integer from 1 through 7.");
        }

        _settings = new ThroughFocusSpotSettings(
            RayDensity: Math.Max(1, numRings),
            Pattern: distribution,
            DefocusStepMicrometers: deltaFocus * 1000,
            FocusPlaneCount: numSteps);
    }

    public ThroughFocusAnalysis(Optic optic, ThroughFocusSpotSettings settings) : base(optic)
    {
        var stepCount = Math.Clamp(settings.FocusPlaneCount, 1, 7);
        if (stepCount % 2 == 0)
        {
            stepCount = Math.Min(7, stepCount + 1);
        }

        _settings = settings with
        {
            RayDensity = Math.Clamp(settings.RayDensity, 1, 32),
            DefocusStepMicrometers = double.IsFinite(settings.DefocusStepMicrometers)
                ? Math.Max(0, settings.DefocusStepMicrometers)
                : 50,
            FocusPlaneCount = stepCount,
            PlotScaleMicrometers = double.IsFinite(settings.PlotScaleMicrometers)
                ? Math.Max(0, settings.PlotScaleMicrometers)
                : 0
        };
    }

    public override string Name => "Through Focus";

    public override AnalysisData GenerateData()
    {
        var analysisOptic = ResolveAnalysisOptic();
        var allFields = SpotAnalysisEngine.DefinedFields(analysisOptic);
        var fieldIndices = _settings.FieldNumber <= 0
            ? Enumerable.Range(0, allFields.Count).ToArray()
            : new[]
            {
                Math.Clamp(_settings.FieldNumber - 1, 0, Math.Max(0, allFields.Count - 1))
            };
        var fields = fieldIndices.Select(index => allFields[index]).ToArray();
        var wavelengths = AnalysisTrace.SelectWavelengths(
            analysisOptic,
            _settings.WavelengthNumber);
        var imageSpace = ImageSpaceAnalysisSupport.CoordinateDescriptor(
            analysisOptic,
            _settings.SurfaceNumber);
        var deltaFocus = _settings.DefocusStepMicrometers / 1000.0;
        var sweep = FocusSweepEvaluator.Evaluate(
            analysisOptic,
            fields,
            wavelengths,
            _settings.FocusPlaneCount,
            deltaFocus,
            _settings.RayDensity,
            _settings.Pattern,
            _settings.SurfaceNumber,
            _settings.Reference,
            _settings.UsePolarization,
            Name);
        var offsets = sweep.Offsets;
        var results = sweep.SpotResults;
        var axisLimit = results
            .SelectMany(result => result.Fields)
            .SelectMany(field => field.Wavelengths)
            .SelectMany(wavelength => wavelength.Rays)
            .Select(ray => Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y)))
            .DefaultIfEmpty(0.01)
            .Max() * 1.05;
        axisLimit = axisLimit <= 1e-12 ? 0.01 : axisLimit;
        var airyRadius = AiryDiskSupport.CalculateRadius(analysisOptic, fields, wavelengths, _settings.ShowAiryDisk);
        axisLimit = Math.Max(axisLimit, airyRadius * 1.05);
        if (_settings.PlotScaleMicrometers > 0)
        {
            axisLimit = imageSpace.IsAfocalAngle
                ? _settings.PlotScaleMicrometers
                : _settings.PlotScaleMicrometers / 1000.0;
        }

        var panes = new List<AnalysisPlotPane>(_settings.FocusPlaneCount * fields.Length);
        for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            for (var stepIndex = 0; stepIndex < _settings.FocusPlaneCount; stepIndex++)
            {
                var field = fields[fieldIndex];
                var fieldTitle = MtfPresentation.FieldName(analysisOptic, field);
                var title = fieldIndex == 0
                    ? $"Defocus: {offsets[stepIndex]:+0.000;-0.000;+0.000} {ImageSpaceAnalysisSupport.DefocusUnitLabel(analysisOptic)}\n{fieldTitle}"
                    : fieldTitle;
                var series = results[stepIndex].Fields[fieldIndex].Wavelengths
                    .Select((wavelength, wavelengthIndex) => new AnalysisSeries(
                        fieldIndex == fields.Length - 1 ? $"X ({imageSpace.AxisUnitLabel})" : "",
                        stepIndex == 0 ? $"Y ({imageSpace.AxisUnitLabel})" : "",
                        wavelength.Rays.Select(ray => new AnalysisPoint(ray.X, ray.Y)).ToArray(),
                        AnalysisSeriesKind.Scatter,
                        $"{wavelength.Wavelength.Micrometers:0.0000} \u00B5m",
                        ColorIndex: IsColorByField() ? fieldIndex : wavelengthIndex,
                        MarkerStyle: _settings.UseSymbols
                            ? (AnalysisMarkerStyle)(wavelengthIndex % 4)
                            : AnalysisMarkerStyle.Circle,
                        MarkerSize: _settings.UseSymbols ? 2.8 : 2.2,
                        Opacity: 0.7,
                        LegendKey: IsColorByField()
                            ? $"field:{fieldIndex}"
                            : $"wavelength:{wavelength.Wavelength.Micrometers:R}",
                        XQuantity: imageSpace.Quantity,
                        XUnit: imageSpace.Unit,
                        YQuantity: imageSpace.Quantity,
                        YUnit: imageSpace.Unit)).ToList();
                if (_settings.ShowAiryDisk && airyRadius > 0)
                {
                    series.Add(AiryDiskSupport.CreateSeries(airyRadius, imageSpace));
                }

                var fieldRays = results[stepIndex].Fields[fieldIndex].WeightedRays
                    .ToArray();
                var metrics = stepIndex == _settings.FocusPlaneCount / 2
                    ? new[]
                    {
                        new AnalysisPlotMetric(
                            "RMS 半径",
                            SpotAnalysisEngine.RmsRadius(fieldRays) * imageSpace.MetricScale,
                            imageSpace.MetricUnitLabel),
                        new AnalysisPlotMetric(
                            "GEO 半径",
                            fieldRays
                                .Select(ray => Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y)))
                                .DefaultIfEmpty(0)
                                .Max() * imageSpace.MetricScale,
                            imageSpace.MetricUnitLabel)
                    }
                    : null;
                panes.Add(new AnalysisPlotPane(title, series, new AnalysisPlotOptions(
                    Title: title,
                    EqualAspect: true,
                    XMinimum: -axisLimit,
                    XMaximum: axisLimit,
                    YMinimum: -axisLimit,
                    YMaximum: axisLimit,
                    GridOpacity: 0.25,
                    HideTickLabels: true),
                    metrics,
                    Footer: fieldTitle));
            }
        }

        var points = sweep.Points;
        var best = sweep.Best;
        var metricSeries = new AnalysisSeries(
            ImageSpaceAnalysisSupport.DefocusLabel(analysisOptic),
            $"RMS spot radius ({imageSpace.AxisUnitLabel})",
            points.Select(point => new AnalysisPoint(point.FocusShift, point.RmsSpotRadius)).ToArray(),
            XQuantity: AnalysisAxisQuantity.Defocus,
            XUnit: ImageSpaceAnalysisSupport.DefocusUnit(analysisOptic),
            YQuantity: AnalysisAxisQuantity.Radius,
            YUnit: imageSpace.Unit);
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["FocusStep"] = deltaFocus,
            ["FocusStepDiopters"] = imageSpace.IsAfocalAngle ? deltaFocus : 0,
            ["DefocusStepMicrometers"] = _settings.DefocusStepMicrometers,
            ["DefocusStepDiopters"] = imageSpace.IsAfocalAngle ? deltaFocus : 0,
            ["DefocusUnit"] = ImageSpaceAnalysisSupport.DefocusUnitLabel(analysisOptic),
            ["FocusPlaneCount"] = _settings.FocusPlaneCount,
            ["NumRings"] = _settings.RayDensity,
            ["RayDensity"] = _settings.RayDensity,
            ["Distribution"] = _settings.Pattern,
            ["Pattern"] = _settings.Pattern,
            ["WavelengthNumber"] = _settings.WavelengthNumber,
            ["FieldNumber"] = _settings.FieldNumber,
            ["SurfaceNumber"] = _settings.SurfaceNumber,
            ["ColorRaysBy"] = _settings.ColorRaysBy,
            ["Reference"] = _settings.Reference,
            ["UsePolarization"] = _settings.UsePolarization,
            ["ShowAiryDisk"] = _settings.ShowAiryDisk,
            ["AiryRadius"] = airyRadius,
            ["ImageSpaceAfocal"] = imageSpace.IsAfocalAngle,
            ["ImageCoordinateUnit"] = imageSpace.AxisUnitLabel,
            ["DisplayScale"] = _settings.DisplayScale,
            ["PlotScaleMicrometers"] = _settings.PlotScaleMicrometers,
            ["PlotScaleMilliradians"] = imageSpace.IsAfocalAngle ? _settings.PlotScaleMicrometers : 0,
            ["ScaleBarMicrometers"] = imageSpace.IsAfocalAngle ? 0 : axisLimit * 2 * 1000,
            ["ScaleBarMilliradians"] = imageSpace.IsAfocalAngle ? axisLimit * 2 : 0,
            ["ScatterRays"] = _settings.ScatterRays,
            ["UseSymbols"] = _settings.UseSymbols,
            ["Minus2StepRms"] = points.ElementAtOrDefault(0)?.RmsSpotRadius ?? 0,
            ["Minus1StepRms"] = points.ElementAtOrDefault(1)?.RmsSpotRadius ?? 0,
            ["NominalRms"] = points.ElementAtOrDefault(2)?.RmsSpotRadius ?? 0,
            ["Plus1StepRms"] = points.ElementAtOrDefault(3)?.RmsSpotRadius ?? 0,
            ["Plus2StepRms"] = points.ElementAtOrDefault(4)?.RmsSpotRadius ?? 0,
            ["BestFocusShift"] = best.FocusShift,
            ["BestRmsSpotRadius"] = best.RmsSpotRadius,
            ["Radius80AtBest"] = points.OrderBy(point => point.RmsSpotRadius).FirstOrDefault()?.Radius80 ?? 0
        }, metricSeries, new[] { metricSeries }, PlotPanes: panes, PlotPaneColumns: _settings.FocusPlaneCount);
    }

    private bool IsColorByField()
    {
        return string.Equals(_settings.ColorRaysBy, "field", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_settings.ColorRaysBy, "视场", StringComparison.Ordinal);
    }

    private Optic ResolveAnalysisOptic()
    {
        if (_settings.ScatterRays
            || Optic.SurfaceGroup.Items.All(surface => surface.ScatteringModel is null))
        {
            return Optic;
        }

        var clone = Optic.FromSnapshot(Optic.ToSnapshot());
        foreach (var surface in clone.SurfaceGroup.Items)
        {
            surface.ScatteringModel = null;
        }

        return clone;
    }

}

public sealed class ThroughFocusMtfAnalysis : BaseAnalysis
{
    private readonly double _spatialFrequency;
    private readonly double _deltaFocus;
    private readonly int _numSteps;
    private readonly int _pupilSampling;

    public ThroughFocusMtfAnalysis(
        Optic optic,
        double spatialFrequency = 20,
        double deltaFocus = 0.1,
        int numSteps = 5,
        int pupilSampling = 128) : base(optic)
    {
        _spatialFrequency = Math.Max(0, spatialFrequency);
        _deltaFocus = Math.Abs(deltaFocus);
        _numSteps = Math.Clamp(numSteps % 2 == 0 ? numSteps + 1 : numSteps, 1, 15);
        _pupilSampling = Math.Max(8, pupilSampling);
    }

    public override string Name => "Through Focus MTF";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        var imageSurface = Optic.SurfaceGroup.Items.LastOrDefault();
        if (wavelength is null || imageSurface is null)
        {
            return AnalysisData.Unavailable(Name, "No optical data");
        }

        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var defocus = Enumerable.Range(0, _numSteps)
            .Select(index => (index - (_numSteps / 2)) * _deltaFocus)
            .ToArray();
        var tangential = fields.Select(_ => new double[_numSteps]).ToArray();
        var sagittal = fields.Select(_ => new double[_numSteps]).ToArray();
        var originalCoordinateSystem = imageSurface.CoordinateSystem;
        if (Optic.ImageSpaceAfocal)
        {
            for (var step = 0; step < _numSteps; step++)
            {
                for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                {
                    tangential[fieldIndex][step] = SampledMtfEngine.Calculate(
                        Optic,
                        fields[fieldIndex],
                        wavelength,
                        0,
                        _spatialFrequency,
                        _pupilSampling,
                        defocus: defocus[step]);
                    sagittal[fieldIndex][step] = SampledMtfEngine.Calculate(
                        Optic,
                        fields[fieldIndex],
                        wavelength,
                        _spatialFrequency,
                        0,
                        _pupilSampling,
                        defocus: defocus[step]);
                }
            }
        }
        else
        {
            try
            {
                for (var step = 0; step < _numSteps; step++)
                {
                    imageSurface.CoordinateSystem = originalCoordinateSystem with
                    {
                        Origin = originalCoordinateSystem.Origin with
                        {
                            Z = originalCoordinateSystem.Origin.Z + defocus[step]
                        }
                    };
                    for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                    {
                        tangential[fieldIndex][step] = SampledMtfEngine.Calculate(
                            Optic, fields[fieldIndex], wavelength, 0, _spatialFrequency, _pupilSampling);
                        sagittal[fieldIndex][step] = SampledMtfEngine.Calculate(
                            Optic, fields[fieldIndex], wavelength, _spatialFrequency, 0, _pupilSampling);
                    }
                }
            }
            finally
            {
                imageSurface.CoordinateSystem = originalCoordinateSystem;
            }
        }

        var smoothDefocus = _numSteps < 2
            ? defocus
            : Enumerable.Range(0, 256)
                .Select(index => defocus[0] + ((defocus[^1] - defocus[0]) * index / 255.0))
                .ToArray();
        var series = new List<AnalysisSeries>(fields.Count * 2);
        var defocusLabel = ImageSpaceAnalysisSupport.DefocusLabel(Optic);
        var defocusUnit = ImageSpaceAnalysisSupport.DefocusUnit(Optic);
        var frequencyUnitLabel = ImageSpaceAnalysisSupport.SpatialFrequencyUnitLabel(Optic);
        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var tangentialSmooth = Interpolate(defocus, tangential[fieldIndex], smoothDefocus);
            var sagittalSmooth = Interpolate(defocus, sagittal[fieldIndex], smoothDefocus);
            series.Add(new AnalysisSeries(
                defocusLabel,
                "MTF",
                smoothDefocus.Select((x, index) => new AnalysisPoint(x, Math.Clamp(tangentialSmooth[index], 0, 1))).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, field, "Tangential"),
                ColorIndex: fieldIndex,
                XQuantity: AnalysisAxisQuantity.Defocus,
                XUnit: defocusUnit,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: AnalysisAxisUnit.Dimensionless));
            series.Add(new AnalysisSeries(
                defocusLabel,
                "MTF",
                smoothDefocus.Select((x, index) => new AnalysisPoint(x, Math.Clamp(sagittalSmooth[index], 0, 1))).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, field, "Sagittal"),
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: fieldIndex,
                XQuantity: AnalysisAxisQuantity.Defocus,
                XUnit: defocusUnit,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: AnalysisAxisUnit.Dimensionless));
        }

        var values = new Dictionary<string, object>
        {
            ["SpatialFrequency"] = _spatialFrequency,
            ["FrequencyUnit"] = frequencyUnitLabel,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["FocusPlaneCount"] = _numSteps,
            ["FocusStep"] = _deltaFocus,
            ["FocusStepDiopters"] = Optic.ImageSpaceAfocal ? _deltaFocus : 0,
            ["DefocusUnit"] = ImageSpaceAnalysisSupport.DefocusUnitLabel(Optic),
            ["ImageSpaceAfocal"] = Optic.ImageSpaceAfocal,
            ["PupilSampling"] = _pupilSampling,
            ["RawTangential"] = tangential,
            ["RawSagittal"] = sagittal
        };
        return new AnalysisData(Name, values, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            Title: $"Through-Focus MTF at {_spatialFrequency:0.###} {frequencyUnitLabel}, \u03BB={wavelength.Micrometers:0.000} \u00B5m",
            XMinimum: defocus[0],
            XMaximum: defocus[^1],
            YMinimum: 0,
            YMaximum: 1.05,
            ShowLegend: true,
            DottedGrid: true,
            GridOpacity: 0.5));
    }

    internal static double[] Interpolate(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double> target)
    {
        if (x.Count == 1)
        {
            return target.Select(_ => y[0]).ToArray();
        }

        if (x.Count < 4)
        {
            return target.Select(value => LinearInterpolate(x, y, value)).ToArray();
        }

        var intervals = x.Count - 1;
        var unknowns = intervals * 4;
        var matrix = new double[unknowns, unknowns];
        var rightHandSide = new double[unknowns];
        var equation = 0;
        for (var interval = 0; interval < intervals; interval++)
        {
            var h = x[interval + 1] - x[interval];
            matrix[equation, (interval * 4)] = 1;
            rightHandSide[equation++] = y[interval];
            matrix[equation, (interval * 4)] = 1;
            matrix[equation, (interval * 4) + 1] = h;
            matrix[equation, (interval * 4) + 2] = h * h;
            matrix[equation, (interval * 4) + 3] = h * h * h;
            rightHandSide[equation++] = y[interval + 1];
        }

        for (var knot = 1; knot < x.Count - 1; knot++)
        {
            var leftInterval = knot - 1;
            var h = x[knot] - x[knot - 1];
            matrix[equation, (leftInterval * 4) + 1] = 1;
            matrix[equation, (leftInterval * 4) + 2] = 2 * h;
            matrix[equation, (leftInterval * 4) + 3] = 3 * h * h;
            matrix[equation, (knot * 4) + 1] = -1;
            equation++;
            matrix[equation, (leftInterval * 4) + 2] = 2;
            matrix[equation, (leftInterval * 4) + 3] = 6 * h;
            matrix[equation, (knot * 4) + 2] = -2;
            equation++;
        }

        matrix[equation, 3] = 1;
        matrix[equation++, 7] = -1;
        matrix[equation, ((intervals - 2) * 4) + 3] = 1;
        matrix[equation, ((intervals - 1) * 4) + 3] = -1;
        var coefficients = Solve(matrix, rightHandSide);
        return target.Select(value =>
        {
            var interval = Math.Clamp(x.ToList().FindLastIndex(item => item <= value), 0, intervals - 1);
            var t = value - x[interval];
            return coefficients[interval * 4]
                + (coefficients[(interval * 4) + 1] * t)
                + (coefficients[(interval * 4) + 2] * t * t)
                + (coefficients[(interval * 4) + 3] * t * t * t);
        }).ToArray();
    }

    private static double LinearInterpolate(IReadOnlyList<double> x, IReadOnlyList<double> y, double value)
    {
        var interval = Math.Clamp(x.ToList().FindLastIndex(item => item <= value), 0, x.Count - 2);
        var fraction = (value - x[interval]) / (x[interval + 1] - x[interval]);
        return y[interval] + ((y[interval + 1] - y[interval]) * fraction);
    }

    private static double[] Solve(double[,] matrix, double[] rightHandSide)
    {
        var size = rightHandSide.Length;
        var augmented = new double[size, size + 1];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                augmented[row, column] = matrix[row, column];
            }

            augmented[row, size] = rightHandSide[row];
        }

        for (var pivot = 0; pivot < size; pivot++)
        {
            var best = Enumerable.Range(pivot, size - pivot).MaxBy(row => Math.Abs(augmented[row, pivot]));
            for (var column = pivot; column <= size; column++)
            {
                (augmented[pivot, column], augmented[best, column]) = (augmented[best, column], augmented[pivot, column]);
            }

            var divisor = augmented[pivot, pivot];
            for (var column = pivot; column <= size; column++)
            {
                augmented[pivot, column] /= divisor;
            }

            for (var row = 0; row < size; row++)
            {
                if (row == pivot)
                {
                    continue;
                }

                var factor = augmented[row, pivot];
                for (var column = pivot; column <= size; column++)
                {
                    augmented[row, column] -= factor * augmented[pivot, column];
                }
            }
        }

        return Enumerable.Range(0, size).Select(row => augmented[row, size]).ToArray();
    }
}
