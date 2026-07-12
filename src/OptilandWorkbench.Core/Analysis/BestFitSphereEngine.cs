using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed record BestFitSphereResult(
    double CenterX,
    double CenterY,
    double CenterZ,
    double Radius,
    int ValidRayCount);

public static class BestFitSphereEngine
{
    public static BestFitSphereResult Calculate(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int numRings = 15)
    {
        var wavefront = ReferenceSphereWavefrontEngine.Generate(
            optic,
            field,
            wavelength,
            numRings,
            ReferenceSphereStrategy.BestFitSphere);
        return new BestFitSphereResult(
            wavefront.CenterX,
            wavefront.CenterY,
            wavefront.CenterZ,
            wavefront.Radius,
            wavefront.Samples.Count(sample => sample.Intensity > 0));
    }
}
