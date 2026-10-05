using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateHighYield(Optic optic, MeritOperandDefinition definition)
    {
        var ray = SequentialRayDefinition(optic, definition);
        if (ray.Px * ray.Px + ray.Py * ray.Py > 1 + 1e-12)
            throw new ArgumentOutOfRangeException(nameof(definition), "HYLD 瞳孔坐标必须在单位圆内。");
        return RayManufacturingMetrics.HighYieldContribution(ResolveSurface(optic, ray.Surface), RequireRaySample(optic, ray));
    }

    private static double EvaluateRayAngleRange(Optic optic, MeritOperandDefinition definition)
    {
        if (definition.ZemaxDataParameters.Length < 5)
            throw new InvalidOperationException("角度范围需要完整七项参数；旧 ZMX 扩展记录尚未验证，请在编辑器重新建立操作数。");
        var first = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var last = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        var surfaces = optic.SurfaceGroup.Items;
        var start = surfaces.IndexOf(ResolveSurface(optic, first));
        var end = surfaces.IndexOf(ResolveSurface(optic, last));
        if (start > end) throw new ArgumentException("起始面必须在终止面之前或与其相同。");
        var wave = IntegerDataParameter(definition, 0, definition.Wavelength);
        RequireWavelength(optic, wave);
        var hx = FiniteParameter(definition, 1, definition.Hx);
        var hy = FiniteParameter(definition, 2, definition.Hy);
        var px = FiniteParameter(definition, 3, definition.Px);
        var py = FiniteParameter(definition, 4, definition.Py);
        if (Math.Abs(hx) > 1 || Math.Abs(hy) > 1 || px * px + py * py > 1 + 1e-12)
            throw new ArgumentOutOfRangeException(nameof(definition), "视场坐标须在 [-1,1] 内，瞳孔坐标须在单位圆内。");
        var ray = new MeritOperandDefinition
        {
            Surface = first,
            Wavelength = wave,
            Hx = hx,
            Hy = hy,
            Px = px,
            Py = py,
            ZemaxDataParameters = [hx, hy, px, py]
        };
        var numbers = surfaces.Skip(start).Take(end - start + 1).Select(s => s.Number).ToArray();
        var samples = RequireRaySamples(optic, ray, numbers);
        var type = CanonicalType(definition.Type);
        var incident = type.EndsWith("I", StringComparison.Ordinal);
        var maximum = type.StartsWith("MX", StringComparison.Ordinal);
        var violation = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var surface = surfaces[start + i];
            var sample = samples[i];
            var normal = PositiveZNormal(surface, surface.CoordinateSystem.ToLocalPoint(sample.Position));
            var direction = incident ? sample.IncidentDirection
                ?? throw new InvalidOperationException("追迹未记录入射方向。") : sample.Direction;
            direction = UnitVector(surface.CoordinateSystem.ToLocalDirection(direction));
            var angle = Math.Acos(Math.Clamp(Math.Abs(Dot(normal, direction)), 0, 1)) * 180 / Math.PI;
            if (!double.IsFinite(angle)) throw new InvalidOperationException("光线角度不是有限值。");
            // Native range boundaries accumulate every violation, not only the worst surface.
            violation += maximum ? Math.Max(0, angle - definition.Target) : Math.Max(0, definition.Target - angle);
        }
        return definition.Target + (maximum ? violation : -violation);
    }

    private static double EvaluateDirectionalWorkingFNumber(Optic optic, MeritOperandDefinition definition)
    {
        if (optic.ImageSpaceAfocal) throw new NotSupportedException("SFNO/TFNO 需要有焦像空间。");
        var field = ZemaxIntegerParameter(definition, 0, definition.Field);
        if (field < 0 || field > optic.Fields.Count) throw new ArgumentOutOfRangeException(nameof(field));
        var coordinate = field == 0 ? (X: 0.0, Y: 0.0) : FieldCoordinates.Normalize(optic.Fields, optic.Fields[field - 1].X, optic.Fields[field - 1].Y);
        // The shared diffraction scale currently probes the X/Y pupil axes.
        // Do not silently call them sagittal/tangential for arbitrary azimuths.
        if (coordinate.X != 0) throw new NotSupportedException("SFNO/TFNO 当前支持轴上及 Y 方向视场；任意方位尚待验证。");
        var wavelength = RequireWavelength(optic, ZemaxIntegerParameter(definition, 1, definition.Wavelength));
        var axes = DiffractionEngine.WorkingFNumbers(optic, coordinate, wavelength,
            aimAtStop: optic.RayAimingEnabled, zemaxDirectionalAverage: true, strictFullPupil: true);
        return CanonicalType(definition.Type) == "TFNO" ? axes.Tangential : axes.Sagittal;
    }

}
