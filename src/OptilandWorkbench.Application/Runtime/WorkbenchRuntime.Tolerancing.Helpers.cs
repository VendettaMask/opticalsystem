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
            ToleranceCriterion criterion)
    {
        var tolerancing = CurrentOptic.CreateTolerancing();
        ConfigureToleranceCriterion(tolerancing, criterion);

        var target = GetSurfaceByNumber(surfaceNumber);
        if (Math.Abs(radiusSigma) > 1e-12)
        {
            var span = Math.Max(Math.Abs(target.Radius) * 3, Math.Abs(radiusSigma) * 10);
            tolerancing.AddPerturbation(new VariablePerturbation(
                $"表面 {surfaceNumber} 半径 N(0,{NumericDisplayFormatter.Format(Math.Abs(radiusSigma))})",
                new DelegateVariable(
                    $"表面 {surfaceNumber} 半径",
                    () => GetSurfaceByNumber(surfaceNumber).Radius,
                    value => SetSurfaceRadius(GetSurfaceByNumber(surfaceNumber), value),
                    -Math.Max(1, span),
                    Math.Max(1, span),
                    Math.Max(1e-6, Math.Abs(radiusSigma))),
                new NormalSampler(0, Math.Abs(radiusSigma))));
        }

        if (Math.Abs(thicknessSigma) > 1e-12)
        {
            tolerancing.AddPerturbation(new VariablePerturbation(
                $"表面 {surfaceNumber} 厚度 N(0,{NumericDisplayFormatter.Format(Math.Abs(thicknessSigma))})",
                new DelegateVariable(
                    $"表面 {surfaceNumber} 厚度",
                    () => GetSurfaceByNumber(surfaceNumber).Thickness,
                    value =>
                    {
                        GetSurfaceByNumber(surfaceNumber).Thickness = value;
                        CurrentOptic.SurfaceGroup.Renumber();
                    },
                    0,
                    Math.Max(1, target.Thickness * 4),
                    Math.Max(1e-6, Math.Abs(thicknessSigma))),
                new NormalSampler(0, Math.Abs(thicknessSigma))));
        }

        if (compensationIterations > 0 && Surfaces.Count > 1)
        {
            var imageSpacingSurfaceNumber = Surfaces[^2].Number;
            var imageSpacing = GetSurfaceByNumber(imageSpacingSurfaceNumber).Thickness;
            tolerancing.AddCompensator(new DelegateVariable(
                $"表面 {imageSpacingSurfaceNumber} 像面位置补偿",
                () => GetSurfaceByNumber(imageSpacingSurfaceNumber).Thickness,
                value =>
                {
                    GetSurfaceByNumber(imageSpacingSurfaceNumber).Thickness = value;
                    CurrentOptic.SurfaceGroup.Renumber();
                },
                0,
                Math.Max(1, imageSpacing + 100),
                0.5));
        }

        return tolerancing;
    }

    private Tolerancing BuildConfiguredTolerancing(
        IReadOnlyList<ToleranceOperandDto> operands,
        ToleranceCriterion criterion)
    {
        var tolerancing = CurrentOptic.CreateTolerancing();
        ConfigureToleranceCriterion(tolerancing, criterion);

        foreach (var operand in operands.Where(item => item.Enabled))
        {
            if (operand.Kind == ToleranceOperandKind.Compensator)
            {
                tolerancing.AddCompensator(CreateToleranceVariable(operand));
                continue;
            }

            tolerancing.AddPerturbation(new VariableRangePerturbation(
                ToleranceOperandName(operand),
                CreateToleranceVariable(operand),
                operand.Minimum,
                operand.Maximum,
                operand.Distribution == ToleranceDistribution.Normal));
        }

        return tolerancing;
    }

    private double EvaluateToleranceCriterion(ToleranceCriterion criterion)
    {
        return EvaluateToleranceCriterion(CreateToleranceCriterionDefinitions(criterion));
    }

    private void ConfigureToleranceCriterion(Tolerancing tolerancing, ToleranceCriterion criterion)
    {
        var definitions = CreateToleranceCriterionDefinitions(criterion)
            .Where(definition => definition.Enabled && Math.Abs(definition.Weight) > 0)
            .ToArray();
        foreach (var definition in definitions)
        {
            tolerancing.AddOperand(MeritFunctionCatalog.CreateOperand(CurrentOptic, definition));
        }

        tolerancing.SetCriterionEvaluator(() => EvaluateToleranceCriterion(definitions));
    }

    private IReadOnlyList<MeritOperandDefinition> CreateToleranceCriterionDefinitions(
        ToleranceCriterion criterion)
    {
        return criterion == ToleranceCriterion.RmsWavefront
            ? MeritFunctionCatalog.CreateDefaultRmsWavefront(CurrentOptic)
            : MeritFunctionCatalog.CreateDefaultRmsSpot(CurrentOptic);
    }

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

    private IOptimizationVariable CreateToleranceVariable(ToleranceOperandDto operand)
    {
        var surface = GetSurfaceByNumber(operand.SurfaceNumber);
        return operand.Kind switch
        {
            ToleranceOperandKind.Radius => new DelegateVariable(
                ToleranceOperandName(operand),
                () => GetSurfaceByNumber(operand.SurfaceNumber).Radius,
                value => SetSurfaceRadius(GetSurfaceByNumber(operand.SurfaceNumber), value),
                -1e9,
                1e9,
                Math.Max(1e-8, ToleranceStep(operand))),
            ToleranceOperandKind.Thickness => new DelegateVariable(
                ToleranceOperandName(operand),
                () => GetSurfaceByNumber(operand.SurfaceNumber).Thickness,
                value =>
                {
                    GetSurfaceByNumber(operand.SurfaceNumber).Thickness = value;
                    SyncSurfacePositionsPreservingDecenter();
                },
                -1e6,
                1e6,
                Math.Max(1e-8, ToleranceStep(operand))),
            ToleranceOperandKind.Compensator => new DelegateVariable(
                ToleranceOperandName(operand),
                () => GetSurfaceByNumber(operand.SurfaceNumber).Thickness,
                value =>
                {
                    GetSurfaceByNumber(operand.SurfaceNumber).Thickness = value;
                    SyncSurfacePositionsPreservingDecenter();
                },
                surface.Thickness + operand.Minimum,
                surface.Thickness + operand.Maximum,
                Math.Max(1e-8, ToleranceStep(operand))),
            ToleranceOperandKind.Conic => new DelegateVariable(
                ToleranceOperandName(operand),
                () => GetSurfaceByNumber(operand.SurfaceNumber).Conic,
                value =>
                {
                    var target = GetSurfaceByNumber(operand.SurfaceNumber);
                    target.Conic = value;
                },
                -1e6,
                1e6,
                Math.Max(1e-8, ToleranceStep(operand))),
            ToleranceOperandKind.DecenterX => CoordinateVariable(
                operand,
                coordinate => coordinate.Origin.X,
                (coordinate, value) => coordinate with { Origin = coordinate.Origin with { X = value } }),
            ToleranceOperandKind.DecenterY => CoordinateVariable(
                operand,
                coordinate => coordinate.Origin.Y,
                (coordinate, value) => coordinate with { Origin = coordinate.Origin with { Y = value } }),
            ToleranceOperandKind.TiltX => CoordinateVariable(
                operand,
                coordinate => coordinate.RotationXDegrees,
                (coordinate, value) => coordinate with { RotationXDegrees = value }),
            ToleranceOperandKind.TiltY => CoordinateVariable(
                operand,
                coordinate => coordinate.RotationYDegrees,
                (coordinate, value) => coordinate with { RotationYDegrees = value }),
            ToleranceOperandKind.ElementDecenterX
                or ToleranceOperandKind.ElementDecenterY
                or ToleranceOperandKind.ElementTiltX
                or ToleranceOperandKind.ElementTiltY =>
                ElementCoordinateVariable(CurrentOptic, operand),
            ToleranceOperandKind.AsphereCoefficient =>
                AsphereCoefficientVariable(CurrentOptic, operand),
            ToleranceOperandKind.RefractiveIndex => new DelegateVariable(
                ToleranceOperandName(operand),
                () => GlassData(GetSurfaceByNumber(operand.SurfaceNumber)).Nd,
                value =>
                {
                    var target = GetSurfaceByNumber(operand.SurfaceNumber);
                    var data = GlassData(target);
                    SetToleranceGlass(target, value, data.Vd);
                },
                1,
                5,
                Math.Max(1e-8, ToleranceStep(operand))),
            ToleranceOperandKind.AbbeNumber => new DelegateVariable(
                ToleranceOperandName(operand),
                () => GlassData(GetSurfaceByNumber(operand.SurfaceNumber)).Vd,
                value =>
                {
                    var target = GetSurfaceByNumber(operand.SurfaceNumber);
                    var data = GlassData(target);
                    SetToleranceGlass(target, data.Nd, Math.Max(0.1, value));
                },
                0.1,
                500,
                Math.Max(1e-8, ToleranceStep(operand))),
            _ => throw new NotSupportedException($"Unsupported tolerance operand: {operand.Kind}.")
        };
    }

    private IOptimizationVariable CoordinateVariable(
        ToleranceOperandDto operand,
        Func<CoordinateSystem, double> getter,
        Func<CoordinateSystem, double, CoordinateSystem> setter)
    {
        return new DelegateVariable(
            ToleranceOperandName(operand),
            () => getter(GetSurfaceByNumber(operand.SurfaceNumber).CoordinateSystem),
            value =>
            {
                var target = GetSurfaceByNumber(operand.SurfaceNumber);
                target.CoordinateSystem = setter(target.CoordinateSystem, value);
            },
            -1e6,
            1e6,
            Math.Max(1e-8, ToleranceStep(operand)));
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
