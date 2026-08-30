using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

public sealed class SolveManager
{
    private Optic _optic;

    public SolveManager(Optic optic)
    {
        _optic = optic;
    }

    public double DesiredBackFocus { get; set; } = 30.0;

    public bool KeepImageAtBackFocus { get; set; } = true;

    internal void Rebind(Optic optic)
    {
        _optic = optic;
    }

    public void ApplyAll()
    {
        if (!KeepImageAtBackFocus || _optic.SurfaceGroup.Items.Count < 2)
        {
            return;
        }

        var image = _optic.SurfaceGroup.Items[^1];
        var poweredTrack = _optic.SurfaceGroup.Items.Take(_optic.SurfaceGroup.Items.Count - 1)
            .Where((surface, index) => index != 0 || !ObjectConjugate.IsInfinite(surface))
            .Sum(surface => surface.Thickness);
        image.Thickness = Math.Max(0, DesiredBackFocus - poweredTrack);
    }
}
