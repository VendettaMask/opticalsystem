using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class WavefrontEngine
{
    internal static WavefrontResult GenerateScalarAfocalHuygensSamples(Optic optic,
        (double Hx, double Hy) field, Wavelength wavelength, IReadOnlyList<PupilSample> nodes,
        bool aimAtStop, Wavelength? referenceWavelength) => GenerateChiefRay(optic, field, wavelength,
            nodes.Select(p => new PupilSample(p.X, p.Y, 1)).ToArray(), aimAtStop,
            referenceWavelength: referenceWavelength, allowGeometricAfocalReference: true);

    // Explicit scalar Gaussian-integral path only. Normal/native wavefront APIs
    // retain their protocol. All pupil nodes still use physical TraceFinalSamples.
    internal static WavefrontResult GenerateQuadratureHuygensSamples(Optic optic,
        (double Hx, double Hy) field, Wavelength wavelength, IReadOnlyList<PupilSample> nodes,
        bool aimAtStop, Wavelength? referenceWavelength, bool allowGeometricReference) => GenerateChiefRay(optic, field, wavelength,
            nodes.Select(p => new PupilSample(p.X, p.Y, 1)).ToArray(), aimAtStop,
            referenceWavelength: referenceWavelength, allowGeometricAfocalReference: allowGeometricReference,
            phaseInclusiveAfocalReference: true);
}
