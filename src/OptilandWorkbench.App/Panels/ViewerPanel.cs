using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.App.Controls;

namespace OptilandWorkbench.App.Panels;

public sealed class ViewerPanel : UserControl
{
    private static readonly IBrush ToolbarBackground = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255));
    private static readonly IBrush ToolbarBorder = new SolidColorBrush(Color.FromRgb(209, 209, 214));

    private readonly OptilandConnector _connector;
    private readonly OpticSceneControl _scene2D = new() { MinHeight = 320, ViewMode = OpticSceneViewMode.TwoDimensional };
    private readonly OpticSceneControl _scene3D = new() { MinHeight = 320, ViewMode = OpticSceneViewMode.ThreeDimensional };
    private readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TabControl _viewTabs;

    public ViewerPanel(OptilandConnector connector)
    {
        _connector = connector;

        ToolTip.SetTip(_scene2D, "滚轮以指针为中心缩放，拖动平移");
        ToolTip.SetTip(_scene3D, "滚轮缩放，拖动旋转，Shift+拖动平移");

        _viewTabs = new TabControl
        {
            SelectedIndex = 1,
            ItemsSource = new object[]
            {
                new TabItem { Header = "二维视图", Content = Build2DWorkspace() },
                new TabItem { Header = "三维视图", Content = Build3DWorkspace() }
            }
        };

        var root = new DockPanel();
        var summaryBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252)),
            BorderBrush = ToolbarBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 5),
            Child = _summary
        };
        DockPanel.SetDock(summaryBar, Dock.Bottom);
        root.Children.Add(summaryBar);
        root.Children.Add(_viewTabs);
        Content = root;

        _connector.OpticLoaded += (_, _) => Refresh();
        _connector.OpticChanged += (_, _) => Refresh();
        Refresh();
    }

    public void ShowView(OpticSceneViewMode mode)
    {
        _viewTabs.SelectedIndex = mode == OpticSceneViewMode.TwoDimensional ? 0 : 1;
    }

    private Control Build2DWorkspace()
    {
        var showRays = new CheckBox
        {
            Content = "显示光线",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        showRays.IsCheckedChanged += (_, _) => _scene2D.ShowRays = showRays.IsChecked == true;
        var reset = CompactButton("rotate-ccw", "恢复二维视图的缩放与平移");
        reset.Click += (_, _) => _scene2D.ResetView();

        return SceneWithOverlay(
            _scene2D,
            Toolbar(new Control[] { showRays, reset }, HorizontalAlignment.Right));
    }

    private Control Build3DWorkspace()
    {
        var showRays = new CheckBox
        {
            Content = "光线",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        showRays.IsCheckedChanged += (_, _) => _scene3D.ShowRays = showRays.IsChecked == true;

        var renderMode = new ComboBox
        {
            ItemsSource = new[] { "实体", "框架" },
            SelectedIndex = 0,
            MinWidth = 88,
            VerticalAlignment = VerticalAlignment.Center
        };
        renderMode.SelectionChanged += (_, _) =>
        {
            _scene3D.RenderMode = renderMode.SelectedIndex == 1
                ? OpticSceneRenderMode.Wireframe
                : OpticSceneRenderMode.Solid;
        };
        ToolTip.SetTip(renderMode, "三维渲染模式");

        var reset = CompactButton("rotate-ccw", "重置三维视角");
        reset.Click += (_, _) => _scene3D.ResetView();

        var topToolbar = Toolbar(
            new Control[]
            {
                new TextBlock
                {
                    Text = "三维布局",
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                },
                showRays,
                renderMode,
                reset
            },
            HorizontalAlignment.Right);

        var presetToolbar = Toolbar(
            new Control[]
            {
                PresetButton("cuboid", "等轴测视图", OpticSceneViewPreset.Isometric),
                PresetButton("panel-left", "侧视图", OpticSceneViewPreset.Side),
                PresetButton("panel-top", "俯视图", OpticSceneViewPreset.Top),
                PresetButton("square", "端面视图", OpticSceneViewPreset.End),
                PresetButton("flip-horizontal-2", "反向视图", OpticSceneViewPreset.Reverse)
            },
            HorizontalAlignment.Center,
            VerticalAlignment.Bottom);

        return SceneWithOverlay(
            _scene3D,
            topToolbar,
            presetToolbar);
    }

    private Button PresetButton(string iconName, string tooltip, OpticSceneViewPreset preset)
    {
        var button = CompactButton(iconName, tooltip);
        button.Click += (_, _) => _scene3D.SetViewPreset(preset);
        return button;
    }

    private static Control SceneWithOverlay(Control scene, params Control[] overlays)
    {
        var grid = new Grid();
        grid.Children.Add(scene);
        foreach (var overlay in overlays)
        {
            grid.Children.Add(overlay);
        }

        return grid;
    }

    private static Border Toolbar(
        IEnumerable<Control> controls,
        HorizontalAlignment horizontalAlignment,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };
        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }

        return new Border
        {
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            Margin = new Thickness(10),
            Padding = new Thickness(7, 5),
            CornerRadius = new CornerRadius(7),
            Background = ToolbarBackground,
            BorderBrush = ToolbarBorder,
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 4 12 0 #1A000000"),
            Child = panel
        };
    }

    private static Button CompactButton(string iconName, string tooltip)
    {
        var button = new Button
        {
            Content = new LocalIcon
            {
                IconName = iconName,
                Width = 18,
                Height = 18,
                Stroke = new SolidColorBrush(Color.FromRgb(48, 48, 52))
            },
            Width = 36,
            MinWidth = 0,
            Height = 30,
            Padding = new Thickness(0)
        };
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private void Refresh()
    {
        _scene2D.Optic = _connector.CurrentOptic;
        _scene3D.Optic = _connector.CurrentOptic;
        _scene2D.InvalidateVisual();
        _scene3D.InvalidateVisual();

        var focalLength = _connector.CurrentOptic.Paraxial.EstimateEffectiveFocalLength();
        var fNumber = _connector.CurrentOptic.Paraxial.EstimateFNumber();
        _summary.Text = $"有效焦距 {focalLength:0.###} mm    F 数 {fNumber:0.###}    系统总长 {_connector.CurrentOptic.SurfaceGroup.TotalTrack:0.###} mm";
    }
}
