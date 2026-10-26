using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class OpticalAssemblyWindow
{
    private Control BuildPresentation()
    {
        var header = LaboratoryPresentation.Header("光学装调", "scan-eye",
            "计算表面共轭 → 选择找像位置 → 检查返回叉丝像", LaboratoryPresentation.Muted(_source),
            _calculate, _simulate, _cancel);
        _source.FontSize = DisplayTypography.BodySmall;
        _source.FontWeight = FontWeight.Normal;
        _modeInfo.FontSize = DisplayTypography.BodySmall;
        LaboratoryPresentation.Muted(_modeInfo);
        var instrument = new StackPanel { Spacing = 8 };
        instrument.Children.Add(Field("自准直仪焦距 (mm)", _collimator));
        foreach (var pair in new[] { ("附加物镜焦距 (mm)", _head), ("测量 NA（近轴）", _na), ("找像位置偏移 (mm)", _offset) })
        {
            var field = Field(pair.Item1, pair.Item2); instrument.Children.Add(field); _focusedFields.Add(field);
        }
        foreach (var pair in new[] { ("仪器通光直径 (mm)", _pupil), ("物镜到入口顶点距离 (mm)", _distance) })
        {
            var field = Field(pair.Item1, pair.Item2); instrument.Children.Add(field); _infinityFields.Add(field);
        }
        var settings = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                LaboratoryPresentation.Section("测量设置", "sliders-horizontal",
                    Field("观察方向", _direction), Field("测量模式", _probeMode), Field("测量波长 (nm)", _wavelength)),
                LaboratoryPresentation.Section("测量头参数", "scan-eye", instrument, _modeInfo),
                LaboratoryPresentation.Section("叉丝与探测面", "crosshair",
                    Field("叉丝总长 (mm)", _length), Field("线宽 (mm)", _lineWidth), Field("探测面宽度 (mm)", _sensor)),
                new Expander
                {
                    Header = "坐标与模拟说明", HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = new StackPanel { Spacing = 8, Margin = new Thickness(12), Children =
                    {
                        LaboratoryPresentation.Hint("基于当前打开的镜头（含未保存修改） · 位置以第一实体表面顶点为零，沿设计 +Z 为正"),
                        LaboratoryPresentation.Hint("理想薄透镜测量头；镜组使用真实曲面往返追迹。显示几何像与归一化亮度，不含衍射、镀膜强度及实际仪器标定。")
                    } }
                }
            }
        };
        _grid.Columns.Add(Column("面", nameof(TableRow.Surface), 42));
        _grid.Columns.Add(Column("表面名称", nameof(TableRow.Label), 85));
        _grid.Columns.Add(Column("像类型", nameof(TableRow.Kind), 75));
        _grid.Columns.Add(Column("位置 (mm)", nameof(TableRow.Position), 110));
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "计算说明",
            Binding = new Binding(nameof(TableRow.Message)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        _grid.BorderThickness = new Thickness(0);
        var table = new Grid { RowDefinitions = new("Auto,*") };
        table.Children.Add(new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(14, 12),
            Children = { LaboratoryPresentation.Heading("表面共轭位置", "list-filter"), _sort }
        });
        Grid.SetRow(_grid, 1); table.Children.Add(_grid);
        var tableCard = LaboratoryPresentation.Card(table, "AssemblyConjugateCard");
        var preview = new Grid { RowDefinitions = new("Auto,*,Auto") };
        preview.Children.Add(new StackPanel
        {
            Spacing = 5,
            Margin = new Thickness(14, 12),
            Children =
            { LaboratoryPresentation.Heading("返回叉丝像", "scan-eye"), LaboratoryPresentation.Hint("探测面物理尺寸 · 固定毫米刻度") }
        });
        _image.Margin = new Thickness(12, 0, 12, 12);
        Grid.SetRow(_image, 1); preview.Children.Add(_image);
        _imageInfo.FontSize = DisplayTypography.BodySmall;
        LaboratoryPresentation.Muted(_imageInfo);
        Grid.SetRow(_imageInfo, 2); preview.Children.Add(_imageInfo);
        var previewCard = LaboratoryPresentation.Card(preview, "AssemblyPreviewCard");
        var results = new Grid { ColumnSpacing = 12, RowSpacing = 12, Children = { tableCard, previewCard } };
        var main = new Grid { Name = "AssemblyWorkspace", ColumnDefinitions = new("288,*"), ColumnSpacing = 14 };
        var settingsScroll = LaboratoryPresentation.Scroll(settings);
        settingsScroll.Name = "AssemblySettings";
        settingsScroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden;
        main.Children.Add(settingsScroll);
        var resultScroll = LaboratoryPresentation.Scroll(results);
        Grid.SetColumn(resultScroll, 1); main.Children.Add(resultScroll);
        bool? previousStacked = null;
        main.SizeChanged += (_, _) =>
        {
            var stacked = main.Bounds.Width < 1080;
            if (previousStacked != stacked)
            {
                previousStacked = stacked;
                results.ColumnDefinitions = new(stacked ? "*" : "3*,2*");
                results.RowDefinitions = new(stacked ? "320,Auto" : "*");
                Grid.SetColumn(previewCard, stacked ? 0 : 1); Grid.SetRow(previewCard, stacked ? 1 : 0);
            }
            results.Height = stacked ? double.NaN : Math.Max(530, main.Bounds.Height);
        };
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 12, Margin = new Thickness(16) };
        root.Children.Add(header); Grid.SetRow(main, 1); root.Children.Add(main);
        var status = LaboratoryPresentation.Status(_status); Grid.SetRow(status, 2); root.Children.Add(status);
        return MainWindowTitleBar.Wrap(this, root);
    }
}
