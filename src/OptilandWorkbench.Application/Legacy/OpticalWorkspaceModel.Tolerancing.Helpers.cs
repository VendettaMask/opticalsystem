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

namespace OptilandWorkbench.Application.Legacy;

public partial class OpticalWorkspaceModel
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
        using var batch = MeritFunctionCatalog.BeginEvaluationBatch();
        var contribution = 0.0;
        foreach (var definition in definitions)
        {
            if (!definition.Enabled || Math.Abs(definition.Weight) <= 0)
            {
                continue;
            }

            var evaluation = MeritFunctionCatalog.Evaluate(CurrentOptic, definition);
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
            ToleranceOperandKind.RefractiveIndex => "TIND",
            ToleranceOperandKind.AbbeNumber => "TABB",
            ToleranceOperandKind.Compensator => "COMP",
            _ => operand.Kind.ToString()
        };
        return $"{code}  面 {operand.SurfaceNumber}"
            + (string.IsNullOrWhiteSpace(operand.Comment) ? string.Empty : $"  {operand.Comment}");
    }
}
