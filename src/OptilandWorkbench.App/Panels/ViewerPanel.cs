using Avalonia.Controls;
using Avalonia.Layout;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.App.Controls;

namespace OptilandWorkbench.App.Panels;

public sealed class ViewerPanel : UserControl
{
    private readonly OptilandConnector _connector;
    private readonly OpticSceneControl _scene = new() { MinHeight = 320 };
    private readonly TextBlock _summary = new() { Margin = new Avalonia.Thickness(0, 8, 0, 0) };

    public ViewerPanel(OptilandConnector connector)
    {
        _connector = connector;

        var root = new DockPanel { Margin = new Avalonia.Thickness(12) };
        DockPanel.SetDock(_summary, Dock.Bottom);
        root.Children.Add(_summary);
        root.Children.Add(_scene);
        Content = root;

        _connector.OpticLoaded += (_, _) => Refresh();
        _connector.OpticChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        _scene.Optic = _connector.CurrentOptic;
        _scene.InvalidateVisual();

        var focalLength = _connector.CurrentOptic.Paraxial.EstimateEffectiveFocalLength();
        var fNumber = _connector.CurrentOptic.Paraxial.EstimateFNumber();
        _summary.Text = $"EFL {focalLength:0.###} mm    F/# {fNumber:0.###}    Track {_connector.CurrentOptic.SurfaceGroup.TotalTrack:0.###} mm";
    }
}
