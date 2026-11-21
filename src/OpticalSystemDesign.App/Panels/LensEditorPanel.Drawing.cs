using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.App.ViewModels;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class LensEditorPanel
{
    private object?[] _loadedPhysicalInputs = [];
    private readonly ComboBox _surfaceOpacity = new() { Name = "SurfaceOpacity" };
    private readonly CheckBox _hideSurfaceRays = DrawingFlag("HideSurfaceRays", "隐藏这个表面的光线");
    private readonly CheckBox _skipSurfaceRays = DrawingFlag("SkipSurfaceRays", "光线忽略这个面");
    private readonly CheckBox _hideSurface = DrawingFlag("HideSurface", "不显示此表面");
    private readonly CheckBox _hideSurfaceEdges = DrawingFlag("HideSurfaceEdges", "不显示此表面的边缘");
    private readonly CheckBox _drawSurfaceAxes = DrawingFlag("DrawSurfaceLocalAxes", "显示局部坐标轴");
    private readonly CheckBox _drawSurfaceOrigin = DrawingFlag("DrawSurfaceLocalOrigin", "绘制局部原点");
    private readonly ComboBox _surfaceEdgeMode = new() { Name = "SurfaceEdgeMode" };
    private readonly ComboBox _surfaceDrawingPrecision = new() { Name = "SurfaceDrawingPrecision" };

    private Control BuildOpacityPicker()
    {
        _surfaceOpacity.ItemsSource = new[] { 1.0, 0.9, 0.75, 0.5, 0.25, 0.1, 0.0 }
            .Select(value => new ComboBoxItem { Content = value == 1 ? "100%（默认）" : value == 0 ? "0%（完全透明）" : $"{value * 100:0}%", Tag = value }).ToArray();
        _surfaceOpacity.SelectedIndex = 0;
        AutomationProperties.SetName(_surfaceOpacity, "表面透明度");
        ToolTip.SetTip(_surfaceOpacity, "显示不透明度比例：100% 保持默认绘制强度，0% 完全透明；不改变材料透过率。");
        return _surfaceOpacity;
    }

    private Control BuildDrawingPage()
    {
        _surfaceEdgeMode.ItemsSource = new[]
        {
            Choice("方形到下表面", SurfaceEdgeModeDto.SquareToNext),
            Choice("锥形到下表面", SurfaceEdgeModeDto.DirectToNext),
            Choice("平直到下表面", SurfaceEdgeModeDto.FlatToNext),
            Choice("不绘制边缘", SurfaceEdgeModeDto.None)
        };
        _surfaceDrawingPrecision.ItemsSource = new[]
        {
            Choice("标准", SurfaceDrawingPrecisionDto.Standard), Choice("中等", SurfaceDrawingPrecisionDto.Medium),
            Choice("高", SurfaceDrawingPrecisionDto.High), Choice("演示", SurfaceDrawingPrecisionDto.Presentation)
        };
        _surfaceEdgeMode.SelectedIndex = _surfaceDrawingPrecision.SelectedIndex = 0;
        AutomationProperties.SetName(_surfaceEdgeMode, "边缘显示");
        AutomationProperties.SetName(_surfaceDrawingPrecision, "绘图精度");
        ToolTip.SetTip(_hideSurfaceRays, "隐藏到达此表面的绘图光线段，后续光线段继续显示。");
        ToolTip.SetTip(_skipSurfaceRays, "绘图时跳过此面节点，直连前后节点；物理追迹仍经过此面。用于无光焦度的辅助面。");
        ToolTip.SetTip(_surfaceEdgeMode, "平直到下表面仅用于二维布局；三维布局和实体模型采用方形到下表面。");
        ToolTip.SetTip(_surfaceDrawingPrecision, "提高表面曲线和网格的绘图采样密度，不改变光线追迹精度。");
        var flags = PropertyStack(_hideSurfaceRays, _skipSurfaceRays, _hideSurface, _hideSurfaceEdges, _drawSurfaceAxes, _drawSurfaceOrigin);
        flags.Width = 310; flags.Margin = new Thickness(0, 0, 18, 8);
        var choices = PropertyStack(PropertyRow("边缘显示：", _surfaceEdgeMode), PropertyRow("绘图精度：", _surfaceDrawingPrecision));
        var fields = new WrapPanel { Orientation = Orientation.Horizontal, Children = { flags, choices } };
        return PropertyStack(fields, _drawingSummary);
    }

    private static ComboBoxItem Choice<T>(string label, T value) => new() { Content = label, Tag = value };
    private static CheckBox DrawingFlag(string name, string caption)
    {
        var control = new CheckBox { Name = name, Content = caption, MinHeight = 32 };
        AutomationProperties.SetName(control, caption);
        return control;
    }

    private void LoadDrawingOptions(SurfaceEditorRow row)
    {
        var drawing = row.Drawing;
        var opacity = _surfaceOpacity.Items.Cast<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, drawing.Opacity));
        if (opacity is null)
        {
            opacity = new ComboBoxItem { Content = $"{drawing.Opacity * 100:0.####}%", Tag = drawing.Opacity };
            _surfaceOpacity.ItemsSource = _surfaceOpacity.Items.Cast<ComboBoxItem>().Concat(new[] { opacity }).ToArray();
        }
        _surfaceOpacity.SelectedItem = opacity;
        _hideSurfaceRays.IsChecked = drawing.HideRays;
        _skipSurfaceRays.IsChecked = drawing.SkipRayDrawing;
        _hideSurface.IsChecked = drawing.HideSurface;
        _hideSurfaceEdges.IsChecked = drawing.HideEdges;
        _drawSurfaceAxes.IsChecked = drawing.DrawLocalAxes;
        _drawSurfaceOrigin.IsChecked = drawing.DrawLocalOrigin;
        _surfaceEdgeMode.SelectedItem = _surfaceEdgeMode.Items.Cast<ComboBoxItem>().First(item => Equals(item.Tag, drawing.EdgeMode));
        _surfaceDrawingPrecision.SelectedItem = _surfaceDrawingPrecision.Items.Cast<ComboBoxItem>().First(item => Equals(item.Tag, drawing.Precision));
        LoadRowColor(row);
    }

    private SurfaceDrawingOptionsDto SelectedDrawingOptions() => new(
        (_surfaceOpacity.SelectedItem as ComboBoxItem)?.Tag is double opacity ? opacity : 1,
        _hideSurfaceRays.IsChecked == true, _skipSurfaceRays.IsChecked == true,
        _hideSurface.IsChecked == true, _hideSurfaceEdges.IsChecked == true,
        _drawSurfaceAxes.IsChecked == true, _drawSurfaceOrigin.IsChecked == true,
        (_surfaceEdgeMode.SelectedItem as ComboBoxItem)?.Tag is SurfaceEdgeModeDto edgeMode ? edgeMode : SurfaceEdgeModeDto.SquareToNext,
        (_surfaceDrawingPrecision.SelectedItem as ComboBoxItem)?.Tag is SurfaceDrawingPrecisionDto precision ? precision : SurfaceDrawingPrecisionDto.Standard,
        SelectedRowColor());

    // Compare the loaded control values rather than normalized optical numbers:
    // infinity switches, imported geometry names and display rounding are not edits.
    private object?[] PhysicalPropertyInputs() => [
        _geometryPicker.SelectedItem, _aperturePicker.SelectedItem,
        _gratingOrder.Value, _gratingPeriod.Value, _infiniteGratingPeriod.IsChecked,
        _gratingAngle.Value, _thinLensFocalLength.Value, _infiniteThinLensFocalLength.IsChecked,
        _stopSurface.IsChecked, _surfaceCoating.Text, _polynomialRadius.Text,
        _polynomialCount.Value, _polynomialCoefficients.Text, _biconicRadiusX.Text, _biconicConicX.Text];
}
