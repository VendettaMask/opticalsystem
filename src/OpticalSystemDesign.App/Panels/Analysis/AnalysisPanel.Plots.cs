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
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Formatting;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class AnalysisPanel
{
    private static Control BuildSinglePlot(AnalysisViewDto view)
    {
        var plot = new AnalysisPlotControl
        {
            Series = view.Series,
            PlotOptions = view.PresentationKind == AnalysisPresentationKind.LateralColor
                ? view.PlotOptions with { ShowLegend = false }
                : view.PlotOptions,
            MinHeight = DisplayTypography.Scale(AnalysisPlotMinimumHeight),
            MinWidth = DisplayTypography.Scale(240)
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
            if (plot.PlotOptions.ShowLegend && view.Series.Any(series => !string.IsNullOrWhiteSpace(series.Name)))
            {
                return BuildPlotLegendLayout(plotRoot, BuildNamedSeriesLegend([plot]));
            }
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

            plot.SetVisibleSeries(original.Where(series =>
                !TryGetSelectableLegend(series, out var seriesKey, out _)
                || enabled.Contains(seriesKey)).ToArray());
        });
        plot.PlotOptions = view.PlotOptions with { ShowLegend = false };
        var layout = BuildPlotLegendLayout(plotRoot, legend);
        layout.Children.Add(EmptyPlotMessage(view.Series.Count == 0));
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
        var paneGrid = new ResponsiveAnalysisPaneGrid(columns, DisplayTypography.Scale(200))
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
                MinWidth = DisplayTypography.Scale(176),
                MinHeight = DisplayTypography.Scale(176),
                Margin = new Thickness(2)
            };
            var squareHost = new OptionalSquarePlotHost
            {
                Child = plot,
                IsSquare = defaultSquareCells,
                MinWidth = DisplayTypography.Scale(176),
                MinHeight = DisplayTypography.Scale(176)
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

        if (panes.Count > 9)
        {
            return BuildPanePlot(panes, 3);
        }

        var gridSize = StandardSpotGridSize(panes.Count);
        var fieldGrid = new ResponsiveAnalysisPaneGrid(gridSize.Columns, DisplayTypography.Scale(200))
        {
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
                MinWidth = DisplayTypography.Scale(176),
                MinHeight = DisplayTypography.Scale(176)
            };
            var plotHost = new OptionalSquarePlotHost
            {
                Child = plot,
                IsSquare = true,
                MinWidth = DisplayTypography.Scale(176),
                MinHeight = DisplayTypography.Scale(176),
                Margin = new Thickness(8)
            };
            var title = new TextBlock
            {
                Text = pane.Title,
                FontSize = DisplayTypography.CompactBody,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 2, 4, 4)
            };
            var card = new TitledSquarePlotCard(title, plotHost) { Margin = new Thickness(4) };
            var position = StandardSpotGridPosition(panes.Count, paneIndex);
            Grid.SetColumn(card, position.Column);
            Grid.SetRow(card, position.Row);
            fieldGrid.Children.Add(card);
            plots.Add(plot);
        }

        return BuildPanePlotContent(fieldGrid, panes, plots);
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
        var matrix = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                "Auto," + string.Join(',', Enumerable.Repeat("*", columns))),
            RowDefinitions = new RowDefinitions(
                "Auto," + string.Join(',', Enumerable.Repeat("*", rows))),
            Margin = new Thickness(8)
        };

        matrix.MinWidth = DisplayTypography.Scale(120 + columns * 176);
        matrix.MinHeight = DisplayTypography.Scale(34 + rows * 176);

        var corner = new TextBlock
        {
            Text = configurationMatrix
                ? "结构 →\n视场    ↓"
                : "波长 →\n视场    ↓",
            FontSize = DisplayTypography.CompactBody,
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
                    ? panes[column].MatrixCell is { } cell
                        ? $"结构 {cell.ConfigurationNumber}"
                        : ConfigurationMatrixConfigurationLabel(panes[column].Title)
                    : MatrixWavelengthLabel(panes[column].Title),
                FontSize = DisplayTypography.CompactBody,
                Classes = { ProductFonts.NumericClass },
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
                    ? panes[paneIndex].MatrixCell?.FieldLabel ?? ConfigurationMatrixFieldLabel(panes[paneIndex].Title)
                    : panes[paneIndex].Footer,
                FontSize = DisplayTypography.CompactBody,
                Classes = { configurationMatrix ? "interface-text" : ProductFonts.NumericClass },
                FontWeight = configurationMatrix
                    ? FontWeight.SemiBold
                    : FontWeight.Normal,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 14, 0)
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
                Name = configurationMatrix ? series.Name : string.Empty
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
                MinWidth = DisplayTypography.Scale(176),
                MinHeight = DisplayTypography.Scale(176)
            };
            var plotHost = new OptionalSquarePlotHost
            {
                Child = plot,
                IsSquare = true,
                MinWidth = DisplayTypography.Scale(176),
                MinHeight = DisplayTypography.Scale(176),
                Margin = new Thickness(3)
            };
            if (configurationMatrix) ToolTip.SetTip(plotHost, pane.Title + Environment.NewLine + pane.Footer);
            Grid.SetColumn(plotHost, (index % columns) + 1);
            Grid.SetRow(plotHost, (index / columns) + 1);
            matrix.Children.Add(plotHost);
            columnPlots[index % columns].Add((plot, compactSeries));
        }

        var legendEntries = new List<(string Key, string Label, Color Color)>();
        if (configurationMatrix)
        {
            foreach (var series in panes.SelectMany(pane => pane.Series))
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
                    plot.SetVisibleSeries(originalSeries.Where(series =>
                        !TryGetSelectableLegend(series, out var seriesKey, out _)
                        || enabledLegendKeys.Contains(seriesKey)).ToArray());
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
                plot.SetVisibleSeries(isVisible ? originalSeries : Array.Empty<AnalysisSeriesDto>());
            }
        });
        return BuildPlotLegendLayout(matrix, legend);
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
        var columns = Math.Clamp(requestedColumns, 1, Math.Max(1, panes.Count));
        var rows = (int)Math.Ceiling(panes.Count / (double)columns);
        var matrix = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                "Auto," + string.Join(',', Enumerable.Repeat("*", columns))),
            RowDefinitions = new RowDefinitions(
                string.Join(',', Enumerable.Repeat("*", rows)) + ",Auto,Auto"),
            MinWidth = DisplayTypography.Scale(120 + columns * 184),
            MinHeight = DisplayTypography.Scale(40 + rows * 184),
            Margin = new Thickness(8)
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
                FontSize = DisplayTypography.CompactBody,
                MaxWidth = DisplayTypography.Scale(120),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(4, 0, 12, 0),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Right,
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
                MinWidth = DisplayTypography.Scale(176),
                MinHeight = DisplayTypography.Scale(176)
            };
            var plotHost = new OptionalSquarePlotHost
            {
                Child = plot,
                IsSquare = true,
                MinWidth = DisplayTypography.Scale(176),
                MinHeight = DisplayTypography.Scale(176),
                Margin = new Thickness(2)
            };
            Grid.SetColumn(plotHost, (index % columns) + 1);
            Grid.SetRow(plotHost, index / columns);
            matrix.Children.Add(plotHost);
            plots.Add(plot);
        }

        for (var column = 0; column < columns; column++)
        {
            var header = new TextBlock
            {
                Text = DefocusMicrometersLabel(panes[column].Title),
                FontSize = DisplayTypography.CompactBody,
                TextWrapping = TextWrapping.Wrap,
                Classes = { ProductFonts.NumericClass },
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
            FontSize = DisplayTypography.CompactBody,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(axisCaption, 1);
        Grid.SetColumnSpan(axisCaption, columns);
        Grid.SetRow(axisCaption, rows + 1);
        matrix.Children.Add(axisCaption);

        return BuildPanePlotContent(matrix, panes, plots);
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
        // Include room for axes and captions in each fan's logical minimum.
        // Keep readable axes; the page scrolls if a complete pair cannot fit.
        var minimumFanSize = DisplayTypography.Scale(200);
        var fieldPanes = PairFanPanesByField(panes);
        if (fieldPanes is null)
        {
            return BuildPanePlot(panes, panesPerField, defaultSquareCells);
        }

        var fieldCount = fieldPanes.Count;
        var fieldColumns = Math.Min(3, (int)Math.Ceiling(Math.Sqrt(fieldCount)));
        var fieldRows = (int)Math.Ceiling(fieldCount / (double)fieldColumns);
        var fieldGrid = new ResponsiveAnalysisPaneGrid(fieldColumns, 2 * minimumFanSize + 24, columnUnits: 2)
        {
            MinWidth = 2 * minimumFanSize + 24,
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
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                RowDefinitions = new RowDefinitions("Auto,*"),
                Margin = new Thickness(8)
            };
            var title = new TextBlock
            {
                Text = field.Title,
                FontSize = DisplayTypography.CompactBody,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
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
                    MinWidth = minimumFanSize,
                    MinHeight = minimumFanSize,
                    Margin = new Thickness(2)
                };
                var squareHost = new OptionalSquarePlotHost
                {
                    Child = plot,
                    IsSquare = defaultSquareCells,
                    SquareHorizontalAlignment = paneOffset == 0 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    MinWidth = minimumFanSize,
                    MinHeight = minimumFanSize
                };
                Grid.SetColumn(squareHost, paneOffset);
                Grid.SetRow(squareHost, 1);
                pair.Children.Add(squareHost);
                plots.Add(plot);
            }

            var row = fieldIndex / fieldColumns;
            var indexInRow = fieldIndex % fieldColumns;
            var fieldsInRow = Math.Min(fieldColumns, fieldCount - (row * fieldColumns));
            var centeringOffset = fieldColumns - fieldsInRow;
            Grid.SetColumn(pair, centeringOffset + (indexInRow * 2));
            Grid.SetColumnSpan(pair, 2);
            Grid.SetRow(pair, row);
            fieldGrid.Children.Add(pair);
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
        plotRoot.MinHeight = Math.Max(plotRoot.MinHeight, AnalysisPlotMinimumHeight);
        var sourceSeries = panes.SelectMany(pane => pane.Series).ToArray();
        var legendEntries = SelectableLegendEntries(sourceSeries);
        if (legendEntries.Count > 0)
        {
            var originals = plots.Select(plot => plot.Series.ToArray()).ToArray();
            var enabled = legendEntries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
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

                for (var index = 0; index < plots.Count; index++)
                {
                    plots[index].SetVisibleSeries(originals[index].Where(series =>
                        !TryGetSelectableLegend(series, out var seriesKey, out _)
                        || enabled.Contains(seriesKey)).ToArray());
                }
            });
            foreach (var plot in plots)
            {
                plot.PlotOptions = plot.PlotOptions with { ShowLegend = false };
            }

            return BuildPlotLegendLayout(plotRoot, legend);
        }
        return sourceSeries.Any(series => !string.IsNullOrWhiteSpace(series.Name))
            ? BuildPlotLegendLayout(plotRoot, BuildNamedSeriesLegend(plots))
            : plotRoot;
    }

    private static Panel BuildNamedSeriesLegend(IReadOnlyList<AnalysisPlotControl> plots)
    {
        var originals = plots.Select(plot => plot.Series).ToArray();
        // Position identifies a curve within this result, even when labels repeat.
        static string Key(int plot, int series) => $"series:{plot}:{series}";
        var entries = originals.SelectMany((series, plotIndex) => series
            .Select((item, seriesIndex) => (Key: Key(plotIndex, seriesIndex), Label: item.Name,
                Color: AnalysisPlotControl.SeriesColor(item))))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Label)).ToArray();
        var enabled = entries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var plot in plots)
            plot.PlotOptions = plot.PlotOptions with { ShowLegend = false };
        return BuildSelectableLegend(entries, (key, isVisible) =>
        {
            if (isVisible) enabled.Add(key);
            else enabled.Remove(key);
            for (var index = 0; index < plots.Count; index++)
            {
                var plotIndex = index;
                plots[index].SetVisibleSeries(originals[index].Where((series, seriesIndex) =>
                    string.IsNullOrWhiteSpace(series.Name) || enabled.Contains(Key(plotIndex, seriesIndex))).ToArray());
            }
        });
    }

    private static TextBlock EmptyPlotMessage(bool isVisible)
    {
        var message = new TextBlock
        {
            Text = "当前分析没有可绘制的数值序列",
            IsVisible = isVisible,
            FontSize = DisplayTypography.CompactBody,
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

    private static Grid BuildPlotLegendLayout(Control plotRoot, Control legend)
    {
        // Every selectable plot legend uses the same page-relative anchor,
        // independent of the plot's axes, pane count or square viewport.
        legend.HorizontalAlignment = HorizontalAlignment.Right;
        legend.VerticalAlignment = VerticalAlignment.Top;
        legend.Margin = new Thickness(ResponsivePlotLegendGrid.LegendInset);
        return new ResponsivePlotLegendGrid(plotRoot, legend);
    }

    private static Panel BuildSelectableLegend(
        IReadOnlyList<(string Key, string Label, Color Color)> entries,
        Action<string, bool> setVisibility)
    {
        var legend = new StackPanel { Spacing = 2 };
        foreach (var entry in entries)
        {
            var brush = new SolidColorBrush(entry.Color);
            var content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children =
                {
                    new Border
                    {
                        Width = 8,
                        Height = 8,
                        CornerRadius = new CornerRadius(4),
                        Background = brush,
                        Margin = new Thickness(0, 0, 7, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = entry.Label,
                        Foreground = brush,
                        Classes = { ProductFonts.NumericClass },
                        FontSize = DisplayTypography.CompactBody,
                        MaxWidth = 220,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
            Grid.SetColumn(content.Children[1], 1);
            var checkBox = new CheckBox
            {
                IsChecked = true,
                Content = content,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
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
        if (series.ConfigurationNumber is { } configuration && !string.IsNullOrWhiteSpace(series.LegendLabel))
        {
            key = $"configuration:{configuration}/{(string.IsNullOrWhiteSpace(series.LegendKey) ? series.Name : series.LegendKey)}";
            label = series.LegendLabel;
            return true;
        }
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

        var cards = new WrapPanel();
        foreach (var pane in populated)
        {
            var card = new StackPanel { Width = DisplayTypography.Scale(190), Margin = new Thickness(0, 2, 12, 2), Spacing = 2 };
            var header = new TextBlock
            {
                Text = pane.Title,
                FontSize = DisplayTypography.CompactBody,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            header.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextPrimary);
            card.Children.Add(header);
            foreach (var metric in pane.Metrics!)
            {
                var text = new TextBlock
                {
                    Text = $"{metric.Label}：{NumericDisplayFormatter.Format(metric.Value)} {metric.Unit}".TrimEnd(),
                    FontSize = DisplayTypography.CompactBody,
                    TextWrapping = TextWrapping.Wrap
                };
                text.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
                card.Children.Add(text);
            }
            cards.Children.Add(card);
        }

        var reference = populated
            .Select(pane => pane.Footer)
            .FirstOrDefault(footer => !string.IsNullOrWhiteSpace(footer));
        var referenceText = new TextBlock
        {
            Text = reference ?? string.Empty,
            IsVisible = !string.IsNullOrWhiteSpace(reference),
            FontSize = DisplayTypography.CompactBody,
            TextWrapping = TextWrapping.Wrap
        };
        referenceText.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                cards,
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
