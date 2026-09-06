using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class IncoherentIrradianceAnalysis : BaseAnalysis
{
    private readonly int _numRays;
    private readonly int _resolutionX;
    private readonly int _resolutionY;
    private readonly int _detectorSurfaceIndex;
    private readonly string _distribution;
    private readonly bool _normalize;

    public IncoherentIrradianceAnalysis(
        Optic optic,
        int numRays = 5,
        int resolutionX = 128,
        int resolutionY = 128,
        int detectorSurfaceIndex = -1,
        string distribution = "random",
        bool normalize = true) : base(optic)
    {
        if (numRays is < 1 or > ApertureSampler.MaximumPupilSampleCount)
        {
            throw new ArgumentOutOfRangeException(nameof(numRays));
        }

        AnalysisResourceLimits.ValidateAnalysisGrid(resolutionX, resolutionY, "Irradiance resolution");
        var sampling = RayGenerator.ParseSampling(distribution);
        ValidateSamplingBudget(numRays, sampling);
        _numRays = numRays;
        _resolutionX = resolutionX;
        _resolutionY = resolutionY;
        _detectorSurfaceIndex = detectorSurfaceIndex;
        _distribution = distribution;
        _normalize = normalize;
    }

    public override string Name => "Incoherent Irradiance";

    public override AnalysisData GenerateData()
    {
        if (Optic.SurfaceGroup.Items.Count == 0)
        {
            return Status("No detector surface");
        }

        var detectorIndex = _detectorSurfaceIndex < 0
            ? Optic.SurfaceGroup.Items.Count + _detectorSurfaceIndex
            : _detectorSurfaceIndex;
        if (detectorIndex < 0 || detectorIndex >= Optic.SurfaceGroup.Items.Count)
        {
            return Status("Detector surface index is out of range");
        }

        var detector = Optic.SurfaceGroup.Items[detectorIndex];
        if (!TryGetExtent(detector.PhysicalAperture, out var extent))
        {
            return Status("Detector surface has no supported physical aperture");
        }

        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var wavelengths = Optic.Wavelengths.ToArray();
        if (fields.Count == 0 || wavelengths.Length == 0)
        {
            return Status("No fields or wavelengths");
        }

        AnalysisResourceLimits.ValidateAggregateGridWork(
            _resolutionX,
            _resolutionY,
            fields.Count,
            wavelengths.Length,
            RadiometricPupilSampleCount(
                _numRays,
                RayGenerator.ParseSampling(_distribution)),
            "Incoherent irradiance");

        var xStep = (extent.XMaximum - extent.XMinimum) / _resolutionX;
        var yStep = (extent.YMaximum - extent.YMinimum) / _resolutionY;
        var pixelArea = xStep * yStep;
        var pupilSamples = GenerateRadiometricPupilSamples(
            _numRays,
            RayGenerator.ParseSampling(_distribution));
        var panes = new List<AnalysisPlotPane>(fields.Count * wavelengths.Length);
        var peaks = new List<double>(fields.Count * wavelengths.Length);
        var validRayCount = 0;

        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var field = fields[fieldIndex];
            for (var wavelengthIndex = 0; wavelengthIndex < wavelengths.Length; wavelengthIndex++)
            {
                var wavelength = wavelengths[wavelengthIndex];
                var bundle = Optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                    field.Hx,
                    field.Hy,
                    wavelength.Micrometers,
                    pupilSamples);
                using var trace = Optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected(new[] { detectorIndex }));
                var irradiance = new double[_resolutionX, _resolutionY];
                foreach (var sampleValue in trace.GetSurfaceSamples(detectorIndex))
                {
                    if (sampleValue is not { } sample)
                    {
                        continue;
                    }
                    if (sample.Intensity <= 0 || sample.Vignetted)
                    {
                        continue;
                    }

                    var local = detector.CoordinateSystem.ToLocalPoint(sample.Position);
                    var xBin = BinIndex(local.X, extent.XMinimum, extent.XMaximum, _resolutionX);
                    var yBin = BinIndex(local.Y, extent.YMinimum, extent.YMaximum, _resolutionY);
                    if (xBin < 0 || yBin < 0)
                    {
                        continue;
                    }

                    irradiance[xBin, yBin] += sample.Intensity / pixelArea;
                    validRayCount++;
                }

                var peak = irradiance.Cast<double>().DefaultIfEmpty(0).Max();
                peaks.Add(peak);
                var points = new List<AnalysisPoint>(_resolutionX * _resolutionY);
                for (var x = 0; x < _resolutionX; x++)
                {
                    ComputationCancellation.ThrowIfCancellationRequested();
                    var xCenter = extent.XMinimum + ((x + 0.5) * xStep);
                    for (var y = 0; y < _resolutionY; y++)
                    {
                        var yCenter = extent.YMinimum + ((y + 0.5) * yStep);
                        var value = _normalize && peak > 0 ? irradiance[x, y] / peak : irradiance[x, y];
                        points.Add(new AnalysisPoint(xCenter, yCenter, Value: value));
                    }
                }

                var title = $"{MtfPresentation.FieldName(Optic, field)}, "
                    + $"\u03BB{wavelengthIndex} = {wavelength.Micrometers:0.000} \u00B5m";
                var series = new AnalysisSeries(
                    "X (mm)",
                    "Y (mm)",
                    points,
                    AnalysisSeriesKind.Heatmap,
                    ValueLabel: _normalize ? "Normalized Irradiance" : "Irradiance (W/mm\u00B2)",
                    ColorMap: AnalysisColorMap.Inferno,
                    ValueMinimum: _normalize ? 0 : null,
                    ValueMaximum: _normalize ? 1 : null,
                    XQuantity: AnalysisAxisQuantity.ImageHeight,
                    XUnit: AnalysisAxisUnit.Millimeter,
                    YQuantity: AnalysisAxisQuantity.ImageHeight,
                    YUnit: AnalysisAxisUnit.Millimeter,
                    ValueQuantity: AnalysisAxisQuantity.Irradiance,
                    ValueUnit: _normalize
                        ? AnalysisAxisUnit.Dimensionless
                        : AnalysisAxisUnit.WattsPerSquareMillimeter);
                panes.Add(new AnalysisPlotPane(title, new[] { series }, new AnalysisPlotOptions(
                    Title: title,
                    EqualAspect: true,
                    XMinimum: extent.XMinimum,
                    XMaximum: extent.XMaximum,
                    YMinimum: extent.YMinimum,
                    YMaximum: extent.YMaximum,
                    GridOpacity: 0)));
            }
        }

        var firstSeries = panes.FirstOrDefault()?.Series.FirstOrDefault();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["DetectorSurfaceIndex"] = detectorIndex,
            ["DetectorExtent"] = $"[{extent.XMinimum:R}, {extent.XMaximum:R}] x [{extent.YMinimum:R}, {extent.YMaximum:R}] mm",
            ["Resolution"] = $"{_resolutionX} x {_resolutionY}",
            ["NumRays"] = _numRays,
            ["Distribution"] = _distribution,
            ["Normalized"] = _normalize,
            ["ValidRayCount"] = validRayCount,
            ["PeakIrradiance"] = peaks.DefaultIfEmpty(0).Max(),
            ["FieldCount"] = fields.Count,
            ["WavelengthCount"] = wavelengths.Length
        }, firstSeries, firstSeries is null ? null : new[] { firstSeries }, PlotPanes: panes, PlotPaneColumns: wavelengths.Length);
    }

    private AnalysisData Status(string message)
    {
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Status"] = message,
            ["DetectorApertureRequirement"] = "Set a physical aperture on the detector surface"
        }, Outcome: AnalysisOutcome.Unavailable, OutcomeReason: message);
    }

    private static bool TryGetExtent(
        IPhysicalAperture? aperture,
        out (double XMinimum, double XMaximum, double YMinimum, double YMaximum) extent)
    {
        if (PhysicalApertureBoundsCalculator.TryGetBounds(aperture, out var bounds))
        {
            extent = (bounds.XMinimum, bounds.XMaximum, bounds.YMinimum, bounds.YMaximum);
            return true;
        }

        extent = default;
        return false;
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

    private static void ValidateSamplingBudget(int numRays, PupilSampling sampling)
    {
        if (sampling == PupilSampling.Hexapolar)
        {
            _ = ApertureSampler.CountHexapolarRingSamples(numRays);
        }
    }

    private static IReadOnlyList<PupilSample> GenerateRadiometricPupilSamples(
        int numRays,
        PupilSampling sampling) =>
        sampling == PupilSampling.Hexapolar
            ? ApertureSampler.GenerateHexapolarRings(numRays)
            : ApertureSampler.Generate(numRays, sampling);

    private static int RadiometricPupilSampleCount(
        int numRays,
        PupilSampling sampling) =>
        sampling == PupilSampling.Hexapolar
            ? ApertureSampler.CountHexapolarRingSamples(numRays)
            : numRays;
}
