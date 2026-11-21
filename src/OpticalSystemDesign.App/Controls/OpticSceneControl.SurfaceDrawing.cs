using System.Globalization;
using Avalonia;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Controls;

public sealed partial class OpticSceneControl
{
    private static double DisplayOpacity(double opacity) =>
        double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 1;

    private void DrawLocalFrames2D(DrawingContext context,
        IReadOnlyList<SceneSurface2Dto> surfaces, Func<double, double> mapZ, Func<double, double> mapY)
    {
        Point Project(ScenePoint2Dto point) => new(mapZ(point.Z), mapY(point.Y));
        foreach (var surface in surfaces)
        {
            if (surface.LocalFrame is not { } frame) continue;
            DrawLocalFrame(context, surface.SurfaceNumber, Project(frame.Origin),
                Project(frame.XAxisEnd), Project(frame.YAxisEnd), Project(frame.ZAxisEnd),
                frame.DrawAxes, frame.DrawOrigin);
        }
    }

    private void DrawLocalFrames3D(DrawingContext context,
        IReadOnlyList<SceneSurface3Dto> surfaces, Func<ScenePoint3Dto, Point> project)
    {
        foreach (var surface in surfaces)
        {
            if (surface.LocalFrame is not { } frame) continue;
            DrawLocalFrame(context, surface.SurfaceNumber, project(frame.Origin),
                project(frame.XAxisEnd), project(frame.YAxisEnd), project(frame.ZAxisEnd),
                frame.DrawAxes, frame.DrawOrigin);
        }
    }

    private void DrawLocalFrame(DrawingContext context, int number, Point origin,
        Point xEnd, Point yEnd, Point zEnd, bool drawAxes, bool drawOrigin)
    {
        if (!Finite(origin)) return;
        if (drawAxes)
        {
            // The same engineering X/Y/Z convention as the existing orientation
            // triad. Endpoints are provided by the actual surface transform.
            DrawLocalAxis(context, origin, xEnd, $"X{number}", Color.FromRgb(210, 60, 52));
            DrawLocalAxis(context, origin, yEnd, $"Y{number}", Color.FromRgb(43, 154, 82));
            DrawLocalAxis(context, origin, zEnd, $"Z{number}", Color.FromRgb(38, 102, 196));
        }
        if (drawOrigin)
        {
            var solidBackground = VisualStyle == OpticSceneVisualStyle.SolidModel
                && ThemeDrawingSurface.Material(this) is null;
            var foreground = solidBackground ? SolidReferencePlanePen.Brush!
                : ThemeBrush(ThemeResourceBindings.SceneTarget, TargetPen.Brush!);
            var background = solidBackground ? SolidModelBackgroundBrush
                : ThemeBrush(ThemeResourceBindings.SceneBackground, Brushes.White);
            var pen = new Pen(foreground, 1.2);
            context.DrawEllipse(background, pen, origin, 3.5, 3.5);
            context.DrawLine(pen, origin + new Vector(-6, 0), origin + new Vector(6, 0));
            context.DrawLine(pen, origin + new Vector(0, -6), origin + new Vector(0, 6));
            DrawFrameLabel(context, $"O{number}", foreground, origin + new Vector(7, 4));
        }
    }

    private static void DrawLocalAxis(DrawingContext context, Point origin, Point end, string label, Color color)
    {
        if (!Finite(end)) return;
        var brush = new SolidColorBrush(color);
        var direction = new Vector(end.X - origin.X, end.Y - origin.Y);
        var length = direction.Length;
        if (length > 0.5)
        {
            var pen = new Pen(brush, 1.25);
            context.DrawLine(pen, origin, end);
            var unit = direction / length;
            var normal = new Vector(-unit.Y, unit.X);
            var arrowLength = Math.Min(6, length * 0.25);
            context.DrawLine(pen, end, end - unit * arrowLength + normal * arrowLength * 0.45);
            context.DrawLine(pen, end, end - unit * arrowLength - normal * arrowLength * 0.45);
            DrawFrameLabel(context, label, brush, end + unit * 4);
        }
        else
        {
            // A local axis normal to the viewing plane projects to a point.
            context.DrawEllipse(brush, null, origin, 1.8, 1.8);
            DrawFrameLabel(context, label, brush, origin + new Vector(-18, -18));
        }
    }

    private static void DrawFrameLabel(DrawingContext context, string label, IBrush brush, Point position)
    {
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            DisplayTypography.Typeface(FontWeight.SemiBold), DisplayTypography.CompactBody, brush);
        context.DrawText(text, position);
    }

    private static bool Finite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
}
