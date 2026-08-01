namespace OptilandWorkbench.Core.Domain;

/// <summary>
/// Defines the persisted object-conjugate convention used by sequential optics.
/// </summary>
public static class ObjectConjugate
{
    /// <summary>
    /// Returns true only when the Object-surface thickness explicitly stores positive infinity.
    /// A zero thickness is a finite object located at the first physical surface.
    /// </summary>
    public static bool IsInfinite(OpticalSurface? objectSurface)
    {
        return objectSurface is not null
            && double.IsPositiveInfinity(objectSurface.Thickness);
    }
}
