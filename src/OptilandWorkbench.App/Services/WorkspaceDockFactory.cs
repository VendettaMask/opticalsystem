using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Panels;
using Orientation = Dock.Model.Core.Orientation;

namespace OptilandWorkbench.App.Services;

public sealed class WorkspaceDockFactory : Factory
{
    public const string RootId = "workspace:root";
    public const string MainLayoutId = "workspace:main";
    public const string ToolDockId = "workspace:tools";
    public const string SystemToolId = "tool:system-options";
    public const string DocumentDockId = "workspace:documents";
    public const string LensDocumentId = "document:lens-editor";

    private readonly IWorkbenchApplication _application;
    private readonly AppSettings _settings;
    private readonly Dictionary<string, WorkspaceDocumentDescriptor> _descriptors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _content = new(StringComparer.Ordinal);

    public WorkspaceDockFactory(IWorkbenchApplication application, AppSettings settings)
    {
        _application = application;
        _settings = settings;
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow()
        };
    }

    public event EventHandler? LayoutChanged;

    public IRootDock? RootLayout { get; private set; }

    public IDocumentDock? PrimaryDocumentDock { get; private set; }

    public IReadOnlyCollection<WorkspaceDocumentDescriptor> Descriptors => _descriptors.Values;

    public override IDocumentDock CreateDocumentDock()
    {
        return new DocumentDock
        {
            IsCollapsable = false,
            CanCreateDocument = false,
            EnableWindowDrag = true,
            CloseButtonShowMode = DocumentCloseButtonShowMode.Always
        };
    }

    public override IRootDock CreateLayout()
    {
        _descriptors.Clear();
        DisposeContent();
        var lensDescriptor = new WorkspaceDocumentDescriptor(
            LensDocumentId,
            WorkspaceDocumentKind.LensEditor,
            "镜头数据");
        _descriptors[lensDescriptor.Id] = lensDescriptor;
        var lens = CreateDocument(lensDescriptor);
        var documentDock = (DocumentDock)CreateDocumentDock();
        documentDock.Id = DocumentDockId;
        documentDock.Title = "文档";
        documentDock.VisibleDockables = CreateList<IDockable>(lens);
        documentDock.ActiveDockable = lens;

        var systemContent = CreateSystemToolContent();
        _content[SystemToolId] = systemContent;
        var systemTool = new Tool
        {
            Id = SystemToolId,
            Title = "系统选项",
            CanClose = false,
            CanFloat = true,
            CanPin = true,
            Context = systemContent
        };
        var initialWidth = _settings.LeftPaneWidth > 0
            ? Math.Clamp(_settings.LeftPaneWidth, 230, 420)
            : 286;
        var toolDock = new ToolDock
        {
            Id = ToolDockId,
            Title = "系统选项",
            Alignment = Alignment.Left,
            Proportion = Math.Clamp(initialWidth / 1440.0, 0.16, 0.34),
            GripMode = GripMode.Visible,
            AutoHide = false,
            IsCollapsable = true,
            VisibleDockables = CreateList<IDockable>(systemTool),
            ActiveDockable = systemTool
        };
        var mainLayout = new ProportionalDock
        {
            Id = MainLayoutId,
            Title = "工作区",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                toolDock,
                new ProportionalDockSplitter(),
                documentDock),
            ActiveDockable = documentDock
        };
        var root = new RootDock
        {
            Id = RootId,
            Title = "Optiland Workbench",
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(mainLayout),
            DefaultDockable = mainLayout,
            ActiveDockable = mainLayout,
            LeftPinnedDockables = CreateList<IDockable>(),
            RightPinnedDockables = CreateList<IDockable>(),
            TopPinnedDockables = CreateList<IDockable>(),
            BottomPinnedDockables = CreateList<IDockable>()
        };
        RootLayout = root;
        PrimaryDocumentDock = documentDock;
        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = EnumerateDockables(layout)
            .Where(dockable => !string.IsNullOrWhiteSpace(dockable.Id))
            .GroupBy(dockable => dockable.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new Func<object?>(() => ResolveContent(group.Key)),
                StringComparer.Ordinal);
        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            [RootId] = () => RootLayout,
            [DocumentDockId] = () => PrimaryDocumentDock
        };
        base.InitLayout(layout);
        RootLayout = layout as IRootDock ?? FindRoot(layout);
        PrimaryDocumentDock = EnumerateDockables(layout).OfType<IDocumentDock>()
            .FirstOrDefault(dock => dock.Id == DocumentDockId)
            ?? EnumerateDockables(layout).OfType<IDocumentDock>().FirstOrDefault();
        foreach (var dockable in EnumerateDockables(layout))
        {
            if (dockable.Id == SystemToolId || _descriptors.ContainsKey(dockable.Id))
            {
                dockable.Context = ResolveContent(dockable.Id);
            }
        }
    }

    public void RegisterDescriptors(IEnumerable<WorkspaceDocumentDescriptor> descriptors)
    {
        _descriptors.Clear();
        foreach (var descriptor in descriptors)
        {
            _descriptors[descriptor.Id] = descriptor;
        }

        if (!_descriptors.ContainsKey(LensDocumentId))
        {
            _descriptors[LensDocumentId] = new WorkspaceDocumentDescriptor(
                LensDocumentId,
                WorkspaceDocumentKind.LensEditor,
                "镜头数据");
        }
    }

    public Document OpenDocument(WorkspaceDocumentDescriptor descriptor)
    {
        _descriptors[descriptor.Id] = descriptor;
        var existing = RootLayout is null
            ? null
            : EnumerateDockables(RootLayout).OfType<Document>()
                .FirstOrDefault(document => document.Id == descriptor.Id);
        if (existing is not null)
        {
            SetActiveDockable(existing);
            if (existing.Owner is IDock owner)
            {
                SetFocusedDockable(owner, existing);
            }

            ActivateWindow(existing);
            return existing;
        }

        var document = CreateDocument(descriptor);
        var target = ActiveDocumentDock() ?? PrimaryDocumentDock
            ?? throw new InvalidOperationException("工作区中没有文档停靠区域。");
        AddDockable(target, document);
        SetActiveDockable(document);
        SetFocusedDockable(target, document);
        RaiseLayoutChanged();
        return document;
    }

    public WorkspaceDocumentDescriptor? Descriptor(string id)
    {
        return _descriptors.TryGetValue(id, out var descriptor) ? descriptor : null;
    }

    public void UpdateDescriptor(WorkspaceDocumentDescriptor descriptor)
    {
        _descriptors[descriptor.Id] = descriptor;
    }

    public void ApplyLock(string id, bool locked)
    {
        if (_content.TryGetValue(id, out var control))
        {
            SetLocked(control, locked);
        }
    }

    public IReadOnlyList<WorkspaceDocumentDescriptor> SnapshotDescriptors()
    {
        return _descriptors.Values.Select(descriptor =>
        {
            if (!_content.TryGetValue(descriptor.Id, out var control))
            {
                return descriptor;
            }

            return control switch
            {
                AnalysisPanel analysis => descriptor with
                {
                    AnalysisName = analysis.AnalysisName,
                    InstanceId = analysis.InstanceId,
                    Settings = new Dictionary<string, string>(analysis.Settings),
                    IsLocked = analysis.IsLocked
                },
                ViewerPanel viewer => descriptor with { IsLocked = viewer.IsLocked },
                _ => descriptor
            };
        }).ToArray();
    }

    public override bool OnDockableClosing(IDockable? dockable)
    {
        if (dockable?.Id == LensDocumentId)
        {
            return false;
        }

        return base.OnDockableClosing(dockable);
    }

    public override void OnDockableClosed(IDockable? dockable)
    {
        if (dockable is not null)
        {
            if (_content.Remove(dockable.Id, out var control) && control is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _descriptors.Remove(dockable.Id);
        }

        base.OnDockableClosed(dockable);
        RaiseLayoutChanged();
    }

    public override void OnDockableMoved(IDockable? dockable)
    {
        base.OnDockableMoved(dockable);
        RaiseLayoutChanged();
    }

    public override void OnDockableDocked(IDockable? dockable, DockOperation operation)
    {
        base.OnDockableDocked(dockable, operation);
        RaiseLayoutChanged();
    }

    public override void OnDockableUndocked(IDockable? dockable, DockOperation operation)
    {
        base.OnDockableUndocked(dockable, operation);
        RaiseLayoutChanged();
    }

    public override void OnWindowAdded(IDockWindow? window)
    {
        base.OnWindowAdded(window);
        RaiseLayoutChanged();
    }

    public override void OnWindowRemoved(IDockWindow? window)
    {
        base.OnWindowRemoved(window);
        RaiseLayoutChanged();
    }

    public IReadOnlyList<Document> OpenDocuments()
    {
        return RootLayout is null
            ? Array.Empty<Document>()
            : EnumerateDockables(RootLayout).OfType<Document>().ToArray();
    }

    public void DisposeContent()
    {
        foreach (var disposable in _content.Values.OfType<IDisposable>())
        {
            disposable.Dispose();
        }

        _content.Clear();
    }

    private Document CreateDocument(WorkspaceDocumentDescriptor descriptor)
    {
        return new Document
        {
            Id = descriptor.Id,
            Title = descriptor.Title,
            CanClose = descriptor.Kind != WorkspaceDocumentKind.LensEditor,
            CanFloat = true,
            CanDrag = true,
            CanDrop = true,
            Context = ResolveContent(descriptor.Id)
        };
    }

    private Control ResolveContent(string id)
    {
        if (_content.TryGetValue(id, out var existing))
        {
            return existing;
        }

        if (id == SystemToolId)
        {
            return _content[id] = CreateSystemToolContent();
        }

        if (!_descriptors.TryGetValue(id, out var descriptor))
        {
            return new TextBlock
            {
                Text = "此页面类型不可用，已从会话中跳过。",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray
            };
        }

        Control control = descriptor.Kind switch
        {
            WorkspaceDocumentKind.LensEditor => new LensEditorPanel(_application.Prescription, _application.Events),
            WorkspaceDocumentKind.Viewer2D => new ViewerPanel(_application.Visualization, _application.Events, SceneDimension.TwoDimensional),
            WorkspaceDocumentKind.Viewer3D => new ViewerPanel(_application.Visualization, _application.Events, SceneDimension.ThreeDimensional),
            WorkspaceDocumentKind.SolidModel => new ViewerPanel(
                _application.Visualization,
                _application.Events,
                SceneDimension.ThreeDimensional,
                ViewerPresentationMode.SolidModel),
            WorkspaceDocumentKind.Optimization => new OptimizationPanel(_application.Prescription, _application.Optimization, _application.Events),
            WorkspaceDocumentKind.Tolerancing => new TolerancingPanel(_application.Prescription, _application.Tolerancing, _application.Events),
            WorkspaceDocumentKind.MultiConfiguration => new MultiConfigurationPanel(_application.Prescription, _application.MultiConfiguration, _application.Events),
            WorkspaceDocumentKind.Analysis => new AnalysisPanel(
                _application.Analyses,
                _application.Events,
                _settings,
                descriptor.AnalysisName ?? descriptor.Title,
                descriptor.InstanceId,
                descriptor.Settings),
            _ => throw new ArgumentOutOfRangeException()
        };
        SetLocked(control, descriptor.IsLocked);
        _content[id] = control;
        return control;
    }

    private Control CreateSystemToolContent()
    {
        return new SystemPropertiesPanel(_application.Prescription, _application.Events);
    }

    private IDocumentDock? ActiveDocumentDock()
    {
        if (RootLayout?.ActiveDockable is IDocumentDock direct)
        {
            return direct;
        }

        var activeDocument = OpenDocuments().FirstOrDefault(document => document.IsActive);
        return activeDocument?.Owner as IDocumentDock;
    }

    private void RaiseLayoutChanged() => LayoutChanged?.Invoke(this, EventArgs.Empty);

    private static void SetLocked(Control control, bool value)
    {
        switch (control)
        {
            case AnalysisPanel analysis:
                analysis.IsLocked = value;
                break;
            case ViewerPanel viewer:
                viewer.IsLocked = value;
                break;
        }
    }

    public static IEnumerable<IDockable> EnumerateDockables(IDockable root)
    {
        return EnumerateDockables(root, new HashSet<IDockable>());
    }

    private static IEnumerable<IDockable> EnumerateDockables(IDockable dockable, HashSet<IDockable> seen)
    {
        if (!seen.Add(dockable))
        {
            yield break;
        }

        yield return dockable;
        if (dockable is IRootDock root)
        {
            foreach (var collection in new[]
                     {
                         root.HiddenDockables,
                         root.LeftPinnedDockables,
                         root.RightPinnedDockables,
                         root.TopPinnedDockables,
                         root.BottomPinnedDockables
                     })
            {
                if (collection is null)
                {
                    continue;
                }

                foreach (var child in collection)
                {
                    foreach (var descendant in EnumerateDockables(child, seen))
                    {
                        yield return descendant;
                    }
                }
            }

            if (root.PinnedDock is not null)
            {
                foreach (var descendant in EnumerateDockables(root.PinnedDock, seen))
                {
                    yield return descendant;
                }
            }

            if (root.Windows is not null)
            {
                foreach (var window in root.Windows)
                {
                    if (window.Layout is null)
                    {
                        continue;
                    }

                    foreach (var child in EnumerateDockables(window.Layout, seen))
                    {
                        yield return child;
                    }
                }
            }
        }

        if (dockable is not IDock dock)
        {
            yield break;
        }

        if (dock.VisibleDockables is not null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                foreach (var descendant in EnumerateDockables(child, seen))
                {
                    yield return descendant;
                }
            }
        }

        foreach (var related in new[]
                 {
                     dock.ActiveDockable,
                     dock.DefaultDockable,
                     dock.FocusedDockable
                 })
        {
            if (related is null)
            {
                continue;
            }

            foreach (var descendant in EnumerateDockables(related, seen))
            {
                yield return descendant;
            }
        }
    }
}
