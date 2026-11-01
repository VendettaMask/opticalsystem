using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.Interactions;

/// <summary>An ideal plane lens with air powers in mm^-1 and a beam-dependent phase.</summary>
public sealed class ParaxialLensInteractionModel : IInteractionModel
{
    public ParaxialLensInteractionModel(double powerX, double powerY, int opdMode = 1)
    {
        if (!double.IsFinite(powerX) || !double.IsFinite(powerY)) throw new ArgumentOutOfRangeException(nameof(powerX));
        if (powerX != powerY && opdMode != 1) throw new NotSupportedException("独立 X/Y 近轴光焦度使用光束积分 OPD 模式 1。");
        if (opdMode is not (0 or 1 or 2)) throw new NotSupportedException("近轴面支持模式 1/2，以及已证明准直光束的模式 0；其他共轭模式仍需验证。");
        PowerX = powerX; PowerY = powerY; OpdMode = opdMode;
    }

    public string Kind => "paraxial_lens";
    public double PowerX { get; }
    public double PowerY { get; }
    public int OpdMode { get; }

    internal Vector3D Outgoing(Vector3D incident, Vector3D position, double before, double after)
    {
        if (Math.Abs(incident.Z) < 1e-14 || before <= 0 || after <= 0)
            throw new ArithmeticException("近轴面需要非掠入射方向和正折射率。");
        var orientation = Math.CopySign(1, incident.Z);
        var d = new Vector3D((before * incident.X / incident.Z - orientation * PowerX * position.X) / after,
            (before * incident.Y / incident.Z - orientation * PowerY * position.Y) / after, 1);
        return Math.CopySign(1, incident.Z) * d / d.Length;
    }

    public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
    {
        if (context.Geometry is not OpticalSystemDesign.Core.Geometries.PlaneGeometry)
            throw new NotSupportedException("近轴作用必须位于平面上。");
        if (context.IsReflective) throw new NotSupportedException("反射近轴面的相位和方向约定尚未验证。");
        var direction = Outgoing(ray.Direction, ray.Origin, context.RefractiveIndexBefore, context.RefractiveIndexAfter);
        var phase = PowerX == 0 && PowerY == 0 && context.RefractiveIndexBefore == context.RefractiveIndexAfter ? 0
            : context.SurfacePhaseOpticalPath?.Invoke(ray, context)
                ?? throw new NotSupportedException("近轴面相位需要共享顺序追迹器提供物方光束及主光线，不能只用单个界面计算。");
        return new(ray with { Direction = direction, OpticalPathDifference = ray.OpticalPathDifference + phase, IsNormalized = true },
            RayInteractionKind.Transmitted);
    }

    public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context) => ray with
    { Angle = (context.RefractiveIndexBefore * ray.Angle - PowerY * ray.Height) / context.RefractiveIndexAfter };

    public IInteractionModel Clone() => new ParaxialLensInteractionModel(PowerX, PowerY, OpdMode);
}
