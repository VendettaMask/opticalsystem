using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class OpticalAssemblyWindow
{
    private readonly ProgressBar _activity = new() { IsIndeterminate = true, Width = 70, Height = 3, IsVisible = false };
    private Control? _emptyTable, _emptyPreview, _imageDetails;

    private Control BuildPresentation()
    {
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (button, icon) in new[] { (_calculate, "alignment-compute"), (_simulate, "alignment-reticle"), (_cancel, "action-stop") })
        {
            var label = button.Content?.ToString() ?? "";
            AutomationProperties.SetName(button, label);
            button.Height = double.NaN; button.MinHeight = 32; button.Padding = new(10, 5);
            ToolTip.SetTip(button, label);
            button.Content = CommandLabel(button == _calculate ? "计算共轭" : button == _simulate ? "更新图像" : "取消", icon);
            commands.Children.Add(button);
        }
        _simulate.Classes.Remove("accent");
        _source.FontSize = DisplayTypography.BodySmall; _source.FontWeight = FontWeight.Normal;
        _source.TextTrimming = TextTrimming.CharacterEllipsis; _source.VerticalAlignment = VerticalAlignment.Center;
        _source.Margin = new(16, 0, 0, 0); LaboratoryPresentation.Muted(_source);
        _source.Bind(ToolTip.TipProperty, new Binding(nameof(TextBlock.Text)) { Source = _source });
        var toolbar = new Grid { Name = "LaboratoryHeader", ColumnDefinitions = new("Auto,*"), Margin = new(10, 6) };
        toolbar.Children.Add(commands); Grid.SetColumn(_source, 1); toolbar.Children.Add(_source);

        var instrument = new StackPanel { Spacing = 6 };
        instrument.Children.Add(Field("自准直仪焦距 (mm)", _collimator));
        foreach (var pair in new[] { ("附加物镜焦距 (mm)", _head), ("测量 NA（近轴）", _na), ("找像位置偏移 (mm)", _offset) })
        { var field = Field(pair.Item1, pair.Item2); instrument.Children.Add(field); _focusedFields.Add(field); }
        foreach (var pair in new[] { ("仪器通光直径 (mm)", _pupil), ("物镜到入口顶点距离 (mm)", _distance) })
        { var field = Field(pair.Item1, pair.Item2); instrument.Children.Add(field); _infinityFields.Add(field); }
        _modeInfo.FontSize = DisplayTypography.BodySmall; LaboratoryPresentation.Muted(_modeInfo);
        var settings = new StackPanel
        {
            Children =
            {
                ParameterGroup("测量设置", "settings-general", true,
                    Field("观察方向", _direction), Field("测量模式", _probeMode), Field("测量波长 (nm)", _wavelength)),
                ParameterGroup("测量头参数", "alignment-measurement-head", true, instrument),
                ParameterGroup("叉丝与探测面", "alignment-reticle", false,
                    Field("叉丝总长 (mm)", _length), Field("线宽 (mm)", _lineWidth), Field("探测面宽度 (mm)", _sensor)),
                ParameterGroup("坐标与模拟说明", "information-note", false, _modeInfo,
                    LaboratoryPresentation.Hint("基于当前打开的镜头（含未保存修改） · 位置以第一实体表面顶点为零，沿设计 +Z 为正"),
                    LaboratoryPresentation.Hint("理想薄透镜测量头；镜组使用真实曲面往返追迹。显示几何像与归一化亮度，不含衍射、镀膜强度及实际仪器标定。"))
            }
        };
        var settingsScroll = LaboratoryPresentation.Scroll(settings); settingsScroll.Name = "AssemblySettings";
        var settingsPane = Pane(settingsScroll); settingsPane.Name = "AssemblyParameterPane";

        _grid.Columns.Add(Column("面", nameof(TableRow.Surface), 44));
        _grid.Columns.Add(Column("表面名称", nameof(TableRow.Label), 92));
        _grid.Columns.Add(Column("像类型", nameof(TableRow.Kind), 80));
        _grid.Columns[^1].Width = DataGridLength.SizeToCells;
        _grid.Columns.Add(Column("位置 (mm)", nameof(TableRow.Position), 108));
        _grid.Columns.Add(new DataGridTextColumn
        { Header = "计算说明", Binding = new Binding(nameof(TableRow.Message)), Width = new(1, DataGridLengthUnitType.Star), MinWidth = 170 });
        foreach (var column in _grid.Columns)
        {
            var header = new TextBlock { Text = column.Header?.ToString(), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            header.Bind(TextBlock.FontSizeProperty, new Binding(nameof(FontSize)) { Source = _grid });
            column.Header = header;
        }
        _grid.BorderThickness = new(0); _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _emptyTable = EmptyState("alignment-conjugates", "暂无共轭结果", null);
        _emptyTable.Name = "AssemblyEmptyTable";
        if (_emptyTable is StackPanel emptyContent && emptyContent.Children[^1] is TextBlock explanation)
            explanation.Bind(TextBlock.TextProperty, new Binding(nameof(TextBlock.Text)) { Source = _status });
        var tableBody = new Grid { Children = { _grid, _emptyTable } };
        var sortStrip = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(10, 7) };
        AutomationProperties.SetName(_sort, "按找像位置排序"); _sort.Content = "位置排序";
        ToolTip.SetTip(_sort, "按找像位置排序");
        sortStrip.Children.Add(LaboratoryPresentation.Heading("表面共轭位置", "alignment-conjugates"));
        Grid.SetColumn(_sort, 1); sortStrip.Children.Add(_sort);
        var table = new Grid { Name = "AssemblyConjugateCard", RowDefinitions = new("Auto,*") };
        table.Children.Add(HeaderBand(sortStrip)); Grid.SetRow(tableBody, 1); table.Children.Add(tableBody);
        var tablePane = Pane(table);

        _emptyPreview = EmptyState("alignment-reticle", "尚无叉丝图像", "计算共轭位置后，选择顶点像或球心像。");
        _emptyPreview.Name = "AssemblyEmptyPreview";
        var imageBody = new Grid { Name = "AssemblyDetectorViewport", MinHeight = 180, Children = { _image, _emptyPreview } };
        imageBody.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        _image.BindThemeResource(AssemblyReticleView.AxisBrushProperty, ThemeResourceBindings.TextSecondary);
        _image.Margin = new(8); _image.MinHeight = 0;
        _imageInfo.FontSize = DisplayTypography.BodySmall; _imageInfo.Margin = new(10, 6);
        LaboratoryPresentation.Muted(_imageInfo);
        var imageNotes = new ScrollViewer { Content = _imageInfo, MaxHeight = 150, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _imageDetails = new Expander
        {
            Name = "AssemblyImageDetails", Header = "图像信息", IsExpanded = true, Content = imageNotes,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var preview = new Grid { Name = "AssemblyPreviewCard", RowDefinitions = new("Auto,*,Auto") };
        preview.Children.Add(HeaderBand(new Grid { Margin = new(10, 7), Children = { LaboratoryPresentation.Heading("返回叉丝像", "alignment-reticle") } }));
        Grid.SetRow(imageBody, 1); preview.Children.Add(imageBody); Grid.SetRow(_imageDetails, 2); preview.Children.Add(_imageDetails);
        ToolTip.SetTip(imageBody, "探测面物理尺寸 · 固定毫米刻度");
        var previewPane = Pane(preview);

        var split = new Grid { Name = "AssemblyResultSplit", ColumnDefinitions = new("3*,5,2.3*") };
        var resultSplitter = Splitter("AssemblyResultSplitter"); Grid.SetColumn(resultSplitter, 1);
        split.Children.Add(tablePane); split.Children.Add(resultSplitter); Grid.SetColumn(previewPane, 2); split.Children.Add(previewPane);
        var tableTab = new TabItem { Header = "表面共轭位置" };
        var imageTab = new TabItem { Header = "返回叉丝像" };
        var tabs = new TabControl { Name = "AssemblyResultTabs", Items = { tableTab, imageTab }, SelectedIndex = 0, IsVisible = false };
        // Keep both panes in one parent. Tabs select visibility at narrow widths;
        // changing visual parents during layout can otherwise lose selection or fail.
        var resultHost = new Grid { Name = "AssemblyResults", RowDefinitions = new("Auto,*"), Children = { tabs, split } };
        Grid.SetRow(split, 1);
        var main = new Grid { Name = "AssemblyWorkspace", ColumnDefinitions = new("300,5,*"), Margin = new(8) };
        main.ColumnDefinitions[0].MinWidth = 250; main.ColumnDefinitions[0].MaxWidth = 500;
        main.ColumnDefinitions[2].MinWidth = 300;
        main.Children.Add(settingsPane);
        var settingsSplitter = Splitter("AssemblyParameterSplitter"); Grid.SetColumn(settingsSplitter, 1); main.Children.Add(settingsSplitter);
        Grid.SetColumn(resultHost, 2); main.Children.Add(resultHost);
        var tabbed = false;
        var tableSize = split.ColumnDefinitions[0].Width; var imageSize = split.ColumnDefinitions[2].Width;
        void ArrangeResults()
        {
            var narrow = resultHost.Bounds.Width < 790;
            if (tabbed != narrow)
            {
                if (narrow)
                { tableSize = split.ColumnDefinitions[0].Width; imageSize = split.ColumnDefinitions[2].Width; }
                tabbed = narrow;
                split.ColumnDefinitions[0].Width = narrow ? new(1, GridUnitType.Star) : tableSize;
                split.ColumnDefinitions[1].Width = new(narrow ? 0 : 5);
                split.ColumnDefinitions[2].Width = narrow ? new(0) : imageSize;
                Grid.SetColumn(previewPane, narrow ? 0 : 2);
            }
            tabs.IsVisible = narrow; resultSplitter.IsVisible = !narrow;
            tablePane.IsVisible = !narrow || tabs.SelectedIndex == 0;
            previewPane.IsVisible = !narrow || tabs.SelectedIndex == 1;
        }
        resultHost.SizeChanged += (_, _) => ArrangeResults();
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, tabs)) ArrangeResults(); };
        void UpdateTableDensity()
        {
            var scale = Math.Max(1, _grid.FontSize / 13);
            _grid.RowHeight = Math.Max(32, _grid.FontSize * 1.85);
            _grid.ColumnHeaderHeight = Math.Max(34, _grid.FontSize * 2);
            foreach (var column in _grid.Columns.OfType<DataGridTextColumn>()) column.FontSize = _grid.FontSize;
            foreach (var (index, width) in new[] { (0, 44), (1, 92), (3, 108) })
                _grid.Columns[index].Width = new(width * scale);
            main.ColumnDefinitions[0].Width = new(Math.Max(300, _grid.FontSize * 19));
        }
        _grid.PropertyChanged += (_, e) => { if (e.Property == FontSizeProperty) UpdateTableDensity(); };
        UpdateTableDensity();

        _status.FontSize = DisplayTypography.BodySmall; _status.TextWrapping = TextWrapping.NoWrap;
        _status.TextTrimming = TextTrimming.CharacterEllipsis; _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Bind(ToolTip.TipProperty, new Binding(nameof(TextBlock.Text)) { Source = _status });
        var statusContent = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(10, 5), ColumnSpacing = 12 };
        statusContent.Children.Add(_status); Grid.SetColumn(_activity, 1); statusContent.Children.Add(_activity);
        var statusBar = HeaderBand(statusContent); statusBar.Name = "AssemblyStatusBar";
        statusBar.BorderThickness = new(0, 1, 0, 0);
        statusBar.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.StatusBarBackground);
        var root = new Grid { RowDefinitions = new("Auto,*,Auto") };
        root.Children.Add(HeaderBand(toolbar)); Grid.SetRow(main, 1); root.Children.Add(main);
        Grid.SetRow(statusBar, 2); root.Children.Add(statusBar);
        return MainWindowTitleBar.Wrap(this, root);
    }

    private static Control CommandLabel(string text, string icon)
    {
        var glyph = new LocalIcon { IconName = icon, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { glyph, label } };
    }
    private static Border Pane(Control content)
    {
        var pane = new Border { Child = content, BorderThickness = new(1), ClipToBounds = true };
        pane.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        pane.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border); return pane;
    }
    private static Border HeaderBand(Control content)
    {
        var band = new Border { Child = content, BorderThickness = new(0, 0, 0, 1) };
        band.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        band.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border); return band;
    }
    private static GridSplitter Splitter(string name) => new()
    { Name = name, Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
    private static Control ParameterGroup(string title, string icon, bool expanded, params Control[] controls)
    {
        var content = new StackPanel { Spacing = 6, Margin = new(10, 3, 10, 10) };
        foreach (var control in controls) content.Children.Add(control);
        return new Expander
        {
            Header = LaboratoryPresentation.Heading(title, icon), Content = content, IsExpanded = expanded,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
    }
    private static Control EmptyState(string icon, string title, string? message)
    {
        var glyph = new LocalIcon { IconName = icon, Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.BindThemeResource(LocalIcon.StrokeProperty, ThemeResourceBindings.TextMuted);
        return new StackPanel
        {
            MaxWidth = 340, Margin = new(20), Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children = { glyph, new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap },
                LaboratoryPresentation.Muted(new TextBlock { Text = message, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }) }
        };
    }
    private void RefreshAssemblyPresentation()
    {
        if (_emptyTable is not null) _emptyTable.IsVisible = _result is null || _result.Rows.Count == 0;
        if (_emptyPreview is not null) _emptyPreview.IsVisible = _image.Result is null;
        if (_imageDetails is not null) _imageDetails.IsVisible = _result is not null;
        _activity.IsVisible = _busy;
    }
}
