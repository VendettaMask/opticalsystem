using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class AnalysisPanel
{
    private static Control BuildSeidelReportSections(AnalysisViewDto view, IReadOnlyList<AnalysisReportSectionDto> sections,
        string file, DateTimeOffset generatedAt, TextBox report)
    {
        var metadata = new TextBlock
        {
            Text = $"{Path.GetFileName(file)}   ·   {generatedAt.LocalDateTime.ToString("yyyy/M/d", CultureInfo.InvariantCulture)}",
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = DisplayTypography.BodySmall
        };
        metadata.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
        ToolTip.SetTip(metadata, file);
        string Metric(string label) => FindRowText(view, label, "—");
        var overview = new WrapPanel
        {
            Margin = new Thickness(0, 5, 0, 0),
            Children =
            {
                SeidelMetric("波长 (µm)", Metric("分析波长 (µm)")),
                SeidelMetric("佩兹伐半径 (mm)", Metric("佩兹伐半径 (mm)")),
                SeidelMetric("光学不变量", Metric("光学不变量")),
                SeidelMetric("主光线斜率", $"物方 {Metric("物方主光线斜率")}   像方 {Metric("像方主光线斜率")}"),
                SeidelMetric("边缘光线斜率", $"物方 {Metric("物方边缘光线斜率")}   像方 {Metric("像方边缘光线斜率")}")
            }
        };
        var heading = new StackPanel
        {
            Margin = new Thickness(16, 12, 16, 6),
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = "赛德尔系数", FontSize = DisplayTypography.SectionTitle, FontWeight = FontWeight.SemiBold },
                metadata,
                overview
            }
        };
        var tabs = new TabControl
        {
            Margin = new Thickness(8, 0, 8, 8),
            TabStripPlacement = Avalonia.Controls.Dock.Top
        };
        foreach (var section in sections)
        {
            tabs.Items.Add(SeidelReportTab(section.Label, BuildSeidelTableSection(section)));
        }
        tabs.Items.Add(SeidelReportTab("文本", report));
        tabs.SelectedIndex = 0;
        DockPanel.SetDock(heading, Avalonia.Controls.Dock.Top);
        var panel = new DockPanel { Children = { heading, tabs } };
        panel.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Surface);
        return panel;
    }

    private static TabItem SeidelReportTab(string title, Control content) => new()
    {
        Header = title,
        Content = content,
        FontSize = DisplayTypography.Body,
        FontWeight = FontWeight.Medium,
        MinHeight = DisplayTypography.Scale(32),
        Padding = new Thickness(10, 5)
    };

    private static Control SeidelMetric(string label, string value)
    {
        var caption = new TextBlock { Text = label, FontSize = DisplayTypography.Caption };
        caption.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
        var reading = new SelectableTextBlock { Text = value, FontSize = DisplayTypography.Body, FontWeight = FontWeight.SemiBold };
        reading.BindThemeResource(TextBlock.FontFamilyProperty, ProductFonts.NumericResource);
        return new StackPanel { Margin = new Thickness(0, 2, 18, 7), Spacing = 2, Children = { caption, reading } };
    }

    private static Control BuildSeidelTableSection(AnalysisReportSectionDto section)
    {
        var table = section.Table;
        var grid = new DataGrid
        {
            ItemsSource = table.Rows,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = false,
            CanUserReorderColumns = false,
            CanUserResizeColumns = true,
            FrozenColumnCount = 1,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            RowHeight = DisplayTypography.Scale(27),
            ColumnHeaderHeight = DisplayTypography.Scale(30)
        };
        grid.Styles.Add(new Style(selector => selector.OfType<DataGridColumnHeader>())
        {
            Setters = { new Setter(DataGridColumnHeader.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) }
        });
        for (var column = 0; column < table.Columns.Count; column++)
        {
            var index = column;
            var longest = table.Rows.Select(row => row[index].Length).DefaultIfEmpty(0).Max();
            // Leave room for the native vertical scroll bar over the last visible cell.
            var trailingInset = index == table.Columns.Count - 1 ? 16 : 0;
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = table.Columns[index],
                HeaderTemplate = new FuncDataTemplate<string>((label, _) => new TextBlock
                {
                    Text = label,
                    TextAlignment = index == 0 ? TextAlignment.Left : TextAlignment.Right,
                    Margin = new Thickness(0, 0, trailingInset, 0)
                }),
                Width = index == 0 ? new DataGridLength(DisplayTypography.Scale(63)) : new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = DisplayTypography.Scale(index == 0 ? 56 : Math.Max(68, longest * 8 + 14) + trailingInset),
                ClipboardContentBinding = new Binding($"[{index}]"),
                CellTemplate = new FuncDataTemplate<IReadOnlyList<string>>((row, _) =>
                {
                    var total = row?[0] == "累计";
                    var cell = new TextBlock
                    {
                        Text = row?[index],
                        TextAlignment = index == 0 ? TextAlignment.Left : TextAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 8 + trailingInset, 0),
                        FontSize = DisplayTypography.BodySmall,
                        FontWeight = total ? FontWeight.SemiBold : FontWeight.Normal
                    };
                    cell.BindThemeResource(TextBlock.FontFamilyProperty, ProductFonts.NumericResource);
                    if (total) cell.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextAccent);
                    return cell;
                })
            });
        }

        var caption = new TextBlock
        {
            Text = $"{section.Title}  ·  {section.Unit}",
            Margin = new Thickness(8, 8, 8, 7),
            FontSize = DisplayTypography.CardTitle,
            FontWeight = FontWeight.SemiBold
        };
        DockPanel.SetDock(caption, Avalonia.Controls.Dock.Top);
        return new DockPanel { Children = { caption, grid } };
    }
}
