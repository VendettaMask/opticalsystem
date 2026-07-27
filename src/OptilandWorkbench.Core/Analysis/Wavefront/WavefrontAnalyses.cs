using System.Globalization;
using System.Text;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class WavefrontAnalysis : BaseAnalysis
{
    private readonly int _numRings;
    private readonly int _mapSize;
    private readonly int? _pupilSampling;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly bool _removeTilt;
    private readonly double _rotationDegrees;
    private readonly double _displayScale;
    private readonly string _apodization;
    private readonly bool _referenceChiefRay;
    private readonly bool _useExitPupilShape;
    private readonly int _surfaceNumber;
    private readonly string _displayAs;
    private readonly double _pupilSx;
    private readonly double _pupilSy;
    private readonly double _pupilSr;
    private readonly string _name;

    public WavefrontAnalysis(
        Optic optic,
        int numRings = 15,
        int mapSize = 65,
        int? pupilSampling = null,
        int wavelengthNumber = 0,
        int fieldNumber = 0,
        bool removeTilt = false,
        double rotationDegrees = 0,
        double displayScale = 1,
        string apodization = "无",
        bool referenceChiefRay = false,
        bool useExitPupilShape = true,
        int surfaceNumber = -1,
        string displayAs = "表面",
        double pupilSx = 0,
        double pupilSy = 0,
        double pupilSr = 1,
        string name = "Wavefront") : base(optic)
    {
        _numRings = Math.Max(2, numRings);
        _mapSize = Math.Max(17, mapSize);
        _pupilSampling = pupilSampling.HasValue ? Math.Clamp(pupilSampling.Value, 8, 512) : null;
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(0, fieldNumber);
        _removeTilt = removeTilt;
        _rotationDegrees = double.IsFinite(rotationDegrees) ? rotationDegrees : 0;
        _displayScale = double.IsFinite(displayScale) ? Math.Max(0.01, displayScale) : 1;
        _apodization = apodization;
        _referenceChiefRay = referenceChiefRay;
        _useExitPupilShape = useExitPupilShape;
        _surfaceNumber = surfaceNumber;
        _displayAs = displayAs;
        _pupilSx = double.IsFinite(pupilSx) ? pupilSx : 0;
        _pupilSy = double.IsFinite(pupilSy) ? pupilSy : 0;
        _pupilSr = double.IsFinite(pupilSr) ? Math.Max(1e-6, pupilSr) : 1;
        _name = name;
    }

    public override string Name => _name;

    public override AnalysisData GenerateData()
    {
        var wavelengths = Optic.Wavelengths.ToArray();
        var wavelength = _wavelengthNumber > 0
            ? wavelengths.ElementAtOrDefault(Math.Clamp(_wavelengthNumber - 1, 0, Math.Max(0, wavelengths.Length - 1)))
            : wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var field = _fieldNumber > 0
            ? fields[Math.Clamp(_fieldNumber - 1, 0, Math.Max(0, fields.Count - 1))]
            : fields.LastOrDefault();
        var wavefront = _pupilSampling.HasValue
            ? WavefrontEngine.GenerateChiefRayUniform(
                Optic,
                field,
                wavelength,
                _pupilSampling.Value,
                aimAtStop: _useExitPupilShape)
            : WavefrontEngine.GenerateChiefRay(Optic, field, wavelength, _numRings);
        var valid = wavefront.Samples.Where(sample => sample.Intensity > 0).ToArray();
        if (_removeTilt)
        {
            valid = RemoveBestFitPlane(valid);
        }

        var mean = valid.Select(sample => sample.OpdWaves).DefaultIfEmpty(0).Average();
        var minimum = valid.Select(sample => sample.OpdWaves).DefaultIfEmpty(0).Min();
        var maximum = valid.Select(sample => sample.OpdWaves).DefaultIfEmpty(0).Max();
        var sampling = _pupilSampling ?? _mapSize;
        var displayOffset = _pupilSampling.HasValue ? minimum : 0;
        var mapPoints = BuildWavefrontMap(valid, sampling)
            .Select(point => point with
            {
                X = _pupilSx + (point.X * _pupilSr),
                Y = _pupilSy + (point.Y * _pupilSr),
                Value = (point.Value ?? 0) - displayOffset
            })
            .ToArray();
        var series = new AnalysisSeries(
            "Pupil X",
            "Pupil Y",
            mapPoints,
            AnalysisSeriesKind.Heatmap,
            ValueLabel: "OPD (waves)");
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["RayCount"] = wavefront.Samples.Count,
            ["VignettedRayCount"] = wavefront.VignettedRayCount,
            ["ReferenceOpticalPathLength"] = wavefront.ReferenceOpticalPath,
            ["MeanOpticalPathDifference"] = mean * wavelength.Micrometers * 1e-3,
            ["RmsOpticalPathDifference"] = Rms(valid) * wavelength.Micrometers * 1e-3,
            ["PeakToValleyOpticalPathDifference"] = (maximum - minimum) * wavelength.Micrometers * 1e-3,
            ["RmsWaves"] = Rms(valid),
            ["PeakToValleyWaves"] = maximum - minimum,
            ["ReferenceSphereRadius"] = wavefront.Radius,
            ["PupilDiameterMillimeters"] = Math.Abs(Optic.Paraxial.EstimateExitPupilDiameter(wavelength.Micrometers)),
            ["FieldHx"] = field.Hx,
            ["FieldHy"] = field.Hy,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["Reference"] = _referenceChiefRay ? "chief_ray" : "reference_sphere",
            ["Sampling"] = $"{sampling} x {sampling}",
            ["RotationDegrees"] = _rotationDegrees,
            ["DisplayScale"] = _displayScale,
            ["Apodization"] = _apodization,
            ["ReferenceChiefRay"] = _referenceChiefRay,
            ["UseExitPupilShape"] = _useExitPupilShape,
            ["WavelengthNumber"] = Array.IndexOf(wavelengths, wavelength) + 1,
            ["FieldNumber"] = _fieldNumber <= 0 ? fields.Count : _fieldNumber,
            ["SurfaceNumber"] = _surfaceNumber < 0
                ? Optic.SurfaceGroup.Items.LastOrDefault()?.Number ?? 0
                : _surfaceNumber,
            ["SurfaceLabel"] = _surfaceNumber < 0
                ? Optic.SurfaceGroup.Items.LastOrDefault()?.Label ?? "Image"
                : Optic.SurfaceGroup.Items.FirstOrDefault(surface => surface.Number == _surfaceNumber)?.Label
                    ?? _surfaceNumber.ToString(),
            ["DisplayAs"] = _displayAs,
            ["RemoveTilt"] = _removeTilt,
            ["PupilSx"] = _pupilSx,
            ["PupilSy"] = _pupilSy,
            ["PupilSr"] = _pupilSr
        }, series, new[] { series }, new AnalysisPlotOptions(
            Title: _pupilSampling.HasValue
                ? $"Wavefront Function: PV={maximum - minimum:0.0000}, RMS={Rms(valid):0.0000} waves"
                : $"OPD Map: RMS={wavefront.Rms:0.000} waves",
            EqualAspect: true,
            XMinimum: _pupilSx - _pupilSr,
            XMaximum: _pupilSx + _pupilSr,
            YMinimum: _pupilSy - _pupilSr,
            YMaximum: _pupilSy + _pupilSr));
    }

    private static WavefrontSample[] RemoveBestFitPlane(IReadOnlyList<WavefrontSample> samples)
    {
        if (samples.Count < 3)
        {
            return samples.ToArray();
        }

        var count = samples.Count;
        var sx = samples.Sum(sample => sample.NormalizedPupilX);
        var sy = samples.Sum(sample => sample.NormalizedPupilY);
        var sz = samples.Sum(sample => sample.OpdWaves);
        var sxx = samples.Sum(sample => sample.NormalizedPupilX * sample.NormalizedPupilX);
        var syy = samples.Sum(sample => sample.NormalizedPupilY * sample.NormalizedPupilY);
        var sxy = samples.Sum(sample => sample.NormalizedPupilX * sample.NormalizedPupilY);
        var sxz = samples.Sum(sample => sample.NormalizedPupilX * sample.OpdWaves);
        var syz = samples.Sum(sample => sample.NormalizedPupilY * sample.OpdWaves);
        var matrix = new[,]
        {
            { (double)count, sx, sy, sz },
            { sx, sxx, sxy, sxz },
            { sy, sxy, syy, syz }
        };
        for (var pivot = 0; pivot < 3; pivot++)
        {
            var divisor = matrix[pivot, pivot];
            if (Math.Abs(divisor) <= 1e-15)
            {
                return samples.ToArray();
            }

            for (var column = pivot; column < 4; column++)
            {
                matrix[pivot, column] /= divisor;
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

        var piston = matrix[0, 3];
        var xTilt = matrix[1, 3];
        var yTilt = matrix[2, 3];
        return samples.Select(sample => sample with
        {
            OpdWaves = sample.OpdWaves
                - piston
                - (xTilt * sample.NormalizedPupilX)
                - (yTilt * sample.NormalizedPupilY)
        }).ToArray();
    }

    private static double Rms(IReadOnlyList<WavefrontSample> samples)
    {
        var meanSquare = samples.Select(sample => sample.OpdWaves * sample.OpdWaves)
            .DefaultIfEmpty(0)
            .Average();
        return Math.Sqrt(meanSquare);
    }

    internal static IReadOnlyList<AnalysisPoint> BuildWavefrontMap(
        IReadOnlyList<WavefrontSample> samples,
        int mapSize)
    {
        var points = new List<AnalysisPoint>(mapSize * mapSize);
        for (var row = 0; row < mapSize; row++)
        {
            var y = -1 + (2.0 * row / (mapSize - 1.0));
            for (var column = 0; column < mapSize; column++)
            {
                var x = -1 + (2.0 * column / (mapSize - 1.0));
                if ((x * x) + (y * y) > 1)
                {
                    continue;
                }

                var nearest = samples
                    .Select(sample => (Sample: sample, DistanceSquared:
                        ((sample.NormalizedPupilX - x) * (sample.NormalizedPupilX - x))
                        + ((sample.NormalizedPupilY - y) * (sample.NormalizedPupilY - y))))
                    .OrderBy(item => item.DistanceSquared)
                    .Take(8)
                    .ToArray();
                var exact = nearest.FirstOrDefault(item => item.DistanceSquared <= 1e-20);
                var value = exact.Sample is not null
                    ? exact.Sample.OpdWaves
                    : nearest.Sum(item => item.Sample.OpdWaves / Math.Max(1e-20, item.DistanceSquared))
                        / nearest.Sum(item => 1 / Math.Max(1e-20, item.DistanceSquared));
                points.Add(new AnalysisPoint(x, y, Value: value));
            }
        }

        return points;
    }
}

public sealed class ZernikeAnalysis : BaseAnalysis
{
    private readonly int _numRings;
    private readonly bool _useUniformGrid;
    private readonly int _numTerms;
    private readonly int _mapSize;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly string _name;
    private readonly double _obscurationRatio;

    public ZernikeAnalysis(
        Optic optic,
        int numRings = 15,
        int numTerms = 37,
        int mapSize = 65,
        int wavelengthNumber = 0,
        int fieldNumber = 0,
        string name = "Zernike",
        double obscurationRatio = 0.5) : base(optic)
    {
        _useUniformGrid = string.Equals(name, "Zernike Fringe", StringComparison.OrdinalIgnoreCase);
        _numRings = _useUniformGrid
            ? Math.Clamp(numRings, 8, 512)
            : Math.Max(2, numRings);
        _numTerms = _useUniformGrid
            ? Math.Clamp(numTerms, 1, ZernikeFitEngine.MaximumFringeTerm)
            : Math.Max(1, numTerms);
        _mapSize = Math.Max(17, mapSize);
        _wavelengthNumber = wavelengthNumber;
        _fieldNumber = fieldNumber;
        _name = name;
        _obscurationRatio = Math.Clamp(obscurationRatio, 0, 0.95);
    }

    public override string Name => _name;

    public override AnalysisData GenerateData()
    {
        var wavelengths = Optic.Wavelengths.ToArray();
        var wavelength = _wavelengthNumber > 0
            ? wavelengths.ElementAtOrDefault(_wavelengthNumber - 1)
            : wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var field = _fieldNumber > 0
            ? fields.ElementAtOrDefault(_fieldNumber - 1)
            : fields.LastOrDefault();
        var wavefront = _useUniformGrid
            ? WavefrontEngine.GenerateChiefRayUniform(Optic, field, wavelength, _numRings)
            : WavefrontEngine.GenerateChiefRay(Optic, field, wavelength, _numRings);
        var isStandard = string.Equals(Name, "Zernike Standard", StringComparison.OrdinalIgnoreCase);
        var isAnnular = string.Equals(Name, "Zernike Annular", StringComparison.OrdinalIgnoreCase);
        var coefficients = isAnnular
            ? ZernikeFitEngine.FitAnnular(wavefront.Samples, _numTerms, _obscurationRatio)
            : isStandard
                ? ZernikeFitEngine.FitStandard(wavefront.Samples, _numTerms)
                : ZernikeFitEngine.FitFringe(wavefront.Samples, _numTerms);
        double Evaluate(IReadOnlyList<ZernikeCoefficient> terms, double x, double y) =>
            isAnnular
                ? ZernikeFitEngine.EvaluateAnnular(terms, x, y, _obscurationRatio)
                : isStandard
                    ? ZernikeFitEngine.EvaluateStandard(terms, x, y)
                    : ZernikeFitEngine.Evaluate(terms, x, y);
        var values = coefficients.ToDictionary(
            coefficient => $"Z{coefficient.Number} (n={coefficient.RadialOrder}, m={coefficient.AzimuthalOrder})",
            coefficient => (object)coefficient.Value);
        values["ZernikeType"] = "fringe";
        values["WavelengthMicrometers"] = wavelength.Micrometers;
        values["FieldHx"] = field.Hx;
        values["FieldHy"] = field.Hy;
        values["Sampling"] = _useUniformGrid
            ? $"{_numRings} x {_numRings}"
            : $"{_numRings} hexapolar rings";
        values["RayCount"] = wavefront.Samples.Count;
        values["VignettedRayCount"] = wavefront.VignettedRayCount;
        var validSamples = wavefront.Samples.Where(sample =>
        {
            var radiusSquared = (sample.NormalizedPupilX * sample.NormalizedPupilX)
                + (sample.NormalizedPupilY * sample.NormalizedPupilY);
            return sample.Intensity > 0
                && (!isAnnular || radiusSquared >= (_obscurationRatio * _obscurationRatio) - 1e-12);
        }).ToArray();
        var piston = coefficients.FirstOrDefault(coefficient => coefficient.Number == 1)?.Value ?? 0;
        var referenceTerms = coefficients.Where(coefficient => coefficient.Number <= 3).ToArray();
        var relativeToChief = validSamples
            .Select(sample => sample.OpdWaves - piston)
            .ToArray();
        var relativeToCenter = validSamples
            .Select(sample => sample.OpdWaves - Evaluate(
                referenceTerms,
                sample.NormalizedPupilX,
                sample.NormalizedPupilY))
            .ToArray();
        var fitResiduals = validSamples
            .Select(sample => sample.OpdWaves - Evaluate(
                coefficients,
                sample.NormalizedPupilX,
                sample.NormalizedPupilY))
            .ToArray();
        var coefficientRmsChief = Math.Sqrt(coefficients
            .Where(coefficient => coefficient.Number >= 2)
            .Sum(coefficient => coefficient.Value * coefficient.Value));
        var coefficientRmsCenter = Math.Sqrt(coefficients
            .Where(coefficient => coefficient.Number >= 4)
            .Sum(coefficient => coefficient.Value * coefficient.Value));
        var rmsChief = Rms(relativeToChief);
        var rmsCenter = Rms(relativeToCenter);
        var peakToValleyChief = PeakToValley(relativeToChief);
        var peakToValleyCenter = PeakToValley(relativeToCenter);
        var rmsFitError = Rms(fitResiduals);
        var maximumFitError = fitResiduals.Select(Math.Abs).DefaultIfEmpty(0).Max();
        var variance = rmsCenter * rmsCenter;
        var strehl = Math.Exp(-Math.Pow(2 * Math.PI * rmsCenter, 2));
        values["PeakToValleyChiefWaves"] = peakToValleyChief;
        values["PeakToValleyCenterWaves"] = peakToValleyCenter;
        values["RmsChiefWaves"] = rmsChief;
        values["RmsCenterWaves"] = rmsCenter;
        values["VarianceWavesSquared"] = variance;
        values["EstimatedStrehlRatio"] = strehl;
        values["RmsFitErrorWaves"] = rmsFitError;
        values["MaximumFitErrorWaves"] = maximumFitError;
        var heatmapPoints = new List<AnalysisPoint>(_mapSize * _mapSize);
        for (var row = 0; row < _mapSize; row++)
        {
            var y = -1 + (2.0 * row / (_mapSize - 1.0));
            for (var column = 0; column < _mapSize; column++)
            {
                var x = -1 + (2.0 * column / (_mapSize - 1.0));
                if ((x * x) + (y * y) <= 1
                    && (!isAnnular || (x * x) + (y * y) >= _obscurationRatio * _obscurationRatio))
                {
                    heatmapPoints.Add(new AnalysisPoint(x, y, Value: Evaluate(coefficients, x, y)));
                }
            }
        }

        var heatmap = new AnalysisSeries(
            "Pupil X",
            "Pupil Y",
            heatmapPoints,
            AnalysisSeriesKind.Heatmap,
            ValueLabel: "OPD (waves)");
        var coefficientBars = new AnalysisSeries(
            "Zernike term",
            "Coefficient",
            coefficients.Select(coefficient => new AnalysisPoint(
                coefficient.Number,
                coefficient.Value,
                $"Z{coefficient.Number}")).ToArray(),
            AnalysisSeriesKind.Bar);
        return new AnalysisData(
            Name,
            values,
            coefficientBars,
            new[] { heatmap },
            new AnalysisPlotOptions(
                Title: "Zernike Fringe Fit",
                EqualAspect: true,
                XMinimum: -1,
                XMaximum: 1,
                YMinimum: -1,
                YMaximum: 1),
            ReportText: string.Equals(Name, "Zernike Fringe", StringComparison.OrdinalIgnoreCase)
                ? BuildFringeReport(
                    field,
                    wavelength.Micrometers,
                    coefficients,
                    peakToValleyChief,
                    peakToValleyCenter,
                    rmsChief,
                    rmsCenter,
                    variance,
                    strehl,
                    rmsFitError,
                    maximumFitError)
                : isStandard
                    ? BuildStandardReport(
                        field,
                        wavelength.Micrometers,
                        coefficients,
                        peakToValleyChief,
                        peakToValleyCenter,
                        rmsChief,
                        rmsCenter,
                        coefficientRmsChief,
                        coefficientRmsCenter,
                        rmsFitError,
                        maximumFitError)
                    : isAnnular
                        ? BuildAnnularReport(
                            field,
                            wavelength.Micrometers,
                            coefficients,
                            peakToValleyChief,
                            peakToValleyCenter,
                            rmsChief,
                            rmsCenter,
                            variance,
                            strehl,
                            rmsFitError,
                            maximumFitError)
                : null);
    }

    private string BuildFringeReport(
        (double Hx, double Hy) field,
        double wavelengthMicrometers,
        IReadOnlyList<ZernikeCoefficient> coefficients,
        double peakToValleyChief,
        double peakToValleyCenter,
        double rmsChief,
        double rmsCenter,
        double variance,
        double strehl,
        double rmsFitError,
        double maximumFitError)
    {
        var actualField = FieldCoordinates.Denormalize(Optic.Fields, field.Hx, field.Hy);
        var fieldMagnitude = Math.Sqrt(
            (actualField.X * actualField.X)
            + (actualField.Y * actualField.Y));
        var builder = new StringBuilder();
        builder.AppendLine("注意：RMS（对主光线）是 OPD 的 RMS 在减去 piston 后。");
        builder.AppendLine("此 RMS（对中心）是 RMS 减去 piston 和倾斜后。");
        builder.AppendLine("“对中心”表示以使波前差最小的参考点为基准。");
        builder.AppendLine();
        builder.AppendLine("使用 Zernike Fringe 多项式。");
        builder.AppendLine("关于主光线的 OPD。");
        builder.AppendLine();
        builder.AppendLine($"{"面",-24}:  像");
        builder.AppendLine($"{"视场",-22}:  {fieldMagnitude.ToString("0.0000", CultureInfo.InvariantCulture)} mm");
        builder.AppendLine($"{"波长",-22}:  {wavelengthMicrometers.ToString("0.0000", CultureInfo.InvariantCulture)} µm");
        builder.AppendLine($"{"波峰到波谷（对主光线）",-14}:  {peakToValleyChief.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine($"{"波峰到波谷（对中心）",-15}:  {peakToValleyCenter.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine($"{"RMS（对主光线）",-17}:  {rmsChief.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine($"{"RMS（对中心）",-19}:  {rmsCenter.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine($"{"方差",-22}:  {variance.ToString("0.00000000", CultureInfo.InvariantCulture)} 波平方");
        builder.AppendLine($"{"斯特列尔率（估算）",-14}:  {strehl.ToString("0.00000000", CultureInfo.InvariantCulture)}");
        builder.AppendLine();
        builder.AppendLine($"{"RMS 匹配误差",-17}:  {rmsFitError.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine($"{"最大匹配误差",-18}:  {maximumFitError.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine();
        foreach (var coefficient in coefficients)
        {
            builder.Append("Z ");
            builder.Append(coefficient.Number.ToString(CultureInfo.InvariantCulture).PadLeft(3));
            builder.Append("  ");
            builder.Append(coefficient.Value.ToString("0.00000000", CultureInfo.InvariantCulture).PadLeft(14));
            builder.Append("  :  ");
            builder.AppendLine(FringeExpression(coefficient.RadialOrder, coefficient.AzimuthalOrder));
        }

        return builder.ToString().TrimEnd();
    }

    private string BuildStandardReport(
        (double Hx, double Hy) field,
        double wavelengthMicrometers,
        IReadOnlyList<ZernikeCoefficient> coefficients,
        double peakToValleyChief,
        double peakToValleyCenter,
        double rayRmsChief,
        double rayRmsCenter,
        double coefficientRmsChief,
        double coefficientRmsCenter,
        double rmsFitError,
        double maximumFitError)
    {
        var actualField = FieldCoordinates.Denormalize(Optic.Fields, field.Hx, field.Hy);
        var fieldMagnitude = Math.Sqrt(
            (actualField.X * actualField.X)
            + (actualField.Y * actualField.Y));
        var rayVariance = rayRmsCenter * rayRmsCenter;
        var coefficientVariance = coefficientRmsCenter * coefficientRmsCenter;
        var rayStrehl = Math.Exp(-Math.Pow(2 * Math.PI * rayRmsCenter, 2));
        var coefficientStrehl = Math.Exp(-Math.Pow(2 * Math.PI * coefficientRmsCenter, 2));
        var builder = new StringBuilder();
        builder.AppendLine("注意：RMS（对主光线）是 OPD 的 RMS 在减去 piston 后。");
        builder.AppendLine("此 RMS（对中心）是 RMS 减去 piston 和倾斜后。");
        builder.AppendLine("“对中心”表示以使波前差最小的参考点为基准。");
        builder.AppendLine();
        builder.AppendLine("使用 Zernike Standard 多项式。");
        builder.AppendLine("关于主光线的 OPD。");
        builder.AppendLine();
        builder.AppendLine($"{"面",-24}:  像");
        builder.AppendLine($"{"视场",-22}:  {fieldMagnitude.ToString("0.0000", CultureInfo.InvariantCulture)} mm");
        builder.AppendLine($"{"波长",-22}:  {wavelengthMicrometers.ToString("0.0000", CultureInfo.InvariantCulture)} µm");
        builder.AppendLine($"{"波峰到波谷（对主光线）",-14}:  {peakToValleyChief.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine($"{"波峰到波谷（对中心）",-15}:  {peakToValleyCenter.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine();
        AppendStandardStatistics(builder, "来自集合光线", rayRmsChief, rayRmsCenter, rayVariance, rayStrehl);
        builder.AppendLine();
        AppendStandardStatistics(
            builder,
            "来自集合匹配系数",
            coefficientRmsChief,
            coefficientRmsCenter,
            coefficientVariance,
            coefficientStrehl);
        builder.AppendLine();
        builder.AppendLine($"{"RMS 匹配误差",-17}:  {rmsFitError.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine($"{"最大匹配误差",-18}:  {maximumFitError.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine();
        foreach (var coefficient in coefficients)
        {
            builder.Append("Z ");
            builder.Append(coefficient.Number.ToString(CultureInfo.InvariantCulture).PadLeft(3));
            builder.Append("  ");
            builder.Append(coefficient.Value.ToString("0.00000000", CultureInfo.InvariantCulture).PadLeft(14));
            builder.Append("  :  ");
            builder.AppendLine(StandardExpression(coefficient.RadialOrder, coefficient.AzimuthalOrder));
        }

        return builder.ToString().TrimEnd();
    }

    private string BuildAnnularReport(
        (double Hx, double Hy) field,
        double wavelengthMicrometers,
        IReadOnlyList<ZernikeCoefficient> coefficients,
        double peakToValleyChief,
        double peakToValleyCenter,
        double rmsChief,
        double rmsCenter,
        double variance,
        double strehl,
        double rmsFitError,
        double maximumFitError)
    {
        var actualField = FieldCoordinates.Denormalize(Optic.Fields, field.Hx, field.Hy);
        var fieldMagnitude = Math.Sqrt(
            (actualField.X * actualField.X)
            + (actualField.Y * actualField.Y));
        var builder = new StringBuilder();
        builder.AppendLine("注意：RMS（对主光线）是 OPD 的 RMS 在减去 piston 后。");
        builder.AppendLine("此 RMS（对中心）是 RMS 减去 piston 和倾斜后。");
        builder.AppendLine("“对中心”表示以使波前差最小的参考点为基准。");
        builder.AppendLine();
        builder.AppendLine("使用 Zernike Annular 多项式。");
        builder.AppendLine("关于主光线的 OPD。");
        builder.AppendLine();
        builder.AppendLine($"{"面",-24}:  像");
        builder.AppendLine($"{"视场",-22}:  {fieldMagnitude.ToString("0.0000", CultureInfo.InvariantCulture)} mm");
        builder.AppendLine($"{"波长",-22}:  {wavelengthMicrometers.ToString("0.0000", CultureInfo.InvariantCulture)} µm");
        builder.AppendLine($"{"遮光",-22}:  {_obscurationRatio.ToString("0.0000", CultureInfo.InvariantCulture)}");
        builder.AppendLine();
        builder.AppendLine($"{"波峰到波谷（对主光线）",-14}:  {peakToValleyChief.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine($"{"波峰到波谷（对中心）",-15}:  {peakToValleyCenter.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine($"{"RMS（对主光线）",-17}:  {rmsChief.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine($"{"RMS（对中心）",-19}:  {rmsCenter.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine($"{"方差",-22}:  {variance.ToString("0.00000000", CultureInfo.InvariantCulture)} 波平方");
        builder.AppendLine($"{"斯特列尔率（Est）",-14}:  {strehl.ToString("0.00000000", CultureInfo.InvariantCulture)}");
        builder.AppendLine();
        builder.AppendLine($"{"RMS 匹配误差",-17}:  {rmsFitError.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine($"{"最大匹配误差",-18}:  {maximumFitError.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine();
        foreach (var coefficient in coefficients)
        {
            builder.Append("Z ");
            builder.Append(coefficient.Number.ToString(CultureInfo.InvariantCulture).PadLeft(3));
            builder.Append("  ");
            builder.AppendLine(coefficient.Value
                .ToString("0.00000000", CultureInfo.InvariantCulture)
                .PadLeft(14));
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendStandardStatistics(
        StringBuilder builder,
        string heading,
        double rmsChief,
        double rmsCenter,
        double variance,
        double strehl)
    {
        builder.AppendLine($"{heading}：");
        builder.AppendLine($"{"RMS（对主光线）",-17}:  {rmsChief.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine($"{"RMS（对中心）",-19}:  {rmsCenter.ToString("0.00000000", CultureInfo.InvariantCulture)} 波");
        builder.AppendLine($"{"方差",-22}:  {variance.ToString("0.00000000", CultureInfo.InvariantCulture)} 波平方");
        builder.AppendLine($"{"斯特列尔率（Est）",-14}:  {strehl.ToString("0.00000000", CultureInfo.InvariantCulture)}");
    }

    private static double Rms(IReadOnlyCollection<double> values)
    {
        return values.Count == 0
            ? 0
            : Math.Sqrt(values.Sum(value => value * value) / values.Count);
    }

    private static double PeakToValley(IReadOnlyCollection<double> values)
    {
        return values.Count == 0 ? 0 : values.Max() - values.Min();
    }

    private static string FringeExpression(int radialOrder, int azimuthalOrder)
    {
        var absoluteM = Math.Abs(azimuthalOrder);
        var terms = new List<(long Coefficient, int Power)>();
        var maximum = (radialOrder - absoluteM) / 2;
        for (var k = 0; k <= maximum; k++)
        {
            var coefficient = Math.Pow(-1, k) * Factorial(radialOrder - k)
                / (Factorial(k)
                    * Factorial(((radialOrder + absoluteM) / 2) - k)
                    * Factorial(((radialOrder - absoluteM) / 2) - k));
            terms.Add(((long)Math.Round(coefficient), radialOrder - (2 * k)));
        }

        var radial = new StringBuilder();
        for (var index = 0; index < terms.Count; index++)
        {
            var (coefficient, power) = terms[index];
            var magnitude = Math.Abs(coefficient);
            if (index > 0)
            {
                radial.Append(coefficient < 0 ? " - " : " + ");
            }
            else if (coefficient < 0)
            {
                radial.Append('-');
            }

            if (power == 0 || magnitude != 1)
            {
                radial.Append(magnitude.ToString(CultureInfo.InvariantCulture));
            }

            if (power > 0)
            {
                radial.Append('p');
                if (power > 1)
                {
                    radial.Append('^');
                    radial.Append(power.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        if (absoluteM == 0)
        {
            return terms.Count == 1 ? radial.ToString() : $"({radial})";
        }

        var angle = azimuthalOrder > 0 ? "COS" : "SIN";
        var angleArgument = absoluteM == 1
            ? "A"
            : $"{absoluteM.ToString(CultureInfo.InvariantCulture)}A";
        return $"({radial}) * {angle} ({angleArgument})";
    }

    private static string StandardExpression(int radialOrder, int azimuthalOrder)
    {
        var normalizationSquared = azimuthalOrder == 0
            ? radialOrder + 1
            : 2 * (radialOrder + 1);
        var polynomial = FringeExpression(radialOrder, azimuthalOrder);
        return normalizationSquared == 1
            ? polynomial
            : $"{normalizationSquared.ToString(CultureInfo.InvariantCulture)}^(1/2) {polynomial}";
    }

    private static double Factorial(int value)
    {
        var result = 1.0;
        for (var number = 2; number <= value; number++)
        {
            result *= number;
        }

        return result;
    }
}
