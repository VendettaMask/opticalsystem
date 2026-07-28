using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Tolerancing;

namespace OptilandWorkbench.Application.Legacy;

public partial class OpticalWorkspaceModel
{
    private Tolerancing BuildConfiguredTolerancingWorker(
        Optic optic,
        IReadOnlyList<ToleranceOperandDto> operands,
        ToleranceCriterion criterion)
    {
        var tolerancing = optic.CreateTolerancing();
        ConfigureToleranceCriterionWorker(optic, tolerancing, criterion);
        foreach (var operand in operands.Where(item => item.Enabled))
        {
            if (operand.Kind == ToleranceOperandKind.Compensator)
            {
                tolerancing.AddCompensator(CreateToleranceVariableWorker(optic, operand));
            }
            else
            {
                tolerancing.AddPerturbation(new VariableRangePerturbation(
                    ToleranceOperandName(operand),
                    CreateToleranceVariableWorker(optic, operand),
                    operand.Minimum,
                    operand.Maximum,
                    operand.Distribution == ToleranceDistribution.Normal));
            }
        }

        return tolerancing;
    }

    private Tolerancing BuildDefaultTolerancingWorker(
        Optic optic,
        int surfaceNumber,
        double radiusSigma,
        double thicknessSigma,
        int compensationIterations,
        ToleranceCriterion criterion)
    {
        var tolerancing = optic.CreateTolerancing();
        ConfigureToleranceCriterionWorker(optic, tolerancing, criterion);
        var target = FindSurface(optic, surfaceNumber);
        if (Math.Abs(radiusSigma) > 1e-12)
        {
            var span = Math.Max(Math.Abs(target.Radius) * 3, Math.Abs(radiusSigma) * 10);
            tolerancing.AddPerturbation(new VariablePerturbation(
                $"surface {surfaceNumber} radius",
                new DelegateVariable(
                    $"surface {surfaceNumber} radius",
                    () => FindSurface(optic, surfaceNumber).Radius,
                    value => SetSurfaceRadius(FindSurface(optic, surfaceNumber), value),
                    -Math.Max(1, span),
                    Math.Max(1, span),
                    Math.Max(1e-6, Math.Abs(radiusSigma))),
                new NormalSampler(0, Math.Abs(radiusSigma))));
        }

        if (Math.Abs(thicknessSigma) > 1e-12)
        {
            tolerancing.AddPerturbation(new VariablePerturbation(
                $"surface {surfaceNumber} thickness",
                new DelegateVariable(
                    $"surface {surfaceNumber} thickness",
                    () => FindSurface(optic, surfaceNumber).Thickness,
                    value =>
                    {
                        FindSurface(optic, surfaceNumber).Thickness = value;
                        SyncSurfacePositions(optic);
                    },
                    0,
                    Math.Max(1, target.Thickness * 4),
                    Math.Max(1e-6, Math.Abs(thicknessSigma))),
                new NormalSampler(0, Math.Abs(thicknessSigma))));
        }

        if (compensationIterations > 0 && optic.SurfaceGroup.Items.Count > 1)
        {
            var spacingSurface = optic.SurfaceGroup.Items[^2];
            var surfaceId = spacingSurface.Number;
            tolerancing.AddCompensator(new DelegateVariable(
                $"surface {surfaceId} image spacing",
                () => FindSurface(optic, surfaceId).Thickness,
                value =>
                {
                    FindSurface(optic, surfaceId).Thickness = value;
                    SyncSurfacePositions(optic);
                },
                0,
                Math.Max(1, spacingSurface.Thickness + 100),
                0.5));
        }

        return tolerancing;
    }

    private void ConfigureToleranceCriterionWorker(
        Optic optic,
        Tolerancing tolerancing,
        ToleranceCriterion criterion)
    {
        var definitions = (criterion == ToleranceCriterion.RmsWavefront
                ? MeritFunctionCatalog.CreateDefaultRmsWavefront(optic)
                : MeritFunctionCatalog.CreateDefaultRmsSpot(optic))
            .Where(definition => definition.Enabled && Math.Abs(definition.Weight) > 0)
            .ToArray();
        foreach (var definition in definitions)
        {
            tolerancing.AddOperand(MeritFunctionCatalog.CreateOperand(optic, definition));
        }

        tolerancing.SetCriterionEvaluator(() => EvaluateToleranceCriterionWorker(optic, definitions));
    }

    private static double EvaluateToleranceCriterionWorker(
        Optic optic,
        IReadOnlyList<MeritOperandDefinition> definitions)
    {
        using var batch = MeritFunctionCatalog.BeginEvaluationBatch();
        var contribution = 0.0;
        foreach (var definition in definitions)
        {
            if (!definition.Enabled || Math.Abs(definition.Weight) <= 0)
            {
                continue;
            }

            var evaluation = MeritFunctionCatalog.Evaluate(optic, definition);
            if (!string.IsNullOrEmpty(evaluation.Error)
                || !double.IsFinite(evaluation.Value)
                || !double.IsFinite(evaluation.Contribution))
            {
                return double.PositiveInfinity;
            }

            contribution += evaluation.Contribution;
        }

        return double.IsFinite(contribution)
            ? Math.Sqrt(Math.Max(0, contribution))
            : double.PositiveInfinity;
    }

    private IOptimizationVariable CreateToleranceVariableWorker(Optic optic, ToleranceOperandDto operand)
    {
        var surface = FindSurface(optic, operand.SurfaceNumber);
        return operand.Kind switch
        {
            ToleranceOperandKind.Radius => new DelegateVariable(
                ToleranceOperandName(operand),
                () => FindSurface(optic, operand.SurfaceNumber).Radius,
                value => SetSurfaceRadius(FindSurface(optic, operand.SurfaceNumber), value),
                -1e9,
                1e9,
                Math.Max(1e-8, ToleranceStep(operand))),
            ToleranceOperandKind.Thickness => ThicknessVariable(optic, operand, -1e6, 1e6),
            ToleranceOperandKind.Compensator => ThicknessVariable(
                optic,
                operand,
                surface.Thickness + operand.Minimum,
                surface.Thickness + operand.Maximum),
            ToleranceOperandKind.Conic => new DelegateVariable(
                ToleranceOperandName(operand),
                () => FindSurface(optic, operand.SurfaceNumber).Conic,
                value =>
                {
                    var target = FindSurface(optic, operand.SurfaceNumber);
                    target.Conic = value;
                    SyncSurfaceGeometry(target);
                },
                -1e6,
                1e6,
                Math.Max(1e-8, ToleranceStep(operand))),
            ToleranceOperandKind.DecenterX => CoordinateVariableWorker(
                optic,
                operand,
                coordinate => coordinate.Origin.X,
                (coordinate, value) => coordinate with { Origin = coordinate.Origin with { X = value } }),
            ToleranceOperandKind.DecenterY => CoordinateVariableWorker(
                optic,
                operand,
                coordinate => coordinate.Origin.Y,
                (coordinate, value) => coordinate with { Origin = coordinate.Origin with { Y = value } }),
            ToleranceOperandKind.TiltX => CoordinateVariableWorker(
                optic,
                operand,
                coordinate => coordinate.RotationXDegrees,
                (coordinate, value) => coordinate with { RotationXDegrees = value }),
            ToleranceOperandKind.TiltY => CoordinateVariableWorker(
                optic,
                operand,
                coordinate => coordinate.RotationYDegrees,
                (coordinate, value) => coordinate with { RotationYDegrees = value }),
            ToleranceOperandKind.RefractiveIndex => GlassVariable(optic, operand, useAbbeNumber: false),
            ToleranceOperandKind.AbbeNumber => GlassVariable(optic, operand, useAbbeNumber: true),
            _ => throw new NotSupportedException($"Unsupported tolerance operand: {operand.Kind}.")
        };
    }

    private IOptimizationVariable ThicknessVariable(
        Optic optic,
        ToleranceOperandDto operand,
        double minimum,
        double maximum) =>
        new DelegateVariable(
            ToleranceOperandName(operand),
            () => FindSurface(optic, operand.SurfaceNumber).Thickness,
            value =>
            {
                FindSurface(optic, operand.SurfaceNumber).Thickness = value;
                SyncSurfacePositions(optic);
            },
            minimum,
            maximum,
            Math.Max(1e-8, ToleranceStep(operand)));

    private IOptimizationVariable CoordinateVariableWorker(
        Optic optic,
        ToleranceOperandDto operand,
        Func<CoordinateSystem, double> getter,
        Func<CoordinateSystem, double, CoordinateSystem> setter) =>
        new DelegateVariable(
            ToleranceOperandName(operand),
            () => getter(FindSurface(optic, operand.SurfaceNumber).CoordinateSystem),
            value =>
            {
                var target = FindSurface(optic, operand.SurfaceNumber);
                target.CoordinateSystem = setter(target.CoordinateSystem, value);
            },
            -1e6,
            1e6,
            Math.Max(1e-8, ToleranceStep(operand)));

    private IOptimizationVariable GlassVariable(
        Optic optic,
        ToleranceOperandDto operand,
        bool useAbbeNumber) =>
        new DelegateVariable(
            ToleranceOperandName(operand),
            () =>
            {
                var data = GlassData(FindSurface(optic, operand.SurfaceNumber));
                return useAbbeNumber ? data.Vd : data.Nd;
            },
            value =>
            {
                var target = FindSurface(optic, operand.SurfaceNumber);
                var data = GlassData(target);
                SetToleranceGlass(optic, target, useAbbeNumber ? data.Nd : value, useAbbeNumber ? Math.Max(0.1, value) : data.Vd);
            },
            useAbbeNumber ? 0.1 : 1,
            useAbbeNumber ? 500 : 5,
            Math.Max(1e-8, ToleranceStep(operand)));

    private static OpticalSurface FindSurface(Optic optic, int surfaceNumber) =>
        optic.SurfaceGroup.Items.FirstOrDefault(surface => surface.Number == surfaceNumber)
        ?? throw new ArgumentOutOfRangeException(nameof(surfaceNumber));

    private static void SyncSurfacePositions(Optic optic)
    {
        var z = 0.0;
        foreach (var surface in optic.SurfaceGroup.Items)
        {
            surface.CoordinateSystem = surface.CoordinateSystem with
            {
                Origin = surface.CoordinateSystem.Origin with { Z = z }
            };
            z += surface.Thickness;
        }
    }

    private static void SetToleranceGlass(Optic optic, OpticalSurface surface, double nd, double vd)
    {
        var material = new AbbeMaterial(
            $"{surface.Material}-TOL",
            nd,
            vd,
            surface.MaterialAfter.PropagationModel);
        surface.MaterialAfter = material;
        var index = optic.SurfaceGroup.Items.IndexOf(surface);
        if (index >= 0 && index + 1 < optic.SurfaceGroup.Items.Count)
        {
            optic.SurfaceGroup.Items[index + 1].MaterialBefore = material.Clone();
        }
    }
}
