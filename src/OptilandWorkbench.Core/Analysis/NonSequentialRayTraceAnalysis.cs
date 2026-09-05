using System.Globalization;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.NonSequential;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Core.Analysis;

public sealed class NonSequentialRayTraceAnalysis : BaseAnalysis
{
    private readonly NonSequentialDocument _document;
    private readonly int _sourceNumber;
    private readonly bool _directRay;
    private readonly Vector3D _origin;
    private readonly Vector3D _direction;
    private readonly int _wavelengthNumber;
    private readonly double _powerWatts;
    private readonly bool _layoutRays;
    private readonly bool _splitFresnelRays;

    public NonSequentialRayTraceAnalysis(
        Optic optic,
        NonSequentialDocument? document = null,
        int sourceNumber = 0,
        bool directRay = false,
        double x = 0,
        double y = 0,
        double z = 0,
        double l = 0,
        double m = 0,
        double n = 1,
        int wavelengthNumber = 1,
        double powerWatts = 1,
        bool layoutRays = false,
        bool splitFresnelRays = true) : base(optic)
    {
        _document = (document ?? StarOptProjectStore.CreateDefaultNonSequentialDocument(optic)).Clone();
        _sourceNumber = Math.Max(0, sourceNumber);
        _directRay = directRay;
        _origin = new Vector3D(x, y, z);
        _direction = new Vector3D(l, m, n);
        _wavelengthNumber = Math.Max(1, wavelengthNumber);
        _powerWatts = powerWatts;
        _layoutRays = layoutRays;
        _splitFresnelRays = splitFresnelRays;
    }

    public override string Name => "Non-Sequential Ray Trace";

    public override AnalysisData GenerateData()
    {
        var sourceObjects = _document.Objects
            .Where(item => item.Enabled && item.Parameters is SourceParameters).ToArray();
        if (!_directRay && sourceObjects.Length == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object>
            {
                ["Status"] = "No non-sequential source objects"
            }, ReportText: "非序列场景没有启用的光源对象。请在对象编辑器中添加光源后再追迹。",
                Outcome: AnalysisOutcome.NotApplicable, OutcomeReason: "No non-sequential source objects");
        }

        if (_directRay && (!double.IsFinite(_direction.Length) || _direction.Length <= 1e-15))
        {
            throw new InvalidOperationException("直接追迹方向必须是有限非零向量。");
        }

        var wavelengthIndex = Math.Clamp(_wavelengthNumber - 1, 0, _document.Wavelengths.Count - 1);
        var direct = _directRay
            ? new RealRay(
                _origin,
                _direction,
                _document.Wavelengths[wavelengthIndex].Nanometers,
                _powerWatts)
            : null;
        Guid? sourceId = _sourceNumber > 0 && sourceObjects.Length > 0
            ? sourceObjects[Math.Clamp(_sourceNumber - 1, 0, sourceObjects.Length - 1)].Id
            : null;
        var result = new NonSequentialDocumentTracer().Trace(
            _document,
            Optic.Materials,
            new NonSequentialDocumentTraceRequest(
                _layoutRays ? NonSequentialTracePurpose.Layout : NonSequentialTracePurpose.Analysis,
                sourceId,
                direct,
                _splitFresnelRays));

        var branchSeries = result.Branches
            .Where(branch => branch.Segments.Count > 0)
            .Select((branch, index) => new AnalysisSeries(
                "Z (mm)",
                "Y (mm)",
                branch.Segments.SelectMany(segment => new[]
                {
                    new AnalysisPoint(segment.Start.Z, segment.Start.Y),
                    new AnalysisPoint(segment.End.Z, segment.End.Y)
                }).ToArray(),
                Name: $"分支 {branch.Id}",
                ColorIndex: index,
                ShowMarkers: false,
                LineWidth: 1.5,
                XQuantity: AnalysisAxisQuantity.Coordinate,
                XUnit: AnalysisAxisUnit.Millimeter,
                YQuantity: AnalysisAxisQuantity.RayHeight,
                YUnit: AnalysisAxisUnit.Millimeter))
            .ToArray();
        var table = new AnalysisTable(
            new[] { "分支", "父分支", "层级", "对象", "面", "交互", "X", "Y", "Z", "强度", "累计光程" },
            result.Branches.SelectMany(branch => branch.Segments.Select(segment => (IReadOnlyList<string>)new[]
            {
                branch.Id.ToString(CultureInfo.InvariantCulture),
                branch.ParentId?.ToString(CultureInfo.InvariantCulture) ?? "-",
                branch.Level.ToString(CultureInfo.InvariantCulture),
                segment.ObjectId?.ToString() ?? "-",
                segment.FaceNumber.ToString(CultureInfo.InvariantCulture),
                segment.InteractionKind?.ToString() ?? branch.TerminationReason.ToString(),
                Format(segment.End.X), Format(segment.End.Y), Format(segment.End.Z),
                Format(segment.Intensity), Format(segment.CumulativeOpticalPathLength)
            })).ToArray());
        var energy = result.EnergyBalance;
        var values = new Dictionary<string, object>
        {
            ["SourceCount"] = sourceObjects.Length,
            ["BranchCount"] = result.Branches.Count,
            ["SegmentCount"] = result.SegmentCount,
            ["DetectorCount"] = result.Detectors.Count,
            ["SourcePowerWatts"] = energy.SourcePowerWatts,
            ["DetectorPowerWatts"] = energy.DetectorPowerWatts,
            ["AbsorbedPowerWatts"] = energy.AbsorbedPowerWatts,
            ["EscapedPowerWatts"] = energy.EscapedPowerWatts,
            ["TruncatedPowerWatts"] = energy.TruncatedPowerWatts,
            ["EnergyBalanceErrorWatts"] = energy.SourcePowerWatts - energy.AccountedPowerWatts
        };
        var primary = branchSeries.FirstOrDefault() ?? new AnalysisSeries(
            "Z (mm)", "Y (mm)", Array.Empty<AnalysisPoint>(),
            XQuantity: AnalysisAxisQuantity.Coordinate,
            XUnit: AnalysisAxisUnit.Millimeter,
            YQuantity: AnalysisAxisQuantity.RayHeight,
            YUnit: AnalysisAxisUnit.Millimeter);
        return new AnalysisData(
            Name,
            values,
            primary,
            branchSeries,
            new AnalysisPlotOptions(Title: "非序列射线树", EqualAspect: true, DottedGrid: true),
            Table: table,
            ReportText: BuildReport(result));
    }

    private static string BuildReport(NonSequentialDocumentTraceResult result)
    {
        var energy = result.EnergyBalance;
        var lines = new List<string>
        {
            "Non-Sequential Ray Trace",
            $"Branches: {result.Branches.Count}",
            $"Segments: {result.SegmentCount}",
            $"Source power (W): {Format(energy.SourcePowerWatts)}",
            $"Detector power (W): {Format(energy.DetectorPowerWatts)}",
            $"Absorbed power (W): {Format(energy.AbsorbedPowerWatts)}",
            $"Escaped power (W): {Format(energy.EscapedPowerWatts)}",
            $"Truncated power (W): {Format(energy.TruncatedPowerWatts)}",
            $"Balance error (W): {Format(energy.SourcePowerWatts - energy.AccountedPowerWatts)}",
            string.Empty,
            "Detectors:"
        };
        lines.AddRange(result.Detectors.Select(detector =>
            $"{detector.DetectorName}: {detector.PixelsX} x {detector.PixelsY}, {Format(detector.TotalPowerWatts)} W"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string Format(double value) => value.ToString("0.############", CultureInfo.InvariantCulture);
}
