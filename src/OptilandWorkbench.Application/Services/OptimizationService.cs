using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Visualization;
using ContractAnalysisColorMap = OptilandWorkbench.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OptilandWorkbench.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OptilandWorkbench.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OptilandWorkbench.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OptilandWorkbench.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OptilandWorkbench.Application.Contracts.AnalysisSeriesKind;

namespace OptilandWorkbench.Application.Services;

internal sealed partial class OptimizationService : WorkbenchServiceBase, IOptimizationService
{
    public OptimizationService(WorkspaceCoordinator workspace)
        : base(workspace)
    {
    }

    public IReadOnlyList<string> OptimizerNames => Runtime.OptimizerNames;

    public IReadOnlyList<MeritOperandTypeDto> GetMeritOperandTypes()
    {
        return MeritFunctionCatalog.Types
            .Select(type =>
            {
                if (!ZemaxOperandRegistry.TryGet(type.Code, out var descriptor))
                {
                    return new MeritOperandTypeDto(type.Code, type.DisplayName, type.Description);
                }

                return new MeritOperandTypeDto(
                    type.Code,
                    type.DisplayName,
                    type.Description,
                    descriptor.Parameters.Select(parameter => new MeritOperandParameterDto(
                        parameter.Slot,
                        parameter.DisplayName,
                        parameter.ValueKind.ToString(),
                        parameter.Unit,
                        !parameter.DisplayName.Equals("Unused", StringComparison.OrdinalIgnoreCase))).ToArray(),
                    descriptor.SupportLevel == ZemaxOperandSupportLevel.CompatibilityOnly);
            })
            .ToArray();
    }

    public IReadOnlyList<MeritOperandRowDto> GetMeritFunction()
    {
        using var cancellationScope = ComputationCancellation.Push(CancellationToken.None);
        lock (Gate)
        {
            var operands = Runtime.CurrentOptic.MeritFunctionOperands.ToArray();
            var evaluations = MeritFunctionCatalog.EvaluateAll(Runtime.CurrentOptic, operands);
            var weightSum = operands
                .Where(operand => operand.Enabled
                    && MeritFunctionCatalog.CanonicalType(operand.Type) is not ("BLNK" or "DMFS"))
                .Sum(operand => Math.Abs(operand.Weight));
            return operands
                .Select((operand, index) =>
                {
                    var evaluation = evaluations[index];
                    var rawIntegers = ResolveRawIntegerParameters(operand);
                    var rawData = ResolveRawDataParameters(operand);
                    return new MeritOperandRowDto(
                        index + 1,
                        operand.Enabled,
                        MeritFunctionCatalog.CanonicalType(operand.Type),
                        operand.Surface,
                        operand.Field,
                        operand.Wavelength,
                        operand.Hx,
                        operand.Hy,
                        operand.Px,
                        operand.Py,
                        operand.Target,
                        operand.Weight,
                        evaluation.Value,
                        weightSum > 0 ? evaluation.Contribution / weightSum : 0,
                        operand.Comment,
                        evaluation.Error,
                        operand.PupilRings,
                        operand.PupilArms,
                        operand.PupilObscuration,
                        operand.PupilSampling,
                        operand.SpatialFrequency,
                        operand.IgnoreLateralColor,
                        operand.PolychromaticReference,
                        operand.CompatibilityOnly,
                        rawIntegers?[0],
                        rawIntegers?[1],
                        rawData?[0],
                        rawData?[1],
                        rawData?[2],
                        rawData?[3]);
                })
                .ToArray();
        }
    }

    public void SetMeritFunction(IReadOnlyList<MeritOperandRowDto> operands)
    {
        MutateTransactional(WorkspaceChangeCategory.Optimization, () => Runtime.ReplaceMeritFunction(
            operands.Select(operand =>
            {
                var type = MeritFunctionCatalog.CanonicalType(operand.Type);
                var isZemaxOperand = ZemaxOperandRegistry.TryGet(type, out var descriptor);
                var forceCompatibilityOnly = operand.CompatibilityOnly
                    || MeritFunctionCatalog.HasOpaqueZemaxParameters(type);
                var definition = new MeritOperandDefinition
                {
                    Enabled = forceCompatibilityOnly ? false : operand.Enabled,
                    Type = type,
                    Surface = isZemaxOperand ? operand.Surface : Math.Max(0, operand.Surface),
                    Field = isZemaxOperand ? operand.Field : Math.Max(0, operand.Field),
                    Wavelength = isZemaxOperand ? operand.Wavelength : Math.Max(0, operand.Wavelength),
                    Hx = isZemaxOperand ? operand.Hx : Math.Clamp(operand.Hx, -1, 1),
                    Hy = isZemaxOperand ? operand.Hy : Math.Clamp(operand.Hy, -1, 1),
                    Px = isZemaxOperand ? operand.Px : Math.Clamp(operand.Px, -1, 1),
                    Py = isZemaxOperand ? operand.Py : Math.Clamp(operand.Py, -1, 1),
                    Target = double.IsFinite(operand.Target) ? operand.Target : 0,
                    Weight = double.IsFinite(operand.Weight) ? operand.Weight : 0,
                    Comment = operand.Comment ?? string.Empty,
                    PupilRings = Math.Clamp(operand.PupilRings, 1, 20),
                    PupilArms = Math.Clamp(operand.PupilArms, 3, 36),
                    PupilObscuration = Math.Clamp(operand.PupilObscuration, 0, 0.95),
                    PupilSampling = operand.PupilSampling?.Trim().ToLowerInvariant() switch
                    {
                        "uniform" => "uniform",
                        "gaussian_quad" => "gaussian_quad",
                        _ => "hexapolar"
                    },
                    SpatialFrequency = double.IsFinite(operand.SpatialFrequency)
                        ? Math.Max(0, operand.SpatialFrequency)
                        : 30,
                    IgnoreLateralColor = operand.IgnoreLateralColor,
                    PolychromaticReference = operand.PolychromaticReference,
                    CompatibilityOnly = forceCompatibilityOnly,
                    ZemaxIntegerParameters = isZemaxOperand
                        ? [operand.ZemaxInt1 ?? operand.Surface, operand.ZemaxInt2 ?? operand.Wavelength]
                        : [],
                    ZemaxDataParameters = isZemaxOperand
                        ?
                        [
                            operand.ZemaxData1 ?? operand.Hx,
                            operand.ZemaxData2 ?? operand.Hy,
                            operand.ZemaxData3 ?? operand.Px,
                            operand.ZemaxData4 ?? operand.Py
                        ]
                        : []
                };
                if (isZemaxOperand)
                {
                    ApplyRawZemaxParameters(definition, descriptor);
                }

                return definition;
            })));
    }

    private static int[]? ResolveRawIntegerParameters(MeritOperandDefinition operand)
    {
        if (!ZemaxOperandRegistry.TryGet(operand.Type, out var descriptor))
        {
            return null;
        }

        if (operand.ZemaxIntegerParameters is { Length: >= 2 })
        {
            return [operand.ZemaxIntegerParameters[0], operand.ZemaxIntegerParameters[1]];
        }

        var result = new[] { operand.Surface, operand.Wavelength };
        foreach (var parameter in descriptor.Parameters.Where(parameter => parameter.Slot is "Int1" or "Int2"))
        {
            var index = parameter.Slot == "Int1" ? 0 : 1;
            result[index] = parameter.ValueKind switch
            {
                ZemaxOperandParameterValueKind.Field => operand.Field,
                ZemaxOperandParameterValueKind.Wavelength => operand.Wavelength,
                ZemaxOperandParameterValueKind.Surface => operand.Surface,
                ZemaxOperandParameterValueKind.EndSurface => operand.Wavelength,
                ZemaxOperandParameterValueKind.RowReference => operand.Surface,
                ZemaxOperandParameterValueKind.RowRangeEnd => operand.Wavelength,
                ZemaxOperandParameterValueKind.Integer when parameter.DisplayName == "Rings" => operand.PupilRings,
                _ => result[index]
            };
        }

        return result;
    }

    private static double[]? ResolveRawDataParameters(MeritOperandDefinition operand)
    {
        if (!ZemaxOperandRegistry.TryGet(operand.Type, out var descriptor))
        {
            return null;
        }

        if (operand.ZemaxDataParameters is { Length: >= 4 })
        {
            return
            [
                operand.ZemaxDataParameters[0],
                operand.ZemaxDataParameters[1],
                operand.ZemaxDataParameters[2],
                operand.ZemaxDataParameters[3]
            ];
        }

        var result = new[] { operand.Hx, operand.Hy, operand.Px, operand.Py };
        foreach (var parameter in descriptor.Parameters.Where(parameter => parameter.Slot.StartsWith("Data", StringComparison.Ordinal)))
        {
            var index = int.Parse(parameter.Slot.AsSpan(4), System.Globalization.CultureInfo.InvariantCulture) - 1;
            result[index] = parameter.ValueKind switch
            {
                ZemaxOperandParameterValueKind.Field => operand.Field,
                ZemaxOperandParameterValueKind.NormalizedField when parameter.DisplayName == "Hx" => operand.Hx,
                ZemaxOperandParameterValueKind.NormalizedField when parameter.DisplayName == "Hy" => operand.Hy,
                ZemaxOperandParameterValueKind.PupilCoordinate when parameter.DisplayName == "Px" => operand.Px,
                ZemaxOperandParameterValueKind.PupilCoordinate when parameter.DisplayName == "Py" => operand.Py,
                ZemaxOperandParameterValueKind.SpatialFrequency => operand.SpatialFrequency,
                _ => result[index]
            };
        }

        return result;
    }

    private static void ApplyRawZemaxParameters(
        MeritOperandDefinition definition,
        ZemaxOperandDescriptor descriptor)
    {
        var integers = definition.ZemaxIntegerParameters;
        var data = definition.ZemaxDataParameters;
        definition.Surface = integers[0];
        definition.Wavelength = integers[1];
        definition.Hx = data[0];
        definition.Hy = data[1];
        definition.Px = data[2];
        definition.Py = data[3];

        foreach (var parameter in descriptor.Parameters)
        {
            var raw = parameter.Slot switch
            {
                "Int1" => integers[0],
                "Int2" => integers[1],
                "Data1" => data[0],
                "Data2" => data[1],
                "Data3" => data[2],
                "Data4" => data[3],
                _ => 0
            };
            switch (parameter.ValueKind)
            {
                case ZemaxOperandParameterValueKind.Surface:
                case ZemaxOperandParameterValueKind.RowReference:
                    definition.Surface = checked((int)raw);
                    break;
                case ZemaxOperandParameterValueKind.EndSurface:
                case ZemaxOperandParameterValueKind.RowRangeEnd:
                case ZemaxOperandParameterValueKind.Wavelength:
                    definition.Wavelength = checked((int)raw);
                    break;
                case ZemaxOperandParameterValueKind.Field:
                    definition.Field = checked((int)raw);
                    break;
                case ZemaxOperandParameterValueKind.NormalizedField when parameter.DisplayName == "Hx":
                    definition.Hx = raw;
                    break;
                case ZemaxOperandParameterValueKind.NormalizedField:
                    definition.Hy = raw;
                    break;
                case ZemaxOperandParameterValueKind.PupilCoordinate when parameter.DisplayName == "Px":
                    definition.Px = raw;
                    break;
                case ZemaxOperandParameterValueKind.PupilCoordinate:
                    definition.Py = raw;
                    break;
                case ZemaxOperandParameterValueKind.SpatialFrequency:
                    definition.SpatialFrequency = raw;
                    break;
                case ZemaxOperandParameterValueKind.Integer when parameter.DisplayName == "Rings":
                    definition.PupilRings = Math.Clamp(checked((int)raw), 1, 20);
                    break;
            }
        }
    }

    public void GenerateDefaultMeritFunction(MeritFunctionPreset preset)
    {
        MutateTransactional(WorkspaceChangeCategory.Optimization, () => Runtime.GenerateDefaultMeritFunction(preset));
    }

    public void GenerateMeritFunction(OptimizationWizardSettingsDto settings)
    {
        var coreSettings = new MeritFunctionWizardSettings(
            settings.ImageQuality switch
            {
                OptimizationImageQuality.RmsWavefront => MeritImageQuality.RmsWavefront,
                OptimizationImageQuality.RmsSpot => MeritImageQuality.RmsSpot,
                OptimizationImageQuality.Contrast => MeritImageQuality.Contrast,
                OptimizationImageQuality.Angular => MeritImageQuality.Angular,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(settings),
                    settings.ImageQuality,
                    "The optimization image-quality mode is invalid.")
            },
            settings.PupilSampling == OptimizationPupilSampling.RectangularArray
                ? MeritPupilSampling.RectangularArray
                : MeritPupilSampling.GaussianQuadrature,
            settings.PupilRings,
            settings.PupilArms,
            settings.PupilObscuration,
            settings.WeightScale,
            settings.UseAllWavelengths,
            settings.IncludeCommonOperands,
            settings.Reference switch
            {
                OptimizationSpotReference.ChiefRay => MeritSpotReference.ChiefRay,
                OptimizationSpotReference.Unreferenced => MeritSpotReference.Unreferenced,
                _ => MeritSpotReference.Centroid
            },
            settings.SpatialFrequency,
            settings.XWeight,
            settings.YWeight,
            settings.IgnoreLateralColor);
        MutateTransactional(WorkspaceChangeCategory.Optimization, () => Runtime.GenerateMeritFunction(
            coreSettings,
            settings.StartRow,
            settings.ReplaceExisting));
    }

    public OptimizationVariableUpdateResultDto UpdateAllSurfaceVariables(
        OptimizationVariableUpdateMode mode)
    {
        return MutateTransactional(WorkspaceChangeCategory.Optimization, () =>
        {
            var lastSurfaceNumber = Runtime.Surfaces.Count == 0
                ? -1
                : Runtime.Surfaces[^1].Number;
            var editable = Runtime.Surfaces
                .Where(surface => surface.Number > 0 && surface.Number < lastSurfaceNumber)
                .ToArray();
            if (editable.Length == 0)
            {
                return new OptimizationVariableUpdateResultDto(mode, 0, 0);
            }

            Runtime.CaptureCurrentState();
            foreach (var surface in editable)
            {
                switch (mode)
                {
                    case OptimizationVariableUpdateMode.ClearAll:
                        surface.RadiusVariable = false;
                        surface.ThicknessVariable = false;
                        break;
                    case OptimizationVariableUpdateMode.SetAllRadii:
                        surface.RadiusVariable = true;
                        break;
                    case OptimizationVariableUpdateMode.SetAllThicknesses:
                        surface.ThicknessVariable = true;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mode));
                }
            }

            Runtime.CommitSurfaceEdit(editable[0], nameof(OpticalSurface.RadiusVariable));
            return new OptimizationVariableUpdateResultDto(
                mode,
                editable.Count(surface => surface.RadiusVariable),
                editable.Count(surface => surface.ThicknessVariable));
        });
    }
}
