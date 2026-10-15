using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Application.Services;

/// <summary>Stateless evaluation of a captured prescription; never reads the live workspace.</summary>
public static class ManufacturingGeometryService
{
    public static double? Sag(SurfaceRowDto surface, double height) =>
        ManufacturingProfileGeometry.Sag(Geometry(surface), height);

    public static double? SlopeDegrees(SurfaceRowDto surface, double height) =>
        ManufacturingProfileGeometry.SlopeDegrees(Geometry(surface), height);

    public static double MaximumRadius(SurfaceRowDto surface) =>
        ManufacturingProfileGeometry.MaximumRadius(Geometry(surface));

    public static double? MinimumThickness(SurfaceRowDto front, SurfaceRowDto back, double semiDiameter) =>
        ManufacturingProfileGeometry.MinimumThickness(Geometry(front), Geometry(back), front.Thickness, semiDiameter);

    public static void EnsureSupported(SurfaceRowDto surface)
    {
        if (surface.Inspection is { } pose
            && (Math.Abs(pose.OriginX) > 1e-12 || Math.Abs(pose.OriginY) > 1e-12
                || Math.Abs(pose.TiltXDegrees) > 1e-12 || Math.Abs(pose.TiltYDegrees) > 1e-12))
            throw new NotSupportedException("当前径向制造评估/单片图仅支持共轴、无倾斜的表面；偏心倾斜系统请使用总体布局图。");
        _ = MaximumRadius(surface);
    }

    private static IGeometry Geometry(SurfaceRowDto surface)
    {
        if (!surface.GeometryComputable)
            throw new NotSupportedException($"表面 {surface.Number} 的面型不可计算。");
        if (surface.GeometryDefinition is not { } definition)
        {
            // Compatibility for callers constructing old standard-only DTOs. Never infer
            // an asphere/freeform from its base radius when the full snapshot is absent.
            return surface.GeometryKind switch
            {
                "平面" or "plane" => new PlaneGeometry(),
                "标准球面/圆锥" or "standard" => new StandardGeometry(surface.Radius, surface.Conic),
                _ => throw new NotSupportedException($"表面 {surface.Number} 缺少完整面型快照，不能按标准面计算。")
            };
        }
        var numbers = new Dictionary<string, double>(definition.Numbers);
        // Radius/conic remain editable prescription fields; higher terms come only
        // from the captured geometry, including DTO copies with edited base parameters.
        if (numbers.ContainsKey("radius")) numbers["radius"] = surface.Radius;
        if (numbers.ContainsKey("conic")) numbers["conic"] = surface.Conic;
        return ComponentSnapshotFactory.ToGeometry(
            new ComponentSnapshot(definition.Kind, numbers, new Dictionary<string, string>()),
            surface.Radius, surface.Conic);
    }
}
