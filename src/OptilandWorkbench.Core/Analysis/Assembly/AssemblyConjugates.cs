using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis.Assembly;

public enum AssemblyImageKind { Vertex, CurvatureCenter }
public enum AssemblyImageStatus { Finite, Infinity, NoReflection, Unsupported, Failed }
public enum AssemblyProbeMode { Focused, Infinity }

public sealed record AssemblyConjugate(int SurfaceNumber, string SurfaceLabel,
    AssemblyImageKind Kind, AssemblyImageStatus Status, double? PositionMillimeters, string Message);

public sealed record AssemblyProbe(double HeadFocalLength = 100, double CollimatorFocalLength = 200,
    double NumericalAperture = 0.02, double ReticleLength = 1, double ReticleWidth = 0.02,
    double FocusOffset = 0, double SensorWidth = 4,
    AssemblyProbeMode Mode = AssemblyProbeMode.Focused, double PupilDiameter = 4, double InstrumentDistance = 100);

public sealed record AssemblyImage(int Size, double SensorWidth, double[] Pixels,
    int LaunchedRays, int ReturnedRays, int RecordedRays, double? Magnification,
    double? ImageLength, double? HeadPosition, string Message,
    AssemblyProbeMode Mode, double CollimatorPosition, bool MatchesSelectedConjugate);

/// <summary>
/// Assembly analysis over the shared single-surface kernel. No intersection,
/// propagation, refraction or reflection implementation is duplicated here.
/// Coordinates in the public results are relative to the first physical vertex.
/// </summary>
public sealed class AssemblyConjugates
{
    private readonly OpticalSurface[] _surfaces;
    private readonly double _wavelength;
    private readonly int _direction;
    private readonly double _datum;
    private readonly double _launchZ;
    private readonly IMaterial _air = new AirMaterial();

    public AssemblyConjugates(Optic optic, double wavelengthNanometers, bool fromRear = false)
    {
        ArgumentNullException.ThrowIfNull(optic);
        if (!double.IsFinite(wavelengthNanometers) || wavelengthNanometers <= 0)
            throw new ArgumentOutOfRangeException(nameof(wavelengthNanometers));
        if (optic.SurfaceGroup.Items.Count is < 3 or > 258)
            throw new ArgumentException("需要 1–256 个实体表面（不含物面和像面）。");
        _surfaces = optic.SurfaceGroup.Items.Select(surface => surface.Clone()).ToArray();
        // Geometrical preview has normalized brightness, not a coating/stray-light simulation.
        foreach (var surface in _surfaces)
        {
            surface.CoatingModel = new NoneCoatingModel();
            surface.ScatteringModel = null;
        }
        _wavelength = wavelengthNanometers;
        _direction = fromRear ? -1 : 1;
        _datum = _surfaces[1].CoordinateSystem.Origin.Z;
        var entrance = _surfaces[fromRear ? ^2 : 1];
        _launchZ = entrance.CoordinateSystem.Origin.Z - _direction * (2 * entrance.SemiDiameter + 10);
    }

    public IReadOnlyList<AssemblyConjugate> Calculate()
    {
        var rows = new List<AssemblyConjugate>();
        for (var index = 1; index < _surfaces.Length - 1; index++)
        {
            foreach (var kind in Enum.GetValues<AssemblyImageKind>())
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                rows.Add(Calculate(index, kind));
            }
        }
        return rows;
    }

    private AssemblyConjugate Calculate(int index, AssemblyImageKind kind)
    {
        var target = _surfaces[index];
        AssemblyConjugate Row(AssemblyImageStatus status, double? position, string message) =>
            new(target.Number, target.Label, kind, status, position, message);
        var issue = InspectPath(index);
        if (issue is not null) return Row(AssemblyImageStatus.Unsupported, null, issue);
        if (!target.IsReflective && Math.Abs(Before(index).RefractiveIndex(_wavelength)
            - target.MaterialAfter.RefractiveIndex(_wavelength)) < 1e-12)
            return Row(AssemblyImageStatus.NoReflection, null, "相邻介质折射率相同；本模型无界面反射像");

        // Derivatives of the EXISTING real-ray kernel give the axial first-order
        // conjugate. Extrapolate the residual derivatives before dividing: a
        // true infinite conjugate has zero height derivative, while the finite
        // probe still carries spherical aberration of order step squared.
        (double A, double B) Derivatives(double scale)
        {
            const double height = 0.001;
            const double slope = 0.00001;
            var a = Residual(index, kind, height * scale, 0) / (height * scale);
            var b = Residual(index, kind, 0, slope * scale) / (slope * scale);
            return (a, b);
        }
        static (double A, double B) Extrapolate((double A, double B) coarse, (double A, double B) fine) =>
            ((4 * fine.A - coarse.A) / 3, (4 * fine.B - coarse.B) / 3);
        var coarse = Derivatives(1);
        var middle = Derivatives(0.5);
        var fine = Derivatives(0.25);
        var estimate = Extrapolate(coarse, middle);
        var refined = Extrapolate(middle, fine);
        if (!double.IsFinite(estimate.A) || !double.IsFinite(estimate.B)
            || !double.IsFinite(refined.A) || !double.IsFinite(refined.B))
            return Row(AssemblyImageStatus.Failed, null, "近轴探测光线未到达目标面");
        if (Math.Abs(estimate.A) < 1e-11 && Math.Abs(refined.A) < 1e-11
            && Math.Abs(refined.B) > 1e-11 && Math.Abs(estimate.B - refined.B) < 1e-7 * (1 + Math.Abs(refined.B)))
            return Row(AssemblyImageStatus.Infinity, null, "共轭在无穷远；需平行光设置");
        var position = _launchZ + _direction * refined.B / refined.A - _datum;
        var previousPosition = _launchZ + _direction * estimate.B / estimate.A - _datum;
        if (!double.IsFinite(position) || !double.IsFinite(previousPosition)
            || Math.Abs(position - previousPosition) > 1e-5 * (1 + Math.Abs(position)))
            return Row(AssemblyImageStatus.Failed, null, "共轭接近奇点或探测步长未收敛");
        return Row(AssemblyImageStatus.Finite, position, "近轴找像位置；有限孔径像见右侧预览");
    }

    private double Residual(int index, AssemblyImageKind kind, double height, double slope)
    {
        var ray = new RealRay(new Vector3D(0, height, _launchZ),
            new Vector3D(0, slope, _direction), _wavelength).Normalize();
        foreach (var i in Prefix(index))
        {
            var next = Pass(_surfaces[i], ray, Incident(i), Transmitted(i), ignoreAperture: true);
            if (next is null) return double.NaN;
            ray = next;
        }
        var target = _surfaces[index].Clone();
        target.IsReflective = false;
        target.InteractionModel = new RefractiveReflectiveInteractionModel();
        var hit = Pass(target, ray, Incident(index), Incident(index), ignoreAperture: true);
        if (hit is null) return double.NaN;
        var point = target.CoordinateSystem.ToLocalPoint(hit.Origin);
        if (kind == AssemblyImageKind.Vertex) return point.Y;
        var normal = target.CoordinateSystem.ToGlobalDirection(target.Geometry.SurfaceNormal(point));
        // Zero tangential component: incident ray is normal to the sphere.
        return ray.Direction.Y * normal.Z - ray.Direction.Z * normal.Y;
    }

    public AssemblyImage Simulate(int surfaceNumber, AssemblyImageKind kind, AssemblyProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        Validate(probe);
        var index = Array.FindIndex(_surfaces, s => s.Number == surfaceNumber);
        if (index <= 0 || index >= _surfaces.Length - 1 || !Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(surfaceNumber));
        var conjugate = Calculate(index, kind);
        var focused = probe.Mode == AssemblyProbeMode.Focused;
        if (conjugate.Status is not (AssemblyImageStatus.Finite or AssemblyImageStatus.Infinity))
            throw new InvalidOperationException(conjugate.Message);
        if (focused && conjugate.Status == AssemblyImageStatus.Infinity)
            throw new InvalidOperationException("所选共轭在无穷远，请切换到无穷远（平行光）模式。");
        const int size = 320;
        var entranceIndex = _direction > 0 ? 1 : _surfaces.Length - 2;
        var entrance = _surfaces[entranceIndex];
        double? headZ = focused ? _datum + conjugate.PositionMillimeters!.Value
            + _direction * (probe.FocusOffset - probe.HeadFocalLength) : null;
        const double headSeparation = 10;
        var collimatorZ = headZ is { } position ? position - _direction * headSeparation
            : entrance.CoordinateSystem.Origin.Z - _direction * probe.InstrumentDistance;
        var pupilRadius = focused ? probe.HeadFocalLength * probe.NumericalAperture : probe.PupilDiameter / 2;
        var matched = focused ? probe.FocusOffset == 0 : conjugate.Status == AssemblyImageStatus.Infinity;
        var pixels = new double[size * size];
        if (Math.Abs(Incident(entranceIndex).RefractiveIndex(_wavelength) - 1) > 1e-9)
            return Empty("首版测量头要求外部介质为空气");
        // Check both axial and aperture-edge clearance before composing the probe path.
        var edgeSag = entrance.Geometry.Sag(0, Math.Min(entrance.SemiDiameter, pupilRadius));
        var frontmost = entrance.CoordinateSystem.Origin.Z
            + (_direction > 0 ? Math.Min(0, edgeSag) : Math.Max(0, edgeSag));
        if (!double.IsFinite(frontmost) || _direction * (frontmost - (headZ ?? collimatorZ)) <= 0.1)
            return Empty(focused ? "测量物镜将进入镜组；请增大测量物镜焦距/工作距离"
                : "自准直仪物镜将进入镜组；请增大仪器到入口顶点的距离");

        var detectorZ = collimatorZ - _direction * probe.CollimatorFocalLength;
        var outgoingCollimator = ProbeLens(collimatorZ, probe.CollimatorFocalLength, _direction);
        var outgoingHead = headZ is { } outZ ? ProbeLens(outZ, probe.HeadFocalLength, _direction) : null;
        var returningHead = headZ is { } inZ ? ProbeLens(inZ, probe.HeadFocalLength, -_direction) : null;
        var returningCollimator = ProbeLens(collimatorZ, probe.CollimatorFocalLength, -_direction);
        var detector = new OpticalSurface
        {
            Geometry = new PlaneGeometry(),
            CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, detectorZ)),
            InteractionModel = new RefractiveReflectiveInteractionModel(),
            CoatingModel = new NoneCoatingModel()
        };
        var mirror = _surfaces[index].Clone();
        mirror.IsReflective = true;
        mirror.InteractionModel = new RefractiveReflectiveInteractionModel(true);
        var prefix = Prefix(index).ToArray();
        var returnPrefix = prefix.Reverse().ToArray();

        RealRay? ImageRay(double x, double y, double pupilX, double pupilY)
        {
            var source = new Vector3D(x, y, detectorZ);
            var ray = new RealRay(source, new Vector3D(pupilX, pupilY, collimatorZ) - source,
                _wavelength).Normalize();
            ray = Pass(outgoingCollimator, ray, _air, _air, ignoreAperture: true);
            if (ray is null || !InsideProbe(ray, pupilRadius)) return null;
            if (outgoingHead is not null)
            {
                ray = Pass(outgoingHead, ray, _air, _air, ignoreAperture: true);
                if (ray is null || !InsideProbe(ray, pupilRadius)) return null;
            }
            foreach (var i in prefix)
            {
                ray = Pass(_surfaces[i], ray, Incident(i), Transmitted(i));
                if (ray is null) return null;
            }
            ray = Pass(mirror, ray, Incident(index), Incident(index));
            if (ray is null) return null;
            foreach (var i in returnPrefix)
            {
                ray = Pass(_surfaces[i], ray, Transmitted(i), Incident(i));
                if (ray is null) return null;
            }
            if (returningHead is not null)
            {
                ray = Pass(returningHead, ray, _air, _air, ignoreAperture: true);
                if (ray is null || !InsideProbe(ray, pupilRadius)) return null;
            }
            ray = Pass(returningCollimator, ray, _air, _air, ignoreAperture: true);
            if (ray is null || !InsideProbe(ray, pupilRadius)) return null;
            return Pass(detector, ray, _air, _air, ignoreAperture: true);
        }

        var launched = 0;
        var returned = 0;
        var recorded = 0;
        var alongSamples = Math.Clamp((int)Math.Ceiling(probe.ReticleLength / probe.SensorWidth * size * 1.5) | 1, 81, 1025);
        var acrossSamples = Math.Clamp((int)Math.Ceiling(probe.ReticleWidth / probe.SensorWidth * size * 1.5) | 1, 3, 33);
        // Sample both finite-width arms. Deterministic area/pupil samples are
        // deposited in fixed physical detector coordinates; no per-image stretch.
        for (var arm = 0; arm < 2; arm++)
            for (var along = 0; along < alongSamples; along++)
                for (var across = 0; across < acrossSamples; across++)
                {
                    ComputationCancellation.ThrowIfCancellationRequested();
                    var a = probe.ReticleLength * ((along + 0.5) / alongSamples - 0.5);
                    var b = probe.ReticleWidth * ((across + 0.5) / acrossSamples - 0.5);
                    if (arm == 1 && Math.Abs(a) < probe.ReticleWidth / 2) continue;
                    for (var px = -3; px <= 3; px++)
                        for (var py = -3; py <= 3; py++)
                        {
                            if (px * px + py * py > 9) continue;
                            launched++;
                            var ray = ImageRay(arm == 0 ? a : b, arm == 0 ? b : a,
                                px / 3.0 * pupilRadius, py / 3.0 * pupilRadius);
                            if (ray is null) continue;
                            returned++;
                            var ix = (int)Math.Floor((ray.Origin.X / probe.SensorWidth + 0.5) * size);
                            var iy = (int)Math.Floor((0.5 - ray.Origin.Y / probe.SensorWidth) * size);
                            if (ix < 0 || ix >= size || iy < 0 || iy >= size) continue;
                            pixels[iy * size + ix] += 1;
                            recorded++;
                        }
                }
        const double differentialField = 0.0001;
        var positive = ImageRay(differentialField, 0, 0, 0);
        var negative = ImageRay(-differentialField, 0, 0, 0);
        double? magnification = positive is null || negative is null ? null
            : (positive.Origin.X - negative.Origin.X) / (2 * differentialField);
        var modeMessage = focused ? "附加测量物镜模式。" : matched
            ? "无穷远模式：平行出射，所选共轭为 ∞。"
            : "无穷远模式：所选共轭为有限位置，当前未对焦到此共轭；返回像可能模糊或受遮挡。";
        return new AssemblyImage(size, probe.SensorWidth, pixels, launched, returned, recorded,
            magnification, magnification is { } m ? Math.Abs(m) * probe.ReticleLength : null,
            headZ - _datum, modeMessage + (recorded == 0 ? "没有光线落入探测范围；检查孔径和测量头参数。"
                : "几何往返追迹；理想仪器、归一化亮度，不含衍射及镀膜亮度。"),
            probe.Mode, collimatorZ - _datum, matched);

        AssemblyImage Empty(string reason) => new(size, probe.SensorWidth, pixels, 0, 0, 0,
            null, null, headZ - _datum, reason, probe.Mode, collimatorZ - _datum, matched);
    }

    private RealRay? Pass(OpticalSurface surface, RealRay ray, IMaterial before, IMaterial after,
        bool ignoreAperture = false)
    {
        // The shared kernel owns all propagation and surface interactions.
        var hit = surface.TraceRayValue(ray, before, after, 0, 0, ignoreAperture);
        if (hit.StopTracing || hit.Sample.Vignetted || hit.InteractionKind == RayInteractionKind.TotalInternalReflection)
            return null;
        if (!ignoreAperture && surface.SemiDiameter > 0)
        {
            var point = surface.CoordinateSystem.ToLocalPoint(hit.Ray.Origin);
            if (point.X * point.X + point.Y * point.Y > surface.SemiDiameter * surface.SemiDiameter)
                return null;
        }
        return hit.Ray;
    }

    private static bool InsideProbe(RealRay ray, double radius) =>
        ray.Origin.X * ray.Origin.X + ray.Origin.Y * ray.Origin.Y <= radius * radius * (1 + 1e-10);

    private static OpticalSurface ProbeLens(double z, double focalLength, int direction) => new()
    {
        Geometry = new PlaneGeometry(),
        CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, z), RotationYDegrees: direction > 0 ? 0 : 180),
        InteractionModel = new ThinLensInteractionModel(focalLength),
        CoatingModel = new NoneCoatingModel()
    };

    private IEnumerable<int> Prefix(int target)
    {
        for (var i = _direction > 0 ? 1 : _surfaces.Length - 2; i != target; i += _direction)
            yield return i;
    }

    private IMaterial Before(int index) => _surfaces[index - 1].MaterialAfter;
    private IMaterial Incident(int index) => _direction > 0 ? Before(index) : _surfaces[index].MaterialAfter;
    private IMaterial Transmitted(int index) => _direction > 0 ? _surfaces[index].MaterialAfter : Before(index);

    private string? InspectPath(int target)
    {
        foreach (var i in Prefix(target).Append(target))
        {
            var surface = _surfaces[i];
            var c = surface.CoordinateSystem;
            if (Math.Abs(c.Origin.X) + Math.Abs(c.Origin.Y) + Math.Abs(c.RotationXDegrees)
                + Math.Abs(c.RotationYDegrees) + Math.Abs(c.RotationZDegrees) > 1e-12)
                return $"首版仅支持共轴系统；表面 {surface.Number} 含偏心或倾斜";
            if (surface.Geometry is not PlaneGeometry and not StandardGeometry
                || surface.Geometry is StandardGeometry { Conic: not 0 })
                return $"表面 {surface.Number} 非球面/特殊面型；首版仅支持球面和平面";
            if (surface.InteractionModel is not RefractiveReflectiveInteractionModel)
                return $"表面 {surface.Number} 的特殊交互模型暂不支持";
            if (i != target && surface.IsReflective)
                return $"表面 {surface.Number} 为前置反射面，遮挡当前观察路径";
            if (!double.IsFinite(c.Origin.Z) || (i > 1 && c.Origin.Z < _surfaces[i - 1].CoordinateSystem.Origin.Z))
                return "首版不支持折返或负厚度处方";
            if (Before(i) is UnresolvedMaterial || surface.MaterialAfter is UnresolvedMaterial)
                return $"表面 {surface.Number} 的材料未解析";
        }
        return null;
    }

    private static void Validate(AssemblyProbe p)
    {
        if (!Enum.IsDefined(p.Mode)
            || !double.IsFinite(p.CollimatorFocalLength) || p.CollimatorFocalLength is < 1 or > 10000
            || !double.IsFinite(p.ReticleLength) || p.ReticleLength is < 0.01 or > 20
            || !double.IsFinite(p.ReticleWidth) || p.ReticleWidth <= 0 || p.ReticleWidth > p.ReticleLength
            || !double.IsFinite(p.SensorWidth) || p.SensorWidth is < 0.1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(p), "测量头、叉丝或探测范围参数无效。");
        if (p.Mode == AssemblyProbeMode.Focused
            && (!double.IsFinite(p.HeadFocalLength) || p.HeadFocalLength is < 1 or > 10000
                || !double.IsFinite(p.NumericalAperture) || p.NumericalAperture is < 0.001 or > 0.2
                || !double.IsFinite(p.FocusOffset) || Math.Abs(p.FocusOffset) > 10000))
            throw new ArgumentOutOfRangeException(nameof(p), "附加测量物镜参数无效。");
        if (p.Mode == AssemblyProbeMode.Infinity
            && (!double.IsFinite(p.PupilDiameter) || p.PupilDiameter is < 0.1 or > 200
                || !double.IsFinite(p.InstrumentDistance) || p.InstrumentDistance is < 0.1 or > 10000))
            throw new ArgumentOutOfRangeException(nameof(p), "无穷远模式的通光直径或仪器距离无效。");
    }
}
