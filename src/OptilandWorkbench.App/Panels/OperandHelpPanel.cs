using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed class OperandHelpPanel : UserControl
{
    private readonly IReadOnlyList<MeritOperandTypeDto> _allOperands;
    private readonly ObservableCollection<MeritOperandTypeDto> _visibleOperands = new();
    private readonly TextBox _search = new()
    {
        PlaceholderText = "搜索代码、名称、定义或参数",
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly ComboBox _supportFilter = new()
    {
        ItemsSource = new[] { "全部", "可计算", "兼容保留" },
        SelectedIndex = 0,
        MinWidth = 104
    };
    private readonly DataGrid _operandGrid;
    private readonly TextBlock _count = new();
    private readonly TextBlock _title = new()
    {
        FontSize = 20,
        FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _metadata = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _definition = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _calculation = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _parameters = new() { Spacing = 0 };

    public OperandHelpPanel(IOptimizationService optimization)
    {
        ArgumentNullException.ThrowIfNull(optimization);
        _allOperands = optimization.GetMeritOperandTypes();
        _operandGrid = CreateOperandGrid();
        _search.TextChanged += (_, _) => ApplyFilter();
        _supportFilter.SelectionChanged += (_, _) => ApplyFilter();
        _operandGrid.SelectionChanged += (_, _) => ShowSelectedOperand();

        AutomationProperties.SetName(_search, "搜索操作数");
        AutomationProperties.SetHelpText(_search, "按代码、中文名称、定义、计算说明或参数筛选操作数。可输入多个关键词。");
        AutomationProperties.SetName(_supportFilter, "操作数支持状态筛选");
        AutomationProperties.SetName(_operandGrid, "操作数列表");

        Content = BuildContent();
        ApplyFilter();
    }

    private Control BuildContent()
    {
        var filterBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(10, 10, 10, 8),
            Children = { _search, _supportFilter }
        };
        Grid.SetColumn(_supportFilter, 1);

        var leftPane = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children = { filterBar, _operandGrid }
        };
        Grid.SetRow(_operandGrid, 1);
        var countBar = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 7),
            Child = _count
        };
        countBar.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        _count.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        Grid.SetRow(countBar, 2);
        leftPane.Children.Add(countBar);

        var details = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(18, 14, 22, 24),
            Children =
            {
                _title,
                _metadata,
                Divider(),
                SectionTitle("定义"),
                _definition,
                SectionTitle("计算说明"),
                _calculation,
                ContributionNote(),
                SectionTitle("参数"),
                _parameters
            }
        };
        _metadata.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        var detailScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = details
        };
        AutomationProperties.SetName(detailScroller, "操作数定义与计算说明");

        var leftFrame = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = leftPane
        };
        leftFrame.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        leftFrame.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);

        var responsive = new ResponsiveTwoPaneGrid(
            leftFrame,
            detailScroller,
            "320,1,*",
            "260,1,*",
            breakpoint: 760);
        responsive.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Workspace);
        return responsive;
    }

    private DataGrid CreateOperandGrid()
    {
        var grid = new DataGrid
        {
            ItemsSource = _visibleOperands,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserResizeColumns = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            SelectionMode = DataGridSelectionMode.Single,
            RowHeight = UiDensity.CompactTableRowHeight,
            ColumnHeaderHeight = UiDensity.TableHeaderHeight,
            BorderThickness = new Thickness(0)
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "代码",
            Binding = new Binding(nameof(MeritOperandTypeDto.Code)),
            Width = new DataGridLength(78)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "名称",
            Binding = new Binding(nameof(MeritOperandTypeDto.DisplayName)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        return grid;
    }

    private void ApplyFilter()
    {
        var selectedCode = (_operandGrid.SelectedItem as MeritOperandTypeDto)?.Code;
        var filter = _supportFilter.SelectedIndex switch
        {
            1 => OperandHelpSupportFilter.Executable,
            2 => OperandHelpSupportFilter.CompatibilityOnly,
            _ => OperandHelpSupportFilter.All
        };
        var filtered = OperandHelpProjection.Filter(_allOperands, _search.Text, filter);
        _visibleOperands.Clear();
        foreach (var operand in filtered)
        {
            _visibleOperands.Add(operand);
        }

        _count.Text = $"显示 {filtered.Count} / {_allOperands.Count}";
        _operandGrid.SelectedItem = selectedCode is null
            ? filtered.FirstOrDefault()
            : filtered.FirstOrDefault(operand => operand.Code == selectedCode)
                ?? filtered.FirstOrDefault();
        if (_operandGrid.SelectedItem is null)
        {
            ClearDetails();
            return;
        }

        ShowSelectedOperand();
    }

    private void ShowSelectedOperand()
    {
        if (_operandGrid.SelectedItem is not MeritOperandTypeDto operand)
        {
            ClearDetails();
            return;
        }

        _title.Text = $"{operand.Code} · {operand.DisplayName}";
        _metadata.Text = operand.CompatibilityOnly
            ? $"{operand.Category} · 仅兼容保留"
            : $"{operand.Category} · 当前可计算";
        _definition.Text = operand.Description;
        _calculation.Text = operand.Calculation;
        AutomationProperties.SetName(this, $"操作数帮助：{operand.Code} {operand.DisplayName}");
        ShowParameters(operand);
    }

    private void ShowParameters(MeritOperandTypeDto operand)
    {
        _parameters.Children.Clear();
        if (operand.Parameters is not { Count: > 0 })
        {
            _parameters.Children.Add(new TextBlock
            {
                Text = "Workbench 原生操作数使用评价函数编辑器中的 Surface、Field、Wavelength、Hx、Hy、Px、Py 通用字段；实际使用项见上方计算说明。",
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var parameter in operand.Parameters)
        {
            _parameters.Children.Add(ParameterRow(parameter));
        }
    }

    private static Control ParameterRow(MeritOperandParameterDto parameter)
    {
        var valueKind = TranslateValueKind(parameter.ValueKind);
        var unit = string.IsNullOrWhiteSpace(parameter.Unit) ? string.Empty : $" · {parameter.Unit}";
        var usage = parameter.IsEditable ? string.Empty : " · 未使用";
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("72,150,*"),
            ColumnSpacing = 8,
            Children =
            {
                new TextBlock { Text = parameter.Slot, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = TranslateParameterName(parameter.DisplayName) },
                new TextBlock
                {
                    Text = $"{valueKind}{unit}{usage}",
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
        Grid.SetColumn(row.Children[1], 1);
        Grid.SetColumn(row.Children[2], 2);
        row.Children[2].BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);

        var frame = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 7),
            Child = row
        };
        frame.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return frame;
    }

    private void ClearDetails()
    {
        _title.Text = "没有匹配的操作数";
        _metadata.Text = "请调整搜索词或支持状态筛选。";
        _definition.Text = string.Empty;
        _calculation.Text = string.Empty;
        _parameters.Children.Clear();
        AutomationProperties.SetName(this, "操作数帮助");
    }

    private static Control ContributionNote()
    {
        var text = new TextBlock
        {
            Text = "通用贡献：除说明和控制行外，贡献 = |Weight| × (Value − Target)²。边界操作数满足约束时会把 Value 钳到 Target，因此贡献为 0。",
            TextWrapping = TextWrapping.Wrap
        };
        text.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        return text;
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 8, 0, 0)
    };

    private static Border Divider()
    {
        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4)
        };
        divider.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Border);
        return divider;
    }

    private static string TranslateParameterName(string name) => name switch
    {
        "Surface" => "表面",
        "Start surface" => "起始表面",
        "End surface" => "终止表面",
        "Wavelength" => "波长",
        "Field" => "视场",
        "Rings" => "瞳孔环数",
        "Operand row" => "操作数行",
        "Operand row 1" => "操作数行 1",
        "Operand row 2" => "操作数行 2",
        "First operand row" => "首操作数行",
        "Last operand row" => "末操作数行",
        "Spatial frequency" => "空间频率",
        "Edge code" => "边缘方向代码",
        "Sampling" => "采样",
        "Polarization" => "偏振标志",
        "Absolute" => "绝对值标志",
        "Flag" => "标志",
        "Mode" => "模式",
        "Unused" => "未使用",
        _ => name
    };

    private static string TranslateValueKind(string valueKind) => valueKind switch
    {
        "Integer" => "整数",
        "RowReference" => "前序行引用",
        "RowRangeEnd" => "行范围终点",
        "Flag" => "标志",
        "Surface" => "表面编号",
        "EndSurface" => "终止表面编号",
        "Field" => "视场编号",
        "Wavelength" => "波长编号",
        "NormalizedField" => "归一化视场",
        "PupilCoordinate" => "归一化瞳孔坐标",
        "SpatialFrequency" => "空间频率",
        "Numeric" => "数值",
        _ => valueKind
    };
}
