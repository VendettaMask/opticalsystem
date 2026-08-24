using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;

namespace OptilandWorkbench.Tests;

public sealed class DocumentRevisionAndSaveTests
{
    [Fact]
    public async Task SaveUpdatesPersistenceStatusWithoutPublishingAnOpticRevision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"save-revision-{Guid.NewGuid():N}.staropt");
        try
        {
            using var application = WorkbenchApplication.Create("cooke");
            var surface = application.Prescription.GetSurfaces()[1];
            application.Prescription.UpdateSurface(surface with { Radius = surface.Radius + 1 });

            var revisionBeforeSave = application.Events.Revision;
            var workspaceEvents = new List<WorkspaceChangedEventArgs>();
            var statusEventCount = 0;
            application.Events.Changed += (_, args) => workspaceEvents.Add(args);
            application.Events.StatusChanged += (_, _) => statusEventCount++;

            await application.Documents.SaveAsync(path);

            Assert.Equal(revisionBeforeSave, application.Events.Revision);
            Assert.Empty(workspaceEvents);
            Assert.Equal(1, statusEventCount);
            Assert.False(application.Documents.GetSnapshot().IsDirty);
            Assert.Equal(Path.GetFullPath(path), application.Documents.CurrentPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EditWhileSnapshotIsBeingSavedKeepsCurrentRevisionDirty()
    {
        var saveStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSaveToFinish = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var workspace = new WorkspaceCoordinator(new OpticContext(Optic.CreateCookeTriplet()));
        var documents = new OpticalDocumentService(
            workspace,
            async (_, _, cancellationToken) =>
            {
                saveStarted.SetResult();
                await allowSaveToFinish.Task.WaitAsync(cancellationToken);
            });
        var prescription = new PrescriptionService(workspace);
        var path = Path.Combine(Path.GetTempPath(), $"save-race-{Guid.NewGuid():N}.staropt");

        var saveTask = documents.SaveAsync(path);
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var surface = prescription.GetSurfaces()[1];
        prescription.UpdateSurface(surface with { Radius = surface.Radius + 1 });
        var editedRevision = workspace.Revision;
        allowSaveToFinish.SetResult();
        await saveTask;

        var snapshot = documents.GetSnapshot();
        Assert.Equal(editedRevision, snapshot.Revision);
        Assert.True(snapshot.IsDirty);
        Assert.Equal(Path.GetFullPath(path), documents.CurrentPath);
        Assert.Contains("当前修改尚未保存", snapshot.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueuedSavesWriteSnapshotsInCaptureOrderAndLeaveTheNewestRevisionClean()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writtenRadii = new List<double>();
        using var workspace = new WorkspaceCoordinator(new OpticContext(Optic.CreateCookeTriplet()));
        var callIndex = 0;
        var documents = new OpticalDocumentService(
            workspace,
            async (document, _, cancellationToken) =>
            {
                var currentCall = Interlocked.Increment(ref callIndex);
                if (currentCall == 1)
                {
                    firstStarted.SetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    secondStarted.SetResult();
                }

                writtenRadii.Add(document.ActiveOptic.SurfaceGroup.Items[1].Radius);
            });
        var prescription = new PrescriptionService(workspace);
        var path = Path.Combine(Path.GetTempPath(), $"ordered-save-{Guid.NewGuid():N}.staropt");

        var firstSave = documents.SaveAsync(path);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var surface = prescription.GetSurfaces()[1];
        var latestRadius = surface.Radius + 4.5;
        prescription.UpdateSurface(surface with { Radius = latestRadius });
        var secondSave = documents.SaveAsync(path);

        Assert.False(secondStarted.Task.IsCompleted);
        releaseFirst.SetResult();
        await Task.WhenAll(firstSave, secondSave);

        Assert.Equal(2, writtenRadii.Count);
        Assert.Equal(latestRadius, writtenRadii[^1], 12);
        Assert.False(documents.GetSnapshot().IsDirty);
    }

    [Fact]
    public async Task FailedQueuedSaveDoesNotBlockTheNextSnapshot()
    {
        using var workspace = new WorkspaceCoordinator(new OpticContext(Optic.CreateCookeTriplet()));
        var callIndex = 0;
        var documents = new OpticalDocumentService(
            workspace,
            (_, _, _) => Interlocked.Increment(ref callIndex) == 1
                ? Task.FromException(new IOException("Injected save failure."))
                : Task.CompletedTask);
        var path = Path.Combine(Path.GetTempPath(), $"failed-queued-save-{Guid.NewGuid():N}.staropt");

        var failed = documents.SaveAsync(path);
        var succeeded = documents.SaveAsync(path);

        await Assert.ThrowsAsync<IOException>(() => failed);
        await succeeded;
        Assert.Equal(2, callIndex);
        Assert.False(documents.GetSnapshot().IsDirty);
    }

    [Fact]
    public void RejectedDeletesUpdateStatusWithoutChangingTheOpticRevision()
    {
        using var application = WorkbenchApplication.Create("blank");
        var initialRevision = application.Events.Revision;
        var workspaceEventCount = 0;
        var statusEventCount = 0;
        application.Events.Changed += (_, _) => workspaceEventCount++;
        application.Events.StatusChanged += (_, _) => statusEventCount++;

        application.Prescription.RemoveSurface(0);
        application.Prescription.RemoveField(0);
        application.Prescription.RemoveWavelength(0);

        Assert.Equal(initialRevision, application.Events.Revision);
        Assert.Equal(0, workspaceEventCount);
        Assert.Equal(3, statusEventCount);
    }

    [Fact]
    public async Task OpeningAFileIsCleanButSwitchingConfigurationMarksTheProjectDirty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"configuration-dirty-{Guid.NewGuid():N}.staropt");
        try
        {
            using (var source = WorkbenchApplication.Create("cooke"))
            {
                source.MultiConfiguration.Add();
                await source.Documents.SaveAsync(path);
            }

            using var restored = WorkbenchApplication.Create("blank");
            await restored.Documents.OpenAsync(path);
            Assert.False(restored.Documents.GetSnapshot().IsDirty);
            var events = new List<WorkspaceChangedEventArgs>();
            restored.Events.Changed += (_, args) => events.Add(args);

            restored.MultiConfiguration.Activate(1);

            Assert.True(restored.Documents.GetSnapshot().IsDirty);
            Assert.False(events.Single().FileSwitched);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
