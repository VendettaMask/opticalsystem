using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.FileIO;

internal static partial class ZemaxZmxReader
{
    private sealed record ZemaxSurfaceFrameOffset(bool TiltFirst, int Pickup, Vector3D Decenter, Vector3D TiltDegrees)
    {
        public bool HasTransform => Decenter != Vector3D.Zero || TiltDegrees != Vector3D.Zero;
        public ZemaxSurfaceFrameOffset Inverse() => new(!TiltFirst, 0, Decenter * -1, TiltDegrees * -1);
    }

    private static void ReadSurfaceFrameOffset(ZemaxSurface surface, string[] tokens)
    {
        const string command = "SCBD";
        if (tokens.Length != 9)
            throw new InvalidDataException("Zemax SCBD requires its complete tilt and decenter record.");
        var stage = RequiredInt(tokens, 1, command);
        var order = RequiredInt(tokens, 2, command);
        var pickup = RequiredInt(tokens, 3, command);
        if (stage is not (1 or 2) || order is not (0 or 1) || pickup < 0 || stage == 1 && pickup != 0)
            throw new InvalidDataException("Invalid Zemax SCBD stage, operation order or pickup.");
        if (stage == 1 ? surface.BeforeFrameOffset is not null : surface.AfterFrameOffset is not null)
            throw new InvalidDataException("Duplicate Zemax SCBD record for the same surface and stage.");

        var offset = new ZemaxSurfaceFrameOffset(order == 1, pickup,
            new Vector3D(Value(4, surface.LengthScaleToMillimeters), Value(5, surface.LengthScaleToMillimeters), 0),
            new Vector3D(Value(6), Value(7), Value(8)));
        if (stage == 1) surface.BeforeFrameOffset = offset;
        else surface.AfterFrameOffset = offset;

        double Value(int column, double scale = 1)
        {
            var value = RequiredDouble(tokens, column, command) * scale;
            return double.IsFinite(value) ? value : throw new InvalidDataException("Zemax SCBD requires finite offsets and angles.");
        }
    }

    private static void ResolveSurfaceFramePickups(IReadOnlyList<ZemaxSurface> surfaces)
    {
        var byNumber = surfaces.ToDictionary(surface => surface.Number);
        foreach (var surface in surfaces)
        {
            if (surface.BeforeFrameOffset is null && surface.AfterFrameOffset is null) continue;
            if (surface.Number == 0 || surface.Type == "COORDBRK")
                throw new NotSupportedException("Zemax SCBD is supported on physical surfaces following the object.");
            if (surface.AfterFrameOffset is not { Pickup: > 0 } after) continue;
            var sourceNumber = surface.Number - (after.Pickup - 1) / 2;
            if (!byNumber.TryGetValue(sourceNumber, out var source))
                throw new InvalidDataException("Zemax SCBD pickup refers to a missing surface.");
            surface.AfterFrameOffset = after.Pickup % 2 == 0 ? source.BeforeFrameOffset?.Inverse() : source.BeforeFrameOffset;
        }
    }

    private static void ApplySurfaceFrameOffset(ZemaxSurfaceFrameOffset? offset, ref Vector3D origin, ref Matrix3x3 rotation)
    {
        if (offset is null) return;
        var (x, y, z) = offset.TiltDegrees;
        if (offset.TiltFirst)
        {
            rotation *= RotationMatrix(x, y, z);
            origin += rotation.Transform(offset.Decenter);
        }
        else
        {
            origin += rotation.Transform(offset.Decenter);
            // Intrinsic X-Y-Z rotations multiply on the right in this order.
            rotation *= RotationMatrix(-x, -y, -z).Transpose();
        }
    }
}
