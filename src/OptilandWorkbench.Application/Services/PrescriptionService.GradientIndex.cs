using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Application.Services;

internal sealed partial class PrescriptionService
{
    public IReadOnlyList<GradientIndexProfileOptionDto> GetGradientIndexProfiles() => Enumerable.Range(1, 5).Select(number =>
        new GradientIndexProfileOptionDto($"gradient{number}", $"Gradient {number}",
            GradientIndexProfileData.Keys($"gradient{number}").Select(key => new GradientIndexCoefficientOptionDto(key,
                key is "n0" or "n0Squared" ? "无量纲" : "镜头长度单位的负幂",
                key == "n0Squared" ? 2.25 : key == "n0" ? 1.5 : 0,
                GradientIndexParameters.ScalarParaxialVariableDisabledReason($"gradient{number}", key))).ToArray())).ToArray();

    public GradientIndexMaterialEditDto? GetGradientIndexMaterial(int surfaceNumber)
    {
        lock (Gate)
        {
            var surface = FindSurface(surfaceNumber) ?? throw new ArgumentException("表面不存在。");
            if (surface.MaterialAfter is not GradientIndexMaterial material) return null;
            var options = material.IntegrationOptions;
            var coefficients = GradientIndexProfileData.Values(material.Profile).Select(pair =>
            {
                var variable = material.Variables.TryGetValue(pair.Key, out var range);
                var width = Math.Max(Math.Abs(pair.Value), .1);
                return new GradientIndexCoefficientEditDto(pair.Key, pair.Value, variable,
                    range?.Minimum ?? (pair.Key is "n0" or "n0Squared" ? Math.Max(double.Epsilon, pair.Value / 2) : pair.Value - width),
                    range?.Maximum ?? pair.Value + width);
            }).ToArray();
            var dispersion = (material.Profile as Gradient5IndexProfile)?.Dispersion;
            return new(material.Name, GradientIndexProfileData.Kind(material.Profile), coefficients,
                dispersion is null ? null : new(dispersion.ReferenceWavelengthNanometers, dispersion.MinimumWavelengthNanometers,
                    dispersion.MaximumWavelengthNanometers, dispersion.K, dispersion.L),
                new(material.MaximumPathLength, options.MaximumStep, options.MinimumStep, options.PositionTolerance,
                    options.DirectionTolerance, options.OpticalPathTolerance, options.RelativeTolerance, options.SurfaceTolerance, options.MaximumAttempts));
        }
    }

    public void UpdateGradientIndexMaterial(int surfaceNumber, GradientIndexMaterialEditDto material, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(material);
        MutateTransactional(WorkspaceChangeCategory.Surface, () =>
        {
            if (expectedRevision != Workspace.Revision) throw new InvalidOperationException("工程已变化，请重新打开 GRIN 材料编辑器。");
            var surface = FindSurface(surfaceNumber) ?? throw new ArgumentException("表面不存在。");
            if (surfaceNumber <= 0 || surfaceNumber >= Runtime.Surfaces.Count - 1 || surface.IsReflective
                || surface.Geometry is INonComputableGeometry)
                throw new NotSupportedException("GRIN 材料必须设置在具有下一边界的可计算透射面后。");
            if (!double.IsFinite(surface.Thickness) || surface.Thickness <= 0)
                throw new ArgumentException("GRIN 材料需要有限正厚度和明确的下一边界。");
            ArgumentNullException.ThrowIfNull(material.Coefficients);
            ArgumentNullException.ThrowIfNull(material.Integration);
            if (material.Coefficients.Any(c => c is null)
                || material.Coefficients.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != material.Coefficients.Count)
                throw new ArgumentException("GRIN 系数不能缺失或重复。");
            var values = material.Coefficients.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
            var d = material.Dispersion;
            var dispersion = d is null ? null : new Gradient5Dispersion(d.ReferenceWavelengthNanometers,
                d.MinimumWavelengthNanometers, d.MaximumWavelengthNanometers, d.K, d.L);
            var profile = GradientIndexProfileData.Create(material.Profile, values, dispersion);
            var variables = material.Coefficients.Where(c => c.Variable).ToDictionary(c => c.Key, c => new GradientIndexVariableRange(c.Minimum, c.Maximum));
            foreach (var key in variables.Keys)
                if (GradientIndexParameters.ScalarParaxialVariableDisabledReason(material.Profile, key) is { } reason)
                    throw new NotSupportedException(reason);
            var i = material.Integration;
            var replacement = new GradientIndexMaterial(material.Name, profile, i.MaximumPathLength,
                new(i.MaximumStep, i.MinimumStep, i.PositionTolerance, i.DirectionTolerance, i.OpticalPathTolerance,
                    i.RelativeTolerance, i.SurfaceTolerance, i.MaximumAttempts), variables);
            foreach (var wavelength in Runtime.CurrentOptic.Wavelengths)
            {
                _ = GradientIndexParaxialTransport.Between(replacement, 0, surface.Thickness, wavelength.Nanometers);
            }
            // The desktop field/automatic-aperture pipeline currently requires the formal scalar
            // paraxial model. Reject unsupported complete systems before publishing an edit.
            var candidate = Optic.FromSnapshot(Runtime.CurrentOptic.ToSnapshot());
            candidate.SurfaceGroup.Items[surfaceNumber].MaterialAfter = replacement;
            candidate.SurfaceGroup.Items[surfaceNumber + 1].MaterialBefore = replacement.Clone();
            _ = candidate.Paraxial.EstimateEffectiveFocalLength();
            Runtime.CaptureCurrentState();
            Runtime.CurrentOptic.InvalidateRayTraceCache();
            surface.MaterialAfter = replacement;
            Runtime.CommitSurfaceEdit(surface, nameof(OpticalSurface.Material));
        });
    }
}
