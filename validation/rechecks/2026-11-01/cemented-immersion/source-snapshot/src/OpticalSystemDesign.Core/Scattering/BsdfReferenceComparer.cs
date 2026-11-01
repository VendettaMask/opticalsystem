using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Scattering;

public enum BsdfReferenceOrigin { SyntheticControl, CommercialNativeExport }
public sealed record BsdfReferenceIdentity(BsdfReferenceOrigin Origin, string Engine, string Version, string ExecutableSha256,
    string ReferenceFileSha256, string SourceFileSha256, string ModelDataSha256, string CoordinateProtocol = "AwayDirectionsMaterialFrameDegrees/1");
public sealed record BsdfReferenceValue(BsdfSide Side, double WavelengthNanometers, double IncidentThetaDegrees, double IncidentPhiDegrees,
    double OutgoingThetaDegrees, double OutgoingPhiDegrees, double IncidentIndex, double TransmittedIndex, double ValuePerSteradian, double Reflection, double Transmission);
public sealed record BsdfReferenceTolerance(double ValueAbsolute, double ValueRelative, double EnergyAbsolute, double EnergyRelative);
public sealed record BsdfReferenceDifference(int Index, double ActualValue, double ReferenceValue, double ValueError, double ValueAllowance,
    double ReflectionError, double TransmissionError, bool Passed);
public sealed record BsdfReferenceComparison(BsdfReferenceIdentity Identity, BsdfReferenceTolerance Tolerance, IReadOnlyList<BsdfReferenceDifference> Points)
{
    public bool NumericalComparisonPassed => Points.Count > 0 && Points.All(p => p.Passed);
    public bool CommercialComparisonPassed => Identity.Origin == BsdfReferenceOrigin.CommercialNativeExport && NumericalComparisonPassed;
}

/// <summary>Pointwise external-value audit. Requires matching frozen inputs and an explicit source
/// tolerance. Does not fit, rescale, offset or substitute reference data. Native sampling PDF is
/// not an equality requirement: scattering values and integrated powers are compared instead.</summary>
public static class BsdfReferenceComparer
{
    public static BsdfReferenceComparison Compare(MeasuredBsdfData data, BsdfReferenceIdentity identity, IReadOnlyList<BsdfReferenceValue> points,
        BsdfReferenceTolerance tolerance, CancellationToken cancellationToken = default)
    {
        static bool Sha(string s) => s is { Length: 64 } && s.All(Uri.IsHexDigit);
        if (data is null || identity is null || !Enum.IsDefined(identity.Origin) || string.IsNullOrWhiteSpace(identity.Engine) || identity.Engine.Length > 256 || string.IsNullOrWhiteSpace(identity.Version) || identity.Version.Length > 256
            || !Sha(identity.ReferenceFileSha256) || !Sha(identity.SourceFileSha256) || !Sha(identity.ModelDataSha256) || identity.Origin == BsdfReferenceOrigin.CommercialNativeExport && !Sha(identity.ExecutableSha256)
            || identity.CoordinateProtocol != "AwayDirectionsMaterialFrameDegrees/1" || !identity.SourceFileSha256.Equals(data.SourceSha256, StringComparison.OrdinalIgnoreCase) || !identity.ModelDataSha256.Equals(data.DataSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("商业 BSDF 参考缺少精确输入、版本、方向约定或原件身份。");
        if (tolerance is null || new[] { tolerance.ValueAbsolute, tolerance.ValueRelative, tolerance.EnergyAbsolute, tolerance.EnergyRelative }.Any(v => !double.IsFinite(v) || v < 0)
            || points is null || points.Count is < 1 or > 65536 || points.Any(p => p is null || !Enum.IsDefined(p.Side)
                || !double.IsFinite(p.WavelengthNanometers) || p.WavelengthNanometers is < 200 or > 20000 || !double.IsFinite(p.IncidentThetaDegrees) || p.IncidentThetaDegrees is < 0 or > 90
                || !double.IsFinite(p.IncidentPhiDegrees) || p.IncidentPhiDegrees is < 0 or > 360 || !double.IsFinite(p.OutgoingThetaDegrees) || p.OutgoingThetaDegrees is < 0 or > 180
                || !double.IsFinite(p.OutgoingPhiDegrees) || p.OutgoingPhiDegrees is < 0 or > 360 || !double.IsFinite(p.IncidentIndex) || p.IncidentIndex <= 0 || !double.IsFinite(p.TransmittedIndex) || p.TransmittedIndex <= 0
                || !double.IsFinite(p.ValuePerSteradian) || p.ValuePerSteradian < 0 || !double.IsFinite(p.Reflection) || p.Reflection < 0 || !double.IsFinite(p.Transmission) || p.Transmission < 0 || p.Reflection + p.Transmission > 1 + 32 * 2.2204460492503131e-16))
            throw new InvalidDataException("BSDF 参考值或显式容差无效。");
        var frozen = points.ToArray(); var results = new List<BsdfReferenceDifference>(); var model = new MeasuredBsdf(data);
        if (frozen.Select(p => (p.Side, p.WavelengthNanometers, p.IncidentThetaDegrees, p.IncidentPhiDegrees, p.OutgoingThetaDegrees, p.OutgoingPhiDegrees, p.IncidentIndex, p.TransmittedIndex)).Distinct().Count() != frozen.Length)
            throw new InvalidDataException("BSDF 逐值参考包含重复输入点；重复捕获须作为独立参考批次。");
        static Vector3D Direction(double theta, double phi) { var t = theta * Math.PI / 180; var p = phi * Math.PI / 180; return new(Math.Sin(t) * Math.Cos(p), Math.Sin(t) * Math.Sin(p), MeasuredBsdfGrid.Cos(theta)); }
        for (var i = 0; i < frozen.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var p = frozen[i]; var c = new BsdfContext(Direction(p.IncidentThetaDegrees, p.IncidentPhiDegrees), new(0, 0, 1), p.WavelengthNanometers, p.Side, new(1, 0, 0), p.IncidentIndex, p.TransmittedIndex);
            var actual = model.Evaluate(c, Direction(p.OutgoingThetaDegrees, p.OutgoingPhiDegrees)); var e = model.Integrate(c);
            var difference = Math.Abs(actual - p.ValuePerSteradian); var allowance = tolerance.ValueAbsolute + tolerance.ValueRelative * Math.Abs(p.ValuePerSteradian);
            var re = Math.Abs(e.Reflection - p.Reflection); var te = Math.Abs(e.Transmission - p.Transmission);
            var passed = double.IsFinite(actual) && difference <= allowance && re <= tolerance.EnergyAbsolute + tolerance.EnergyRelative * p.Reflection && te <= tolerance.EnergyAbsolute + tolerance.EnergyRelative * p.Transmission;
            results.Add(new(i, actual, p.ValuePerSteradian, difference, allowance, re, te, passed));
        }
        return new(identity, tolerance, Array.AsReadOnly(results.ToArray()));
    }
}
