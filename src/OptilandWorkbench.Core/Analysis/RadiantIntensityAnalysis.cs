using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed class RadiantIntensityAnalysis : BaseAnalysis
{
    private readonly int _binsX;
    private readonly int _binsY;
    private readonly double _angleXMinimum;
    private readonly double _angleXMaximum;
    private readonly double _angleYMinimum;
    private readonly double _angleYMaximum;
    private readonly bool _useAbsoluteUnits;
    private readonly int _referenceSurfaceIndex;
    private readonly int _numRays;
    private readonly string _distribution;
    private readonly bool _normalize;

    public RadiantIntensityAnalysis(
        Optic optic,
        int binsX = 101,
        int binsY = 101,
        double angleXMinimum = -15,
        double angleXMaximum = 15,
        double angleYMinimum = -15,
        double angleYMaximum = 15,
        bool useAbsoluteUnits = true,
        int referenceSurfaceIndex = -1,
        int numRays = 100000,
        string distribution = "random",
        bool? normalize = null) : base(optic)
    {
        _binsX = Math.Max(1, binsX);
        _binsY = Math.Max(1, binsY);
        _angleXMinimum = Math.Min(angleXMinimum, angleXMaximum);
        _angleXMaximum = Math.Max(angleXMinimum, angleXMaximum);
        _angleYMinimum = Math.Min(angleYMinimum, angleYMaximum);
        _angleYMaximum = Math.Max(angleYMinimum, angleYMaximum);
        _useAbsoluteUnits = useAbsoluteUnits;
        _referenceSurfaceIndex = referenceSurfaceIndex;
        _numRays = Math.Max(1, numRays);
        _distribution = distribution;
        _normalize = normalize ?? !useAbsoluteUnits;
    }

    public override string Name => "Radiant Intensity";

    public override AnalysisData GenerateData()
    {
        if (Optic.SurfaceGroup.Items.Count == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No reference surface" });
        }

        var referenceIndex = _referenceSurfaceIndex < 0
            ? Optic.SurfaceGroup.Items.Count + _referenceSurfaceIndex
            : _referenceSurfaceIndex;
        if (referenceIndex < 0 || referenceIndex >= Optic.SurfaceGroup.Items.Count)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "Reference surface index is out of range" });
        }

        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var wavelengths = Optic.Wavelengths.ToArray();
        if (fields.Count == 0 || wavelengths.Length == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No fields or wavelengths" });
        }

        var xStep = (_angleXMaximum - _angleXMinimum) / _binsX;
        var yStep = (_angleYMaximum - _angleYMinimum) / _binsY;
        var solidAngle = DegreesToRadians(xStep) * DegreesToRadians(yStep);
        var pupilSamples = SpotAnalysisEngine.CreatePupilSamples(_numRays, _distribution);
        var maps = new List<IntensityMap>(fields.Count * wavelengths.Length);
        var validRayCount = 0;

        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            for (var wavelengthIndex = 0; wavelengthIndex < wavelengths.Length; wavelengthIndex++)
            {
                var wavelength = wavelengths[wavelengthIndex];
                var bundle = Optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                    field.Hx,
                    field.Hy,
                    wavelength.Micrometers,
                    pupilSamples);
                var trace = Optic.SequentialRayTracer.Trace(bundle);
                var values = new double[_binsX, _binsY];
                foreach (var history in trace.RayHistories)
                {
                    if (history.Count <= referenceIndex)
                    {
                        continue;
                    }

                    var sample = history[referenceIndex];
                    if (sample.Intensity <= 1e-12
                        || !double.IsFinite(sample.Direction.X)
                        || !double.IsFinite(sample.Direction.Y)
                        || !double.IsFinite(sample.Direction.Z)
                        || Math.Abs(sample.Direction.Z) <= 1e-9)
                    {
                        continue;
                    }

                    var angleX = Math.Atan2(sample.Direction.X, sample.Direction.Z) * 180 / Math.PI;
                    var angleY = Math.Atan2(sample.Direction.Y, sample.Direction.Z) * 180 / Math.PI;
                    var xBin = BinIndex(angleX, _angleXMinimum, _angleXMaximum, _binsX);
                    var yBin = BinIndex(angleY, _angleYMinimum, _angleYMaximum, _binsY);
                    if (xBin < 0 || yBin < 0)
                    {
                        continue;
                    }

                    values[xBin, yBin] += sample.Intensity;
                    validRayCount++;
                }

                if (_useAbsoluteUnits && solidAngle > 1e-12)
                {
                    for (var x = 0; x < _binsX; x++)
                    {
                        for (var y = 0; y < _binsY; y++)
                        {
                            values[x, y] /= solidAngle;
                        }
                    }
                }

                maps.Add(new IntensityMap(fieldIndex, wavelengthIndex, field, wavelength, values));
            }
        }

        var peaks = maps.Select(map => map.Values.Cast<double>().DefaultIfEmpty(0).Max()).ToArray();
        var globalMaximum = _normalize ? 1 : peaks.DefaultIfEmpty(0).Max();
        if (globalMaximum <= 0)
        {
            globalMaximum = 1;
        }

        var panes = new List<AnalysisPlotPane>(maps.Count * 2);
        foreach (var map in maps)
        {
            var peak = map.Values.Cast<double>().DefaultIfEmpty(0).Max();
            var display = new double[_binsX, _binsY];
            for (var x = 0; x < _binsX; x++)
            {
                for (var y = 0; y < _binsY; y++)
                {
                    display[x, y] = _normalize && peak > 1e-9 ? map.Values[x, y] / peak : map.Values[x, y];
                }
            }

            var valueLabel = _normalize ? "Normalized Intensity" : "Radiant Intensity (W/sr)";
            var title = $"{MtfPresentation.FieldName(Optic, map.Field)}, "
                + $"\u03BB={map.Wavelength.Micrometers:0.000} \u00B5m";
            var heatmapPoints = new List<AnalysisPoint>(_binsX * _binsY);
            for (var x = 0; x < _binsX; x++)
            {
                var xCenter = _angleXMinimum + ((x + 0.5) * xStep);
                for (var y = 0; y < _binsY; y++)
                {
                    var yCenter = _angleYMinimum + ((y + 0.5) * yStep);
                    heatmapPoints.Add(new AnalysisPoint(xCenter, yCenter, Value: display[x, y]));
                }
            }

            var heatmap = new AnalysisSeries(
                "X-Angle (degrees)",
                "Y-Angle (degrees)",
                heatmapPoints,
                AnalysisSeriesKind.Heatmap,
                ValueLabel: valueLabel,
                ColorMap: AnalysisColorMap.Jet,
                ValueMinimum: 0,
                ValueMaximum: globalMaximum);
            panes.Add(new AnalysisPlotPane(title, new[] { heatmap }, new AnalysisPlotOptions(
                Title: title,
                XMinimum: _angleXMinimum,
                XMaximum: _angleXMaximum,
                YMinimum: _angleYMinimum,
                YMaximum: _angleYMaximum,
                DottedGrid: true,
                GridOpacity: 0.7)));

            var centerY = _binsY / 2;
            var crossSection = new AnalysisSeries(
                "X-Angle (degrees)",
                valueLabel,
                Enumerable.Range(0, _binsX)
                    .Select(x => new AnalysisPoint(_angleXMinimum + ((x + 0.5) * xStep), display[x, centerY]))
                    .ToArray(),
                ColorIndex: 3);
            panes.Add(new AnalysisPlotPane("Central Cross-Section", new[] { crossSection }, new AnalysisPlotOptions(
                Title: "Central Cross-Section",
                XMinimum: _angleXMinimum + (xStep / 2),
                XMaximum: _angleXMaximum - (xStep / 2),
                YMinimum: -0.05 * globalMaximum,
                YMaximum: 1.1 * globalMaximum,
                DottedGrid: true,
                GridOpacity: 0.7)));
        }

        var firstSeries = panes.FirstOrDefault()?.Series.FirstOrDefault();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["ReferenceSurfaceIndex"] = referenceIndex,
            ["AngularBins"] = $"{_binsX} x {_binsY}",
            ["AngleXRange"] = $"[{_angleXMinimum:R}, {_angleXMaximum:R}] deg",
            ["AngleYRange"] = $"[{_angleYMinimum:R}, {_angleYMaximum:R}] deg",
            ["UseAbsoluteUnits"] = _useAbsoluteUnits,
            ["Normalized"] = _normalize,
            ["NumRays"] = _numRays,
            ["Distribution"] = _distribution,
            ["ValidRayCount"] = validRayCount,
            ["PeakRadiantIntensity"] = peaks.DefaultIfEmpty(0).Max(),
            ["FieldCount"] = fields.Count,
            ["WavelengthCount"] = wavelengths.Length
        }, firstSeries, firstSeries is null ? null : new[] { firstSeries }, PlotPanes: panes, PlotPaneColumns: wavelengths.Length * 2);
    }

    private static int BinIndex(double value, double minimum, double maximum, int count)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            return -1;
        }

        if (value == maximum)
        {
            return count - 1;
        }

        return Math.Clamp((int)Math.Floor((value - minimum) / (maximum - minimum) * count), 0, count - 1);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    private sealed record IntensityMap(
        int FieldIndex,
        int WavelengthIndex,
        (double Hx, double Hy) Field,
        Wavelength Wavelength,
        double[,] Values);
}
