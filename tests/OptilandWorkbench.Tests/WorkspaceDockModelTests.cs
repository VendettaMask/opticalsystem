using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm.Core;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using System.Collections.ObjectModel;
using System.Reflection;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class WorkspaceDockModelTests
{
    [Fact]
    public void NonSequentialModeCreatesIndependentObjectWorkspace()
    {
        using var application = WorkbenchApplication.Create("cooke");
        application.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
        var factory = new WorkspaceDockFactory(application, new AppSettings());

        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        var primary = Assert.Single(factory.OpenDocuments());
        Assert.Equal(WorkspaceDockFactory.NonSequentialObjectDocumentId, primary.Id);
        Assert.IsType<NonSequentialObjectEditorPanel>(primary.Context);
        var systemTool = WorkspaceDockFactory.EnumerateDockables(layout)
            .Single(dockable => dockable.Id == WorkspaceDockFactory.SystemToolId);
        Assert.Equal("非序列设置", systemTool.Title);
        Assert.IsType<NonSequentialModePanel>(systemTool.Context);
        factory.DisposeContent();
    }

    [Fact]
    public async Task LensLibraryOpenUsesHostUnsavedChangesWorkflowAndHonorsCancellation()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var requestedPath = string.Empty;
        var factory = new WorkspaceDockFactory(
            application,
            new AppSettings(),
            path =>
            {
                requestedPath = path;
                return Task.FromResult(false);
            });
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);
        var documentsBefore = factory.OpenDocuments().Count;

        await factory.OpenLensLibraryProjectAsync("blocked.staropt");

        Assert.Equal("blocked.staropt", requestedPath);
        Assert.Equal(documentsBefore, factory.OpenDocuments().Count);
        factory.DisposeContent();
    }

    [Fact]
    public void ClosingDirtyTolerancingDocumentPreservesItForTheGlobalSaveGuard()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var factory = new WorkspaceDockFactory(application, new AppSettings());
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);
        var descriptor = new WorkspaceDocumentDescriptor(
            "document:tolerancing",
            WorkspaceDocumentTypes.Tolerancing,
            "公差数据编辑器");
        var document = factory.OpenDocument(descriptor);
        var panel = Assert.IsType<TolerancingPanel>(document.Context);
        var operands = Assert.IsType<ObservableCollection<ToleranceOperandEditorRow>>(
            typeof(TolerancingPanel)
                .GetField("_operands", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(panel));
        operands[0].Comment = "关闭面板后仍需保存";

        factory.CloseDockable(document);

        Assert.True(factory.HasUnsavedToleranceChanges);
        var reopened = factory.OpenDocument(descriptor);
        Assert.Same(panel, reopened.Context);
        factory.DisposeContent();
    }

    [Theory]
    [InlineData(1, 1600, 900, 1, 1)]
    [InlineData(2, 1600, 900, 1, 2)]
    [InlineData(3, 1600, 900, 2, 2)]
    [InlineData(4, 1600, 900, 2, 2)]
    [InlineData(5, 1600, 900, 2, 3)]
    [InlineData(6, 1600, 900, 2, 3)]
    [InlineData(8, 1600, 900, 3, 3)]
    public void AdaptiveTilePlansBalancedGrid(
        int documentCount,
        double width,
        double height,
        int expectedRows,
        int expectedColumns)
    {
        var plan = AdaptiveMdiLayout.Plan(documentCount, width, height);

        Assert.Equal(expectedRows, plan.Rows);
        Assert.Equal(expectedColumns, plan.Columns);
    }

    [Fact]
    public void AdaptiveTileAppliesEqualNormalAspectBoundsAndCentersLastRow()
    {
        var documents = Enumerable.Range(1, 5)
            .Select(index => new Document { Id = $"document:{index}" })
            .ToArray();
        var dock = new DocumentDock
        {
            VisibleDockables = documents.Cast<IDockable>().ToList()
        };
        dock.SetVisibleBounds(0, 0, 1600, 900);

        AdaptiveMdiLayout.TileDocuments(dock);

        Assert.All(documents, document =>
        {
            Assert.Equal(MdiWindowState.Normal, document.MdiState);
            Assert.Equal(1600.0 / 3, document.MdiBounds.Width, 6);
            Assert.Equal(450, document.MdiBounds.Height, 6);
            Assert.InRange(
                document.MdiBounds.Width / document.MdiBounds.Height,
                1.1,
                1.3);
        });
        Assert.Equal(0, documents[0].MdiBounds.X, 6);
        Assert.Equal(1600.0 / 6, documents[3].MdiBounds.X, 6);
        Assert.Equal(1600.0 / 2, documents[4].MdiBounds.X, 6);
    }

    [Fact]
    public void TileAndCascadeReturnAllDocumentsToPrimaryMdiArea()
    {
        using var application = WorkbenchApplication.Create("cooke");
        using var manager = new PanelManager(application, new AppSettings());
        UseTestHostWindows(manager.Factory);
        manager.Factory.OpenDocument(new WorkspaceDocumentDescriptor(
            "document:viewer-2d",
            WorkspaceDocumentTypes.Viewer2D,
            "二维视图"));
        manager.Factory.OpenDocument(new WorkspaceDocumentDescriptor(
            "analysis:spot",
            WorkspaceDocumentTypes.Analysis,
            "Spot Diagram",
            "Spot Diagram"));

        manager.FloatAllWindows();
        Assert.NotEmpty(manager.Layout.Windows!);
        manager.TileAllWindows();

        var target = Assert.IsAssignableFrom<IDocumentDock>(manager.Factory.PrimaryDocumentDock);
        Assert.Empty(manager.Layout.Windows!);
        Assert.Equal(DocumentLayoutMode.Mdi, target.LayoutMode);
        Assert.Equal(3, manager.Factory.OpenDocuments().Count);
        Assert.All(manager.Factory.OpenDocuments(), document => Assert.Same(target, document.Owner));

        manager.FloatAllWindows();
        Assert.NotEmpty(manager.Layout.Windows!);
        manager.CascadeAllWindows();

        Assert.Empty(manager.Layout.Windows!);
        Assert.Equal(DocumentLayoutMode.Mdi, target.LayoutMode);
        Assert.All(manager.Factory.OpenDocuments(), document => Assert.Same(target, document.Owner));
    }

    [Fact]
    public async Task MergeSinglePaneReturnsAllDocumentsAndUsesTabbedLayout()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(OptilandWorkbench.App.App));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create("cooke");
            using var manager = new PanelManager(application, new AppSettings());
            var window = new Window
            {
                Width = 1200,
                Height = 800,
                Content = manager.WorkspaceControl
            };

            try
            {
                window.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                manager.Factory.OpenDocument(new WorkspaceDocumentDescriptor(
                    "document:viewer-2d",
                    WorkspaceDocumentTypes.Viewer2D,
                    "二维视图"));
                manager.FloatAllWindows();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.NotEmpty(manager.Layout.Windows!);

                manager.DockToSinglePane();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                var target = Assert.IsAssignableFrom<IDocumentDock>(manager.Factory.PrimaryDocumentDock);
                Assert.Empty(manager.Layout.Windows!);
                Assert.Equal(DocumentLayoutMode.Tabbed, target.LayoutMode);
                Assert.Equal(2, manager.Factory.OpenDocuments().Count);
                Assert.All(manager.Factory.OpenDocuments(), document => Assert.Same(target, document.Owner));
                Assert.DoesNotContain(
                    window.GetVisualDescendants(),
                    visual => visual is MdiDocumentControl { IsVisible: true });
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void BulkWindowCommandsRemoveEmptyFloatingHosts()
    {
        using var application = WorkbenchApplication.Create("cooke");
        using var manager = new PanelManager(application, new AppSettings());
        UseTestHostWindows(manager.Factory);
        var mainDocumentDock = Assert.IsAssignableFrom<IDocumentDock>(manager.Factory.PrimaryDocumentDock);
        var floatingDocument = new Document
        {
            Id = "document:floating-restored",
            Title = "恢复的浮动页面"
        };
        var floatingDocumentDock = new DocumentDock
        {
            Id = WorkspaceDockFactory.DocumentDockId,
            VisibleDockables = manager.Factory.CreateList<IDockable>(floatingDocument),
            ActiveDockable = floatingDocument
        };
        floatingDocument.Owner = floatingDocumentDock;
        var floatingRoot = new RootDock
        {
            VisibleDockables = manager.Factory.CreateList<IDockable>(floatingDocumentDock),
            ActiveDockable = floatingDocumentDock
        };
        manager.Layout.Windows = manager.Factory.CreateList<IDockWindow>(
            new DockWindow(),
            new DockWindow
            {
                Layout = new RootDock
                {
                    VisibleDockables = manager.Factory.CreateList<IDockable>(new DocumentDock())
                }
            },
            new DockWindow { Layout = floatingRoot });

        manager.Factory.InitLayout(manager.Layout);

        Assert.Same(mainDocumentDock, manager.Factory.PrimaryDocumentDock);

        manager.DockAllWindows();

        Assert.Empty(manager.Layout.Windows);
        Assert.Same(mainDocumentDock, floatingDocument.Owner);
    }

    [Fact]
    public void DefaultLayoutContainsToolDockAndOnlyLensDocument()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var factory = new WorkspaceDockFactory(application, new AppSettings());
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        var documents = factory.OpenDocuments();

        var document = Assert.Single(documents);
        Assert.Equal(WorkspaceDockFactory.LensDocumentId, document.Id);
        var viewLocator = new WorkspaceViewLocator();
        var firstTemplateRoot = viewLocator.Build(document);
        var secondTemplateRoot = viewLocator.Build(document);
        Assert.NotSame(document.Context, firstTemplateRoot);
        Assert.NotSame(firstTemplateRoot, secondTemplateRoot);
        Assert.Contains(
            WorkspaceDockFactory.EnumerateDockables(layout),
            dockable => dockable.Id == WorkspaceDockFactory.ToolDockId && dockable is IToolDock);
        factory.DisposeContent();
    }

    [Fact]
    public void ReopeningStableAnalysisFocusesExistingDocumentAndCloneIsIndependent()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var factory = new WorkspaceDockFactory(application, new AppSettings());
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);
        var stable = new WorkspaceDocumentDescriptor(
            "analysis:spot-diagram",
            WorkspaceDocumentTypes.Analysis,
            "点列图",
            "Spot Diagram",
            Guid.NewGuid());

        var first = factory.OpenDocument(stable);
        var reopened = factory.OpenDocument(stable);
        var cloneId = Guid.NewGuid();
        var clone = factory.OpenDocument(stable with
        {
            Id = $"analysis:spot-diagram:{cloneId:N}",
            InstanceId = cloneId,
            Title = "点列图（副本）"
        });

        Assert.Same(first, reopened);
        Assert.NotSame(first, clone);
        Assert.Equal(3, factory.OpenDocuments().Count);
        factory.DisposeContent();
    }

    [Fact]
    public void NonSequentialDetectorViewerIsAStableWorkspaceDocument()
    {
        using var application = WorkbenchApplication.Create("cooke");
        application.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
        using var manager = new PanelManager(application, new AppSettings());

        manager.ShowNonSequentialDetectorViewer();
        manager.ShowNonSequentialDetectorViewer();

        var document = Assert.Single(
            manager.Factory.OpenDocuments(),
            item => item.Id == WorkspaceDockFactory.NonSequentialDetectorViewerDocumentId);
        Assert.Equal("探测器查看器", document.Title);
        Assert.IsType<NonSequentialDetectorViewerPanel>(document.Context);
        Assert.IsAssignableFrom<IDocumentDock>(document.Owner);
        Assert.True(WorkspaceDocumentTypes.IsKnown(WorkspaceDocumentTypes.NonSequentialDetectorViewer));
    }

    [Fact]
    public void ManufacturingDocumentsCreateTheirExpectedPanels()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var factory = new WorkspaceDockFactory(application, new AppSettings());
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        var manufacturability = factory.OpenDocument(new WorkspaceDocumentDescriptor(
            "document:manufacturability",
            WorkspaceDocumentTypes.Manufacturability,
            "可加工性评估"));
        var drawing = factory.OpenDocument(new WorkspaceDocumentDescriptor(
            "document:optical-drawing",
            WorkspaceDocumentTypes.OpticalDrawing,
            "光学制图"));

        Assert.IsType<ManufacturabilityPanel>(manufacturability.Context);
        Assert.IsType<OpticalDrawingPanel>(drawing.Context);
        factory.DisposeContent();
    }

    [Fact]
    public void DockLayoutSerializesAndRestoresStableDocumentIds()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var settings = new AppSettings();
        var factory = new WorkspaceDockFactory(application, settings);
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);
        var viewer = factory.OpenDocument(new WorkspaceDocumentDescriptor(
            "document:viewer-2d",
            WorkspaceDocumentTypes.Viewer2D,
            "二维视图"));
        var previousOwner = Assert.IsAssignableFrom<IDock>(viewer.Owner);
        Assert.True(previousOwner.VisibleDockables!.Remove(viewer));
        viewer.Owner = layout;
        layout.HiddenDockables = factory.CreateList<IDockable>(viewer);
        var serializer = new WorkspaceDockLayoutSerializer();

        var json = serializer.Serialize(layout);
        var restored = serializer.Deserialize(json);

        Assert.NotNull(restored);
        factory.RegisterDescriptors(new[]
        {
            new WorkspaceDocumentDescriptor(
                WorkspaceDockFactory.LensDocumentId,
                WorkspaceDocumentTypes.LensEditor,
                "镜头数据"),
            new WorkspaceDocumentDescriptor(
                "document:viewer-2d",
                WorkspaceDocumentTypes.Viewer2D,
                "二维视图")
        });
        factory.InitLayout(restored!);
        Assert.Contains(restored!.HiddenDockables!, document => document.Id == "document:viewer-2d");
        Assert.Contains(factory.OpenDocuments(), document => document.Id == "document:viewer-2d");
        Assert.NotNull(factory.OpenDocuments().Single(document => document.Id == "document:viewer-2d").Context);
        factory.DisposeContent();
    }

    [Fact]
    public void DockLayoutRoundTripsFloatingWindowWithoutDuplicateDocuments()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var factory = new WorkspaceDockFactory(application, new AppSettings());
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);
        var viewer = factory.OpenDocument(new WorkspaceDocumentDescriptor(
            "document:viewer-3d",
            WorkspaceDocumentTypes.Viewer3D,
            "三维视图"));
        var previousOwner = Assert.IsAssignableFrom<IDock>(viewer.Owner);
        Assert.True(previousOwner.VisibleDockables!.Remove(viewer));

        var floatingDock = new DocumentDock
        {
            Id = "floating:documents",
            VisibleDockables = factory.CreateList<IDockable>(viewer),
            ActiveDockable = viewer
        };
        viewer.Owner = floatingDock;
        var floatingRoot = new RootDock
        {
            Id = "floating:root",
            VisibleDockables = factory.CreateList<IDockable>(floatingDock),
            ActiveDockable = floatingDock
        };
        var window = new DockWindow
        {
            X = 120,
            Y = 80,
            Width = 900,
            Height = 640,
            Layout = floatingRoot
        };
        floatingRoot.Window = window;
        layout.Windows = factory.CreateList<IDockWindow>(window);
        var serializer = new WorkspaceDockLayoutSerializer();

        var restored = serializer.Deserialize(serializer.Serialize(layout));

        Assert.NotNull(restored);
        var restoredWindow = Assert.Single(restored!.Windows!);
        Assert.Equal(120, restoredWindow.X);
        Assert.Equal(80, restoredWindow.Y);
        Assert.Equal(900, restoredWindow.Width);
        Assert.Equal(640, restoredWindow.Height);
        Assert.Same(restoredWindow, restoredWindow.Layout!.Window);
        Assert.Single(
            WorkspaceDockFactory.EnumerateDockables(restored),
            document => document.Id == "document:viewer-3d");
        factory.DisposeContent();
    }

    [Fact]
    public void DockLayoutSerializationFiltersEmptyFloatingHosts()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var factory = new WorkspaceDockFactory(application, new AppSettings());
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);
        layout.Windows = factory.CreateList<IDockWindow>(
            new DockWindow(),
            new DockWindow
            {
                Layout = new RootDock
                {
                    VisibleDockables = factory.CreateList<IDockable>(new DocumentDock())
                }
            });
        var serializer = new WorkspaceDockLayoutSerializer();

        var restored = serializer.Deserialize(serializer.Serialize(layout));

        Assert.NotNull(restored);
        Assert.Empty(restored!.Windows!);
        factory.DisposeContent();
    }

    [Fact]
    public void DockLayoutDeserializationFiltersLegacyEmptyFloatingHosts()
    {
        var layout = new RootDock
        {
            Windows = new List<IDockWindow>
            {
                new DockWindow
                {
                    Layout = new RootDock
                    {
                        VisibleDockables = new List<IDockable> { new DocumentDock() }
                    }
                }
            }
        };
        var rawSerializer = new Dock.Serializer.SystemTextJson.DockSerializer();
        var serializer = new WorkspaceDockLayoutSerializer();

        var restored = serializer.Deserialize(rawSerializer.Serialize(layout));

        Assert.NotNull(restored);
        Assert.Empty(restored!.Windows!);
    }

    [Fact]
    public void DockLayoutSerializationPrunesRepeatedAndCyclicStructuralReferences()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var factory = new WorkspaceDockFactory(application, new AppSettings());
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);
        var toolDock = Assert.IsType<ToolDock>(
            WorkspaceDockFactory.EnumerateDockables(layout)
                .Single(dockable => dockable.Id == WorkspaceDockFactory.ToolDockId));
        var lens = factory.OpenDocuments().Single();
        var documentDock = Assert.IsAssignableFrom<IDock>(lens.Owner);
        toolDock.VisibleDockables!.Add(toolDock);
        documentDock.VisibleDockables!.Add(lens);
        var serializer = new WorkspaceDockLayoutSerializer();

        var restored = serializer.Deserialize(serializer.Serialize(layout));

        Assert.NotNull(restored);
        Assert.Single(
            WorkspaceDockFactory.EnumerateDockables(restored),
            dockable => dockable.Id == WorkspaceDockFactory.ToolDockId);
        Assert.Single(
            WorkspaceDockFactory.EnumerateDockables(restored),
            dockable => dockable.Id == WorkspaceDockFactory.LensDocumentId);
        var restoredToolDock = Assert.IsType<ToolDock>(
            WorkspaceDockFactory.EnumerateDockables(restored)
                .Single(dockable => dockable.Id == WorkspaceDockFactory.ToolDockId));
        Assert.DoesNotContain(restoredToolDock, restoredToolDock.VisibleDockables!);
        factory.DisposeContent();
    }

    [Fact]
    public void ActiveDocumentLockCommandTogglesOneState()
    {
        using var application = WorkbenchApplication.Create("cooke");
        using var manager = new PanelManager(application, new AppSettings());
        var viewer = manager.Factory.OpenDocument(new WorkspaceDocumentDescriptor(
            "document:viewer-2d",
            WorkspaceDocumentTypes.Viewer2D,
            "二维视图"));
        manager.Factory.SetActiveDockable(viewer);

        manager.ToggleActiveDocumentLocked();
        Assert.True(manager.Factory.Descriptor(viewer.Id)!.IsLocked);

        manager.ToggleActiveDocumentLocked();
        Assert.False(manager.Factory.Descriptor(viewer.Id)!.IsLocked);
    }

    private static void UseTestHostWindows(WorkspaceDockFactory factory)
    {
        factory.HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new TestHostWindow()
        };
    }

    private sealed class TestHostWindow : IHostWindow
    {
        public IHostWindowState HostWindowState => null!;

        public bool IsTracked { get; set; }

        public IDockWindow? Window { get; set; }

        public void Present(bool isDialog)
        {
        }

        public void Exit()
        {
        }

        public void SetPosition(double x, double y)
        {
        }

        public void GetPosition(out double x, out double y)
        {
            x = 0;
            y = 0;
        }

        public void SetSize(double width, double height)
        {
        }

        public void GetSize(out double width, out double height)
        {
            width = 0;
            height = 0;
        }

        public void SetWindowState(DockWindowState windowState)
        {
        }

        public DockWindowState GetWindowState() => DockWindowState.Normal;

        public void SetTitle(string? title)
        {
        }

        public void SetLayout(IDock layout)
        {
        }

        public void SetActive()
        {
        }
    }

}
