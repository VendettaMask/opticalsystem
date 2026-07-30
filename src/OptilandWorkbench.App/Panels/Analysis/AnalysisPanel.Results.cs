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
    private static Control BuildResultContent(
        AnalysisViewDto view,
        OpticalDocumentSnapshot document,
        DateTimeOffset generatedAt,
        SceneDto? cardinalScene = null)
    {
        if (IsCardinalPointsView(view) && cardinalScene?.TwoDimensional is not null)
        {
            return BuildCardinalPointsScene(view, cardinalScene);
        }

        if (IsSeidelCoefficientsView(view))
        {
            return BuildSeidelCoefficientsReport(view, document, generatedAt);
        }

        if (IsZernikeFringeView(view)
            || IsZernikeStandardView(view)
            || IsZernikeAnnularView(view))
        {
            return BuildZernikeCoefficientReport(view, document, generatedAt);
        }

        var plotRoot = view.PlotPanes.Count > 0
            ? IsStandardSpotView(view)
                ? BuildStandardSpotPanePlot(view.PlotPanes)
                : IsThroughFocusSpotView(view)
                ? BuildThroughFocusSpotPanePlot(view.PlotPanes, view.PlotPaneColumns)
                : IsConfigurationMatrixSpotView(view)
                ? BuildConfigurationMatrixSpotPanePlot(view.PlotPanes, view.PlotPaneColumns)
                : IsMatrixSpotView(view)
                ? BuildMatrixSpotPanePlot(view.PlotPanes, view.PlotPaneColumns)
                : IsRayFanView(view) || IsOpticalPathDifferenceView(view)
                ? BuildRayFanPanePlot(view.PlotPanes)
                : BuildPanePlot(view.PlotPanes, view.PlotPaneColumns)
            : IsSeidelDiagramView(view)
                ? BuildSeidelDiagramPlot(view)
                : IsWavefrontMapView(view)
                    ? BuildWavefrontMapPlot(view)
                : IsFftPsfView(view)
                    ? BuildFftPsfPlot(view)
                : IsHuygensPsfView(view)
                    ? BuildHuygensPsfPlot(view)
                : IsFoucaultView(view)
                    ? BuildFoucaultPlot(view)
                : IsFullFieldAberrationView(view)
                    ? BuildFullFieldAberrationPlot(view)
                : BuildSinglePlot(view);
        var plotPage = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        plotPage.Children.Add(plotRoot);
        var titleBlock = BuildAnalysisTitleBlock(view, document, generatedAt);
        Grid.SetRow(titleBlock, 1);
        plotPage.Children.Add(titleBlock);
        var resultsGrid = BuildResultsGrid(view);
        var report = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            MinHeight = 300,
            Text = view.ReportText
        };
        report.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        report.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        return new TabControl
        {
            TabStripPlacement = Avalonia.Controls.Dock.Bottom,
            ItemsSource = new object[]
            {
                new TabItem { Header = "绘图", Content = plotPage },
                new TabItem { Header = "数据", Content = resultsGrid },
                new TabItem { Header = "文本", Content = report }
            }
        };
    }

    private static bool IsCardinalPointsView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "基面数据", StringComparison.Ordinal)
            || string.Equals(view.Name, "Cardinal Points Data", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSeidelCoefficientsView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "赛德尔系数", StringComparison.Ordinal)
            || string.Equals(view.Name, "Seidel Coefficients", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsZernikeFringeView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "Zernike Fringe系数", StringComparison.Ordinal)
            || string.Equals(view.Name, "Zernike Fringe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsZernikeStandardView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "Zernike Standard系数", StringComparison.Ordinal)
            || string.Equals(view.Name, "Zernike Standard", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsZernikeAnnularView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "Zernike Annular系数", StringComparison.Ordinal)
            || string.Equals(view.Name, "Zernike Annular", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSeidelDiagramView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "赛德尔图", StringComparison.Ordinal)
            || string.Equals(view.Name, "Seidel Diagram", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFullFieldAberrationView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "全视场像差", StringComparison.Ordinal)
            || string.Equals(view.Name, "Full Field Aberration", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWavefrontMapView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "波前图", StringComparison.Ordinal)
            || string.Equals(view.Name, "Wavefront Map", StringComparison.OrdinalIgnoreCase);
    }

    private static Control BuildWavefrontMapPlot(AnalysisViewDto view)
    {
        return new WavefrontSurfaceControl
        {
            Series = view.Series.FirstOrDefault(),
            RotationDegrees = FindRowNumber(view, "旋转", 0),
            DisplayScale = FindRowNumber(view, "显示缩放", 1),
            DisplayAs = FindRowText(view, "显示为", "表面"),
            MinHeight = 420
        };
    }

    private static bool IsFftPsfView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "FFT PSF", StringComparison.OrdinalIgnoreCase);
    }

    private static Control BuildFftPsfPlot(AnalysisViewDto view)
    {
        var logarithmic = FindRowText(view, "类型", "线性")
            .Contains("对数", StringComparison.Ordinal);
        return new WavefrontSurfaceControl
        {
            Series = view.Series.FirstOrDefault(),
            RotationDegrees = FindRowNumber(view, "旋转", 0),
            DisplayScale = 1,
            DisplayAs = FindRowText(view, "显示为", "伪彩色"),
            ColorBarTitle = view.PlotOptions.Title,
            ColorBarUnit = logarithmic ? "dB" : string.Empty,
            XAxisLabel = "X 像面（µm）",
            YAxisLabel = "Y 像面（µm）",
            ValueMinimum = logarithmic ? null : 0,
            MinHeight = 420
        };
    }

    private static bool IsHuygensPsfView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "惠更斯PSF", StringComparison.Ordinal)
            || string.Equals(view.Name, "Huygens PSF", StringComparison.OrdinalIgnoreCase);
    }

    private static Control BuildHuygensPsfPlot(AnalysisViewDto view)
    {
        var logarithmic = FindRowText(view, "类型", "线性")
            .Contains("对数", StringComparison.Ordinal);
        return new WavefrontSurfaceControl
        {
            Series = view.Series.FirstOrDefault(),
            RotationDegrees = FindRowNumber(view, "旋转", 0),
            DisplayScale = 1,
            DisplayAs = FindRowText(view, "显示为", "伪彩色"),
            ColorBarTitle = view.PlotOptions.Title,
            ColorBarUnit = logarithmic ? "dB" : string.Empty,
            XAxisLabel = "X 像面（µm）",
            YAxisLabel = "Y 像面（µm）",
            ValueMinimum = logarithmic ? null : 0,
            MinHeight = 420
        };
    }

    private static bool IsFoucaultView(AnalysisViewDto view)
    {
        return string.Equals(view.Name, "傅科分析", StringComparison.Ordinal)
            || string.Equals(view.Name, "Foucault Analysis", StringComparison.OrdinalIgnoreCase);
    }

    private static Control BuildFoucaultPlot(AnalysisViewDto view)
    {
        return new FoucaultPlotControl
        {
            Series = view.Series.FirstOrDefault(),
            DisplayAs = FindRowText(view, "显示为", "灰度"),
            MinHeight = 420
        };
    }

    private static Control BuildFullFieldAberrationPlot(AnalysisViewDto view)
    {
        return new FullFieldAberrationControl
        {
            Series = view.Series.FirstOrDefault(),
            XFieldWidth = FindRowNumber(view, "X 视场宽度", 1),
            YFieldWidth = FindRowNumber(view, "Y 视场宽度", 1),
            DisplayAs = FindRowText(view, "显示为", "图标"),
            DisplayMode = FindRowText(view, "显示", "绝对值"),
            MinHeight = 420
        };
    }

    private static string FindRowText(AnalysisViewDto view, string metric, string fallback)
    {
        return view.Rows.FirstOrDefault(row =>
                string.Equals(row.Metric, metric, StringComparison.Ordinal))?.Value
            ?? view.Rows.FirstOrDefault(row =>
                row.Metric.Contains(metric, StringComparison.Ordinal))?.Value
            ?? fallback;
    }

    private static Control BuildSeidelDiagramPlot(AnalysisViewDto view)
    {
        return new SeidelDiagramControl
        {
            Table = view.Table,
            MaximumAberration = FindRowNumber(view, "最大像差范围", 0.1),
            GridInterval = FindRowNumber(view, "网格线间隔", 0.01),
            MinHeight = 420
        };
    }

    private static double FindRowNumber(AnalysisViewDto view, string metric, double fallback)
    {
        var text = view.Rows.FirstOrDefault(row =>
            row.Metric.Contains(metric, StringComparison.Ordinal))?.Value;
        return TryNumber(text, out var value) ? value : fallback;
    }

    private static Control BuildSeidelCoefficientsReport(
        AnalysisViewDto view,
        OpticalDocumentSnapshot document,
        DateTimeOffset generatedAt)
    {
        var file = string.IsNullOrWhiteSpace(document.Path) ? document.Name : document.Path;
        var text = string.Join(Environment.NewLine, new[]
        {
            "像差系数数据表",
            "",
            $"文件：{file}",
            "题目：",
            $"日期：{generatedAt.LocalDateTime:yyyy/M/d}",
            "",
            view.ReportText
        });
        var report = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            Padding = new Thickness(14, 12),
            Text = text
        };
        report.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        report.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        return report;
    }

    private static Control BuildZernikeCoefficientReport(
        AnalysisViewDto view,
        OpticalDocumentSnapshot document,
        DateTimeOffset generatedAt)
    {
        var file = string.IsNullOrWhiteSpace(document.Path) ? document.Name : document.Path;
        var title = IsZernikeStandardView(view)
            ? "Zernike Standard系数数据表"
            : IsZernikeAnnularView(view)
                ? "Zernike Annular系数数据表"
                : "Zernike Fringe系数数据表";
        var text = string.Join(Environment.NewLine, new[]
        {
            title,
            "",
            $"文件：{file}",
            "题目：",
            $"日期：{generatedAt.LocalDateTime:yyyy/M/d}",
            "",
            view.ReportText
        });
        var report = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            Padding = new Thickness(14, 12),
            Text = text
        };
        report.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        report.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        root.Children.Add(report);
        var titleBlock = BuildAnalysisTitleBlock(view, document, generatedAt);
        Grid.SetRow(titleBlock, 1);
        root.Children.Add(titleBlock);
        return root;
    }

    private static Control BuildCardinalPointsScene(AnalysisViewDto view, SceneDto scene)
    {
        var twoDimensional = scene.TwoDimensional!;
        var startSurface = twoDimensional.Surfaces
            .Where(surface => surface.SurfaceNumber > 0)
            .MinBy(surface => surface.SurfaceNumber)
            ?? twoDimensional.Surfaces.First();
        var endSurface = twoDimensional.Surfaces.MaxBy(surface => surface.SurfaceNumber)
            ?? twoDimensional.Surfaces[^1];
        var startZ = startSurface.Points.Select(point => point.Z).DefaultIfEmpty(twoDimensional.ZMin).Average();
        var endZ = endSurface.Points.Select(point => point.Z).DefaultIfEmpty(twoDimensional.ZMax).Average();
        var referenceSurfaceNumber = view.Rows
            .Where(row => string.Equals(row.Metric, "参考面", StringComparison.Ordinal))
            .Select(row => TryNumber(row.Value, out var number) ? (int?)Math.Round(number) : null)
            .FirstOrDefault()
            ?? endSurface.SurfaceNumber;
        var referenceSurface = twoDimensional.Surfaces.FirstOrDefault(surface =>
                surface.SurfaceNumber == referenceSurfaceNumber)
            ?? endSurface;
        var referenceZ = referenceSurface.Points
            .Select(point => point.Z)
            .DefaultIfEmpty(endZ)
            .Average();
        var annotations = new List<OpticSceneAnnotation2D>();
        annotations.Add(new OpticSceneAnnotation2D(
            referenceZ,
            $"参考面 S{referenceSurface.SurfaceNumber}  0.000000 mm",
            Color.FromRgb(72, 78, 86),
            OpticSceneAnnotationPlacement2D.Above));

        AddPair("焦平面", "F物", "F像", Color.FromRgb(205, 52, 52));
        AddPair("主平面", "H物", "H像", Color.FromRgb(34, 111, 202));
        AddPair("反主平面", "H̄物", "H̄像", Color.FromRgb(130, 76, 170));
        AddPair("节平面", "N物", "N像", Color.FromRgb(36, 145, 86));
        AddPair("反节平面", "N̄物", "N̄像", Color.FromRgb(39, 135, 116));

        var sceneControl = new OpticSceneControl
        {
            Scene = scene,
            ViewMode = OpticSceneViewMode.TwoDimensional,
            VisualStyle = OpticSceneVisualStyle.OpticalLayout,
            ShowRays = true,
            ShowRayArrows = false,
            ShowScaleBar = true,
            MinHeight = 420,
            Annotations2D = annotations
        };
        var table = BuildCardinalPointsTable(view, referenceSurface.SurfaceNumber);
        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("3*,Auto"),
            Children = { sceneControl, table }
        };
        Grid.SetRow(table, 1);
        return content;

        void AddPair(string rowName, string objectLabel, string imageLabel, Color color)
        {
            var row = view.Table?.Rows.FirstOrDefault(candidate =>
                string.Equals(candidate.ElementAtOrDefault(0), rowName, StringComparison.Ordinal));
            if (row is null
                || !TryNumber(row.ElementAtOrDefault(1), out var objectPosition)
                || !TryNumber(row.ElementAtOrDefault(2), out var imagePosition))
            {
                return;
            }

            var objectZ = startZ + objectPosition;
            var imageZ = endZ + imagePosition;
            annotations.Add(new OpticSceneAnnotation2D(
                objectZ,
                $"{objectLabel}  {FormatReferenceDistance(objectZ - referenceZ)} mm",
                color,
                OpticSceneAnnotationPlacement2D.Above));
            annotations.Add(new OpticSceneAnnotation2D(
                imageZ,
                $"{imageLabel}  {FormatReferenceDistance(imageZ - referenceZ)} mm",
                color,
                OpticSceneAnnotationPlacement2D.Below));
        }
    }

    private static string FormatReferenceDistance(double distance)
    {
        return distance.ToString("+0.000000;-0.000000;0.000000", CultureInfo.InvariantCulture);
    }

    private static Control BuildCardinalPointsTable(AnalysisViewDto view, int referenceSurfaceNumber)
    {
        if (view.Table is null)
        {
            return new Border();
        }

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.4*,*,*"),
            RowDefinitions = new RowDefinitions(
                string.Join(',', Enumerable.Repeat("28", view.Table.Rows.Count + 1)))
        };
        for (var column = 0; column < view.Table.Columns.Count; column++)
        {
            AddCell(view.Table.Columns[column], 0, column, true);
        }

        for (var row = 0; row < view.Table.Rows.Count; row++)
        {
            for (var column = 0; column < view.Table.Columns.Count; column++)
            {
                AddCell(view.Table.Rows[row].ElementAtOrDefault(column) ?? string.Empty, row + 1, column, false);
            }
        }

        var panel = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(24, 8, 24, 14),
            Children =
            {
                new TextBlock
                {
                    Text = $"基面数据（mm）    图中标注距离相对于参考面 S{referenceSurfaceNumber}",
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold
                },
                grid
            }
        };
        var border = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = panel
        };
        border.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return border;

        void AddCell(string text, int row, int column, bool header)
        {
            var cell = new TextBlock
            {
                Text = text,
                FontSize = header ? 12 : 11.5,
                FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal,
                FontFamily = column == 0 ? FontFamily.Default : new FontFamily("Cascadia Mono, Consolas"),
                Margin = new Thickness(8, 3),
                HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }
    }

    private static bool TryNumber(string? text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static DataGrid BuildResultsGrid(AnalysisViewDto view)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            MinHeight = 220,
            ItemsSource = view.Table is null
                ? (System.Collections.IEnumerable)view.Rows
                : view.Table.Rows
        };
        grid.BindThemeResource(DataGrid.RowBackgroundProperty, ThemeResourceBindings.Surface);
        if (view.Table is not null)
        {
            var rowGroups = new Dictionary<IReadOnlyList<string>, string>(
                ReferenceEqualityComparer.Instance);
            for (var index = 0; index < view.Table.Rows.Count; index++)
            {
                rowGroups[view.Table.Rows[index]] = view.Table.RowGroups is not null
                    && index < view.Table.RowGroups.Count
                        ? view.Table.RowGroups[index]
                        : string.Empty;
            }
            grid.LoadingRow += (_, args) =>
            {
                if (args.Row.DataContext is not IReadOnlyList<string> row
                    || !rowGroups.TryGetValue(row, out var group))
                {
                    return;
                }

                if (group.Equals("实光线", StringComparison.Ordinal))
                {
                    args.Row.Background = new SolidColorBrush(Color.FromArgb(28, 31, 119, 180));
                    args.Row.Foreground = new SolidColorBrush(Color.FromRgb(24, 82, 125));
                }
                else if (group.Equals("近轴光线", StringComparison.Ordinal))
                {
                    args.Row.Background = new SolidColorBrush(Color.FromArgb(28, 255, 127, 14));
                    args.Row.Foreground = new SolidColorBrush(Color.FromRgb(166, 82, 0));
                }
            };
            for (var index = 0; index < view.Table.Columns.Count; index++)
            {
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = view.Table.Columns[index],
                    Binding = new Binding($"[{index}]"),
                    Width = DataGridLength.Auto
                });
            }

            return grid;
        }

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "指标",
            Binding = new Binding(nameof(AnalysisRowDto.Metric)),
            Width = new DataGridLength(180)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "值",
            Binding = new Binding(nameof(AnalysisRowDto.Value)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        return grid;
    }

    private static Control BuildAnalysisErrorContent(string message)
    {
        var panel = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 720,
            Children =
            {
                new LocalIcon
                {
                    IconName = "circle-alert",
                    Width = 32,
                    Height = 32,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = "分析未完成",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Foreground = Brushes.Gray
                }
            }
        };
        return new Border
        {
            Padding = new Thickness(24),
            Child = panel
        };
    }

    private static Control BuildAnalysisTitleBlock(
        AnalysisViewDto view,
        OpticalDocumentSnapshot document,
        DateTimeOffset generatedAt)
    {
        var compactSummary = BuildCompactAnalysisSummary(view);
        var showPaneMetrics = !IsConfigurationMatrixSpotView(view);
        var hasPaneMetrics = showPaneMetrics
            && view.PlotPanes.Any(pane => pane.Metrics is { Count: > 0 });
        var visibleRows = hasPaneMetrics || compactSummary is not null
            ? Array.Empty<AnalysisRowDto>()
            : view.Rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Metric))
                .Take(7)
                .ToArray();
        var resultLines = compactSummary?.Lines ?? (visibleRows.Length == 0
            ? "暂无摘要数据"
            : string.Join(Environment.NewLine, visibleRows.Select(row => $"{row.Metric}: {row.Value}")));
        if (compactSummary is null && view.Rows.Count > visibleRows.Length)
        {
            resultLines += $"{Environment.NewLine}其余 {view.Rows.Count - visibleRows.Length} 项见“数据”页";
        }

        var documentName = string.IsNullOrWhiteSpace(document.Path)
            ? document.Name
            : Path.GetFileName(document.Path);
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?.Split('+')[0]
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "1.0.0";

        var left = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(16, 10, 16, 10)
        };
        left.Children.Add(new TextBlock
        {
            Text = compactSummary?.Title ?? view.Name,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        });
        left.Children.Add(new TextBlock
        {
            Text = generatedAt.LocalDateTime.ToString(compactSummary is null ? "yyyy/MM/dd HH:mm:ss" : "yyyy/M/d"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(82, 82, 87))
        });
        var resultSummary = new TextBlock
        {
            Text = resultLines,
            FontSize = 11,
            LineHeight = 16,
            TextWrapping = TextWrapping.Wrap
        };
        var paneMetrics = showPaneMetrics
            ? BuildPaneMetricsSummary(view.PlotPanes)
            : null;
        if (paneMetrics is null)
        {
            left.Children.Add(resultSummary);
        }
        else if (visibleRows.Length == 0)
        {
            left.Children.Add(paneMetrics);
        }
        else
        {
            var summaryBody = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*")
            };
            summaryBody.Children.Add(resultSummary);
            Grid.SetColumn(paneMetrics, 1);
            summaryBody.Children.Add(paneMetrics);
            left.Children.Add(summaryBody);
        }

        var product = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "S.T.A.R. Labs",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = $"Optical System Design  {version}",
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };
        var documentInfo = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(12, 7),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = documentName,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = document.Name,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(82, 82, 87)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };
        var right = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        right.Children.Add(product);
        var documentBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(174, 174, 178)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = documentInfo
        };
        Grid.SetRow(documentBorder, 1);
        right.Children.Add(documentBorder);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,*"),
            MinHeight = 126
        };
        grid.Children.Add(left);
        var rightBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(174, 174, 178)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = right
        };
        Grid.SetColumn(rightBorder, 1);
        grid.Children.Add(rightBorder);

        var titleBorder = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 1),
            Child = grid
        };
        titleBorder.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        titleBorder.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return titleBorder;
    }

    private static CompactAnalysisSummary? BuildCompactAnalysisSummary(AnalysisViewDto view)
    {
        if (string.Equals(view.Name, "光迹图", StringComparison.Ordinal)
            || string.Equals(view.Name, "Footprint Diagram", StringComparison.OrdinalIgnoreCase))
        {
            var surfaceNumber = FindRowText(view, "表面序号", "0");
            var surfaceLabel = FindRowText(view, "表面标注", string.Empty);
            var rayXMinimum = FindRowText(view, "光线 X 最小", "0");
            var rayXMaximum = FindRowText(view, "光线 X 最大", "0");
            var rayYMinimum = FindRowText(view, "光线 Y 最小", "0");
            var rayYMaximum = FindRowText(view, "光线 Y 最大", "0");
            var maximumRadius = FindRowText(view, "最大半径", "0");
            var wavelengths = FindRowText(view, "波长 (µm)", "0");
            var colorBasis = FindRowText(view, "颜色显示", "field");
            var legendMeaning = string.Equals(colorBasis, "field", StringComparison.OrdinalIgnoreCase)
                ? "图例对应于视场位置"
                : "图例对应于波长";
            var surfaceText = string.IsNullOrWhiteSpace(surfaceLabel)
                ? $"面 {surfaceNumber}:"
                : $"面 {surfaceNumber}: {surfaceLabel}";
            return new CompactAnalysisSummary(
                "光迹图",
                $"{surfaceText}{Environment.NewLine}"
                + $"光线 X 最小 = {rayXMinimum}    光线 X 最大 = {rayXMaximum}{Environment.NewLine}"
                + $"光线 Y 最小 = {rayYMinimum}    光线 Y 最大 = {rayYMaximum}{Environment.NewLine}"
                + $"最大半径 = {maximumRadius}    波长 = {wavelengths}{Environment.NewLine}"
                + legendMeaning);
        }

        if (IsHuygensPsfView(view))
        {
            var wavelengthRange = FindRowText(view, "波长范围", "0");
            var fieldHy = FindRowText(view, "归一化视场 Hy", "0");
            var imageExtent = FindRowText(view, "像的尺寸", "0");
            var strehl = FindRowText(view, "峰值斯特列尔比", "0");
            var centroidX = FindRowText(view, "质心 X", "0");
            var centroidY = FindRowText(view, "质心 Y", "0");
            return new CompactAnalysisSummary(
                view.PlotOptions.Title,
                $"从 {wavelengthRange}，视场 Hy = {fieldHy}{Environment.NewLine}"
                + $"像的尺寸：{imageExtent} µm 平方。{Environment.NewLine}"
                + $"斯特列尔率：{strehl}{Environment.NewLine}"
                + $"中心坐标：{centroidX}, {centroidY} µm");
        }

        if (IsFoucaultView(view))
        {
            var wavelength = FindRowText(view, "分析波长", "0");
            var field = FindRowText(view, "归一化视场 Hy", "0");
            var knife = FindRowText(view, "刀口", "水平线上");
            var position = FindRowText(view, "刀口 Y 位置", "0");
            var sampling = FindRowText(view, "采样", "32 x 32");
            return new CompactAnalysisSummary(
                "傅科分析",
                $"{wavelength} µm，视场 Hy = {field}{Environment.NewLine}"
                + $"刀口：{knife}，Y = {position} µm{Environment.NewLine}"
                + $"取样：{sampling}");
        }

        if (IsWavefrontMapView(view))
        {
            var wavelength = FindRowText(view, "分析波长", "0");
            var field = FindRowText(view, "归一化视场 Hy", "0");
            var peakToValley = FindRowText(view, "波峰到波谷", "0");
            var rms = FindRowText(view, "RMS 波数", "0");
            var surface = FindRowText(view, "表面标注", "像面");
            var pupilDiameter = FindRowText(view, "出瞳直径", "0");
            return new CompactAnalysisSummary(
                "波前函数",
                $"{wavelength} µm，视场 Hy = {field}{Environment.NewLine}"
                + $"波峰到波谷 = {peakToValley} 波，RMS = {rms} 波{Environment.NewLine}"
                + $"面：{surface}{Environment.NewLine}"
                + $"出瞳直径：{pupilDiameter} mm");
        }

        if (IsFullFieldAberrationView(view))
        {
            var aberration = FindRowText(view, "像差", "离焦");
            var wavelength = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("波长", StringComparison.Ordinal))?.Value ?? "0";
            var decomposition = FindRowText(view, "分解", "Zernike项");
            var maximumTerm = FindRowText(view, "最大项", "37");
            var mean = FindRowText(view, "平均", "0");
            var display = FindRowText(view, "显示", "绝对值");
            var shape = FindRowText(view, "视场形状", "椭圆");
            var minimum = FindRowText(view, "绘图范围最小值", "0");
            var maximum = FindRowText(view, "绘图范围最大值", "0");
            return new CompactAnalysisSummary(
                aberration,
                $"波长：{wavelength} µm{Environment.NewLine}"
                + $"分解：{decomposition}    最大项：{maximumTerm}{Environment.NewLine}"
                + $"平均：{mean} 波长{Environment.NewLine}"
                + $"显示：{display}    视场：{shape}{Environment.NewLine}"
                + $"绘图范围：从 {minimum} 到 {maximum}");
        }

        if (string.Equals(view.Name, "轴向像差", StringComparison.Ordinal)
            || string.Equals(view.Name, "Axial Aberration", StringComparison.OrdinalIgnoreCase))
        {
            var shortest = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("短波长", StringComparison.Ordinal))?.Value ?? "0";
            var longest = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("长波长", StringComparison.Ordinal))?.Value ?? "0";
            return new CompactAnalysisSummary(
                "轴向像差",
                $"波长：从 {shortest} 到 {longest} µm{Environment.NewLine}"
                + "图例对应于波长");
        }

        if (string.Equals(view.Name, "垂轴色差", StringComparison.Ordinal)
            || string.Equals(view.Name, "Lateral Color", StringComparison.OrdinalIgnoreCase))
        {
            var shortest = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("短波长", StringComparison.Ordinal))?.Value ?? "0";
            var longest = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("长波长", StringComparison.Ordinal))?.Value ?? "0";
            var realRays = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("使用实际光线", StringComparison.Ordinal))?.Value;
            var rayText = string.Equals(realRays, "True", StringComparison.OrdinalIgnoreCase)
                ? "使用实际光线。"
                : "使用近轴光线。";
            return new CompactAnalysisSummary(
                "垂轴色差",
                $"短波长：{shortest} µm{Environment.NewLine}"
                + $"长波长：{longest} µm{Environment.NewLine}"
                + rayText);
        }

        if (string.Equals(view.Name, "色焦移", StringComparison.Ordinal)
            || string.Equals(view.Name, "Color Focus Shift", StringComparison.OrdinalIgnoreCase))
        {
            var maximumChange = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("最大焦移变化", StringComparison.Ordinal))?.Value ?? "0";
            var diffractionLimit = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("衍射极限变化", StringComparison.Ordinal))?.Value ?? "0";
            var pupilZone = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("光瞳区域", StringComparison.Ordinal))?.Value ?? "0";
            return new CompactAnalysisSummary(
                "色焦移",
                $"最大焦移变化：{maximumChange} µm{Environment.NewLine}"
                + $"衍射极限变化：{diffractionLimit} µm{Environment.NewLine}"
                + $"光瞳区域：{pupilZone}");
        }

        if (IsSeidelDiagramView(view))
        {
            var wavelength = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("波长", StringComparison.Ordinal))?.Value ?? "0";
            var maximum = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("最大像差范围", StringComparison.Ordinal))?.Value ?? "0.1";
            var interval = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("网格线间隔", StringComparison.Ordinal))?.Value ?? "0.01";
            return new CompactAnalysisSummary(
                "赛德尔图",
                $"波长：{wavelength} µm{Environment.NewLine}"
                + $"最大像差范围是 {maximum} 毫米。{Environment.NewLine}"
                + $"网格线相隔 {interval} 毫米。");
        }

        if (string.Equals(view.Name, "矩阵点列图", StringComparison.Ordinal)
            || string.Equals(view.Name, "Matrix Spot Diagram", StringComparison.OrdinalIgnoreCase))
        {
            var scale = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("缩放标尺", StringComparison.Ordinal))?.Value ?? "0";
            var reference = view.Rows.FirstOrDefault(row =>
                string.Equals(row.Metric, "参考", StringComparison.Ordinal))?.Value ?? "主光线";
            return new CompactAnalysisSummary(
                "矩阵点列图",
                $"单位是 µm。    图例对应于波长{Environment.NewLine}"
                + $"缩放标尺：{scale}    参考：{reference}");
        }

        if (IsConfigurationMatrixSpotView(view))
        {
            var wavelengthCount = Math.Clamp(
                view.PlotPaneColumns,
                1,
                Math.Max(1, view.PlotPanes.Count));
            var fieldCount = (int)Math.Ceiling(view.PlotPanes.Count / (double)wavelengthCount);
            var reference = view.Rows.FirstOrDefault(row =>
                string.Equals(row.Metric, "参考", StringComparison.Ordinal))?.Value ?? "主光线";
            return new CompactAnalysisSummary(
                "结构矩阵点列图",
                $"行：结构 × 视场    列：波长{Environment.NewLine}"
                + $"{fieldCount} 个视场 × {wavelengthCount} 个波长，共 {view.PlotPanes.Count} 个点列图{Environment.NewLine}"
                + $"单位：mm    参考：{reference}");
        }

        if (string.Equals(view.Name, "全视场点列图", StringComparison.Ordinal)
            || string.Equals(view.Name, "Full Field Spot Diagram", StringComparison.OrdinalIgnoreCase))
        {
            var rmsRadius = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("RMS 半径", StringComparison.Ordinal))?.Value ?? "0";
            var geometricRadius = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("GEO 半径", StringComparison.Ordinal))?.Value ?? "0";
            var scale = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("缩放标尺", StringComparison.Ordinal))?.Value ?? "0";
            var reference = view.Rows.FirstOrDefault(row =>
                string.Equals(row.Metric, "参考", StringComparison.Ordinal))?.Value ?? "主光线";
            return new CompactAnalysisSummary(
                "全视场点列图",
                $"单位是 µm。        图例对应于波长{Environment.NewLine}"
                + $"RMS 半径：{rmsRadius}    GEO 半径：{geometricRadius}{Environment.NewLine}"
                + $"缩放标尺：{scale}    参考：{reference}");
        }

        if (string.Equals(view.Name, "场曲/畸变", StringComparison.Ordinal)
            || string.Equals(view.Name, "Field Curvature and Distortion", StringComparison.OrdinalIgnoreCase))
        {
            var curvatureMaximum = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("场曲.最大像面偏移", StringComparison.Ordinal));
            var distortionMaximum = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("畸变.最大绝对畸变", StringComparison.Ordinal));
            var distortionModel = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("畸变.畸变模型", StringComparison.Ordinal));
            var combinedLines = new List<string>();
            if (curvatureMaximum is not null)
            {
                combinedLines.Add($"最大像面偏移 = {curvatureMaximum.Value} mm");
            }

            if (distortionMaximum is not null)
            {
                var combinedDistortionUnit = distortionMaximum.Metric.Contains("mm", StringComparison.OrdinalIgnoreCase)
                    ? " mm"
                    : "%";
                combinedLines.Add($"最大畸变 = {distortionMaximum.Value}{combinedDistortionUnit}");
            }

            if (distortionModel is not null)
            {
                combinedLines.Add($"畸变模型 = {distortionModel.Value}");
            }

            return new CompactAnalysisSummary(
                "场曲/畸变",
                combinedLines.Count == 0 ? "暂无摘要数据" : string.Join(Environment.NewLine, combinedLines));
        }

        if (string.Equals(view.Name, "场曲", StringComparison.Ordinal)
            || string.Equals(view.Name, "Field Curvature", StringComparison.OrdinalIgnoreCase))
        {
            var curvatureMaximumField = FindMaximumFieldRow(view);
            var sagittalFieldCurvature = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("最大弧矢场曲", StringComparison.Ordinal));
            var tangentialFieldCurvature = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("最大子午场曲", StringComparison.Ordinal));
            var maximumImageDelta = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("最大像面偏移", StringComparison.Ordinal));
            var curvatureLines = new List<string>();
            AddMaximumFieldLine(curvatureLines, curvatureMaximumField);
            if (sagittalFieldCurvature is not null)
            {
                curvatureLines.Add($"弧矢场曲 = {sagittalFieldCurvature.Value} mm");
            }

            if (tangentialFieldCurvature is not null)
            {
                curvatureLines.Add($"子午场曲 = {tangentialFieldCurvature.Value} mm");
            }

            if (maximumImageDelta is not null)
            {
                curvatureLines.Add($"最大像面偏移 = {maximumImageDelta.Value} mm");
            }

            return new CompactAnalysisSummary(
                "场曲",
                curvatureLines.Count == 0 ? "暂无摘要数据" : string.Join(Environment.NewLine, curvatureLines));
        }

        if (!string.Equals(view.Name, "畸变", StringComparison.Ordinal)
            && !string.Equals(view.Name, "Distortion", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var model = view.Rows.FirstOrDefault(row => row.Metric.Contains("畸变模型", StringComparison.Ordinal))?.Value;
        var title = model?.ToLowerInvariant() switch
        {
            "f-tan" => "F-Tan(Theta) 畸变",
            "f-theta" => "F-Theta 畸变",
            _ => "畸变"
        };
        var maximumField = FindMaximumFieldRow(view);
        var maximumDistortion = view.Rows.FirstOrDefault(row =>
            row.Metric.Contains("最大绝对畸变", StringComparison.Ordinal));
        var distortionUnit = maximumDistortion?.Metric.Contains("mm", StringComparison.OrdinalIgnoreCase) == true
            ? " mm"
            : "%";
        var lines = new List<string>();
        AddMaximumFieldLine(lines, maximumField);

        if (maximumDistortion is not null)
        {
            lines.Add($"最大畸变 = {maximumDistortion.Value}{distortionUnit}");
        }

        return new CompactAnalysisSummary(title, lines.Count == 0 ? "暂无摘要数据" : string.Join(Environment.NewLine, lines));
    }

    private static AnalysisRowDto? FindMaximumFieldRow(AnalysisViewDto view)
    {
        return view.Rows.FirstOrDefault(row =>
            row.Metric.Contains("最大视场", StringComparison.Ordinal)
            || row.Metric.Contains("最大物高", StringComparison.Ordinal)
            || row.Metric.Contains("最大像高", StringComparison.Ordinal));
    }

    private static void AddMaximumFieldLine(ICollection<string> lines, AnalysisRowDto? maximumField)
    {
        if (maximumField is null)
        {
            return;
        }

        var fieldUnit = maximumField.Metric.Contains("deg", StringComparison.OrdinalIgnoreCase)
            ? " 度"
            : " mm";
        lines.Add($"最大视场 = {maximumField.Value}{fieldUnit}");
    }

    private sealed record CompactAnalysisSummary(string Title, string Lines);
}
