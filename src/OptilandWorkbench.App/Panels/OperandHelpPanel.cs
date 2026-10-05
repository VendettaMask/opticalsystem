using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed class OperandHelpPanel : UserControl
{
    private readonly IReadOnlyList<MeritOperandTypeDto> _allOperands;
    private readonly TreeView _operandTree = new() { AutoScrollToSelectedItem = true };
    private readonly Dictionary<string, TreeViewItem> _treeItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expandedGroups = new(StringComparer.Ordinal);
    private bool _autoExpanded;
    private bool _updatingTree;
    private readonly TextBox _search = new()
    {
        PlaceholderText = "搜索分类、代码、名称或参数",
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly ComboBox _supportFilter = new()
    {
        ItemsSource = new[] { "全部", "可计算", "兼容保留" },
        SelectedIndex = 0,
        MinWidth = 104
    };
    private readonly TextBlock _count = new();
    private readonly TextBlock _level = new() { FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _title = new()
    {
        FontSize = DisplayTypography.LargeTitle,
        FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _metadata = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _definition = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _calculation = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _parameters = new() { Spacing = 0 };
    private readonly TextBlock _definitionHeading = SectionTitle("定义");
    private readonly TextBlock _calculationHeading = SectionTitle("计算说明");
    private readonly TextBlock _parameterHeading = SectionTitle("参数");
    private readonly Control _contribution = ContributionNote();
    private readonly TextBlock _childrenHeading = SectionTitle("二级目录 · 操作数族");
    private readonly StackPanel _children = new() { Spacing = 6 };
    private readonly ScrollViewer _detailScroller = new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
    };

    public OperandHelpPanel(IOptimizationService optimization)
    {
        ArgumentNullException.ThrowIfNull(optimization);
        _allOperands = optimization.GetMeritOperandTypes();
        _search.TextChanged += (_, _) => ApplyFilter();
        _supportFilter.SelectionChanged += (_, _) => ApplyFilter();
        _operandTree.SelectionChanged += (_, _) =>
        {
            if (!_updatingTree) ShowSelectedNode();
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_operandTree, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);

        AutomationProperties.SetName(_search, "搜索操作数");
        AutomationProperties.SetHelpText(_search, "按分类、代码、中文名称、定义、计算说明或参数筛选操作数。可输入多个关键词；结果保留分类路径。");
        AutomationProperties.SetName(_supportFilter, "操作数支持状态筛选");
        AutomationProperties.SetName(_operandTree, "操作数分类树");
        AutomationProperties.SetHelpText(_operandTree, "按功能大类、操作数族、具体操作数逐级浏览。使用左右方向键展开或折叠，上下方向键选择。");

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

        var hierarchyGuide = new TextBlock
        {
            Text = "功能大类 › 操作数族 › 具体操作数",
            Margin = new Thickness(10, 0, 10, 8),
            TextWrapping = TextWrapping.Wrap
        };
        hierarchyGuide.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        var leftPane = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Children = { filterBar, hierarchyGuide, _operandTree }
        };
        Grid.SetRow(hierarchyGuide, 1);
        Grid.SetRow(_operandTree, 2);
        var countBar = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 7),
            Child = _count
        };
        countBar.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        _count.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        Grid.SetRow(countBar, 3);
        leftPane.Children.Add(countBar);

        var details = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(18, 14, 22, 24),
            Children =
            {
                _level,
                _title,
                _metadata,
                _status,
                Divider(),
                _definitionHeading,
                _definition,
                _calculationHeading,
                _calculation,
                _contribution,
                _parameterHeading,
                _parameters,
                _childrenHeading,
                _children
            }
        };
        _level.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        _metadata.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        _status.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        _detailScroller.Content = details;
        AutomationProperties.SetName(_detailScroller, "操作数定义与计算说明");

        var leftFrame = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = leftPane
        };
        leftFrame.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        leftFrame.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);

        var responsive = new ResponsiveTwoPaneGrid(
            leftFrame,
            _detailScroller,
            "320,1,*",
            "260,1,*",
            breakpoint: 760);
        responsive.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Workspace);
        return responsive;
    }

    private TreeViewItem CreateTreeItem(OperandHelpNode node, bool expandMatches)
    {
        var header = new StackPanel { Spacing = 2, Margin = new Thickness(0, 3) };
        header.Children.Add(new TextBlock
        {
            Text = node.Operand is null ? $"{node.Title}  ({node.Operands.Count})" : node.Title,
            FontWeight = node.Level switch
            {
                OperandHelpLevel.Category => FontWeight.Bold,
                OperandHelpLevel.Family => FontWeight.SemiBold,
                _ => FontWeight.Normal
            },
            TextWrapping = TextWrapping.Wrap
        });
        if (node.Operand is { } operand)
        {
            var status = new TextBlock { Text = OperandHelpTaxonomy.Status(operand), TextWrapping = TextWrapping.Wrap };
            status.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
            header.Children.Add(status);
        }
        var item = new TreeViewItem
        {
            Header = header,
            Tag = node,
            IsExpanded = node.Children.Count > 0 && (expandMatches || _expandedGroups.Contains(node.Key))
        };
        AutomationProperties.SetName(item, node.Operand is null
            ? $"{node.Path}，{node.Operands.Count} 项"
            : $"{node.Title}，{OperandHelpTaxonomy.Status(node.Operand)}");
        ToolTip.SetTip(item, AutomationProperties.GetName(item));
        _treeItems.Add(node.Key, item);
        item.ItemsSource = node.Children.Select(child => CreateTreeItem(child, expandMatches)).ToArray();
        return item;
    }

    private void ApplyFilter()
    {
        var selectedKey = ((_operandTree.SelectedItem as TreeViewItem)?.Tag as OperandHelpNode)?.Key;
        if (!_autoExpanded)
        {
            foreach (var pair in _treeItems)
            {
                if (pair.Value.IsExpanded) _expandedGroups.Add(pair.Key);
                else _expandedGroups.Remove(pair.Key);
            }
        }
        var filter = _supportFilter.SelectedIndex switch
        {
            1 => OperandHelpSupportFilter.Executable,
            2 => OperandHelpSupportFilter.CompatibilityOnly,
            _ => OperandHelpSupportFilter.All
        };
        var filtered = OperandHelpProjection.Filter(_allOperands, _search.Text, filter);
        var roots = OperandHelpProjection.BuildTree(filtered);
        _autoExpanded = !string.IsNullOrWhiteSpace(_search.Text) || filter != OperandHelpSupportFilter.All;
        _updatingTree = true;
        try
        {
            _operandTree.SelectedItem = null;
            _treeItems.Clear();
            _operandTree.ItemsSource = roots.Select(node => CreateTreeItem(node, _autoExpanded)).ToArray();
            var key = selectedKey is not null && _treeItems.ContainsKey(selectedKey)
                ? selectedKey : _autoExpanded ? filtered.FirstOrDefault()?.Code : roots.FirstOrDefault()?.Key;
            if (key is not null)
            {
                foreach (var root in _operandTree.Items.OfType<TreeViewItem>()) ExpandTo(root, key);
                _operandTree.SelectedItem = _treeItems[key];
            }
        }
        finally { _updatingTree = false; }
        _count.Text = $"{roots.Count} 类 · 显示 {filtered.Count} / {_allOperands.Count} 项";
        ShowSelectedNode();
    }

    private static bool ExpandTo(TreeViewItem item, string key)
    {
        if ((item.Tag as OperandHelpNode)?.Key == key) return true;
        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            if (!ExpandTo(child, key)) continue;
            item.IsExpanded = true;
            return true;
        }
        return false;
    }

    private void ShowSelectedNode()
    {
        if ((_operandTree.SelectedItem as TreeViewItem)?.Tag is not OperandHelpNode node)
        {
            ClearDetails();
            return;
        }
        var operand = node.Operand;
        var isLeaf = operand is not null;
        _detailScroller.Offset = default;
        _level.IsVisible = true;
        _level.Text = node.Level switch
        {
            OperandHelpLevel.Category => "一级目录 · 功能大类",
            OperandHelpLevel.Family => "二级目录 · 操作数族",
            _ => "三级目录 · 具体操作数"
        };
        _title.Text = node.Title;
        _metadata.IsVisible = node.Level != OperandHelpLevel.Category;
        _metadata.Text = node.Path;
        _status.IsVisible = isLeaf;
        _status.Text = isLeaf ? OperandHelpTaxonomy.Status(operand!) : string.Empty;
        _definitionHeading.Text = isLeaf ? "定义" : "分类概览";
        _calculationHeading.Text = isLeaf ? "计算说明" : "包含内容";
        _definitionHeading.IsVisible = true;
        _calculationHeading.IsVisible = isLeaf || !string.IsNullOrEmpty(node.Summary);
        _calculation.IsVisible = _calculationHeading.IsVisible;
        _parameterHeading.IsVisible = isLeaf;
        _parameters.IsVisible = isLeaf;
        _contribution.IsVisible = isLeaf && !operand!.CompatibilityOnly;
        ShowChildren(node);
        AutomationProperties.SetName(this, $"操作数帮助：{node.Title}");
        if (operand is null)
        {
            var executable = node.Operands.Count(item => !item.CompatibilityOnly);
            _definition.Text = $"当前显示 {node.Operands.Count} 项操作数，其中 {executable} 项有计算路径、{node.Operands.Count - executable} 项仅保留。具体支持范围见各项说明。";
            _calculation.Text = node.Summary;
            _parameters.Children.Clear();
            return;
        }
        var family = OperandHelpTaxonomy.FamilyFor(operand.Code);
        _definition.Text = family.Id == "grin-point"
            ? $"{OperandHelpTaxonomy.DisplayName(operand)}，用于指定表面、指定波长下的 GRIN 介质。\n{family.Summary}"
            : operand.Code == "HYLD"
                ? "指定真实光线在给定表面、给定波长下的高良率贡献，与入射或出射光线方向余弦、表面法线及两侧折射率有关。"
                : operand.Description;
        _calculation.Text = operand.Calculation;
        _parameterHeading.Text = operand.CompatibilityOnly ? "保留的原始参数" : "参数";
        ShowParameters(operand);
    }

    private void ShowChildren(OperandHelpNode node)
    {
        _children.Children.Clear();
        _children.IsVisible = _childrenHeading.IsVisible = node.Children.Count > 0;
        _childrenHeading.Text = node.Level == OperandHelpLevel.Category
            ? "二级目录 · 操作数族" : "三级目录 · 具体操作数";
        foreach (var child in node.Children)
        {
            var label = new StackPanel { Spacing = 3 };
            label.Children.Add(new TextBlock
            {
                Text = child.Title,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            var summary = new TextBlock
            {
                Text = child.Operand is { } operand
                    ? OperandHelpTaxonomy.Status(operand)
                    : $"{child.Operands.Count} 项 · {child.Operands.Count(item => !item.CompatibilityOnly)} 项有计算路径",
                TextWrapping = TextWrapping.Wrap
            };
            summary.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
            label.Children.Add(summary);
            var button = new Button
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 8)
            };
            AutomationProperties.SetName(button, $"打开{child.Title}");
            AutomationProperties.SetHelpText(button, $"{child.Path}。{summary.Text}");
            button.Click += (_, _) =>
            {
                foreach (var root in _operandTree.Items.OfType<TreeViewItem>()) ExpandTo(root, child.Key);
                _operandTree.SelectedItem = _treeItems[child.Key];
                _treeItems[child.Key].Focus();
            };
            _children.Children.Add(button);
        }
    }

    private void ShowParameters(MeritOperandTypeDto operand)
    {
        _parameters.Children.Clear();
        if (operand.CompatibilityOnly)
        {
            _parameters.Children.Add(new TextBlock
            {
                Text = "以下为导入文件保留的参数槽位；未核实的槽位不代表已完成参数定义，当前不参与计算。",
                TextWrapping = TextWrapping.Wrap
            });
        }
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
        _level.IsVisible = false;
        _status.IsVisible = false;
        _title.Text = "没有匹配的操作数";
        _metadata.IsVisible = true;
        _metadata.Text = "请调整搜索词或支持状态筛选。";
        _definition.Text = string.Empty;
        _calculation.Text = string.Empty;
        _parameters.Children.Clear();
        _definitionHeading.IsVisible = false;
        _calculationHeading.IsVisible = false;
        _parameterHeading.IsVisible = false;
        _parameters.IsVisible = false;
        _contribution.IsVisible = false;
        _childrenHeading.IsVisible = false;
        _children.IsVisible = false;
        _children.Children.Clear();
        AutomationProperties.SetName(this, "操作数帮助");
    }

    private static Control ContributionNote()
    {
        var text = new TextBlock
        {
            Text = "通用贡献：除说明、控制行和 EQUA 外，贡献 = |Weight| × (Value − Target)²。边界操作数满足约束时会把 Value 钳到 Target，因此贡献为 0；EQUA 使用 Target 作为相等容差，贡献 = |Weight| × Value²。",
            TextWrapping = TextWrapping.Wrap
        };
        text.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        return text;
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = DisplayTypography.SectionTitle,
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
        "Mode (0 flat edge, 1 equation)" => "模式（0 平边，1 方程）",
        "Tilt About X" => "绕 X 倾角",
        "Tilt About Y" => "绕 Y 倾角",
        "Tilt About Z" => "绕 Z 倾角",
        "BFS data (0..7)" => "拟合球面数据（0..7）",
        "Minimum radius" => "最小径向坐标",
        "Maximum radius" => "最大径向坐标",
        "Start surface" => "起始表面",
        "End surface" => "终止表面",
        "Wavelength" => "波长",
        "Wavelength (0=primary)" => "波长（0=主波长）",
        "Field (0=axial)" => "视场（0=轴上）",
        "Signed Wave" => "带符号波长",
        "Ref Field" => "参考视场",
        "X Width" => "X 全宽",
        "Y Width" => "Y 全宽",
        "Field" => "视场",
        "Rings" => "瞳孔环数",
        "Operand row" => "操作数行",
        "Operand row 1" => "操作数行 1",
        "Operand row 2" => "操作数行 2",
        "First operand row" => "首操作数行",
        "Last operand row" => "末操作数行",
        "Spatial frequency" => "空间频率",
        "Edge code" => "边缘方向代码",
        "Use X" => "X 方向标志",
        "Embedded waist radius" => "嵌入模束腰半径",
        "Surface 1 to waist" => "面 1 到束腰距离",
        "M squared" => "M² 因子",
        "Sampling" => "采样",
        "Pupil sampling (1..4)" => "瞳孔采样（1..4，受工作预算限制）",
        "Image sampling (1..4)" => "像面采样（1..4，受工作预算限制）",
        "Surface (0=restore)" => "像面（0 恢复原像面）",
        "Profile data (1..8)" => "面形统计数据（1..8）",
        "Samp (33..513)" => "采样（1..5，即 33..513）",
        "Off-axis" => "离轴坐标",
        "Remove" => "移除参考面",
        "BFS" => "最佳拟合球面类型",
        "Orientation (0..3)" => "方向（0..3）",
        "Refocus" => "近轴调焦（0/1）",
        "Sampling (1..2)" => "采样（1..2）",
        "All configurations (0 only)" => "所有配置（当前仅 0）",
        "Image delta (0=automatic)" => "图像间隔（0 为自动）",
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
        "SignedWavelength" => "正/负波长编号（百分比/长度）",
        "NormalizedField" => "归一化视场",
        "PupilCoordinate" => "归一化瞳孔坐标",
        "SpatialFrequency" => "空间频率",
        "Numeric" => "数值",
        _ => valueKind
    };
}
