using System.Numerics;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

public static class AxiconProbe
{
    public static void Run(string snapshot, string output, string[] levels)
    {
        const int imageSize = 32;
        const double pitch = .00025;
        var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(snapshot));
        var wavelength = optic.Wavelengths.First();
        var lambda = wavelength.Micrometers * .001;
        var distance = Math.Abs(optic.Paraxial.EstimateExitPupilLocation());
        var diameter = Math.Abs(optic.Paraxial.EstimateExitPupilDiameter());
        var window = imageSize / 2 * pitch;
        var curvature = window * window / (Math.Sqrt(distance * distance + window * window) + distance);
        var imageIndex = optic.SurfaceGroup.Items[^1].MaterialAfter.RefractiveIndex(wavelength.Nanometers);
        if (optic.ImageSpaceAfocal || curvature * imageIndex > lambda / 4)
            throw new InvalidOperationException("Expected the scalar planar branch");
        var frame = DiffractionEngine.CreateHuygensImageFrame(optic, (0, 0), wavelength, optic.RayAimingEnabled);
        var x = Enumerable.Range(0, imageSize).Select(i => (i - imageSize / 2) * pitch).ToArray();
        var k = 2 * Math.PI / lambda;
        var points = x.Select(value => frame.Center + frame.TangentX * value).ToArray();
        foreach (var token in levels)
        {
            var n = int.Parse(token);
            var sum = new Complex[imageSize];
            var compensation = new Complex[imageSize];
            var ideal = 0.0;
            var count = 0;
            var transmitted = 0;
            var radius = n / 2d - 1;
            var limit = 1 - .5 / radius;
            double? wavefrontRadius = null;
            var samples = new List<(double X, double Y)>(16384);
            void AddBatch()
            {
                var wavefront = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), wavelength, samples,
                    aimAtStop: optic.RayAimingEnabled, allowGeometricReference: true, useSystemFocalReference: false);
                if (wavefront.ImageRefractiveIndex != imageIndex || (wavefrontRadius.HasValue && wavefrontRadius != wavefront.Radius))
                    throw new InvalidOperationException("Inconsistent wavefront datum");
                wavefrontRadius = wavefront.Radius;
                count += wavefront.Samples.Count;
                foreach (var sample in wavefront.Samples)
                {
                    var power = sample.LaunchIntensity ?? sample.Intensity;
                    if (sample.Intensity <= 0 || power <= 0 || !double.IsFinite(power)) continue;
                    var amplitude = Math.Sqrt(power);
                    ideal += amplitude;
                    if (sample.ImageDirection is not { Length: > 0 } direction) continue;
                    transmitted++;
                    var pupil = new Vector3D(sample.PupilX, sample.PupilY, sample.PupilZ);
                    for (var i = 0; i < imageSize; ++i)
                    {
                        var delta = points[i] - pupil;
                        var dot = delta.X * direction.X + delta.Y * direction.Y + delta.Z * direction.Z;
                        var phasePath = -sample.OpdWaves * lambda + imageIndex * (dot - wavefront.Radius);
                        var add = Complex.FromPolarCoordinates(amplitude, k * phasePath) - compensation[i];
                        var next = sum[i] + add;
                        compensation[i] = (next - sum[i]) - add;
                        sum[i] = next;
                    }
                }
                samples.Clear();
            }
            for (var row = 0; row < n; row++)
                for (var column = 0; column < n; column++)
                {
                    var px = (column - n / 2d) / radius;
                    var py = (row - n / 2d) / radius;
                    if (px * px + py * py > limit * limit) continue;
                    samples.Add((px, py));
                    if (samples.Count == samples.Capacity) AddBatch();
                }
            if (samples.Count != 0) AddBatch();
            var halfExtent = .61 * lambda * distance * (n - 2) / (imageIndex * diameter);
            double Sinc(double arg) => Math.Abs(arg) < 1e-8 ? 1 - arg * arg / 6 : Math.Sin(arg) / arg;
            var y = x.Select((value, i) => Math.Abs(value) >= halfExtent ? 0
                : Math.Pow(sum[i].Magnitude * Sinc(Math.PI * value / halfExtent) / ideal, 2)).ToArray();
            Console.WriteLine($"Axicon {n}: transmitted {transmitted} / {count} pupil nodes, peak {y.Max():R}, center {y[imageSize / 2]:R}");
            JsonFiles.Write(Path.Combine(output, $"axicon-line-{n}.json"), new
            {
                n, count, transmitted, ideal, wavefrontRadius, halfExtent, lambda, distance, diameter, curvature,
                imageIndex, x = x.Select(value => value * 1000), y
            });
        }
    }
}
