using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Reflection;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.FuiThemePreview;

/// <summary>A preview-only instrument shell around the unchanged product 2-D renderer.</summary>
internal sealed class FuiEngineeringView : Grid, IDisposable
{
    private readonly SurfaceSelectionService _selection;
    private readonly bool _useRegisteredTheme;
    private readonly TextBlock _revision = Label("WAITING FOR CORE", 10, "#778F91");
    private readonly TextBlock _footer = Label("—", 10, "#859A98");
    private readonly CheckBox _rays = Toggle("光线", true);
    private readonly CheckBox _labels = Toggle("标注", true);
    private readonly CheckBox _paper = Toggle("浅底", false);
    internal OpticSceneControl Canvas { get; } = new()
    {
        ViewMode = OpticSceneViewMode.TwoDimensional,
        RayLineWidth = 0.85,
        ShowScaleBar = true,
        Margin = new Thickness(6, 0)
    };
    internal bool ShowAnnotations { get => _labels.IsChecked == true; set => _labels.IsChecked = value; }

    internal FuiEngineeringView(SurfaceSelectionService selection, bool useRegisteredTheme = false)
    {
        _selection = selection;
        _useRegisteredTheme = useRegisteredTheme;
        _selection.Changed += OnSelectionChanged;
        RowDefinitions = new RowDefinitions("44,32,*,28");
        Background = B("#081216");
        ClipToBounds = true;
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 6, 16, 0) };
        heading.Children.Add(new StackPanel
        {
            Spacing = 3,
            Children = { Label("02 / MERIDIONAL SECTION", 11, "#C1C8BA"), Label("光路剖面  /  Z–Y", 10, "#7B9394") }
        });
        _revision.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(_revision, 1); heading.Children.Add(_revision); Children.Add(heading);
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 0) };
        var toggles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { _rays, _labels, _paper } };
        toolbar.Children.Add(toggles);
        var fit = new Button
        {
            Content = "适配视图  ↺",
            FontSize = 11,
            Padding = new Thickness(6, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = B("#ABBAB4")
        };
        ToolTip.SetTip(fit, "复位二维平移和缩放；不修改处方。");
        fit.Click += (_, _) => Canvas.ResetView();
        SetColumn(fit, 1); toolbar.Children.Add(fit); SetRow(toolbar, 1); Children.Add(toolbar);
        var drawing = new Border
        {
            BorderBrush = B("#2D4143"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Background = B("#0D1B20"),
            Child = Canvas
        };
        SetRow(drawing, 2); Children.Add(drawing);
        _footer.Margin = new Thickness(16, 6, 16, 0); SetRow(_footer, 3); Children.Add(_footer);
        _rays.IsCheckedChanged += (_, _) => Canvas.ShowRays = _rays.IsChecked == true;
        _labels.IsCheckedChanged += (_, _) => UpdateAnnotations();
        _paper.IsCheckedChanged += (_, _) => ApplyCanvasPalette();
        ApplyCanvasPalette();
    }

    internal SceneDto? Scene
    {
        get => Canvas.Scene;
        set
        {
            // Reset the viewport when the displayed source revision changes.
            if (Canvas.Scene?.SourceRevision != value?.SourceRevision) Canvas.ResetView();
            Canvas.Scene = value;
            _revision.Text = value is null ? "WAITING FOR CORE" : $"CORE / REV {value.SourceRevision:000000}";
            _footer.Text = value?.TwoDimensional is { } scene
                ? $"{scene.Rays.Count:00} TRACED PATHS   /   {scene.LensElements.Count:00} GLASS ELEMENTS   /   mm     ·     拖动平移 · 滚轮缩放"
                : "当前结果已清空，等待同修订光路。";
            UpdateAnnotations(); Canvas.InvalidateVisual();
        }
    }

    private void ApplyCanvasPalette()
    {
        var paper = _paper.IsChecked == true;
        if (_useRegisteredTheme && !paper)
        {
            foreach (var key in new[] { ThemeResourceBindings.SceneBackground, ThemeResourceBindings.SceneLensFill,
                ThemeResourceBindings.SceneReference, ThemeResourceBindings.SceneAxis, ThemeResourceBindings.PlotText })
                Canvas.Resources.Remove(key);
            UpdateAnnotations(); Canvas.InvalidateVisual();
            return;
        }
        // Local display resources only. Black 2-D outlines and physical ray colours
        // remain owned by OpticSceneControl; no product palette is changed here.
        Canvas.Resources[ThemeResourceBindings.SceneBackground] = B(paper ? "#CAD3CE" : "#0D1B20");
        Canvas.Resources[ThemeResourceBindings.SceneLensFill] = B(paper ? "#A8B9BA" : "#A0B1B0");
        Canvas.Resources[ThemeResourceBindings.SceneReference] = B(paper ? "#53696A" : "#819592");
        Canvas.Resources[ThemeResourceBindings.SceneAxis] = B(paper ? "#6D8181" : "#526C70");
        Canvas.Resources[ThemeResourceBindings.PlotText] = B(paper ? "#405657" : "#95AAA4");
        UpdateAnnotations(); Canvas.InvalidateVisual();
    }

    private void UpdateAnnotations()
    {
        var annotations = new List<OpticSceneAnnotation2D>();
        if (_labels.IsChecked == true && Scene?.TwoDimensional is { } scene)
        {
            var ink = Color.Parse(_paper.IsChecked == true ? "#526A68" : "#84948A");
            foreach (var element in scene.LensElements)
            {
                var front = scene.Surfaces.Single(surface => surface.SurfaceNumber == element.FrontSurfaceNumber);
                var z = front.Points.Count == 0 ? scene.ZMin : front.Points.Average(point => point.Z);
                annotations.Add(new(z, $"S{element.FrontSurfaceNumber:00}—{element.BackSurfaceNumber:00} / {element.Material}", ink,
                    OpticSceneAnnotationPlacement2D.Above));
            }
            var image = scene.Surfaces.LastOrDefault(surface => surface.IsReferencePlane && surface.Points.Count > 0);
            if (image is not null)
                annotations.Add(new(image.Points.Average(point => point.Z), "IMAGE / 像面", ink, OpticSceneAnnotationPlacement2D.Below));
        }
        Canvas.Annotations2D = annotations;
        Canvas.InvalidateVisual();
    }

    internal void ExerciseDisplayControls()
    {
        _rays.IsChecked = false;
        if (Canvas.ShowRays) throw new InvalidOperationException("Ray toggle did not reach the native renderer.");
        _rays.IsChecked = true;
        _labels.IsChecked = false;
        if (Canvas.Annotations2D.Count != 0) throw new InvalidOperationException("Annotation toggle failed.");
        _labels.IsChecked = true;
        _paper.IsChecked = true; _paper.IsChecked = false;
        Canvas.ResetView();
        var outline = typeof(OpticSceneControl).GetField("TwoDLensOutlinePen", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IPen;
        if (outline?.Thickness != 2 || outline.Brush is not ISolidColorBrush outlineBrush || outlineBrush.Color != Colors.Black)
            throw new InvalidOperationException("The native black engineering-outline contract changed.");
        var rayPen = typeof(OpticSceneControl).GetMethod("RayPenFor", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(Canvas, [0, 550d, false, Canvas.RayLineWidth, 1d]) as IPen;
        if (rayPen?.Brush is not ISolidColorBrush rayBrush || rayBrush.Color != Color.FromRgb(220, 55, 48))
            throw new InvalidOperationException("The native first-field ray colour changed.");
    }

    private void OnSelectionChanged(object? sender, SurfaceSelectionChangedEventArgs args) => Canvas.HighlightedSurfaceNumber = args.SurfaceNumber;
    public void Dispose() => _selection.Changed -= OnSelectionChanged;
    private static CheckBox Toggle(string text, bool value)
    {
        var toggle = new CheckBox { Content = text, IsChecked = value, FontSize = 11, MinHeight = 26, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, $"二维图{text}");
        return toggle;
    }
    private static SolidColorBrush B(string color) => new(Color.Parse(color));
    private static TextBlock Label(string text, double size, string color) => new()
    {
        Text = text,
        FontSize = size,
        FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI"),
        Foreground = B(color)
    };
}
