using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

internal static class MetricWavelengthSelection
{
    internal static Wavelength[] Select(Optic optic, int wave)
    {
        if (wave < 0 || wave > optic.Wavelengths.Count || optic.Wavelengths.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(wave), "波长编号不存在；0 表示多波长。");
        var selected = wave == 0 ? optic.Wavelengths.ToArray() : [optic.Wavelengths[wave - 1]];
        if (wave == 0 && selected.Any(w => !double.IsFinite(w.Weight) || w.Weight < 0))
            throw new InvalidOperationException("多波长权重必须为有限非负值。");
        var weightScale = wave == 0 ? selected.Max(w => w.Weight) : 1;
        if (weightScale <= 0) throw new InvalidOperationException("没有正权重波长。");
        // Normalize copies: do not mutate the optic, silently equalize zero weights,
        // or let a positive single-wavelength selection inherit a zero spectral weight.
        var wavelengths = selected.Where(w => wave > 0 || w.Weight > 0).Select(w => new Wavelength
        {
            Nanometers = w.Nanometers,
            Weight = wave == 0 ? w.Weight / weightScale : 1,
            IsPrimary = w.IsPrimary,
            Label = w.Label
        }).ToArray();
        if (wavelengths.Any(w => !double.IsFinite(w.Nanometers) || w.Nanometers <= 0))
            throw new InvalidOperationException("波长必须为有限正数。");
        return wavelengths;
    }
}
