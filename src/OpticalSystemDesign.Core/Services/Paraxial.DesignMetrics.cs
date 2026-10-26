using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Core.Services;

public sealed partial class Paraxial
{
    /// <summary>Infinite-object chief image height per unit input slope at the current image plane, aimed through the stop center.</summary>
    public double EstimateChiefRayImageScale()
    {
        EnsureComputable();
        if (!ObjectConjugate.IsInfinite(_optic.SurfaceGroup.Items[0]))
            throw new NotSupportedException("The chief image slope scale requires an infinite object.");
        var stop = _optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        if (stop <= 0) throw new InvalidOperationException("A physical stop is required.");
        var before = TraceMatrix(0, stop, PrimaryWavelengthNanometers());
        var image = TraceMatrix(0, _optic.SurfaceGroup.Items.Count, PrimaryWavelengthNanometers(), includeTrailingTranslation: false);
        if (!double.IsFinite(before.A) || Math.Abs(before.A) <= 1e-15)
            throw new ArithmeticException("The chief image slope scale is singular at this stop.");
        var result = image.B - image.A * before.B / before.A;
        return double.IsFinite(result) ? result : throw new ArithmeticException("The chief image scale is non-finite.");
    }
}
