using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

internal static class CameraPupilQuadrature
{
    internal static IReadOnlyList<PupilSample> Generate(DirectCameraRenderSettings settings)
    {
        if (!Enum.IsDefined(settings.PupilSampling)) throw new ArgumentOutOfRangeException(nameof(settings.PupilSampling));
        if (settings.PupilSampling == CameraPupilSampling.GaussianRings)
            return ApertureSampler.GenerateGaussianQuadrature(settings.PupilRings, settings.PupilArms);

        // Equal-area radial strata, each with an opposite pair. A hard pupil edge no longer
        // accepts/rejects a whole Gaussian ring at once. No throughput renormalization.
        var count = checked(settings.PupilRings * settings.PupilArms);
        var pairs = count / 2;
        if (count < 4 || count % 2 != 0) throw new ArgumentException("Area-stratified pupil sampling requires an even node count.");
        var nodes = new PupilSample[count];
        var angleStep = Math.PI * (3 - Math.Sqrt(5));
        for (var i = 0; i < pairs; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var radius = Math.Sqrt((i + .5) / pairs);
            var angle = i * angleStep;
            var x = radius * Math.Cos(angle); var y = radius * Math.Sin(angle);
            nodes[2 * i] = new(x, y, 1d / count);
            nodes[2 * i + 1] = new(-x, -y, 1d / count);
        }
        return Array.AsReadOnly(nodes);
    }
}
