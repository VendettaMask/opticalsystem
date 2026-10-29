using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Modules;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

/// <summary>Read-only projection of the shared-Core scene. Navigation performs no optical work.</summary>
internal sealed class TopologyStructurePlot : Control
{
    internal static readonly IBrush[] FieldBrushes = [Brushes.RoyalBlue, Brushes.ForestGreen, Brushes.Red, Brushes.DarkViolet, Brushes.DarkOrange, Brushes.Teal];
    internal TopologyStructureScene? Data { get; private set; }
    internal double Zoom { get; private set; } = 1;
    internal Vector Pan { get; private set; }
    private Point? _drag;
    internal event Action? NavigationChanged;

    public TopologyStructurePlot()
    {
        Focusable = true; ClipToBounds = true; MinHeight = 320;
        AutomationProperties.SetName(this, UiLocalization.Text("镜头结构"));
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }
    internal void SetScene(TopologyStructureScene? data) { Data = data; Fit(); }
    internal void Fit() { Zoom = 1; Pan = default; InvalidateVisual(); NavigationChanged?.Invoke(); }
    internal void ZoomBy(double ratio, Point? anchor = null)
    {
        if (Data is null) return;
        var next = Math.Clamp(Zoom * ratio, .25, 32); ratio = next / Zoom;
        var point = anchor ?? PlotBounds.Center; var relative = point - PlotBounds.Center;
        Pan = relative - (relative - Pan) * ratio; Zoom = next;
        InvalidateVisual(); NavigationChanged?.Invoke();
    }
    private Rect PlotBounds => new(56, 24, Math.Max(1, Bounds.Width - 72), Math.Max(1, Bounds.Height - 68));
    internal void PanBy(Vector delta) { if (Data is null) return; Pan += delta; InvalidateVisual(); }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (Data is not null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        { Focus(); _drag = e.GetPosition(this); e.Pointer.Capture(this); e.Handled = true; }
        base.OnPointerPressed(e);
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag is { } before) { var point = e.GetPosition(this); PanBy(point - before); _drag = point; e.Handled = true; }
        base.OnPointerMoved(e);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { _drag = null; e.Pointer.Capture(null); base.OnPointerReleased(e); }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { _drag = null; base.OnPointerCaptureLost(e); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    { if (Data is not null) { ZoomBy(Math.Pow(1.15, Math.Clamp(e.Delta.Y, -8, 8)), e.GetPosition(this)); e.Handled = true; } base.OnPointerWheelChanged(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Add or Key.OemPlus) { ZoomBy(1.25); e.Handled = true; }
        else if (e.Key is Key.Subtract or Key.OemMinus) { ZoomBy(.8); e.Handled = true; }
        else if (e.Key == Key.Home) { Fit(); e.Handled = true; }
        base.OnKeyDown(e);
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context); var background = ThemeBrush(ThemeResourceBindings.PlotBackground); var ink = ThemeBrush(ThemeResourceBindings.PlotAxis);
        context.FillRectangle(background, new Rect(Bounds.Size));
        if (Data is not { } data || Bounds.Width < 100 || Bounds.Height < 100) return;
        var scene = data.Scene; var plot = PlotBounds;
        var middle = (scene.ZMin + scene.ZMax) / 2;
        var scale = .92 * Zoom * Math.Min(plot.Width / Math.Max(.1, scene.ZMax - scene.ZMin), plot.Height / (2 * Math.Max(.1, scene.YExtent)));
        Point Map(ScenePoint2Dto point) => plot.Center + Pan + new Vector((point.Z - middle) * scale, -point.Y * scale);
        using (context.PushClip(plot))
        {
            var axisY = Map(new(middle, 0)).Y;
            context.DrawLine(new Pen(ink, .7, DashStyle.Dash), new(plot.Left, axisY), new(plot.Right, axisY));
            foreach (var element in scene.LensElements)
            {
                if (element.Boundary.Count < 3) continue;
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(Map(element.Boundary[0]), true);
                    foreach (var p in element.Boundary.Skip(1)) path.LineTo(Map(p));
                    path.EndFigure(true);
                }
                using (context.PushOpacity(.1)) context.DrawGeometry(FieldBrushes[0], null, geometry);
            }
            foreach (var ray in scene.Rays)
            {
                var pen = new Pen(FieldBrushes[Math.Abs(ray.FieldIndex) % FieldBrushes.Length], 1, ray.Vignetted ? DashStyle.Dash : null);
                using var opacity = context.PushOpacity(ray.Vignetted ? .6 : .9);
                foreach (var segment in ray.Segments) context.DrawLine(pen, Map(segment.Start), Map(segment.End));
            }
            foreach (var surface in scene.Surfaces.Where(s => s.SurfaceNumber > 0))
            {
                if (surface.SurfaceNumber == data.ImageSurfaceNumber && surface.Points.Count > 0)
                {
                    var z = surface.Points[0].Z;
                    context.DrawLine(new Pen(ink, 1.5), Map(new(z, -scene.YExtent)), Map(new(z, scene.YExtent)));
                    continue;
                }
                var pen = new Pen(surface.IsStop ? Brushes.DarkGoldenrod : ink, surface.IsStop ? 2 : 1.4,
                    surface.SurfaceNumber == data.ReferenceSurfaceNumber ? DashStyle.Dash : null);
                for (var i = 1; i < surface.Points.Count; i++) context.DrawLine(pen, Map(surface.Points[i - 1]), Map(surface.Points[i]));
            }
            foreach (var edge in scene.LensEdges) context.DrawLine(new Pen(ink, 1.2), Map(edge.Start), Map(edge.End));
        }
        context.DrawRectangle(null, new Pen(ink, .8), plot);
        for (var i = 0; i <= 4; i++)
        {
            var x = plot.Left + i * plot.Width / 4; var y = plot.Top + i * plot.Height / 4;
            var zValue = middle + (x - plot.Center.X - Pan.X) / scale; var yValue = -(y - plot.Center.Y - Pan.Y) / scale;
            if (Math.Abs(zValue) < 1e-12) zValue = 0;
            if (Math.Abs(yValue) < 1e-12) yValue = 0;
            var xText = Text(zValue.ToString("G4", CultureInfo.CurrentCulture), ink);
            var yText = Text(yValue.ToString("G4", CultureInfo.CurrentCulture), ink);
            context.DrawText(xText, new(x - xText.Width / 2, plot.Bottom + 5));
            context.DrawText(yText, new(plot.Left - yText.Width - 7, y - yText.Height / 2));
        }
        var caption = Text("Z (mm)", ink); context.DrawText(caption, new(plot.Center.X - caption.Width / 2, Bounds.Height - caption.Height - 2));
        context.DrawText(Text("Y (mm)", ink), new(3, 3));
    }
    private static FormattedText Text(string text, IBrush brush) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        DisplayTypography.Typeface(), DisplayTypography.Scale(11), brush);
    private IBrush ThemeBrush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush
        : new SolidColorBrush(key == ThemeResourceBindings.PlotBackground ? ThemePalette.Light.PlotBackground : ThemePalette.Light.PlotAxis);
}
