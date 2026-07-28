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

internal sealed class TolerancingService : WorkbenchServiceBase, ITolerancingService
{
    public TolerancingService(WorkspaceCoordinator workspace)
        : base(workspace)
    {
    }

    public IReadOnlyList<ToleranceOperandDto> GenerateWizard(ToleranceWizardSettingsDto settings)
    {
        lock (Gate)
        {
            var surfaces = Connector.CurrentOptic.SurfaceGroup.Items;
            if (surfaces.Count == 0)
            {
                return Array.Empty<ToleranceOperandDto>();
            }

            var first = Math.Clamp(settings.StartSurface, 0, surfaces.Count - 1);
            var last = Math.Clamp(settings.EndSurface, first, surfaces.Count - 1);
            var rows = new List<ToleranceOperandDto>();
            for (var surfaceNumber = first; surfaceNumber <= last; surfaceNumber++)
            {
                var surface = surfaces[surfaceNumber];
                if (settings.IncludeRadius && Math.Abs(surface.Radius) > 1e-9)
                {
                    var tolerance = settings.RadiusMode == RadiusToleranceMode.Percent
                        ? Math.Abs(surface.Radius) * Math.Abs(settings.RadiusTolerance) / 100.0
                        : Math.Abs(settings.RadiusTolerance);
                    AddSymmetric(rows, ToleranceOperandKind.Radius, surfaceNumber, tolerance, settings.Distribution, "曲率半径");
                }

                if (settings.IncludeThickness && surfaceNumber < surfaces.Count - 1)
                {
                    AddSymmetric(rows, ToleranceOperandKind.Thickness, surfaceNumber, settings.ThicknessTolerance, settings.Distribution, "轴向厚度/空气间隔");
                }

                if (settings.IncludeDecenter && surfaceNumber > 0 && surfaceNumber < surfaces.Count - 1)
                {
                    AddSymmetric(rows, ToleranceOperandKind.DecenterX, surfaceNumber, settings.DecenterTolerance, settings.Distribution, "表面 X 偏心");
                    AddSymmetric(rows, ToleranceOperandKind.DecenterY, surfaceNumber, settings.DecenterTolerance, settings.Distribution, "表面 Y 偏心");
                }

                if (settings.IncludeTilt && surfaceNumber > 0 && surfaceNumber < surfaces.Count - 1)
                {
                    AddSymmetric(rows, ToleranceOperandKind.TiltX, surfaceNumber, settings.TiltToleranceDegrees, settings.Distribution, "表面 X 倾斜");
                    AddSymmetric(rows, ToleranceOperandKind.TiltY, surfaceNumber, settings.TiltToleranceDegrees, settings.Distribution, "表面 Y 倾斜");
                }

                var isGlass = surface.MaterialAfter.RefractiveIndex(587.6) > 1.0001
                    && !surface.IsReflective;
                if (isGlass && settings.IncludeRefractiveIndex)
                {
                    AddSymmetric(rows, ToleranceOperandKind.RefractiveIndex, surfaceNumber, settings.RefractiveIndexTolerance, settings.Distribution, "折射率");
                }

                if (isGlass && settings.IncludeAbbeNumber)
                {
                    AddSymmetric(rows, ToleranceOperandKind.AbbeNumber, surfaceNumber, settings.AbbeNumberTolerance, settings.Distribution, "阿贝数");
                }
            }

            if (settings.IncludeImageCompensator && surfaces.Count > 1)
            {
                rows.Add(new ToleranceOperandDto(
                    rows.Count + 1,
                    true,
                    ToleranceOperandKind.Compensator,
                    surfaces.Count - 2,
                    Math.Min(settings.CompensatorMinimum, settings.CompensatorMaximum),
                    Math.Max(settings.CompensatorMinimum, settings.CompensatorMaximum),
                    settings.Distribution,
                    "像面位置补偿（像面前间隔）"));
            }

            return rows;
        }
    }

    public ToleranceValidationResultDto ValidateOperands(IReadOnlyList<ToleranceOperandDto> operands)
    {
        lock (Gate)
        {
            var messages = new List<string>();
            var surfaceCount = Connector.CurrentOptic.SurfaceGroup.Items.Count;
            var enabled = operands.Where(item => item.Enabled).ToArray();
            if (!enabled.Any(item => item.Kind != ToleranceOperandKind.Compensator))
            {
                messages.Add("至少需要一个启用的公差操作数。");
            }

            foreach (var operand in enabled)
            {
                if (operand.SurfaceNumber < 0 || operand.SurfaceNumber >= surfaceCount)
                {
                    messages.Add($"第 {operand.Index} 行的表面 {operand.SurfaceNumber} 不存在。");
                }

                if (!double.IsFinite(operand.Minimum)
                    || !double.IsFinite(operand.Maximum)
                    || operand.Minimum > operand.Maximum)
                {
                    messages.Add($"第 {operand.Index} 行的最小值/最大值无效。");
                }
                else if (operand.Kind != ToleranceOperandKind.Compensator
                         && Math.Abs(operand.Minimum) <= 1e-15
                         && Math.Abs(operand.Maximum) <= 1e-15)
                {
                    messages.Add($"第 {operand.Index} 行的公差范围不能同时为零。");
                }

                if (operand.SurfaceNumber >= 0 && operand.SurfaceNumber < surfaceCount)
                {
                    var surface = Connector.CurrentOptic.SurfaceGroup.Items[operand.SurfaceNumber];
                    if (operand.Kind is ToleranceOperandKind.Thickness or ToleranceOperandKind.Compensator
                        && surface.Thickness + operand.Minimum < 0)
                    {
                        messages.Add($"第 {operand.Index} 行会产生负的厚度/间隔。");
                    }

                    if (operand.Kind == ToleranceOperandKind.Compensator
                        && operand.SurfaceNumber >= surfaceCount - 1)
                    {
                        messages.Add($"第 {operand.Index} 行的像面补偿器必须作用于像面之前的间隔。");
                    }

                    if (operand.Kind == ToleranceOperandKind.RefractiveIndex
                        && surface.MaterialAfter.RefractiveIndex(587.6) + operand.Minimum <= 1)
                    {
                        messages.Add($"第 {operand.Index} 行会产生无效的玻璃折射率。");
                    }

                    if (operand.Kind == ToleranceOperandKind.AbbeNumber
                        && GlassAbbeNumber(surface) + operand.Minimum <= 0.1)
                    {
                        messages.Add($"第 {operand.Index} 行会产生无效的阿贝数。");
                    }
                }
            }

            foreach (var duplicate in enabled
                         .GroupBy(item => (item.Kind, item.SurfaceNumber))
                         .Where(group => group.Count() > 1))
            {
                messages.Add($"{duplicate.Key.Kind} / 表面 {duplicate.Key.SurfaceNumber} 重复定义。");
            }

            return new ToleranceValidationResultDto(messages.Count == 0, messages);
        }
    }

    private static double GlassAbbeNumber(OpticalSurface surface)
    {
        return surface.MaterialAfter switch
        {
            AbbeMaterial abbe => abbe.Vd,
            CatalogGlassMaterial { ZemaxData: not null } catalog =>
                catalog.ZemaxData.ReferenceAbbeNumber,
            _ => 50
        };
    }

    public Task<TolerancingResultDto> RunAsync(
        TolerancingRequestDto request,
        CancellationToken cancellationToken = default)
    {
        Optic snapshot;
        CancellationTokenSource linked;
        lock (Gate)
        {
            snapshot = Optic.FromSnapshot(Connector.CurrentOptic.ToSnapshot());
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }

        return RunTolerancingWorkerAsync(snapshot, request, linked);
    }

    private static async Task<TolerancingResultDto> RunTolerancingWorkerAsync(
        Optic snapshot,
        TolerancingRequestDto request,
        CancellationTokenSource linked)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                using var cancellationScope = ComputationCancellation.Push(linked.Token);
                var worker = new OpticalWorkspaceModel(snapshot);
                var view = worker.RunTolerancing(
                    worker.Surfaces.FirstOrDefault(surface => surface.Number == request.SurfaceNumber),
                    request.RadiusSigma,
                    request.ThicknessSigma,
                    request.Trials,
                    request.Seed,
                    request.CompensationIterations,
                    request.Operands,
                    request.Criterion,
                    request.YieldLimit,
                    linked.Token,
                    request.MaxDegreeOfParallelism);
                linked.Token.ThrowIfCancellationRequested();
                return new TolerancingResultDto(
                    view.Summary,
                    view.SensitivityRows.Select(row => new TolerancingSensitivityRowDto(
                        row.Perturbation,
                        row.DeltaMerit,
                        row.NegativeMerit,
                        row.PositiveMerit,
                        row.WorstMerit)).ToArray(),
                    view.TrialRows.Select(row => new TolerancingTrialRowDto(
                        row.Trial,
                        row.Merit,
                        row.CompensatedMerit,
                        row.Degradation)).ToArray(),
                    view.Details,
                    view.Statistics is null
                        ? null
                        : new TolerancingStatisticsDto(
                            view.Statistics.Nominal,
                            view.Statistics.Mean,
                            view.Statistics.StandardDeviation,
                            view.Statistics.Minimum,
                            view.Statistics.Maximum,
                            view.Statistics.Percentile50,
                            view.Statistics.Percentile90,
                            view.Statistics.Percentile95,
                            view.Statistics.Yield));
            }, linked.Token).ConfigureAwait(false);
        }
    }

    private static void AddSymmetric(
        ICollection<ToleranceOperandDto> rows,
        ToleranceOperandKind kind,
        int surfaceNumber,
        double tolerance,
        ToleranceDistribution distribution,
        string comment)
    {
        var absolute = Math.Abs(tolerance);
        if (absolute <= 1e-15)
        {
            return;
        }

        rows.Add(new ToleranceOperandDto(
            rows.Count + 1,
            true,
            kind,
            surfaceNumber,
            -absolute,
            absolute,
            distribution,
            comment));
    }
}
