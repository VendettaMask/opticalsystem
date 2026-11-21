using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class PrescriptionService
{
    public LensScaleSettingsDto GetLensScaleSettings()
    {
        lock (Gate)
        {
            var document = Runtime.CaptureDocument();
            return new(Workspace.Revision, Runtime.CurrentOptic.LensLengthUnit,
                Runtime.Surfaces.Select(s => new LensScaleSurfaceDto(s.Number, s.Label)).ToArray(), document.Configurations.Count);
        }
    }

    public void ScaleLens(LensScaleRequestDto request, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (Gate)
        {
            if (expectedRevision != Workspace.Revision) throw new InvalidOperationException("工程已变化，请重新打开镜头缩放。");
            if (request.TargetUnit is { } unit && !Enum.IsDefined(unit)) throw new ArgumentOutOfRangeException(nameof(request));
            if (request.TargetUnit.HasValue && request.Factor != 1) throw new ArgumentException("单位换算不能同时指定尺寸缩放因子。");
            var original = Runtime.CaptureDocument();
            var configurations = original.Configurations.Select(optic =>
            {
                if (request.TargetUnit is not { } target) return LensScaler.Scale(optic, request.Factor, request.FirstSurface, request.LastSurface);
                var converted = optic.Clone();
                converted.LensLengthUnit = target;
                return converted;
            }).ToArray();
            if (request.TargetUnit.HasValue ? original.Configurations.All(o => o.LensLengthUnit == request.TargetUnit.Value) : request.Factor == 1) return;
            var pickups = original.OperandPickups?.Select(p =>
            {
                if (request.TargetUnit.HasValue) return p;
                var target = LensScaler.OperandMultiplier(original.Configurations[p.ConfigurationIndex], p.Operand, request.Factor, request.FirstSurface, request.LastSurface);
                var source = LensScaler.OperandMultiplier(original.Configurations[p.SourceConfigurationIndex], p.SourceOperand, request.Factor, request.FirstSurface, request.LastSurface);
                var scale = p.Scale * target / source;
                var offset = p.Offset * target;
                if (!double.IsFinite(scale) || !double.IsFinite(offset) || (p.Scale != 0 && scale == 0) || (p.Offset != 0 && offset == 0))
                    throw new ArgumentException("缩放后的多配置拾取数值超出有效范围。");
                return p with { Scale = scale, Offset = offset };
            }).ToArray();
            // Complete validation on detached data before capturing history or publishing any events.
            _ = new MultiConfiguration(configurations, original.BrokenLinks, original.OperandRows, original.OperandVariables, pickups);
            var staged = original with { ActiveOptic = configurations[original.ActiveConfigurationIndex], Configurations = configurations, OperandPickups = pickups };
            Workspace.MutateTransactional(WorkspaceChangeCategory.Surface, () =>
                Runtime.CommitComputedDocument(staged, request.TargetUnit is { } newUnit
                    ? $"镜头数据单位已换算为 {newUnit.Symbol()}。"
                    : $"镜头已按 {request.Factor:G6} 倍缩放（表面 {request.FirstSurface}–{request.LastSurface}）。"),
                refreshAutomaticSemiDiameters: false);
        }
    }
}
