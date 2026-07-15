using Avalonia.Controls;
using Avalonia.Layout;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.App.Controls;

namespace OptilandWorkbench.App.Panels;

public sealed class ViewerPanel : UserControl
{
    private readonly OptilandConnector _connector;
    private readonly OpticSceneControl _scene2D = new() { MinHeight = 320, ViewMode = OpticSceneViewMode.TwoDimensional };
    private readonly OpticSceneControl _scene3D = new() { MinHeight = 320, ViewMode = OpticSceneViewMode.ThreeDimensional };
    private readonly TextBlock _summary = new() { Margin = new Avalonia.Thickness(0, 8, 0, 0) };

    public ViewerPanel(OptilandConnector connector)
    {
        _connector = connector;

        var root = new DockPanel { Margin = new Avalonia.Thickness(12) };
        var showRays = new CheckBox
        {
            Content = "显示光线",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        showRays.IsCheckedChanged += (_, _) =>
        {
            var visible = showRays.IsChecked == true;
            _scene2D.ShowRays = visible;
            _scene3D.ShowRays = visible;
        };
        var renderMode = new ComboBox
        {
            ItemsSource = new[] { "实体", "框架" },
            SelectedIndex = 0,
            MinWidth = 96,
            VerticalAlignment = VerticalAlignment.Center
        };
        renderMode.SelectionChanged += (_, _) =>
        {
            _scene3D.RenderMode = renderMode.SelectedIndex == 1
                ? OpticSceneRenderMode.Wireframe
                : OpticSceneRenderMode.Solid;
        };
        var resetView = new Button { Content = "重置视图", MinWidth = 92 };
        resetView.Click += (_, _) =>
        {
            _scene2D.ResetView();
            _scene3D.ResetView();
        };
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children = { showRays, renderMode, resetView }
        };
        ToolTip.SetTip(renderMode, "三维渲染模式");
        ToolTip.SetTip(_scene2D, "滚轮以指针为中心缩放，拖动平移");
        ToolTip.SetTip(_scene3D, "滚轮缩放，拖动旋转，Shift+拖动平移");

        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        DockPanel.SetDock(_summary, Dock.Bottom);
        root.Children.Add(_summary);
        root.Children.Add(new TabControl
        {
            ItemsSource = new object[]
            {
                new TabItem { Header = "二维视图", Content = _scene2D },
                new TabItem { Header = "三维视图", Content = _scene3D }
            }
        });
        Content = root;

        _connector.OpticLoaded += (_, _) => Refresh();
        _connector.OpticChanged += (_, _) => Refresh();
        Refresh();
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
