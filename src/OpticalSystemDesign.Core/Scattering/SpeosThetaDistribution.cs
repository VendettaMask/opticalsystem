using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Scattering;

/// <summary>Theta-linear tables and node-local White Specular power allocation.
/// Peak motion between measured inputs retains the explicitly declared SlopePowerTransport/1 model.</summary>
internal sealed class SpeosThetaDistribution : IMeasuredBsdfDistribution
{
    private sealed record Part(BsdfSide Side, double Theta, double Phi, SpeosThetaSlice Diffuse, SpeosThetaSlice? Peak);
    private readonly Part[] _parts;
    private readonly double[] _theta, _phi;
    private readonly SpeosSpectralDefinition _spectrum;
    private readonly SpeosPeakDefinition? _peak;
    private readonly bool _fixed, _projected;
    public bool HasTransmission => _parts.Any(p => p.Diffuse.T + (p.Peak?.T ?? 0) > 0);
    public bool HasBothSides => _parts.Select(p => p.Side).Distinct().Count() == 2;

    internal SpeosThetaDistribution(IReadOnlyList<MeasuredBsdfPoint> points, BsdfCoordinates coordinates,
        BsdfStoredQuantity quantity, SpeosSpectralDefinition spectrum, SpeosPeakDefinition? peak)
    {
        _spectrum = spectrum; _peak = peak; _fixed = coordinates == BsdfCoordinates.FixedMaterialAxes;
        _projected = quantity == BsdfStoredQuantity.ProjectedBsdf;
        if (points.Select(p => p.WavelengthNanometers).Distinct().Count() != 1
            || points.Any(p => !Enum.IsDefined(p.Side) || !double.IsFinite(p.WavelengthNanometers) || p.WavelengthNanometers is < 200 or > 20000
                || !double.IsFinite(p.IncidentThetaDegrees) || p.IncidentThetaDegrees is < 0 or > 90
                || !double.IsFinite(p.IncidentPhiDegrees) || p.IncidentPhiDegrees is < 0 or > 360 || !_fixed && p.IncidentPhiDegrees != 0
                || !double.IsFinite(p.OutgoingThetaDegrees) || p.OutgoingThetaDegrees is < 0 or > 90
                || !double.IsFinite(p.OutgoingPhiDegrees) || p.OutgoingPhiDegrees is < 0 or > 360))
            throw new InvalidDataException("Invalid compact Speos theta input axes.");
        _theta = points.Select(p => p.IncidentThetaDegrees).Distinct().Order().ToArray();
        _phi = points.Select(p => p.IncidentPhiDegrees).Distinct().Order().ToArray();
        if (_theta.Length > 64 || _phi.Length > 65 || _fixed && (_phi.Length < 2 || _phi[0] != 0 || _phi[^1] != 360))
            throw new InvalidDataException("Speos input axes exceed limits or omit the periodic seam.");
        var peaks = peak?.Points.GroupBy(p => (p.Side, p.IncidentThetaDegrees, p.IncidentPhiDegrees)).ToDictionary(g => g.Key, g => g.ToArray());
        _parts = points.GroupBy(p => (p.Side, p.IncidentThetaDegrees, p.IncidentPhiDegrees)).Select(g =>
        {
            Services.ComputationCancellation.ThrowIfCancellationRequested();
            var nodes = g.ToArray(); SpeosThetaSlice? shape = null;
            if (peaks is not null)
            {
                if (!peaks.TryGetValue(g.Key, out var pn) || !nodes.Select(Key).ToHashSet().SetEquals(pn.Select(Key)))
                    throw new InvalidDataException("Speos peak/background grids differ.");
                shape = new(pn, _projected, !_fixed && g.Key.IncidentThetaDegrees == 0);
            }
            return new Part(g.Key.Side, g.Key.IncidentThetaDegrees, g.Key.IncidentPhiDegrees, new(nodes, _projected, !_fixed && g.Key.IncidentThetaDegrees == 0), shape);
        }).ToArray();
        if (_parts.Length != _theta.Length * _phi.Length * (HasBothSides ? 2 : 1) || peaks is not null && peaks.Count != _parts.Length)
            throw new InvalidDataException("Speos input grid has missing slices.");
        foreach (var values in peak is null ? new[] { points } : new[] { points, peak.Points })
        foreach (var group in values.GroupBy(p => (p.Side, p.IncidentThetaDegrees)))
        {
            var first = group.Where(p => p.IncidentPhiDegrees == _phi[0]).ToArray();
            var last = group.Where(p => p.IncidentPhiDegrees == _phi[^1]).ToArray();
            var a = new SpeosThetaSlice(first, _projected); var b = new SpeosThetaSlice(last, _projected);
            foreach (var p in group) foreach (var t in new[] { false, true })
                if (!MeasuredBsdfGrid.Close(a.Eval(t, MeasuredBsdfGrid.Cos(p.OutgoingThetaDegrees), p.OutgoingPhiDegrees * Math.PI / 180), b.Eval(t, MeasuredBsdfGrid.Cos(p.OutgoingThetaDegrees), p.OutgoingPhiDegrees * Math.PI / 180)))
                    throw new InvalidDataException("Speos input seam differs.");
            if (group.Key.IncidentThetaDegrees == 0)
                foreach (var rotation in group.GroupBy(p => p.IncidentPhiDegrees))
                {
                    var normal = new SpeosThetaSlice(rotation.ToArray(), _projected);
                    foreach (var p in group) foreach (var t in new[] { false, true })
                        if (!MeasuredBsdfGrid.Close(a.Eval(t, MeasuredBsdfGrid.Cos(p.OutgoingThetaDegrees), p.OutgoingPhiDegrees * Math.PI / 180), normal.Eval(t, MeasuredBsdfGrid.Cos(p.OutgoingThetaDegrees), p.OutgoingPhiDegrees * Math.PI / 180)))
                            throw new InvalidDataException("Speos normal-incidence input rotations differ.");
                }
        }
        var low = spectrum.Lobes.Max(l => l.Wavelengths[0]); var high = spectrum.Lobes.Min(l => l.Wavelengths[^1]);
        if (low > high) throw new InvalidDataException("Speos R/T spectra have no common wavelength range.");
        foreach (var part in _parts) foreach (var wave in spectrum.Lobes.SelectMany(l => l.Wavelengths).Append(low).Append(high).Where(w => w >= low && w <= high).Distinct())
        {
            var e = Mass(part, false, wave) + Mass(part, true, wave);
            if (!double.IsFinite(e) || e > 1 + 32 * 2.2204460492503131e-16) throw new InvalidDataException("Speos theta/White Specular R+T exceeds one; no rescaling.");
            if (part.Peak is { } s && s.R + s.T > 0 && part.Theta == 90) throw new InvalidDataException("SlopePowerTransport requires a non-grazing measured peak.");
            if (part.Peak?.T > 0 && Math.Sin(part.Theta * Math.PI / 180) * (part.Side == BsdfSide.Front ? peak!.IncidentIndex / peak.TransmittedIndex : peak!.TransmittedIndex / peak.IncidentIndex) >= 1)
                throw new InvalidDataException("Measured Speos transmission peak is outside the declared refracting hemisphere.");
        }
        if (spectrum.RuntimeWhite) ValidateRuntimePower(low, high);
    }
    private static object Key(MeasuredBsdfPoint p) => (p.Side, p.WavelengthNanometers, p.IncidentThetaDegrees, p.IncidentPhiDegrees, p.OutgoingThetaDegrees, p.OutgoingPhiDegrees);
    private (double Diffuse, double Peak) Scales(Part part, bool transmission, double wave)
    {
        var d = transmission ? part.Diffuse.T : part.Diffuse.R;
        var s = transmission ? part.Peak?.T ?? 0 : part.Peak?.R ?? 0;
        var spectral = _spectrum.Scale(transmission, wave);
        if (_peak?.WhiteReferenceNanometers is not { } white) return (spectral, spectral);
        var peakScale = _spectrum.Scale(transmission, white);
        var remainder = Math.Max(0, (d + s) * spectral - s * peakScale);
        return (d > 0 ? remainder / d : 0, peakScale);
    }
    private double Mass(Part p, bool t, double wave)
    {
        var scales = Scales(p, t, wave);
        return scales.Diffuse * (t ? p.Diffuse.T : p.Diffuse.R) + scales.Peak * (t ? p.Peak?.T ?? 0 : p.Peak?.R ?? 0);
    }
    private static double ScaledMass(Part p, bool t, (double Diffuse, double Peak) scales) =>
        scales.Diffuse * (t ? p.Diffuse.T : p.Diffuse.R) + scales.Peak * (t ? p.Peak?.T ?? 0 : p.Peak?.R ?? 0);
    private static double Wrap(double angle) => (angle % 360 + 360) % 360;
    // First upper endpoint, including an exact interior measurement. At the first measurement
    // a two-node cell still has its second measurement as the scale reference.
    private static double Upper(double[] axis, double value)
    {
        if (axis.Length == 1) return axis[0];
        for (var i = 1; i < axis.Length; i++) if (value <= axis[i]) return axis[i];
        return axis[^1];
    }
    private Part RuntimeReference(BsdfContext c)
    {
        var theta = Math.Acos(Math.Clamp(BsdfContext.Dot(c.IncomingDirection, c.SurfaceNormal), 0, 1)) * 180 / Math.PI;
        var phi = 0d;
        if (_fixed)
        {
            var frame = Frame(c);
            var incoming = Wrap(Math.Atan2(BsdfContext.Dot(c.IncomingDirection, frame.Y), BsdfContext.Dot(c.IncomingDirection, frame.X)) * 180 / Math.PI);
            var rotation = Wrap(180 - incoming);
            var rotations = _peak?.FileCones?.Select(p => p.RotationDegrees).Distinct().Order().ToArray();
            if (rotations is not { Length: > 0 }) rotations = _phi.Select(p => Wrap(180 - p)).Append(360).Distinct().Order().ToArray();
            var end = rotations[^1];
            if (end <= 180 && rotation > 180) rotation = 360 - rotation;
            if (end == 90 && rotation > 90) rotation = 180 - rotation;
            phi = Wrap(180 - Upper(rotations, rotation));
        }
        var upper = Upper(_theta, Math.Clamp(theta, _theta[0], _theta[^1]));
        return _parts.FirstOrDefault(p => p.Side == c.Side && p.Theta == upper && Math.Abs(p.Phi - phi) < 1e-8)
            ?? throw new InvalidDataException("Runtime White 的参考入射节点不存在。");
    }
    private void ValidateRuntimePower(double low, double high)
    {
        static IEnumerable<(double Low, double High)> Cells(double[] axis) => axis.Length == 1
            ? new[] { (axis[0], axis[0]) } : axis.Zip(axis.Skip(1), (a, b) => (a, b));
        var waves = _spectrum.Lobes.SelectMany(l => l.Wavelengths).Append(low).Append(high).Where(w => w >= low && w <= high).Distinct().ToArray();
        foreach (var side in _parts.Select(p => p.Side).Distinct())
        foreach (var cell in Cells(_theta)) foreach (var azimuth in Cells(_phi))
        {
            Services.ComputationCancellation.ThrowIfCancellationRequested();
            var theta = (cell.Low + cell.High) * Math.PI / 360;
            var phi = (azimuth.Low + azimuth.High) * Math.PI / 360;
            var reference = RuntimeReference(new(new(Math.Sin(theta) * Math.Cos(phi), Math.Sin(theta) * Math.Sin(phi), Math.Cos(theta)), new(0, 0, 1), low, side, new(1, 0, 0)));
            var corners = _parts.Where(p => p.Side == side && (p.Theta == cell.Low || p.Theta == cell.High) && (p.Phi == azimuth.Low || p.Phi == azimuth.High)).ToArray();
            if (corners.Any(p => reference.Diffuse.R == 0 && p.Diffuse.R > 0 || reference.Diffuse.T == 0 && p.Diffuse.T > 0))
                throw new InvalidDataException("Runtime White 的漫反射参考为零而相邻节点非零；不能推断共用缩放。");
            foreach (var wave in waves)
            {
                var r = Scales(reference, false, wave); var t = Scales(reference, true, wave);
                foreach (var part in corners)
                {
                    var energy = ScaledMass(part, false, r) + ScaledMass(part, true, t);
                    if (!double.IsFinite(energy) || energy > 1 + 32 * 2.2204460492503131e-16)
                        throw new InvalidDataException("Runtime White 在插值单元角点产生 R+T>1；不缩放掩盖非被动数据。");
                }
            }
        }
    }
    private (Vector3D X, Vector3D Y) Frame(BsdfContext c)
    {
        var x = _fixed ? c.MaterialTangent ?? throw new InvalidDataException("Fixed Speos axes need a material tangent.") : c.IncomingDirection - c.SurfaceNormal * BsdfContext.Dot(c.IncomingDirection, c.SurfaceNormal);
        if (x.Length < 1e-12) x = BsdfContext.Cross(Math.Abs(c.SurfaceNormal.Z) < .9 ? new(0, 0, 1) : new Vector3D(0, 1, 0), c.SurfaceNormal);
        x = BsdfContext.Unit(x); return (x, BsdfContext.Cross(c.SurfaceNormal, x));
    }
    private IEnumerable<(Part Part, double Weight)> Inputs(BsdfContext c)
    {
        var f = Frame(c); var phi = _fixed ? Math.Atan2(BsdfContext.Dot(c.IncomingDirection, f.Y), BsdfContext.Dot(c.IncomingDirection, f.X)) * 180 / Math.PI : 0;
        if (phi < 0) phi += 360;
        var theta = Math.Acos(Math.Clamp(BsdfContext.Dot(c.IncomingDirection, c.SurfaceNormal), 0, 1)) * 180 / Math.PI;
        var b = MeasuredBsdfGrid.Bracket(_theta, Math.Clamp(theta, _theta[0], _theta[^1])); var a = MeasuredBsdfGrid.Bracket(_phi, phi);
        double Weight(double[] axis, double value, (int Low, int High, double T) v) => value == axis[v.Low] ? v.Low == v.High ? 1 : 1 - v.T : value == axis[v.High] ? v.T : 0;
        var found = false;
        foreach (var p in _parts) if (p.Side == c.Side)
        {
            var w = Weight(_theta, p.Theta, b) * Weight(_phi, p.Phi, a);
            if (w > 0) { found = true; yield return (p, w); }
        }
        if (!found) throw new InvalidDataException("Speos measurement has no data for this side.");
    }
    private (double X, double Y) Shift(BsdfContext c, Part p, bool t)
    {
        if (_peak is null) return (0, 0);
        var ratio = t ? p.Side == BsdfSide.Front ? _peak.IncidentIndex / _peak.TransmittedIndex : _peak.TransmittedIndex / _peak.IncidentIndex : 1;
        if (t && Math.Abs(c.IncidentRefractiveIndex / c.TransmittedRefractiveIndex - ratio) > 1e-10 * ratio) throw new InvalidDataException("Speos peak media differ from declared media.");
        var f = Frame(c); var x = -BsdfContext.Dot(c.IncomingDirection, f.X) * ratio; var y = -BsdfContext.Dot(c.IncomingDirection, f.Y) * ratio;
        var sin = Math.Sin(p.Theta * Math.PI / 180) * ratio; var square = x * x + y * y;
        if (sin >= 1 || square >= 1) throw new InvalidDataException("Speos slope peak outside non-grazing domain.");
        var source = -sin / Math.Sqrt(1 - sin * sin); var phi = p.Phi * Math.PI / 180;
        return (x / Math.Sqrt(1 - square) - source * Math.Cos(phi), y / Math.Sqrt(1 - square) - source * Math.Sin(phi));
    }
    public BsdfEnergy Integrate(BsdfContext c)
    {
        var inputs = Inputs(c).ToArray();
        if (!_spectrum.RuntimeWhite) return new(inputs.Sum(p => p.Weight * Mass(p.Part, false, c.WavelengthNanometers)), inputs.Sum(p => p.Weight * Mass(p.Part, true, c.WavelengthNanometers)));
        var reference = RuntimeReference(c);
        var r = Scales(reference, false, c.WavelengthNanometers); var t = Scales(reference, true, c.WavelengthNanometers);
        return new(inputs.Sum(p => p.Weight * ScaledMass(p.Part, false, r)), inputs.Sum(p => p.Weight * ScaledMass(p.Part, true, t)));
    }
    public double Evaluate(BsdfContext c, Vector3D direction)
    {
        var unit = BsdfContext.Unit(direction); var signed = BsdfContext.Dot(c.SurfaceNormal, unit); var mu = Math.Abs(signed); if (mu == 0) return 0;
        var t = signed < 0; var f = Frame(c); var dx = BsdfContext.Dot(unit, f.X); var dy = BsdfContext.Dot(unit, f.Y); var phi = Math.Atan2(dy, dx); if (phi < 0) phi += 2 * Math.PI;
        double value = 0;
        foreach (var (p, weight) in Inputs(c))
        {
            var scale = Scales(p, t, c.WavelengthNanometers);
            var q = scale.Diffuse * p.Diffuse.Eval(t, mu, phi) * (_projected ? 1 : mu);
            if (p.Peak is not null && (t ? p.Peak.T : p.Peak.R) > 0)
            {
                var delta = Shift(c, p, t); var sx = dx / mu - delta.X; var sy = dy / mu - delta.Y; var smu = 1 / Math.Sqrt(1 + sx * sx + sy * sy);
                var sp = Math.Atan2(sy, sx); if (sp < 0) sp += 2 * Math.PI;
                q += scale.Peak * p.Peak.Eval(t, smu, sp) * (_projected ? 1 : smu) * Math.Pow(smu / mu, 3);
            }
            value += weight * q / mu;
        }
        return value;
    }
    public BsdfSample Sample(MeasuredBsdf model, BsdfContext c, double u1, double u2)
        => SampleLobe(model, c, u1, u2, null);
    internal BsdfSample SampleLobe(MeasuredBsdf model, BsdfContext c, double u1, double u2, bool? transmission)
    {
        if (!double.IsFinite(u1) || !double.IsFinite(u2) || u1 is < 0 or >= 1 || u2 is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(u1));
        var choices = Inputs(c).SelectMany(p => new[] { false, true }.SelectMany(t =>
        {
            var scale = Scales(p.Part, t, c.WavelengthNanometers);
            return new[] { (p.Part, Transmission: t, Peak: false, Mass: p.Weight * scale.Diffuse * (t ? p.Part.Diffuse.T : p.Part.Diffuse.R)),
                (p.Part, Transmission: t, Peak: true, Mass: p.Weight * scale.Peak * (t ? p.Part.Peak?.T ?? 0 : p.Part.Peak?.R ?? 0)) };
        })).Where(p => p.Mass > 0 && (transmission is null || p.Transmission == transmission)).ToArray();
        var total = choices.Sum(p => p.Mass); if (total == 0) return new LambertianReflectionBsdf(0).Sample(c, u1, u2);
        var target = Math.Clamp(u1, 5.551115123125783e-17, Math.BitDecrement(1d)) * total; var selected = choices[^1];
        foreach (var p in choices) { selected = p; if (target < p.Mass) break; target -= p.Mass; }
        var (mu, phi) = (selected.Peak ? selected.Part.Peak! : selected.Part.Diffuse).Sample(selected.Transmission, Math.Clamp(target / selected.Mass, 5.551115123125783e-17, Math.BitDecrement(1d)), Math.Clamp(u2, 5.551115123125783e-17, Math.BitDecrement(1d)));
        var f = Frame(c); var radius = Math.Sqrt(Math.Max(0, 1 - mu * mu));
        var delta = selected.Peak ? Shift(c, selected.Part, selected.Transmission) : (X: 0d, Y: 0d);
        var direction = BsdfContext.Unit(f.X * (radius * Math.Cos(phi) / mu + delta.X) + f.Y * (radius * Math.Sin(phi) / mu + delta.Y) + c.SurfaceNormal * (selected.Transmission ? -1 : 1));
        return new(direction, model.Evaluate(c, direction), model.Pdf(c, direction), selected.Transmission ? BsdfLobe.DiffuseTransmission : BsdfLobe.DiffuseReflection);
    }
}
