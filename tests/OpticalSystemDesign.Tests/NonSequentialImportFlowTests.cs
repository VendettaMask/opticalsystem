using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class NonSequentialImportFlowTests
{
    [Theory]
    [InlineData("editor", false)]
    [InlineData("editor", true)]
    [InlineData("ImportZemaxAsync", false)]
    [InlineData("ImportZemaxAsync", true)]
    [InlineData("OpenAsync", false)]
    [InlineData("OpenAsync", true)]
    public async Task FilePickerEntryPointsImportDirectlyAndCancellationLeavesTheProjectUnchanged(string entry, bool cancel)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            using var files = new CameraSensorFileTests.TemporaryFiles();
            using var app = WorkbenchApplication.Create();
            app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
            app.NonSequential.AddObject(NonSequentialObjectKind.SourceRay);
            using var editor = entry == "editor" ? new NonSequentialObjectEditorPanel(app.NonSequential, app.Events) : null;
            Window owner = editor is not null ? new Window { Content = editor } : new MainWindow(
                new AppSettings { WorkbenchMode = "NonSequential" }, app, new WorkspaceSessionStore(files.Path("workspace")));
            var ready = owner is not MainWindow;
            if (owner is MainWindow main) main.StartupCompleted += (_, _) => ready = true;
            try
            {
                owner.Show();
                for (var n = 0; n < 250 && !ready; n++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
                Assert.True(ready);
                var original = app.Documents.GetRecoveryState();
                var file = DispatchProxy.Create<IStorageFile, AnalysisImageExportTests.StorageProxy>();
                ((AnalysisImageExportTests.StorageProxy)(object)file).Handler = method => method.Name == "get_Path" ? new Uri(NonSequentialColorFringesTests.Source) : null;
                var provider = DispatchProxy.Create<IStorageProvider, AnalysisImageExportTests.StorageProxy>();
                var pickerCalls = 0;
                ((AnalysisImageExportTests.StorageProxy)(object)provider).Handler = method =>
                {
                    if (method.Name == "get_CanOpen") return true;
                    if (method.Name != "OpenFilePickerAsync") throw new InvalidOperationException(method.Name);
                    pickerCalls++;
                    return Task.FromResult<IReadOnlyList<IStorageFile>>(cancel ? [] : [file]);
                };
                typeof(TopLevel).GetField("_storageProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, provider);
                if (editor is not null)
                {
                    var button = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == UiLocalization.Text("导入 Zemax NSC"));
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    for (var n = 0; n < 250 && !button.IsEnabled; n++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
                    Assert.True(button.IsEnabled);
                }
                else
                    await (Task)typeof(MainWindow).GetMethod(entry, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null)!;
                Assert.Equal(1, pickerCalls);
                Assert.Empty(owner.OwnedWindows);
                Assert.Equal(cancel ? 1 : 5, app.NonSequential.GetDocument().Objects.Count);
                if (cancel) Assert.Equal(original, app.Documents.GetRecoveryState());
            }
            finally { foreach (var window in owner.OwnedWindows.ToArray()) window.Close(); owner.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SupportedSceneImportsAllObjectsWithoutAnInspectionWindowAndCanBeUndone()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create();
            app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
            app.NonSequential.AddObject(NonSequentialObjectKind.SourceRay);
            var original = app.NonSequential.GetDesignSceneIdentity();
            var owner = new Window();
            try
            {
                owner.Show();
                await NonSequentialFileImport.ImportAsync(owner, app.NonSequential, NonSequentialColorFringesTests.Source);
                Assert.Equal(5, app.NonSequential.GetDocument().Objects.Count);
                Assert.Empty(owner.OwnedWindows);
                Assert.NotEqual(original, app.NonSequential.GetDesignSceneIdentity());
                Assert.True(app.Documents.Undo());
                Assert.Equal(original, app.NonSequential.GetDesignSceneIdentity());
            }
            finally { owner.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedOrMissingFileOnlyShowsTheShortFailureMessageAndPreservesTheScene(bool missing)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            using var files = new CameraSensorFileTests.TemporaryFiles();
            using var app = WorkbenchApplication.Create();
            app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
            app.NonSequential.AddObject(NonSequentialObjectKind.SourceRay);
            var original = app.Documents.GetRecoveryState();
            var path = missing ? files.Path("missing.zmx") : Path.Combine(Path.GetDirectoryName(NonSequentialColorFringesTests.Source)!, "Extraordinary dispersion.ZMX");
            var owner = new Window();
            try
            {
                owner.Show();
                var operation = NonSequentialFileImport.ImportAsync(owner, app.NonSequential, path);
                for (var n = 0; n < 250 && !owner.OwnedWindows.Any(); n++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
                var failure = Assert.Single(owner.OwnedWindows);
                Assert.Equal("nsc-import-unavailable", AutomationProperties.GetAutomationId(failure));
                Assert.Equal(new[] { UiLocalization.Text("无法导入此文件。"), Path.GetFileName(path) },
                    failure.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t) && t != UiLocalization.Text("关闭")));
                Assert.Empty(failure.GetVisualDescendants().OfType<TextBox>());
                Assert.Equal(original, app.Documents.GetRecoveryState());
                failure.GetVisualDescendants().OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await operation;
                Assert.Empty(owner.OwnedWindows);
                Assert.Equal(original, app.Documents.GetRecoveryState());
            }
            finally { foreach (var window in owner.OwnedWindows.ToArray()) window.Close(); owner.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClosingTheOwnerDuringReadingCancelsWithoutApplyingOrOpeningADialog()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            var pending = new TaskCompletionSource<ZemaxNscImportResult>();
            var service = DispatchProxy.Create<INonSequentialDocumentService, PendingImportService>();
            var proxy = (PendingImportService)service;
            proxy.Pending = pending.Task;
            var owner = new Window();
            owner.Show();
            var operation = NonSequentialFileImport.ImportAsync(owner, service, NonSequentialColorFringesTests.Source);
            owner.Close();
            Assert.True(proxy.Token.IsCancellationRequested);
            pending.SetResult(NonSequentialColorFringesTests.Import());
            await operation;
            Assert.False(proxy.Applied);
            Assert.Empty(owner.OwnedWindows);
        }, CancellationToken.None);
    }

    public class PendingImportService : DispatchProxy
    {
        public Task<ZemaxNscImportResult> Pending { get; set; } = null!;
        public CancellationToken Token { get; private set; }
        public bool Applied { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case nameof(INonSequentialDocumentService.GetDesignSceneIdentity): return "original";
                case nameof(INonSequentialDocumentService.InspectZemaxNscAsync): Token = (CancellationToken)args![1]!; return Pending;
                case nameof(INonSequentialDocumentService.ApplyZemaxNscImport): Applied = true; return null;
                default: throw new NotSupportedException(targetMethod.Name);
            }
        }
    }
}
