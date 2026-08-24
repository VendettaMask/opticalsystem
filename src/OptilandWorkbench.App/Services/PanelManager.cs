using Avalonia.Controls;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Manufacturing;
using OptilandWorkbench.App.Panels;

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
    private bool _initialized;
    private bool _disposed;

    public PanelManager(
        IWorkbenchApplication application,
        AppSettings settings,
        WorkspaceSessionStore? sessionStore = null,
        Func<string, Task<bool>>? openProjectAsync = null)
    {
        _application = application;
        _settings = settings;
        _sessionStore = sessionStore ?? new WorkspaceSessionStore();
        Factory = new WorkspaceDockFactory(application, settings, openProjectAsync);
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

    public bool HasUnsavedToleranceChanges => Factory.HasUnsavedToleranceChanges;

    public Task<bool> SaveUnsavedToleranceChangesAsync(TopLevel owner) =>
        Factory.SaveUnsavedToleranceChangesAsync(owner);

    public void ApplyDisplaySettings()
    {
        Factory.ApplyDisplaySettings();
        WorkspaceControl.InvalidateVisual();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RestoreCurrentSessionAsync(cancellationToken);
        _initialized = true;
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
                ShowAnalysis(_application.Analyses.AnalysisNames.FirstOrDefault() ?? "表面数据报告");
                break;
            case WorkspacePanelId.Optimization:
                OpenStable("document:optimization", WorkspaceDocumentTypes.Optimization, "优化");
                break;
            case WorkspacePanelId.Tolerancing:
                OpenStable("document:tolerancing", WorkspaceDocumentTypes.Tolerancing, "公差数据编辑器");
                break;
            case WorkspacePanelId.MultiConfiguration:
                OpenStable("document:multi-configuration", WorkspaceDocumentTypes.MultiConfiguration, "多配置");
                break;
            default:
                OpenStable(WorkspaceDockFactory.LensDocumentId, WorkspaceDocumentTypes.LensEditor, "镜头数据");
                break;
        }
    }

    public void ShowViewer(OpticSceneViewMode mode)
    {
        if (mode == OpticSceneViewMode.TwoDimensional)
        {
            OpenStable("document:viewer-2d", WorkspaceDocumentTypes.Viewer2D, "二维视图");
        }
        else
        {
            OpenStable("document:viewer-3d", WorkspaceDocumentTypes.Viewer3D, "三维视图");
        }
    }

    public void ShowSolidModel()
    {
        OpenStable("document:solid-model", WorkspaceDocumentTypes.SolidModel, "实体模型");
    }

    public void ShowTolerancingDataViewer() =>
        OpenStable("document:tolerancing", WorkspaceDocumentTypes.Tolerancing, "公差数据编辑器");

    public void ShowTolerancingReport() =>
        OpenStable("document:tolerance-report", WorkspaceDocumentTypes.ToleranceReport, "1: 公差报告");

    public void ShowTolerancingHistogram() =>
        OpenStable("document:tolerance-histogram", WorkspaceDocumentTypes.ToleranceHistogram, "直方图");

    public void ShowTolerancingYield() =>
        OpenStable("document:tolerance-yield", WorkspaceDocumentTypes.ToleranceYield, "良率");

    public async Task RunTolerancingAsync(Window owner)
    {
        const string editorId = "document:tolerancing";
        OpenStable(editorId, WorkspaceDocumentTypes.Tolerancing, "公差数据编辑器");
        var editor = Factory.DocumentContent<TolerancingPanel>(editorId)
            ?? throw new InvalidOperationException("公差数据编辑器不可用。");
        if (!await editor.ShowAnalysisDialogAsync(owner))
        {
            return;
        }

        Factory.RefreshDocumentContent("document:tolerance-histogram");
        Factory.RefreshDocumentContent("document:tolerance-yield");
        ShowTolerancingReport();
        Factory.RefreshDocumentContent("document:tolerance-report");
    }

    public void ShowMaterialLibrary()
    {
        OpenStable("document:material-library", WorkspaceDocumentTypes.MaterialLibrary, "材料库");
    }

    public void ShowLensLibrary()
    {
        OpenStable("document:lens-library", WorkspaceDocumentTypes.LensLibrary, "镜头库");
    }

    public void ShowStockLensCatalog()
    {
        OpenStable(
            "document:stock-lens-catalog",
            WorkspaceDocumentTypes.StockLensCatalog,
            "库存镜头查看");
    }

    public void ShowStockLensMatching()
    {
        OpenStable(
            "document:stock-lens-matching",
            WorkspaceDocumentTypes.StockLensMatching,
            "库存镜头匹配");
    }

    public void ShowGlassCatalog()
    {
        OpenStable("document:glass-catalog", WorkspaceDocumentTypes.GlassCatalog, "玻璃");
    }

    public void ShowMaterialAnalysis(MaterialAnalysisKind kind)
    {
        Factory.OpenDocument(new WorkspaceDocumentDescriptor(
            $"document:material-analysis:{kind}",
            WorkspaceDocumentTypes.MaterialAnalysis,
            MaterialAnalysisPanel.Title(kind),
            Settings: new Dictionary<string, string>
            {
                ["MaterialAnalysisKind"] = kind.ToString()
            }));
    }

    public void ShowManufacturability()
    {
        OpenStable(
            "document:manufacturability",
            WorkspaceDocumentTypes.Manufacturability,
            "可加工性评估");
    }

    public void ShowOpticalDrawing(OpticalDrawingStandard standard)
    {
        var isGb = standard == OpticalDrawingStandard.GbT13323_2009;
        Factory.OpenDocument(new WorkspaceDocumentDescriptor(
            isGb ? "document:optical-drawing-gb" : "document:optical-drawing-iso",
            WorkspaceDocumentTypes.OpticalDrawing,
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
            WorkspaceDocumentTypes.Analysis,
            analysisName,
            analysisName,
            StableAnalysisGuid(canonical)));
    }

    public void CloneActiveAnalysis()
    {
        if (ActiveDocument() is not Document active
            || Factory.Descriptor(active.Id) is not { TypeId: WorkspaceDocumentTypes.Analysis } descriptor)
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
        if (Factory.PrimaryDocumentDock is not IDock target)
        {
            return;
        }

        foreach (var document in FloatingDocuments().Distinct().ToArray())
        {
            MoveDocumentToDock(document, target);
        }

        PruneEmptyFloatingWindows();
        ScheduleSave();
    }

    public void DockToSinglePane()
    {
        if (CollectDocumentsInPrimaryDock() is not { } target)
        {
            return;
        }

        MdiLayoutHelper.RestoreDocuments(target);
        Factory.SetDocumentDockLayoutModeTabbed(target);
        ActivateLastDocument(target);
        ScheduleSave();
    }

    public void CloseAllDocuments()
    {
        foreach (var document in Factory.OpenDocuments()
                     .Where(document => document.Id != WorkspaceDockFactory.LensDocumentId)
                     .ToArray())
        {
            Factory.CloseDockable(document);
        }

        PruneEmptyFloatingWindows();
    }

    public void FloatAllWindows()
    {
        PruneEmptyFloatingWindows();
        var floatingDocumentIds = FloatingDocuments()
            .Select(document => document.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var document in Factory.OpenDocuments().ToArray())
        {
            if (!floatingDocumentIds.Contains(document.Id))
            {
                Factory.FloatDockable(document);
            }
        }

        CascadeFloatingWindows();
    }

    public void TileAllWindows()
    {
        if (CollectDocumentsInPrimaryDock() is not { } target)
        {
            return;
        }

        Factory.SetDocumentDockLayoutModeMdi(target);
        ArrangeMdiDocuments(target, AdaptiveMdiLayout.TileDocuments);
        ScheduleSave();
    }

    public void CascadeAllWindows()
    {
        if (CollectDocumentsInPrimaryDock() is not { } target)
        {
            return;
        }

        Factory.SetDocumentDockLayoutModeMdi(target);
        ArrangeMdiDocuments(target, MdiLayoutHelper.CascadeDocuments);
        ScheduleSave();
    }

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

    public void ToggleActiveDocumentLocked()
    {
        if (ActiveDocument() is not Document document
            || Factory.Descriptor(document.Id) is not { } descriptor)
        {
            return;
        }

        SetActiveDocumentLocked(!descriptor.IsLocked);
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

    private void OpenStable(string id, string typeId, string title)
    {
        Factory.OpenDocument(new WorkspaceDocumentDescriptor(id, typeId, title));
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
            PruneEmptyFloatingWindows();
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
                WorkspaceDocumentTypes.IsKnown(descriptor.TypeId)
                && (descriptor.TypeId != WorkspaceDocumentTypes.Analysis
                    || knownAnalyses.Contains(_application.Analyses.CanonicalKey(descriptor.AnalysisName ?? descriptor.Title))))
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

    private void CascadeFloatingWindows()
    {
        var windows = Layout.Windows?.ToArray() ?? Array.Empty<IDockWindow>();
        if (windows.Length == 0)
        {
            return;
        }

        var workArea = FloatingWorkArea();
        var width = Math.Min(920, Math.Max(360, workArea.Width * 0.72));
        var height = Math.Min(680, Math.Max(240, workArea.Height * 0.72));
        var availableX = Math.Max(0, workArea.Width - width - 48);
        var availableY = Math.Max(0, workArea.Height - height - 48);
        var stepX = windows.Length > 1 ? Math.Min(30, availableX / (windows.Length - 1)) : 0;
        var stepY = windows.Length > 1 ? Math.Min(28, availableY / (windows.Length - 1)) : 0;
        for (var index = 0; index < windows.Length; index++)
        {
            ApplyWindowBounds(
                windows[index],
                workArea.X + ((24 + (index * stepX)) * workArea.Scaling),
                workArea.Y + ((24 + (index * stepY)) * workArea.Scaling),
                width,
                height);
        }

        ScheduleSave();
    }

    private FloatingWindowWorkArea FloatingWorkArea()
    {
        var topLevel = TopLevel.GetTopLevel(WorkspaceControl);
        var screens = topLevel?.Screens;
        var screen = topLevel is null || screens is null
            ? null
            : screens.ScreenFromTopLevel(topLevel) ?? screens.Primary;
        if (screen is null)
        {
            return new FloatingWindowWorkArea(0, 0, 1440, 900, 1);
        }

        var scaling = Math.Max(0.1, screen.Scaling);
        return new FloatingWindowWorkArea(
            screen.WorkingArea.X,
            screen.WorkingArea.Y,
            screen.WorkingArea.Width / scaling,
            screen.WorkingArea.Height / scaling,
            scaling);
    }

    private static void ApplyWindowBounds(
        IDockWindow window,
        double x,
        double y,
        double width,
        double height)
    {
        window.WindowState = DockWindowState.Normal;
        window.X = x;
        window.Y = y;
        window.Width = width;
        window.Height = height;

        if (window.Host is not { } host)
        {
            return;
        }

        var wasTracked = host.IsTracked;
        host.IsTracked = false;
        try
        {
            host.SetWindowState(DockWindowState.Normal);
            host.SetPosition(x, y);
            host.SetSize(width, height);
        }
        finally
        {
            host.IsTracked = wasTracked;
        }
    }

    private void PruneEmptyFloatingWindows()
    {
        var emptyWindows = Layout.Windows?
            .Where(window => !WorkspaceDockLayoutSerializer.HasFloatingContent(window))
            .ToArray()
            ?? Array.Empty<IDockWindow>();
        foreach (var window in emptyWindows)
        {
            window.Host?.Exit();
            Factory.RemoveWindow(window);
            Layout.Windows?.Remove(window);
        }
    }

    private IDocumentDock? CollectDocumentsInPrimaryDock()
    {
        if (Factory.PrimaryDocumentDock is not IDocumentDock target)
        {
            return null;
        }

        foreach (var document in Factory.OpenDocuments().ToArray())
        {
            MoveDocumentToDock(document, target);
        }

        PruneEmptyFloatingWindows();
        ActivateLastDocument(target);
        return target;
    }

    private void MoveDocumentToDock(Document document, IDock target)
    {
        if (document.Owner is not IDock source || ReferenceEquals(source, target))
        {
            return;
        }

        Factory.MoveDockable(source, target, document, null);
    }

    private void ArrangeMdiDocuments(IDocumentDock target, Action<IDocumentDock> arrange)
    {
        arrange(target);
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!_disposed
                    && ReferenceEquals(Factory.PrimaryDocumentDock, target)
                    && target.LayoutMode == DocumentLayoutMode.Mdi)
                {
                    arrange(target);
                }
            },
            DispatcherPriority.Background);
    }

    private void ActivateLastDocument(IDocumentDock target)
    {
        if (target.VisibleDockables?.LastOrDefault() is not { } document)
        {
            return;
        }

        Factory.SetActiveDockable(document);
        Factory.SetFocusedDockable(target, document);
    }

    private IEnumerable<Document> FloatingDocuments()
    {
        return Layout.Windows?
            .Where(window => window.Layout is not null)
            .SelectMany(window => WorkspaceDockFactory.EnumerateDockables(window.Layout!))
            .OfType<Document>()
            ?? Enumerable.Empty<Document>();
    }

    private readonly record struct FloatingWindowWorkArea(
        double X,
        double Y,
        double Width,
        double Height,
        double Scaling);

    private void OnLayoutChanged(object? sender, EventArgs args) => ScheduleSave();

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        if (_initialized && args.FileSwitched)
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
