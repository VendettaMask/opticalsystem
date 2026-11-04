using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.NonSequential;
using CoreDocument = OpticalSystemDesign.Core.NonSequential.NonSequentialDocument;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class NonSequentialDocumentService
{
    public string GetDesignSceneIdentity()
    { lock (Gate) return NonSequentialSceneHasher.Compute(Runtime.CurrentNonSequentialDocument, Runtime.CurrentOptic.Materials); }

    public Task SaveDesignExperimentAsync(string path, NonSequentialDesignExperiment plan, NonSequentialDesignResult? optimization = null,
        NonSequentialToleranceResult? tolerance = null, CancellationToken cancellationToken = default)
    {
        lock (Gate)
        {
            if (plan.SourceSceneSha256 != NonSequentialSceneHasher.Compute(Runtime.CurrentNonSequentialDocument, Runtime.CurrentOptic.Materials))
                throw new InvalidOperationException("原始场景或材料已改变，不能把当前场景冒充该结果的输入。请恢复原场景后保存完整实验。");
            var source = new Core.Serialization.StarOptProjectDocument([Runtime.CurrentOptic.Clone()], 0,
                NonSequentialDocument: Runtime.CurrentNonSequentialDocument.Clone());
            var materials = Runtime.CurrentOptic.Materials.CreateSnapshot();
            var kind = tolerance is null ? NonSequentialDesignRunKind.Optimization : NonSequentialDesignRunKind.Tolerance;
            return Task.Run(() => NonSequentialDesignArchiveStore.SaveAsync(path, source, materials, plan, kind,
                optimization, tolerance, cancellationToken), cancellationToken);
        }
    }

    public Task<NonSequentialDesignReplayResult> ReplayDesignExperimentAsync(string path,
        IProgress<NonSequentialDesignProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => NonSequentialDesignArchiveStore.ReplayAsync(path, progress, cancellationToken), cancellationToken);
    public void UpdatePhysicsSettings(bool usePolarization, bool coherentDetection) =>
        MutateTransactional(WorkspaceChangeCategory.NonSequential, () =>
        {
            var original = Runtime.CurrentNonSequentialDocument;
            var updated = new CoreDocument(original.Name, original.Wavelengths, original.Objects, original.AmbientMaterial,
                original.TraceSettings with { UsePolarization = usePolarization, CoherentDetection = coherentDetection }, original.MeshAssets);
            Runtime.ReplaceNonSequentialDocument(updated, "已更新非序列偏振与相干设置；已有追迹结果需重新计算。");
        });

    public Task<NonSequentialDesignResult> OptimizeAsync(IReadOnlyList<NonSequentialDesignVariable> variables,
        IReadOnlyList<NonSequentialDesignTarget> targets, NonSequentialDesignSettings settings,
        IProgress<NonSequentialDesignProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        lock (Gate)
        {
            var document = Runtime.CurrentNonSequentialDocument.Clone();
            var materials = Runtime.CurrentOptic.Materials.CreateSnapshot();
            var frozenVariables = variables.ToArray(); var frozenTargets = targets.ToArray();
            return Task.Run(() => NonSequentialDesignEngine.Optimize(document, materials, frozenVariables, frozenTargets,
                settings, progress, cancellationToken), cancellationToken);
        }
    }

    public Task<NonSequentialToleranceResult> ToleranceAsync(IReadOnlyList<NonSequentialToleranceVariable> tolerances,
        IReadOnlyList<NonSequentialDesignTarget> targets, NonSequentialToleranceSettings settings,
        IReadOnlyList<NonSequentialDesignVariable>? compensators = null,
        IProgress<NonSequentialDesignProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        lock (Gate)
        {
            var document = Runtime.CurrentNonSequentialDocument.Clone(); var materials = Runtime.CurrentOptic.Materials.CreateSnapshot();
            var frozenTolerances = tolerances.ToArray(); var frozenTargets = targets.ToArray(); var frozenCompensators = compensators?.ToArray();
            return Task.Run(() => NonSequentialToleranceEngine.Run(document, materials, frozenTolerances, frozenTargets,
                settings.Samples, settings.ManufacturingSeed, settings.Trace, frozenCompensators,
                settings.CompensationTraceEvaluations, progress, cancellationToken), cancellationToken);
        }
    }

    public void ApplyDesignResult(NonSequentialDesignResult result) =>
        MutateTransactional(WorkspaceChangeCategory.NonSequential, () =>
        {
            ArgumentNullException.ThrowIfNull(result);
            if (!result.Passed || !result.TrainingMetrics.All(m => m.Passed) || !result.ValidationMetrics.All(m => m.Passed))
                throw new InvalidOperationException("候选尚未通过目标与独立种子验证，不能应用。");
            if (!StringComparer.Ordinal.Equals(result.SourceSceneSha256,
                NonSequentialSceneHasher.Compute(Runtime.CurrentNonSequentialDocument, Runtime.CurrentOptic.Materials)))
                throw new InvalidOperationException("场景或材料已改变，请基于当前场景重新运行设计。");
            var candidate = NonSequentialDesignParameters.Apply(Runtime.CurrentNonSequentialDocument, result.Variables, result.BestValues);
            ValidateMaterials(candidate);
            Runtime.ReplaceNonSequentialDocument(candidate, "已应用通过独立验证的非序列设计；可以撤销。");
        });
}
