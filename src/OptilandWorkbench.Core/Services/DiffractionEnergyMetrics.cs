using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Services;

/// <summary>Finite-window scalar PSF energy using the formal FFT/Huygens engines.</summary>
public static class DiffractionEnergyMetrics
{
    public static DiffractionEnergyDistribution Create(Optic optic, int pupilSamplingCode, int wave, int fieldNumber,
        EnergyRegion region, int reference, int imageSamplingCode = 1, double imageDeltaMicrometers = 0)
    {
        ArgumentNullException.ThrowIfNull(optic); ComputationCancellation.ThrowIfCancellationRequested();
        if (optic.ImageSpaceAfocal) throw new NotSupportedException("衍射能量操作数当前只支持有焦像空间。");
        if (fieldNumber < 1 || fieldNumber > optic.Fields.Count) throw new ArgumentOutOfRangeException(nameof(fieldNumber));
        if (!Enum.IsDefined(region) || reference is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(reference));
        if (pupilSamplingCode is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(pupilSamplingCode), "瞳孔采样代码支持 1..4。");
        var huygens = reference >= 3;
        if (huygens && (imageSamplingCode is < 1 or > 4 || !double.IsFinite(imageDeltaMicrometers) || imageDeltaMicrometers < 0))
            throw new ArgumentOutOfRangeException(nameof(imageSamplingCode), "惠更斯图像采样为 1..4，间隔必须有限非负。");
        var wavelengths = MetricWavelengthSelection.Select(optic, wave);
        var primary = wave > 0 ? optic.Wavelengths[wave - 1]
            : optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0];
        var pupilSize = 32 << (pupilSamplingCode - 1);
        var imageSize = huygens ? 32 << (imageSamplingCode - 1) : 2 * pupilSize;
        AnalysisResourceLimits.ValidateAggregateGridWork(imageSize, imageSize, 1, wavelengths.Length,
            checked(pupilSize * pupilSize), "衍射能量");
        if (huygens)
        {
            AnalysisResourceLimits.ValidateDirectPsfWork(pupilSize, imageSize);
            if (checked((long)pupilSize * pupilSize * imageSize * imageSize * wavelengths.Length) > AnalysisResourceLimits.MaximumDirectPsfOperations)
                throw new InvalidOperationException("多波长惠更斯积分超过共享计算预算。");
        }
        else AnalysisResourceLimits.ValidateFftGrid(pupilSize, imageSize);
        (double Hx, double Hy) field = FieldCoordinates.Normalize(optic.Fields, optic.Fields[fieldNumber - 1].X, optic.Fields[fieldNumber - 1].Y);
        var chiefWave = optic.Wavelengths.ToList().IndexOf(primary) + 1;
        var chief = RayBundleMetrics.Trace(optic, optic.SurfaceGroup.Items[^1].Number, chiefWave,
            field.Hx, field.Hy, [new PupilSample(0, 0, 1)], requireUnvignetted: true,
            includeSurfaceTransmission: false, aimAtStop: huygens ? optic.RayAimingEnabled : true)[0];
        var points = new List<WeightedEnergyPoint>();
        (double X, double Y) vertex;
        if (huygens)
        {
            var delta = imageDeltaMicrometers > 0 ? imageDeltaMicrometers / 1000
                : DiffractionEngine.DefaultHuygensImageDeltaMillimeters(optic, field, wavelengths.MaxBy(w => w.Nanometers)!, pupilSize);
            var frame = DiffractionEngine.CreateHuygensImageFrame(optic, field, primary, optic.RayAimingEnabled);
            var toVertex = optic.SurfaceGroup.Items[^1].CoordinateSystem.Origin - frame.Center;
            vertex = (Dot(toVertex, frame.TangentX) * 1000, Dot(toVertex, frame.TangentY) * 1000);
            var results = wavelengths.Select(w => (Wave: w, Psf: DiffractionEngine.ComputeHuygensPsf(optic, field, w,
                pupilSize, imageSize, delta, usePolarization: false, aimAtStop: optic.RayAimingEnabled, referenceWavelength: primary))).ToArray();
            var shortest = wavelengths.Min(w => w.Nanometers);
            for (var y = 0; y < imageSize; y++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                for (var x = 0; x < imageSize; x++)
                {
                    var intensity = results.Sum(r => r.Wave.Weight * Math.Pow(shortest / r.Wave.Nanometers, 2) * r.Psf.Values[y, x]);
                    points.Add(new((x - imageSize / 2) * delta * 1000, (y - imageSize / 2) * delta * 1000, intensity));
                }
            }
        }
        else
        {
            vertex = (-chief.Position.X * 1000, -chief.Position.Y * 1000);
            var results = wavelengths.Select(w => (Wave: w, Psf: DiffractionEngine.ComputeFftPsf(optic, field, w,
                pupilSize, imageSize, usePolarization: false, cellCenteredPupil: true, zemaxFftSampling: true,
                aimAtStop: optic.RayAimingEnabled, referenceWavelength: primary))).ToArray();
            if (results.Any(r => r.Psf.SampleSpacingUnit != AnalysisAxisUnit.Micrometer)) throw new InvalidOperationException("FFT PSF 未发布微米坐标。");
            var delta = results.Min(r => r.Psf.SampleSpacingMicrometers);
            // Use the same central FFT window and coordinate convention as the
            // formal diffraction-energy analysis, without building a plot.
            var start = (imageSize - pupilSize) / 2;
            for (var y = start; y < start + pupilSize; y++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                for (var x = start; x < start + pupilSize; x++)
                {
                    var px = (x - (imageSize - 1) / 2) * delta; var py = (y - (imageSize - 1) / 2) * delta;
                    var intensity = results.Sum(r => r.Wave.Weight * PsfAnalysis.BilinearSample(r.Psf, px, py));
                    points.Add(new(px, py, intensity));
                }
            }
        }
        var pointReference = reference % 3;
        var center = pointReference == 0 ? (0.0, 0.0) : pointReference == 2 ? vertex
            : GeometricEnergyDistribution.Reference(points, GeometricEnergyReference.Centroid);
        return new(points, center, region);
    }
    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

/// <summary>Pixel-area energy normalized over the finite sampled image window.</summary>
public sealed class DiffractionEnergyDistribution
{
    private readonly PsfPixelEnergyGrid _grid;
    public double MaximumCoveredDistanceMicrometers { get; }

    public DiffractionEnergyDistribution(IReadOnlyList<WeightedEnergyPoint> points, (double X, double Y) center, EnergyRegion region)
    {
        ArgumentNullException.ThrowIfNull(points); ComputationCancellation.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(region) || !double.IsFinite(center.X) || !double.IsFinite(center.Y)) throw new ArgumentOutOfRangeException(nameof(region));
        if (points.Count == 0 || points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Weight) || p.Weight < 0))
            throw new InvalidOperationException("PSF 网格必须具有有限坐标和非负强度。");
        var xs = points.Select(p => p.X).Distinct().Order().ToArray(); var ys = points.Select(p => p.Y).Distinct().Order().ToArray();
        var dx = Pitch(xs); var dy = Pitch(ys);
        if ((long)xs.Length * ys.Length != points.Count || points.Select(p => (p.X, p.Y)).Distinct().Count() != points.Count)
            throw new InvalidOperationException("PSF 必须是完整均匀矩形网格，包括零强度像素。");
        var scale = points.Max(p => p.Weight);
        if (scale <= 0) throw new InvalidOperationException("PSF 没有正能量。");
        var xLimit = Math.Min(center.X - xs[0] + dx / 2, xs[^1] - center.X + dx / 2);
        var yLimit = Math.Min(center.Y - ys[0] + dy / 2, ys[^1] - center.Y + dy / 2);
        MaximumCoveredDistanceMicrometers = region switch
        { EnergyRegion.XSlit => xLimit, EnergyRegion.YSlit => yLimit, _ => Math.Min(xLimit, yLimit) };
        if (!double.IsFinite(MaximumCoveredDistanceMicrometers) || MaximumCoveredDistanceMicrometers <= 0)
            throw new InvalidOperationException("所选参考点不在有效图像窗口内；请增大图像范围。");
        var type = region switch { EnergyRegion.XSlit => "X", EnergyRegion.YSlit => "Y", EnergyRegion.Square => "ensquared", _ => "encircled" };
        _grid = new PsfPixelEnergyGrid(points.Select(p => new EnergySample(p.X, p.Y, p.Weight / scale)).ToArray(), center, type);
    }
    public double FractionAtDistance(double distanceMicrometers)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        if (!double.IsFinite(distanceMicrometers) || distanceMicrometers < 0) throw new ArgumentOutOfRangeException(nameof(distanceMicrometers));
        if (distanceMicrometers > MaximumCoveredDistanceMicrometers) throw new InvalidOperationException("所需积分区域超出有效图像窗口。");
        return _grid.Fraction(distanceMicrometers);
    }
    public double DistanceAtFraction(double fraction)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        if (!double.IsFinite(fraction) || fraction < 0 || fraction >= 1) throw new ArgumentOutOfRangeException(nameof(fraction), "有限 PSF 窗口支持 0 <= Fraction < 1。");
        if (fraction == 0) return 0;
        if (fraction > _grid.Fraction(MaximumCoveredDistanceMicrometers)) throw new InvalidOperationException("图像采样窗口不足以覆盖所需能量分数。");
        return _grid.RadiusContaining(fraction);
    }
    private static double Pitch(double[] values)
    {
        if (values.Length < 2) throw new InvalidOperationException("PSF 网格每轴至少需要两个点。");
        var delta = values[1] - values[0];
        if (!double.IsFinite(delta) || delta <= 1e-12 || values.Skip(1).Select((v, i) => v - values[i]).Any(d => Math.Abs(d / delta - 1) > 1e-8))
            throw new InvalidOperationException("PSF 网格间隔必须有限、均匀且大于 1e-12 µm。");
        return delta;
    }
}
