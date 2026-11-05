using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.Application.Runtime;

public partial class WorkbenchRuntime
{
    private void ConfigureAdditionalCompensators(Optic optic, Tolerancing tolerancing,
        ToleranceCompensationAlgorithm algorithm, IReadOnlyList<ToleranceCompensatorDto>? definitions)
    {
        tolerancing.CompensationOptimizer = algorithm switch
        {
            ToleranceCompensationAlgorithm.DampedLeastSquares => "Damped Least Squares",
            ToleranceCompensationAlgorithm.CoordinatePatternSearch => "Coordinate Pattern Search",
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };
        if (definitions is null) return;
        var pivots = definitions.Where(item => item.Pivot is not null)
            .GroupBy(item => (item.SurfaceNumber, item.EndSurfaceNumber))
            .ToDictionary(group => group.Key, group => group.First().Pivot!);
        foreach (var definition in definitions)
        {
            var surface = FindSurface(optic, definition.SurfaceNumber);
            var group = definition.Kind.IsElement();
            var pivot = pivots.TryGetValue((definition.SurfaceNumber, definition.EndSurfaceNumber), out var offset) && group
                ? new Vector3D(offset.X, offset.Y, offset.Z) : Vector3D.Zero;
            var axis = definition.Kind switch
            {
                ToleranceCompensatorKind.ElementDecenterX => ToleranceCompensatorKind.DecenterX,
                ToleranceCompensatorKind.ElementDecenterY => ToleranceCompensatorKind.DecenterY,
                ToleranceCompensatorKind.ElementTiltX => ToleranceCompensatorKind.TiltX,
                ToleranceCompensatorKind.ElementTiltY => ToleranceCompensatorKind.TiltY,
                _ => definition.Kind
            };
            double Get()
            {
                var target = FindSurface(optic, definition.SurfaceNumber);
                var pose = target.CoordinateSystem;
                return axis switch
                {
                    ToleranceCompensatorKind.Thickness => target.Thickness,
                    ToleranceCompensatorKind.DecenterX => pose.ToGlobalPoint(pivot).X,
                    ToleranceCompensatorKind.DecenterY => pose.ToGlobalPoint(pivot).Y,
                    ToleranceCompensatorKind.TiltX => pose.RotationXDegrees,
                    ToleranceCompensatorKind.TiltY => pose.RotationYDegrees,
                    _ => throw new ArgumentOutOfRangeException(nameof(definitions))
                };
            }
            void Set(double value)
            {
                if (axis == ToleranceCompensatorKind.Thickness)
                {
                    SetToleranceThickness(optic, definition.SurfaceNumber, value);
                    return;
                }

                var target = FindSurface(optic, definition.SurfaceNumber);
                var pose = target.CoordinateSystem;
                var next = axis switch
                {
                    ToleranceCompensatorKind.DecenterX => pose with { Origin = pose.Origin with { X = value - pose.ToGlobalDirection(pivot).X } },
                    ToleranceCompensatorKind.DecenterY => pose with { Origin = pose.Origin with { Y = value - pose.ToGlobalDirection(pivot).Y } },
                    ToleranceCompensatorKind.TiltX => KeepMechanicalPivot(pose, pose with { RotationXDegrees = value }, pivot),
                    ToleranceCompensatorKind.TiltY => KeepMechanicalPivot(pose, pose with { RotationYDegrees = value }, pivot),
                    _ => throw new ArgumentOutOfRangeException(nameof(definitions))
                };
                if (pose == next) return;
                if (group)
                {
                    for (var number = definition.SurfaceNumber + 1; number <= definition.EndSurfaceNumber; number++)
                    {
                        var member = FindSurface(optic, number);
                        member.CoordinateSystem = member.CoordinateSystem.MoveWithFrame(pose, next);
                    }
                }
                target.CoordinateSystem = next;
            }

            var nominal = Get();
            var location = group ? $"{surface.Number}–{definition.EndSurfaceNumber}" : surface.Number.ToString();
            tolerancing.AddCompensator(new DelegateVariable(
                $"本地补偿 {definition.Kind} 面 {location}", Get, Set,
                nominal + definition.Minimum, nominal + definition.Maximum,
                Math.Max(1e-8, (definition.Maximum - definition.Minimum) / 10)));
        }
    }

    internal static CoordinateSystem KeepMechanicalPivot(CoordinateSystem pose, CoordinateSystem next, Vector3D pivot) =>
        pivot == Vector3D.Zero || pose == next
            ? next : next with { Origin = pose.ToGlobalPoint(pivot) - next.ToGlobalDirection(pivot) };
}
