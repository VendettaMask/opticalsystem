using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.NonSequential;
using BoxParameters = OpticalSystemDesign.Application.Contracts.BoxParameters;
using CylinderParameters = OpticalSystemDesign.Application.Contracts.CylinderParameters;
using MeshObjectParameters = OpticalSystemDesign.Application.Contracts.MeshObjectParameters;
using NonSequentialObjectKind = OpticalSystemDesign.Application.Contracts.NonSequentialObjectKind;
using PlaneRectangleParameters = OpticalSystemDesign.Application.Contracts.PlaneRectangleParameters;
using SourceParameters = OpticalSystemDesign.Application.Contracts.SourceParameters;
using SphereParameters = OpticalSystemDesign.Application.Contracts.SphereParameters;
using StandardLensParameters = OpticalSystemDesign.Application.Contracts.StandardLensParameters;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class NonSequentialObjectEditorPanel
{
    private readonly IVisualizationService? _visualization;
    private readonly INonSequentialAnalysisService? _analysis;
    private readonly IOpticalDocumentService? _documents;
    private readonly TreeView _tree = new() { Name = "nsc-tree", SelectionMode = SelectionMode.Multiple, AutoScrollToSelectedItem = true };
    private readonly TextBox _search = new() { Name = "nsc-search", PlaceholderText = "搜索名称、材料或编号…" };
    private readonly TextBlock _treeStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _scope = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _taskStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _propertyTitle = new() { Text = "对象属性", FontSize = DisplayTypography.CardTitle, FontWeight = FontWeight.SemiBold };
    private readonly OpticSceneControl _model = new()
    {
        Name = "nsc-model",
        ViewMode = OpticSceneViewMode.ThreeDimensional,
        RenderMode = OpticSceneRenderMode.Transparent,
        FitObjectBounds = true,
        ShowRays = false
    };
    private readonly HashSet<Guid> _selectedIds = [];
    private readonly HashSet<string> _expandedNodes = ["structure", "optical", "simulation", "sets"];
    private readonly Dictionary<string, TreeViewItem> _treeItems = [];
    private readonly Dictionary<string, ToggleButton> _navigationButtons = [];
    private readonly Dictionary<string, Control> _batchFields = [];
    private readonly HashSet<string> _batchDirty = [];
    private StackPanel _singleCommon = null!;
    private Grid _workspace = null!, _center = null!;
    private Control _inspector = null!;
    private ToggleButton _modelTab = null!, _tableTab = null!;
    private NonSequentialDocumentDto? _document;
    private NavigationNode? _navigationRoot;
    private SceneDto? _fullScene;
    private HashSet<Guid>? _isolatedIds;
    private CancellationTokenSource? _sceneCancellation;
    private string _navigation = "structure";
    private bool _synchronizingSelection;
    private bool? _narrowWorkspace;
    private Guid[]? _tableActivationSelection;
    internal Task SceneRefresh { get; private set; } = Task.CompletedTask;
    internal IReadOnlySet<Guid> SelectedObjectIds => _selectedIds;
    internal int MaterializedNodeCount => _treeItems.Count;

    private Control BuildWorkspace(Control inspector)
    {
        _inspector = inspector;
        _workspace = new Grid { ColumnDefinitions = new ColumnDefinitions("272,5,*,5,320"), RowDefinitions = new RowDefinitions("*,0,0") };
        _workspace.ColumnDefinitions[0].MinWidth = 180;
        _workspace.ColumnDefinitions[2].MinWidth = 220;
        _workspace.ColumnDefinitions[4].MinWidth = 240;
        _workspace.Children.Add(BuildNavigator());
        var split = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(split, 1); _workspace.Children.Add(split);
        _center = BuildModelArea(); Grid.SetColumn(_center, 2); _workspace.Children.Add(_center);
        var split2 = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(split2, 3); _workspace.Children.Add(split2);
        Grid.SetColumn(inspector, 4); _workspace.Children.Add(inspector);
        var rowSplit = new GridSplitter
        {
            Height = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Rows,
            IsVisible = false
        };
        Grid.SetRow(rowSplit, 1); Grid.SetColumnSpan(rowSplit, 5); _workspace.Children.Add(rowSplit);
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 1080;
            if (_narrowWorkspace == narrow) return;
            _narrowWorkspace = narrow;
            _workspace.ColumnDefinitions[4].Width = narrow ? new GridLength(0) : new GridLength(320);
            _workspace.ColumnDefinitions[4].MinWidth = narrow ? 0 : 240;
            _workspace.ColumnDefinitions[3].Width = narrow ? new GridLength(0) : new GridLength(6);
            split2.IsVisible = !narrow;
            rowSplit.IsVisible = narrow; _workspace.RowDefinitions[1].Height = new GridLength(narrow ? 6 : 0);
            _workspace.RowDefinitions[2].Height = narrow ? new GridLength(280) : new GridLength(0);
            Grid.SetColumn(inspector, narrow ? 0 : 4); Grid.SetColumnSpan(inspector, narrow ? 5 : 1);
            Grid.SetRow(inspector, narrow ? 2 : 0);
        };
        _model.ObjectPicked += (_, e) =>
        {
            if (_document?.Objects.FirstOrDefault(o => o.ObjectNumber == e.Number) is not { } row) return;
            var ids = e.Toggle ? _selectedIds.ToHashSet() : new HashSet<Guid>();
            if (!ids.Add(row.Id)) ids.Remove(row.Id);
            SelectObjects(ids);
        };
        return _workspace;
    }

    private Control BuildNavigator()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(12, 10) };
        var tabs = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 4 };
        foreach (var (key, label) in new[] { ("structure", "场景结构"), ("optical", "光学"), ("simulation", "仿真") })
        {
            var button = UiLocalization.Preserve(new ToggleButton
            {
                Name = "nsc-nav-" + key,
                IsChecked = key == _navigation,
                Padding = new Thickness(8, 5),
                FontSize = DisplayTypography.BodySmall
            });
            button.BorderThickness = new Thickness(0); button.Margin = new Thickness(1, 0);
            UiLocalization.BindLanguage(button, () => button.Content = UiLocalization.Text(label));
            button.Click += (_, _) => SetNavigation(key); _navigationButtons[key] = button; tabs.Children.Add(button);
        }
        root.Children.Add(tabs);
        _search.Margin = new Thickness(0, 10, 0, 6); Grid.SetRow(_search, 1); root.Children.Add(_search);
        _search.TextChanged += (_, _) => RebuildNavigation();
        _treeStatus.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
        _treeStatus.FontSize = DisplayTypography.BodySmall; _treeStatus.Margin = new Thickness(2, 2, 2, 6);
        Grid.SetRow(_treeStatus, 2); root.Children.Add(_treeStatus);
        Grid.SetRow(_tree, 3); root.Children.Add(_tree);
        _tree.SelectionChanged += (_, _) =>
        {
            if (_synchronizingSelection) return;
            var nodes = _tree.SelectedItems?.OfType<TreeViewItem>().Select(i => i.Tag).OfType<NavigationNode>().ToArray() ?? [];
            var ids = nodes.SelectMany(n => n.ObjectIds()).Distinct().ToArray();
            if (ids.Length > 0) SelectObjects(ids, false);
            else if (nodes.FirstOrDefault()?.Command is { } command) ShowTaskProperties(command);
            else SelectObjects([], false);
        };
        var organize = new Button { Content = new LocalIconLabel("layers", "整理"), HorizontalAlignment = HorizontalAlignment.Left };
        QuietCommand(organize);
        var group = WorkspaceCommand("layers", "新建分组", async () => await CreateFolderAsync());
        var save = WorkspaceCommand("copy", "保存选择集", async () => await SaveSelectionAsync());
        organize.Flyout = new Flyout { Content = new StackPanel { Spacing = 6, Children = { group, save } } };
        Grid.SetRow(organize, 4); root.Children.Add(organize);
        return WorkspaceSurface(root);
    }

    private Grid BuildModelArea()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        _modelTab = new ToggleButton { Name = "nsc-model-tab", Content = "三维模型", IsChecked = _visualization is not null };
        _tableTab = new ToggleButton { Name = "nsc-table-tab", Content = "对象表", IsChecked = _visualization is null };
        _modelTab.IsEnabled = _visualization is not null;
        foreach (var tab in new[] { _modelTab, _tableTab }) { tab.BorderThickness = new Thickness(0); tab.Padding = new Thickness(12, 5); }
        _modelTab.Click += (_, _) => ShowObjectTable(false); _tableTab.Click += (_, _) => ShowObjectTable(true);
        var rays = new CheckBox { Content = "显示光线" };
        rays.IsCheckedChanged += (_, _) => { _model.ShowRays = rays.IsChecked == true; _model.InvalidateVisual(); };
        var modes = new[] { new WorkspaceChoice<OpticSceneRenderMode>(OpticSceneRenderMode.Solid, "实体"),
            new(OpticSceneRenderMode.Transparent, "透明"), new(OpticSceneRenderMode.Wireframe, "线框") };
        var mode = new ComboBox { ItemsSource = modes, SelectedItem = modes[1], MinWidth = 95 };
        mode.SelectionChanged += (_, _) => { if (mode.SelectedItem is WorkspaceChoice<OpticSceneRenderMode> selected) _model.RenderMode = selected.Value; };
        var presets = new[] { new WorkspaceChoice<OpticSceneViewPreset>(OpticSceneViewPreset.Isometric, "等轴测视图"),
            new(OpticSceneViewPreset.Front, "前视图"), new(OpticSceneViewPreset.Back, "后视图"), new(OpticSceneViewPreset.Left, "左视图"),
            new(OpticSceneViewPreset.Right, "右视图"), new(OpticSceneViewPreset.Top, "俯视图"), new(OpticSceneViewPreset.Bottom, "仰视图") };
        var view = new ComboBox { ItemsSource = presets, SelectedItem = presets[0], MinWidth = 100 };
        view.SelectionChanged += (_, _) => { if (view.SelectedItem is WorkspaceChoice<OpticSceneViewPreset> selected) _model.SetViewPreset(selected.Value); };
        var settings = WorkspaceCommand("system-settings", "视图设置", () => { }); settings.Name = "nsc-view-settings";
        QuietCommand(settings);
        settings.Flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
        { PropertyLine("显示方式", mode), PropertyLine("视角", view), rays,
            WorkspaceCommand("layout-3d", "准备布局光线", async () => await PrepareLayoutAsync()) }
            }
        };
        var tools = new DockPanel { Margin = new Thickness(10, 6), LastChildFill = true };
        var viewCommands = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
        { WorkspaceIcon("action-search", "定位", FocusSelection), WorkspaceIcon("layers", "隔离所选", IsolateSelection),
            WorkspaceIcon("action-reset-view", "显示整机", ShowAllObjects), settings }
        };
        DockPanel.SetDock(viewCommands, Avalonia.Controls.Dock.Right); tools.Children.Add(viewCommands);
        tools.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _modelTab, _tableTab } });
        root.Children.Add(WorkspaceSurface(tools));
        var views = new Grid(); views.Children.Add(_model); views.Children.Add(_grid);
        _grid.IsVisible = _visualization is null; _model.IsVisible = _visualization is not null;
        Grid.SetRow(views, 1); root.Children.Add(views);
        var taskMenu = WorkspaceCommand("ray-path-analysis", "任务", () => { }); QuietCommand(taskMenu);
        taskMenu.Flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
        { WorkspaceCommand("layout-3d", "准备布局光线", async () => await PrepareLayoutAsync()),
            WorkspaceCommand("system-settings", "追迹设置", OpenTrace),
            WorkspaceCommand("ray-path-analysis", "光线路径", OpenPaths) }
            }
        };
        var status = new DockPanel { Margin = new Thickness(12, 3), LastChildFill = true };
        DockPanel.SetDock(taskMenu, Avalonia.Controls.Dock.Right); status.Children.Add(taskMenu);
        _taskStatus.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
        _scope.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
        foreach (var text in new[] { _scope, _taskStatus }) { text.FontSize = DisplayTypography.BodySmall; text.VerticalAlignment = VerticalAlignment.Center; }
        status.Children.Add(new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _scope,
            new TextBlock { Text = " · ", VerticalAlignment = VerticalAlignment.Center }, _taskStatus }
        });
        var taskSurface = WorkspaceSurface(status); Grid.SetRow(taskSurface, 2); root.Children.Add(taskSurface);
        return root;
    }
    private static void PaginateNavigation(NavigationNode root)
    {
        var pending = new Stack<NavigationNode>(); pending.Push(root);
        while (pending.TryPop(out var node))
        {
            foreach (var child in node.Children) pending.Push(child);
            if (node.Children.Count <= 128) continue;
            var children = node.Children.ToArray(); node.Children.Clear();
            for (var i = 0; i < children.Length; i += 128)
                node.Children.Add(new NavigationNode(node.Key + "-page-" + i,
                    UiLocalization.Format("条目 {0}–{1}", i + 1, Math.Min(children.Length, i + 128)), "layers")
                { Children = children.Skip(i).Take(128).ToList(), IsPage = true });
        }
    }

    private static Border WorkspaceSurface(Control content)
    {
        var border = new Border { Child = content, BorderThickness = new Thickness(0, 0, 1, 1) };
        border.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        border.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return border;
    }
    private Button WorkspaceCommand(string icon, string text, Action action)
    {
        var button = new Button { Content = new LocalIconLabel(icon, text), Margin = new Thickness(2), Padding = new Thickness(8, 4) };
        UiLocalization.BindLanguage(button, () => AutomationProperties.SetName(button, UiLocalization.Text(text)));
        button.Click += (_, _) => { try { action(); } catch (Exception e) { _summary.Text = e.Message; } };
        return button;
    }
    private Button WorkspaceCommand(string icon, string text, Func<Task> action)
    {
        var button = WorkspaceCommand(icon, text, () => { });
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception e) { if (!_disposed) _summary.Text = e.Message; }
            finally { if (!_disposed) button.IsEnabled = true; }
        };
        return button;
    }
    private static void QuietCommand(Button button)
    {
        button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0);
        button.Padding = new Thickness(8, 5); button.Margin = new Thickness(2, 0);
    }
    private static Button QuietIcon(Button button, string icon, string text)
    {
        QuietCommand(button); button.Content = new LocalIcon { IconName = icon, Width = 18, Height = 18 };
        button.MinWidth = 32;
        UiLocalization.BindLanguage(button, () =>
        { ToolTip.SetTip(button, UiLocalization.Text(text)); AutomationProperties.SetName(button, UiLocalization.Text(text)); });
        return button;
    }
    private Button WorkspaceIcon(string icon, string text, Action action) => QuietIcon(WorkspaceCommand(icon, text, action), icon, text);
    private static Border ToolbarDivider()
    {
        var divider = new Border { Width = 1, Height = 20, Margin = new Thickness(6, 3) };
        divider.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Border); return divider;
    }
    private static Grid PropertyLine(string label, Control editor)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("74,*"), ColumnSpacing = 6 };
        var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        caption.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
        row.Children.Add(caption); Grid.SetColumn(editor, 1); row.Children.Add(editor); return row;
    }
    private void GroupPlacementFields()
    {
        var enabled = _fields["objectEnabled"];
        if (enabled.Parent is Grid enabledRow)
        { _parameters.Children.Remove(enabledRow); enabledRow.Children.Remove(enabled); }
        ((CheckBox)enabled).Content = UiLocalization.Text("参与追迹");
        _visible.Content = UiLocalization.Text("显示");
        if (_visible.Parent is Panel oldParent) oldParent.Children.Remove(_visible);
        _parameters.Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 14, Children = { _visible, enabled } });
        var pose = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*,44,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), ColumnSpacing = 6, RowSpacing = 5 };
        for (var i = 0; i < 3; i++)
        {
            var axis = new[] { "X", "Y", "Z" }[i];
            var position = _fields["object" + axis]; var tilt = _fields["objectTilt" + axis];
            foreach (var control in new[] { position, tilt })
            {
                var old = (Grid)control.Parent!; _parameters.Children.Remove(old); old.Children.Remove(control); control.MinWidth = 0;
            }
            var axisText = new TextBlock { Text = axis, VerticalAlignment = VerticalAlignment.Center };
            var tiltText = new TextBlock { Text = UiLocalization.Text("倾斜 " + axis), VerticalAlignment = VerticalAlignment.Center };
            axisText.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
            tiltText.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
            Grid.SetRow(axisText, i); pose.Children.Add(axisText);
            Grid.SetColumn(position, 1); Grid.SetRow(position, i); pose.Children.Add(position);
            Grid.SetColumn(tiltText, 2); Grid.SetRow(tiltText, i); pose.Children.Add(tiltText);
            Grid.SetColumn(tilt, 3); Grid.SetRow(tilt, i); pose.Children.Add(tilt);
        }
        _parameters.Children.Add(new Expander
        {
            Header = "位置 (mm) / 倾斜 (°)",
            IsExpanded = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = pose
        });
    }
    private void GroupParameterFields(int start, string title)
    {
        var children = _parameters.Children.Skip(start).ToArray(); if (children.Length == 0) return;
        var content = new StackPanel { Spacing = 6 };
        foreach (var child in children) { _parameters.Children.Remove(child); content.Children.Add(child); }
        _parameters.Children.Add(new Expander
        {
            Header = title,
            IsExpanded = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = content
        });
    }
    private void TableSelectionChanged()
    {
        if (_loading || _synchronizingSelection || !_grid.IsVisible || _tableActivationSelection is not null) return;
        SelectObjects(_grid.SelectedItems.OfType<ObjectRow>().Select(o => o.Id).ToArray());
    }
    private ObjectRow? CurrentSelectionRow() => (_grid.ItemsSource as IEnumerable<ObjectRow> ?? [])
        .FirstOrDefault(row => _selectedIds.Contains(row.Id));
    internal void SelectObjects(IEnumerable<Guid> ids, bool updateTree = true)
    {
        if (_document is null || _synchronizingSelection) return;
        var valid = _document.Objects.Select(o => o.Id).ToHashSet();
        var requested = ids.Where(valid.Contains).ToArray();
        _synchronizingSelection = true;
        try
        {
            _selectedIds.Clear(); _selectedIds.UnionWith(requested);
            _grid.SelectedItems.Clear();
            foreach (var row in (_grid.ItemsSource as IEnumerable<ObjectRow> ?? []).Where(o => _selectedIds.Contains(o.Id))) _grid.SelectedItems.Add(row);
            if (_grid.SelectedItems.Count > 0) _grid.ScrollIntoView(_grid.SelectedItems[0], null);
            if (updateTree)
            {
                _tree.SelectedItems?.Clear();
                if (_navigationRoot is not null)
                    foreach (var id in _selectedIds)
                        if (FindObjectItem(_navigationRoot, id) is { } item) _tree.SelectedItems?.Add(item);
            }
            var numbers = _document.Objects.Where(o => _selectedIds.Contains(o.Id)).Select(o => o.ObjectNumber).ToHashSet();
            _model.HighlightedObjectNumbers = numbers;
            _model.HighlightedSurfaceNumber = numbers.Count == 1 ? numbers.First() : null;
            _model.InvalidateVisual();
        }
        finally { _synchronizingSelection = false; }
        LoadSelection();
    }
    internal void ShowObjectTable(bool visible)
    {
        var ids = _selectedIds.ToArray();
        RestoreTableSelectionAfterLayout(ids);
        _grid.IsVisible = visible; _model.IsVisible = !visible; _modelTab.IsChecked = !visible; _tableTab.IsChecked = visible;
    }
    private void RestoreTableSelectionAfterLayout(Guid[] ids)
    {
        _tableActivationSelection = ids;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !ReferenceEquals(_tableActivationSelection, ids)) return;
            _tableActivationSelection = null; SelectObjects(ids);
        }, DispatcherPriority.Background);
    }
    internal void SetNavigation(string key)
    {
        _navigation = key;
        foreach (var (id, button) in _navigationButtons) button.IsChecked = id == key;
        RebuildNavigation();
    }

    private NavigationNode ObjectNode(NonSequentialObjectRowDto row) => ObjectNode(row, "");
    private NavigationNode ObjectNode(NonSequentialObjectRowDto row, string prefix)
    {
        var icon = row.Parameters is SourceParameters ? "source-analysis"
            : row.Kind == NonSequentialObjectKind.Null ? "move"
            : row.Kind == NonSequentialObjectKind.DetectorRectangle ? "detector-view" : "solid-model";
        var state = !row.Enabled ? " · " + UiLocalization.Text("不参与追迹") : !row.Visible ? " · " + UiLocalization.Text("隐藏") : "";
        var node = new NavigationNode(prefix + "object-" + row.Id, $"{row.ObjectNumber}  {row.Name}{state}", icon, row.Id);
        if (row.Parameters is StandardLensParameters)
        {
            node.Children.Add(new(node.Key + "-front", UiLocalization.Text("前表面"), "report-surface-data", row.Id));
            node.Children.Add(new(node.Key + "-back", UiLocalization.Text("后表面"), "report-surface-data", row.Id));
        }
        return node;
    }
    private NavigationNode BuildNavigation()
    {
        var doc = _document!; var organization = doc.Organization ?? NonSequentialOrganization.Empty;
        var root = new NavigationNode(_navigation, doc.Name, "nonsequential-mode");
        if (_navigation == "structure")
        {
            var folders = organization.Folders.ToDictionary(f => f.Id, f => new NavigationNode("folder-" + f.Id, f.Name, "layers") { FolderId = f.Id });
            foreach (var f in organization.Folders) (f.ParentId is { } parent ? folders[parent] : root).Children.Add(folders[f.Id]);
            var objects = doc.Objects.ToDictionary(o => o.Id, ObjectNode);
            var assigned = organization.Folders.SelectMany(f => f.ObjectIds.Select(id => (id, f.Id))).ToDictionary(p => p.id, p => p.Id);
            foreach (var row in doc.Objects)
            {
                var parent = assigned.TryGetValue(row.Id, out var folder) ? folders[folder]
                    : row.ReferenceObjectId is { } reference && objects.TryGetValue(reference, out var referenceNode) ? referenceNode : root;
                parent.Children.Add(objects[row.Id]);
            }
        }
        else if (_navigation == "optical")
        {
            void Group(string key, string label, string icon, IEnumerable<NonSequentialObjectRowDto> rows)
            {
                var group = new NavigationNode(key, UiLocalization.Text(label), icon);
                group.Children.AddRange(rows.Select(ObjectNode)); if (group.Children.Count > 0) root.Children.Add(group);
            }
            Group("sources", "光源", "source-analysis", doc.Objects.Where(o => o.Parameters is SourceParameters));
            var materials = new NavigationNode("materials", UiLocalization.Text("材料"), "materials-library");
            foreach (var g in doc.Objects.Where(o => o.Parameters is not SourceParameters && o.Kind is not (NonSequentialObjectKind.Null or NonSequentialObjectKind.DetectorRectangle))
                .GroupBy(o => o.Material).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var material = new NavigationNode("material-" + g.Key, g.Key, "materials-library"); material.Children.AddRange(g.Select(ObjectNode)); materials.Children.Add(material);
            }
            root.Children.Add(materials);
            Group("detectors", "探测器", "detector-view", doc.Objects.Where(o => o.Kind == NonSequentialObjectKind.DetectorRectangle));
            Group("references", "坐标参考", "move", doc.Objects.Where(o => o.Kind == NonSequentialObjectKind.Null));
        }
        else
        {
            foreach (var (key, text, icon) in new[] { ("settings", "追迹设置", "system-settings"), ("layout", "布局光线", "layout-3d"),
                ("trace", "光线追迹", "action-run"), ("detectors", "探测器", "detector-view"), ("paths", "光线路径", "ray-path-analysis"), ("design", "优化与公差", "system-settings") })
                root.Children.Add(new NavigationNode("task-" + key, UiLocalization.Text(text), icon) { Command = key });
        }
        var sets = new NavigationNode("sets", UiLocalization.Text("选择集"), "copy");
        foreach (var set in organization.SelectionSets)
        {
            var item = new NavigationNode("set-" + set.Id, set.Name, "layers") { SelectionSetId = set.Id };
            item.Children.AddRange(doc.Objects.Where(o => set.ObjectIds.Contains(o.Id)).Select(o => ObjectNode(o, "set-" + set.Id + "-"))); sets.Children.Add(item);
        }
        if (sets.Children.Count > 0) root.Children.Add(sets);
        PaginateNavigation(root);
        return root;
    }
    private NavigationNode? FilterNode(NavigationNode node, HashSet<Guid> matches)
    {
        if (string.IsNullOrWhiteSpace(_search.Text)) return node;
        var children = node.Children.Select(c => FilterNode(c, matches)).OfType<NavigationNode>().ToList();
        if (!(node.ObjectId is { } id && matches.Contains(id)) && children.Count == 0) return null;
        var filtered = node with { Children = children }; return filtered;
    }
    private void RebuildNavigation()
    {
        if (_document is null) return;
        var query = _search.Text?.Trim() ?? "";
        var matches = _document.Objects.Where(o => query.Length == 0 || o.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || o.Material.Contains(query, StringComparison.OrdinalIgnoreCase) || o.ObjectNumber.ToString(CultureInfo.InvariantCulture) == query).Select(o => o.Id).ToHashSet();
        _synchronizingSelection = true;
        try
        {
            _tree.Items.Clear(); _treeItems.Clear(); _navigationRoot = FilterNode(BuildNavigation(), matches);
            if (_navigationRoot is not null) _tree.Items.Add(CreateTreeItem(_navigationRoot));
            _treeStatus.Text = query.Length == 0 ? UiLocalization.Format("{0} 个对象", _document.Objects.Count)
                : UiLocalization.Format("{0} 个匹配", matches.Count);
        }
        finally { _synchronizingSelection = false; }
        SelectObjects(_selectedIds.ToArray());
    }
    private TreeViewItem CreateTreeItem(NavigationNode node)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
        { new LocalIcon { IconName = node.Icon, Width = 16, Height = 16 }, UiLocalization.Preserve(new TextBlock { Text = node.Label, VerticalAlignment = VerticalAlignment.Center }) }
        };
        var item = new TreeViewItem
        {
            Tag = node,
            Header = header,
            IsExpanded = _expandedNodes.Contains(node.Key) || !node.IsPage && !string.IsNullOrWhiteSpace(_search.Text),
            Padding = new Thickness(2, 4)
        };
        AutomationProperties.SetAutomationId(item, "nsc-node-" + node.Key);
        AutomationProperties.SetName(item, node.Label);
        _treeItems[node.Key] = item;
        void Populate()
        {
            if (item.Items.Count == 1 && item.Items[0] is string)
            { item.Items.Clear(); foreach (var child in node.Children) item.Items.Add(CreateTreeItem(child)); }
        }
        if (node.Children.Count > 0)
        {
            item.Items.Add("…"); if (item.IsExpanded) Populate();
            item.PropertyChanged += (_, e) =>
            {
                if (e.Property != TreeViewItem.IsExpandedProperty) return;
                if (item.IsExpanded) { _expandedNodes.Add(node.Key); Populate(); } else _expandedNodes.Remove(node.Key);
            };
        }
        item.ContextMenu = BuildNodeMenu(node);
        if (node.Command is not null) item.DoubleTapped += (_, e) => { RunTask(node.Command); e.Handled = true; };
        return item;
    }
    private TreeViewItem? FindObjectItem(NavigationNode node, Guid id)
    {
        // Selection sets are aliases; locate the primary structure/optical path first.
        if (node.Key == "sets" || !node.ObjectIds().Contains(id)) return null;
        if (!_treeItems.TryGetValue(node.Key, out var item)) return null;
        if (node.ObjectId == id) return item;
        item.IsExpanded = true;
        foreach (var child in node.Children) if (FindObjectItem(child, id) is { } found) return found;
        return null;
    }

    private ContextMenu BuildNodeMenu(NavigationNode node)
    {
        var menu = new ContextMenu();
        void Command(string text, Action action)
        {
            var item = new MenuItem { Header = text }; item.Click += (_, _) => { try { action(); } catch (Exception e) { _summary.Text = e.Message; } }; menu.Items.Add(item);
        }
        if (node.Command is { } task) { Command("打开", () => RunTask(task)); return menu; }
        Guid[] Targets() => node.ObjectId is { } id && _selectedIds.Contains(id) ? _selectedIds.ToArray() : node.ObjectIds().Distinct().ToArray();
        Command("定位", () => { SelectObjects(Targets()); FocusSelection(); });
        Command("隔离所选", () => { SelectObjects(Targets()); IsolateSelection(); });
        Command("显示", () => SetFlags(Targets(), visible: true)); Command("隐藏", () => SetFlags(Targets(), visible: false));
        Command("参与追迹", () => SetFlags(Targets(), enabled: true)); Command("不参与追迹", () => SetFlags(Targets(), enabled: false));
        var move = new MenuItem { Header = "移到分组" };
        foreach (var folder in (_document?.Organization ?? NonSequentialOrganization.Empty).Folders.Prepend(new(Guid.Empty, UiLocalization.Text("工程根目录"), null, [])))
        {
            var item = new MenuItem { Header = folder.Name };
            item.Click += (_, _) => MoveToFolder(Targets(), folder.Id == Guid.Empty ? null : folder.Id); move.Items.Add(item);
        }
        menu.Items.Add(move);
        if (node.FolderId is { } folderId)
        {
            Command("新建子分组", async () => await CreateFolderAsync(folderId));
            Command("重命名", async () => await RenameOrganizationAsync(folderId, false));
            Command("删除分组", () => DeleteFolder(folderId));
        }
        if (node.SelectionSetId is { } setId)
        {
            Command("重命名", async () => await RenameOrganizationAsync(setId, true));
            Command("删除选择集", () => UpdateOrganization(o => o with { SelectionSets = o.SelectionSets.Where(s => s.Id != setId).ToArray() }));
        }
        return menu;
    }
    private void UpdateOrganization(Func<NonSequentialOrganization, NonSequentialOrganization> change)
    {
        _service.UpdateOrganization(change(_service.GetDocument().Organization ?? NonSequentialOrganization.Empty)); Refresh();
    }
    internal void MoveToFolder(IEnumerable<Guid> ids, Guid? destination)
    {
        var selected = ids.ToHashSet();
        UpdateOrganization(o => o with
        {
            Folders = o.Folders.Select(f => f with
            { ObjectIds = f.ObjectIds.Where(id => !selected.Contains(id)).Concat(f.Id == destination ? selected : []).ToArray() }).ToArray()
        });
    }
    internal Guid AddFolder(string name, Guid? parent = null)
    {
        var id = Guid.NewGuid(); UpdateOrganization(o => o with { Folders = o.Folders.Append(new(id, name, parent, [])).ToArray() }); return id;
    }
    private async Task CreateFolderAsync(Guid? parent = null)
    {
        if (await PromptNameAsync("新建分组", "") is { } name) AddFolder(name, parent);
    }
    internal void SaveSelection(string name)
    {
        if (_selectedIds.Count == 0) return;
        var ids = _selectedIds.ToArray(); UpdateOrganization(o => o with { SelectionSets = o.SelectionSets.Append(new(Guid.NewGuid(), name, ids)).ToArray() });
    }
    private async Task SaveSelectionAsync()
    { if (_selectedIds.Count > 0 && await PromptNameAsync("保存选择集", "") is { } name) SaveSelection(name); }
    private void DeleteFolder(Guid id) => UpdateOrganization(o =>
    {
        var folder = o.Folders.Single(f => f.Id == id);
        return o with { Folders = o.Folders.Where(f => f.Id != id).Select(f => f.ParentId == id ? f with { ParentId = folder.ParentId } : f).ToArray() };
    });
    private async Task RenameOrganizationAsync(Guid id, bool selectionSet)
    {
        var o = _service.GetDocument().Organization!;
        var old = selectionSet ? o.SelectionSets.Single(s => s.Id == id).Name : o.Folders.Single(f => f.Id == id).Name;
        if (await PromptNameAsync("重命名", old) is not { } name) return;
        UpdateOrganization(o => selectionSet ? o with { SelectionSets = o.SelectionSets.Select(s => s.Id == id ? s with { Name = name } : s).ToArray() }
            : o with { Folders = o.Folders.Select(f => f.Id == id ? f with { Name = name } : f).ToArray() });
    }
    private async Task<string?> PromptNameAsync(string title, string value)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return null;
        var box = UiLocalization.Preserve(new TextBox { Text = value, MaxLength = 256, Name = "nsc-group-name" });
        var dialog = new Window { Title = title, Width = 380, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var ok = new Button { Content = "确定", IsDefault = true, IsEnabled = !string.IsNullOrWhiteSpace(value) };
        var cancel = new Button { Content = "取消", IsCancel = true }; cancel.Click += (_, _) => dialog.Close();
        box.TextChanged += (_, _) => ok.IsEnabled = !string.IsNullOrWhiteSpace(box.Text);
        ok.Click += (_, _) => dialog.Close(box.Text?.Trim());
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children = { box, new StackPanel
        { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { ok, cancel } } }
        };
        ThemeChrome.ApplyDialogDecoration(dialog); return await dialog.ShowDialog<string?>(owner);
    }

    private void RefreshWorkspace(NonSequentialDocumentDto document, bool preserve, Guid? select)
    {
        var ids = select is { } id ? new[] { id } : preserve && _selectedIds.Count > 0 ? _selectedIds.ToArray()
            : document.Objects.Take(1).Select(o => o.Id).ToArray();
        _document = document;
        if (TopLevel.GetTopLevel(this) is not null) RestoreTableSelectionAfterLayout(ids);
        if (!preserve) { _isolatedIds = null; _model.ResetView(); _search.Text = ""; }
        if (_isolatedIds is not null) { _isolatedIds.IntersectWith(document.Objects.Select(o => o.Id)); if (_isolatedIds.Count == 0) _isolatedIds = null; }
        _selectedIds.Clear(); _selectedIds.UnionWith(ids); RebuildNavigation();
        UpdateTaskState(); SceneRefresh = RefreshModelAsync();
    }
    private async Task RefreshModelAsync()
    {
        _sceneCancellation?.Cancel(); _sceneCancellation?.Dispose();
        var cancel = _sceneCancellation = new CancellationTokenSource();
        _fullScene = null; _model.Scene = null; _model.ClearObjectPicking(); _model.InvalidateVisual();
        if (_visualization is null) return;
        try
        {
            var scene = await _visualization.BuildSceneAsync(new VisualizationRequestDto(SceneDimension.ThreeDimensional), cancel.Token);
            if (_disposed || cancel.IsCancellationRequested || scene.SourceRevision != _events.Revision) return;
            _fullScene = scene; ApplyViewFilter();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!_disposed && !cancel.IsCancellationRequested) _summary.Text = e.Message; }
    }
    internal void IsolateSelection()
    {
        if (_selectedIds.Count == 0) return;
        _isolatedIds = _selectedIds.ToHashSet(); ApplyViewFilter(); _model.FitView();
    }
    internal void ShowAllObjects() { _isolatedIds = null; ApplyViewFilter(); _model.FitView(); }
    private void FocusSelection()
    {
        if (!_model.FocusObjects(_document?.Objects.Where(o => _selectedIds.Contains(o.Id)).Select(o => o.ObjectNumber).ToHashSet() ?? []))
            _summary.Text = UiLocalization.Text("所选对象当前不可见。");
    }
    private void ApplyViewFilter()
    {
        if (_fullScene?.ThreeDimensional is not { } scene || _document is null) return;
        var numbers = _isolatedIds is null ? null : _document.Objects.Where(o => _isolatedIds.Contains(o.Id)).Select(o => o.ObjectNumber).ToHashSet();
        var surfaces = numbers is null ? scene.Surfaces : scene.Surfaces.Where(s => numbers.Contains(s.SurfaceNumber)).ToArray();
        var rays = scene.Rays.Where(r => numbers is null || r.Segments.Any(s => s.TargetSurfaceNumber is int n && numbers.Contains(n))).Take(800).ToArray();
        _model.Scene = _fullScene with { ThreeDimensional = scene with { Surfaces = surfaces, Rays = rays } };
        _model.ClearObjectPicking(); _model.InvalidateVisual();
        _scope.Text = numbers is null ? UiLocalization.Format("整机 · {0} 个对象", _document.Objects.Count)
            : UiLocalization.Format("已隔离 {0} 个对象", numbers.Count);
    }
    private void SetFlags(IEnumerable<Guid> ids, bool? visible = null, bool? enabled = null)
    {
        var selected = ids.ToHashSet(); var updates = (_grid.ItemsSource as IEnumerable<ObjectRow> ?? []).Where(o => selected.Contains(o.Id))
            .Select(o => o.ToUpdate() with { Visible = visible ?? o.Visible, Enabled = enabled ?? o.Enabled }).ToArray();
        if (updates.Length > 0) _service.UpdateObjects(updates);
    }
    private void AnalysisChanged(object? sender, NonSequentialTraceSessionDto? session) => Dispatcher.UIThread.Post(() =>
    { if (_disposed) return; UpdateTaskState(); RebuildNavigation(); SceneRefresh = RefreshModelAsync(); });
    private void UpdateTaskState()
    {
        var session = _analysis?.GetCurrentSession();
        _taskStatus.Text = session is null ? UiLocalization.Text("分析尚未运行")
            : UiLocalization.Format("{0} 条分支 · {1} 段 · {2}", session.BranchCount, session.SegmentCount,
                UiLocalization.Text(session.IsStale ? "结果需更新" : "结果可用"));
    }
    private void ShowTaskProperties(string command)
    {
        SelectObjects([], false);
        _propertyTitle.Text = UiLocalization.Text("仿真");
        _singleCommon.IsVisible = false; _parameters.Children.Clear(); _fields.Clear();
        _parameters.Children.Add(WorkspaceCommand("action-run", "打开", () => RunTask(command)));
        _parameters.Children.Add(new TextBlock { Text = _taskStatus.Text, TextWrapping = TextWrapping.Wrap });
    }
    private void RunTask(string key)
    {
        switch (key)
        {
            case "settings": case "trace": OpenTrace(); break;
            case "layout": _ = PrepareLayoutAsync(); break;
            case "detectors": OpenDetectors(); break;
            case "paths": OpenPaths(); break;
            case "design": if (TopLevel.GetTopLevel(this) is Window owner) _ = new NonSequentialDesignWindow(_service).ShowDialog(owner); break;
        }
    }
    private void OpenTrace()
    { if (_analysis is not null && TopLevel.GetTopLevel(this) is Window owner) _ = new NonSequentialTraceControlWindow(_service, _analysis).ShowDialog(owner); }
    private void OpenSceneSettings()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var panel = new NonSequentialModePanel(_service, _events, _appSettings);
        var window = new Window
        {
            Title = "场景设置",
            Width = 390,
            Height = 900,
            MinWidth = 320,
            MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };
        window.Closed += (_, _) => panel.Dispose(); ThemeChrome.ApplyDialogDecoration(window);
        _ = window.ShowDialog(owner);
    }
    private async Task PrepareLayoutAsync()
    {
        if (_analysis is null) return;
        try { await _analysis.RefreshLayoutSessionAsync(); }
        catch (Exception e) { if (!_disposed) _summary.Text = e.Message; }
    }
    private void OpenDetectors()
    {
        if (_analysis is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var panel = new NonSequentialDetectorViewerPanel(_service, _analysis);
        var window = new Window { Title = "探测器", Width = 1100, Height = 760, Content = panel };
        window.Closed += (_, _) => panel.Dispose(); ThemeChrome.ApplyDialogDecoration(window); window.Show(owner);
    }
    private void OpenPaths()
    {
        if (_analysis is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var path = _analysis.GetCurrentSession()?.RayDatabasePath;
        if (string.IsNullOrWhiteSpace(path)) { _summary.Text = UiLocalization.Text("分析尚未运行"); return; }
        new NonSequentialRayDatabaseWindow(_analysis, path, true).Show(owner);
    }

    private void BuildBatchProperties()
    {
        _batchFields.Clear(); _batchDirty.Clear(); _singleCommon.IsVisible = false;
        _propertyTitle.Text = UiLocalization.Format("公共属性 · {0} 个对象", _selectedIds.Count);
        var rows = (_grid.ItemsSource as IEnumerable<ObjectRow> ?? []).Where(o => _selectedIds.Contains(o.Id)).ToArray();
        void Boolean(string key, string label, Func<ObjectRow, bool> read)
        {
            var values = rows.Select(read).Distinct().ToArray(); var check = new CheckBox { Content = label, IsThreeState = true, IsChecked = values.Length == 1 ? values[0] : null, Name = "nsc-batch-" + key };
            var initial = check.IsChecked;
            check.IsCheckedChanged += (_, _) => { if (!_loading) { if (check.IsChecked != initial) _batchDirty.Add(key); else _batchDirty.Remove(key); } }; _batchFields[key] = check; _parameters.Children.Add(check);
        }
        Boolean("visible", "显示", o => o.Visible); Boolean("enabled", "参与追迹", o => o.Enabled);
        void Text(string key, string label, IEnumerable<string> values)
        {
            var distinct = values.Distinct().ToArray(); var box = UiLocalization.Preserve(new TextBox
            { Name = "nsc-batch-" + key, Text = distinct.Length == 1 ? distinct[0] : "", PlaceholderText = distinct.Length == 1 ? "" : UiLocalization.Text("多个值") });
            var initial = box.Text;
            box.TextChanged += (_, _) => { if (!_loading) { if (box.Text != initial) _batchDirty.Add(key); else _batchDirty.Remove(key); } }; _batchFields[key] = box;
            _parameters.Children.Add(new StackPanel { Spacing = 3, Children = { new TextBlock { Text = label }, box } });
        }
        foreach (var (key, label, read) in new (string, string, Func<ObjectRow, double>)[]
        { ("x", "X (mm)", o => o.X), ("y", "Y (mm)", o => o.Y), ("z", "Z (mm)", o => o.Z),
            ("tx", "倾斜 X", o => o.TiltX), ("ty", "倾斜 Y", o => o.TiltY), ("tz", "倾斜 Z", o => o.TiltZ) })
            Text(key, UiLocalization.Text(label), rows.Select(o => read(o).ToString("G17", CultureInfo.InvariantCulture)));
        if (rows.All(o => SupportsMaterial(o.Parameters))) Text("material", UiLocalization.Text("材料"), rows.Select(o => o.Material));
        _parameters.Children.Add(new TextBlock { Text = UiLocalization.Text("仅应用已修改的字段。位置使用各对象的局部坐标。"), TextWrapping = TextWrapping.Wrap });
    }
    private static bool SupportsMaterial(Application.Contracts.NonSequentialObjectParameters p) => p is StandardLensParameters or SphereParameters or CylinderParameters or BoxParameters or MeshObjectParameters;
    private static Application.Contracts.NonSequentialObjectParameters WithMaterial(Application.Contracts.NonSequentialObjectParameters p, string material) => p switch
    { StandardLensParameters x => x with { Material = material }, SphereParameters x => x with { Material = material }, CylinderParameters x => x with { Material = material }, BoxParameters x => x with { Material = material }, MeshObjectParameters x => x with { Material = material }, _ => p };
    private void ApplyBatchProperties()
    {
        if (_batchDirty.Count == 0) return;
        try
        {
            double Number(string key, double old) => !_batchDirty.Contains(key) ? old
                : double.TryParse(((TextBox)_batchFields[key]).Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : throw new FormatException(UiLocalization.Text("请输入有效数值。"));
            bool Flag(string key, bool old) => !_batchDirty.Contains(key) ? old : ((CheckBox)_batchFields[key]).IsChecked ?? old;
            var updates = (_grid.ItemsSource as IEnumerable<ObjectRow> ?? []).Where(o => _selectedIds.Contains(o.Id)).Select(o => o.ToUpdate() with
            {
                Visible = Flag("visible", o.Visible),
                Enabled = Flag("enabled", o.Enabled),
                X = Number("x", o.X),
                Y = Number("y", o.Y),
                Z = Number("z", o.Z),
                TiltXDegrees = Number("tx", o.TiltX),
                TiltYDegrees = Number("ty", o.TiltY),
                TiltZDegrees = Number("tz", o.TiltZ),
                Parameters = _batchDirty.Contains("material") ? WithMaterial(o.Parameters, ((TextBox)_batchFields["material"]).Text?.Trim() ?? "") : o.Parameters
            }).ToArray();
            _service.UpdateObjects(updates); Refresh();
        }
        catch (Exception e) { _summary.Text = e.Message; }
    }
    private sealed record NavigationNode(string Key, string Label, string Icon, Guid? ObjectId = null)
    {
        public List<NavigationNode> Children { get; init; } = [];
        public Guid? FolderId { get; init; }
        public Guid? SelectionSetId { get; init; }
        public string? Command { get; init; }
        public bool IsPage { get; init; }
        public IEnumerable<Guid> ObjectIds()
        {
            var pending = new Stack<NavigationNode>(); pending.Push(this);
            while (pending.TryPop(out var node))
            {
                if (node.ObjectId is { } id) yield return id;
                foreach (var child in node.Children) pending.Push(child);
            }
        }
    }
    private sealed record WorkspaceChoice<T>(T Value, string Label)
    { public override string ToString() => UiLocalization.Text(Label); }
    private static string KindLabel(NonSequentialObjectKind kind) => UiLocalization.Text(kind switch
    {
        NonSequentialObjectKind.SourceRay => "单光线光源",
        NonSequentialObjectKind.SourcePoint => "点光源",
        NonSequentialObjectKind.SourceRectangle => "矩形面光源",
        NonSequentialObjectKind.SourceGaussian => "高斯光源",
        NonSequentialObjectKind.SourceEllipse => "椭圆面光源",
        NonSequentialObjectKind.SourceTwoAngle => "双角度光源",
        NonSequentialObjectKind.SourceRadial => "径向光源",
        NonSequentialObjectKind.SourceVolumeRectangle => "矩形体光源",
        NonSequentialObjectKind.SourceVolumeEllipse => "椭球体光源",
        NonSequentialObjectKind.SourceVolumeCylinder => "圆柱体光源",
        NonSequentialObjectKind.PlaneRectangle => "矩形平面",
        NonSequentialObjectKind.Sphere => "球体",
        NonSequentialObjectKind.Cylinder => "圆柱体",
        NonSequentialObjectKind.Box => "盒体",
        NonSequentialObjectKind.StandardLens => "标准透镜",
        NonSequentialObjectKind.Mesh => "网格",
        NonSequentialObjectKind.DetectorRectangle => "矩形探测器",
        NonSequentialObjectKind.Null => "坐标参考",
        NonSequentialObjectKind.OpticalAssembly => "光学组件",
        _ => kind.ToString()
    });
}
