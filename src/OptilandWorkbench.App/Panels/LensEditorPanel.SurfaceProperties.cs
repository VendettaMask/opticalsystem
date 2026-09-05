using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.App.Panels;

public sealed partial class LensEditorPanel
{
    private enum SurfacePropertyPage { Type, Drawing, Aperture, Scattering, Coordinates, PhysicalOptics, Coating, Import, Composite, Polarization }

    private readonly Grid _propertyBody = new() { Name = "SurfacePropertiesBody", Height = 300 };
    private readonly ContentControl _propertyPageHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _stopSurface = new() { Name = "SurfaceIsStop", Content = "使此表面为光阑" };
    private readonly CheckBox _fixedSemiDiameter = new() { Name = "SurfaceFixedSemiDiameter", Content = "固定净半径" };
    private readonly NumericUpDown _surfaceSemiDiameter = Number(168, 0.1m, 1_000_000, 0.1m, 10);
    private readonly TextBox _surfaceCoating = new() { Name = "SurfaceCoating", PlaceholderText = "None" };
    private readonly TextBlock _drawingSummary = PropertyNote("");
    private readonly TextBlock _coordinatesSummary = PropertyNote("");
    private readonly TextBlock _scatterSummary = PropertyNote("");
    private readonly TextBlock _coatingModelSummary = PropertyNote("");
    private readonly TextBlock _importSummary = PropertyNote("");
    private readonly TextBlock _interactionSummary = PropertyNote("");
    private readonly TextBlock _apertureSummary = PropertyNote("");
    private readonly StackPanel _gratingProperties = new() { Name = "SurfaceGratingProperties", Spacing = 6 };
    private readonly StackPanel _thinLensProperties = new() { Name = "SurfaceThinLensProperties", Spacing = 6 };
    private readonly TextBlock _surfacePropertiesTitle = new()
    {
        Name = "SurfacePropertiesTitle",
        Text = "表面属性",
        FontWeight = FontWeight.SemiBold,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button _previousPropertySurface = PropertyHeaderButton("PreviousPropertySurface", "chevron-left", "上一面");
    private readonly Button _nextPropertySurface = PropertyHeaderButton("NextPropertySurface", "chevron-right", "下一面");

    private Control BuildSurfacePropertiesSection()
    {
        var toggle = PropertyHeaderButton("SurfacePropertiesToggle", "chevron-down", "展开表面属性");
        var icon = (LocalIcon)toggle.Content!;
        var editorBorder = new Border
        {
            Name = "SurfacePropertiesEditor",
            IsVisible = false,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = BuildSurfacePropertiesEditor()
        };
        editorBorder.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        toggle.Click += (_, _) =>
        {
            editorBorder.IsVisible = !editorBorder.IsVisible;
            icon.IconName = editorBorder.IsVisible ? "chevron-up" : "chevron-down";
            var action = editorBorder.IsVisible ? "收起表面属性" : "展开表面属性";
            ToolTip.SetTip(toggle, action);
            Avalonia.Automation.AutomationProperties.SetName(toggle, action);
        };
        _previousPropertySurface.Click += (_, _) => NavigatePropertySurface(-1);
        _nextPropertySurface.Click += (_, _) => NavigatePropertySurface(1);
        var header = new Border
        {
            Name = "SurfacePropertiesHeader",
            Padding = new Thickness(8, 3),
            Child = new StackPanel
            {
                Name = "SurfacePropertiesHeaderItems",
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Left,
                Children = { toggle, _surfacePropertiesTitle, _previousPropertySurface, _nextPropertySurface }
            }
        };
        header.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        return new StackPanel { Children = { header, editorBorder } };
    }

    private static Button PropertyHeaderButton(string name, string iconName, string caption)
    {
        var icon = new LocalIcon { IconName = iconName, Width = 14, Height = 14 };
        icon.BindThemeResource(LocalIcon.StrokeProperty, ThemeResourceBindings.TextPrimary);
        var button = new Button
        {
            Name = name,
            Width = 28,
            Height = 28,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = icon
        };
        button.BindThemeResource(Button.BorderBrushProperty, ThemeResourceBindings.Border);
        ToolTip.SetTip(button, caption);
        Avalonia.Automation.AutomationProperties.SetName(button, caption);
        return button;
    }

    private void UpdateSurfacePropertiesHeader()
    {
        var row = _grid.SelectedItem as SurfaceEditorRow;
        _surfacePropertiesTitle.Text = row is null ? "表面属性" : $"表面 {row.Number} 属性";
        _previousPropertySurface.IsEnabled = row is { Number: > 0 };
        _nextPropertySurface.IsEnabled = row is { IsLastSurface: false };
    }

    private void NavigatePropertySurface(int offset)
    {
        if (_disposed || _grid.SelectedItem is not SurfaceEditorRow selected) return;
        if (!_grid.CommitEdit(DataGridEditingUnit.Cell, true)
            || !_grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
        _grid.Focus();
        var revision = _events.Revision;
        // A numeric LostFocus commit may queue a row refresh. Select its replacement,
        // and never carry a queued navigation command into another document/revision.
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || revision != _events.Revision) return;
            var rows = _grid.ItemsSource!.Cast<SurfaceEditorRow>().ToArray();
            var index = Array.FindIndex(rows, row => row.Number == selected.Number);
            if (index < 0 || index + offset < 0 || index + offset >= rows.Length) return;
            ClearSurfaceContext();
            CloseRadiusSolve();
            _grid.SelectedItem = rows[index + offset];
            _grid.ScrollIntoView(_grid.SelectedItem, null);
        });
    }

    private Control BuildSurfacePropertiesEditor()
    {
        _geometryPicker.Name = "SurfaceGeometry";
        _aperturePicker.Name = "SurfaceAperture";
        _surfaceSemiDiameter.Name = "SurfaceSemiDiameter";
        _applyComponentsButton.Name = "ApplySurfaceProperties";
        _applyComponentsButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        _geometryPicker.SelectionChanged += (_, _) => UpdateGeometryPropertyVisibility();
        _fixedSemiDiameter.IsCheckedChanged += (_, _) =>
            _surfaceSemiDiameter.IsEnabled = _fixedSemiDiameter.IsEnabled && _fixedSemiDiameter.IsChecked == true;

        _gratingProperties.Children.Add(PropertyRow("衍射级次：", _gratingOrder));
        _gratingProperties.Children.Add(PropertyRow("周期 (μm)：", WithInfinity(_gratingPeriod, _infiniteGratingPeriod)));
        _gratingProperties.Children.Add(PropertyRow("槽角 (°)：", _gratingAngle));
        _thinLensProperties.Children.Add(PropertyRow("焦距 (mm)：", WithInfinity(_thinLensFocalLength, _infiniteThinLensFocalLength)));

        var typePage = new WrapPanel { Name = "SurfaceTypePage", Orientation = Orientation.Horizontal };
        var typeFields = PropertyStack(
            PropertyRow("表面类型：", _geometryPicker),
            PropertyRow("表面颜色：", UnavailablePicker("默认颜色")),
            PropertyRow("表面透明度：", UnavailablePicker("100%")),
            PropertyRow("行颜色：", UnavailablePicker("默认颜色")),
            _gratingProperties,
            _thinLensProperties);
        typeFields.Width = 350;
        typeFields.Margin = new Thickness(0, 0, 22, 10);
        typePage.Children.Add(typeFields);
        var typeFlags = PropertyStack(
            _stopSurface,
            UnavailableFlag("设为全局坐标参考面"),
            UnavailableFlag("表面不能是超半球面"),
            UnavailableFlag("忽略这个表面"),
            PropertyNote("灰色项尚未实现，不能编辑；不会改变当前追迹。"));
        typeFlags.Width = 270;
        typePage.Children.Add(typeFlags);
        foreach (var flag in typeFlags.Children.OfType<CheckBox>())
        {
            flag.MinHeight = 32;
            flag.VerticalAlignment = VerticalAlignment.Center;
        }

        var pages = new Dictionary<SurfacePropertyPage, Control>
        {
            [SurfacePropertyPage.Type] = typePage,
            [SurfacePropertyPage.Drawing] = PropertyStack(
                _drawingSummary,
                PropertyNote("绘图沿用当前主题与布局设置。独立表面颜色、透明度、隐藏表面及行颜色尚未实现。")),
            [SurfacePropertyPage.Aperture] = PropertyStack(
                PropertyRow("孔径类型：", _aperturePicker),
                PropertyRow("净半径 (mm)：", _surfaceSemiDiameter),
                _fixedSemiDiameter,
                _apertureSummary,
                PropertyNote("净半径与物理孔径是不同设置。取消固定后自动求净半径；原孔径类型未变时保留其导入参数。具体孔径尺寸仍不支持在此编辑。")),
            [SurfacePropertyPage.Scattering] = PropertyStack(
                _scatterSummary,
                PropertyNote("本页只读。当前散射模型仅支持主光线损耗近似，不等同于完整 BSDF / 杂散光追迹；尚未接入散射参数编辑。")),
            [SurfacePropertyPage.Coordinates] = PropertyStack(
                _coordinatesSummary,
                PropertyNote("以上为当前表面的实际全局坐标，只读。Zemax 倾斜/偏心的前后顺序、坐标返回与参考面编辑尚未接入，不将其作为等价控制。")),
            [SurfacePropertyPage.PhysicalOptics] = PropertyStack(
                _interactionSummary,
                PropertyNote("衍射光栅和薄透镜参数在“类型”页按面型显示。逐表面物理光学传播设置尚未实现；系统级 PSF / 波前分析不是逐面 POP 传播。")),
            [SurfacePropertyPage.Coating] = PropertyStack(
                PropertyRow("膜层名称：", _surfaceCoating),
                _coatingModelSummary,
                PropertyNote("留空或 None 表示无膜层。名称编辑沿用镜头数据表：非 None 使用实验性透过率起伏近似，不是完整多层薄膜求解。名称未变时保留原有膜层模型。")),
            [SurfacePropertyPage.Import] = PropertyStack(
                _importSummary,
                PropertyNote("此处展示当前面型支持状态。不支持的导入面型保持只读，不替换成标准面；面型 DLL、逐表面导入设置尚未实现。")),
            [SurfacePropertyPage.Composite] = PropertyStack(
                PropertyNote("复合表面尚未实现，当前没有可应用的复合参数。")),
            [SurfacePropertyPage.Polarization] = PropertyStack(
                PropertyNote("逐表面偏振覆盖尚未实现。偏振相关分析使用现有 Jones / Fresnel 模型，不在此提供无效的勾选项。"))
        };

        var navigation = new ListBox
        {
            Name = "SurfacePropertyNavigation",
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4),
            ItemsSource = new[]
            {
                PageItem(SurfacePropertyPage.Type, "类型"), PageItem(SurfacePropertyPage.Drawing, "绘图"),
                PageItem(SurfacePropertyPage.Aperture, "孔径"), PageItem(SurfacePropertyPage.Scattering, "散射"),
                PageItem(SurfacePropertyPage.Coordinates, "倾斜/偏心"), PageItem(SurfacePropertyPage.PhysicalOptics, "物理光学"),
                PageItem(SurfacePropertyPage.Coating, "膜层"), PageItem(SurfacePropertyPage.Import, "导入"),
                PageItem(SurfacePropertyPage.Composite, "复合"), PageItem(SurfacePropertyPage.Polarization, "偏振")
            }
        };
        navigation.BindThemeResource(ListBox.BackgroundProperty, ThemeResourceBindings.Surface);
        var pageScroll = new ScrollViewer
        {
            Name = "SurfacePropertyScroll",
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(14, 12),
            Content = _propertyPageHost
        };
        navigation.SelectionChanged += (_, _) =>
        {
            if (navigation.SelectedItem is ListBoxItem { Tag: SurfacePropertyPage page })
            {
                _propertyPageHost.Content = pages[page];
                pageScroll.Offset = default;
            }
        };

        _componentSummary.MinWidth = 0;
        _componentSummary.MaxWidth = double.PositiveInfinity;
        _componentSummary.TextWrapping = TextWrapping.NoWrap;
        _componentSummary.TextTrimming = TextTrimming.CharacterEllipsis;
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(14, 4, 14, 6) };
        var revert = CommandButton("refresh-cw", "还原", 72);
        revert.Name = "RevertSurfaceProperties";
        revert.Margin = new Thickness(8, 0, 0, 0);
        revert.Click += (_, _) => LoadComponentSelection();
        footer.Children.Add(_componentSummary);
        footer.Children.Add(_applyComponentsButton);
        footer.Children.Add(revert);
        Grid.SetColumn(_applyComponentsButton, 1);
        Grid.SetColumn(revert, 2);
        var separator = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Child = navigation };
        separator.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        _propertyBody.ColumnDefinitions = new ColumnDefinitions("124,*");
        _propertyBody.RowDefinitions = new RowDefinitions("*,Auto");
        _propertyBody.Children.Add(separator);
        _propertyBody.Children.Add(pageScroll);
        _propertyBody.Children.Add(footer);
        Grid.SetRowSpan(separator, 2);
        Grid.SetColumn(pageScroll, 1);
        Grid.SetColumn(footer, 1);
        Grid.SetRow(footer, 1);
        _propertyBody.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Surface);
        SettingsPanelChrome.ApplyInputStyles(_propertyBody);
        navigation.SelectedIndex = 0;
        return _propertyBody;
    }

    private void LoadSurfaceProperties(SurfaceEditorRow row)
    {
        _stopSurface.IsChecked = row.IsStop;
        _stopSurface.IsEnabled = row.GeometryComputable && row.Number > 0 && !row.IsLastSurface && !row.IsStop;
        ToolTip.SetTip(_stopSurface, row.IsStop ? "要移动光阑，请选择另一个表面并将其设为光阑。" : "应用后将此表面设为唯一光阑。");
        _surfaceCoating.Text = row.Coating;
        _surfaceCoating.IsEnabled = row.GeometryComputable;
        _fixedSemiDiameter.IsChecked = row.SemiDiameterFixed;
        _fixedSemiDiameter.IsEnabled = row.GeometryComputable;
        _surfaceSemiDiameter.Value = (decimal)Math.Clamp(row.SemiDiameter, 0.1, 1_000_000);
        _surfaceSemiDiameter.IsEnabled = row.GeometryComputable && row.SemiDiameterFixed;
        _drawingSummary.Text = $"表面 {row.Number}：{row.SurfaceRole}\n当前净半径：{row.SemiDiameter:0.######} mm\n机械半直径：{row.MechanicalSemiDiameter:0.######} mm";
        _apertureSummary.Text = $"当前物理孔径：{row.ApertureKind}";
        _coatingModelSummary.Text = $"当前膜层模型：{row.CoatingKind}";
        _interactionSummary.Text = $"当前交互模型：{row.InteractionKind}";
        _importSummary.Text = $"当前表面类型：{row.GeometryKind}\n计算/编辑状态：{(row.GeometryComputable ? "已支持" : "不支持，保留导入数据，只读")}";
        _scatterSummary.Text = row.Inspection is { } details
            ? $"当前散射模型：{(details.ScatteringKind == "none" ? "无" : details.ScatteringKind)}"
            : "当前散射模型：—";
        _coordinatesSummary.Text = row.Inspection is { } position
            ? $"X：{position.OriginX:0.######} mm    Y：{position.OriginY:0.######} mm    Z：{position.OriginZ:0.######} mm\n"
                + $"X 倾斜：{position.TiltXDegrees:0.######}°    Y 倾斜：{position.TiltYDegrees:0.######}°    Z 倾斜：{position.TiltZDegrees:0.######}°"
            : "表面坐标：—";
        UpdateGeometryPropertyVisibility();
    }

    private void UpdateGeometryPropertyVisibility()
    {
        _gratingProperties.IsVisible = _geometryPicker.SelectedItem is "平面光栅" or "标准曲面光栅";
        _thinLensProperties.IsVisible = !_gratingProperties.IsVisible
            && _grid.SelectedItem is SurfaceEditorRow { InteractionKind: "薄透镜" or "反射薄透镜" };
    }

    private static ListBoxItem PageItem(SurfacePropertyPage page, string title) => new()
    {
        Tag = page,
        Content = title,
        MinHeight = 28,
        Padding = new Thickness(10, 3)
    };

    private static StackPanel PropertyStack(params Control[] children)
    {
        var stack = new StackPanel { Spacing = 6, MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var child in children) stack.Children.Add(child);
        return stack;
    }

    private static Control PropertyRow(string caption, Control editor)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("116,*"), Width = 350, MinHeight = 32 };
        grid.Children.Add(new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center });
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.VerticalAlignment = VerticalAlignment.Center;
        if (editor is not Grid) editor.Width = double.NaN;
        grid.Children.Add(editor);
        Grid.SetColumn(editor, 1);
        return grid;
    }

    private static Grid WithInfinity(Control input, CheckBox infinite)
    {
        input.Width = double.NaN;
        input.HorizontalAlignment = HorizontalAlignment.Stretch;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        infinite.Margin = new Thickness(6, 0, 0, 0);
        grid.Children.Add(input);
        grid.Children.Add(infinite);
        Grid.SetColumn(infinite, 1);
        return grid;
    }

    private static TextBlock PropertyNote(string text)
    {
        var note = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 660, Margin = new Thickness(0, 6, 0, 0) };
        note.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        return note;
    }

    private static ComboBox UnavailablePicker(string value)
    {
        var picker = new ComboBox { IsEnabled = false, ItemsSource = new[] { $"{value}（未实现）" }, SelectedIndex = 0 };
        ToolTip.SetTip(picker, "尚未接入独立表面显示属性；此项不可编辑。当前显示沿用主题。");
        return picker;
    }

    private static CheckBox UnavailableFlag(string caption)
    {
        var flag = new CheckBox { Content = caption, IsEnabled = false };
        ToolTip.SetTip(flag, "尚未实现该选项的追迹语义，不可编辑。此处不代表导入文件中的原始状态。");
        return flag;
    }
}
