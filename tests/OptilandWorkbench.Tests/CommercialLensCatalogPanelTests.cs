using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Panels;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CommercialLensCatalogPanelTests
{
    [Fact]
    public async Task SelectingVendorImmediatelyFiltersVisibleCatalogRows()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var service = new StubLensLibraryService(new[]
            {
                Entry("thorlabs", "Thorlabs", "AC254-100-A"),
                Entry("newport", "Newport", "KPX100")
            });
            var panel = new CommercialLensCatalogPanel(service);
            var vendor = PrivateField<ComboBox>(panel, "_vendor");
            var results = PrivateField<DataGrid>(panel, "_results");

            Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<object>>(results.ItemsSource).Count());

            vendor.SelectedItem = "Newport";

            var row = Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(results.ItemsSource));
            var manufacturer = row.GetType().GetProperty("Manufacturer")?.GetValue(row) as string;
            Assert.Equal("Newport", manufacturer);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task StockLensMatchingPageBuildsRankedRowsFromCurrentFirstOrderTarget()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var service = new StubLensLibraryService(new[]
            {
                Entry("thorlabs", "Thorlabs", "AC254-100-A"),
                Entry("newport", "Newport", "KPX100")
            });
            var panel = new StockLensMatchingPanel(new StubDocuments(), service, new StubEvents());
            var results = PrivateField<DataGrid>(panel, "_results");

            Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<object>>(results.ItemsSource).Count());
            panel.Dispose();
        }, CancellationToken.None);
    }

    private static CommercialLensEntryDto Entry(string id, string manufacturer, string partNumber) => new(
        id,
        manufacturer,
        partNumber,
        partNumber,
        "本机 Zemax Stockcat",
        "https://example.com",
        string.Empty,
        "目录镜头",
        "B",
        "S",
        2,
        25,
        10,
        9,
        20,
        0.1,
        486,
        656,
        0,
        0,
        "仅目录",
        null,
        "测试目录头",
        new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero),
        8);

    private static T PrivateField<T>(object instance, string name) where T : class =>
        Assert.IsType<T>(instance.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance));

    private sealed class StubLensLibraryService(IReadOnlyList<CommercialLensEntryDto> entries) : ILensLibraryService
    {
        public string LibraryDirectory => string.Empty;

        public IReadOnlyList<LensLibraryEntryDto> GetLenses() => Array.Empty<LensLibraryEntryDto>();

        public IReadOnlyList<CommercialLensEntryDto> GetCommercialLenses() => entries;

        public string? GetNativeProjectPath(string lensId) => null;

        public string? GetCommercialNativeProjectPath(string lensId) => null;

        public Task<SceneDto?> BuildPreviewAsync(
            string lensId,
            CancellationToken cancellationToken = default) => Task.FromResult<SceneDto?>(null);

        public Task<SceneDto?> BuildCommercialPreviewAsync(
            string lensId,
            CancellationToken cancellationToken = default) => Task.FromResult<SceneDto?>(null);
    }

    private sealed class StubDocuments : IOpticalDocumentService
    {
        public string? CurrentPath => null;

        public OpticalDocumentSnapshot GetSnapshot() => new(
            "测试系统",
            null,
            0,
            "就绪",
            false,
            false,
            25,
            1,
            25,
            10,
            4,
            1,
            1,
            8);

        public void NewBlank()
        {
        }

        public void NewCooke()
        {
        }

        public void NewTessar()
        {
        }

        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public bool Undo() => false;

        public bool Redo() => false;
    }

    private sealed class StubEvents : IWorkspaceEventStream
    {
        public event EventHandler<WorkspaceChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public event EventHandler? StatusChanged
        {
            add { }
            remove { }
        }

        public long Revision => 0;
    }
}
