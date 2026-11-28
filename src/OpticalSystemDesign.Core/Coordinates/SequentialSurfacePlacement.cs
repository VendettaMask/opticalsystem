using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Core.Coordinates;

/// <summary>A native coordinate operation, separate from a surface's computed world pose.</summary>
public sealed record CoordinateBreakParameters(double X, double Y, double TiltX, double TiltY, double TiltZ, bool TiltFirst)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CoordinateFrameReturn? Return { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? EditingRestriction { get; init; }
    private IReadOnlyList<CoordinateChiefRaySolve> _chiefRaySolves = Array.Empty<CoordinateChiefRaySolve>();
    private IReadOnlyList<CoordinateParameterPickup> _pickups = Array.Empty<CoordinateParameterPickup>();
    public IReadOnlyList<CoordinateChiefRaySolve> ChiefRaySolves { get => _chiefRaySolves; init => _chiefRaySolves = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CoordinateParameterPickup> Pickups { get => _pickups; init => _pickups = Array.AsReadOnly(value.ToArray()); }
    public double Value(int parameter) => parameter switch { 0 => X, 1 => Y, 2 => TiltX, 3 => TiltY, 4 => TiltZ, _ => throw new ArgumentOutOfRangeException(nameof(parameter)) };
    public CoordinateBreakParameters WithValue(int parameter, double value) => parameter switch
    {
        0 => this with { X = value },
        1 => this with { Y = value },
        2 => this with { TiltX = value },
        3 => this with { TiltY = value },
        4 => this with { TiltZ = value },
        _ => throw new ArgumentOutOfRangeException(nameof(parameter))
    };
    public CoordinateSystem Apply(CoordinateSystem incoming)
    {
        var origin = incoming.Origin;
        var rotation = incoming.RotationMatrix();
        Apply(ref origin, ref rotation);
        return CoordinateSystem.FromRotation(origin, rotation);
    }

    internal void Apply(ref Vector3D origin, ref Matrix3x3 rotation)
    {
        var decenter = new Vector3D(X, Y, 0);
        if (TiltFirst)
        {
            rotation *= new CoordinateSystem(Vector3D.Zero, TiltX, TiltY, TiltZ).RotationMatrix();
            origin += rotation.Transform(decenter);
        }
        else
        {
            origin += rotation.Transform(decenter);
            rotation *= new CoordinateSystem(Vector3D.Zero, -TiltX, -TiltY, -TiltZ).RotationMatrix().Transpose();
        }
    }
}

public sealed record SequentialSurfacePlacement(double X, double Y, double Z, double RotationX, double RotationY, double RotationZ,
    double ObjectDistanceAtImport = 0, CoordinateBreakParameters? Break = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public CoordinateSystem Frame => new(new Vector3D(X, Y, Z), RotationX, RotationY, RotationZ);
    public static SequentialSurfacePlacement From(CoordinateSystem frame, double objectDistance = 0, CoordinateBreakParameters? coordinateBreak = null) =>
        new(frame.Origin.X, frame.Origin.Y, frame.Origin.Z, frame.RotationXDegrees, frame.RotationYDegrees, frame.RotationZDegrees, objectDistance, coordinateBreak);

    public static void Rebuild(IReadOnlyList<OpticalSurface> surfaces, Optic? optic = null)
    {
        if (surfaces.Count == 0) return;
        optic?.InvalidateRayTraceCache();
        if (surfaces.Any(surface => surface.Placement is null))
            throw new NotSupportedException("坐标变量的依赖链不能通过未映射的表面插入或删除来修改。");
        var root = surfaces[0].Placement!;
        var distance = double.IsFinite(surfaces[0].Thickness) ? surfaces[0].Thickness : 0;
        surfaces[0].CoordinateSystem = root.Frame with { Origin = root.Frame.Origin + new Vector3D(0, 0, root.ObjectDistanceAtImport - distance) };
        for (var index = 1; index < surfaces.Count; index++)
        {
            var surface = surfaces[index]; var previous = surfaces[index - 1]; var placement = surface.Placement!;
            var gap = double.IsFinite(previous.Thickness) ? previous.Thickness : 0;
            var afterPrevious = previous.CoordinateSystem with { Origin = previous.CoordinateSystem.ToGlobalPoint(new Vector3D(0, 0, gap)) };
            var incoming = placement.Frame.MoveWithFrame(CoordinateSystem.Global, afterPrevious);
            var operation = placement.Break;
            if (operation is not null)
            {
                if (operation.Return is { } frameReturn)
                {
                    if (frameReturn.SurfaceNumber < 0 || frameReturn.SurfaceNumber >= index || !Enum.IsDefined(frameReturn.Mode))
                        throw new InvalidOperationException("坐标返回必须引用前面的表面。");
                    var target = surfaces[frameReturn.SurfaceNumber].CoordinateSystem;
                    incoming = target with { Origin = frameReturn.Mode == CoordinateReturnMode.Orientation ? incoming.Origin : target.Origin };
                }
                foreach (var pickup in operation.Pickups)
                {
                    if (pickup.SourceSurface < 0 || pickup.SourceSurface >= index || surfaces[pickup.SourceSurface].Placement?.Break is not { } source)
                        throw new InvalidOperationException("Coordinate pickup must reference an earlier coordinate operation.");
                    operation = operation.WithValue(pickup.Parameter, source.Value(pickup.SourceParameter) * pickup.Scale + pickup.Offset);
                }
                if (operation.ChiefRaySolves.Count > 0)
                    operation = CoordinateChiefRaySolves.Resolve(optic ?? throw new InvalidOperationException("Dynamic chief-ray solves require their owning optic."), index, incoming, operation);
                if (operation != placement.Break) surface.Placement = placement = placement with { Break = operation };
            }
            var pose = operation?.Apply(incoming) ?? incoming;
            if (placement.Break is not null)
                surface.Geometry = new PlaneGeometry(pose.ToLocalDirection(incoming.ToGlobalDirection(new Vector3D(0, 0, 1))));
            surface.CoordinateSystem = pose;
        }
    }
}

public enum CoordinateReturnMode { Orientation, PositionAndOrientation }
public sealed record CoordinateFrameReturn(int SurfaceNumber, CoordinateReturnMode Mode);
