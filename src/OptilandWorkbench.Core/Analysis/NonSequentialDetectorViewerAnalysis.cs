using OptilandWorkbench.Core.NonSequential;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Core.Analysis;

public sealed class NonSequentialDetectorViewerAnalysis : BaseAnalysis
{
    private readonly NonSequentialDocument _document;
    private readonly int _detectorNumber;
    private readonly int _sourceNumber;
    private readonly IReadOnlyList<NonSequentialDetectorFrame>? _databaseFrames;

    public NonSequentialDetectorViewerAnalysis(
        Optic optic,
        NonSequentialDocument? document = null,
        int detectorNumber = 1,
        int sourceNumber = 0,
        IReadOnlyList<NonSequentialDetectorFrame>? databaseFrames = null) : base(optic)
    {
        _document = (document ?? StarOptProjectStore.CreateDefaultNonSequentialDocument(optic)).Clone();
        _detectorNumber = Math.Max(1, detectorNumber);
        _sourceNumber = Math.Max(0, sourceNumber);
        _databaseFrames = databaseFrames;
    }

    public override string Name => "Non-Sequential Detector Viewer";

    public override AnalysisData GenerateData()
    {
        var detectors = _document.Objects.Where(item => item.Enabled
            && item.Kind == NonSequentialObjectKind.DetectorRectangle).ToArray();
        var sources = _document.Objects
            .Where(item => item.Enabled && item.Parameters is SourceParameters).ToArray();
        if (detectors.Length == 0 || _databaseFrames is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object>
            {
                ["Status"] = detectors.Length == 0 ? "No detector objects" : "No trace result"
            }, ReportText: detectors.Length == 0
                ? "场景没有启用的矩形探测器。"
                : "没有可用的非序列追迹结果。请先在追迹控制中运行追迹；探测器查看器不会隐式重新追迹。");
        }

        var detectorIndex = Math.Clamp(_detectorNumber - 1, 0, detectors.Length - 1);
        var detectorObject = detectors[detectorIndex];
        var detectorParameters = (DetectorRectangleParameters)detectorObject.Parameters;
        var sourceId = _sourceNumber > 0
            ? sources[Math.Clamp(_sourceNumber - 1, 0, sources.Length - 1)].Id
            : (Guid?)null;
        var frame = _databaseFrames.SingleOrDefault(item => item.DetectorId == detectorObject.Id)
            ?? throw new InvalidOperationException("当前追迹结果不包含所选探测器。");
        var combined = new double[frame.PixelsX * frame.PixelsY];
        foreach (var wavelength in frame.PowerByWavelength.Values)
        {
            for (var index = 0; index < combined.Length; index++) combined[index] += wavelength[index];
        }
        var pixelArea = detectorParameters.WidthMillimeters / frame.PixelsX
            * detectorParameters.HeightMillimeters / frame.PixelsY;
        var points = new AnalysisPoint[combined.Length];
        for (var y = 0; y < frame.PixelsY; y++)
            for (var x = 0; x < frame.PixelsX; x++)
            {
                var index = y * frame.PixelsX + x;
                points[index] = new AnalysisPoint(
                    -detectorParameters.WidthMillimeters / 2 + (x + 0.5) * detectorParameters.WidthMillimeters / frame.PixelsX,
                    -detectorParameters.HeightMillimeters / 2 + (y + 0.5) * detectorParameters.HeightMillimeters / frame.PixelsY,
                    Value: combined[index] / pixelArea);
            }
        var series = new AnalysisSeries(
            "X (mm)",
            "Y (mm)",
            points,
            AnalysisSeriesKind.Heatmap,
            detectorObject.Name,
            ValueLabel: "辐照度 (W/mm²)",
            ColorMap: AnalysisColorMap.Inferno,
            XQuantity: AnalysisAxisQuantity.Coordinate,
            XUnit: AnalysisAxisUnit.Millimeter,
            YQuantity: AnalysisAxisQuantity.Coordinate,
            YUnit: AnalysisAxisUnit.Millimeter,
            ValueQuantity: AnalysisAxisQuantity.Irradiance,
            ValueUnit: AnalysisAxisUnit.WattsPerSquareMillimeter);
        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["DetectorNumber"] = detectorIndex + 1,
                ["DetectorName"] = detectorObject.Name,
                ["PixelsX"] = frame.PixelsX,
                ["PixelsY"] = frame.PixelsY,
                ["TotalPowerWatts"] = frame.TotalPowerWatts,
                ["MaximumIrradianceWattsPerSquareMillimeter"] = combined.Length == 0 ? 0 : combined.Max() / pixelArea,
                ["Source"] = _databaseFrames is null ? "Current trace" : "Filtered ray database"
            },
            series,
            new[] { series },
            new AnalysisPlotOptions(Title: $"探测器：{detectorObject.Name}", EqualAspect: true, DefaultSquareViewport: true));
    }
}
