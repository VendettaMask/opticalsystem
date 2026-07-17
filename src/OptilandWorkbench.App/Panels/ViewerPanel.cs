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
    private readonly ContentControl _workspace = new();
    private readonly Control _twoDimensionalWorkspace;
    private readonly Control _threeDimensionalWorkspace;

    public ViewerPanel(OptilandConnector connector)
    {
        _connector = connector;

        ToolTip.SetTip(_scene2D, "滚轮以指针为中心缩放，拖动平移");
        ToolTip.SetTip(_scene3D, "滚轮缩放，拖动旋转，Shift+拖动平移");

        _twoDimensionalWorkspace = Build2DWorkspace();
        _threeDimensionalWorkspace = Build3DWorkspace();
        _workspace.Content = _threeDimensionalWorkspace;

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
        root.Children.Add(_workspace);
        Content = root;

        _connector.OpticLoaded += (_, _) => Refresh();
        _connector.OpticChanged += (_, _) => Refresh();
        Refresh();
    }

    public void ShowView(OpticSceneViewMode mode)
    {
        _workspace.Content = mode == OpticSceneViewMode.TwoDimensional
            ? _twoDimensionalWorkspace
            : _threeDimensionalWorkspace;
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
        var reset = CompactButton("重置视图", "恢复二维视图的缩放与平移");
        reset.Click += (_, _) => _scene2D.ResetView();

        return SceneWithOverlay(
            _scene2D,
            Toolbar(new Control[] { showRays, reset }, HorizontalAlignment.Right),
            BuildHint("二维布局 · 拖动平移 · 滚轮缩放"));
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

        var reset = CompactButton("⟳", "重置三维视角");
        reset.Width = 36;
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
                PresetButton("◇", "等轴测视图", OpticSceneViewPreset.Isometric),
                PresetButton("▰", "侧视图", OpticSceneViewPreset.Side),
                PresetButton("▱", "俯视图", OpticSceneViewPreset.Top),
                PresetButton("▢", "端面视图", OpticSceneViewPreset.End),
                PresetButton("◁", "反向视图", OpticSceneViewPreset.Reverse)
            },
            HorizontalAlignment.Center,
            VerticalAlignment.Bottom);

        return SceneWithOverlay(
            _scene3D,
            topToolbar,
            presetToolbar,
            BuildHint("拖动旋转 · Shift+拖动平移 · 滚轮缩放"));
    }

    private Button PresetButton(string glyph, string tooltip, OpticSceneViewPreset preset)
    {
        var button = CompactButton(glyph, tooltip);
        button.Width = 36;
        button.FontSize = 15;
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

    private static Border BuildHint(string text)
    {
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10),
            Padding = new Thickness(8, 5),
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(218, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(170, 209, 209, 214)),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 3 9 0 #14000000"),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(72, 83, 98))
            }
        };
    }

    private static Button CompactButton(string content, string tooltip)
    {
        var button = new Button
        {
            Content = content,
            MinWidth = 72,
            Height = 30,
            Padding = new Thickness(7, 2)
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
