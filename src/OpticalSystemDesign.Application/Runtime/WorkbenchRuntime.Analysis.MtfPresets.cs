using OpticalSystemDesign.Core.Analysis;

namespace OpticalSystemDesign.Application.Runtime;

public partial class WorkbenchRuntime
{
    // Product presets follow the physical angular band. Explicit request values
    // keep their declared cycles/mrad; this never clips or rescales user data.
    private double MtfScanFrequencyPreset(double focalDefault, double cutoffFraction = 0.1)
    {
        if (!CurrentOptic.ImageSpaceAfocal) return focalDefault;
        var wavelength = CurrentOptic.Wavelengths.FirstOrDefault(w => w.IsPrimary)
            ?? CurrentOptic.Wavelengths.FirstOrDefault();
        if (wavelength is null || CurrentOptic.SurfaceGroup.Items.Count < 2) return 0;
        try
        {
            var cutoff = MtfFrequencyScale.AfocalCutoffCyclesPerMilliradian(CurrentOptic, wavelength);
            return double.IsFinite(cutoff) && cutoff > 0 ? cutoff * cutoffFraction : 0;
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or ArgumentException or ArithmeticException)
        {
            // A settings panel must remain reachable for an incomplete or
            // unsupported prescription. Zero marks an unresolved preset;
            // executing the analysis retains its normal applicability checks.
            return 0;
        }
    }

    private double MtfFieldFrequencyPreset(int slot)
    {
        ReadOnlySpan<double> fractions = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5];
        return MtfScanFrequencyPreset(slot * 10, fractions[Math.Clamp(slot - 1, 0, 5)]);
    }
}
