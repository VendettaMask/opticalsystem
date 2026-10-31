namespace OpticalSystemDesign.Core.Scattering;

public sealed record SpeosPeakImportOptions(double HalfAngleDegrees, double RelativeHeight = 0, double? WhiteReferenceNanometers = null);
public sealed record SpeosPeakCone(bool Transmission, double RotationDegrees, double IncidenceDegrees, double WavelengthNanometers, double HalfAngleDegrees, double RelativeHeight);
internal sealed record SpeosCone(bool Transmission, double Rotation, double Incidence, double Wavelength, double HalfAngleDegrees, double RelativeHeight);
internal static class SpeosPeakBuilder
{
    internal static MeasuredBsdfData Apply(MeasuredBsdfData data, double ni, double nt, double? white, IReadOnlyList<SpeosCone>? cones = null, SpeosPeakImportOptions? manual = null)
    {
        if (data.ZemaxDefinition is not null || cones is null && manual is null) return data;
        var projected = data.StoredQuantity == BsdfStoredQuantity.ProjectedBsdf;
        if (manual is not null && (!double.IsFinite(manual.HalfAngleDegrees) || manual.HalfAngleDegrees is <= 0 or >= 90 || !double.IsFinite(manual.RelativeHeight) || manual.RelativeHeight is < 0 or > 1)) throw new InvalidDataException("镜面锥半角须在 0–90° 内，高度比例须为 0–1。");
        var diffuse = new List<MeasuredBsdfPoint>(); var peak = new List<MeasuredBsdfPoint>();
        foreach (var group in data.Points.GroupBy(p => (p.Side, p.WavelengthNanometers, p.IncidentThetaDegrees, p.IncidentPhiDegrees)))
        {
            Services.ComputationCancellation.ThrowIfCancellationRequested();
            var first = group.First(); var rotation = data.Coordinates == BsdfCoordinates.FixedMaterialAxes ? Wrap(180 - first.IncidentPhiDegrees) : 0;
            var maximumR = group.Max(p => p.BrdfPerSteradian); var maximumT = group.Max(p => p.BtdfPerSteradian);
            double Value(MeasuredBsdfPoint p, bool transmission)
            {
                var value = transmission ? p.BtdfPerSteradian : p.BrdfPerSteradian;
                if (value == 0) return 0;
                var candidates = cones?.Where(c => c.Transmission == transmission && Math.Abs(c.Incidence - first.IncidentThetaDegrees) < 1e-9 && (c.Wavelength == 0 || Math.Abs(c.Wavelength - first.WavelengthNanometers) < 1e-9)).ToArray();
                // Symmetry-expanded rotations inherit the same scalar cone width/height. A split seam needs
                // a declared manual cone; never select an arbitrary cone at an unmeasured input rotation.
                SpeosCone? cone = null;
                if (candidates is { Length: > 0 })
                {
                    var limit = candidates.Max(c => c.Rotation); var a = rotation;
                    if (limit <= 180 && a > 180) a = 360 - a;
                    if (limit == 90 && a > 90) a = 180 - a;
                    if (limit == 0) a = 0;
                    cone = candidates.FirstOrDefault(c => Math.Abs(c.Rotation - a) < 1e-8) ?? throw new InvalidDataException("增强锥缺少展开后的材料旋转节点。");
                }
                if (manual is null && cone is null) throw new InvalidDataException("增强锥缺少入射节点。");
                var angle = manual?.HalfAngleDegrees ?? cone!.HalfAngleDegrees; var height = manual?.RelativeHeight ?? cone!.RelativeHeight;
                var sin = Math.Sin(p.IncidentThetaDegrees * Math.PI / 180) * (transmission ? p.Side == BsdfSide.Front ? ni / nt : nt / ni : 1);
                if (sin >= 1) throw new InvalidDataException("镜面增强锥的透射方向不在有效半球内。");
                var ci = Math.Sqrt(1 - sin * sin); var mu = MeasuredBsdfGrid.Cos(p.OutgoingThetaDegrees);
                var incomingPhi = data.Coordinates == BsdfCoordinates.FixedMaterialAxes ? p.IncidentPhiDegrees : 0;
                var cosine = ci * mu - sin * Math.Sqrt(Math.Max(0, 1 - mu * mu)) * Math.Cos((p.OutgoingPhiDegrees - incomingPhi) * Math.PI / 180);
                if (cosine < Math.Cos(angle * Math.PI / 180)) return 0;
                var max = transmission ? maximumT : maximumR;
                return Math.Max(0, value - max * height);
            }
            foreach (var p in group)
            {
                var r = Value(p, false); var t = Value(p, true);
                peak.Add(p with { BrdfPerSteradian = r, BtdfPerSteradian = t });
                diffuse.Add(p with { BrdfPerSteradian = p.BrdfPerSteradian - r, BtdfPerSteradian = p.BtdfPerSteradian - t });
            }
        }
        if (white is { } wavelength && data.SpeosSpectral is null)
        {
            if (!double.IsFinite(wavelength) || wavelength is < 200 or > 20000) throw new InvalidDataException("White Specular 基准波长无效。");
            var constant = new List<MeasuredBsdfPoint>();
            foreach (var group in peak.GroupBy(p => (p.Side, p.IncidentThetaDegrees, p.IncidentPhiDegrees, p.OutgoingThetaDegrees, p.OutgoingPhiDegrees)))
            {
                var ordered = group.OrderBy(p => p.WavelengthNanometers).ToArray(); var waves = ordered.Select(p => p.WavelengthNanometers).ToArray(); var b = MeasuredBsdfGrid.Bracket(waves, wavelength);
                var r = ordered[b.Low].BrdfPerSteradian * (1 - b.T) + ordered[b.High].BrdfPerSteradian * b.T;
                var t = ordered[b.Low].BtdfPerSteradian * (1 - b.T) + ordered[b.High].BtdfPerSteradian * b.T;
                constant.AddRange(ordered.Select(p => p with { BrdfPerSteradian = r, BtdfPerSteradian = t }));
            }
            peak = constant;
        }
        return new(data.Name, data.SourceFileName, data.SourceSha256, diffuse, coordinates: data.Coordinates, sourceFormat: data.SourceFormat,
            importNotes: data.ImportNotes + "; native cone split at retained nodes: excess above relative stored-quantity height inside half-angle; diffuse remains fixed, peak uses SlopePowerTransport/1 with analytic solid-angle Jacobian; white base nm=" + white?.ToString("G17", System.Globalization.CultureInfo.InvariantCulture),
            storedQuantity: data.StoredQuantity, speosPeak: new(ni, nt, white, peak, ManualCone: manual,
                FileCones: cones?.Select(c => new SpeosPeakCone(c.Transmission, c.Rotation, c.Incidence, c.Wavelength, c.HalfAngleDegrees, c.RelativeHeight)).ToArray()), speosSpectral: data.SpeosSpectral);
    }
    private static double Wrap(double x) => (x % 360 + 360) % 360;
}
