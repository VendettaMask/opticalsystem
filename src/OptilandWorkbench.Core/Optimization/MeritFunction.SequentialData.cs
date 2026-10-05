using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateLocalRayData(Optic optic, MeritOperandDefinition definition)
    {
        var ray = SequentialRayDefinition(optic, definition);
        var surface = ResolveSurface(optic, ray.Surface);
        var sample = RequireRaySample(optic, ray);
        var position = surface.CoordinateSystem.ToLocalPoint(sample.Position);
        var direction = UnitVector(surface.CoordinateSystem.ToLocalDirection(sample.Direction));
        var normal = PositiveZNormal(surface, position);
        var type = CanonicalType(definition.Type);
        if (type is "RAIN" or "RAID" or "RAEN" or "RAED")
        {
            var measured = type is "RAIN" or "RAID"
                ? UnitVector(surface.CoordinateSystem.ToLocalDirection(sample.IncidentDirection
                    ?? throw new InvalidOperationException("追迹没有记录该面的入射方向。")))
                : direction;
            var cosine = Math.Clamp(Math.Abs(Dot(normal, measured)), 0, 1);
            return type is "RAID" or "RAED" ? Math.Acos(cosine) * 180 / Math.PI : cosine;
        }

        return type switch
        {
            "REAZ" => position.Z,
            "REAA" => direction.X,
            "REAB" => direction.Y,
            "REAC" => direction.Z,
            "RENA" => normal.X,
            "RENB" => normal.Y,
            "RENC" => normal.Z,
            "RETX" => RaySlope(direction.X, direction.Z),
            "RETY" => RaySlope(direction.Y, direction.Z),
            _ => throw new NotSupportedException(type)
        };
    }

    private static double EvaluateRayOpticalPath(Optic optic, MeritOperandDefinition definition)
    {
        var isRange = CanonicalType(definition.Type) == "PLEN";
        var start = isRange ? ZemaxIntegerParameter(definition, 0, definition.Surface)
            : ObjectConjugate.IsInfinite(optic.SurfaceGroup.Items.FirstOrDefault())
                ? optic.SurfaceGroup.Items.ElementAtOrDefault(1)?.Number
                    ?? throw new InvalidOperationException("系统没有首个光学面。")
                : optic.SurfaceGroup.Items.FirstOrDefault()?.Number
                    ?? throw new InvalidOperationException("系统没有物面。");
        var end = isRange ? ZemaxIntegerParameter(definition, 1, definition.Wavelength)
            : ZemaxIntegerParameter(definition, 0, definition.Surface);
        var ray = SequentialRayDefinition(optic, definition, isRange ? 0 : null, end);
        var endPath = RequireRaySample(optic, ray).PhaseInclusiveOpticalPathLength
            ?? throw new InvalidOperationException("追迹没有记录含相位的光程。");
        ray.Surface = start;
        var startPath = RequireRaySample(optic, ray).PhaseInclusiveOpticalPathLength
            ?? throw new InvalidOperationException("追迹没有记录参考面的含相位光程。");
        return endPath - startPath;
    }

    private static MeritOperandDefinition SequentialRayDefinition(
        Optic optic, MeritOperandDefinition definition, int? wavelength = null, int? surface = null)
    {
        var ray = definition.Clone();
        ray.Surface = surface ?? ZemaxIntegerParameter(definition, 0, definition.Surface);
        ray.Wavelength = wavelength ?? ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        RequireWavelength(optic, ray.Wavelength);
        ResolveSurface(optic, ray.Surface);
        ray.Hx = FiniteParameter(definition, 0, definition.Hx);
        ray.Hy = FiniteParameter(definition, 1, definition.Hy);
        ray.Px = FiniteParameter(definition, 2, definition.Px);
        ray.Py = FiniteParameter(definition, 3, definition.Py);
        if (Math.Abs(ray.Hx) > 1 || Math.Abs(ray.Hy) > 1
            || Math.Abs(ray.Px) > 1 || Math.Abs(ray.Py) > 1)
            throw new ArgumentOutOfRangeException(nameof(definition), "归一化视场和瞳孔坐标必须在 [-1, 1] 内。");
        // Explicit Zemax Hx=Hy=0 always means the axial field, even if row 1 is off-axis.
        if (definition.ZemaxDataParameters.Length > 0) ray.Field = 0;
        return ray;
    }

    private static RayTraceSample RequireRaySample(Optic optic, MeritOperandDefinition ray) =>
        RequireRaySamples(optic, ray, [ray.Surface])[0];

    private static RayTraceSample[] RequireRaySamples(Optic optic, MeritOperandDefinition ray, IReadOnlyList<int> surfaceNumbers)
    {
        if (surfaceNumbers.Contains(optic.SurfaceGroup.Items[0].Number)
            && ObjectConjugate.IsInfinite(optic.SurfaceGroup.Items[0]))
            throw new InvalidOperationException("无穷远物面没有有限交点。请指定实际光学面。");
        ComputationCancellation.ThrowIfCancellationRequested();
        var field = ray.ZemaxDataParameters.Length > 0 || ray.Field <= 0
            ? (X: ray.Hx, Y: ray.Hy) : ResolveNormalizedField(optic, ray);
        var wavelength = RequireWavelength(optic, ray.Wavelength);
        // The tracer owns bounded, revision-aware reuse keyed by the exact generated ray.
        // Do not retain a second sample cache across possible mutations inside a merit batch.
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
            field.X, field.Y, ray.Px, ray.Py, wavelength.Micrometers, aimAtStop: optic.RayAimingEnabled);
        var indices = surfaceNumbers.Select(number => optic.SurfaceGroup.Items.IndexOf(ResolveSurface(optic, number))).ToArray();
        using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected(indices));
        return indices.Select(index =>
        {
            if (!trace.TryGetSample(0, index, out var value))
                throw new InvalidOperationException($"光线未到达表面 {optic.SurfaceGroup.Items[index].Number}。");
            var sample = value.ToRayTraceSample();
            if (sample.Vignetted || sample.Intensity <= 0 || sample.InteractionKind is null)
                throw new InvalidOperationException($"光线未有效到达表面 {sample.SurfaceNumber}，不能返回操作数值。");
            return sample;
        }).ToArray();
    }

    private static double EvaluateSurfaceShapeData(Optic optic, MeritOperandDefinition definition)
    {
        var surface = ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface));
        OpticCapabilityPreflight.EnsureSurfaceSupported(surface, OpticCapabilityOperation.Optimization);
        var type = CanonicalType(definition.Type);
        var x = type == "SAGX" ? surface.SemiDiameter : type == "SAGY" ? 0
            : FiniteParameter(definition, 0, definition.Hx);
        var y = type == "SAGY" ? surface.SemiDiameter : type == "SAGX" ? 0
            : FiniteParameter(definition, 1, definition.Hy);
        var sag = surface.Geometry.Sag(x, y);
        if (!double.IsFinite(sag)) throw new InvalidOperationException("指定坐标超出面形的有限矢高定义域。");
        if (type is "SAGX" or "SAGY") return sag;
        var point = new Vector3D(x, y, sag);
        var normal = PositiveZNormal(surface, point);
        if (type == "NORD")
        {
            var next = ResolveNextSurface(optic, surface.Number);
            OpticCapabilityPreflight.EnsureSurfaceSupported(next, OpticCapabilityOperation.Optimization);
            var intersection = next.Geometry.DistanceToIntersection(
                next.CoordinateSystem.ToLocalPoint(surface.CoordinateSystem.ToGlobalPoint(point)),
                next.CoordinateSystem.ToLocalDirection(surface.CoordinateSystem.ToGlobalDirection(normal)));
            if (!intersection.IsHit || !double.IsFinite(intersection.Distance))
                throw new InvalidOperationException("沿表面法线没有到下一表面的有效交点。");
            return intersection.Distance;
        }
        var global = FiniteParameter(definition, 2, definition.Px);
        if (global is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(definition), "Global 必须为 0 或 1。");
        if (global == 1) normal = GlobalCoordinateMetrics.ReferenceFrame(optic)
            .ToLocalDirection(surface.CoordinateSystem.ToGlobalDirection(normal));
        return type switch
        {
            "NORX" => normal.X,
            "NORY" => normal.Y,
            "NORZ" => normal.Z,
            _ => throw new NotSupportedException(type)
        };
    }

    private static double EvaluateSurfaceThermalExpansion(Optic optic, MeritOperandDefinition definition) =>
        ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface)).ThermalExpansionPpmPerC
            ?? throw new InvalidOperationException("该表面的 TCE 未提供；请在镜头数据中输入热膨胀系数（10⁻⁶/°C）。玻璃目录系数使用 GTCE。");

    private static double EvaluateGlassThermalExpansion(Optic optic, MeritOperandDefinition definition)
    {
        var material = ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface)).MaterialAfter;
        return material is CatalogGlassMaterial { ZemaxData.ThermalExpansionLow: { } alpha }
            && double.IsFinite(alpha) ? alpha
            : throw new InvalidOperationException("该玻璃目录没有 Alpha1 热膨胀系数数据。");
    }

    private static double? GlassPartialDispersionDeviation(IMaterial material) =>
        material is CatalogGlassMaterial { ZemaxData.RelativePartialDispersionDeviation: { } deviation }
            && double.IsFinite(deviation) ? deviation : null;

    private static double EvaluateAdditionalFirstOrder(Optic optic, MeritOperandDefinition definition)
    {
        var type = CanonicalType(definition.Type);
        var wavelength = RequireWavelength(optic, type == "OBSN" ? 0
            : ZemaxIntegerParameter(definition, 1, definition.Wavelength));
        return type switch
        {
            "AMAG" => optic.Paraxial.AngularMagnification(wavelength.Micrometers),
            "LINV" => optic.Paraxial.LagrangeInvariant(wavelength.Micrometers),
            "PIMH" => optic.Paraxial.ParaxialImageHeight(wavelength.Micrometers),
            "OBSN" => optic.Paraxial.ObjectNumericalAperture(),
            _ => throw new NotSupportedException(type)
        };
    }

    private static Wavelength RequireWavelength(Optic optic, int index)
    {
        if (index < 0 || index > optic.Wavelengths.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "波长编号不在当前系统范围内。");
        return ResolveWavelength(optic, index);
    }

    private static double FiniteParameter(MeritOperandDefinition definition, int index, double fallback)
    {
        var value = ZemaxDataParameter(definition, index, fallback);
        return double.IsFinite(value) ? value
            : throw new ArgumentOutOfRangeException(nameof(definition), "操作数参数必须是有限数值。");
    }

    private static Vector3D PositiveZNormal(OpticalSurface surface, Vector3D point)
    {
        var normal = UnitVector(surface.Geometry.SurfaceNormal(point));
        return normal.Z < 0 ? -normal : normal;
    }

    private static Vector3D UnitVector(Vector3D value) => double.IsFinite(value.Length) && value.Length > 1e-15
        ? value / value.Length : throw new InvalidOperationException("方向或法线不是有效单位向量。");

    private static double Dot(Vector3D left, Vector3D right) => left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static double RaySlope(double transverse, double axial) => Math.Abs(axial) > 1e-15
        ? transverse / axial : throw new InvalidOperationException("光线平行于局部 XY 平面，斜率无穷大。");
}
