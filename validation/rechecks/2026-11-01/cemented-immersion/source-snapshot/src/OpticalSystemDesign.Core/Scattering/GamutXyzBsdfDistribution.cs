using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Scattering;

/// <summary>Signed calibration is evaluated on absolute XYZ knots inside its declared
/// cone. The resulting nonnegative radiometric grids use the ordinary Zemax integrator
/// and sampler. A signed channel mixture is never used as a probability distribution.</summary>
internal sealed class GamutXyzBsdfDistribution : IMeasuredBsdfDistribution
{
    private readonly double[] _waves;
    private readonly ZemaxBsdfDistribution[] _bands;
    public bool HasTransmission => _bands.Any(b => b.HasTransmission);
    public bool HasBothSides => _bands.All(b => b.HasBothSides);
    internal GamutXyzBsdfDistribution(IReadOnlyList<MeasuredBsdfPoint> y, BsdfCoordinates coordinates, ZemaxBsdfDefinition definition, ZemaxXyzDefinition xyz)
    {
        var gamut = xyz.Calibration.FitEvidence!.Gamut!;
        _waves = xyz.Calibration.Bands.Select(b => b.WavelengthNanometers).ToArray();
        if ((long)y.Count * _waves.Length > MeasuredBsdfData.MaximumPoints) throw new InvalidDataException("XYZ 校准后的辐射度网格超过节点预算；不降采样。");
        static object Key(MeasuredBsdfPoint p) => (p.Side, p.WavelengthNanometers, p.IncidentThetaDegrees, p.IncidentPhiDegrees, p.OutgoingThetaDegrees, p.OutgoingPhiDegrees);
        var keys = y.Select(Key).ToHashSet();
        if (!keys.SetEquals(xyz.XPoints.Select(Key)) || !keys.SetEquals(xyz.ZPoints.Select(Key))) throw new InvalidDataException("XYZ 通道角度轴不一致。");
        var cx = new ZemaxBsdfDistribution(xyz.XPoints, coordinates, definition with { Integrals = xyz.XIntegrals });
        var cy = new ZemaxBsdfDistribution(y, coordinates, definition);
        var cz = new ZemaxBsdfDistribution(xyz.ZPoints, coordinates, definition with { Integrals = xyz.ZIntegrals });
        var xs = cx.AbsoluteNodes(xyz.XPoints).ToDictionary(Key); var ys = cy.AbsoluteNodes(y); var zs = cz.AbsoluteNodes(xyz.ZPoints).ToDictionary(Key);
        if (xyz.Calibration.PositiveModel is not null || xyz.Calibration.AbsoluteRbfModel is not null)
        {
            var values = ys.Select(p =>
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                double Value(MeasuredBsdfPoint q) => definition.Transmission ? q.BtdfPerSteradian : q.BrdfPerSteradian;
                var vx = Value(xs[Key(p)]); var vy = Value(p); var vz = Value(zs[Key(p)]);
                if (!gamut.Contains(vx, vy, vz)) throw new InvalidDataException("Zemax XYZ 绝对通道节点在正值模型颜色域外；不能外推。");
                var result = xyz.Calibration.PositiveModel is { } positive ? positive.Evaluate(vx, vy, vz) : xyz.Calibration.AbsoluteRbfModel!.Evaluate(vx,vy,vz);
                if (result.Any(v => !double.IsFinite(v) || v < 0)) throw new InvalidDataException("XYZ 正值映射产生无效辐射度。");
                return result;
            }).ToArray();
            _bands = _waves.Select((w, band) => ZemaxBsdfDistribution.FromAbsoluteNodes(ys.Select((p, i) => p with
                { WavelengthNanometers = w, BrdfPerSteradian = definition.Transmission ? 0 : values[i][band], BtdfPerSteradian = definition.Transmission ? values[i][band] : 0 }).ToArray(), coordinates, definition)).ToArray();
            return;
        }
        var xt = xyz.XIntegrals.ToDictionary(t => (t.Side, t.SpecularThetaDegrees, t.SampleRotationDegrees));
        var zt = xyz.ZIntegrals.ToDictionary(t => (t.Side, t.SpecularThetaDegrees, t.SampleRotationDegrees));
        _bands = xyz.Calibration.Bands.Select(b =>
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var nodes = ys.Select(p =>
            {
                var x = xs[Key(p)]; var z = zs[Key(p)];
                double Value(MeasuredBsdfPoint q) => definition.Transmission ? q.BtdfPerSteradian : q.BrdfPerSteradian;
                var vx = Value(x); var vy = Value(p); var vz = Value(z);
                if (!gamut.Contains(vx, vy, vz)) throw new InvalidDataException("Zemax XYZ 绝对通道节点在校准颜色域外；不能外推。");
                var value = b.X * vx + b.Y * vy + b.Z * vz;
                if (!double.IsFinite(value) || value < 0) throw new InvalidDataException("XYZ 校准产生负辐射度；不能裁剪为零。");
                return p with { WavelengthNanometers = b.WavelengthNanometers, BrdfPerSteradian = definition.Transmission ? 0 : value, BtdfPerSteradian = definition.Transmission ? value : 0 };
            }).ToArray();
            var energy = definition.Integrals.Select(t =>
            {
                var key = (t.Side, t.SpecularThetaDegrees, t.SampleRotationDegrees);
                var value = b.X * xt[key].Fraction + b.Y * t.Fraction + b.Z * zt[key].Fraction;
                if (!double.IsFinite(value) || value < 0 || value > 1 + 32 * 2.2204460492503131e-16) throw new InvalidDataException("XYZ 校准产生非被动 TIS；不缩放或裁剪。");
                return t with { Fraction = value };
            }).ToArray();
            return new ZemaxBsdfDistribution(nodes, coordinates, definition with { Integrals = energy });
        }).ToArray();
    }
    private static BsdfContext At(BsdfContext c, double wave) => new(c.IncomingDirection, c.SurfaceNormal, wave, c.Side, c.MaterialTangent, c.IncidentRefractiveIndex, c.TransmittedRefractiveIndex);
    private (int Low, int High, double T) Bracket(BsdfContext c)
    {
        if (c.WavelengthNanometers < _waves[0] || c.WavelengthNanometers > _waves[^1]) throw new InvalidDataException("XYZ 校准不能外推测量波长。");
        return MeasuredBsdfGrid.Bracket(_waves, c.WavelengthNanometers);
    }
    public BsdfEnergy Integrate(BsdfContext c)
    {
        var b = Bracket(c); var lo = _bands[b.Low].Integrate(At(c, _waves[b.Low])); var hi = _bands[b.High].Integrate(At(c, _waves[b.High]));
        return new(lo.Reflection * (1 - b.T) + hi.Reflection * b.T, lo.Transmission * (1 - b.T) + hi.Transmission * b.T);
    }
    public double Evaluate(BsdfContext c, Vector3D direction)
    {
        var b = Bracket(c); return _bands[b.Low].Evaluate(At(c, _waves[b.Low]), direction) * (1 - b.T) + _bands[b.High].Evaluate(At(c, _waves[b.High]), direction) * b.T;
    }
    public BsdfSample Sample(MeasuredBsdf model, BsdfContext c, double u1, double u2)
    {
        if (!double.IsFinite(u1) || !double.IsFinite(u2) || u1 is < 0 or >= 1 || u2 is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(u1));
        var b = Bracket(c); var lower = (1 - b.T) * _bands[b.Low].Integrate(At(c, _waves[b.Low])).Total;
        var upper = b.T * _bands[b.High].Integrate(At(c, _waves[b.High])).Total; var total = lower + upper;
        if (total == 0) return new LambertianReflectionBsdf(0).Sample(c, u1, u2);
        var target = u1 * total; var index = target < lower ? b.Low : b.High;
        var local = target < lower ? target / lower : (target - lower) / upper;
        var sampled = _bands[index].Sample(model, At(c, _waves[index]), Math.Clamp(local, 0, Math.BitDecrement(1d)), u2);
        return new(sampled.Direction, model.Evaluate(c, sampled.Direction), model.Pdf(c, sampled.Direction), sampled.Lobe);
    }
}
