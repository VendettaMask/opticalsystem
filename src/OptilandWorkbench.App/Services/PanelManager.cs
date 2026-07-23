using Avalonia.Controls;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Manufacturing;

namespace OptilandWorkbench.App.Services;

public enum WorkspacePanelId
{
    LensEditor,
    SystemProperties,
    Viewer,
    Analysis,
    Optimization,
    Tolerancing,
    MultiConfiguration
}

public sealed class PanelManager : IDisposable
{
    private readonly IWorkbenchApplication _application;
    private readonly AppSettings _settings;
    private readonly WorkspaceSessionStore _sessionStore;
    private readonly WorkspaceDockLayoutSerializer _layoutSerializer = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly SemaphoreSlim _sessionSaveGate = new(1, 1);
    private long _restoreGeneration;
    private bool _restoring;
    private bool _disposed;

    public PanelManager(
        IWorkbenchApplication application,
        AppSettings settings,
        WorkspaceSessionStore? sessionStore = null)
    {
        _application = application;
        _settings = settings;
        _sessionStore = sessionStore ?? new WorkspaceSessionStore();
        Factory = new WorkspaceDockFactory(application, settings);
        Layout = Factory.CreateLayout();
        Factory.InitLayout(Layout);
        WorkspaceControl = new DockControl
        {
            InitializeFactory = true,
            InitializeLayout = false,
            Factory = Factory,
            Layout = Layout,
            IsDockingEnabled = true
        };
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += OnSaveTimerTick;
        Factory.LayoutChanged += OnLayoutChanged;
        _application.Events.Changed += OnWorkspaceChanged;
    }

    public WorkspaceDockFactory Factory { get; }

    public event EventHandler<WorkspacePersistenceFailedEventArgs>? PersistenceFailed;

    public IRootDock Layout { get; private set; }

    public DockControl WorkspaceControl { get; }

    public Control WorkspaceGrid => WorkspaceControl;

    public void ApplyDisplaySettings()
    {
        Factory.ApplyDisplaySettings();
        WorkspaceControl.InvalidateVisual();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RestoreCurrentSessionAsync(cancellationToken);
    }

    public void Show(WorkspacePanelId id)
    {
        switch (id)
        {
            case WorkspacePanelId.SystemProperties:
                FocusSystemTool();
                break;
            case WorkspacePanelId.Viewer:
                ShowViewer(OpticSceneViewMode.ThreeDimensional);
                break;
            case WorkspacePanelId.Analysis:
                ShowAnalysis(_application.Analyses.AnalysisNames.FirstOrDefault() ?? "处方报告");
                break;
            case WorkspacePanelId.Optimization:
                OpenStable("document:optimization", WorkspaceDocumentKind.Optimization, "优化");
                break;
            case WorkspacePanelId.Tolerancing:
                OpenStable("document:tolerancing", WorkspaceDocumentKind.Tolerancing, "公差");
                break;
            case WorkspacePanelId.MultiConfiguration:
                OpenStable("document:multi-configuration", WorkspaceDocumentKind.MultiConfiguration, "多配置");
                break;
            default:
                OpenStable(WorkspaceDockFactory.LensDocumentId, WorkspaceDocumentKind.LensEditor, "镜头数据");
                break;
        }
    }

    public void ShowViewer(OpticSceneViewMode mode)
    {
        if (mode == OpticSceneViewMode.TwoDimensional)
        {
            OpenStable("document:viewer-2d", WorkspaceDocumentKind.Viewer2D, "二维视图");
        }
        else
        {
            OpenStable("document:viewer-3d", WorkspaceDocumentKind.Viewer3D, "三维视图");
        }
    }

    public void ShowSolidModel()
    {
        OpenStable("document:solid-model", WorkspaceDocumentKind.SolidModel, "实体模型");
    }

    public void ShowMaterialLibrary()
    {
        OpenStable("document:material-library", WorkspaceDocumentKind.MaterialLibrary, "材料库");
    }

    public void ShowGlassCatalog()
    {
        OpenStable("document:glass-catalog", WorkspaceDocumentKind.GlassCatalog, "玻璃");
    }

    public void ShowManufacturability()
    {
        OpenStable(
            "document:manufacturability",
            WorkspaceDocumentKind.Manufacturability,
            "可加工性评估");
    }

    public void ShowOpticalDrawing(OpticalDrawingStandard standard)
    {
        var isGb = standard == OpticalDrawingStandard.GbT13323_2009;
        Factory.OpenDocument(new WorkspaceDocumentDescriptor(
            isGb ? "document:optical-drawing-gb" : "document:optical-drawing-iso",
            WorkspaceDocumentKind.OpticalDrawing,
            isGb ? "光学制图 · GB/T 13323—2009" : "光学制图 · ISO 10110",
            Settings: new Dictionary<string, string>
            {
                ["standard"] = standard.ToString()
            }));
    }

    public void ShowAnalysis(string analysisName)
    {
        var canonical = _application.Analyses.CanonicalKey(analysisName);
        Factory.OpenDocument(new WorkspaceDocumentDescriptor(
            $"analysis:{canonical}",
            WorkspaceDocumentKind.Analysis,
            analysisName,
            analysisName,
            StableAnalysisGuid(canonical)));
    }

    public void CloneActiveAnalysis()
    {
        if (ActiveDocument() is not Document active
            || Factory.Descriptor(active.Id) is not { Kind: WorkspaceDocumentKind.Analysis } descriptor)
        {
            return;
        }

        var instanceId = Guid.NewGuid();
        Factory.OpenDocument(descriptor with
        {
            Id = $"analysis:{_application.Analyses.CanonicalKey(descriptor.AnalysisName ?? descriptor.Title)}:{instanceId:N}",
            Title = $"{descriptor.Title}（副本）",
            InstanceId = instanceId,
            Settings = descriptor.Settings is null ? null : new Dictionary<string, string>(descriptor.Settings)
        });
    }

    public void DockAllWindows()
    {
        foreach (var document in Factory.OpenDocuments())
        {
            Factory.DockAsDocument(document);
        }
    }

    public void DockToSinglePane()
    {
        DockAllWindows();
        if (Factory.PrimaryDocumentDock is not IDock target)
        {
            return;
        }

        foreach (var document in Factory.OpenDocuments().ToArray())
        {
            if (document.Owner is IDock source && !ReferenceEquals(source, target))
            {
                Factory.MoveDockable(source, target, document, null);
            }
        }
    }

    public void CloseAllDocuments()
    {
        foreach (var document in Factory.OpenDocuments()
                     .Where(document => document.Id != WorkspaceDockFactory.LensDocumentId)
                     .ToArray())
        {
            Factory.CloseDockable(document);
        }
    }

    public void FloatAllWindows()
    {
        foreach (var document in Factory.OpenDocuments().ToArray())
        {
            Factory.FloatDockable(document);
        }

        var windows = Layout.Windows?.ToArray() ?? Array.Empty<IDockWindow>();
        for (var index = 0; index < windows.Length; index++)
        {
            windows[index].X = 90 + (index * 28);
            windows[index].Y = 90 + (index * 28);
            windows[index].Width = 920;
            windows[index].Height = 680;
            windows[index].Save();
        }
    }

    public void TileAllWindows()
    {
        FloatAllWindows();
        ArrangeFloatingWindows(tile: true);
    }

    public void CascadeAllWindows()
    {
        FloatAllWindows();
        ArrangeFloatingWindows(tile: false);
    }

    public void DockAnalysisWindows() => DockAllWindows();

    public void FloatAnalysisWindows() => FloatAllWindows();

    public void TileAnalysisWindows() => TileAllWindows();

    public void CascadeAnalysisWindows() => CascadeAllWindows();

    public void SetActiveDocumentLocked(bool locked)
    {
        if (ActiveDocument() is not Document document
            || Factory.Descriptor(document.Id) is not { } descriptor)
        {
            return;
        }

        Factory.UpdateDescriptor(descriptor with { IsLocked = locked });
        Factory.ApplyLock(document.Id, locked);
        ScheduleSave();
    }

    public WorkspaceLayoutState CaptureLayout()
    {
        var toolDock = WorkspaceDockFactory.EnumerateDockables(Layout)
            .OfType<ToolDock>()
            .FirstOrDefault(tool => tool.Id == WorkspaceDockFactory.ToolDockId);
        var width = toolDock is null || double.IsNaN(toolDock.Proportion)
            ? 286
            : Math.Clamp(toolDock.Proportion * 1440, 230, 420);
        return new WorkspaceLayoutState(width, 0, 0);
    }

    public void ApplyLayout(WorkspaceLayoutState layout)
    {
        var toolDock = WorkspaceDockFactory.EnumerateDockables(Layout)
            .OfType<ToolDock>()
            .FirstOrDefault(tool => tool.Id == WorkspaceDockFactory.ToolDockId);
        if (toolDock is not null)
        {
            toolDock.Proportion = Math.Clamp(layout.LeftPaneWidth / 1440.0, 0.16, 0.34);
        }
    }

    public void ResetLayout()
    {
        ReplaceLayout(Factory.CreateLayout());
        ScheduleSave();
    }

    public async Task SaveDefaultLayoutAsync(CancellationToken cancellationToken = default)
    {
        await SaveSessionAsync(null, cancellationToken);
    }

    public async Task RestoreDefaultLayoutAsync(CancellationToken cancellationToken = default)
    {
        var session = await _sessionStore.LoadAsync(null, cancellationToken);
        if (!TryRestore(session))
        {
            if (session is not null)
            {
                _sessionStore.Quarantine(null);
            }

            ResetLayout();
        }
    }

    public async Task SaveLayoutSlotAsync(int slot, CancellationToken cancellationToken = default)
    {
        await _sessionSaveGate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
            {
                return;
            }

            await _sessionStore.SaveSlotAsync(slot, CaptureSession(), cancellationToken);
        }
        finally
        {
            _sessionSaveGate.Release();
        }
    }

    public async Task LoadLayoutSlotAsync(int slot, CancellationToken cancellationToken = default)
    {
        var session = await _sessionStore.LoadSlotAsync(slot, cancellationToken);
        if (session is not null && !TryRestore(session))
        {
            _sessionStore.QuarantineSlot(slot);
        }
    }

    public Task SaveCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        return SaveSessionAsync(_application.Documents.CurrentPath, cancellationToken);
    }

    public async Task RestoreCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        var generation = Interlocked.Increment(ref _restoreGeneration);
        _restoring = true;
        try
        {
            var path = _application.Documents.CurrentPath;
            var session = await _sessionStore.LoadAsync(path, cancellationToken);
            if (_disposed || generation != Interlocked.Read(ref _restoreGeneration))
            {
                return;
            }

            if (!TryRestore(session))
            {
                if (session is not null)
                {
                    _sessionStore.Quarantine(path);
                }

                if (path is not null)
                {
                    session = await _sessionStore.LoadAsync(null, cancellationToken);
                    if (_disposed || generation != Interlocked.Read(ref _restoreGeneration))
                    {
                        return;
                    }

                    if (!TryRestore(session))
                    {
                        if (session is not null)
                        {
                            _sessionStore.Quarantine(null);
                        }

                        ResetLayout();
                    }
                }
            }
        }
        finally
        {
            if (generation == Interlocked.Read(ref _restoreGeneration))
            {
                _restoring = false;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Increment(ref _restoreGeneration);
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTimerTick;
        Factory.LayoutChanged -= OnLayoutChanged;
        _application.Events.Changed -= OnWorkspaceChanged;
        Factory.DisposeContent();
    }

    private void OpenStable(string id, WorkspaceDocumentKind kind, string title)
    {
        Factory.OpenDocument(new WorkspaceDocumentDescriptor(id, kind, title));
    }

    private void FocusSystemTool()
    {
        var tool = WorkspaceDockFactory.EnumerateDockables(Layout)
            .FirstOrDefault(dockable => dockable.Id == WorkspaceDockFactory.SystemToolId);
        if (tool is null)
        {
            return;
        }

        Factory.SetActiveDockable(tool);
        if (tool.Owner is IDock owner)
        {
            Factory.SetFocusedDockable(owner, tool);
        }
    }

    private IDockable? ActiveDocument()
    {
        return Factory.OpenDocuments().FirstOrDefault(document => document.IsActive)
            ?? Factory.OpenDocuments().FirstOrDefault(document => document.Owner is IDock dock && dock.ActiveDockable == document);
    }

    private async Task SaveSessionAsync(string? path, CancellationToken cancellationToken)
    {
        await _sessionSaveGate.WaitAsync(cancellationToken);
        try
        {
            if (_restoring || _disposed)
            {
                return;
            }

            await _sessionStore.SaveAsync(path, CaptureSession(), cancellationToken);
        }
        finally
        {
            _sessionSaveGate.Release();
        }
    }

    private async void OnSaveTimerTick(object? sender, EventArgs args)
    {
        _saveTimer.Stop();
        try
        {
            await SaveCurrentSessionAsync();
        }
        catch (Exception exception)
        {
            PersistenceFailed?.Invoke(this, new WorkspacePersistenceFailedEventArgs(exception));
        }
    }

    private WorkspaceSession CaptureSession() => new(
        WorkspaceSessionStore.CurrentVersion,
        _layoutSerializer.Serialize(Layout),
        Factory.SnapshotDescriptors(),
        ActiveDocument()?.Id);

    private bool TryRestore(WorkspaceSession? session)
    {
        if (session is null || string.IsNullOrWhiteSpace(session.DockLayoutJson))
        {
            return false;
        }

        try
        {
            var knownAnalyses = _application.Analyses.AnalysisNames
                .Select(_application.Analyses.CanonicalKey)
                .ToHashSet(StringComparer.Ordinal);
            var descriptors = session.Documents.Where(descriptor =>
                descriptor.Kind != WorkspaceDocumentKind.Analysis
                || knownAnalyses.Contains(_application.Analyses.CanonicalKey(descriptor.AnalysisName ?? descriptor.Title)))
                .ToArray();
            Factory.RegisterDescriptors(descriptors);
            var restored = _layoutSerializer.Deserialize(session.DockLayoutJson);
            if (restored is null)
            {
                return false;
            }

            ReplaceLayout(restored);
            var active = session.ActiveDocumentId is null
                ? null
                : Factory.OpenDocuments().FirstOrDefault(document => document.Id == session.ActiveDocumentId);
            if (active is not null)
            {
                Factory.SetActiveDockable(active);
                if (active.Owner is IDock owner)
                {
                    Factory.SetFocusedDockable(owner, active);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ReplaceLayout(IRootDock layout)
    {
        Factory.DisposeContent();
        ClampFloatingWindows(layout);
        Layout = layout;
        Factory.InitLayout(layout);
        WorkspaceControl.Layout = layout;
    }

    private void ClampFloatingWindows(IRootDock layout)
    {
        var screens = TopLevel.GetTopLevel(WorkspaceControl)?.Screens;
        var workingArea = screens?.Primary?.WorkingArea;
        if (workingArea is null || layout.Windows is null)
        {
            return;
        }

        foreach (var window in layout.Windows)
        {
            var bounds = WorkspaceSessionStore.ClampWindowBounds(
                window.X,
                window.Y,
                window.Width,
                window.Height,
                workingArea.Value.X,
                workingArea.Value.Y,
                workingArea.Value.Width,
                workingArea.Value.Height);
            window.X = bounds.X;
            window.Y = bounds.Y;
            window.Width = bounds.Width;
            window.Height = bounds.Height;
        }
    }

    private void ArrangeFloatingWindows(bool tile)
    {
        var windows = Layout.Windows?.ToArray() ?? Array.Empty<IDockWindow>();
        if (windows.Length == 0)
        {
            return;
        }

        if (!tile)
        {
            for (var index = 0; index < windows.Length; index++)
            {
                windows[index].X = 80 + (index * 30);
                windows[index].Y = 80 + (index * 28);
                windows[index].Width = 920;
                windows[index].Height = 680;
                windows[index].Save();
            }

            return;
        }

        var columns = (int)Math.Ceiling(Math.Sqrt(windows.Length));
        var rows = (int)Math.Ceiling(windows.Length / (double)columns);
        var cellWidth = Math.Max(420, 1440.0 / columns);
        var cellHeight = Math.Max(320, 900.0 / rows);
        for (var index = 0; index < windows.Length; index++)
        {
            windows[index].X = 30 + ((index % columns) * cellWidth);
            windows[index].Y = 50 + ((index / columns) * cellHeight);
            windows[index].Width = cellWidth;
            windows[index].Height = cellHeight;
            windows[index].Save();
        }
    }

    private void OnLayoutChanged(object? sender, EventArgs args) => ScheduleSave();

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        if (args.FileSwitched)
        {
            Dispatcher.UIThread.Post(RestoreAfterFileSwitchAsync);
        }
    }

    private async void RestoreAfterFileSwitchAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await RestoreCurrentSessionAsync();
        }
        catch (Exception exception)
        {
            PersistenceFailed?.Invoke(this, new WorkspacePersistenceFailedEventArgs(exception));
        }
    }

    private void ScheduleSave()
    {
        if (_restoring || _disposed)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static Guid StableAnalysisGuid(string canonical)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
        return new Guid(bytes);
    }
}

public sealed record WorkspacePersistenceFailedEventArgs(Exception Exception);
