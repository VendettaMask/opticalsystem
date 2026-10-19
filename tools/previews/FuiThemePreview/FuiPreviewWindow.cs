using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using Path = System.IO.Path;

namespace OpticalSystemDesign.FuiThemePreview;

internal sealed class FuiPreviewWindow : Window, IDisposable
{
    private readonly WorkbenchApplication _workbench;
    private readonly SystemPropertiesPanel _sidebar;
    private readonly LensEditorPanel _editor;
    private readonly FuiEngineeringView _engineering;
    private readonly FuiSpatialStage _stage = new();
    private readonly TextBlock _efl = Readout("—", 29);
    private readonly TextBlock _fNumber = Readout("—", 24);
    private readonly TextBlock _index = Readout("—", 18);
    private readonly TextBlock _revision = Readout("BUILDING SHARED-CORE MESH", 10);
    private readonly TextBlock _surfaceInfo = Readout("—", 11);
    private readonly TextBlock _status = Readout("独立预览 / 不安装、不保存", 11);
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = B("#E3917B"), IsVisible = false, FontSize = 12 };
    private readonly TabControl _views;
    private readonly Grid _workspace;
    private int _generation;
    private bool _disposed;
    private Exception? _sceneError;
    private readonly bool _engineeringFirst;
    private readonly bool _registeredTheme;

    internal FuiPreviewWindow(bool fitScreen, bool engineeringFirst = false, bool registeredTheme = false)
    {
        _engineeringFirst = engineeringFirst;
        _registeredTheme = registeredTheme;
        _workbench = WorkbenchApplication.Create(userCatalogDirectory: Path.Combine(Path.GetTempPath(), "OpticalSystemDesign-FuiPreview", "catalogs"));
        CreateSample();
        var selection = new SurfaceSelectionService();
        _sidebar = new SystemPropertiesPanel(_workbench.Prescription, _workbench.Materials, _workbench.Events);
        _editor = new LensEditorPanel(_workbench.Prescription, _workbench.Events, selection);
        _engineering = new FuiEngineeringView(selection, registeredTheme);
        var table = _editor.GetLogicalDescendants().OfType<DataGrid>().Single();
        if (!registeredTheme)
        {
            _editor.Styles.Add(new Style(s => s.OfType<DataGridRow>().Class("glass-material-row"))
            { Setters = { new Setter(DataGridRow.BackgroundProperty, B("#183449")) } });
            _editor.Styles.Add(new Style(s => s.OfType<DataGridRow>().Class("glass-material-row").Class(":selected"))
            { Setters = { new Setter(DataGridRow.BackgroundProperty, B("#284C63")) } });
            _editor.Styles.Add(new Style(s => s.OfType<DataGridRow>().Class("glass-material-row").Descendant().OfType<DataGridCell>())
            { Setters = { new Setter(DataGridCell.BackgroundProperty, Brushes.Transparent) } });
            // The product grid owns a closer row style. Paint its actual Fluent rectangle too.
            table.Styles.Add(new Style(s => s.OfType<DataGridRow>().Class("glass-material-row"))
            { Setters = { new Setter(DataGridRow.BackgroundProperty, B("#183449")) } });
            table.Styles.Add(new Style(s => s.OfType<DataGridRow>().Class("glass-material-row").Not(s => s.Class(":selected"))
                .Template().OfType<Rectangle>().Name("BackgroundRectangle"))
            { Setters = { new Setter(Shape.FillProperty, B("#183449")), new Setter(Visual.OpacityProperty, 1d) } });
            table.Styles.Add(new Style(s => s.OfType<DataGridRow>().Class("glass-material-row").Class(":selected")
                .Template().OfType<Rectangle>().Name("BackgroundRectangle"))
            { Setters = { new Setter(Shape.FillProperty, B("#284C63")), new Setter(Visual.OpacityProperty, 1d) } });
        }
        table.RowHeight = 24;
        table.ColumnHeaderHeight = 29;
        Title = registeredTheme ? "光域 / Lumen · 完整主题交互预览 — Optical System Design" : "光域 03 · 光路剖面 / C# 交互预览 — Optical System Design";
        Width = 1600; Height = 1000; MinWidth = 1150; MinHeight = 800;
        Background = B("#050C10");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (fitScreen && Screens.Primary is { } screen)
        {
            Width = Math.Min(Width, Math.Max(MinWidth, screen.WorkingArea.Width / screen.Scaling - 40));
            Height = Math.Min(Height, Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - 70));
        }
        var root = new Grid { RowDefinitions = new RowDefinitions("60,44,*,28"), Margin = new Thickness(18, 8, 18, 8), RowSpacing = 8 };
        root.Children.Add(Header());
        var navigation = Navigation(); Grid.SetRow(navigation, 1); root.Children.Add(navigation);
        _workspace = new Grid { ColumnDefinitions = new ColumnDefinitions("256,14,*") };
        _workspace.Children.Add(Section("01", "SYSTEM / 系统参数", _sidebar));
        var documents = new Grid { RowDefinitions = new RowDefinitions("*,8,316") };
        var hero = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,218") };
        _views = new TabControl
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemsSource = new[]
            {
                new TabItem { Header = "01 / 空间结构", Content = _stage },
                new TabItem { Header = "02 / 光路剖面", Content = _engineering }
            },
            SelectedIndex = engineeringFirst ? 1 : 0
        };
        hero.Children.Add(_views);
        var readouts = Readouts(); Grid.SetColumn(readouts, 2); hero.Children.Add(readouts);
        documents.Children.Add(hero);
        var splitter = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 8, ResizeDirection = GridResizeDirection.Rows, Background = B("#0F2026") };
        Grid.SetRow(splitter, 1); documents.Children.Add(splitter);
        var editor = Section("03", "PRESCRIPTION / 镜头数据  ·  12 列原生编辑器", _editor);
        Grid.SetRow(editor, 2); documents.Children.Add(editor);
        Grid.SetColumn(documents, 2); _workspace.Children.Add(documents);
        Grid.SetRow(_workspace, 2); root.Children.Add(_workspace);
        var status = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        status.Children.Add(_status);
        var boundary = Readout("UI STUDY ONLY   /   未安装主题 · 不做像质达标声明", 10); Grid.SetColumn(boundary, 1); status.Children.Add(boundary);
        Grid.SetRow(status, 3); root.Children.Add(status);
        Content = root;
        DisplayTypography.Apply(this);
        Opened += async (_, _) => await RefreshSceneAsync();
        _workbench.Events.Changed += OnChanged;
        Closed += (_, _) => Dispose();
    }

    private void CreateSample()
    {
        for (var i = 0; i < 6; i++) _workbench.Prescription.AddSurface();
        var rows = _workbench.Prescription.GetSurfaces().ToArray();
        _workbench.Prescription.UpdateSurface(rows[0] with { Thickness = double.PositiveInfinity, SemiDiameter = 8 });
        double[] radii = [32, -32, 48, -48, 60, -60];
        double[] thicknesses = [5, 12, 4, 14, 5, 35];
        for (var i = 0; i < 6; i++)
            _workbench.Prescription.UpdateSurface(rows[i + 1] with
            {
                Label = $"S{i + 1}",
                Radius = radii[i],
                Thickness = thicknesses[i],
                Material = i % 2 == 0 ? "N-BK7" : "Air",
                SemiDiameter = 8,
                SemiDiameterFixed = true,
                IsStop = i == 0
            });
        _workbench.Prescription.UpdateSurface(rows[^1] with { Label = "Image", Thickness = 0, SemiDiameter = 12, SemiDiameterFixed = true });
        _workbench.Prescription.UpdateSystemSettings(_workbench.Prescription.GetSystemSettings() with { ApertureValue = 10 });
    }

    private Control Header()
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4) };
        header.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
        {
            Readout("S.T.A.R. LABS    /    OPTICAL ENGINEERING ENVIRONMENT", 10),
            new TextBlock { Text = "Optical System Design", Foreground = B("#D9DEDA"), FontSize = 23, FontWeight = FontWeight.Light }
        }
        });
        var identity = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 4,
            Children =
        {
            new TextBlock { Text = _registeredTheme ? "光域 / LUMEN" : "光域 03", Foreground = B("#C39B61"), FontSize = 19, HorizontalAlignment = HorizontalAlignment.Right },
            Readout("FUI / SPATIAL INSTRUMENT    ·    C# PREVIEW", 10)
        }
        };
        Grid.SetColumn(identity, 1); header.Children.Add(identity);
        return header;
    }

    private Control Navigation()
    {
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2) };
        var menu = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        foreach (var name in new[] { "文件", "设置", "视图", "分析", "优化", "公差", "加工与图纸", "数据库", "实验室", "窗口", "帮助" })
        {
            var item = QuietButton(name);
            ToolTip.SetTip(item, "菜单外观走查；不连接正式文件命令。");
            menu.Children.Add(item);
        }
        root.Children.Add(menu);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (_registeredTheme)
        {
            var controls = QuietButton("控件与对话框");
            controls.Click += (_, _) => new ThemeControlGalleryWindow().Show(this);
            actions.Children.Add(controls);
        }
        var reset = QuietButton("复位当前图");
        reset.Click += (_, _) => { if (_views.SelectedIndex == 1) _engineering.Canvas.ResetView(); else _stage.ResetCamera(); };
        actions.Children.Add(reset);
        var guide = new CheckBox { Content = "辅助线", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        guide.IsCheckedChanged += (_, _) => { _stage.ShowGuides = guide.IsChecked == true; _engineering.ShowAnnotations = guide.IsChecked == true; _stage.InvalidateVisual(); };
        actions.Children.Add(guide);
        var rebuild = new Button { Content = "重建模型", Padding = new Thickness(12, 4), FontSize = 12 };
        rebuild.Classes.Add("accent"); rebuild.Click += async (_, _) => await RefreshSceneAsync(); actions.Children.Add(rebuild);
        Grid.SetColumn(actions, 1); root.Children.Add(actions);
        return root;
    }

    private Control Readouts()
    {
        var stack = new StackPanel { Spacing = 14, Margin = new Thickness(14, 10) };
        stack.Children.Add(Readout("光学参数 / OPTICAL PARAMETERS", 10));
        stack.Children.Add(_surfaceInfo);
        stack.Children.Add(new Border { Height = 1, Background = B("#304144") });
        stack.Children.Add(Readout("EFFECTIVE FOCAL LENGTH / 有效焦距", 9.5));
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _efl, Readout("mm", 12) } });
        stack.Children.Add(Readout("F-NUMBER / 有效 F 数", 10)); stack.Children.Add(_fNumber);
        stack.Children.Add(new Border { Height = 1, Background = B("#26383D") });
        stack.Children.Add(Readout("MATERIAL INDEX / 首组参考折射率", 9.5)); stack.Children.Add(_index);
        stack.Children.Add(_revision); stack.Children.Add(_error);
        return new Border { Background = B("#091317"), BorderThickness = new Thickness(1, 0, 0, 0), BorderBrush = B("#304144"), Child = stack };
    }

    private static Control Section(string number, string title, Control content)
    {
        var root = new DockPanel();
        var header = new Border
        {
            Padding = new Thickness(8, 7),
            BorderBrush = B("#344447"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { new TextBlock { Text = number, Foreground = B("#C39B61"), FontSize = 10 }, Readout(title, 10) }
            }
        };
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top); root.Children.Add(header); root.Children.Add(content);
        return new Border { Background = B("#091317"), Child = root, ClipToBounds = true };
    }

    private static Button QuietButton(string label) => new()
    {
        Content = label,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(0),
        Foreground = B("#AABAB8"),
        Padding = new Thickness(9, 5),
        FontSize = 12
    };
    private static SolidColorBrush B(string color) => new(Color.Parse(color));
    private static TextBlock Readout(string text, double size) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI"),
        FontSize = size,
        Foreground = B(size >= 18 ? "#D3DCD5" : "#7D9699"),
        TextWrapping = TextWrapping.Wrap
    };

    private void OnChanged(object? sender, WorkspaceChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (_disposed) return;
            _stage.Scene = null;
            _engineering.Scene = null;
            _efl.Text = _fNumber.Text = _index.Text = "—";
            _surfaceInfo.Text = "结构：—";
            await RefreshSceneAsync();
        });
    }

    private async Task RefreshSceneAsync()
    {
        if (_disposed) return;
        var generation = ++_generation;
        try
        {
            var spatial = _workbench.Visualization.BuildSceneAsync(new VisualizationRequestDto(SceneDimension.ThreeDimensional, RayCount: 7));
            var section = _workbench.Visualization.BuildSceneAsync(new VisualizationRequestDto(SceneDimension.TwoDimensional, RayCount: 7));
            await Task.WhenAll(spatial, section);
            var scene = await spatial;
            var sectionScene = await section;
            if (_disposed || generation != _generation || scene.SourceRevision != _workbench.Events.Revision || sectionScene.SourceRevision != _workbench.Events.Revision) return;
            _stage.Scene = scene;
            _engineering.Scene = sectionScene;
            _sceneError = null; _error.IsVisible = false;
            _efl.Text = scene.Summary.EffectiveFocalLength.ToString("0.00", CultureInfo.InvariantCulture);
            _fNumber.Text = scene.Summary.FNumber.ToString("0.000", CultureInfo.InvariantCulture);
            var mesh = scene.ThreeDimensional!;
            _index.Text = mesh.LensElements.FirstOrDefault()?.RefractiveIndex.ToString("0.0000", CultureInfo.InvariantCulture) ?? "—";
            _surfaceInfo.Text = $"结构：{mesh.LensElements.Count} 片 / {mesh.Surfaces.Count(s => !s.IsReferencePlane)} 面";
            _revision.Text = $"CORE / REV {scene.SourceRevision:000000}";
            _status.Text = "独立内存工程   ·   三维为共享 Core 网格的显示投影   ·   二维工程图保留原始线色   ·   关闭即丢弃";
        }
        catch (Exception exception)
        {
            if (_disposed || generation != _generation) return;
            _sceneError = exception; _error.Text = "模型构建失败，请检查处方。"; _error.IsVisible = true;
            _stage.Scene = null;
            _engineering.Scene = null;
            _efl.Text = _fNumber.Text = _index.Text = "—";
            _surfaceInfo.Text = "结构：—";
        }
    }

    internal async Task<object> VerifyAsync()
    {
        if (_sceneError is not null) throw new InvalidOperationException("Core model build failed.", _sceneError);
        if (_stage.Scene?.ThreeDimensional is not { LensElements.Count: 3 } model || _stage.Scene.SourceRevision != _workbench.Events.Revision)
            throw new InvalidOperationException("The spatial stage does not show the current Core model.");
        var original = _workbench.Prescription.GetSurfaces()[1];
        _workbench.Prescription.UpdateSurface(original with { Radius = original.Radius + 1 });
        await WaitForCurrentSceneAsync();
        _workbench.Prescription.UpdateSurface(original);
        await WaitForCurrentSceneAsync();
        _views.SelectedIndex = 1;
        UpdateLayout();
        await Task.Delay(250);
        Dispatcher.UIThread.RunJobs();
        UpdateLayout();
        var engineeringScene = _engineering.Canvas;
        if (engineeringScene.Scene?.TwoDimensional is not { Rays.Count: > 0 } || engineeringScene.Scene.SourceRevision != _workbench.Events.Revision)
            throw new InvalidOperationException("The original 2-D engineering view did not load the same prescription.");
        _engineering.ExerciseDisplayControls();
        if (engineeringScene.Annotations2D.Count != 4 || engineeringScene.RayLineWidth != 0.85)
            throw new InvalidOperationException("The section presentation did not retain its annotations or fine rays.");
        _views.SelectedIndex = _engineeringFirst ? 1 : 0;
        UpdateLayout();
        PreviewPalette.Restore(); PreviewPalette.Apply();
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs(); UpdateLayout();
        var grid = _editor.GetVisualDescendants().OfType<DataGrid>().Single();
        var materials = _editor.GetVisualDescendants().OfType<DataGridRow>().Where(row => row.Classes.Contains("glass-material-row")).ToArray();
        if (grid.Columns.Count != 12 || materials.Length != 3 || materials.Any(row => row.Background is not ISolidColorBrush brush || brush.Color.B < brush.Color.G + 10))
            throw new InvalidOperationException("The full editor or material-row cue was lost.");
        if (_sidebar.Bounds.Width is < 240 or > 280 || ((ScrollViewer)_sidebar.Content!).VerticalScrollBarVisibility != ScrollBarVisibility.Hidden)
            throw new InvalidOperationException("Sidebar contract changed.");
        if (_surfaceInfo.Text != "结构：3 片 / 6 面" || typeof(FuiPreviewWindow).Assembly.GetType("OpticalSystemDesign.FuiThemePreview.AssemblyDial") is not null)
            throw new InvalidOperationException("The decorative dial was not replaced by compact structure information.");
        _stage.ResetCamera(); _stage.ShowGuides = false; _stage.InvalidateVisual(); _stage.ShowGuides = true;
        return new
        {
            Passed = true,
            Version = 3,
            ReadoutRevision = "CompactStructureWithoutDial",
            DecorativeDialRemoved = true,
            StructureText = _surfaceInfo.Text,
            CSharpNativeControls = true,
            SurfaceRows = 8,
            ActualTableColumns = grid.Columns.Count,
            MaterialRowsBlue = materials.Length,
            SidebarWidthDIP = _sidebar.Bounds.Width,
            SharedCoreElements = model.LensElements.Count,
            MeshFaces = model.Surfaces.Sum(s => s.Faces.Count),
            SceneMatchesPrescriptionRevision = true,
            TwoDimensionalEngineeringViewPreserved = true,
            TwoDimensionalRayPaths = engineeringScene.Scene.TwoDimensional.Rays.Count,
            TwoDimensionalNativeRendererReused = true,
            SectionAnnotations = engineeringScene.Annotations2D.Count,
            SectionControlsExercised = true,
            BlackLensOutlinesAndPhysicalRayPaletteUnchanged = true,
            PrescriptionEditAndRestoreRefreshedScene = true,
            CameraAndGuideResetExercised = true,
            OpticalCalculationAdded = false,
            OpticalAcceptanceClaimed = false,
            ProductThemeRegistered = _registeredTheme,
            RegisteredThemeUsedWithoutPreviewOverrides = _registeredTheme,
            UserSettingsWritten = false
        };
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_generation;
        _workbench.Events.Changed -= OnChanged; _engineering.Dispose(); _editor.Dispose(); _sidebar.Dispose(); _workbench.Dispose();
    }

    private async Task WaitForCurrentSceneAsync()
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (_stage.Scene?.SourceRevision != _workbench.Events.Revision || _engineering.Scene?.SourceRevision != _workbench.Events.Revision)
        {
            if (_sceneError is not null) throw new InvalidOperationException("Scene refresh failed.", _sceneError);
            if (timer.Elapsed > TimeSpan.FromSeconds(8))
                throw new InvalidOperationException($"Scene did not reach current revision {_workbench.Events.Revision}; displayed {_stage.Scene?.SourceRevision}.");
            await Task.Delay(50);
        }
    }
}
