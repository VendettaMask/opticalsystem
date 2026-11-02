using Avalonia.Controls;
using Avalonia;
using System.Text.Json;
using Avalonia.Data;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using OpticalSystemDesign.App.Services;
using Orientation = Dock.Model.Core.Orientation;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

internal enum CoatingPaneLayout { Columns, Rows, Tabs }

/// <summary>One coating experiment, retained page controls, and Dock's native tab/float/drop lifecycle.</summary>
internal sealed class CoatingDockWorkspace : DockControl, IDisposable
{
    protected override Type StyleKeyOverride => typeof(DockControl);
    internal const string EditorId = "coating:editor";
    internal const string SpectrumId = "coating:spectrum";
    internal const string StackId = "coating:stack";
    internal const string ResultsId = "coating:results";
    private readonly CoatingDockFactory _factory;
    private bool _disposed;
    internal CoatingDockFactory DockFactory => _factory;
    internal IEnumerable<Control> PageContents => _factory.Contents;
    internal event Action<Window>? HostCreated;

    internal CoatingDockWorkspace(Control editor, Control spectrum, Window owner)
    {
        Name = "CoatingWorkspace";
        _factory = new CoatingDockFactory(owner, host => HostCreated?.Invoke(host));
        _factory.Register(EditorId, "膜系编辑器", editor);
        _factory.Register(SpectrumId, "光学评价", spectrum);
        InitializeFactory = true; InitializeLayout = false; IsDockingEnabled = true; Factory = _factory;
        ApplyLayout(CoatingPaneLayout.Columns, reset: true);
        UiLocalization.LanguageChanged += OnLanguageChanged;
    }

    internal void Open(string id, string? title = null, Control? content = null)
    {
        if (content is not null) _factory.Register(id, title!, content);
        _factory.Open(id);
    }

    internal void Close(string id) => _factory.CloseDockable(_factory.Pages[id]);
    internal bool IsPageOpen(string id) => _factory.AllDockables().Contains(_factory.Pages.GetValueOrDefault(id));
    internal void FloatActive() { if (_factory.ActivePage() is { } page) _factory.FloatDockable(page); }
    internal void RegisterPageFactory(string id, string title, Func<Control> create) => _factory.RegisterLazy(id, title, create);

    internal string CaptureLayout()
    {
        foreach (var window in _factory.Root?.Windows?.ToArray() ?? []) window.Save();
        _factory.NormalizeNodeIds();
        var active = _factory.AllDockables().OfType<IDock>().Where(d => d.ActiveDockable is not null && d.VisibleDockables?.Contains(d.ActiveDockable) == true)
            .ToDictionary(d => d.Id, d => d.ActiveDockable!.Id, StringComparer.Ordinal);
        return JsonSerializer.Serialize(new CoatingSavedLayout(1, new WorkspaceDockLayoutSerializer().Serialize(_factory.Root!),
            active, _factory.ActivePage()?.Id));
    }

    internal void RestoreLayout(string json)
    {
        var snapshot = CoatingDockLayoutStore.Parse(json);
        var candidate = new WorkspaceDockLayoutSerializer().Deserialize(snapshot.Tree) ?? throw new InvalidDataException("镀膜布局为空。");
        _factory.ValidateLayout(candidate);
        _factory.ValidateActive(candidate, snapshot);
        ClampWindows(candidate);
        var previous = CaptureLayout();
        try { Install(candidate, snapshot); }
        catch (Exception error)
        {
            var rollback = CoatingDockLayoutStore.Parse(previous);
            var prior = new WorkspaceDockLayoutSerializer().Deserialize(rollback.Tree)!;
            Install(prior, rollback);
            throw new InvalidDataException("无法恢复镀膜布局，已还原原布局。", error);
        }
    }

    private void Install(IRootDock candidate, CoatingSavedLayout snapshot)
    {
        _factory.Resetting = true;
        try
        {
            Layout = null; _factory.CloseFloatingWindows();
            _factory.Adopt(candidate); _factory.RestoreActive(snapshot);
            _factory.InitLayout(candidate); Layout = candidate; _factory.RestoreActive(snapshot);
        }
        finally { _factory.Resetting = false; }
        candidate.ShowWindows?.Execute(null);
    }

    private void ClampWindows(IRootDock root)
    {
        var screens = TopLevel.GetTopLevel(this)?.Screens;
        var areas = screens?.All.Select(s => new CoatingScreenArea(s.WorkingArea.X, s.WorkingArea.Y,
            s.WorkingArea.Width, s.WorkingArea.Height, s.Scaling)).ToArray() ?? [];
        foreach (var window in root.Windows ?? [])
        {
            var bounds = CoatingDockLayoutStore.ClampWindow(window.X, window.Y, window.Width, window.Height, areas);
            window.X = bounds.X; window.Y = bounds.Y; window.Width = bounds.Width; window.Height = bounds.Height;
        }
    }

    internal void ApplyLayout(CoatingPaneLayout layout, bool reset = false)
    {
        var active = reset ? EditorId : _factory.ActivePage()?.Id;
        var pages = reset ? new[] { EditorId, SpectrumId }
            : _factory.AllDockables().OfType<IDocument>().Select(d => d.Id).Concat([EditorId, SpectrumId]).Distinct().ToArray();
        _factory.Resetting = true;
        try
        {
            Layout = null;
            _factory.CloseFloatingWindows();
            Layout = _factory.BuildLayout(layout, pages);
            _factory.InitLayout(Layout);
            if (active is not null && _factory.Pages.TryGetValue(active, out var page) && _factory.AllDockables().Contains(page))
            { _factory.SetActiveDockable(page); if (page.Owner is IDock dock) _factory.SetFocusedDockable(dock, page); }
        }
        finally { _factory.Resetting = false; }
    }

    private void OnLanguageChanged(object? sender, EventArgs args) => _factory.RefreshTitles();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; UiLocalization.LanguageChanged -= OnLanguageChanged;
        _factory.Resetting = true; _factory.CloseFloatingWindows(); Layout = null;
    }
}

internal sealed class CoatingDockFactory : Factory
{
    private readonly string _group = "coating:pages:" + Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, string> _titles = new(StringComparer.Ordinal);
    private readonly Window _owner;
    private readonly Action<Window> _hostCreated;
    private readonly Dictionary<string, Control> _contents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<Control>> _builders = new(StringComparer.Ordinal);
    internal IEnumerable<Control> Contents => _contents.Values;
    internal Dictionary<string, Document> Pages { get; } = new(StringComparer.Ordinal);
    internal IRootDock? Root { get; private set; }
    internal bool Resetting { get; set; }
    private string? _activePage;

    internal CoatingDockFactory(Window owner, Action<Window> hostCreated)
    {
        _owner = owner; _hostCreated = hostCreated;
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>> { [nameof(IDockWindow)] = CreateHost };
        ContextLocator = new Dictionary<string, Func<object?>>();
    }

    private IHostWindow CreateHost()
    {
        var host = new HostWindow
        {
            Name = "CoatingFloatingWindow",
            ShowInTaskbar = false,
            MinWidth = 380,
            MinHeight = 300,
            FontFamily = _owner.FontFamily,
            FontSize = _owner.FontSize,
            FontStyle = _owner.FontStyle,
            FontWeight = _owner.FontWeight
        };
        _hostCreated(host);
        host.Bind(Window.FontSizeProperty, new Binding(nameof(Window.FontSize)) { Source = _owner });
        host.Bind(Window.FontFamilyProperty, new Binding(nameof(Window.FontFamily)) { Source = _owner });
        host.Bind(Window.FontStyleProperty, new Binding(nameof(Window.FontStyle)) { Source = _owner });
        host.Bind(Window.FontWeightProperty, new Binding(nameof(Window.FontWeight)) { Source = _owner });
        return host;
    }

    internal void Register(string id, string title, Control content)
    {
        if (Pages.TryGetValue(id, out var retained))
        {
            if (!ReferenceEquals(retained.Context, content)) throw new InvalidOperationException("停靠页必须保留同一份编辑或分析状态。");
            return;
        }
        _titles[id] = title; _contents.Add(id, content); EnsurePage(id);
        ContextLocator![id] = () => _contents[id];
    }

    internal void RegisterLazy(string id, string title, Func<Control> create)
    {
        _titles.Add(id, title); _builders.Add(id, create);
        ContextLocator![id] = () => EnsurePage(id).Context;
    }

    private Document EnsurePage(string id)
    {
        if (!_contents.TryGetValue(id, out var content)) _contents[id] = content = _builders[id]();
        if (Pages.TryGetValue(id, out var retained)) { retained.Context = content; return retained; }
        var page = new Document
        {
            Id = id,
            Title = UiLocalization.Text(_titles[id]),
            Context = content,
            DockGroup = _group,
            CanClose = true,
            CanFloat = true,
            CanDrag = true,
            CanPin = false
        };
        Pages.Add(id, page); return page;
    }

    internal void RefreshTitles()
    {
        foreach (var (id, page) in Pages) page.Title = UiLocalization.Text(_titles[id]);
    }

    public override IDocumentDock CreateDocumentDock() => new DocumentDock
    {
        Id = "coating:group:" + Guid.NewGuid().ToString("N"),
        DockGroup = _group,
        IsCollapsable = true,
        CanCreateDocument = false,
        EnableWindowDrag = true,
        CloseButtonShowMode = DocumentCloseButtonShowMode.Always,
        MinWidth = 300,
        MinHeight = 240
    };

    internal IRootDock BuildLayout(CoatingPaneLayout layout, string[] ids)
    {
        foreach (var page in Pages.Values) page.Owner = null;
        var editor = Pages[CoatingDockWorkspace.EditorId];
        var first = CreateDocumentDock(); first.IsCollapsable = false;
        first.VisibleDockables = CreateList<IDockable>(editor); first.ActiveDockable = editor;
        IDock body = first;
        var others = ids.Where(id => id != editor.Id && Pages.ContainsKey(id)).Select(id => Pages[id]).ToArray();
        if (layout == CoatingPaneLayout.Tabs)
        {
            foreach (var page in others) first.VisibleDockables.Add(page);
        }
        else
        {
            var second = CreateDocumentDock(); second.VisibleDockables = CreateList<IDockable>(others);
            second.ActiveDockable = others.FirstOrDefault();
            first.Proportion = .5; second.Proportion = .5;
            body = new ProportionalDock
            {
                Id = "coating:split",
                Orientation = layout == CoatingPaneLayout.Rows ? Orientation.Vertical : Orientation.Horizontal,
                VisibleDockables = CreateList<IDockable>(first, new ProportionalDockSplitter(), second),
                ActiveDockable = first
            };
        }
        return Root = new RootDock
        {
            Id = "coating:root",
            DockGroup = _group + ":root",
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(body),
            ActiveDockable = body,
            DefaultDockable = body,
            Windows = CreateList<IDockWindow>()
        };
    }

    public override IRootDock CreateLayout() => BuildLayout(CoatingPaneLayout.Columns,
        [CoatingDockWorkspace.EditorId, CoatingDockWorkspace.SpectrumId]);

    internal IEnumerable<IDockable> AllDockables() => Root is null ? [] : Walk(Root, includeWindows: true);
    private static IEnumerable<IDockable> Walk(IDockable item, bool includeWindows)
    {
        yield return item;
        if (item is IDock { VisibleDockables: { } children })
            foreach (var child in children) foreach (var nested in Walk(child, includeWindows)) yield return nested;
        if (includeWindows && item is IRootDock { Windows: { } windows })
            foreach (var window in windows) if (window.Layout is { } layout)
                foreach (var child in Walk(layout, includeWindows: true)) yield return child;
    }

    internal IDocument? ActivePage() => AllDockables().OfType<IDocumentDock>().Where(d => d.IsActive)
        .Select(d => d.ActiveDockable).OfType<IDocument>().FirstOrDefault(d => d.Id == _activePage)
        ?? AllDockables().OfType<IDocument>().FirstOrDefault(d => d.Id == _activePage)
        ?? AllDockables().OfType<IDocumentDock>().Select(d => d.ActiveDockable).OfType<IDocument>().FirstOrDefault();

    public override void OnActiveDockableChanged(IDockable? dockable)
    { base.OnActiveDockableChanged(dockable); if (dockable is IDocument) _activePage = dockable.Id; }

    public override void OnFocusedDockableChanged(IDockable? dockable)
    { base.OnFocusedDockableChanged(dockable); if (dockable is IDocument) _activePage = dockable.Id; }

    internal void NormalizeNodeIds()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in AllDockables())
            if (string.IsNullOrWhiteSpace(node.Id) || !seen.Add(node.Id))
            { node.Id = "coating:node:" + Guid.NewGuid().ToString("N"); seen.Add(node.Id); }
    }

    internal void RestoreActive(CoatingSavedLayout snapshot)
    {
        foreach (var dock in AllDockables().OfType<IDock>())
            if (snapshot.ActiveDockables.TryGetValue(dock.Id, out var id)
                && dock.VisibleDockables?.FirstOrDefault(d => d.Id == id) is { } active) dock.ActiveDockable = active;
        if (snapshot.ActivePage is { } pageId && AllDockables().OfType<IDocument>().FirstOrDefault(p => p.Id == pageId) is { } page)
        { SetActiveDockable(page); if (page.Owner is IDock owner) SetFocusedDockable(owner, page); }
    }

    internal void Open(string id)
    {
        var page = EnsurePage(id);
        if (!AllDockables().Contains(page))
        {
            var target = Walk(Root!, includeWindows: false).OfType<IDocumentDock>().FirstOrDefault(d => d.ActiveDockable is not null)
                ?? Walk(Root!, includeWindows: false).OfType<IDocumentDock>().First();
            AddDockable(target, page);
        }
        SetActiveDockable(page);
        if (page.Owner is IDock owner) SetFocusedDockable(owner, page);
        ActivateWindow(page);
    }

    public override void SplitToDock(IDock dock, IDockable dockable, DockOperation operation)
    {
        if (dockable is IDocumentDock group) group.Id = "coating:group:" + Guid.NewGuid().ToString("N");
        if (dock is not IDocumentDock || dockable is not IDocumentDock split
            || operation is not (DockOperation.Left or DockOperation.Right or DockOperation.Top or DockOperation.Bottom)
            || dock.Owner is not IDock { VisibleDockables: { } children } owner || !children.Contains(dock))
        { base.SplitToDock(dock, dockable, operation); return; }
        var index = children.IndexOf(dock); var layout = CreateSplitLayout(dock, split, operation);
        var wasDefault = ReferenceEquals(owner.DefaultDockable, dock);
        RemoveVisibleDockableAt(owner, index); OnDockableRemoved(split); OnDockableUndocked(split, operation);
        InsertVisibleDockable(owner, index, layout); OnDockableAdded(split); owner.ActiveDockable = layout;
        if (wasDefault) owner.DefaultDockable = layout;
        InitDockable(layout, owner); OnDockableDocked(split, operation);
    }

    private void RepairMainArea()
    {
        if (Resetting || Root is null) return;
        var groups = Walk(Root, includeWindows: false).OfType<IDocumentDock>().ToArray();
        var replacement = groups.FirstOrDefault(d => d.VisibleDockables is { Count: > 0 });
        if (replacement is null) return; // Keep an empty drop target when every page is floating/closed.
        foreach (var empty in groups.Where(d => d.VisibleDockables is { Count: 0 }))
        { empty.IsCollapsable = true; CollapseDock(empty); }
        replacement.IsCollapsable = false;
    }

    public override void OnDockableDocked(IDockable? dockable, DockOperation operation)
    { base.OnDockableDocked(dockable, operation); Dispatcher.UIThread.Post(RepairMainArea); }
    public override void OnDockableClosed(IDockable? dockable)
    { base.OnDockableClosed(dockable); Dispatcher.UIThread.Post(RepairMainArea); }
    public override void OnWindowAdded(IDockWindow? window)
    { base.OnWindowAdded(window); Dispatcher.UIThread.Post(RepairMainArea); }
    internal void CloseFloatingWindows()
    {
        foreach (var window in Root?.Windows?.ToArray() ?? []) CloseWindow(window);
    }

    internal void ValidateLayout(IRootDock root)
    {
        var documents = new HashSet<string>(StringComparer.Ordinal);
        var structural = new HashSet<IDockable>(ReferenceEqualityComparer.Instance);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var windows = 0;
        void Visit(IDockable node, int depth)
        {
            if (depth > 16 || !structural.Add(node) || structural.Count > 64 || !ids.Add(node.Id))
                throw new InvalidDataException("镀膜布局节点重复或超出规模限制。");
            if (node is IDocument)
            {
                if (!_titles.ContainsKey(node.Id) || !documents.Add(node.Id)) throw new InvalidDataException("镀膜布局包含未知或重复面板。");
            }
            else if (node is not (RootDock or ProportionalDock or DocumentDock or ProportionalDockSplitter))
                throw new InvalidDataException("镀膜布局包含不支持的节点。");
            if (node is IDock { VisibleDockables: { } children })
            {
                foreach (var child in children) Visit(child, depth + 1);
            }
            if (node is IRootDock r)
            {
                if (new[] { r.HiddenDockables, r.LeftPinnedDockables, r.RightPinnedDockables, r.TopPinnedDockables, r.BottomPinnedDockables }
                    .Any(c => c is { Count: > 0 })) throw new InvalidDataException("镀膜布局不支持隐藏或自动收起的工具节点。");
                foreach (var window in r.Windows ?? [])
                {
                    if (++windows > 4 || window.Layout is null) throw new InvalidDataException("镀膜浮窗数量无效。");
                    Visit(window.Layout, depth + 1);
                }
            }
        }
        Visit(root, 0);
        if (!Walk(root, includeWindows: false).OfType<IDocumentDock>().Any())
            throw new InvalidDataException("镀膜布局缺少主窗口停靠区域。");
    }

    internal void ValidateActive(IRootDock root, CoatingSavedLayout snapshot)
    {
        var nodes = Walk(root, includeWindows: true).ToDictionary(d => d.Id, StringComparer.Ordinal);
        foreach (var (id, child) in snapshot.ActiveDockables)
            if (!nodes.TryGetValue(id, out var node) || node is not IDock dock || dock.VisibleDockables?.All(d => d.Id != child) != false)
                throw new InvalidDataException("镀膜布局的活动标签引用无效。");
        if (snapshot.ActivePage is { } active && (!nodes.TryGetValue(active, out var page) || page is not IDocument))
            throw new InvalidDataException("镀膜布局的当前面板引用无效。");
    }

    internal void Adopt(IRootDock root)
    {
        foreach (var page in Pages.Values) page.Owner = null;
        foreach (var node in Walk(root, includeWindows: true))
        {
            // Dock may temporarily collapse a restored group before its retained content attaches.
            // Its expansion must use the saved live proportion rather than an older collapse cache.
            if (node is IDock { VisibleDockables.Count: > 0 } sized && double.IsFinite(sized.Proportion))
                sized.CollapsedProportion = sized.Proportion;
            if (node is Document page)
            {
                var retained = EnsurePage(page.Id);
                page.Context = retained.Context; page.Title = UiLocalization.Text(_titles[page.Id]);
                page.DockGroup = _group; page.CanClose = true; page.CanDrag = true; page.CanFloat = true; page.CanPin = false;
                Pages[page.Id] = page;
            }
            if (node is IDocumentDock group)
            {
                group.DockGroup = _group; group.CanCreateDocument = false; group.EnableWindowDrag = true;
                group.MinWidth = 300; group.MinHeight = 240; group.CloseButtonShowMode = DocumentCloseButtonShowMode.Always;
            }
            if (node is IRootDock r) r.DockGroup = _group + ":root";
        }
        Root = root;
    }
}
