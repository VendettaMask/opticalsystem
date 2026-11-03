using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

public enum LinearEdgeOrientation { NearVertical, NearHorizontal }
public sealed record LinearImageEdgeResult(LinearEdgeOrientation Orientation, SfrRegion Region,
    SlantedEdgeMeasurement Measurement, double InputScale);

/// <summary>Image-only adapter. Cropping/transposition and one common numerical scale do not change SFR.
/// ESF, LSF and contrast are returned in the supplied linear units; original irradiance is never overwritten.</summary>
public static class LinearImageSfr
{
    public const string Algorithm = "LinearImageEdge/1; cropped; orientation transpose; relative conditioning; " + SlantedEdgeSfr.Algorithm;
    public static LinearImageEdgeResult Measure(IReadOnlyList<double> pixels, int width, int height, SfrRegion roi,
        double pixelPitchMicrometers, LinearEdgeOrientation orientation = LinearEdgeOrientation.NearVertical, int halfWindowPixels = 24,
        Action<SlantedEdgeFit>? fitted = null)
    {
        ArgumentNullException.ThrowIfNull(pixels); ArgumentNullException.ThrowIfNull(roi);
        if (width is < 4 or > 1024 || height is < 4 or > 1024 || (long)width * height > 262144
            || pixels.Count != width * height || roi.X < 0 || roi.Y < 0 || roi.Width is < 32 or > 256 || roi.Height is < 32 or > 256
            || (long)roi.X + roi.Width > width || (long)roi.Y + roi.Height > height || !Enum.IsDefined(orientation))
            throw new ArgumentException("Select one 32–256 pixel ROI inside the complete linear image.");
        var transpose = orientation == LinearEdgeOrientation.NearHorizontal;
        var w = transpose ? roi.Height : roi.Width; var h = transpose ? roi.Width : roi.Height;
        var values = new double[w * h]; double scale = 0;
        for (var y = 0; y < h; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < w; x++)
            {
                var value = pixels[(roi.Y + (transpose ? x : y)) * width + roi.X + (transpose ? y : x)];
                if (!double.IsFinite(value) || value < 0) throw new ArgumentException("Linear pixels must be finite and nonnegative.");
                values[y * w + x] = value; scale = Math.Max(scale, value);
            }
        }
        if (scale <= 0) throw new InvalidOperationException("ROI contains no light.");
        var measured = SlantedEdgeSfr.Measure(values.Select(v => v / scale).ToArray(), w, h,
            new(0, 0, w, h), pixelPitchMicrometers, halfWindowPixels, fit => fitted?.Invoke(fit with
            {
                EdgeIntercept = fit.EdgeIntercept + (transpose ? roi.Y : roi.X) - fit.EdgeSlope * (transpose ? roi.X : roi.Y),
                Contrast = fit.Contrast * scale
            }));
        // Fit uses x(y) in the selected orientation; global intercept remains in that oriented raster.
        measured = measured with
        {
            EdgeIntercept = measured.EdgeIntercept + (transpose ? roi.Y : roi.X) - measured.EdgeSlope * (transpose ? roi.X : roi.Y),
            Contrast = measured.Contrast * scale,
            Esf = Array.AsReadOnly(measured.Esf.Select(p => p with { Value = p.Value * scale }).ToArray()),
            Lsf = Array.AsReadOnly(measured.Lsf.Select(p => p with { Value = p.Value * scale }).ToArray())
        };
        return new(orientation, roi, measured, scale);
    }
}
