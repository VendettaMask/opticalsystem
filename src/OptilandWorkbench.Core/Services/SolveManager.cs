namespace OptilandWorkbench.Core.Services;

public sealed class SolveManager
{
    private readonly Optic _optic;

    public SolveManager(Optic optic)
    {
        _optic = optic;
    }

    public double DesiredBackFocus { get; set; } = 30.0;

    public bool KeepImageAtBackFocus { get; set; } = true;

    public void ApplyAll()
    {
        if (!KeepImageAtBackFocus || _optic.SurfaceGroup.Items.Count < 2)
        {
            return;
        }

        var image = _optic.SurfaceGroup.Items[^1];
        if (!image.Label.Contains("Image", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var poweredTrack = _optic.SurfaceGroup.Items.Take(_optic.SurfaceGroup.Items.Count - 1)
            .Sum(surface => surface.Thickness);
        image.Thickness = Math.Max(0, DesiredBackFocus - poweredTrack);
    }
}
