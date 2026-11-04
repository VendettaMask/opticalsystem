using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.NonSequential;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class NonSequentialDocumentService
{
    public Task<ZemaxNscImportResult> InspectZemaxNscAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => ZemaxNonSequentialReader.Read(path, cancellationToken), cancellationToken);

    public void ApplyZemaxNscImport(ZemaxNscImportResult result, string expectedSceneIdentity) =>
        MutateTransactional(WorkspaceChangeCategory.NonSequential, () =>
        {
            if (!result.CanApply) throw new InvalidOperationException("导入包含未解决的语义或资产问题，不能覆盖当前场景。请查看逐对象诊断。");
            if (expectedSceneIdentity != NonSequentialSceneHasher.Compute(Runtime.CurrentNonSequentialDocument, Runtime.CurrentOptic.Materials))
                throw new InvalidOperationException("检查期间场景或材料已改变，请重新检查导入。");
            var document = result.Document!.Clone(); ValidateMaterials(document);
            Runtime.ReplaceNonSequentialDocument(document, "已导入全部通过检查的 Zemax NSC 对象；可以撤销。");
        });
}
