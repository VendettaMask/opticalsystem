using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Services;
using System.Text.Json;

namespace OptilandWorkbench.Tests;

public sealed class HighPriorityReliabilityTests
{
    [Fact]
    public void UndoRedoRestoresTheCompleteMultiConfigurationDocument()
    {
        var runtime = new WorkbenchRuntime(Optic.CreateDemo());
        runtime.AddMultiConfiguration();
        var before = SerializeDocument(runtime.CaptureDocument());
        var surface = runtime.Surfaces[2];

        runtime.CaptureCurrentState();
        surface.Radius += 6.25;
        runtime.CommitSurfaceEdit(surface, nameof(surface.Radius));
        surface.Thickness += 1.75;
        runtime.CommitSurfaceEdit(surface, nameof(surface.Thickness));
        surface.Material = "Air";
        runtime.CommitSurfaceEdit(surface, nameof(surface.Material));

        var changedDocument = runtime.CaptureDocument();
        var changed = SerializeDocument(changedDocument);
        Assert.All(changedDocument.Configurations, configuration =>
        {
            var changedSurface = configuration.SurfaceGroup.Items[2];
            Assert.Equal(surface.Radius, changedSurface.Radius, 12);
            Assert.Equal(surface.Thickness, changedSurface.Thickness, 12);
            Assert.Equal("Air", changedSurface.Material);
            Assert.Equal(1, changedSurface.MaterialAfter.RefractiveIndex(587.6), 12);
        });

        Assert.True(runtime.Undo());
        Assert.Equal(before, SerializeDocument(runtime.CaptureDocument()));
        Assert.True(runtime.Redo());
        Assert.Equal(changed, SerializeDocument(runtime.CaptureDocument()));
    }

    [Fact]
    public async Task UndoRedoPreservesMultiConfigurationBrokenLinksAfterSavingAndReopening()
    {
        var path = Path.Combine(Path.GetTempPath(), $"undo-multiconfig-{Guid.NewGuid():N}.staropt");
        try
        {
            var runtime = new WorkbenchRuntime(Optic.CreateDemo());
            var alternateIndex = runtime.AddMultiConfiguration();
            runtime.ActivateMultiConfiguration(alternateIndex);
            var alternateSurface = runtime.Surfaces[2];
            var overriddenRadius = alternateSurface.Radius + 9.5;
            alternateSurface.Radius = overriddenRadius;
            runtime.CommitSurfaceEdit(alternateSurface, nameof(alternateSurface.Radius));
            runtime.ActivateMultiConfiguration(0);
            var before = SerializeDocument(runtime.CaptureDocument());
            var baseSurface = runtime.Surfaces[2];

            runtime.CaptureCurrentState();
            baseSurface.Radius -= 4.25;
            runtime.CommitSurfaceEdit(baseSurface, nameof(baseSurface.Radius));
            var changedDocument = runtime.CaptureDocument();
            var changed = SerializeDocument(changedDocument);

            Assert.Equal(
                overriddenRadius,
                changedDocument.Configurations[alternateIndex].SurfaceGroup.Items[2].Radius,
                12);
            Assert.Contains(
                changedDocument.BrokenLinks ?? Array.Empty<MultiConfigurationLinkOverride>(),
                link => link.ConfigurationIndex == alternateIndex
                    && link.SurfaceNumber == 2
                    && link.Property == "radius");

            Assert.True(runtime.Undo());
            Assert.Equal(before, SerializeDocument(runtime.CaptureDocument()));
            await runtime.SaveAsync(path);
            var reopened = await WorkbenchRuntime.ReadDocumentAsync(path);
            Assert.Equal(before, SerializeDocument(reopened));
            Assert.True(runtime.Redo());
            Assert.Equal(changed, SerializeDocument(runtime.CaptureDocument()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MaterialPropagationCopiesTheOpticalMaterialNotOnlyItsDisplayName()
    {
        var multiConfiguration = new MultiConfiguration(Optic.CreateDemo());
        var alternateIndex = multiConfiguration.AddConfiguration();
        var source = multiConfiguration.Configurations[0].SurfaceGroup.Items[2];
        source.MaterialAfter = new ConstantIndexMaterial("TEST-GLASS", 1.732);
        source.Material = "TEST-GLASS";

        multiConfiguration.PropagateBaseLinks();

        var target = multiConfiguration.Configurations[alternateIndex].SurfaceGroup.Items[2];
        Assert.Equal("TEST-GLASS", target.Material);
        Assert.Equal("TEST-GLASS", target.MaterialAfter.Name);
        Assert.Equal(1.732, target.MaterialAfter.RefractiveIndex(587.6), 12);
    }

    [Fact]
    public void PrescriptionEditsInAlternateConfigurationRemainOverridden()
    {
        var runtime = new WorkbenchRuntime(Optic.CreateDemo());
        var alternateIndex = runtime.AddMultiConfiguration();
        runtime.ActivateMultiConfiguration(alternateIndex);
        var surface = runtime.Surfaces[2];
        var customRadius = surface.Radius + 7.5;
        surface.Radius = customRadius;
        runtime.CommitSurfaceEdit(surface, nameof(surface.Radius));
        surface.Material = "Air";
        runtime.CommitSurfaceEdit(surface, nameof(surface.Material));

        runtime.ActivateMultiConfiguration(0);
        runtime.SetMultiConfigurationThickness(0, 2, runtime.Surfaces[2].Thickness + 1);
        runtime.ActivateMultiConfiguration(alternateIndex);

        Assert.Equal(customRadius, runtime.Surfaces[2].Radius, 12);
        Assert.Equal("Air", runtime.Surfaces[2].Material);
        Assert.Equal(1, runtime.Surfaces[2].MaterialAfter.RefractiveIndex(587.6), 12);
    }

    [Fact]
    public void LegacyConfigurationDifferencesAndClonedOverridesArePreserved()
    {
        var baseOptic = Optic.CreateDemo();
        var alternate = Optic.FromSnapshot(baseOptic.ToSnapshot());
        alternate.SurfaceGroup.Items[2].Thickness = 19.75;
        var multiConfiguration = new MultiConfiguration(new[] { baseOptic, alternate });
        var clonedIndex = multiConfiguration.AddConfiguration(1);

        multiConfiguration.SetThickness(0, 2, 8.25);
        multiConfiguration.PropagateBaseLinks();

        Assert.Equal(19.75, multiConfiguration.Configurations[1].SurfaceGroup.Items[2].Thickness, 12);
        Assert.Equal(19.75, multiConfiguration.Configurations[clonedIndex].SurfaceGroup.Items[2].Thickness, 12);
        Assert.Contains(
            multiConfiguration.BrokenLinks,
            link => link.ConfigurationIndex == clonedIndex
                && link.SurfaceNumber == 2
                && link.Property == "thickness");
    }

    [Fact]
    public void FailedPrescriptionEditRollsBackCurrentAndLinkedConfigurations()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var alternateIndex = application.MultiConfiguration.Add();
        application.MultiConfiguration.Activate(alternateIndex);
        var alternateBefore = application.Prescription.GetSurfaces()[2];
        application.MultiConfiguration.Activate(0);
        var originalBase = application.Prescription.GetSurfaces()[2];
        application.Prescription.UpdateSurface(originalBase with { Label = "Undo checkpoint" });
        Assert.True(application.Documents.Undo());
        var baseBefore = application.Prescription.GetSurfaces()[2];
        var snapshotBefore = application.Documents.GetSnapshot();
        Assert.True(snapshotBefore.CanRedo);
        var changeCount = 0;
        application.Events.Changed += (_, _) => changeCount++;

        Assert.Throws<KeyNotFoundException>(() => application.Prescription.UpdateSurface(baseBefore with
        {
            Radius = baseBefore.Radius + 5,
            Thickness = baseBefore.Thickness + 2,
            Material = "MISSING-TRANSACTION-GLASS"
        }));

        Assert.Equal(baseBefore, application.Prescription.GetSurfaces()[2]);
        var snapshotAfter = application.Documents.GetSnapshot();
        Assert.Equal(snapshotBefore.Revision, snapshotAfter.Revision);
        Assert.Equal(snapshotBefore.Status, snapshotAfter.Status);
        Assert.Equal(snapshotBefore.CanUndo, snapshotAfter.CanUndo);
        Assert.Equal(snapshotBefore.CanRedo, snapshotAfter.CanRedo);
        Assert.Equal(0, changeCount);

        application.MultiConfiguration.Activate(alternateIndex);
        Assert.Equal(alternateBefore, application.Prescription.GetSurfaces()[2]);
    }

    [Fact]
    public void FailedAutomaticSemiDiameterRefreshRollsBackPrescriptionAndLeavesWorkspaceUsable()
    {
        var failNextRefresh = true;
        using var workspace = new WorkspaceCoordinator(
            new OpticContext(Optic.CreateDemo()),
            optic =>
            {
                if (failNextRefresh)
                {
                    failNextRefresh = false;
                    throw new InvalidOperationException("Injected aperture refresh failure.");
                }

                AutomaticSemiDiameterSolver.Update(optic);
            });
        var prescription = new PrescriptionService(workspace);
        var initialFieldCount = prescription.GetFields().Count;
        var initialRevision = workspace.Revision;
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        Assert.Throws<InvalidOperationException>(prescription.AddField);

        Assert.Equal(initialFieldCount, prescription.GetFields().Count);
        Assert.Equal(initialRevision, workspace.Revision);
        Assert.False(workspace.Runtime.CanUndo);
        Assert.Equal(0, changedCount);

        prescription.AddField();

        Assert.Equal(initialFieldCount + 1, prescription.GetFields().Count);
        Assert.Equal(initialRevision + 1, workspace.Revision);
        Assert.True(workspace.Runtime.CanUndo);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public async Task OptimizationRunPublishesOnlyAfterAutomaticSemiDiametersAreCurrent()
    {
        const double refreshedSemiDiameter = 123.456;
        using var workspace = new WorkspaceCoordinator(
            new OpticContext(Optic.CreateDemo()),
            optic => optic.SurfaceGroup.Items[1].SemiDiameter = refreshedSemiDiameter);
        var optimization = new OptimizationService(workspace);
        double semiDiameterObservedBySubscriber = 0;
        WorkspaceChangeCategory? observedCategory = null;
        workspace.Changed += (_, args) =>
        {
            semiDiameterObservedBySubscriber = workspace.Runtime.Surfaces[1].SemiDiameter;
            observedCategory = args.Category;
        };

        await optimization.OptimizeSurfaceRadiusAsync(1, "Least Squares", 1);

        Assert.Equal(refreshedSemiDiameter, semiDiameterObservedBySubscriber, 12);
        Assert.Equal(WorkspaceChangeCategory.Optimization, observedCategory);
    }

    [Fact]
    public async Task FailedOptimizationRunApertureRefreshRollsBackDocumentAndHistory()
    {
        using var workspace = new WorkspaceCoordinator(
            new OpticContext(Optic.CreateDemo()),
            _ => throw new OperationCanceledException("Injected aperture refresh cancellation."));
        var optimization = new OptimizationService(workspace);
        var before = SerializeDocument(workspace.Runtime.CaptureDocument());
        var initialRevision = workspace.Revision;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            optimization.OptimizeSurfaceRadiusAsync(1, "Least Squares", 1));

        Assert.Equal(before, SerializeDocument(workspace.Runtime.CaptureDocument()));
        Assert.Equal(initialRevision, workspace.Revision);
        Assert.False(workspace.Runtime.CanUndo);
    }

    [Fact]
    public async Task DocumentCancellationDoesNotWaitForTheOpticModelLock()
    {
        using var context = new OpticContext(Optic.CreateDemo());
        using var linked = context.LinkDocumentToken(CancellationToken.None);
        using var lockHeld = new ManualResetEventSlim();
        using var releaseLock = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            lock (context.SyncRoot)
            {
                lockHeld.Set();
                releaseLock.Wait();
            }
        });

        Assert.True(lockHeld.Wait(TimeSpan.FromSeconds(2)));
        try
        {
            await Task.Run(context.CancelDocumentTasks).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(linked.IsCancellationRequested);
        }
        finally
        {
            releaseLock.Set();
            await holder;
        }
    }

    [Fact]
    public void CancelledOptimizationRestoresTheCompleteInitialState()
    {
        var runtime = new WorkbenchRuntime(Optic.CreateDemo());
        var surface = runtime.Surfaces.First(item => item.IsPlane);
        var initial = runtime.CurrentOptic.ToSnapshot();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);

        Assert.Throws<OperationCanceledException>(() =>
            runtime.OptimizeSurfaceRadius(surface, "Least Squares", 50));

        Assert.Equal(
            JsonSerializer.Serialize(initial),
            JsonSerializer.Serialize(runtime.CurrentOptic.ToSnapshot()));
        Assert.False(runtime.CanUndo);
    }

    private static string SerializeDocument(LoadedOpticalDocument document)
    {
        return JsonSerializer.Serialize(new
        {
            document.ActiveConfigurationIndex,
            Configurations = document.Configurations.Select(configuration => configuration.ToSnapshot()),
            BrokenLinks = document.BrokenLinks ?? Array.Empty<MultiConfigurationLinkOverride>()
        });
    }
}
