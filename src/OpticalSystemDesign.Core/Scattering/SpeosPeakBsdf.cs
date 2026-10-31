using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Scattering;

/// <summary>Registered peak is an independent power distribution. Heights use the original stored quantity.</summary>
public sealed record SpeosPeakDefinition(double IncidentIndex, double TransmittedIndex, double? WhiteReferenceNanometers,
    IReadOnlyList<MeasuredBsdfPoint> Points, string Registration = "SlopePowerTransport/1",
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SpeosPeakImportOptions? ManualCone = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SpeosPeakCone>? FileCones = null)
{
    internal SpeosPeakDefinition Freeze()
    {
        if (!double.IsFinite(IncidentIndex) || IncidentIndex <= 0 || !double.IsFinite(TransmittedIndex) || TransmittedIndex <= 0
            || WhiteReferenceNanometers is { } w && (!double.IsFinite(w) || w is < 200 or > 20000)
            || Registration != "SlopePowerTransport/1" || Points is null || Points.Count is < 1 or > MeasuredBsdfData.MaximumPoints || Points.Any(p => p is null))
            throw new InvalidDataException("Speos 镜面增强载荷无效。");
        if (ManualCone is { } m && (!double.IsFinite(m.HalfAngleDegrees) || m.HalfAngleDegrees is <= 0 or >= 90 || !double.IsFinite(m.RelativeHeight) || m.RelativeHeight is < 0 or > 1)
            || FileCones is { } cones && (cones.Count > 6400 || cones.Any(c => c is null || !double.IsFinite(c.RotationDegrees) || c.RotationDegrees is < 0 or > 360 || !double.IsFinite(c.IncidenceDegrees) || c.IncidenceDegrees is < 0 or > 90 || !double.IsFinite(c.WavelengthNanometers) || c.WavelengthNanometers is < 0 or > 20000 || !double.IsFinite(c.HalfAngleDegrees) || c.HalfAngleDegrees is < 0 or > 90 || !double.IsFinite(c.RelativeHeight) || c.RelativeHeight < 0))) throw new InvalidDataException("Speos 锥参数载荷无效。");
        return this with { FileCones = FileCones is null ? null : Array.AsReadOnly(FileCones.ToArray()), Points = Array.AsReadOnly(Points.OrderBy(p => p.Side).ThenBy(p => p.WavelengthNanometers).ThenBy(p => p.IncidentThetaDegrees).ThenBy(p => p.IncidentPhiDegrees).ThenBy(p => p.OutgoingThetaDegrees).ThenBy(p => p.OutgoingPhiDegrees).ToArray()) };
    }
}

/// <summary>Diffuse part stays in the material frame. Peak power is pushed forward by a bijection of
/// hemisphere slopes. dOmega = mu^3 d(slopeX)d(slopeY); the Jacobian is included in Evaluate/Pdf.
/// This declared native registration conserves power without horizon clipping or rejection sampling.</summary>
internal sealed class SpeosPeakDistribution : IMeasuredBsdfDistribution
{
    private sealed record Part(BsdfSide Side, double Wave, double Cosine, double Phi, MeasuredBsdfGrid.Slice Shape);
    private readonly MeasuredBsdfGrid _diffuse;
    private readonly SpeosPeakDefinition _definition;
    private readonly bool _fixed, _projected;
    private readonly double[] _waves, _cosines, _phis;
    private readonly Part[] _parts;
    private readonly Dictionary<(BsdfSide Side, double Wave, double Cosine, double Phi), Part> _lookup;
    public bool HasTransmission { get; }
    public bool HasBothSides => _diffuse.HasBothSides;
    internal SpeosPeakDistribution(IReadOnlyList<MeasuredBsdfPoint> diffuse, BsdfCoordinates coordinates, BsdfStoredQuantity quantity, SpeosPeakDefinition definition)
    {
        _definition = definition; _fixed = coordinates == BsdfCoordinates.FixedMaterialAxes; _projected = quantity == BsdfStoredQuantity.ProjectedBsdf;
        _diffuse = new(diffuse, coordinates, true, _projected);
        // Validate identical axes, poles, seams and passive peak grid before constructing components.
        _ = new MeasuredBsdfGrid(definition.Points, coordinates, true, _projected);
        static object Key(MeasuredBsdfPoint p) => (p.Side, p.WavelengthNanometers, p.IncidentThetaDegrees, p.IncidentPhiDegrees, p.OutgoingThetaDegrees, p.OutgoingPhiDegrees);
        if (!diffuse.Select(Key).ToHashSet().SetEquals(definition.Points.Select(Key))) throw new InvalidDataException("Speos 峰/背景网格不匹配。");
        _waves = diffuse.Select(p => p.WavelengthNanometers).Distinct().Order().ToArray();
        _cosines = diffuse.Select(p => MeasuredBsdfGrid.Cos(p.IncidentThetaDegrees)).Distinct().Order().ToArray();
        _phis = diffuse.Select(p => p.IncidentPhiDegrees).Distinct().Order().ToArray();
        _parts = definition.Points.GroupBy(p => (p.Side, p.WavelengthNanometers, p.IncidentThetaDegrees, p.IncidentPhiDegrees))
            .Select(g => new Part(g.Key.Side, g.Key.WavelengthNanometers, MeasuredBsdfGrid.Cos(g.Key.IncidentThetaDegrees), g.Key.IncidentPhiDegrees, new(g.ToArray(), false, _projected))).ToArray();
        _lookup = _parts.ToDictionary(p => (p.Side, p.Wave, p.Cosine, p.Phi));
        HasTransmission = _diffuse.HasTransmission || _parts.Any(p => p.Shape.T > 0);
        foreach (var p in _parts)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (p.Cosine == 0 && p.Shape.R + p.Shape.T > 0) throw new InvalidDataException("当前镜面配准要求非掠入射峰；90° 可使用无增强数据。");
            var frameAngle = p.Phi * Math.PI / 180;
            var incoming = new Vector3D(Math.Sqrt(Math.Max(0, 1 - p.Cosine * p.Cosine)) * Math.Cos(frameAngle), Math.Sqrt(Math.Max(0, 1 - p.Cosine * p.Cosine)) * Math.Sin(frameAngle), p.Cosine);
            var c = new BsdfContext(incoming, new(0, 0, 1), p.Wave, p.Side, _fixed ? new(1, 0, 0) : null);
            var d = _diffuse.Integrate(c);
            if (d.Total + p.Shape.R + p.Shape.T > 1 + 32 * 2.2204460492503131e-16) throw new InvalidDataException("White Specular/镜面增强后的 R+T 超过 1；不自动缩放。");
            if (p.Shape.T > 0) _ = SourceSlope(p, true);
        }
    }
    private (Vector3D X, Vector3D Y) Frame(BsdfContext c)
    {
        var x = _fixed ? c.MaterialTangent ?? throw new InvalidDataException("增强模型需要固定材料轴。") : c.IncomingDirection - c.SurfaceNormal * BsdfContext.Dot(c.IncomingDirection, c.SurfaceNormal);
        if (x.Length < 1e-12) x = BsdfContext.Cross(Math.Abs(c.SurfaceNormal.Z) < .9 ? new(0, 0, 1) : new Vector3D(0, 1, 0), c.SurfaceNormal);
        x = BsdfContext.Unit(x); return (x, BsdfContext.Cross(c.SurfaceNormal, x));
    }
    private static IEnumerable<(double Value, double Weight)> Terms(double[] a, double x)
    {
        var b = MeasuredBsdfGrid.Bracket(a, x);
        if (b.Low == b.High) { yield return (a[b.Low], 1); yield break; }
        if (b.T < 1) yield return (a[b.Low], 1 - b.T); if (b.T > 0) yield return (a[b.High], b.T);
    }
    private (Part Part, double Weight)[] Inputs(BsdfContext c)
    {
        var f = Frame(c); var phi = _fixed ? Math.Atan2(BsdfContext.Dot(c.IncomingDirection, f.Y), BsdfContext.Dot(c.IncomingDirection, f.X)) * 180 / Math.PI : 0;
        if (phi < 0) phi += 360;
        var list = new List<(Part, double)>();
        foreach (var w in Terms(_waves, c.WavelengthNanometers)) foreach (var i in Terms(_cosines, Math.Clamp(BsdfContext.Dot(c.IncomingDirection, c.SurfaceNormal), 0, 1))) foreach (var p in Terms(_phis, phi))
        {
            if (!_lookup.TryGetValue((c.Side, w.Value, i.Value, p.Value), out var part)) throw new InvalidDataException("Speos 峰缺少入射侧。");
            list.Add((part, w.Weight * i.Weight * p.Weight));
        }
        return list.ToArray();
    }
    public BsdfEnergy Integrate(BsdfContext c)
    { var d = _diffuse.Integrate(c); var parts = Inputs(c); return new(d.Reflection + parts.Sum(p => p.Weight * p.Part.Shape.R), d.Transmission + parts.Sum(p => p.Weight * p.Part.Shape.T)); }
    private double Ratio(BsdfSide side) => side == BsdfSide.Front ? _definition.IncidentIndex / _definition.TransmittedIndex : _definition.TransmittedIndex / _definition.IncidentIndex;
    private (double X, double Y) SourceSlope(Part p, bool transmission)
    {
        var sin = Math.Sqrt(Math.Max(0, 1 - p.Cosine * p.Cosine)) * (transmission ? Ratio(p.Side) : 1);
        if (sin >= 1) throw new InvalidDataException("Speos 增强透射峰落在全反射/掠射范围。");
        var slope = -sin / Math.Sqrt(1 - sin * sin); var phi = p.Phi * Math.PI / 180;
        return (slope * Math.Cos(phi), slope * Math.Sin(phi));
    }
    private (double X, double Y) Shift(BsdfContext c, Part p, bool transmission)
    {
        var frame = Frame(c); var ratio = transmission ? Ratio(c.Side) : 1;
        if (transmission && Math.Abs(c.IncidentRefractiveIndex / c.TransmittedRefractiveIndex - ratio) > 1e-10 * ratio) throw new InvalidDataException("Speos 增强透射的实际折射率与声明不匹配。");
        var x = -BsdfContext.Dot(c.IncomingDirection, frame.X) * ratio; var y = -BsdfContext.Dot(c.IncomingDirection, frame.Y) * ratio; var square = x * x + y * y;
        if (square >= 1) throw new InvalidDataException("Speos 镜面配准不适用于全反射/精确掠射。");
        var mu = Math.Sqrt(1 - square); var source = SourceSlope(p, transmission); return (x / mu - source.X, y / mu - source.Y);
    }
    public double Evaluate(BsdfContext c, Vector3D direction)
    {
        var value = _diffuse.Evaluate(c, direction); var d = BsdfContext.Unit(direction); var mu = BsdfContext.Dot(c.SurfaceNormal, d); if (mu == 0) return 0;
        var transmission = mu < 0; mu = Math.Abs(mu); var frame = Frame(c); var x = BsdfContext.Dot(d, frame.X) / mu; var y = BsdfContext.Dot(d, frame.Y) / mu;
        foreach (var (p, weight) in Inputs(c))
        {
            if ((transmission ? p.Shape.T : p.Shape.R) == 0) continue;
            var delta = Shift(c, p, transmission); var sx = x - delta.X; var sy = y - delta.Y; var smu = 1 / Math.Sqrt(1 + sx * sx + sy * sy); var phi = Math.Atan2(sy, sx); if (phi < 0) phi += 2 * Math.PI;
            var q = p.Shape.Eval(transmission, smu, phi) * (_projected ? 1 : smu);
            value += weight * q * Math.Pow(smu / mu, 3) / mu;
        }
        return value;
    }
    public BsdfSample Sample(MeasuredBsdf model, BsdfContext c, double u1, double u2)
    {
        if (!double.IsFinite(u1) || !double.IsFinite(u2) || u1 is < 0 or >= 1 || u2 is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(u1));
        var diffuse = _diffuse.Integrate(c); var choices = Inputs(c).SelectMany(p => new[] { (p.Part, p.Weight, T: false, Mass: p.Weight * p.Part.Shape.R), (p.Part, p.Weight, T: true, Mass: p.Weight * p.Part.Shape.T) }).Where(p => p.Mass > 0).ToArray();
        var total = diffuse.Total + choices.Sum(p => p.Mass); if (total == 0) return new LambertianReflectionBsdf(0).Sample(c, u1, u2);
        var target = Math.Clamp(u1, 5.551115123125783e-17, Math.BitDecrement(1d)) * total;
        Vector3D direction; bool transmit;
        if (target < diffuse.Total)
        {
            var s = _diffuse.Sample(model, c, target / diffuse.Total, u2); direction = s.Direction; transmit = s.Lobe == BsdfLobe.DiffuseTransmission;
        }
        else
        {
            target -= diffuse.Total; var selected = choices[^1];
            foreach (var p in choices) { selected = p; if (target < p.Mass) break; target -= p.Mass; }
            var (mu, phi) = selected.Part.Shape.Sample(selected.T, Math.Clamp(target / selected.Mass, 5.551115123125783e-17, Math.BitDecrement(1d)), Math.Clamp(u2, 5.551115123125783e-17, Math.BitDecrement(1d)));
            var r = Math.Sqrt(Math.Max(0, 1 - mu * mu)); var delta = Shift(c, selected.Part, selected.T); var f = Frame(c);
            direction = BsdfContext.Unit(f.X * (r * Math.Cos(phi) / mu + delta.X) + f.Y * (r * Math.Sin(phi) / mu + delta.Y) + c.SurfaceNormal * (selected.T ? -1 : 1)); transmit = selected.T;
        }
        return new(direction, model.Evaluate(c, direction), model.Pdf(c, direction), transmit ? BsdfLobe.DiffuseTransmission : BsdfLobe.DiffuseReflection);
    }
}
