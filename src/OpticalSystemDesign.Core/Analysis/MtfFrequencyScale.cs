using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Core.Analysis;

/// <summary>Physical angular diffraction scale shared by analysis clients.</summary>
public static class MtfFrequencyScale
{
    public static double AfocalCutoffCyclesPerMilliradian(Optic optic, Wavelength wavelength)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(wavelength);
        if (!optic.ImageSpaceAfocal)
            throw new ArgumentException("An angular diffraction cutoff requires afocal image space.", nameof(optic));
        return ImageSpaceAnalysisSupport.AfocalCutoffFrequencyCyclesPerMilliradian(optic, wavelength);
    }
}
