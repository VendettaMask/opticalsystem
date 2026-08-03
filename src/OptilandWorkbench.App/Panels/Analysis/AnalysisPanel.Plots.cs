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
        Control plotRoot = plot;
        if (view.PlotOptions.DefaultSquareViewport)
        {
            plotRoot = new OptionalSquarePlotHost
            {
                Child = plot,
                IsSquare = true
            };
        }
        var legendEntries = SelectableLegendEntries(view.Series);
        if (legendEntries.Count == 0)
        {
            var content = new Grid
            {
                Children =
                {
                    plotRoot,
                    EmptyPlotMessage(view.Series.Count == 0)
                }
            };
            return content;
        }

        var original = view.Series.ToArray();
        var enabled = legendEntries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        var legendBelow = view.PlotOptions.LegendBelow;
        var legend = BuildSelectableLegend(legendEntries, (key, isVisible) =>
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
                !TryGetSelectableLegend(series, out var seriesKey, out _)
                || enabled.Contains(seriesKey)).ToArray();
        }, legendBelow ? Orientation.Horizontal : Orientation.Vertical);
        plot.PlotOptions = view.PlotOptions with { ShowLegend = false };
        var layout = new Grid
        {
            ColumnDefinitions = legendBelow ? new ColumnDefinitions("*") : new ColumnDefinitions("*,Auto"),
            RowDefinitions = legendBelow ? new RowDefinitions("*,Auto") : new RowDefinitions("*")
        };
        layout.Children.Add(plotRoot);
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

    private static Control BuildPanePlot(
        IReadOnlyList<AnalysisPlotPaneDto> panes,
        int requestedColumns,
        bool defaultSquareCells = false)
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
            var squareHost = new OptionalSquarePlotHost
            {
                Child = plot,
                IsSquare = defaultSquareCells,
                MinWidth = 64,
                MinHeight = 64
            };
            Grid.SetColumn(squareHost, index % columns);
            Grid.SetRow(squareHost, index / columns);
            paneGrid.Children.Add(squareHost);
            plots.Add(plot);
        }

        return BuildPanePlotContent(paneGrid, panes, plots);
    }

    private static bool IsRayFanView(AnalysisViewDto view)
    {
        return view.PresentationKind == AnalysisPresentationKind.RayFan;
    }

    private static bool IsPupilAberrationView(AnalysisViewDto view)
    {
        return view.PresentationKind == AnalysisPresentationKind.PupilAberration;
    }

    private static bool IsOpticalPathDifferenceView(AnalysisViewDto view)
    {
        return view.PresentationKind == AnalysisPresentationKind.OpticalPathDifference;
    }

    private static bool IsThroughFocusSpotView(AnalysisViewDto view)
    {
        return view.PresentationKind == AnalysisPresentationKind.ThroughFocusSpot;
    }

    private static bool IsMatrixSpotView(AnalysisViewDto view)
    {
        return view.PresentationKind == AnalysisPresentationKind.MatrixSpot;
    }

    private static bool IsConfigurationMatrixSpotView(AnalysisViewDto view)
    {
        return view.PresentationKind == AnalysisPresentationKind.ConfigurationMatrixSpot;
    }

    private static bool IsStandardSpotView(AnalysisViewDto view)
    {
        return view.PresentationKind == AnalysisPresentationKind.SpotDiagram;
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

        var gridSize = StandardSpotGridSize(panes.Count);
        var fieldGrid = new Grid
        {
            Width = cardWidth * gridSize.Columns,
            Height = cardHeight * gridSize.Rows,
            ColumnDefinitions = new ColumnDefinitions(
                string.Join(',', Enumerable.Repeat("*", gridSize.Columns))),
            RowDefinitions = new RowDefinitions(
                string.Join(',', Enumerable.Repeat("*", gridSize.Rows)))
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
            1 => new[] { (0, 0) },
            2 => new[] { (0, 0), (1, 0) },
            3 => new[] { (0, 0), (1, 0), (2, 0) },
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

    internal static (int Columns, int Rows) StandardSpotGridSize(int paneCount)
    {
        if (paneCount < 1 || paneCount > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(paneCount));
        }

        return paneCount <= 3
            ? (paneCount, 1)
            : (3, 3);
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
        var labelWidth = configurationMatrix ? 180d : 132d;
        var plotColumnWidth = configurationMatrix ? 170d : 146d;
        var plotRowHeight = configurationMatrix ? 170d : 142d;
        var plotWidth = configurationMatrix ? 164d : 140d;
        var plotHeight = configurationMatrix ? 164d : 140d;
        var headerHeight = configurationMatrix ? 36d : 28d;
        var matrix = new Grid
        {
            Width = labelWidth + (columns * plotColumnWidth),
            Height = headerHeight + (rows * plotRowHeight),
            ColumnDefinitions = new ColumnDefinitions(
                $"{labelWidth.ToString(CultureInfo.InvariantCulture)}," +
                string.Join(
                    ',',
                    Enumerable.Repeat(
                        plotColumnWidth.ToString(CultureInfo.InvariantCulture),
                        columns))),
            RowDefinitions = new RowDefinitions(
                headerHeight.ToString(CultureInfo.InvariantCulture) + "," + string.Join(
                    ',',
                    Enumerable.Repeat(
                        plotRowHeight.ToString(CultureInfo.InvariantCulture),
                        rows)))
        };

        var corner = new TextBlock
        {
            Text = configurationMatrix
                ? "结构 →\n视场    ↓"
                : "波长 →\n视场    ↓",
            FontSize = 10.5,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 12, 0)
        };
        matrix.Children.Add(corner);

        for (var column = 0; column < columns && column < panes.Count; column++)
        {
            var header = new TextBlock
            {
                Text = configurationMatrix
                    ? ConfigurationMatrixConfigurationLabel(panes[column].Title)
                    : MatrixWavelengthLabel(panes[column].Title),
                FontSize = 10.5,
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
                    ? ConfigurationMatrixFieldLabel(panes[paneIndex].Title)
                    : panes[paneIndex].Footer,
                FontSize = 10.5,
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
                Name = configurationMatrix ? series.Name : string.Empty,
                XQuantity = AnalysisAxisQuantity.Unspecified,
                XUnit = AnalysisAxisUnit.Unspecified,
                YQuantity = AnalysisAxisQuantity.Unspecified,
                YUnit = AnalysisAxisUnit.Unspecified
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
                FontSize = 9.5,
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

        var legendEntries = new List<(string Key, string Label, Color Color)>();
        if (configurationMatrix)
        {
            foreach (var series in panes.FirstOrDefault()?.Series ?? Array.Empty<AnalysisSeriesDto>())
            {
                if (TryGetSelectableLegend(series, out var key, out var label)
                    && legendEntries.All(entry => entry.Key != key))
                {
                    legendEntries.Add((key, label, AnalysisPlotControl.SeriesColor(series)));
                }
            }
        }
        else
        {
            for (var column = 0; column < columns && column < panes.Count; column++)
            {
                var series = panes[column].Series.FirstOrDefault();
                if (series is null)
                {
                    continue;
                }

                if (TryGetWavelengthLegend(panes[column].Title, out var key, out var label))
                {
                    var color = double.TryParse(
                        key,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var wavelengthMicrometers)
                            ? AnalysisPlotControl.WavelengthColor(wavelengthMicrometers * 1000)
                            : AnalysisPlotControl.SeriesColor(series);
                    legendEntries.Add((key, label, color));
                }
            }
        }

        var enabledLegendKeys = legendEntries
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        var legend = BuildSelectableLegend(legendEntries, (key, isVisible) =>
        {
            if (configurationMatrix)
            {
                if (isVisible)
                {
                    enabledLegendKeys.Add(key);
                }
                else
                {
                    enabledLegendKeys.Remove(key);
                }

                foreach (var (plot, originalSeries) in columnPlots.SelectMany(items => items))
                {
                    plot.Series = originalSeries.Where(series =>
                        !TryGetSelectableLegend(series, out var seriesKey, out _)
                        || enabledLegendKeys.Contains(seriesKey)).ToArray();
                }

                return;
            }

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

    private static string ConfigurationMatrixConfigurationLabel(string title)
    {
        var separator = title.IndexOf(" · ", StringComparison.Ordinal);
        return separator > 0 ? title[..separator].Trim() : title.Trim();
    }

    private static string ConfigurationMatrixFieldLabel(string title)
    {
        var separator = title.IndexOf(" · ", StringComparison.Ordinal);
        return separator > 0 ? title[(separator + 3)..].Trim() : title.Trim();
    }

    private static Control BuildThroughFocusSpotPanePlot(
        IReadOnlyList<AnalysisPlotPaneDto> panes,
        int requestedColumns)
    {
        const int fieldLabelWidth = 120;
        const int plotCellSize = 124;
        const int plotSize = 120;
        const int columnLabelHeight = 24;
        const int axisCaptionHeight = 24;
        var columns = Math.Clamp(requestedColumns, 1, Math.Max(1, panes.Count));
        var rows = (int)Math.Ceiling(panes.Count / (double)columns);
        var matrix = new Grid
        {
            Width = fieldLabelWidth + (columns * plotCellSize),
            Height = (rows * plotCellSize) + columnLabelHeight + axisCaptionHeight,
            ColumnDefinitions = new ColumnDefinitions(
                $"{fieldLabelWidth}," + string.Join(',', Enumerable.Repeat(plotCellSize.ToString(CultureInfo.InvariantCulture), columns))),
            RowDefinitions = new RowDefinitions(
                string.Join(',', Enumerable.Repeat(plotCellSize.ToString(CultureInfo.InvariantCulture), rows))
                    + $",{columnLabelHeight},{axisCaptionHeight}")
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
                FontSize = 10,
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
                    YAxisLabel = string.Empty,
                    XQuantity = AnalysisAxisQuantity.Unspecified,
                    XUnit = AnalysisAxisUnit.Unspecified,
                    YQuantity = AnalysisAxisQuantity.Unspecified,
                    YUnit = AnalysisAxisUnit.Unspecified
                }).ToArray(),
                PlotOptions = pane.PlotOptions with
                {
                    Title = string.Empty,
                    HideTickLabels = true
                },
                Width = plotSize,
                Height = plotSize,
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
                FontSize = 10,
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
            FontSize = 10,
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

    private static Control BuildPairedFanPanePlot(
        IReadOnlyList<AnalysisPlotPaneDto> panes,
        bool defaultSquareCells = false)
    {
        const int panesPerField = 2;
        var fieldPanes = PairFanPanesByField(panes);
        if (fieldPanes is null)
        {
            return BuildPanePlot(panes, panesPerField, defaultSquareCells);
        }

        var fieldCount = fieldPanes.Count;
        var fieldColumns = Math.Min(3, (int)Math.Ceiling(Math.Sqrt(fieldCount)));
        var fieldRows = (int)Math.Ceiling(fieldCount / (double)fieldColumns);
        var fieldGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                string.Join(',', Enumerable.Repeat("*", fieldColumns * 2))),
            RowDefinitions = new RowDefinitions(
                string.Join(',', Enumerable.Repeat("*", fieldRows)))
        };
        var plots = new List<AnalysisPlotControl>(fieldCount * panesPerField);
        for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            var field = fieldPanes[fieldIndex];
            var pair = new Grid
            {
                Width = 520,
                Height = 288,
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                RowDefinitions = new RowDefinitions("28,*")
            };
            var title = new TextBlock
            {
                Text = field.Title,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumnSpan(title, panesPerField);
            pair.Children.Add(title);

            for (var paneOffset = 0; paneOffset < panesPerField; paneOffset++)
            {
                var pane = paneOffset == 0 ? field.Y : field.X;
                var plot = new AnalysisPlotControl
                {
                    Series = pane.Series,
                    PlotOptions = pane.PlotOptions with { Title = string.Empty },
                    Margin = new Thickness(2)
                };
                var squareHost = new OptionalSquarePlotHost
                {
                    Child = plot,
                    IsSquare = defaultSquareCells
                };
                Grid.SetColumn(squareHost, paneOffset);
                Grid.SetRow(squareHost, 1);
                pair.Children.Add(squareHost);
                plots.Add(plot);
            }

            var scaledPair = new Viewbox
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(8),
                Child = pair
            };
            var row = fieldIndex / fieldColumns;
            var indexInRow = fieldIndex % fieldColumns;
            var fieldsInRow = Math.Min(fieldColumns, fieldCount - (row * fieldColumns));
            var centeringOffset = fieldColumns - fieldsInRow;
            Grid.SetColumn(scaledPair, centeringOffset + (indexInRow * 2));
            Grid.SetColumnSpan(scaledPair, 2);
            Grid.SetRow(scaledPair, row);
            fieldGrid.Children.Add(scaledPair);
        }

        return BuildPanePlotContent(fieldGrid, panes, plots);
    }

    private static IReadOnlyList<(string Title, AnalysisPlotPaneDto Y, AnalysisPlotPaneDto X)>?
        PairFanPanesByField(IReadOnlyList<AnalysisPlotPaneDto> panes)
    {
        if (panes.Count == 0 || panes.Count % 2 != 0)
        {
            return null;
        }

        var used = new HashSet<int>();
        var pairs = new List<(string Title, AnalysisPlotPaneDto Y, AnalysisPlotPaneDto X)>(panes.Count / 2);
        for (var yIndex = 0; yIndex < panes.Count; yIndex++)
        {
            if (used.Contains(yIndex) || !IsRayFanAxis(panes[yIndex], "P_y"))
            {
                continue;
            }

            var xIndex = Enumerable.Range(0, panes.Count).FirstOrDefault(index =>
                !used.Contains(index)
                && index != yIndex
                && string.Equals(panes[index].Title, panes[yIndex].Title, StringComparison.Ordinal)
                && IsRayFanAxis(panes[index], "P_x"), -1);
            if (xIndex < 0)
            {
                return null;
            }

            used.Add(yIndex);
            used.Add(xIndex);
            pairs.Add((panes[yIndex].Title, panes[yIndex], panes[xIndex]));
        }

        return used.Count == panes.Count ? pairs : null;
    }

    private static bool IsRayFanAxis(AnalysisPlotPaneDto pane, string axisLabel)
    {
        return pane.Series.Count > 0
            && pane.Series.All(series =>
                string.Equals(series.XAxisLabel, axisLabel, StringComparison.OrdinalIgnoreCase));
    }

    private static Control BuildPanePlotContent(
        Control plotRoot,
        IReadOnlyList<AnalysisPlotPaneDto> panes,
        IReadOnlyList<AnalysisPlotControl> plots)
    {
        var sourceSeries = panes.FirstOrDefault()?.Series ?? Array.Empty<AnalysisSeriesDto>();
        var legendEntries = SelectableLegendEntries(sourceSeries);
        Control legend;
        if (legendEntries.Count > 0)
        {
            var originals = plots.Select(plot => plot.Series.ToArray()).ToArray();
            var enabled = legendEntries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
            legend = BuildSelectableLegend(legendEntries, (key, isVisible) =>
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
                        !TryGetSelectableLegend(series, out var seriesKey, out _)
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
                    Foreground = SeriesBrush(series),
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
        var message = new TextBlock
        {
            Text = "当前分析没有可绘制的数值序列",
            IsVisible = isVisible,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        message.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        return message;
    }

    private static List<(string Key, string Label, Color Color)> SelectableLegendEntries(
        IReadOnlyList<AnalysisSeriesDto> series)
    {
        var entries = new List<(string Key, string Label, Color Color)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in series)
        {
            if (!TryGetSelectableLegend(item, out var key, out var label)
                || !seen.Add(key))
            {
                continue;
            }

            entries.Add((key, label, AnalysisPlotControl.SeriesColor(item)));
        }

        return entries;
    }

    private static StackPanel BuildSelectableLegend(
        IReadOnlyList<(string Key, string Label, Color Color)> entries,
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
            var brush = new SolidColorBrush(entry.Color);
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

    private static bool TryGetSelectableLegend(
        AnalysisSeriesDto series,
        out string key,
        out string label)
    {
        if (!string.IsNullOrWhiteSpace(series.LegendKey)
            && !string.IsNullOrWhiteSpace(series.LegendLabel))
        {
            key = series.LegendKey;
            label = series.LegendLabel;
            return true;
        }

        return TryGetWavelengthLegend(series.Name, out key, out label);
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
        var referenceText = new TextBlock
        {
            Text = reference ?? string.Empty,
            IsVisible = !string.IsNullOrWhiteSpace(reference),
            FontSize = 10.5
        };
        referenceText.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                grid,
                referenceText
            }
        };
    }

    internal static IBrush SeriesBrush(int colorIndex)
    {
        return new SolidColorBrush(AnalysisPlotControl.SeriesColor(colorIndex));
    }

    internal static IBrush SeriesBrush(AnalysisSeriesDto series)
    {
        return new SolidColorBrush(AnalysisPlotControl.SeriesColor(series));
    }
}
