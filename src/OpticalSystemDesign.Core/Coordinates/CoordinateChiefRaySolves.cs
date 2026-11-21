using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.Core.Coordinates;

public sealed record CoordinateChiefRaySolve(int Parameter, int FieldNumber, int WavelengthNumber);
public sealed record CoordinateParameterPickup(int Parameter, int SourceSurface, int SourceParameter, double Scale, double Offset);

internal static class CoordinateChiefRaySolves
{
    public static CoordinateBreakParameters Resolve(Optic optic, int surfaceNumber, CoordinateSystem incoming, CoordinateBreakParameters operation)
    {
        if (operation.ChiefRaySolves.Count == 0) return operation;
        if (optic.RayAimingEnabled && optic.SurfaceGroup.Items.Any(surface => surface.IsStop && surface.Number >= surfaceNumber))
            throw Failure("Ray aiming cannot depend on a stop after its coordinate chief-ray solve.");
        var rays = new Dictionary<(int Field, int Wave), (Vector3D Point, Vector3D Direction)>();
        (Vector3D Point, Vector3D Direction) Ray(CoordinateChiefRaySolve solve)
        {
            var identity = (solve.FieldNumber, solve.WavelengthNumber);
            if (rays.TryGetValue(identity, out var existing)) return existing;
            if (solve.FieldNumber < 1 || solve.FieldNumber > optic.Fields.Count || solve.WavelengthNumber < 0 || solve.WavelengthNumber > optic.Wavelengths.Count)
                throw Failure("Chief-ray solve references an unavailable field or wavelength.");
            var field = optic.Fields[solve.FieldNumber - 1];
            var wavelength = solve.WavelengthNumber == 0 ? optic.Wavelengths.First(wave => wave.IsPrimary) : optic.Wavelengths[solve.WavelengthNumber - 1];
            var ray = optic.SequentialRayTracer.RayGenerator.GenerateAtPhysicalField(field.X, field.Y, 0, 0, wavelength.Micrometers, optic.RayAimingEnabled).Rays.Single();
            var sample = optic.SequentialRayTracer.TraceToSurface(ray, surfaceNumber - 1);
            if (sample is null || sample.Vignetted) throw Failure("Chief ray cannot reach the coordinate solve.");
            var result = (sample.Position, sample.Direction); rays.Add(identity, result); return result;
        }
        // The native order is also the fitting order. Each solve uses the same physical incident chief ray.
        var order = operation.TiltFirst ? new[] { 2, 3, 0, 1 } : new[] { 0, 1, 2, 3 };
        foreach (var parameter in order)
        {
            var solve = operation.ChiefRaySolves.SingleOrDefault(solve => solve.Parameter == parameter);
            if (solve is null) continue;
            var ray = Ray(solve);
            if (parameter < 2)
            {
                var frame = operation.TiltFirst ? (operation with { X = 0, Y = 0 }).Apply(incoming) : incoming;
                var point = frame.ToLocalPoint(ray.Point); var direction = frame.ToLocalDirection(ray.Direction);
                if (Math.Abs(direction.Z) > 1e-12) point -= direction * (point.Z / direction.Z);
                else if (Math.Abs(point.Z) > 1e-8) throw Failure("Chief ray is parallel to the coordinate solve plane.");
                operation = operation.WithValue(parameter, parameter == 0 ? point.X : point.Y);
            }
            else
            {
                double Residual(double angle)
                {
                    var direction = operation.WithValue(parameter, angle).Apply(incoming).ToLocalDirection(ray.Direction);
                    return parameter == 2 ? direction.Y : direction.X;
                }
                var zero = Residual(0); var quarter = Residual(90); var half = Residual(180);
                var constant = (zero + half) / 2; var cosine = (zero - half) / 2; var sine = quarter - constant;
                var amplitude = double.Hypot(cosine, sine);
                if (amplitude < 1e-15)
                {
                    if (Math.Abs(constant) > 1e-12) throw Failure("The tilt chief-ray solve is singular.");
                    continue;
                }
                var ratio = -constant / amplitude;
                if (Math.Abs(ratio) > 1 + 1e-12) throw Failure("The tilt chief-ray solve has no real solution for this operation order.");
                var phase = Math.Atan2(sine, cosine); var angle = Math.Acos(Math.Clamp(ratio, -1, 1));
                var current = operation.Value(parameter);
                double Nearby(double radians)
                {
                    var degrees = radians * 180 / Math.PI;
                    return degrees + 360 * Math.Round((current - degrees) / 360);
                }
                var first = Nearby(phase + angle); var second = Nearby(phase - angle);
                var solution = Math.Abs(first - current) <= Math.Abs(second - current) ? first : second;
                if (Math.Abs(Residual(solution)) > 1e-10) throw Failure("The tilt chief-ray solve did not satisfy its angle constraint.");
                operation = operation.WithValue(parameter, solution);
            }
        }
        return operation;
    }
    private static OptimizationEvaluationException Failure(string reason) => new("Coordinate chief-ray solve", reason);
}
