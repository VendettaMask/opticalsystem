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
        image.Thickness = CalculateImageThickness(_optic.SurfaceGroup.Items.Select(surface => surface.Thickness).ToArray());
    }

    public double CalculateImageThickness(IReadOnlyList<double> thicknesses)
    {
        if (thicknesses.Count < 2) throw new ArgumentException("后焦距求解至少需要两个表面。", nameof(thicknesses));
        var poweredTrack = thicknesses.Take(thicknesses.Count - 1)
            .Where((thickness, index) => index != 0 || !double.IsPositiveInfinity(thickness)).Sum();
        return Math.Max(0, DesiredBackFocus - poweredTrack);
    }
}
