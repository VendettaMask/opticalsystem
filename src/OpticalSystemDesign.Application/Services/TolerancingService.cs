using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Phase;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Tolerancing;
using OpticalSystemDesign.Core.Visualization;
using ContractAnalysisColorMap = OpticalSystemDesign.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OpticalSystemDesign.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OpticalSystemDesign.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OpticalSystemDesign.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OpticalSystemDesign.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OpticalSystemDesign.Application.Contracts.AnalysisSeriesKind;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class TolerancingService : WorkbenchServiceBase, ITolerancingService
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

            if (!Enum.IsDefined(settings.RadiusMode)
                || ((settings.IncludeRadius && settings.RadiusMode == RadiusToleranceMode.Fringes) || settings.IncludeFringeIrregularity
                    || settings.ZernikeIrregularityKind == ToleranceOperandKind.FringeZernikeIrregularity)
                && !SurfaceFringePerturbation.IsValidTestWavelength(settings.TestWavelengthMicrometers))
                throw new ArgumentException("曲率公差模式或条纹检测波长无效。");
            if (settings.ZernikeIrregularityKind is { } requestedKind
                && (!requestedKind.UsesZernikeTerms() || settings.IncludeFringeIrregularity
                    || !SurfaceFormPerturbation.HasValidTerms(requestedKind.SurfaceFormModeFor()!.Value,
                        settings.MinimumZernikeTerm, settings.MaximumZernikeTerm)
                    || !double.IsFinite(settings.ZernikeIrregularity) || settings.ZernikeIrregularity < 0))
                throw new ArgumentException("Zernike 面形公差设置无效。");

            var first = Math.Clamp(settings.StartSurface, 0, surfaces.Count - 1);
            var last = Math.Clamp(settings.EndSurface, first, surfaces.Count - 1);
            var rows = new List<ToleranceOperandDto>();
            for (var surfaceNumber = first; surfaceNumber <= last; surfaceNumber++)
            {
                var surface = surfaces[surfaceNumber];
                var canUseFringes = surfaceNumber > 0 && surfaceNumber < surfaces.Count - 1
                    && SurfaceFringePerturbation.Supports(surface);
                if (canUseFringes && settings.IncludeRadius && settings.RadiusMode == RadiusToleranceMode.Fringes)
                    AddSymmetric(rows, ToleranceOperandKind.FringePower, surfaceNumber, settings.RadiusTolerance,
                        settings.Distribution, "曲率条纹", testWavelengthMicrometers: settings.TestWavelengthMicrometers);
                if (canUseFringes && settings.IncludeFringeIrregularity)
                    AddSymmetric(rows, ToleranceOperandKind.FringeIrregularity, surfaceNumber, settings.FringeIrregularity,
                        settings.Distribution, "面形不规则度", testWavelengthMicrometers: settings.TestWavelengthMicrometers);
                if (settings.ZernikeIrregularityKind is { } kind && settings.ZernikeIrregularity > 1e-15
                    && surfaceNumber > 0 && surfaceNumber < surfaces.Count - 1 && SurfaceFormPerturbation.Supports(surface))
                    rows.Add(new ToleranceOperandDto(rows.Count + 1, true, kind, surfaceNumber,
                        -settings.ZernikeIrregularity, settings.ZernikeIrregularity, settings.Distribution,
                        TestWavelengthMicrometers: kind == ToleranceOperandKind.FringeZernikeIrregularity
                            ? settings.TestWavelengthMicrometers : null,
                        MinimumZernikeTerm: settings.MinimumZernikeTerm, MaximumZernikeTerm: settings.MaximumZernikeTerm));

                if (settings.IncludeRadius && settings.RadiusMode != RadiusToleranceMode.Fringes && Math.Abs(surface.Radius) > 1e-9)
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

    public ToleranceValidationResultDto ValidateOperands(IReadOnlyList<ToleranceOperandDto> operands) =>
        ValidateOperands(operands, null, false);

    private ToleranceValidationResultDto ValidateOperands(IReadOnlyList<ToleranceOperandDto> operands,
        Func<int, double>? sharedFringeRadius, bool joint)
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
            if (!joint && !enabled.Any(item => item.Kind != ToleranceOperandKind.Compensator))
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
                else if (!joint && operand.Kind != ToleranceOperandKind.Compensator
                         && !operand.Kind.SurfaceFormModeFor().HasValue
                         && Math.Abs(operand.Minimum) <= 1e-15
                         && Math.Abs(operand.Maximum) <= 1e-15)
                {
                    messages.Add($"第 {operand.Index} 行的公差范围不能同时为零。");
                }

                if (operand.SurfaceNumber >= 0 && operand.SurfaceNumber < surfaceCount)
                {
                    var surface = Runtime.CurrentOptic.SurfaceGroup.Items[operand.SurfaceNumber];
                    if (IsFringeOperand(operand.Kind))
                    {
                        var wavelength = operand.TestWavelengthMicrometers
                            ?? SurfaceFringePerturbation.DefaultTestWavelengthMicrometers;
                        if (operand.SurfaceNumber == 0 || operand.SurfaceNumber == surfaceCount - 1
                            || !SurfaceFringePerturbation.Supports(surface))
                            messages.Add($"第 {operand.Index} 行的条纹公差需要有圆形有效口径的标准实体表面。");
                        else if (!SurfaceFringePerturbation.IsValidTestWavelength(wavelength))
                            messages.Add($"第 {operand.Index} 行的条纹检测波长须为 0.001–1000 µm。");
                        else if (operand.Kind == ToleranceOperandKind.FringePower
                            && (!SurfaceFringePerturbation.IsValidPowerChange(surface, wavelength, operand.Minimum, sharedFringeRadius?.Invoke(operand.SurfaceNumber))
                                || !SurfaceFringePerturbation.IsValidPowerChange(surface, wavelength, operand.Maximum, sharedFringeRadius?.Invoke(operand.SurfaceNumber))))
                            messages.Add($"第 {operand.Index} 行的曲率条纹范围使净口径超出曲面有效域。");
                    }

                    if (operand.Kind.SurfaceFormModeFor() is { } formMode)
                    {
                        var wavelength = operand.TestWavelengthMicrometers
                            ?? SurfaceFringePerturbation.DefaultTestWavelengthMicrometers;
                        if (operand.SurfaceNumber == 0 || operand.SurfaceNumber == surfaceCount - 1
                            || !SurfaceFormPerturbation.Supports(surface))
                            messages.Add($"第 {operand.Index} 行的面形公差需要具有居中圆形净口径的实体表面。");
                        if (operand.Minimum != -operand.Maximum || operand.Maximum < 0)
                            messages.Add($"第 {operand.Index} 行的面形公差必须使用对称范围。");
                        if (!SurfaceFormPerturbation.HasValidTerms(formMode, operand.MinimumZernikeTerm, operand.MaximumZernikeTerm))
                            messages.Add($"第 {operand.Index} 行的 Zernike 起止项无效。");
                        if (!Enum.IsDefined(operand.IsoUnit))
                            messages.Add($"第 {operand.Index} 行的 ISO 面形单位无效。");
                        if (!Enum.IsDefined(operand.IsoStatistics))
                            messages.Add($"第 {operand.Index} 行的 ISO 统计模型无效。");
                        if (SurfaceFormPerturbation.UsesTestWavelength(formMode, operand.IsoUnit)
                            && !SurfaceFringePerturbation.IsValidTestWavelength(wavelength))
                            messages.Add($"第 {operand.Index} 行的条纹检测波长须为 0.001–1000 µm。");
                        if (formMode == SurfaceFormMode.IsoPower && operand.Distribution != ToleranceDistribution.Normal)
                            messages.Add($"第 {operand.Index} 行的 ISOA 使用截断正态分布。");
                    }

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
                             EndSurfaceNumber: IsFringeOperand(item.Kind) || item.Kind.SurfaceFormModeFor().HasValue ? -1 : item.EndSurfaceNumber,
                             ParameterIndex: IsFringeOperand(item.Kind) || item.Kind.SurfaceFormModeFor().HasValue ? 0 : item.ParameterIndex))
                         .Where(group => group.Count() > 1))
            {
                messages.Add($"{duplicate.Key.Kind} / 表面 {duplicate.Key.SurfaceNumber} 重复定义。");
            }

            foreach (var group in enabled.GroupBy(row => row.SurfaceNumber))
            {
                if (group.Any(row => row.Kind == ToleranceOperandKind.FringePower)
                    && group.Any(row => row.Kind == ToleranceOperandKind.Radius))
                    messages.Add($"表面 {group.Key} 的 TRAD 与 TFRN 同时定义了曲率误差。");
                if (group.Any(row => row.Kind == ToleranceOperandKind.IsoPower)
                    && group.Any(row => row.Kind is ToleranceOperandKind.FringePower or ToleranceOperandKind.Radius))
                    messages.Add($"表面 {group.Key} 的 ISOA 与 TRAD 或 TFRN 同时定义了曲率误差。");
                var irregularities = group.Count(row => row.Kind.UsesZernikeTerms() || row.Kind.UsesIsoStatistics()
                    || row.Kind == ToleranceOperandKind.FringeIrregularity);
                if (irregularities == 2 && ToleranceSurfaceFormExtensions.TryGetIsoPair(group, out var total, out var rotational))
                {
                    var b = total.Maximum * total.IsoMillimetersPerUnit();
                    var c = rotational.Maximum * rotational.IsoMillimetersPerUnit();
                    if (!(c < b || (b == 0 && c == 0)))
                        messages.Add($"表面 {group.Key} 的 ISOC 最大峰谷值换算后须小于 ISOB，或两者均为零。");
                }
                else if (irregularities > 1)
                    messages.Add($"表面 {group.Key} 只能使用一种面形不规则度公差，或联合使用 ISOB 与 ISOC。");
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

    private static bool IsFringeOperand(ToleranceOperandKind kind) =>
        kind is ToleranceOperandKind.FringePower or ToleranceOperandKind.FringeIrregularity;

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
        request = FreezeRequest(request);
        if (request.ConfigurationSettings is { Scope: not ToleranceConfigurationScope.Current })
        {
            JointToleranceSnapshot joint;
            long revision;
            CancellationTokenSource cancellation;
            lock (Gate)
            {
                joint = Runtime.CaptureJointToleranceSnapshot();
                revision = Workspace.Revision;
                cancellation = Workspace.LinkDocumentToken(cancellationToken);
            }
            return RunJointWorkerAsync(joint, revision, request, cancellation);
        }
        Optic snapshot;
        long sourceRevision;
        CancellationTokenSource linked;
        lock (Gate)
        {
            ValidateRequest(request);
            snapshot = Optic.FromSnapshot(Runtime.CurrentOptic.ToSnapshot());
            sourceRevision = Workspace.Revision;
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }

        return RunTolerancingWorkerAsync(snapshot, sourceRevision, request, linked);
    }

    internal void ValidateRequest(TolerancingRequestDto request, Func<int, double>? sharedFringeRadius = null, bool joint = false)
    {
        if (request.ConfigurationSettings is { } scope
            && (scope.Scope != ToleranceConfigurationScope.Current || scope.Configurations is { Count: > 0 }
                || scope.Limits is { Count: > 0 } || scope.MceRows is { Count: > 0 })
            || request.AdditionalCompensators?.Any(row => row.Configuration != 0) == true)
            throw new ArgumentException("指定配置的验收、补偿与配置操作数须在多配置公差中使用。");
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
        if (!joint)
        {
            var phases = (request.AdditionalCompensators ?? []).Select(row => row.Stage);
            if (request.Operands?.Any(row => row.Enabled) != true
                || request.Operands.Any(row => row.Enabled && row.Kind == ToleranceOperandKind.Compensator))
                phases = phases.Append(WorkbenchRuntime.ImageCompensationStage(request.AdditionalCompensators));
            ValidateStageObjectives(request, phases);
            ValidateMeritSources(request, Runtime.CurrentOptic);
        }
        ValidateMtfAndCompensation(request);
        if (request.Operands is { Count: > 0 } operands)
        {
            var validation = ValidateOperands(operands, sharedFringeRadius, joint);
            if (!validation.IsValid)
            {
                throw new ArgumentException(string.Join(" ", validation.Messages), nameof(request));
            }
        }
    }

    private static async Task<TolerancingResultDto> RunJointWorkerAsync(
        JointToleranceSnapshot snapshot, long revision, TolerancingRequestDto request, CancellationTokenSource cancellation)
    {
        using (cancellation)
        {
            return await Task.Run(() =>
            {
                using var computation = ComputationCancellation.Push(cancellation.Token);
                return WorkbenchRuntime.RunJointTolerancing(snapshot, request, revision, cancellation.Token);
            }, cancellation.Token).ConfigureAwait(false);
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
                var nominalSnapshot = request.Trials > 0 ? snapshot.ToSnapshot() : null;
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
                    request.InverseValue, request.MtfSettings, request.CompensationAlgorithm, request.AdditionalCompensators, request.StageObjectives, request.MeritTable);
                linked.Token.ThrowIfCancellationRequested();
                var trials = view.TrialRows.Select(row => new TolerancingTrialRowDto(
                    row.Trial, row.Merit, row.CompensatedMerit, row.Degradation, row.Fields, row.UncompensatedFields,
                    row.Compensators, row.Passed, row.AcceptanceMargin, row.CriterionValue, row.Stages)).ToArray();
                return new TolerancingResultDto(
                    view.Summary,
                    view.SensitivityRows.Select(row => new TolerancingSensitivityRowDto(
                        row.Perturbation,
                        row.DeltaMerit,
                        row.NegativeMerit,
                        row.PositiveMerit,
                        row.WorstMerit, row.NegativeFields, row.PositiveFields, row.NegativeCompensators, row.PositiveCompensators,
                        NegativeStages: row.NegativeStages, PositiveStages: row.PositiveStages)).ToArray(),
                    trials,
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
                            view.Statistics.Yield, view.Statistics.Percentile05),
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
                    view.InverseTarget, view.MtfSettings, view.NominalFields, view.FieldStatistics, view.NominalCompensators,
                    view.NominalStages, CaptureWorstSample(nominalSnapshot, request, view, trials));
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
        int parameterIndex = 0,
        double? testWavelengthMicrometers = null)
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
            parameterIndex,
            testWavelengthMicrometers));
    }
}
