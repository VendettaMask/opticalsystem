using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm.Core;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.Tests;

public sealed class WorkspaceDockModelTests
{
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
            WorkspaceDocumentKind.Analysis,
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
    public void ManufacturingDocumentsCreateTheirExpectedPanels()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var factory = new WorkspaceDockFactory(application, new AppSettings());
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        var manufacturability = factory.OpenDocument(new WorkspaceDocumentDescriptor(
            "document:manufacturability",
            WorkspaceDocumentKind.Manufacturability,
            "可加工性评估"));
        var drawing = factory.OpenDocument(new WorkspaceDocumentDescriptor(
            "document:optical-drawing",
            WorkspaceDocumentKind.OpticalDrawing,
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
            WorkspaceDocumentKind.Viewer2D,
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
                WorkspaceDocumentKind.LensEditor,
                "镜头数据"),
            new WorkspaceDocumentDescriptor(
                "document:viewer-2d",
                WorkspaceDocumentKind.Viewer2D,
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
            WorkspaceDocumentKind.Viewer3D,
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
}
