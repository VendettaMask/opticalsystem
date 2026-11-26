using Avalonia;
using Layout3DScene = OpticalSystemDesign.Application.Contracts.Scene3Dto;
using Layout3DPoint = OpticalSystemDesign.Application.Contracts.ScenePoint3Dto;

namespace OpticalSystemDesign.App.Controls;

public sealed partial class OpticSceneControl
{
    private Layout3DScene? _sequentialFitScene;
    private Size _sequentialFitSize;
    private double _sequentialFitStretch;
    private SequentialProjectionFit? _sequentialFit;

    internal sealed record SequentialProjectionFit(Layout3DPoint Center, double Scale);

    // Fit the displayed geometry and complete ray paths once, then keep that
    // camera pivot and scale while orbiting. Presets and explicit fit invalidate it.
    internal SequentialProjectionFit? SequentialFit(Layout3DScene scene, double width, double height)
    {
        var size = new Size(width, height);
        if (_sequentialFitSize == size && _sequentialFitStretch == VerticalStretch
            && (ReferenceEquals(_sequentialFitScene, scene) || scene.FitBounds is not null && _sequentialFitScene?.FitBounds == scene.FitBounds))
        {
            _sequentialFitScene = scene;
            return _sequentialFit;
        }
        var points = scene.FitBounds is { } bounds ? Enumerable.Range(0, 8).Select(i => new Layout3DPoint(
            (i & 1) == 0 ? bounds.Minimum.X : bounds.Maximum.X,
            (i & 2) == 0 ? bounds.Minimum.Y : bounds.Maximum.Y,
            (i & 4) == 0 ? bounds.Minimum.Z : bounds.Maximum.Z)).ToArray() : scene.Surfaces
            .SelectMany(s => s.Rim.Concat(s.MeridianX).Concat(s.MeridianY).Concat(s.Faces.SelectMany(f => f.Points)))
            .Concat(scene.Surfaces.Where(s => s.LocalFrame is not null).SelectMany(s => s.LocalFrame!.DrawAxes
                ? new[] { s.LocalFrame.Origin, s.LocalFrame.XAxisEnd, s.LocalFrame.YAxisEnd, s.LocalFrame.ZAxisEnd }
                : new[] { s.LocalFrame.Origin }))
            .Concat(scene.LensElements.SelectMany(e =>
                e.FrontRim.Concat(e.BackRim).Concat(e.FrontFaces.Concat(e.BackFaces).SelectMany(f => f.Points))))
            .Concat(scene.Rays.SelectMany(r => r.Segments.SelectMany(s => new[] { s.Start, s.End })))
            .Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z)).ToArray();
        if (points.Length == 0) return null;
        var anchor = new Layout3DPoint((points.Min(p => p.X) + points.Max(p => p.X)) / 2,
            (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2,
            (points.Min(p => p.Z) + points.Max(p => p.Z)) / 2);
        var projected = points.Select(p => ProjectDirectionToView(new Layout3DPoint(p.X - anchor.X,
            (p.Y - anchor.Y) * VerticalStretch, p.Z - anchor.Z))).ToArray();
        var left = projected.Min(p => p.X); var right = projected.Max(p => p.X);
        var top = projected.Min(p => p.Y); var bottom = projected.Max(p => p.Y);
        var offsetX = (left + right) / 2; var offsetY = (top + bottom) / 2;
        var center = new Layout3DPoint(
            anchor.X + offsetX * Math.Sin(_yaw) - offsetY * Math.Cos(_yaw) * Math.Sin(_pitch),
            anchor.Y + offsetY * Math.Cos(_pitch) / VerticalStretch,
            anchor.Z + offsetX * Math.Cos(_yaw) + offsetY * Math.Sin(_yaw) * Math.Sin(_pitch));
        _sequentialFit = new(center, 0.9 * Math.Min(width / Math.Max(1, right - left), height / Math.Max(1, bottom - top)));
        _sequentialFitScene = scene; _sequentialFitSize = size; _sequentialFitStretch = VerticalStretch;
        return _sequentialFit;
    }
}
