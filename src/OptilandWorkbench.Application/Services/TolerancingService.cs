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
using OptilandWorkbench.Core.Tolerancing;
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
    private const int MaximumOperandCount = 1_000;
    public TolerancingService(WorkspaceCoordinator workspace)
        : base(workspace)
    {
    }

    public IReadOnlyList<ToleranceOperandDto> GenerateWizard(ToleranceWizardSettingsDto settings)
    {
        lock (Gate)
        {
            var surfaces = Runtime.CurrentOptic.SurfaceGroup.Items;
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

                if (settings.IncludeConic && Math.Abs(surface.Radius) > 1e-9)
                {
                    AddSymmetric(rows, ToleranceOperandKind.Conic, surfaceNumber, settings.ConicTolerance, settings.Distribution, "圆锥系数");
                }

                if (settings.IncludeThickness && surfaceNumber < surfaces.Count - 1)
                {
                    AddSymmetric(rows, ToleranceOperandKind.Thickness, surfaceNumber, settings.ThicknessTolerance, settings.Distribution, "轴向厚度/空气间隔");
                }

                if (settings.IncludeDecenter
                    && !settings.UseElementGroups
                    && surfaceNumber > 0
                    && surfaceNumber < surfaces.Count - 1)
                {
                    AddSymmetric(rows, ToleranceOperandKind.DecenterX, surfaceNumber, settings.DecenterTolerance, settings.Distribution, "表面 X 偏心");
                    AddSymmetric(rows, ToleranceOperandKind.DecenterY, surfaceNumber, settings.DecenterTolerance, settings.Distribution, "表面 Y 偏心");
                }

                if (settings.IncludeTilt
                    && !settings.UseElementGroups
                    && surfaceNumber > 0
                    && surfaceNumber < surfaces.Count - 1)
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

                if (settings.IncludeAsphereCoefficients)
                {
                    for (var parameterIndex = 1;
                         parameterIndex <= AsphereCoefficientCount(surface);
                         parameterIndex++)
                    {
                        AddSymmetric(
                            rows,
                            ToleranceOperandKind.AsphereCoefficient,
                            surfaceNumber,
                            settings.AsphereCoefficientTolerance,
                            settings.Distribution,
                            $"非球面参数 {parameterIndex}",
                            parameterIndex: parameterIndex);
                    }
                }
            }

            if (settings.UseElementGroups && (settings.IncludeDecenter || settings.IncludeTilt))
            {
                foreach (var (elementStart, elementEnd) in FindElementGroups(surfaces, first, last))
                {
                    if (settings.IncludeDecenter)
                    {
                        AddSymmetric(rows, ToleranceOperandKind.ElementDecenterX, elementStart, settings.DecenterTolerance, settings.Distribution, "元件 X 偏心", elementEnd);
                        AddSymmetric(rows, ToleranceOperandKind.ElementDecenterY, elementStart, settings.DecenterTolerance, settings.Distribution, "元件 Y 偏心", elementEnd);
                    }

                    if (settings.IncludeTilt)
                    {
                        AddSymmetric(rows, ToleranceOperandKind.ElementTiltX, elementStart, settings.TiltToleranceDegrees, settings.Distribution, "元件 X 倾斜", elementEnd);
                        AddSymmetric(rows, ToleranceOperandKind.ElementTiltY, elementStart, settings.TiltToleranceDegrees, settings.Distribution, "元件 Y 倾斜", elementEnd);
                    }
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
        ArgumentNullException.ThrowIfNull(operands);
        lock (Gate)
        {
            var messages = new List<string>();
            if (operands.Count > MaximumOperandCount)
            {
                messages.Add($"公差操作数不能超过 {MaximumOperandCount:N0} 行。");
                return new ToleranceValidationResultDto(false, messages);
            }
            var surfaceCount = Runtime.CurrentOptic.SurfaceGroup.Items.Count;
            var enabled = operands.Where(item => item.Enabled).ToArray();
            if (!enabled.Any(item => item.Kind != ToleranceOperandKind.Compensator))
            {
                messages.Add("至少需要一个启用的公差操作数。");
            }

            foreach (var operand in enabled)
            {
                if (!Enum.IsDefined(operand.Kind))
                {
                    messages.Add($"第 {operand.Index} 行的公差类型无效。");
                }
                if (!Enum.IsDefined(operand.Distribution))
                {
                    messages.Add($"第 {operand.Index} 行的概率分布无效。");
                }
                if (operand.SurfaceNumber < 0 || operand.SurfaceNumber >= surfaceCount)
                {
                    messages.Add($"第 {operand.Index} 行的表面 {operand.SurfaceNumber} 不存在。");
                }

                if (IsElementOperand(operand.Kind)
                    && (operand.EndSurfaceNumber <= operand.SurfaceNumber
                        || operand.EndSurfaceNumber >= surfaceCount))
                {
                    messages.Add($"第 {operand.Index} 行的元件终止面无效。");
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
                    var surface = Runtime.CurrentOptic.SurfaceGroup.Items[operand.SurfaceNumber];
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

                    if (operand.Kind == ToleranceOperandKind.AsphereCoefficient
                        && (operand.ParameterIndex <= 0
                            || operand.ParameterIndex > AsphereCoefficientCount(surface)))
                    {
                        messages.Add($"第 {operand.Index} 行的非球面参数不存在。");
                    }
                }
            }

            foreach (var duplicate in enabled
                         .GroupBy(item => (
                             item.Kind,
                             item.SurfaceNumber,
                             item.EndSurfaceNumber,
                             item.ParameterIndex))
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

    private static int AsphereCoefficientCount(OpticalSurface surface) => surface.Geometry switch
    {
        EvenAsphereGeometry even => even.Coefficients.Count,
        OddAsphereGeometry odd => odd.Coefficients.Count,
        ForbesQGeometry forbes => forbes.QCoefficients.Count,
        _ => 0
    };

    private static bool IsElementOperand(ToleranceOperandKind kind) => kind is
        ToleranceOperandKind.ElementDecenterX
        or ToleranceOperandKind.ElementDecenterY
        or ToleranceOperandKind.ElementTiltX
        or ToleranceOperandKind.ElementTiltY;

    private static IReadOnlyList<(int Start, int End)> FindElementGroups(
        IReadOnlyList<OpticalSurface> surfaces,
        int first,
        int last)
    {
        var groups = new List<(int Start, int End)>();
        var index = Math.Max(1, first);
        var finalSurface = Math.Min(last, surfaces.Count - 2);
        while (index <= finalSurface)
        {
            if (!IsGlassMedium(surfaces[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < surfaces.Count - 1 && IsGlassMedium(surfaces[index]))
            {
                index++;
            }

            if (index <= last && index > start)
            {
                groups.Add((start, index));
            }
        }

        return groups;
    }

    private static bool IsGlassMedium(OpticalSurface surface) =>
        !surface.IsReflective && surface.MaterialAfter.RefractiveIndex(587.6) > 1.0001;

    public Task<TolerancingResultDto> RunAsync(
        TolerancingRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        Optic snapshot;
        long sourceRevision;
        CancellationTokenSource linked;
        lock (Gate)
        {
            snapshot = Optic.FromSnapshot(Runtime.CurrentOptic.ToSnapshot());
            sourceRevision = Workspace.Revision;
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }

        return RunTolerancingWorkerAsync(snapshot, sourceRevision, request, linked);
    }

    private void ValidateRequest(TolerancingRequestDto request)
    {
        int surfaceCount;
        lock (Gate)
        {
            surfaceCount = Runtime.CurrentOptic.SurfaceGroup.Items.Count;
        }
        if (request.SurfaceNumber < 0 || request.SurfaceNumber >= surfaceCount)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "指定的公差表面不存在。");
        }
        if (request.Trials < 0 || request.Trials > MonteCarlo.MaximumTrialCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"Monte Carlo 次数必须在 0 到 {MonteCarlo.MaximumTrialCount:N0} 之间。");
        }
        if (request.CompensationIterations is < 0 or > MonteCarlo.MaximumCompensationIterations
            || request.MaxDegreeOfParallelism is 0 or < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "补偿迭代次数或并行度无效。");
        }
        if (!double.IsFinite(request.RadiusSigma)
            || !double.IsFinite(request.ThicknessSigma)
            || !double.IsFinite(request.YieldLimit)
            || !double.IsFinite(request.InverseValue)
            || request.YieldLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "公差标准差或良率限值无效。");
        }
        if (!Enum.IsDefined(request.Criterion) || !Enum.IsDefined(request.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "公差评价标准或运行模式无效。");
        }
        if (request.Mode is ToleranceAnalysisMode.InverseLimit or ToleranceAnalysisMode.InverseIncrement
            && request.InverseValue <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "反向极限或反向增量必须为正数。");
        }
        if (request.Mode is ToleranceAnalysisMode.InverseLimit or ToleranceAnalysisMode.InverseIncrement
            && request.Operands is not { Count: > 0 })
        {
            throw new ArgumentException("反向灵敏度需要显式公差操作数表。", nameof(request));
        }
        if (request.Operands is { Count: > 0 } operands)
        {
            var validation = ValidateOperands(operands);
            if (!validation.IsValid)
            {
                throw new ArgumentException(string.Join(" ", validation.Messages), nameof(request));
            }
        }
    }

    private static async Task<TolerancingResultDto> RunTolerancingWorkerAsync(
        Optic snapshot,
        long sourceRevision,
        TolerancingRequestDto request,
        CancellationTokenSource linked)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                using var cancellationScope = ComputationCancellation.Push(linked.Token);
                var worker = new WorkbenchRuntime(snapshot);
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
                    request.MaxDegreeOfParallelism,
                    request.Mode,
                    request.InverseValue);
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
                            view.Statistics.Yield),
                    view.SensitivityStatistics is null
                        ? null
                        : new TolerancingSensitivityStatisticsDto(
                            view.SensitivityStatistics.Nominal,
                            view.SensitivityStatistics.RssEstimatedChange,
                            view.SensitivityStatistics.EstimatedCriterion),
                    sourceRevision,
                    view.InverseRows?.Select(row => new TolerancingInverseRowDto(
                        row.Perturbation,
                        new TolerancingInverseEndpointDto(
                            row.Minimum.OriginalTolerance,
                            row.Minimum.AdjustedTolerance,
                            row.Minimum.Criterion,
                            row.Minimum.Status,
                            row.Minimum.Iterations),
                        new TolerancingInverseEndpointDto(
                            row.Maximum.OriginalTolerance,
                            row.Maximum.AdjustedTolerance,
                            row.Maximum.Criterion,
                            row.Maximum.Status,
                            row.Maximum.Iterations))).ToArray(),
                    view.AdjustedOperands,
                    view.InverseTarget);
            }, linked.Token).ConfigureAwait(false);
        }
    }

    private static void AddSymmetric(
        ICollection<ToleranceOperandDto> rows,
        ToleranceOperandKind kind,
        int surfaceNumber,
        double tolerance,
        ToleranceDistribution distribution,
        string comment,
        int endSurfaceNumber = -1,
        int parameterIndex = 0)
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
            comment,
            endSurfaceNumber,
            parameterIndex));
    }
}
