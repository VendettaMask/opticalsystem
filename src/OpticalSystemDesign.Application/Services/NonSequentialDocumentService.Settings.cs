using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.NonSequential;
using CoreSettings = OpticalSystemDesign.Core.NonSequential.NonSequentialTraceSettings;
using CoreWavelength = OpticalSystemDesign.Core.NonSequential.NonSequentialWavelength;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class NonSequentialDocumentService
{
    public void UpdateSceneSettings(NonSequentialSceneSettingsDto settings, string expectedSceneIdentity, long expectedRevision) =>
        MutateTransactional(WorkspaceChangeCategory.NonSequential, () =>
        {
            ArgumentNullException.ThrowIfNull(settings);
            if (Workspace.Revision != expectedRevision || NonSequentialSceneHasher.Compute(Runtime.CurrentNonSequentialDocument, Runtime.CurrentOptic.Materials) != expectedSceneIdentity)
                throw new InvalidOperationException("场景已更新，请重载设置。");
            if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.AmbientMaterial))
                throw new ArgumentException("请输入场景名称和环境介质。");
            var document = Runtime.CurrentNonSequentialDocument.Clone();
            document.Name = settings.Name.Trim(); document.AmbientMaterial = settings.AmbientMaterial.Trim();
            var s = settings.TraceSettings;
            document.TraceSettings = new CoreSettings(s.LayoutRaysPerSource, s.AnalysisRaysPerSource,
                s.MaximumTotalSourceRays, s.MaximumSegmentsPerRay, s.MaximumActiveBranches,
                s.MinimumRelativeIntensity, s.RandomSeed, s.SplitFresnelRays, s.UsePolarization, s.CoherentDetection);
            document.ReplaceWavelengths(settings.Wavelengths.Select(w => new CoreWavelength(w.Label, w.Nanometers, w.Weight, w.IsPrimary)));
            ValidateMaterials(document);
            Runtime.ReplaceNonSequentialDocument(document, "已应用非序列场景设置。");
        });
}
