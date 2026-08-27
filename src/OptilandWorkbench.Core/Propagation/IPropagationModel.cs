using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Propagation;

public interface IPropagationModel
{
    string Kind { get; }

    RealRay Propagate(RealRay ray, double distance);

    IPropagationModel Clone();
}

public sealed class HomogeneousPropagationModel : IPropagationModel
{
    public string Kind => "homogeneous";

    public RealRay Propagate(RealRay ray, double distance)
    {
        var propagated = ray with
        {
            Origin = ray.Origin + (ray.Direction * distance)
        };
        return propagated.IsNormalized ? propagated : propagated.Normalize();
    }

    public IPropagationModel Clone() => new HomogeneousPropagationModel();
}

/// <summary>
/// Applies one radial direction correction at the entrance of a homogeneous
/// propagation segment. This is an explicit approximation and does not solve
/// the eikonal equation or integrate a continuously varying refractive index.
/// </summary>
public class EntranceDirectionApproximationPropagationModel : IPropagationModel
{
    public const string Limitation =
        "入口方向近似只在传播段入口修正一次方向；不求解 eikonal/Hamilton 方程，"
        + "不提供曲线路径、表面事件检测或连续 OPL 积分。";

    public EntranceDirectionApproximationPropagationModel(double radialDirectionCoefficient)
    {
        if (!double.IsFinite(radialDirectionCoefficient))
        {
            throw new ArgumentOutOfRangeException(
                nameof(radialDirectionCoefficient),
                "The entrance radial direction coefficient must be finite.");
        }

        RadialDirectionCoefficient = radialDirectionCoefficient;
    }

    public string Kind => "entrance-direction-approximation";

    public double RadialDirectionCoefficient { get; }

    public RealRay Propagate(RealRay ray, double distance)
    {
        if (!double.IsFinite(distance) || distance < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance),
                "Propagation distance must be finite and non-negative.");
        }

        var normalizedRay = ray.IsNormalized ? ray : ray.Normalize();
        var bentDirection = normalizedRay.Direction + new Backend.Vector3D(
            -normalizedRay.Origin.X * RadialDirectionCoefficient,
            -normalizedRay.Origin.Y * RadialDirectionCoefficient,
            0);
        var directionLength = bentDirection.Length;
        if (!double.IsFinite(directionLength) || directionLength <= 1e-12)
        {
            throw new InvalidOperationException(
                "入口方向近似产生了无效方向；请减小径向方向系数或改用均匀介质传播。"
                + Limitation);
        }

        bentDirection /= directionLength;
        return normalizedRay with
        {
            Origin = normalizedRay.Origin + (bentDirection * distance),
            Direction = bentDirection,
            IsNormalized = true
        };
    }

    public virtual IPropagationModel Clone() =>
        new EntranceDirectionApproximationPropagationModel(RadialDirectionCoefficient);
}

/// <summary>
/// Source-compatibility alias for the former, incorrectly named model.
/// </summary>
[Obsolete(
    "GrinPropagationModel is not a GRIN solver. Use "
    + "EntranceDirectionApproximationPropagationModel. "
    + EntranceDirectionApproximationPropagationModel.Limitation)]
public sealed class GrinPropagationModel : EntranceDirectionApproximationPropagationModel
{
    public GrinPropagationModel(double radialGradient)
        : base(radialGradient)
    {
    }

    public double RadialGradient => RadialDirectionCoefficient;

    public override IPropagationModel Clone() => new GrinPropagationModel(RadialGradient);
}
