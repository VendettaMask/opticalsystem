using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class LensEditorPanel
{
    private readonly TextBox[] _coordinateValues = Enumerable.Range(0, 5).Select(i => new TextBox { Name = $"CoordinateValue{i}" }).ToArray();
    private readonly CheckBox[] _coordinateVariables = Enumerable.Range(0, 5).Select(i => new CheckBox { Name = $"CoordinateVariable{i}", Content = "变量" }).ToArray();
    private readonly TextBlock[] _coordinateLabels = Enumerable.Range(0, 5).Select(_ => new TextBlock { VerticalAlignment = VerticalAlignment.Center }).ToArray();
    private readonly ComboBox _coordinateOrder = new() { Name = "CoordinateOrder" };
    private readonly ComboBox _coordinateReturn = new() { Name = "CoordinateReturn" };
    private readonly ComboBox _coordinateReference = new() { Name = "CoordinateReference" };
    private readonly WrapPanel _coordinateFields = new() { Name = "CoordinateFields", Orientation = Orientation.Horizontal, MaxWidth = 740 };
    private readonly TextBlock _coordinateNote = PropertyNote("");
    private readonly Button _insertCoordinateBefore = CommandButton("action-add", "上插入坐标间断", 150);
    private readonly Button _insertCoordinateAfter = CommandButton("action-add", "下插入坐标间断", 150);
    private CoordinateBreakEditDto? _coordinateEdit;
    private SurfacePropertyPage _activePropertyPage;
    private double _coordinateLengthScale = 1;

    private Control BuildCoordinatesPage()
    {
        var left = new StackPanel { Name = "CoordinateLeftColumn", Spacing = 4, Width = 350, Margin = new Thickness(0, 0, 16, 4) };
        var right = new StackPanel { Name = "CoordinateRightColumn", Spacing = 4, Width = 350, Margin = new Thickness(0, 0, 0, 4) };
        _coordinatesSummary.Margin = new Thickness(0);
        _coordinateOrder.ItemsSource = new[] { CoordinateChoice("先偏心，后倾斜", false), CoordinateChoice("先倾斜，后偏心", true) };
        _coordinateReturn.ItemsSource = new[]
        {
            CoordinateChoice("不返回", CoordinateReturnKind.None),
            CoordinateChoice("返回方向，保持当前位置", CoordinateReturnKind.Orientation),
            CoordinateChoice("返回参考原点与方向", CoordinateReturnKind.PositionAndOrientation)
        };
        for (var i = 0; i < 5; i++)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("116,*,Auto"), ColumnSpacing = 8, MinHeight = 32 };
            _coordinateValues[i].BindThemeResource(TextBox.FontFamilyProperty, ProductFonts.NumericResource);
            _coordinateValues[i].HorizontalAlignment = HorizontalAlignment.Stretch;
            _coordinateValues[i].MinWidth = 90;
            grid.Children.Add(_coordinateLabels[i]);
            grid.Children.Add(_coordinateValues[i]); Grid.SetColumn(_coordinateValues[i], 1);
            grid.Children.Add(_coordinateVariables[i]); Grid.SetColumn(_coordinateVariables[i], 2);
            (i < 2 ? left : right).Children.Add(grid);
        }
        left.Children.Add(PropertyRow("变换顺序", _coordinateOrder));
        left.Children.Add(PropertyRow("坐标返回", _coordinateReturn));
        right.Children.Add(PropertyRow("参考表面", _coordinateReference));
        _coordinateFields.Children.Add(left);
        _coordinateFields.Children.Add(right);
        ToolTip.SetTip(_coordinateOrder, "偏心和倾斜使用进入本行的局部坐标；顺序决定旋转和位移如何组合。");
        ToolTip.SetTip(_coordinateReturn, "返回先建立参考坐标，再应用本行偏心和倾斜；返回原点可能产生有符号的虚传播段。");
        _coordinateReturn.SelectionChanged += (_, _) => UpdateCoordinateControls();
        _insertCoordinateBefore.Name = "InsertCoordinateBefore";
        _insertCoordinateAfter.Name = "InsertCoordinateAfter";
        _insertCoordinateBefore.Click += (_, _) => InsertSelectedCoordinate(false);
        _insertCoordinateAfter.Click += (_, _) => InsertSelectedCoordinate(true);
        var page = PropertyStack(_coordinatesSummary, _coordinateNote, _coordinateFields,
            new WrapPanel { Orientation = Orientation.Horizontal, Children = { _insertCoordinateBefore, _insertCoordinateAfter } },
            PropertyNote("应用仅更新当前结构的坐标参数；插入和删除同步所有结构。修改后可撤销，并随工程保存。"));
        page.MaxWidth = 740;
        page.Margin = new Thickness(0, 0, 0, 16);
        page.HorizontalAlignment = HorizontalAlignment.Left;
        void FitPageWidth() => page.Width = Math.Min(740, _propertyPageHost.Bounds.Width);
        _propertyPageHost.SizeChanged += (_, _) => FitPageWidth();
        FitPageWidth();
        return page;
    }

    private static ComboBoxItem CoordinateChoice(string text, object value) => new() { Content = text, Tag = value };

    private void LoadCoordinateProperties(SurfaceEditorRow row)
    {
        _coordinateEdit = _prescription.GetCoordinateBreak(row.Number);
        _coordinateLengthScale = row.LensLengthUnit.Millimeters();
        var settings = _coordinateEdit.Settings;
        var values = new[] { settings.X / _coordinateLengthScale, settings.Y / _coordinateLengthScale, settings.TiltX, settings.TiltY, settings.TiltZ };
        var labels = new[] { UiLocalization.Format("X 偏心 ({0})", row.LengthUnitSymbol), UiLocalization.Format("Y 偏心 ({0})", row.LengthUnitSymbol),
            UiLocalization.Text("X 倾斜 (°)"), UiLocalization.Text("Y 倾斜 (°)"), UiLocalization.Text("Z 倾斜 (°)") };
        for (var i = 0; i < 5; i++)
        {
            _coordinateValues[i].Text = values[i].ToString("R", CultureInfo.InvariantCulture);
            _coordinateLabels[i].Text = labels[i];
            AutomationProperties.SetName(_coordinateValues[i], labels[i]);
            _coordinateVariables[i].IsChecked = settings.Variables?[i] == true;
            var controlled = _coordinateEdit.ControlledParameters.Contains(i);
            _coordinateValues[i].IsReadOnly = controlled;
            _coordinateVariables[i].IsEnabled = !controlled;
            ToolTip.SetTip(_coordinateValues[i], controlled ? "该参数由拾取或求解控制。" : null);
            ToolTip.SetTip(_coordinateVariables[i], "将本行坐标参数加入优化变量。");
        }
        _coordinateOrder.SelectedItem = _coordinateOrder.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, settings.TiltFirst));
        _coordinateReturn.SelectedItem = _coordinateReturn.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, settings.ReturnKind));
        _coordinateReference.ItemsSource = _coordinateEdit.References.Select(c => CoordinateChoice(
            $"{UiLocalization.Format("表面 {0}", c.Number)} · {c.Label}", c.Number)).ToArray();
        _coordinateReference.SelectedItem = _coordinateReference.Items.Cast<ComboBoxItem>().FirstOrDefault(c => Equals(c.Tag, settings.ReturnSurface));
        _coordinateNote.Text = UiLocalization.Text(_coordinateEdit.Restriction ?? (!_coordinateEdit.IsCoordinateBreak
            ? "此行不是坐标间断。请在需要变换的位置插入坐标间断。"
            : _coordinateEdit.UsesStoredBasis ? "旧工程未保存原始变换参数。本次偏心和倾斜以保留的表面坐标为基准。"
            : "编辑坐标间断参数；拾取或求解控制的参数保持只读。"));
        var last = _prescription.GetSurfaces().Count - 1;
        _insertCoordinateBefore.IsEnabled = row.Number > 0;
        _insertCoordinateAfter.IsEnabled = row.Number < last;
        ToolTip.SetTip(_insertCoordinateBefore, row.Number > 0 ? null : "不能在物面之前插入。");
        ToolTip.SetTip(_insertCoordinateAfter, row.Number < last ? null : "不能在像面之后插入。");
        UpdateCoordinateControls();
    }

    private void UpdateCoordinateControls()
    {
        _coordinateFields.IsVisible = _coordinateEdit?.IsCoordinateBreak == true;
        _coordinateNote.IsVisible = _coordinateEdit is not { IsCoordinateBreak: true, UsesStoredBasis: false, Restriction: null };
        var editable = _coordinateEdit?.CanEdit == true;
        for (var i = 0; i < 5; i++)
        {
            _coordinateValues[i].IsReadOnly = !editable || _coordinateEdit!.ControlledParameters.Contains(i);
            _coordinateVariables[i].IsEnabled = editable && !_coordinateEdit!.ControlledParameters.Contains(i);
        }
        _coordinateOrder.IsEnabled = editable;
        _coordinateReturn.IsEnabled = editable;
        _coordinateReference.IsEnabled = editable && (_coordinateReturn.SelectedItem as ComboBoxItem)?.Tag is CoordinateReturnKind kind && kind != CoordinateReturnKind.None;
        if (_activePropertyPage == SurfacePropertyPage.Coordinates)
        {
            _applyComponentsButton.IsEnabled = _coordinateEdit?.CanEdit == true;
            ToolTip.SetTip(_applyComponentsButton, _coordinateEdit?.CanEdit == true ? "应用当前结构的坐标设置。" : "请先选择可编辑的坐标间断。");
        }
        else
        {
            _applyComponentsButton.IsEnabled = _grid.SelectedItem is SurfaceEditorRow;
            ToolTip.SetTip(_applyComponentsButton, null);
        }
    }

    private void ApplyCoordinateProperties(SurfaceEditorRow row)
    {
        if (_coordinateEdit is not { CanEdit: true } edit) return;
        var values = _coordinateValues.Select(input => PropertyNumber(input.Text)).ToArray();
        _prescription.UpdateCoordinateBreak(row.Number, new(values[0] * _coordinateLengthScale, values[1] * _coordinateLengthScale,
            values[2], values[3], values[4], (_coordinateOrder.SelectedItem as ComboBoxItem)?.Tag is true,
            (_coordinateReturn.SelectedItem as ComboBoxItem)?.Tag is CoordinateReturnKind kind ? kind : CoordinateReturnKind.None,
            (_coordinateReference.SelectedItem as ComboBoxItem)?.Tag is int reference ? reference : 0,
            _coordinateVariables.Select(v => v.IsChecked == true).ToArray()), edit.Revision);
    }

    private void InsertSelectedCoordinate(bool after)
    {
        if (_grid.SelectedItem is not SurfaceEditorRow row || _coordinateEdit is null) return;
        try
        {
            var number = _prescription.InsertCoordinateBreak(row.Number, after, _coordinateEdit.Revision);
            Refresh();
            _grid.SelectedItem = _grid.ItemsSource!.Cast<SurfaceEditorRow>().Single(r => r.Number == number);
            _grid.ScrollIntoView(_grid.SelectedItem, null);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            _componentSummary.Text = UiLocalization.Format("未应用：{0}", UiLocalization.Text(error.Message));
        }
    }
}
