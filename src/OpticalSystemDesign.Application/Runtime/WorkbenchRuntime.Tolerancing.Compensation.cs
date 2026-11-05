using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core;
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
        foreach (var definition in definitions ?? Array.Empty<ToleranceCompensatorDto>())
        {
            var surface = FindSurface(optic, definition.SurfaceNumber);
            var group = definition.Kind.IsElement();
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
                return axis switch
                {
                    ToleranceCompensatorKind.Thickness => target.Thickness,
                    ToleranceCompensatorKind.DecenterX => target.CoordinateSystem.Origin.X,
                    ToleranceCompensatorKind.DecenterY => target.CoordinateSystem.Origin.Y,
                    ToleranceCompensatorKind.TiltX => target.CoordinateSystem.RotationXDegrees,
                    ToleranceCompensatorKind.TiltY => target.CoordinateSystem.RotationYDegrees,
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
                    ToleranceCompensatorKind.DecenterX => pose with { Origin = pose.Origin with { X = value } },
                    ToleranceCompensatorKind.DecenterY => pose with { Origin = pose.Origin with { Y = value } },
                    ToleranceCompensatorKind.TiltX => pose with { RotationXDegrees = value },
                    ToleranceCompensatorKind.TiltY => pose with { RotationYDegrees = value },
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
}
