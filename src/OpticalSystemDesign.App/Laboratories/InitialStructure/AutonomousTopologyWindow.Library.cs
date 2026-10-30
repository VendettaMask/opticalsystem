using System.Security.Cryptography;
using Avalonia.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

public sealed partial class AutonomousTopologyWindow
{
    private LensLibraryDocument _lensLibrary = new();
    private readonly TextBlock _librarySummary = Named(Text("镜头库尚无条目"), "AutonomousLibrarySummary");
    private void RefreshLensLibrarySummary() => _librarySummary.Text = UiLocalization.Text("镜头库条目") + $" {_lensLibrary.Entries.Length} · " + _lensLibrary.Name;
    private double ReadLibraryNumber(string id)
    {
        var number = _numbers[id];
        if (number.Value is not { } value || !decimal.TryParse(number.Text, out var text) || value != text)
            throw new ArgumentException(UiLocalization.Text("请填写有效数字"));
        return (double)value;
    }
    private static string RetrievalOutcomeText(string outcome) => UiLocalization.Text(outcome switch
    {
        "QualifiedRetrievedPrescription" => "已有镜头通过原规格验收",
        "WorkingSeedImageTargetsFailed" => "已有镜头可作工作起点，像质未达标",
        "OriginalAssessmentRejected" => "已有镜头未通过原规格验收",
        "OriginalAssessmentIncomplete" => "已有镜头原规格验收未完成",
        "SourceFieldTooSmall" => "来源镜头视场不足",
        "SourcePupilTooSlow" => "来源镜头孔径能力不足",
        "ScaleOutsideBounds" => "缩放比例超出限制",
        "FixedApertureMappingRequiresSameCount" => "固定口径迁移要求片数一致",
        "CementedSchedulingUnsupported" => "胶合镜头尚未接入自主调度",
        "ProtectedLibraryPrescription" => "来源镜头包含保护片",
        "SourceRejected" => "来源处方或索引不再一致",
        "TransferRejected" => "处方迁移不满足原任务权限或几何",
        "OriginalFirstOrderRejected" => "迁移后未通过原一阶标准",
        "PerEntryFunctionBudget" => "此条目评价额度不足",
        _ => "此条目尚未完成检索验收"
    });

    private async Task OpenLensLibraryAsync()
    {
        var path = await PickOpen("lens-library"); if (path is null) return;
        var library = await LensLibraryFiles.LoadAsync(path);
        _lensLibrary = library; MarkPlanEdited(); RefreshLensLibrarySummary();
        _choices["AutonomousRetrieval"].IsChecked = true;
        _status.Text = UiLocalization.Text("镜头库已载入，尚未检索或追迹");
    }
    private async Task SaveLensLibraryAsync()
    {
        var path = await PickSave("lens-library"); if (path is null) return;
        await LensLibraryFiles.SaveAsync(_lensLibrary, path);
        _status.Text = UiLocalization.Text("镜头库已保存，来源与处方指纹保留");
    }
    private async Task AddCurrentLensAsync()
    {
        var plan = ReadPlan(); var topology = plan.Seeds[0]; var hash = ContentFingerprint.Compute(plan);
        var entry = await Task.Run(() => LensLibraryService.Index("local-" + Guid.NewGuid().ToString("N"),
            new(LensSourceKind.UserProvidedLocalUse, topology.Name, "current-task:" + hash,
                "User-provided current prescription for local design; no redistribution license asserted.", hash),
            TopologyCompiler.Compile(topology, plan.Constraints)));
        AddLibraryEntry(entry);
    }
    private async Task ImportLensLibraryEntryAsync()
    {
        var path = await PickOpen("seed"); if (path is null) return;
        var constraints = new TopologyConstraints { MaximumElementCount = AutonomousTopologyIdentity.MaximumElements };
        await using var stream = BoundedFile.OpenRead(path, BoundedFile.MaximumOpticalDocumentBytes, "library source prescription");
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
        var topology = await AutonomousDesignFiles.ImportSeedAsync(path, constraints);
        var entry = await Task.Run(() => LensLibraryService.Index("local-" + Guid.NewGuid().ToString("N"),
            new(LensSourceKind.UserProvidedLocalUse, Path.GetFileName(path), Path.GetFullPath(path),
                "User-selected prescription for local design; no redistribution license asserted.", hash), TopologyCompiler.Compile(topology, constraints)));
        AddLibraryEntry(entry);
    }
    private void AddLibraryEntry(LensLibraryEntry entry)
    {
        var next = _lensLibrary with { Entries = _lensLibrary.Entries.Add(entry) };
        LensLibraryService.Validate(next);
        _lensLibrary = next; MarkPlanEdited(); RefreshLensLibrarySummary();
        _choices["AutonomousRetrieval"].IsChecked = true;
        _status.Text = UiLocalization.Text("镜头已收录，仅建立索引，尚未检索或验收");
    }
}
