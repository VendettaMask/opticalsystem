using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Core.NonSequential;

public enum NonSequentialBsdfKind { LambertianReflection, Measured }
public sealed record BsdfMaterialAxis(double X = 1, double Y = 0, double Z = 0)
{
    [JsonIgnore] public Vector3D Direction => new(X, Y, Z);
    internal void Validate() => _ = BsdfContext.Unit(Direction);
}

public sealed record NonSequentialSurfaceBsdf(NonSequentialBsdfKind Kind, double Reflectance,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MeasuredBsdfData? MeasuredData = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BsdfMaterialAxis? MaterialAxis = null)
{
    public IBsdf Create()
    {
        if (MaterialAxis is not null)
        {
            MaterialAxis.Validate();
            if (MeasuredData?.Coordinates != BsdfCoordinates.FixedMaterialAxes)
                throw new InvalidDataException("材料轴只适用于固定轴各向异性数据。");
        }
        return Kind switch
        {
            NonSequentialBsdfKind.LambertianReflection when MeasuredData is null => new LambertianReflectionBsdf(Reflectance),
            NonSequentialBsdfKind.Measured when MeasuredData is not null && Reflectance == 0 => new MeasuredBsdf(MeasuredData),
            _ => throw new InvalidDataException("BSDF 模型与实测载荷不一致；实测值不额外乘 Lambertian 反射率。")
        };
    }
    internal void Validate(NonSequentialObjectParameters parameters)
    {
        _ = Create();
        var behavior = parameters switch
        {
            PlaneRectangleParameters p => p.Behavior, SphereParameters p => p.Behavior,
            CylinderParameters p => p.Behavior, BoxParameters p => p.Behavior, MeshObjectParameters p => p.Behavior,
            _ => (NonSequentialSurfaceBehavior?)null
        };
        if (MeasuredData?.HasTransmission == true)
        {
            if (!MeasuredData.HasBothSides || behavior != NonSequentialSurfaceBehavior.Refractive)
                throw new InvalidDataException("BTDF 需要正反两侧测量和折射交互，用声明的介质更新传播；不另加 Fresnel/Snell。");
            return;
        }
        if (behavior is not (NonSequentialSurfaceBehavior.Reflective or NonSequentialSurfaceBehavior.Absorbing))
            throw new InvalidDataException("Lambertian BSDF 仅用于不透明反射或吸收表面，替代其整个反射模型；不与折射/Fresnel 叠加。");
    }
    public (Vector3D X, Vector3D Y) WorldAxes(NonSequentialDocument document, Guid objectId)
    {
        var x = BsdfContext.Unit((MaterialAxis ?? new()).Direction);
        var y = BsdfContext.Unit(BsdfContext.Cross(Math.Abs(x.Z) < .9 ? new(0, 0, 1) : new Vector3D(0, 1, 0), x));
        return (BsdfContext.Unit(document.ToWorldDirection(objectId, x)), BsdfContext.Unit(document.ToWorldDirection(objectId, y)));
    }
    public static string? Protocol(NonSequentialDocument document) => document.Objects.Any(o => o.Enabled && o.SurfaceBsdf?.MeasuredData?.ComputationProtocol == MeasuredBsdfData.AbsoluteRbfXyzProtocol)
        ? MeasuredBsdfData.AbsoluteRbfXyzProtocol : document.Objects.Any(o => o.Enabled && o.SurfaceBsdf?.MeasuredData?.ComputationProtocol == MeasuredBsdfData.LogInterpolationProtocol)
        ? MeasuredBsdfData.LogInterpolationProtocol : document.Objects.Any(o => o.Enabled && o.SurfaceBsdf?.MeasuredData?.ComputationProtocol == MeasuredBsdfData.PolarSupportProtocol)
        ? MeasuredBsdfData.PolarSupportProtocol : document.Objects.Any(o => o.Enabled && o.SurfaceBsdf?.MeasuredData?.ComputationProtocol == MeasuredBsdfData.PositiveXyzProtocol)
        ? MeasuredBsdfData.PositiveXyzProtocol : document.Objects.Any(o => o.Enabled && o.SurfaceBsdf?.MeasuredData?.ComputationProtocol == MeasuredBsdfData.GamutXyzProtocol)
        ? MeasuredBsdfData.GamutXyzProtocol : document.Objects.Any(o => o.Enabled && o.SurfaceBsdf?.MeasuredData?.ComputationProtocol == MeasuredBsdfData.SpeosRuntimeWhiteProtocol)
        ? MeasuredBsdfData.SpeosRuntimeWhiteProtocol : document.Objects.Any(o => o.Enabled && o.SurfaceBsdf?.MeasuredData?.ComputationProtocol == MeasuredBsdfData.SpeosThetaProtocol)
        ? MeasuredBsdfData.SpeosThetaProtocol : document.Objects.Any(o => o.Enabled && o.SurfaceBsdf?.MeasuredData?.ComputationProtocol == MeasuredBsdfData.EnhancedProtocol)
        ? MeasuredBsdfData.EnhancedProtocol : document.Objects.Any(o => o.Enabled && o.SurfaceBsdf?.MeasuredData?.ComputationProtocol == MeasuredBsdfData.SpecularProtocol)
        ? MeasuredBsdfData.SpecularProtocol : document.Objects.Any(o => o.Enabled && o.SurfaceBsdf?.MeasuredData?.ComputationProtocol == MeasuredBsdfData.FixedAxisProtocol)
        ? MeasuredBsdfData.FixedAxisProtocol : document.Objects.Any(o => o.Enabled && o.SurfaceBsdf?.Kind == NonSequentialBsdfKind.Measured)
        ? MeasuredBsdfData.Protocol : document.Objects.Any(o => o.Enabled && o.SurfaceBsdf is not null) ? "LambertianReflection/1" : null;
}
