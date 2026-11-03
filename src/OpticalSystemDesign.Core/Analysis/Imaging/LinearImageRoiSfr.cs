using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

public sealed record LinearSfrRoi(string Id, SfrRegion Region,
    LinearEdgeOrientation Orientation = LinearEdgeOrientation.NearVertical, int HalfWindowPixels = 24);
public enum LinearSfrRoiStatus { Measured, InvalidRegion, NoLight, EdgeQualityFailed }
/// <summary>Index coordinates refer to pixel centres (the top-left centre is 0,0).
/// The normal is unsigned/canonical in the detector's local X,Y frame, not a T/S assignment.</summary>
public sealed record LinearSfrEdgeGeometry(double Column, double Row, double XMillimeters, double YMillimeters,
    double NormalX, double NormalY, double NormalAngleDegrees, int ProjectedSamples, int MinimumBinSamples);
public sealed record LinearSfrRoiResult(LinearSfrRoi Roi, LinearSfrRoiStatus Status, string? Failure,
    LinearImageEdgeResult? Edge, LinearSfrEdgeGeometry? Geometry, SlantedEdgeFit? Fit = null);

/// <summary>Independent image-only measurements; one bad region never supplies a curve or aborts its neighbours.
/// Uses the existing estimator and quality gates, with no target pose, lens data or reference MTF as inputs.</summary>
public static class LinearImageRoiSfr
{
    public const int MaximumRegions = 32;
    public const string Algorithm = "LinearImageRois/1; independent regions; detector-frame normals; " + LinearImageSfr.Algorithm;

    public static IReadOnlyList<LinearSfrRoiResult> Measure(IReadOnlyList<double> pixels, int width, int height,
        double pitchMicrometers, DetectorRowOrder rowOrder, IReadOnlyList<LinearSfrRoi> rois,
        Action<int>? completed = null)
    {
        ArgumentNullException.ThrowIfNull(pixels); ValidateRegions(rois);
        if (width is < 4 or > 1024 || height is < 4 or > 1024 || (long)width * height > 262144
            || pixels.Count != width * height || !double.IsFinite(pitchMicrometers) || pitchMicrometers <= 0 || !Enum.IsDefined(rowOrder))
            throw new ArgumentException("Invalid complete linear raster, pixel pitch or row order.");
        var frozen = pixels.ToArray();
        if (frozen.Any(v => !double.IsFinite(v) || v < 0)) throw new ArgumentException("Linear pixels must be finite and nonnegative.");
        var results = new List<LinearSfrRoiResult>();
        foreach (var roi in rois.ToArray())
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var r = roi.Region;
            if (r.X < 0 || r.Y < 0 || r.Width is < 32 or > 256 || r.Height is < 32 or > 256
                || (long)r.X + r.Width > width || (long)r.Y + r.Height > height
                || roi.HalfWindowPixels is < 8 or > 48 || !Enum.IsDefined(roi.Orientation))
                results.Add(new(roi, LinearSfrRoiStatus.InvalidRegion,
                    "ROI 必须在完整图内，宽高各 32–256 px，半窗口 8–48 px，且选择有效方向。", null, null));
            else
            {
                var light = false;
                for (var y = r.Y; y < r.Y + r.Height && !light; y++)
                    for (var x = r.X; x < r.X + r.Width; x++) if (frozen[y * width + x] > 0) { light = true; break; }
                if (!light) results.Add(new(roi, LinearSfrRoiStatus.NoLight, "ROI 没有入射光，无法测量。", null, null));
                else
                {
                    SlantedEdgeFit? fit = null;
                    try
                    {
                        var edge = LinearImageSfr.Measure(frozen, width, height, r, pitchMicrometers, roi.Orientation, roi.HalfWindowPixels, f => fit = f);
                        results.Add(new(roi, LinearSfrRoiStatus.Measured, null, edge, Geometry(edge, width, height, pitchMicrometers, rowOrder), fit));
                    }
                    catch (InvalidOperationException ex)
                    { results.Add(new(roi, LinearSfrRoiStatus.EdgeQualityFailed, "未满足单一斜边质量条件：" + ex.Message, null, null, fit)); }
                }
            }
            completed?.Invoke(results.Count);
        }
        return results.AsReadOnly();
    }

    public static void ValidateRegions(IReadOnlyList<LinearSfrRoi> rois)
    {
        ArgumentNullException.ThrowIfNull(rois);
        if (rois.Count is < 1 or > MaximumRegions || rois.Any(r => r is null || r.Region is null
                || string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 80 || r.Id.Any(char.IsControl))
            || rois.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != rois.Count)
            throw new ArgumentException($"Specify 1–{MaximumRegions} regions with unique, nonempty identifiers (up to 80 characters).");
    }

    private static LinearSfrEdgeGeometry Geometry(LinearImageEdgeResult edge, int width, int height, double pitch, DetectorRowOrder order)
    {
        var m = edge.Measurement; var r = edge.Region; var horizontal = edge.Orientation == LinearEdgeOrientation.NearHorizontal;
        var scale = Math.Sqrt(1 + m.EdgeSlope * m.EdgeSlope);
        var column = r.X + (r.Width - 1) / 2d; var row = r.Y + (r.Height - 1) / 2d;
        if (horizontal) row = m.EdgeIntercept + m.EdgeSlope * column;
        else column = m.EdgeIntercept + m.EdgeSlope * row;
        var nx = (horizontal ? -m.EdgeSlope : 1) / scale;
        var ny = (horizontal ? 1 : -m.EdgeSlope) / scale;
        var rowSign = order == DetectorRowOrder.PositiveLocalYFirst ? -1 : 1;
        ny *= rowSign;
        // Count the same occupied projection bins from the measured profile spacing, not a second fit.
        var bins = new int[m.Esf.Count]; var step = m.Esf[1].DistancePixels - m.Esf[0].DistancePixels;
        for (var y = r.Y; y < r.Y + r.Height; y++) for (var x = r.X; x < r.X + r.Width; x++)
        {
            var distance = (horizontal ? y - m.EdgeIntercept - m.EdgeSlope * x : x - m.EdgeIntercept - m.EdgeSlope * y) / scale;
            var bin = (int)Math.Floor((distance - m.Esf[0].DistancePixels) / step + .5);
            if ((uint)bin < bins.Length) bins[bin]++;
        }
        return new(column, row, (column + .5 - width / 2d) * pitch * .001,
            rowSign * (row + .5 - height / 2d) * pitch * .001, nx, ny, Math.Atan2(ny, nx) * 180 / Math.PI,
            bins.Sum(), bins.Min());
    }
}
