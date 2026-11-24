using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Formatting;

namespace OpticalSystemDesign.App.Controls;

/// <summary>Shared plot title block with the outlined company mark and source identity.</summary>
internal static class PlotCaption
{
    internal const double MinimumHeight = 132;

    internal static Border ForLayoutScene(SceneDto scene, DateTimeOffset generatedAt, bool colorByConfiguration = false)
    {
        var twoDimensional = scene.Dimension == SceneDimension.TwoDimensional;
        var nonSequential = scene.ThreeDimensional is { } model
            && model.Surfaces.Any(surface => surface.RenderRole != SceneSurfaceRenderRole.OpticalSurface);
        var summary = new StackPanel { Spacing = 2, Margin = new Thickness(16, 8) };
        summary.Children.Add(Label(twoDimensional ? "二维图" : "三维布局", DisplayTypography.CardTitle, ThemeResourceBindings.TextPrimary,
            FontWeight.SemiBold));
        summary.Children.Add(Label(generatedAt.LocalDateTime.ToString("yyyy/MM/dd HH:mm:ss"),
            DisplayTypography.Caption, ThemeResourceBindings.TextMuted));
        if (scene.Configuration is { Count: > 1 } configuration)
            summary.Children.Add(Label(configuration.IncludesAll
                ? $"{UiLocalization.Text("所有结构")} ({configuration.Count})"
                : $"结构：{configuration.Number} / {configuration.Count}",
                DisplayTypography.Caption, ThemeResourceBindings.TextMuted));
        var metrics = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto")
        };
        var values = nonSequential ? new[]
        {
            ("非序列对象", scene.ThreeDimensional!.Surfaces.Count.ToString()),
            ("显示光线", scene.ThreeDimensional.Rays.Count.ToString()),
            ("Z 范围", $"{NumericDisplayFormatter.Format(scene.ThreeDimensional.ZMin)} … {NumericDisplayFormatter.Format(scene.ThreeDimensional.ZMax)} mm")
        } : new[]
        {
            ("有效焦距", MetricRange(scene, item => item.EffectiveFocalLength) + " mm"),
            ("F 数", MetricRange(scene, item => item.FNumber)),
            ("系统总长", MetricRange(scene, item => item.TotalTrack) + " mm")
        };
        for (var row = 0; row < values.Length; row++)
        {
            var name = Label(values[row].Item1, DisplayTypography.CompactBody, ThemeResourceBindings.TextSecondary);
            name.Margin = new Thickness(0, 0, 12, 0);
            var value = Label(values[row].Item2, DisplayTypography.CompactBody, ThemeResourceBindings.TextSecondary);
            Grid.SetRow(name, row);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            metrics.Children.Add(name);
            metrics.Children.Add(value);
        }
        summary.Children.Add(metrics);
        if (scene.Configuration is { IncludesAll: true })
        {
            var legend = ConfigurationLegend(scene);
            legend.IsVisible = colorByConfiguration;
            summary.Children.Add(legend);
        }
        if (nonSequential)
            summary.Children.Add(Label(LayoutStateSummary(scene.NonSequentialLayoutResult).Trim(),
                DisplayTypography.Caption, ThemeResourceBindings.TextMuted));
        var caption = Create(summary, scene.Summary, twoDimensional ? "Viewer2DCompanyLogo" : "Viewer3DCompanyLogo");
        caption.Name = twoDimensional ? "Viewer2DFooter" : "Viewer3DFooter";
        return caption;
    }

    internal static string ConfigurationMetricsSummary(SceneDto scene) =>
        $"{UiLocalization.Text("有效焦距")} {MetricRange(scene, item => item.EffectiveFocalLength)} mm    "
        + $"{UiLocalization.Text("F 数")} {MetricRange(scene, item => item.FNumber)}    "
        + $"{UiLocalization.Text("系统总长")} {MetricRange(scene, item => item.TotalTrack)} mm";

    private static string MetricRange(SceneDto scene, Func<OpticalDocumentSnapshot, double> select)
    {
        var values = (scene.ConfigurationSummaries?.Select(item => item.Summary) ?? new[] { scene.Summary })
            .Select(select).ToArray();
        if (values.Any(value => !double.IsFinite(value))) return "—";
        var min = values.Min(); var max = values.Max();
        return min == max ? NumericDisplayFormatter.Format(min)
            : $"{NumericDisplayFormatter.Format(min)} … {NumericDisplayFormatter.Format(max)}";
    }

    internal static WrapPanel ConfigurationLegend(SceneDto scene)
    {
        var legend = new WrapPanel { Name = "ViewerConfigurationLegend" };
        foreach (var item in scene.ConfigurationSummaries ?? [])
        {
            var entry = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 0, 12, 0) };
            entry.Children.Add(new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(OpticSceneControl.ConfigurationColor(item.Number)),
                VerticalAlignment = VerticalAlignment.Center
            });
            entry.Children.Add(Label($"{UiLocalization.Text("结构")} {item.Number}", DisplayTypography.Caption, ThemeResourceBindings.TextSecondary));
            ToolTip.SetTip(entry, ConfigurationMetricsSummary(new SceneDto(scene.SourceRevision, scene.Dimension, null, null, item.Summary)));
            legend.Children.Add(entry);
        }
        return legend;
    }

    internal static string LayoutStateSummary(NonSequentialLayoutResultDto? result) => result switch
    {
        null or { HasResult: false } => "    尚未准备布局光线",
        { IsStale: true, RaysLoaded: true } => "    警告：正在查看过期结果",
        { IsStale: true } => "    布局结果已过期并隐藏",
        _ => "    布局结果与当前场景一致"
    };

    internal static Border Create(Control summary, OpticalDocumentSnapshot document, string logoName)
    {
        var documentName = string.IsNullOrWhiteSpace(document.Path) ? document.Name : Path.GetFileName(document.Path);
        return Create(summary, documentName, document.Name, logoName);
    }

    internal static Border Create(Control summary, string documentName, string documentTitle, string logoName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?.Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var productDescription = Label($"Optical System Design  {version}", DisplayTypography.Caption,
            ThemeResourceBindings.TextSecondary);
        productDescription.TextAlignment = TextAlignment.Center;
        productDescription.HorizontalAlignment = HorizontalAlignment.Center;
        var product = new StackPanel
        {
            Spacing = 2,
            MinHeight = 64,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new CompanyLogoControl
                {
                    Name = logoName, Width = 180, Height = 28,
                    HorizontalAlignment = HorizontalAlignment.Center, Stretch = Stretch.Uniform
                },
                productDescription
            }
        };
        var documentDescription = Label(documentTitle, DisplayTypography.Caption, ThemeResourceBindings.TextSecondary);
        documentDescription.TextAlignment = TextAlignment.Center;
        var documentNameText = Label(documentName, DisplayTypography.CompactBody, ThemeResourceBindings.TextSecondary,
            FontWeight.Medium);
        documentNameText.TextAlignment = TextAlignment.Center;
        var documentInfo = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(12, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { documentNameText, documentDescription }
        };
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto") };
        right.Children.Add(product);
        var documentBorder = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = documentInfo };
        documentBorder.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        Grid.SetRow(documentBorder, 1);
        right.Children.Add(documentBorder);
        var rightBorder = new Border { BorderThickness = new Thickness(1, 0, 0, 0), Child = right };
        rightBorder.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        var summaryHost = new Grid { Children = { summary, ThemeDrawingSurface.CaptionOrnament(summary) } };
        var grid = new ResponsiveAnalysisFooterGrid(summaryHost, rightBorder) { MinHeight = MinimumHeight };
        var frame = new Border { BorderThickness = new Thickness(0, 1, 0, 1), Child = grid };
        frame.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        frame.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        ThemeDrawingSurface.ApplyCaptionResources(frame);
        return frame;
    }

    private static TextBlock Label(string text, double size, string brush, FontWeight? weight = null)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (weight is { } value) label.FontWeight = value;
        label.BindThemeResource(TextBlock.ForegroundProperty, brush);
        return label;
    }
}
