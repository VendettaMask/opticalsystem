using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Services;

/// <summary>Uses the same plot builders and optical scene renderer as the workbench.</summary>
internal sealed class ReportImageRenderer(Border host)
{
    internal async Task<IReadOnlyList<string>> WriteAsync(ReportImageRequest request,
        string directory, string stem, CancellationToken cancellationToken)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = new List<string>();
            try
            {
                if (request.Scene is { } scene)
                {
                    var width = UniformDiagramPage.SheetWidth;
                    var height = UniformDiagramPage.PlotHeight;
                    var canvas = new OpticSceneControl
                    {
                        Scene = scene,
                        Width = width,
                        Height = height,
                        ViewMode = scene.Dimension == SceneDimension.TwoDimensional
                            ? OpticSceneViewMode.TwoDimensional : OpticSceneViewMode.ThreeDimensional,
                        ShowRays = true,
                        ShowScaleBar = true
                    };
                    var frame = new Border { Width = width, Height = height, Child = canvas };
                    frame.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.PlotBackground);
                    var page = new UniformDiagramPage(frame, PlotCaption.ForLayoutScene(scene, request.CapturedAt));
                    Attach(page, print: request.ForPrint);
                    canvas.FitView();
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                    Save(page, stem + ".png");
                }
                else if (request.Analysis is { } analysis)
                {
                    var plot = AnalysisPanel.CreateReportPlot(analysis, request.Document, request.CapturedAt);
                    if (plot is not null)
                    {
                        Attach(plot, UniformDiagramPage.SheetWidth, UniformDiagramPage.SheetHeight, request.ForPrint);
                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                        plot.UpdateLayout();
                        Save(plot.Child!, stem + ".png");
                    }
                    else
                    {
                        var content = analysis.View.ReportText;
                        if (string.IsNullOrWhiteSpace(content) && analysis.View.Table is { } table)
                            content = string.Join("\n", new[] { string.Join(" | ", table.Columns) }
                                .Concat(table.Rows.Select(row => string.Join(" | ", row))));
                        if (string.IsNullOrWhiteSpace(content))
                            content = string.Join("\n", analysis.View.Rows.Select(row => $"{row.Metric}: {row.Value}"));
                        var lines = content.Replace("\r", "").Split('\n');
                        var pages = lines.Chunk(44).ToArray();
                        for (var i = 0; i < pages.Length; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var page = Page($"{request.Title} ({i + 1}/{pages.Length})", request.Document);
                            var reportText = new TextBlock
                            {
                                Text = string.Join("\n", pages[i]),
                                TextWrapping = TextWrapping.NoWrap,
                                Classes = { ProductFonts.NumericClass },
                                FontSize = DisplayTypography.CardTitle,
                                Margin = new Thickness(16)
                            };
                            page.Children.Add(reportText);
                            Attach(page);
                            // Preserve tabular columns at the normal font size.
                            // Expand the image instead of wrapping numeric rows.
                            reportText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                            page.Width = Math.Max(1200, Math.Ceiling(reportText.DesiredSize.Width + 32));
                            Attach(page);
                            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                            Save(page, $"{stem}-{i + 1:D3}.png");
                        }
                    }
                }
                return (IReadOnlyList<string>)files;
            }
            finally { host.Child = null; }

            void Save(Control control, string fileName)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pixels = new PixelSize(checked((int)Math.Ceiling(control.Bounds.Width)), checked((int)Math.Ceiling(control.Bounds.Height)));
                if (pixels.Width <= 0 || pixels.Height <= 0 || pixels.Width > 32768 || pixels.Height > 32768
                    || (long)pixels.Width * pixels.Height > 33_554_432)
                    throw new InvalidOperationException("截图尺寸超过上限，请减少分析中同时显示的子图数量。");
                if (control is UniformDiagramPage source) source.ValidateForExport();
                using var bitmap = ExportImageFrame.Capture(control,
                    control is UniformDiagramPage ? UniformDiagramPage.ExportScale : 1);
                var file = Path.Combine(directory, fileName);
                AnalysisPanel.SavePlotImage(bitmap, file);
                files.Add(fileName);
            }
        });
    }

    private void Attach(Control control, double defaultWidth = 1200, double height = 900, bool print = false)
    {
        var width = double.IsFinite(control.Width) ? control.Width : defaultWidth;
        host.Width = width; host.Height = height;
        host.Child = print ? new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = control } : control;
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        if (control is StackPanel)
        {
            control.Measure(new Size(width, double.PositiveInfinity));
            control.Height = Math.Max(1, control.DesiredSize.Height);
            host.Height = control.Height;
            host.Measure(new Size(width, control.Height));
            host.Arrange(new Rect(0, 0, width, control.Height));
            host.UpdateLayout();
        }
    }

    private static StackPanel Page(string title, OpticalDocumentSnapshot document)
    {
        var page = new StackPanel { Width = 1200, Spacing = 10 };
        page.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.PlotBackground);
        page.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = DisplayTypography.LargeTitle,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(16, 12)
        });
        page.Children.Add(new TextBlock
        {
            Text = $"{document.Name} | EFL {document.EffectiveFocalLength:G6} mm | F/# {document.FNumber:G5} | TTL {document.TotalTrack:G6} mm",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 0)
        });
        return page;
    }
}
