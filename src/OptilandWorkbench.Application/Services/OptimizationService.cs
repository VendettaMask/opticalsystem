using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Legacy;
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

    public IReadOnlyList<string> OptimizerNames => Connector.OptimizerNames;

    public IReadOnlyList<MeritOperandTypeDto> GetMeritOperandTypes()
    {
        return MeritFunctionCatalog.Types
            .Select(type => new MeritOperandTypeDto(type.Code, type.DisplayName, type.Description))
            .ToArray();
    }

    public IReadOnlyList<MeritOperandRowDto> GetMeritFunction()
    {
        using var cancellationScope = ComputationCancellation.Push(CancellationToken.None);
        using var evaluationBatch = MeritFunctionCatalog.BeginEvaluationBatch();
        lock (Gate)
        {
            var operands = Connector.CurrentOptic.MeritFunctionOperands.ToArray();
            var weightSum = operands
                .Where(operand => operand.Enabled
                    && MeritFunctionCatalog.CanonicalType(operand.Type) is not ("BLNK" or "DMFS"))
                .Sum(operand => Math.Abs(operand.Weight));
            return operands
                .Select((operand, index) =>
                {
                    var evaluation = MeritFunctionCatalog.Evaluate(Connector.CurrentOptic, operand);
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
                        operand.PolychromaticReference);
                })
                .ToArray();
        }
    }

    public void SetMeritFunction(IReadOnlyList<MeritOperandRowDto> operands)
    {
        Mutate(WorkspaceChangeCategory.Optimization, () => Connector.ReplaceMeritFunction(
            operands.Select(operand => new MeritOperandDefinition
            {
                Enabled = operand.Enabled,
                Type = MeritFunctionCatalog.CanonicalType(operand.Type),
                Surface = Math.Max(0, operand.Surface),
                Field = Math.Max(0, operand.Field),
                Wavelength = Math.Max(0, operand.Wavelength),
                Hx = Math.Clamp(operand.Hx, -1, 1),
                Hy = Math.Clamp(operand.Hy, -1, 1),
                Px = Math.Clamp(operand.Px, -1, 1),
                Py = Math.Clamp(operand.Py, -1, 1),
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
                PolychromaticReference = operand.PolychromaticReference
            })));
    }

    public void GenerateDefaultMeritFunction(MeritFunctionPreset preset)
    {
        Mutate(WorkspaceChangeCategory.Optimization, () => Connector.GenerateDefaultMeritFunction(preset));
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
                _ => throw new ArgumentOutOfRangeException(nameof(settings.ImageQuality))
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
        Mutate(WorkspaceChangeCategory.Optimization, () => Connector.GenerateMeritFunction(
            coreSettings,
            settings.StartRow,
            settings.ReplaceExisting));
    }
}
