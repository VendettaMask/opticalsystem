using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Analysis;

public sealed class RmsVsWavelengthAnalysis : BaseAnalysis
{
    private readonly int _waveDensity;
    private readonly int _numRings;
    private readonly string _distribution;
    private readonly int _fieldNumber;
    private readonly string _reference;
    private readonly string _method;
    private readonly string _data;
    private readonly bool _showDiffractionLimit;
    private readonly bool _usePolarization;
    private readonly bool _removeVignetting;

    public RmsVsWavelengthAnalysis(
        Optic optic,
        int waveDensity = 21,
        int numRings = 6,
        string distribution = "hexapolar",
        int fieldNumber = 0,
        string reference = "centroid",
        string method = "GQ",
        string data = "spot",
        bool showDiffractionLimit = false,
        bool usePolarization = false,
        bool removeVignetting = true) : base(optic)
    {
        _waveDensity = Math.Clamp(waveDensity, 2, 100);
        _numRings = Math.Clamp(numRings, 1, 32);
        _distribution = distribution;
        _fieldNumber = Math.Max(0, fieldNumber);
        _reference = RmsScanSupport.NormalizeReference(reference);
        _method = RmsScanSupport.NormalizeMethod(method);
        _data = RmsScanSupport.NormalizeData(data);
        _showDiffractionLimit = showDiffractionLimit;
        _usePolarization = usePolarization;
        _removeVignetting = removeVignetting;
    }

    public override string Name => "RMS vs Wavelength";

    public override AnalysisData GenerateData()
    {
        var definedWavelengths = Optic.Wavelengths.ToArray();
        var fields = RmsScanSupport.SelectedFields(Optic, _fieldNumber);
        if (definedWavelengths.Length == 0 || fields.Count == 0)
        {
            return RmsScanSupport.Empty(Name);
        }

        var minimum = definedWavelengths.Min(wavelength => wavelength.Micrometers);
        var maximum = definedWavelengths.Max(wavelength => wavelength.Micrometers);
        var wavelengths = Enumerable.Range(0, _waveDensity)
            .Select(index => new Wavelength
            {
                Label = $"W{index + 1}",
                Micrometers = minimum + ((maximum - minimum) * index / (_waveDensity - 1.0)),
                Weight = 1,
                IsPrimary = true
            })
            .ToArray();
        var effectiveDistribution = RmsScanSupport.EffectiveDistribution(_method, _distribution);
        var yAxisLabel = RmsScanSupport.AxisLabel(_data);
        var series = fields.Select((field, fieldIndex) => new AnalysisSeries(
            "Wavelength (\u00B5m)",
            yAxisLabel,
            wavelengths.Select(wavelength => new AnalysisPoint(
                wavelength.Micrometers,
                RmsScanSupport.Metric(
                    Optic,
                    (field.Hx, field.Hy),
                    new[] { wavelength },
                    _numRings,
                    effectiveDistribution,
                    _data,
                    _reference,
                    usePolarization: _usePolarization,
                    removeVignetting: _removeVignetting))).ToArray(),
            Name: field.Label,
            ColorIndex: fieldIndex,
            XQuantity: AnalysisAxisQuantity.Wavelength,
            XUnit: AnalysisAxisUnit.Micrometer,
            YQuantity: RmsScanSupport.MetricQuantity(_data),
            YUnit: RmsScanSupport.MetricUnit(_data))).ToList();
        var diffractionLimit = RmsScanSupport.DiffractionLimitValue(Optic, wavelengths, _data);
        if (_showDiffractionLimit && diffractionLimit > 0)
        {
            series.Add(new AnalysisSeries(
                "Wavelength (\u00B5m)",
                yAxisLabel,
                wavelengths.Select(wavelength => new AnalysisPoint(
                    wavelength.Micrometers,
                    RmsScanSupport.DiffractionLimitValue(Optic, new[] { wavelength }, _data))).ToArray(),
                Name: "Diffraction Limit",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: series.Count,
                XQuantity: AnalysisAxisQuantity.Wavelength,
                XUnit: AnalysisAxisUnit.Micrometer,
                YQuantity: RmsScanSupport.MetricQuantity(_data),
                YUnit: RmsScanSupport.MetricUnit(_data)));
        }

        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["WaveDensity"] = _waveDensity,
                ["RayDensity"] = _numRings,
                ["Method"] = _method,
                ["Data"] = _data,
                ["Distribution"] = effectiveDistribution,
                ["FieldNumber"] = _fieldNumber,
                ["Reference"] = _reference,
                ["ShowDiffractionLimit"] = _showDiffractionLimit,
                ["DiffractionLimitMillimeters"] = _data == "spot" ? diffractionLimit : 0,
                ["DiffractionLimitValue"] = diffractionLimit,
                ["DiffractionLimitUnit"] = RmsScanSupport.DiffractionLimitUnit(_data),
                ["UsePolarization"] = _usePolarization,
                ["RemoveVignetting"] = _removeVignetting,
                ["MinimumWavelengthMicrometers"] = minimum,
                ["MaximumWavelengthMicrometers"] = maximum
            },
            series.FirstOrDefault(),
            series,
            new AnalysisPlotOptions(
                Title: "RMS vs. Wavelength",
                XMinimum: minimum,
                XMaximum: maximum,
                YMinimum: 0,
                ShowLegend: true,
                HideTopAndRightAxes: true,
                GridOpacity: 0.25,
                LegendBelow: true));
    }
}

public sealed class RmsVsFocusAnalysis : BaseAnalysis
{
    private readonly int _focusDensity;
    private readonly double _minimumFocus;
    private readonly double _maximumFocus;
    private readonly int _numRings;
    private readonly string _distribution;
    private readonly int _wavelengthNumber;
    private readonly string _reference;
    private readonly string _method;
    private readonly string _data;
    private readonly bool _showDiffractionLimit;
    private readonly bool _usePolarization;
    private readonly bool _removeVignetting;

    public RmsVsFocusAnalysis(
        Optic optic,
        int focusDensity = 21,
        double minimumFocus = -1,
        double maximumFocus = 1,
        int numRings = 6,
        string distribution = "hexapolar",
        int wavelengthNumber = 0,
        string reference = "centroid",
        string method = "GQ",
        string data = "spot",
        bool showDiffractionLimit = false,
        bool usePolarization = false,
        bool removeVignetting = true) : base(optic)
    {
        _focusDensity = Math.Clamp(focusDensity, 2, 100);
        _minimumFocus = Math.Min(minimumFocus, maximumFocus);
        _maximumFocus = Math.Max(minimumFocus, maximumFocus);
        _numRings = Math.Clamp(numRings, 1, 32);
        _distribution = distribution;
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _reference = RmsScanSupport.NormalizeReference(reference);
        _method = RmsScanSupport.NormalizeMethod(method);
        _data = RmsScanSupport.NormalizeData(data);
        _showDiffractionLimit = showDiffractionLimit;
        _usePolarization = usePolarization;
        _removeVignetting = removeVignetting;
    }

    public override string Name => "RMS vs Focus";

    public override AnalysisData GenerateData()
    {
        var fields = AnalysisTrace.DefinedFieldSamples(Optic);
        var wavelengths = RmsScanSupport.SelectedWavelengths(Optic, _wavelengthNumber);
        if (fields.Count == 0 || wavelengths.Count == 0)
        {
            return RmsScanSupport.Empty(Name);
        }

        var focusValues = Enumerable.Range(0, _focusDensity)
            .Select(index => _minimumFocus
                + ((_maximumFocus - _minimumFocus) * index / (_focusDensity - 1.0)))
            .ToArray();
        var effectiveDistribution = RmsScanSupport.EffectiveDistribution(_method, _distribution);
        var yAxisLabel = RmsScanSupport.AxisLabel(_data);
        var series = fields.Select((field, fieldIndex) => new AnalysisSeries(
            "Focus Shift (mm)",
            yAxisLabel,
            focusValues.Select(focus => new AnalysisPoint(
                focus,
                RmsScanSupport.Metric(
                    Optic,
                    (field.Hx, field.Hy),
                    wavelengths,
                    _numRings,
                    effectiveDistribution,
                    _data,
                    _reference,
                    focus,
                    _usePolarization,
                    _removeVignetting))).ToArray(),
            Name: field.Label,
            ColorIndex: fieldIndex,
            XQuantity: AnalysisAxisQuantity.Defocus,
            XUnit: AnalysisAxisUnit.Millimeter,
            YQuantity: RmsScanSupport.MetricQuantity(_data),
            YUnit: RmsScanSupport.MetricUnit(_data))).ToList();
        var diffractionLimit = RmsScanSupport.DiffractionLimitValue(Optic, wavelengths, _data);
        if (_showDiffractionLimit && diffractionLimit > 0)
        {
            series.Add(new AnalysisSeries(
                "Focus Shift (mm)",
                yAxisLabel,
                focusValues.Select(focus => new AnalysisPoint(focus, diffractionLimit)).ToArray(),
                Name: "Diffraction Limit",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: series.Count,
                XQuantity: AnalysisAxisQuantity.Defocus,
                XUnit: AnalysisAxisUnit.Millimeter,
                YQuantity: RmsScanSupport.MetricQuantity(_data),
                YUnit: RmsScanSupport.MetricUnit(_data)));
        }

        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["FocusDensity"] = _focusDensity,
                ["MinimumFocus"] = _minimumFocus,
                ["MaximumFocus"] = _maximumFocus,
                ["RayDensity"] = _numRings,
                ["Method"] = _method,
                ["Data"] = _data,
                ["Distribution"] = effectiveDistribution,
                ["WavelengthNumber"] = _wavelengthNumber,
                ["Reference"] = _reference,
                ["ShowDiffractionLimit"] = _showDiffractionLimit,
                ["DiffractionLimitMillimeters"] = _data == "spot" ? diffractionLimit : 0,
                ["DiffractionLimitValue"] = diffractionLimit,
                ["DiffractionLimitUnit"] = RmsScanSupport.DiffractionLimitUnit(_data),
                ["UsePolarization"] = _usePolarization,
                ["RemoveVignetting"] = _removeVignetting
            },
            series.FirstOrDefault(),
            series,
            new AnalysisPlotOptions(
                Title: "RMS vs. Focus",
                XMinimum: _minimumFocus,
                XMaximum: _maximumFocus,
                YMinimum: 0,
                ShowLegend: true,
                HideTopAndRightAxes: true,
                GridOpacity: 0.25,
                LegendBelow: true));
    }
}

public sealed class RmsFieldMapAnalysis : BaseAnalysis
{
    private readonly int _xFieldSamples;
    private readonly int _yFieldSamples;
    private readonly double _xFieldWidth;
    private readonly double _yFieldWidth;
    private readonly int _numRings;
    private readonly string _distribution;
    private readonly int _wavelengthNumber;
    private readonly string _reference;
    private readonly string _method;
    private readonly string _data;
    private readonly bool _showDiffractionLimit;
    private readonly bool _usePolarization;
    private readonly bool _removeVignetting;

    public RmsFieldMapAnalysis(
        Optic optic,
        int xFieldSamples = 11,
        int yFieldSamples = 11,
        double xFieldWidth = 0,
        double yFieldWidth = 0,
        int numRings = 6,
        string distribution = "hexapolar",
        int wavelengthNumber = 0,
        string reference = "centroid",
        string method = "GQ",
        string data = "spot",
        bool showDiffractionLimit = false,
        bool usePolarization = false,
        bool removeVignetting = true) : base(optic)
    {
        var defaultWidth = Math.Max(1e-9, AnalysisTrace.MaxFieldValue(optic));
        _xFieldSamples = Math.Clamp(xFieldSamples, 3, 101);
        _yFieldSamples = Math.Clamp(yFieldSamples, 3, 101);
        _xFieldWidth = xFieldWidth > 0 ? xFieldWidth : defaultWidth;
        _yFieldWidth = yFieldWidth > 0 ? yFieldWidth : defaultWidth;
        _numRings = Math.Clamp(numRings, 1, 32);
        _distribution = distribution;
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _reference = RmsScanSupport.NormalizeReference(reference);
        _method = RmsScanSupport.NormalizeMethod(method);
        _data = RmsScanSupport.NormalizeData(data);
        _showDiffractionLimit = showDiffractionLimit;
        _usePolarization = usePolarization;
        _removeVignetting = removeVignetting;
    }

    public override string Name => "RMS Field Map";

    public override AnalysisData GenerateData()
    {
        var wavelengths = RmsScanSupport.SelectedWavelengths(Optic, _wavelengthNumber);
        var maximumField = Math.Max(1e-12, AnalysisTrace.MaxFieldValue(Optic));
        if (wavelengths.Count == 0)
        {
            return RmsScanSupport.Empty(Name);
        }

        var points = new List<AnalysisPoint>(_xFieldSamples * _yFieldSamples);
        for (var row = 0; row < _yFieldSamples; row++)
        {
            var y = -_yFieldWidth + (2 * _yFieldWidth * row / (_yFieldSamples - 1.0));
            for (var column = 0; column < _xFieldSamples; column++)
            {
                var x = -_xFieldWidth + (2 * _xFieldWidth * column / (_xFieldSamples - 1.0));
                var effectiveDistribution = RmsScanSupport.EffectiveDistribution(_method, _distribution);
                var rms = RmsScanSupport.Metric(
                    Optic,
                    (x / maximumField, y / maximumField),
                    wavelengths,
                    _numRings,
                    effectiveDistribution,
                    _data,
                    _reference,
                    usePolarization: _usePolarization,
                    removeVignetting: _removeVignetting);
                points.Add(new AnalysisPoint(x, y, Value: rms));
            }
        }

        var values = points.Select(point => point.Value ?? 0).ToArray();
        var series = new AnalysisSeries(
            RmsScanSupport.FieldXAxisLabel(Optic),
            RmsScanSupport.FieldYAxisLabel(Optic),
            points,
            AnalysisSeriesKind.Heatmap,
            Name: RmsScanSupport.SeriesName(_data),
            ValueLabel: RmsScanSupport.AxisLabel(_data),
            ValueMinimum: values.DefaultIfEmpty(0).Min(),
            ValueMaximum: values.DefaultIfEmpty(0).Max(),
            XQuantity: AnalysisTrace.FieldAxisQuantity(Optic),
            XUnit: AnalysisTrace.FieldAxisUnit(Optic),
            YQuantity: AnalysisTrace.FieldAxisQuantity(Optic),
            YUnit: AnalysisTrace.FieldAxisUnit(Optic),
            ValueQuantity: RmsScanSupport.MetricQuantity(_data),
            ValueUnit: RmsScanSupport.MetricUnit(_data));
        var distribution = RmsScanSupport.EffectiveDistribution(_method, _distribution);
        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["XFieldSamples"] = _xFieldSamples,
                ["YFieldSamples"] = _yFieldSamples,
                ["XFieldWidth"] = _xFieldWidth,
                ["YFieldWidth"] = _yFieldWidth,
                ["RayDensity"] = _numRings,
                ["Method"] = _method,
                ["Data"] = _data,
                ["Distribution"] = distribution,
                ["WavelengthNumber"] = _wavelengthNumber,
                ["Reference"] = _reference,
                ["ShowDiffractionLimit"] = _showDiffractionLimit,
                ["DiffractionLimitMillimeters"] = RmsScanSupport.DiffractionLimitMillimeters(Optic, wavelengths),
                ["UsePolarization"] = _usePolarization,
                ["RemoveVignetting"] = _removeVignetting,
                ["MinimumRmsSpotRadius"] = values.DefaultIfEmpty(0).Min(),
                ["MaximumRmsSpotRadius"] = values.DefaultIfEmpty(0).Max()
            },
            series,
            new[] { series },
            new AnalysisPlotOptions(
                Title: "RMS Field Map",
                EqualAspect: true,
                XMinimum: -_xFieldWidth,
                XMaximum: _xFieldWidth,
                YMinimum: -_yFieldWidth,
                YMaximum: _yFieldWidth,
                HideTopAndRightAxes: true));
    }
}

internal static class RmsScanSupport
{
    public static AnalysisData Empty(string name)
    {
        return new AnalysisData(name, new Dictionary<string, object> { ["Status"] = "No optical data" });
    }

    public static IReadOnlyList<AnalysisFieldSample> SelectedFields(Optic optic, int fieldNumber)
    {
        var fields = AnalysisTrace.DefinedFieldSamples(optic);
        return fieldNumber > 0
            ? fields.Skip(fieldNumber - 1).Take(1).ToArray()
            : fields;
    }

    public static IReadOnlyList<Wavelength> SelectedWavelengths(Optic optic, int wavelengthNumber)
    {
        var wavelengths = optic.Wavelengths.ToArray();
        return wavelengthNumber > 0
            ? wavelengths.Skip(wavelengthNumber - 1).Take(1).ToArray()
            : wavelengths;
    }

    public static string NormalizeMethod(string method)
    {
        if (string.Equals(method, "RA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, "Rectangular Array", StringComparison.OrdinalIgnoreCase))
        {
            return "RA";
        }

        return "GQ";
    }

    public static string NormalizeData(string data)
    {
        if (string.Equals(data, "wavefront", StringComparison.OrdinalIgnoreCase)
            || string.Equals(data, "Wavefront", StringComparison.OrdinalIgnoreCase)
            || string.Equals(data, "波前", StringComparison.Ordinal))
        {
            return "wavefront";
        }

        return "spot";
    }

    public static string NormalizeReference(string reference)
    {
        if (string.Equals(reference, "chief", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reference, "chief ray", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reference, "主光线", StringComparison.Ordinal))
        {
            return "chief";
        }

        return "centroid";
    }

    public static string EffectiveDistribution(string method, string distribution)
    {
        return NormalizeMethod(method) == "RA"
            ? "uniform"
            : string.IsNullOrWhiteSpace(distribution) ? "hexapolar" : distribution;
    }

    public static string AxisLabel(string data)
    {
        return NormalizeData(data) == "wavefront"
            ? "RMS Wavefront Error (waves)"
            : "RMS Spot Radius (mm)";
    }

    public static string SeriesName(string data)
    {
        return NormalizeData(data) == "wavefront"
            ? "RMS Wavefront Error"
            : "RMS Spot Radius";
    }

    public static string MaximumValueKey(string data)
    {
        return NormalizeData(data) == "wavefront"
            ? "MaximumRmsWavefrontError"
            : "MaximumRmsSpotSize";
    }

    public static double Metric(
        Optic optic,
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        int numRings,
        string distribution,
        string data,
        string reference,
        double imagePlaneOffset = 0,
        bool usePolarization = false,
        bool removeVignetting = true)
    {
        return NormalizeData(data) == "wavefront"
            ? WavefrontRms(
                optic,
                field,
                wavelengths,
                numRings,
                distribution,
                reference,
                imagePlaneOffset,
                removeVignetting)
            : SpotRadius(
                optic,
                field,
                wavelengths,
                numRings,
                distribution,
                reference,
                imagePlaneOffset,
                usePolarization);
    }

    public static double SpotRadius(
        Optic optic,
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        int numRings,
        string distribution,
        string reference,
        double imagePlaneOffset = 0,
        bool usePolarization = false)
    {
        var result = SpotAnalysisEngine.Generate(
            optic,
            new[] { field },
            wavelengths,
            numRings,
            distribution,
            imagePlaneOffset,
            reference: reference,
            usePolarization: usePolarization);
        var rays = result.Fields.FirstOrDefault()?.Wavelengths
            .SelectMany(wavelength => wavelength.Rays)
            .ToArray() ?? Array.Empty<SpotRayData>();
        return SpotAnalysisEngine.RmsRadius(rays);
    }

    public static double WavefrontRms(
        Optic optic,
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        int numRings,
        string distribution,
        string reference,
        double imagePlaneOffset = 0,
        bool removeVignetting = true)
    {
        if (wavelengths.Count == 0)
        {
            return 0;
        }

        var pupil = string.Equals(distribution, "uniform", StringComparison.OrdinalIgnoreCase)
            ? ApertureSampler.Generate(numRings * numRings, PupilSampling.UniformGrid)
            : ApertureSampler.GenerateGaussianQuadrature(numRings, 6);
        var coordinates = pupil.Select(sample => (sample.X, sample.Y)).ToArray();
        var wavefronts = DiffractionEngine.GenerateDefocusedPolychromaticWavefronts(
            optic,
            field,
            wavelengths,
            coordinates,
            imagePlaneOffset);
        var weightedMeanSquare = 0.0;
        var totalWavelengthWeight = 0.0;
        for (var wavelengthIndex = 0; wavelengthIndex < wavelengths.Count; wavelengthIndex++)
        {
            var wavelength = wavelengths[wavelengthIndex];
            var wavefront = wavefronts[wavelengthIndex];
            var samples = wavefront.Samples.Select((sample, index) => (
                    Sample: sample,
                    Weight: Math.Max(0, pupil[index].Weight)))
                .Where(item => item.Weight > 0
                    && double.IsFinite(item.Sample.OpdWaves)
                    && item.Sample.Intensity > 0)
                .ToArray();
            if (samples.Length == 0)
            {
                continue;
            }

            var rms = WeightedWavefrontRms(wavefront.Samples, pupil, reference);
            var meanSquare = rms * rms;
            var wavelengthWeight = Math.Max(0, wavelength.Weight);
            weightedMeanSquare += wavelengthWeight * meanSquare;
            totalWavelengthWeight += wavelengthWeight;
        }

        return totalWavelengthWeight <= 1e-30
            ? 0
            : Math.Sqrt(Math.Max(0, weightedMeanSquare / totalWavelengthWeight));
    }

    public static double WeightedWavefrontRms(
        IReadOnlyList<WavefrontSample> wavefront,
        IReadOnlyList<PupilSample> pupil,
        string reference)
    {
        var count = Math.Min(wavefront.Count, pupil.Count);
        var samples = Enumerable.Range(0, count)
            .Select(index => (
                Sample: wavefront[index],
                Weight: Math.Max(0, pupil[index].Weight)))
            .Where(item => item.Weight > 0
                && item.Sample.Intensity > 0
                && double.IsFinite(item.Sample.OpdWaves))
            .ToArray();
        var totalWeight = samples.Sum(item => item.Weight);
        if (totalWeight <= 1e-30)
        {
            return 0;
        }

        var piston = samples.Sum(item => item.Weight * item.Sample.OpdWaves) / totalWeight;
        var tiltX = 0.0;
        var tiltY = 0.0;
        if (NormalizeReference(reference) == "centroid")
        {
            (piston, tiltX, tiltY) = WeightedPlane(samples, piston);
        }

        var meanSquare = samples.Sum(item =>
        {
            var residual = item.Sample.OpdWaves
                - piston
                - (tiltX * item.Sample.NormalizedPupilX)
                - (tiltY * item.Sample.NormalizedPupilY);
            return item.Weight * residual * residual;
        }) / totalWeight;
        return Math.Sqrt(Math.Max(0, meanSquare));
    }

    private static (double Piston, double TiltX, double TiltY) WeightedPlane(
        IReadOnlyList<(WavefrontSample Sample, double Weight)> samples,
        double fallbackPiston)
    {
        var matrix = new double[3, 4];
        foreach (var item in samples)
        {
            var basis = new[] { 1.0, item.Sample.NormalizedPupilX, item.Sample.NormalizedPupilY };
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    matrix[row, column] += item.Weight * basis[row] * basis[column];
                }
                matrix[row, 3] += item.Weight * basis[row] * item.Sample.OpdWaves;
            }
        }

        for (var pivot = 0; pivot < 3; pivot++)
        {
            var best = Enumerable.Range(pivot, 3 - pivot)
                .OrderByDescending(row => Math.Abs(matrix[row, pivot]))
                .First();
            if (Math.Abs(matrix[best, pivot]) <= 1e-20)
            {
                return (fallbackPiston, 0, 0);
            }
            if (best != pivot)
            {
                for (var column = pivot; column < 4; column++)
                {
                    (matrix[pivot, column], matrix[best, column]) = (matrix[best, column], matrix[pivot, column]);
                }
            }

            var scale = matrix[pivot, pivot];
            for (var column = pivot; column < 4; column++)
            {
                matrix[pivot, column] /= scale;
            }
            for (var row = 0; row < 3; row++)
            {
                if (row == pivot)
                {
                    continue;
                }

                var factor = matrix[row, pivot];
                for (var column = pivot; column < 4; column++)
                {
                    matrix[row, column] -= factor * matrix[pivot, column];
                }
            }
        }

        return (matrix[0, 3], matrix[1, 3], matrix[2, 3]);
    }

    public static double DiffractionLimitMillimeters(
        Optic optic,
        IReadOnlyList<Wavelength> wavelengths)
    {
        var wavelength = wavelengths.Count == 0
            ? optic.Wavelengths.FirstOrDefault(item => item.IsPrimary) ?? optic.Wavelengths.FirstOrDefault()
            : wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths[0];
        if (wavelength is null)
        {
            return 0;
        }

        var fNumber = Math.Abs(optic.Paraxial.EstimateFNumber());
        return fNumber <= 1e-30 ? 0 : 1.22 * wavelength.Micrometers * 1e-3 * fNumber;
    }

    public static double DiffractionLimitValue(
        Optic optic,
        IReadOnlyList<Wavelength> wavelengths,
        string data)
    {
        if (string.Equals(data, "strehl", StringComparison.OrdinalIgnoreCase))
        {
            return 0.8;
        }

        return NormalizeData(data) == "wavefront"
            ? 0.072
            : DiffractionLimitMillimeters(optic, wavelengths);
    }

    public static string DiffractionLimitUnit(string data)
    {
        if (string.Equals(data, "strehl", StringComparison.OrdinalIgnoreCase))
        {
            return "ratio";
        }

        return NormalizeData(data) == "wavefront" ? "waves" : "mm";
    }

    public static AnalysisAxisQuantity MetricQuantity(string data)
    {
        return NormalizeData(data) == "wavefront"
            ? AnalysisAxisQuantity.WavefrontError
            : AnalysisAxisQuantity.Radius;
    }

    public static AnalysisAxisUnit MetricUnit(string data)
    {
        if (string.Equals(data, "strehl", StringComparison.OrdinalIgnoreCase))
        {
            return AnalysisAxisUnit.Dimensionless;
        }

        return NormalizeData(data) == "wavefront"
            ? AnalysisAxisUnit.Wave
            : AnalysisAxisUnit.Millimeter;
    }

    public static string FieldXAxisLabel(Optic optic)
    {
        return optic.FieldDefinition == FieldDefinitionKind.Angle
            ? "X Field (degrees)"
            : "X Field (mm)";
    }

    public static string FieldYAxisLabel(Optic optic)
    {
        return optic.FieldDefinition == FieldDefinitionKind.Angle
            ? "Y Field (degrees)"
            : "Y Field (mm)";
    }
}
