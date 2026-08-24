using System.Reflection;
using Avalonia.Headless;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class TolerancingPanelRevisionTests
{
    [Fact]
    public async Task WorkspaceChangeCancelsRunAndPreventsStaleResultFromEnteringReport()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        WorkbenchApplication? application = null;
        TolerancingPanel? panel = null;
        CancellationTokenSource? runningCancellation = null;
        var sourceRevision = 0L;

        await session.Dispatch(() =>
        {
            application = WorkbenchApplication.Create("cooke");
            panel = new TolerancingPanel(
                application.Documents,
                application.Prescription,
                application.Tolerancing,
                application.Events);
            runningCancellation = new CancellationTokenSource();
            SetPrivateField(panel, "_runCancellation", runningCancellation);
            sourceRevision = application.Events.Revision;
            SetPrivateField(panel, "_lastResult", new TolerancingResultDto(
                "旧公差结果",
                Array.Empty<TolerancingSensitivityRowDto>(),
                Array.Empty<TolerancingTrialRowDto>(),
                "不得进入新系统报告",
                SourceRevision: sourceRevision));

            var surface = application.Prescription.GetSurfaces().First(item => item.Number == 2);
            application.Prescription.UpdateSurface(surface with { Thickness = surface.Thickness + 0.01 });

            Assert.True(runningCancellation!.IsCancellationRequested);
            Assert.Null(GetPrivateField<TolerancingResultDto>(panel!, "_lastResult"));
            var report = panel!.BuildToleranceReportText();
            Assert.DoesNotContain("旧公差结果", report, StringComparison.Ordinal);
            Assert.Contains("尚未运行", report, StringComparison.Ordinal);

            SetPrivateField(panel, "_lastResult", new TolerancingResultDto(
                "旧公差结果",
                Array.Empty<TolerancingSensitivityRowDto>(),
                Array.Empty<TolerancingTrialRowDto>(),
                "不得进入新系统报告",
                SourceRevision: sourceRevision));
            report = panel.BuildToleranceReportText();
            Assert.DoesNotContain("不得进入新系统报告", report, StringComparison.Ordinal);
            Assert.Contains("已禁止写入当前系统报告", report, StringComparison.Ordinal);
            panel.Dispose();
            application!.Dispose();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OperandEditsAreDirtyAndDocumentReplacementResetsToleranceState()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create("cooke");
            using var panel = new TolerancingPanel(
                application.Documents,
                application.Prescription,
                application.Tolerancing,
                application.Events);
            Assert.False(panel.HasUnsavedChanges);

            var operands = GetPrivateField<System.Collections.ObjectModel.ObservableCollection<ToleranceOperandEditorRow>>(
                panel,
                "_operands")!;
            operands[0].Comment = "需要保存的修改";
            Assert.True(panel.HasUnsavedChanges);

            application.Documents.NewBlank();

            Assert.False(panel.HasUnsavedChanges);
            Assert.Single(operands);
            Assert.NotEqual("需要保存的修改", operands[0].Comment);
        }, CancellationToken.None);
    }

    private static T? GetPrivateField<T>(object instance, string name) where T : class =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as T;

    private static void SetPrivateField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
}
