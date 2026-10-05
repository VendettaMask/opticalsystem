using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Tolerancing;

namespace OptilandWorkbench.Application.Runtime;

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
            double Get() => definition.Kind switch
            {
                ToleranceCompensatorKind.Thickness => FindSurface(optic, definition.SurfaceNumber).Thickness,
                ToleranceCompensatorKind.DecenterX => FindSurface(optic, definition.SurfaceNumber).CoordinateSystem.Origin.X,
                ToleranceCompensatorKind.DecenterY => FindSurface(optic, definition.SurfaceNumber).CoordinateSystem.Origin.Y,
                ToleranceCompensatorKind.TiltX => FindSurface(optic, definition.SurfaceNumber).CoordinateSystem.RotationXDegrees,
                ToleranceCompensatorKind.TiltY => FindSurface(optic, definition.SurfaceNumber).CoordinateSystem.RotationYDegrees,
                _ => throw new ArgumentOutOfRangeException(nameof(definitions))
            };
            void Set(double value)
            {
                var target = FindSurface(optic, definition.SurfaceNumber);
                var pose = target.CoordinateSystem;
                if (definition.Kind == ToleranceCompensatorKind.Thickness)
                {
                    target.Thickness = value;
                    SyncSurfacePositions(optic);
                    return;
                }
                target.CoordinateSystem = definition.Kind switch
                {
                    ToleranceCompensatorKind.DecenterX => pose with { Origin = pose.Origin with { X = value } },
                    ToleranceCompensatorKind.DecenterY => pose with { Origin = pose.Origin with { Y = value } },
                    ToleranceCompensatorKind.TiltX => pose with { RotationXDegrees = value },
                    ToleranceCompensatorKind.TiltY => pose with { RotationYDegrees = value },
                    _ => throw new ArgumentOutOfRangeException(nameof(definitions))
                };
            }
            var nominal = Get();
            tolerancing.AddCompensator(new DelegateVariable(
                $"本地补偿 {definition.Kind} 面 {surface.Number}", Get, Set,
                nominal + definition.Minimum, nominal + definition.Maximum,
                Math.Max(1e-8, (definition.Maximum - definition.Minimum) / 10)));
        }
    }
}
