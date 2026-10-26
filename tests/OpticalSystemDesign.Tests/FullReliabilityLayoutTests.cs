using System.Reflection;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class FullReliabilityLayoutTests
{
    [Fact]
    public async Task FailedRestoreKeepsTheExistingLayoutDescriptorsAndContent()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "restore-rollback-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using var app = WorkbenchApplication.Create("blank");
                using var manager = new PanelManager(app, new AppSettings(), new WorkspaceSessionStore(root));
                var previous = manager.Layout;
                var descriptors = manager.Factory.SnapshotDescriptors();
                var contents = manager.Factory.OpenDocuments().Select(d => d.Context).ToArray();
                var broken = new WorkspaceSession(WorkspaceSessionStore.CurrentVersion, "{broken",
                    descriptors.Select(d => d with { Title = "changed" }).ToArray(), null);
                var result = typeof(PanelManager).GetMethod("TryRestore", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, [broken]);
                Assert.Equal(false, result);
                Assert.Same(previous, manager.Layout);
                Assert.Equal(descriptors, manager.Factory.SnapshotDescriptors());
                Assert.Equal(contents, manager.Factory.OpenDocuments().Select(d => d.Context));
                var rollback = manager.Factory.BeginContentReplacement();
                rollback(false);
                Assert.Equal(contents, manager.Factory.OpenDocuments().Select(d => d.Context));
            }
            finally { Directory.Delete(root, true); }
        }, CancellationToken.None);
    }
}
