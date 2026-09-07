using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Visualization;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.App;

/// <summary>Maps formal scene DTOs to pixels. No local surface, pupil or optical calculation.</summary>
public sealed class CandidatePreviewControl : Control
{
    private static readonly IBrush PrimaryBrush = new ImmutableSolidColorBrush(Color.Parse("#1574C4"));
    private static readonly IBrush SecondaryBrush = new ImmutableSolidColorBrush(Color.Parse("#CB681C"));
    private static readonly IBrush StopBrush = new ImmutableSolidColorBrush(Color.Parse("#CE9408"));
    private int _generation;
    public Layout2DScene? PrimaryScene { get; private set; }
    public Layout2DScene? SecondaryScene { get; private set; }
    public string? PrimaryFingerprint { get; private set; }
    public string? Error { get; private set; }
    public bool IsLoading { get; private set; }
    public event Action? Changed;
    public Task PendingLoad { get; private set; } = Task.CompletedTask;
    public static LayoutBuildOptions Options { get; } = new(RayCount: 7, LowerPupil: -1, UpperPupil: 1, DeleteVignetted: false);

    public void Clear()
    {
        _generation++;
        PrimaryScene = SecondaryScene = null;
        PrimaryFingerprint = Error = null;
        IsLoading = false;
        InvalidateVisual();
        Changed?.Invoke();
    }

    public Task LoadAsync(CandidateSnapshot? primary, CandidateSnapshot? secondary = null)
    {
        Clear();
        var generation = _generation;
        if (primary is null && secondary is null) return PendingLoad = Task.CompletedTask;
        IsLoading = true;
        Changed?.Invoke();
        return PendingLoad = LoadCoreAsync();
        async Task LoadCoreAsync()
        {
            try
            {
                var scenes = await Task.Run(() => (Build(primary), Build(secondary)));
                if (generation != _generation) return;
                (PrimaryScene, SecondaryScene) = scenes;
                PrimaryFingerprint = primary?.OpticFingerprint;
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or ArithmeticException or KeyNotFoundException)
            {
                if (generation != _generation) return;
                Error = exception.Message;
            }
            finally
            {
                if (generation == _generation)
                {
                    IsLoading = false;
                    InvalidateVisual();
                    Changed?.Invoke();
                }
            }
        }
    }

    private static Layout2DScene? Build(CandidateSnapshot? candidate) => candidate is null ? null
        : new Layout2DBuilder(Optic.FromSnapshot(candidate.Optic)).Build(options: Options);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#F7FAFD")), new Rect(Bounds.Size));
        var bounds = new Rect(Bounds.Size).Deflate(18);
        var scenes = new[] { PrimaryScene, SecondaryScene }.OfType<Layout2DScene>().ToArray();
        if (bounds.Width <= 1 || bounds.Height <= 1 || scenes.Length == 0) return;
        var zMin = scenes.Min(scene => scene.ZMin);
        var zMax = scenes.Max(scene => scene.ZMax);
        var yExtent = Math.Max(.1, scenes.Max(scene => scene.YExtent));
        var scale = Math.Min(bounds.Width / Math.Max(.1, zMax - zMin), bounds.Height / (2 * yExtent));
        Point Map(Layout2DPoint point) => new(bounds.Center.X + (point.Z - (zMin + zMax) / 2) * scale, bounds.Center.Y - point.Y * scale);
        context.DrawLine(new Pen(Brushes.LightSlateGray, 1, DashStyle.Dash), new(bounds.Left, bounds.Center.Y), new(bounds.Right, bounds.Center.Y));
        if (SecondaryScene is { } secondary) DrawScene(secondary, SecondaryBrush, .55);
        if (PrimaryScene is { } primary) DrawScene(primary, PrimaryBrush, 1);

        void DrawScene(Layout2DScene scene, IBrush color, double opacity)
        {
            using var layer = context.PushOpacity(opacity);
            foreach (var element in scene.LensElements)
            {
                if (element.Boundary.Count < 3) continue;
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(Map(element.Boundary[0]), true);
                    foreach (var point in element.Boundary.Skip(1)) path.LineTo(Map(point));
                    path.EndFigure(true);
                }
                using (context.PushOpacity(.10)) context.DrawGeometry(color, null, geometry);
            }
            foreach (var surface in scene.Surfaces)
                for (var index = 1; index < surface.Points.Count; index++)
                    context.DrawLine(new Pen(surface.IsStop ? StopBrush : color, surface.IsStop ? 2.4 : 1.5), Map(surface.Points[index - 1]), Map(surface.Points[index]));
            foreach (var edge in scene.LensEdges) context.DrawLine(new Pen(color, 1.2), Map(edge.Start), Map(edge.End));
            foreach (var ray in scene.Rays)
                foreach (var segment in ray.Segments)
                {
                    var start = Map(segment.Start);
                    var end = Map(segment.End);
                    var pen = new Pen(color, .8);
                    context.DrawLine(pen, start, end);
                    var displacement = new Vector(end.X - start.X, end.Y - start.Y);
                    if (displacement.Length < 35) continue;
                    var vector = new Vector(segment.Direction.Z, -segment.Direction.Y);
                    if (!double.IsFinite(vector.Length) || vector.Length < 1e-12) continue;
                    vector /= vector.Length;
                    var center = start + .55 * displacement;
                    var normal = new Vector(-vector.Y, vector.X);
                    context.DrawLine(pen, center, center - 4 * vector + 2 * normal);
                    context.DrawLine(pen, center, center - 4 * vector - 2 * normal);
                }
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PreviewPeer(this);
    private sealed class PreviewPeer(CandidatePreviewControl owner) : ControlAutomationPeer(owner), IValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
        bool IValueProvider.IsReadOnly => true;
        string IValueProvider.Value => owner.Error is { } error ? $"布局未完成：{error}"
            : $"实际二维布局，A {owner.PrimaryScene?.Rays.Count ?? 0} 条光线，B {owner.SecondaryScene?.Rays.Count ?? 0} 条光线，共用毫米比例";
        void IValueProvider.SetValue(string? value) => throw new InvalidOperationException("The optical preview is read-only.");
    }
}
