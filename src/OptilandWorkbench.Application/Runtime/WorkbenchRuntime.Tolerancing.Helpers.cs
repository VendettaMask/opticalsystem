using System.Collections.ObjectModel;
using System.Globalization;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;
using ContractMeritFunctionPreset = OptilandWorkbench.Application.Contracts.MeritFunctionPreset;

namespace OptilandWorkbench.Application.Runtime;

public partial class WorkbenchRuntime
{
    private Tolerancing BuildDefaultTolerancing(
        int surfaceNumber,
        double radiusSigma,
        double thicknessSigma,
        int compensationIterations,
        ToleranceCriterion criterion) => BuildDefaultTolerancingWorker(
            CurrentOptic, surfaceNumber, radiusSigma, thicknessSigma, compensationIterations, criterion);

    private Tolerancing BuildConfiguredTolerancing(
        IReadOnlyList<ToleranceOperandDto> operands,
        ToleranceCriterion criterion) => BuildConfiguredTolerancingWorker(CurrentOptic, operands, criterion);

    private double EvaluateToleranceCriterion(
        IReadOnlyList<MeritOperandDefinition> definitions)
    {
        return EvaluateToleranceCriterionCore(CurrentOptic, definitions);
    }

    private static double EvaluateToleranceCriterionCore(
        Optic optic,
        IReadOnlyList<MeritOperandDefinition> definitions)
    {
        var evaluations = MeritFunctionCatalog.EvaluateAll(optic, definitions);
        var contribution = 0.0;
        var requestedWeight = 0.0;
        var includedWeight = 0.0;
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            if (!definition.Enabled || Math.Abs(definition.Weight) <= 0)
            {
                continue;
            }

            var weight = Math.Abs(definition.Weight);
            requestedWeight += weight;
            var evaluation = evaluations[index];
            if (!string.IsNullOrEmpty(evaluation.Error)
                || !double.IsFinite(evaluation.Value)
                || !double.IsFinite(evaluation.Contribution))
            {
                continue;
            }

            contribution += evaluation.Contribution;
            includedWeight += weight;
        }

        return double.IsFinite(contribution)
            && requestedWeight > 1e-15
            && includedWeight > 1e-15
            ? Math.Sqrt(Math.Max(0, contribution * requestedWeight / includedWeight))
            : double.PositiveInfinity;
    }

    private static IOptimizationVariable ElementCoordinateVariable(
        Optic optic,
        ToleranceOperandDto operand)
    {
        var surfaces = Enumerable.Range(
                operand.SurfaceNumber,
                operand.EndSurfaceNumber - operand.SurfaceNumber + 1)
            .Select(number => FindSurface(optic, number))
            .ToArray();
        if (surfaces.Length < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(operand), "Element tolerance requires at least two surfaces.");
        }

        double Getter() => operand.Kind switch
        {
            ToleranceOperandKind.ElementDecenterX => surfaces[0].CoordinateSystem.Origin.X,
            ToleranceOperandKind.ElementDecenterY => surfaces[0].CoordinateSystem.Origin.Y,
            ToleranceOperandKind.ElementTiltX => surfaces[0].CoordinateSystem.RotationXDegrees,
            ToleranceOperandKind.ElementTiltY => surfaces[0].CoordinateSystem.RotationYDegrees,
            _ => throw new NotSupportedException()
        };

        void Setter(double value)
        {
            var delta = value - Getter();
            if (Math.Abs(delta) <= 1e-15)
            {
                return;
            }

            if (operand.Kind is ToleranceOperandKind.ElementDecenterX
                or ToleranceOperandKind.ElementDecenterY)
            {
                foreach (var surface in surfaces)
                {
                    var origin = surface.CoordinateSystem.Origin;
                    surface.CoordinateSystem = surface.CoordinateSystem with
                    {
                        Origin = operand.Kind == ToleranceOperandKind.ElementDecenterX
                            ? origin with { X = origin.X + delta }
                            : origin with { Y = origin.Y + delta }
                    };
                }

                return;
            }

            var pivot = surfaces[0].CoordinateSystem.Origin;
            var angle = delta * Math.PI / 180.0;
            var sine = Math.Sin(angle);
            var cosine = Math.Cos(angle);
            foreach (var surface in surfaces)
            {
                var coordinate = surface.CoordinateSystem;
                var x = coordinate.Origin.X - pivot.X;
                var y = coordinate.Origin.Y - pivot.Y;
                var z = coordinate.Origin.Z - pivot.Z;
                var rotated = operand.Kind == ToleranceOperandKind.ElementTiltX
                    ? new Vector3D(x, (y * cosine) - (z * sine), (y * sine) + (z * cosine))
                    : new Vector3D((x * cosine) + (z * sine), y, (-x * sine) + (z * cosine));
                surface.CoordinateSystem = coordinate with
                {
                    Origin = new Vector3D(
                        pivot.X + rotated.X,
                        pivot.Y + rotated.Y,
                        pivot.Z + rotated.Z),
                    RotationXDegrees = coordinate.RotationXDegrees
                        + (operand.Kind == ToleranceOperandKind.ElementTiltX ? delta : 0),
                    RotationYDegrees = coordinate.RotationYDegrees
                        + (operand.Kind == ToleranceOperandKind.ElementTiltY ? delta : 0)
                };
            }
        }

        return new DelegateVariable(
            ToleranceOperandName(operand),
            Getter,
            Setter,
            -1e6,
            1e6,
            Math.Max(1e-8, ToleranceStep(operand)));
    }

    private static IOptimizationVariable AsphereCoefficientVariable(
        Optic optic,
        ToleranceOperandDto operand)
    {
        var coefficientIndex = operand.ParameterIndex - 1;
        double Getter() => Coefficients(FindSurface(optic, operand.SurfaceNumber))[coefficientIndex];
        void Setter(double value)
        {
            var surface = FindSurface(optic, operand.SurfaceNumber);
            var coefficients = Coefficients(surface).ToArray();
            coefficients[coefficientIndex] = value;
            surface.Geometry = surface.Geometry switch
            {
                EvenAsphereGeometry even =>
                    new EvenAsphereGeometry(even.Base.Radius, even.Base.Conic, coefficients),
                OddAsphereGeometry odd =>
                    new OddAsphereGeometry(odd.Base.Radius, odd.Base.Conic, coefficients),
                ForbesQGeometry forbes =>
                    new ForbesQGeometry(
                        forbes.Base.Radius,
                        forbes.Base.Conic,
                        forbes.NormalizationRadius,
                        coefficients),
                _ => throw new NotSupportedException("The selected surface has no supported asphere parameters.")
            };
        }

        _ = Getter();
        return new DelegateVariable(
            ToleranceOperandName(operand),
            Getter,
            Setter,
            -1e12,
            1e12,
            Math.Max(1e-12, ToleranceStep(operand)));
    }

    private static IReadOnlyList<double> Coefficients(OpticalSurface surface) => surface.Geometry switch
    {
        EvenAsphereGeometry even => even.Coefficients,
        OddAsphereGeometry odd => odd.Coefficients,
        ForbesQGeometry forbes => forbes.QCoefficients,
        _ => throw new NotSupportedException("The selected surface has no supported asphere parameters.")
    };

    private void SyncSurfacePositionsPreservingDecenter()
    {
        var z = 0.0;
        for (var index = 0; index < Surfaces.Count; index++)
        {
            var surface = Surfaces[index];
            var coordinate = surface.CoordinateSystem;
            surface.CoordinateSystem = coordinate with
            {
                Origin = coordinate.Origin with { Z = z }
            };
            if (index != 0 || !ObjectConjugate.IsInfinite(surface))
            {
                z += surface.Thickness;
            }
        }
    }

    private void SetToleranceGlass(OpticalSurface surface, double nd, double vd)
    {
        var material = new AbbeMaterial($"{surface.Material}-TOL", nd, vd, surface.MaterialAfter.PropagationModel);
        surface.MaterialAfter = material;
        var index = Surfaces.IndexOf(surface);
        if (index >= 0 && index + 1 < Surfaces.Count)
        {
            Surfaces[index + 1].MaterialBefore = material.Clone();
        }
    }

    private static (double Nd, double Vd) GlassData(OpticalSurface surface)
    {
        return surface.MaterialAfter switch
        {
            AbbeMaterial abbe => (abbe.Nd, abbe.Vd),
            CatalogGlassMaterial { ZemaxData: not null } catalog =>
                (catalog.ZemaxData.ReferenceIndexD, catalog.ZemaxData.ReferenceAbbeNumber),
            _ => (surface.MaterialAfter.RefractiveIndex(587.6), 50)
        };
    }

    private static double ToleranceStep(ToleranceOperandDto operand) =>
        Math.Max(Math.Abs(operand.Minimum), Math.Abs(operand.Maximum)) / 5.0;

    private static string ToleranceOperandName(ToleranceOperandDto operand)
    {
        var code = operand.Kind switch
        {
            ToleranceOperandKind.Radius => "TRAD",
            ToleranceOperandKind.Thickness => "TTHI",
            ToleranceOperandKind.Conic => "TCON",
            ToleranceOperandKind.DecenterX => "TSDX",
            ToleranceOperandKind.DecenterY => "TSDY",
            ToleranceOperandKind.TiltX => "TSTX",
            ToleranceOperandKind.TiltY => "TSTY",
            ToleranceOperandKind.ElementDecenterX => "TEDX",
            ToleranceOperandKind.ElementDecenterY => "TEDY",
            ToleranceOperandKind.ElementTiltX => "TETX",
            ToleranceOperandKind.ElementTiltY => "TETY",
            ToleranceOperandKind.AsphereCoefficient => "TPAR",
            ToleranceOperandKind.RefractiveIndex => "TIND",
            ToleranceOperandKind.AbbeNumber => "TABB",
            ToleranceOperandKind.Compensator => "COMP",
            _ => operand.Kind.ToString()
        };
        var location = operand.Kind is ToleranceOperandKind.ElementDecenterX
            or ToleranceOperandKind.ElementDecenterY
            or ToleranceOperandKind.ElementTiltX
            or ToleranceOperandKind.ElementTiltY
            ? $"面 {operand.SurfaceNumber}-{operand.EndSurfaceNumber}"
            : operand.Kind == ToleranceOperandKind.AsphereCoefficient
                ? $"面 {operand.SurfaceNumber} 参数 {operand.ParameterIndex}"
                : $"面 {operand.SurfaceNumber}";
        return $"{code}  {location}"
            + (string.IsNullOrWhiteSpace(operand.Comment) ? string.Empty : $"  {operand.Comment}");
    }
}
