using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed partial class AnalysisPanel
{
private static Control BuildSinglePlot(AnalysisViewDto view)
    {
        var plot = new AnalysisPlotControl
        {
            Series = view.Series,
            PlotOptions = view.PlotOptions,
            MinHeight = 360
        };
        var wavelengthEntries = WavelengthLegendEntries(view.Series);
        if (wavelengthEntries.Count == 0)
        {
            return new Grid
            {
                Children =
                {
                    plot,
                    EmptyPlotMessage(view.Series.Count == 0)
                }
            };
        }

        var original = view.Series.ToArray();
        var enabled = wavelengthEntries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        var legendBelow = view.PlotOptions.LegendBelow;
        var legend = BuildSelectableWavelengthLegend(wavelengthEntries, (key, isVisible) =>
        {
            if (isVisible)
            {
                enabled.Add(key);
            }
            else
            {
                enabled.Remove(key);
            }

            plot.Series = original.Where(series =>
                !TryGetWavelengthLegend(series.Name, out var seriesKey, out _)
                || enabled.Contains(seriesKey)).ToArray();
        }, legendBelow ? Orientation.Horizontal : Orientation.Vertical);
        plot.PlotOptions = view.PlotOptions with { ShowLegend = false };
        var layout = new Grid
        {
            ColumnDefinitions = legendBelow ? new ColumnDefinitions("*") : new ColumnDefinitions("*,Auto"),
            RowDefinitions = legendBelow ? new RowDefinitions("*,Auto") : new RowDefinitions("*")
        };
        layout.Children.Add(plot);
        layout.Children.Add(EmptyPlotMessage(view.Series.Count == 0));
        if (legendBelow)
        {
            Grid.SetRow(legend, 1);
        }
        else
        {
            Grid.SetColumn(legend, 1);
            legend.VerticalAlignment = VerticalAlignment.Center;
        }

        layout.Children.Add(legend);
        return layout;
    }

    private static Control BuildPanePlot(IReadOnlyList<AnalysisPlotPaneDto> panes, int requestedColumns)
    {
        var baseColumns = Math.Clamp(requestedColumns, 1, Math.Max(1, panes.Count));
        var baseRows = (int)Math.Ceiling(panes.Count / (double)baseColumns);
        var columns = baseRows <= 3
            ? baseColumns
            : Math.Min(
                panes.Count,
                Math.Min(6, baseColumns * (int)Math.Ceiling(baseRows / 3.0)));
        var rows = (int)Math.Ceiling(panes.Count / (double)columns);
        var paneGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", columns))),
            RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("*", rows)))
        };
        var plots = new List<AnalysisPlotControl>(panes.Count);
        for (var index = 0; index < panes.Count; index++)
        {
            var pane = panes[index];
            var plot = new AnalysisPlotControl
            {
                Series = pane.Series,
                PlotOptions = pane.PlotOptions,
                MinWidth = 64,
                MinHeight = 64,
                Margin = new Thickness(2)
            };
            Grid.SetColumn(plot, index % columns);
            Grid.SetRow(plot, index / columns);
            paneGrid.Children.Add(plot);
            plots.Add(plot);
        }

        return BuildPanePlotContent(paneGrid, panes, plots);
    }

    private static bool IsRayFanView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "光线像差图", StringComparison.Ordinal)
            || string.Equals(view.Name, "Ray Fan", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpticalPathDifferenceView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "光程差图", StringComparison.Ordinal)
            || string.Equals(view.Name, "Optical Path Difference", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsThroughFocusSpotView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "离焦点列图", StringComparison.Ordinal)
            || string.Equals(view.Name, "Through Focus", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMatrixSpotView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "矩阵点列图", StringComparison.Ordinal)
            || string.Equals(view.Name, "Matrix Spot Diagram", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConfigurationMatrixSpotView(AnalysisViewDto view)
    {
        return IsConfigurationMatrixSpotViewName(view.Name);
    }

    internal static bool IsConfigurationMatrixSpotViewName(string? name)
    {
        return string.Equals(name, "结构矩阵点列图", StringComparison.Ordinal)
            || string.Equals(
                name,
                "Configuration Matrix Spot Diagram",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStandardSpotView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "标准点列图", StringComparison.Ordinal)
            || string.Equals(view.Name, "Spot Diagram", StringComparison.OrdinalIgnoreCase);
    }

    private static Control BuildStandardSpotPanePlot(IReadOnlyList<AnalysisPlotPaneDto> panes)
    {
        if (panes.Count == 0)
        {
            return new Grid();
        }

        const double cardWidth = 280;
        const double plotWidth = 260;
        const double plotHeight = 256;
        const double cardHeight = 290;
        if (panes.Count > 9)
        {
            return BuildPanePlot(panes, 3);
        }

        var fieldGrid = new Grid
        {
            Width = cardWidth * 3,
            Height = cardHeight * 3,
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            RowDefinitions = new RowDefinitions("*,*,*")
        };
        var plots = new List<AnalysisPlotControl>(panes.Count);

        for (var paneIndex = 0; paneIndex < panes.Count; paneIndex++)
        {
            var pane = panes[paneIndex];
            var plot = new AnalysisPlotControl
            {
                Series = pane.Series,
                PlotOptions = pane.PlotOptions with
                {
                    Title = string.Empty,
                    ShowLegend = false,
                    HideTickLabels = true
                },
                Width = plotWidth,
                Height = plotHeight,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var card = new Grid
            {
                Width = cardWidth,
                Height = cardHeight,
                RowDefinitions = new RowDefinitions("34,256"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            card.Children.Add(new TextBlock
            {
                Text = pane.Title,
                FontSize = 11.5,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = cardWidth - 8
            });
            Grid.SetRow(plot, 1);
            card.Children.Add(plot);
            var position = StandardSpotGridPosition(panes.Count, paneIndex);
            Grid.SetColumn(card, position.Column);
            Grid.SetRow(card, position.Row);
            fieldGrid.Children.Add(card);
            plots.Add(plot);
        }

        return BuildPanePlotContent(new Viewbox
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = fieldGrid
        }, panes, plots);
    }

    internal static (int Column, int Row) StandardSpotGridPosition(int paneCount, int paneIndex)
    {
        if (paneCount < 1 || paneCount > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(paneCount));
        }

        if (paneIndex < 0 || paneIndex >= paneCount)
        {
            throw new ArgumentOutOfRangeException(nameof(paneIndex));
        }

        var positions = paneCount switch
        {
            1 => new[] { (1, 1) },
            2 => new[] { (0, 1), (2, 1) },
            3 => new[] { (0, 1), (1, 1), (2, 1) },
            4 => new[] { (0, 0), (2, 0), (0, 2), (2, 2) },
            5 => new[] { (0, 0), (2, 0), (1, 1), (0, 2), (2, 2) },
            6 => new[] { (0, 0), (2, 0), (0, 1), (2, 1), (0, 2), (2, 2) },
            7 => new[] { (0, 0), (2, 0), (0, 1), (1, 1), (2, 1), (0, 2), (2, 2) },
            8 => new[] { (0, 0), (1, 0), (2, 0), (0, 1), (2, 1), (0, 2), (1, 2), (2, 2) },
            _ => new[]
            {
                (0, 0), (1, 0), (2, 0),
                (0, 1), (1, 1), (2, 1),
                (0, 2), (1, 2), (2, 2)
            }
        };
        return positions[paneIndex];
    }

    private static Control BuildMatrixSpotPanePlot(
        IReadOnlyList<AnalysisPlotPaneDto> panes,
        int requestedColumns)
    {
        return BuildMatrixSpotPanePlotCore(
            panes,
            requestedColumns,
            configurationMatrix: false);
    }

    private static Control BuildConfigurationMatrixSpotPanePlot(
        IReadOnlyList<AnalysisPlotPaneDto> panes,
        int requestedColumns)
    {
        return BuildMatrixSpotPanePlotCore(
            panes,
            requestedColumns,
            configurationMatrix: true);
    }

    private static Control BuildMatrixSpotPanePlotCore(
        IReadOnlyList<AnalysisPlotPaneDto> panes,
        int requestedColumns,
        bool configurationMatrix)
    {
        var columns = Math.Clamp(requestedColumns, 1, Math.Max(1, panes.Count));
        var rows = (int)Math.Ceiling(panes.Count / (double)columns);
        var labelWidth = configurationMatrix ? 220d : 150d;
        var plotColumnWidth = configurationMatrix ? 170d : 114d;
        var plotRowHeight = configurationMatrix ? 128d : 104d;
        var plotWidth = configurationMatrix ? 164d : 110d;
        var plotHeight = configurationMatrix ? 122d : 100d;
        var matrix = new Grid
        {
            Width = labelWidth + (columns * plotColumnWidth),
            Height = 36 + (rows * plotRowHeight),
            ColumnDefinitions = new ColumnDefinitions(
                $"{labelWidth.ToString(CultureInfo.InvariantCulture)}," +
                string.Join(
                    ',',
                    Enumerable.Repeat(
                        plotColumnWidth.ToString(CultureInfo.InvariantCulture),
                        columns))),
            RowDefinitions = new RowDefinitions(
                "36," + string.Join(
                    ',',
                    Enumerable.Repeat(
                        plotRowHeight.ToString(CultureInfo.InvariantCulture),
                        rows)))
        };

        var corner = new TextBlock
        {
            Text = "波长 →\n视场    ↓",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 12, 0)
        };
        matrix.Children.Add(corner);

        for (var column = 0; column < columns && column < panes.Count; column++)
        {
            var header = new TextBlock
            {
                Text = MatrixWavelengthLabel(panes[column].Title),
                FontSize = 12,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 3)
            };
            Grid.SetColumn(header, column + 1);
            matrix.Children.Add(header);
        }

        for (var row = 0; row < rows; row++)
        {
            var paneIndex = row * columns;
            if (paneIndex >= panes.Count)
            {
                break;
            }

            var fieldLabel = new TextBlock
            {
                Text = configurationMatrix
                    ? ConfigurationMatrixRowLabel(panes[paneIndex].Title)
                    : panes[paneIndex].Footer,
                FontSize = 12,
                FontFamily = configurationMatrix
                    ? FontFamily.Default
                    : new FontFamily("Cascadia Mono, Consolas"),
                FontWeight = configurationMatrix
                    ? FontWeight.SemiBold
                    : FontWeight.Normal,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 14, 0),
                MaxWidth = labelWidth - 24
            };
            Grid.SetRow(fieldLabel, row + 1);
            matrix.Children.Add(fieldLabel);
        }

        var columnPlots = Enumerable.Range(0, columns)
            .Select(_ => new List<(AnalysisPlotControl Plot, IReadOnlyList<AnalysisSeriesDto> Series)>())
            .ToArray();
        for (var index = 0; index < panes.Count; index++)
        {
            var pane = panes[index];
            var compactSeries = pane.Series.Select(series => series with
            {
                XAxisLabel = string.Empty,
                YAxisLabel = string.Empty,
                Name = string.Empty
            }).ToArray();
            var plot = new AnalysisPlotControl
            {
                Series = compactSeries,
                PlotOptions = pane.PlotOptions with
                {
                    Title = string.Empty,
                    ShowLegend = false,
                    HideTickLabels = true
                },
                Width = plotWidth,
                Height = plotHeight,
                Margin = new Thickness(3)
            };
            Grid.SetColumn(plot, (index % columns) + 1);
            Grid.SetRow(plot, (index / columns) + 1);
            matrix.Children.Add(plot);
            columnPlots[index % columns].Add((plot, compactSeries));
        }

        var firstPlotOptions = panes.FirstOrDefault()?.PlotOptions;
        if (firstPlotOptions?.XMinimum is { } xMinimum
            && firstPlotOptions.XMaximum is { } xMaximum)
        {
            var sharedScale = new TextBlock
            {
                Text = (Math.Abs(xMaximum - xMinimum) * 1000)
                    .ToString("0.000", CultureInfo.InvariantCulture),
                FontSize = 10.5,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(-22, 0, 0, 0),
                RenderTransform = new RotateTransform(-90),
                IsHitTestVisible = false
            };
            Grid.SetColumn(sharedScale, 1);
            Grid.SetRow(sharedScale, 1);
            matrix.Children.Add(sharedScale);
        }

        var legendEntries = new List<(string Key, string Label, int ColorIndex)>();
        for (var column = 0; column < columns && column < panes.Count; column++)
        {
            var series = panes[column].Series.FirstOrDefault();
            if (series is null)
            {
                continue;
            }

            if (TryGetWavelengthLegend(panes[column].Title, out var key, out var label))
            {
                legendEntries.Add((key, label, series.ColorIndex));
            }
        }
        var legend = BuildSelectableWavelengthLegend(legendEntries, (key, isVisible) =>
        {
            var column = legendEntries.FindIndex(entry => entry.Key == key);
            if (column < 0 || column >= columnPlots.Length)
            {
                return;
            }

            foreach (var (plot, originalSeries) in columnPlots[column])
            {
                plot.Series = isVisible ? originalSeries : Array.Empty<AnalysisSeriesDto>();
            }
        });
        legend.Margin = new Thickness(14, 42, 14, 0);
        legend.VerticalAlignment = VerticalAlignment.Top;

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Child = matrix
                },
                legend
            }
        };
        Grid.SetColumn(legend, 1);
        return layout;
    }

    private static string MatrixWavelengthLabel(string title)
    {
        if (TryGetWavelengthLegend(title, out var key, out _)
            && double.TryParse(
                key,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsedWavelength))
        {
            return parsedWavelength.ToString("0.000000", CultureInfo.InvariantCulture);
        }

        var valueText = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? title;
        return double.TryParse(
            valueText,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var wavelength)
                ? wavelength.ToString("0.000000", CultureInfo.InvariantCulture)
                : valueText;
    }

    private static string ConfigurationMatrixRowLabel(string title)
    {
        var wavelengthSeparator = title.LastIndexOf(" · ", StringComparison.Ordinal);
        var rowLabel = wavelengthSeparator > 0
            ? title[..wavelengthSeparator].Trim()
            : title.Trim();
        var structureSeparator = rowLabel.IndexOf(" · ", StringComparison.Ordinal);
        return structureSeparator > 0
            ? rowLabel[..structureSeparator] + Environment.NewLine
                + rowLabel[(structureSeparator + 3)..]
            : rowLabel;
    }

    private static Control BuildThroughFocusSpotPanePlot(
        IReadOnlyList<AnalysisPlotPaneDto> panes,
        int requestedColumns)
    {
        var columns = Math.Clamp(requestedColumns, 1, Math.Max(1, panes.Count));
        var rows = (int)Math.Ceiling(panes.Count / (double)columns);
        var matrix = new Grid
        {
            Width = 142 + (columns * 114),
            Height = (rows * 104) + 58,
            ColumnDefinitions = new ColumnDefinitions(
                "142," + string.Join(',', Enumerable.Repeat("114", columns))),
            RowDefinitions = new RowDefinitions(
                string.Join(',', Enumerable.Repeat("104", rows)) + ",28,30")
        };

        for (var row = 0; row < rows; row++)
        {
            var firstPaneIndex = row * columns;
            if (firstPaneIndex >= panes.Count)
            {
                break;
            }

            var pane = panes[firstPaneIndex];
            var fieldLabel = !string.IsNullOrWhiteSpace(pane.Footer)
                ? pane.Footer
                : pane.Title.Split('\n').LastOrDefault() ?? $"视场 {row + 1}";
            var label = new TextBlock
            {
                Text = fieldLabel,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(4, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(label, row);
            matrix.Children.Add(label);
        }

        var plots = new List<AnalysisPlotControl>(panes.Count);
        for (var index = 0; index < panes.Count; index++)
        {
            var pane = panes[index];
            var plot = new AnalysisPlotControl
            {
                Series = pane.Series.Select(series => series with
                {
                    XAxisLabel = string.Empty,
                    YAxisLabel = string.Empty
                }).ToArray(),
                PlotOptions = pane.PlotOptions with
                {
                    Title = string.Empty,
                    HideTickLabels = true
                },
                Width = 110,
                Height = 100,
                Margin = new Thickness(2)
            };
            Grid.SetColumn(plot, (index % columns) + 1);
            Grid.SetRow(plot, index / columns);
            matrix.Children.Add(plot);
            plots.Add(plot);
        }

        for (var column = 0; column < columns; column++)
        {
            var header = new TextBlock
            {
                Text = DefocusMicrometersLabel(panes[column].Title),
                FontSize = 12,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(header, column + 1);
            Grid.SetRow(header, rows);
            matrix.Children.Add(header);
        }

        var axisCaption = new TextBlock
        {
            Text = "←  离焦：µm  →",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(axisCaption, 1);
        Grid.SetColumnSpan(axisCaption, columns);
        Grid.SetRow(axisCaption, rows + 1);
        matrix.Children.Add(axisCaption);

        return BuildPanePlotContent(new Viewbox
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = matrix
        }, panes, plots);
    }

    private static string DefocusMicrometersLabel(string title)
    {
        var firstLine = title.Split('\n').FirstOrDefault() ?? title;
        var valueText = firstLine
            .Replace("Defocus:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("mm", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        return double.TryParse(
            valueText,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var millimeters)
                ? (millimeters * 1000).ToString("+0;-0;0", CultureInfo.InvariantCulture)
                : valueText;
    }

    private static Control BuildRayFanPanePlot(IReadOnlyList<AnalysisPlotPaneDto> panes)
    {
        const int fieldCount = 5;
        const int panesPerField = 2;
        if (panes.Count != fieldCount * panesPerField)
        {
            return BuildPanePlot(panes, panesPerField);
        }

        var fieldGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            RowDefinitions = new RowDefinitions("*,*,*")
        };
        var positions = new[]
        {
            (Column: 0, Row: 0),
            (Column: 2, Row: 0),
            (Column: 1, Row: 1),
            (Column: 0, Row: 2),
            (Column: 2, Row: 2)
        };
        var plots = new List<AnalysisPlotControl>(fieldCount * panesPerField);
        for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            var firstPaneIndex = fieldIndex * panesPerField;
            var pair = new Grid
            {
                Width = 520,
                Height = 280,
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                RowDefinitions = new RowDefinitions("28,*")
            };
            var title = new TextBlock
            {
                Text = panes[firstPaneIndex].Title,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumnSpan(title, panesPerField);
            pair.Children.Add(title);

            for (var paneOffset = 0; paneOffset < panesPerField; paneOffset++)
            {
                var pane = panes[firstPaneIndex + paneOffset];
                var plot = new AnalysisPlotControl
                {
                    Series = pane.Series,
                    PlotOptions = pane.PlotOptions with { Title = string.Empty },
                    Margin = new Thickness(2)
                };
                Grid.SetColumn(plot, paneOffset);
                Grid.SetRow(plot, 1);
                pair.Children.Add(plot);
                plots.Add(plot);
            }

            var scaledPair = new Viewbox
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = pair
            };
            Grid.SetColumn(scaledPair, positions[fieldIndex].Column);
            Grid.SetRow(scaledPair, positions[fieldIndex].Row);
            fieldGrid.Children.Add(scaledPair);
        }

        return BuildPanePlotContent(fieldGrid, panes, plots);
    }

    private static Control BuildPanePlotContent(
        Control plotRoot,
        IReadOnlyList<AnalysisPlotPaneDto> panes,
        IReadOnlyList<AnalysisPlotControl> plots)
    {
        var sourceSeries = panes.FirstOrDefault()?.Series ?? Array.Empty<AnalysisSeriesDto>();
        var wavelengthEntries = WavelengthLegendEntries(sourceSeries);
        Control legend;
        if (wavelengthEntries.Count > 0)
        {
            var originals = plots.Select(plot => plot.Series.ToArray()).ToArray();
            var enabled = wavelengthEntries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
            legend = BuildSelectableWavelengthLegend(wavelengthEntries, (key, isVisible) =>
            {
                if (isVisible)
                {
                    enabled.Add(key);
                }
                else
                {
                    enabled.Remove(key);
                }

                for (var index = 0; index < plots.Count; index++)
                {
                    plots[index].Series = originals[index].Where(series =>
                        !TryGetWavelengthLegend(series.Name, out var seriesKey, out _)
                        || enabled.Contains(seriesKey)).ToArray();
                }
            }, Orientation.Horizontal);
            foreach (var plot in plots)
            {
                plot.PlotOptions = plot.PlotOptions with { ShowLegend = false };
            }
        }
        else
        {
            var plainLegend = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(12, 4, 12, 12)
            };
            foreach (var series in sourceSeries.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
            {
                plainLegend.Children.Add(new TextBlock
                {
                    Text = $"●  {series.Name}",
                    Foreground = SeriesBrush(series.ColorIndex),
                    Margin = new Thickness(10, 2)
                });
            }

            legend = plainLegend;
        }

        var content = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        content.Children.Add(plotRoot);
        content.Children.Add(legend);
        Grid.SetRow(legend, 1);
        return content;
    }

    private static TextBlock EmptyPlotMessage(bool isVisible)
    {
        return new TextBlock
        {
            Text = "当前分析没有可绘制的数值序列",
            IsVisible = isVisible,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.Gray
        };
    }

    private static List<(string Key, string Label, int ColorIndex)> WavelengthLegendEntries(
        IReadOnlyList<AnalysisSeriesDto> series)
    {
        var entries = new List<(string Key, string Label, int ColorIndex)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in series)
        {
            if (!TryGetWavelengthLegend(item.Name, out var key, out var label)
                || !seen.Add(key))
            {
                continue;
            }

            entries.Add((key, label, item.ColorIndex));
        }

        return entries;
    }

    private static StackPanel BuildSelectableWavelengthLegend(
        IReadOnlyList<(string Key, string Label, int ColorIndex)> entries,
        Action<string, bool> setVisibility,
        Orientation orientation = Orientation.Vertical)
    {
        var legend = new StackPanel
        {
            Orientation = orientation,
            Spacing = orientation == Orientation.Horizontal ? 14 : 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(12, 4, 12, 12)
        };
        foreach (var entry in entries)
        {
            var brush = SeriesBrush(entry.ColorIndex);
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    new Border
                    {
                        Width = 8,
                        Height = 8,
                        CornerRadius = new CornerRadius(4),
                        Background = brush,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = entry.Label,
                        Foreground = brush,
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        FontSize = 11.5,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
            var checkBox = new CheckBox
            {
                IsChecked = true,
                Content = content,
                Margin = new Thickness(2, 1)
            };
            checkBox.IsCheckedChanged += (_, _) =>
                setVisibility(entry.Key, checkBox.IsChecked == true);
            legend.Children.Add(checkBox);
        }

        return legend;
    }

    private static bool TryGetWavelengthLegend(
        string text,
        out string key,
        out string label)
    {
        key = string.Empty;
        label = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var unitIndex = text.IndexOf("µm", StringComparison.OrdinalIgnoreCase);
        if (unitIndex < 0)
        {
            unitIndex = text.IndexOf("μm", StringComparison.OrdinalIgnoreCase);
        }

        if (unitIndex < 0)
        {
            return false;
        }

        var prefix = text[..unitIndex].TrimEnd();
        var start = prefix.Length;
        while (start > 0)
        {
            var character = prefix[start - 1];
            if (!char.IsDigit(character)
                && character is not '.' and not ',' and not '+' and not '-' and not 'e' and not 'E')
            {
                break;
            }

            start--;
        }

        var numberText = prefix[start..].Replace(',', '.');
        if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var wavelength))
        {
            return false;
        }

        key = wavelength.ToString("R", CultureInfo.InvariantCulture);
        label = $"{wavelength.ToString("0.0000", CultureInfo.InvariantCulture)} µm";
        return true;
    }

    private static Control? BuildPaneMetricsSummary(IReadOnlyList<AnalysisPlotPaneDto> panes)
    {
        var populated = panes
            .Where(pane => pane.Metrics is { Count: > 0 })
            .ToArray();
        if (populated.Length == 0)
        {
            return null;
        }

        var labels = populated
            .SelectMany(pane => pane.Metrics!)
            .Select(metric => metric.Label)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var grid = new Grid
        {
            Margin = new Thickness(28, 0, 0, 0),
            ColumnDefinitions = new ColumnDefinitions(
                "Auto," + string.Join(',', Enumerable.Repeat("Auto", populated.Length))),
            RowDefinitions = new RowDefinitions(
                string.Join(',', Enumerable.Repeat("Auto", labels.Length + 1)))
        };

        for (var paneIndex = 0; paneIndex < populated.Length; paneIndex++)
        {
            var header = new TextBlock
            {
                Text = $"视场 {paneIndex + 1}",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(12, 0, 4, 2),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(header, paneIndex + 1);
            grid.Children.Add(header);
        }

        for (var rowIndex = 0; rowIndex < labels.Length; rowIndex++)
        {
            var label = new TextBlock
            {
                Text = labels[rowIndex] + "：",
                FontSize = 11,
                Margin = new Thickness(0, 1, 6, 1)
            };
            Grid.SetRow(label, rowIndex + 1);
            grid.Children.Add(label);
            for (var paneIndex = 0; paneIndex < populated.Length; paneIndex++)
            {
                var metric = populated[paneIndex].Metrics!
                    .FirstOrDefault(item => item.Label == labels[rowIndex]);
                if (metric is null)
                {
                    continue;
                }

                var value = new TextBlock
                {
                    Text = $"{NumericDisplayFormatter.Format(metric.Value)} {metric.Unit}".TrimEnd(),
                    FontSize = 11,
                    Margin = new Thickness(12, 1, 4, 1),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(value, paneIndex + 1);
                Grid.SetRow(value, rowIndex + 1);
                grid.Children.Add(value);
            }
        }

        var reference = populated
            .Select(pane => pane.Footer)
            .FirstOrDefault(footer => !string.IsNullOrWhiteSpace(footer));
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                grid,
                new TextBlock
                {
                    Text = reference ?? string.Empty,
                    IsVisible = !string.IsNullOrWhiteSpace(reference),
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(82, 82, 87))
                }
            }
        };
    }

    internal static IBrush SeriesBrush(int colorIndex)
    {
        return new SolidColorBrush(AnalysisPlotControl.SeriesColor(colorIndex));
    }
}
