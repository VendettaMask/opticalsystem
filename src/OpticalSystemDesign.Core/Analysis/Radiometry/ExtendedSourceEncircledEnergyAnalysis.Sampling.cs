using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed partial class ExtendedSourceEncircledEnergyAnalysis
{
    private sealed record SourceCell(double X, double Y, double Width, double Height, double Weight);
    private sealed record SourceTrace(EnergySample[] Samples, int RayCount, int VignettedRayCount, int SourcePointCount);

    private SourceTrace TraceSourceCells(SourceCell[] cells, IReadOnlyList<Wavelength> wavelengths)
    {
        var perCell = Math.Max(1, _numRays / ((long)cells.Length * wavelengths.Count));
        var perWave = checked((int)(cells.Length * perCell));
        if ((long)perWave * wavelengths.Count > AnalysisResourceLimits.MaximumAnalysisRayWork)
            throw new AnalysisDataUnavailableException(Name, "extended-source ray work exceeds the analysis limit");
        var definedCenter = Optic.Fields.ElementAtOrDefault(Math.Min(_fieldNumber - 1, Optic.Fields.Count - 1));
        var centerX = definedCenter?.X ?? 0;
        var centerY = definedCenter?.Y ?? 0;
        var surfaceIndex = Optic.SurfaceGroup.Items.Count - 1;
        if (surfaceIndex < 0) return new SourceTrace([], 0, 0, 0);
        var surface = Optic.SurfaceGroup.Items[surfaceIndex];
        var coordinates = ImageSpaceAnalysisSupport.CoordinateDescriptor(Optic);
        var samples = new List<EnergySample>();
        var rays = new List<RealRay>(4096);
        var weights = new List<double>(4096);
        var rayCount = 0;
        var vignetted = 0;
        foreach (var wavelength in wavelengths)
        {
            var spectralWeight = EnergyCurveSupport.WavelengthWeight(wavelength);
            var sampleIndex = 0;
            foreach (var cell in cells)
            {
                for (var index = 0; index < perCell; index++, sampleIndex++)
                {
                    ComputationCancellation.ThrowIfCancellationRequested();
                    // Pupil coordinates use bases 2/3 in the shared sampler;
                    // independent bases 5/7 cover each source-area stratum.
                    var x = centerX + cell.X + (RadicalInverse(sampleIndex + 1, 5) - 0.5) * cell.Width;
                    var y = centerY + cell.Y + (RadicalInverse(sampleIndex + 1, 7) - 0.5) * cell.Height;
                    var pupil = ApertureSampler.LowDiscrepancyDiskSample(sampleIndex);
                    rayCount++;
                    try
                    {
                        var bundle = Optic.SequentialRayTracer.RayGenerator.GenerateAtPhysicalField(
                            x, y, pupil.X, pupil.Y, wavelength.Micrometers, aimAtStop: Optic.RayAimingEnabled);
                        rays.Add(bundle.Rays[0]);
                        weights.Add(cell.Weight * spectralWeight);
                    }
                    catch (RayAimingException) { vignetted++; }
                    catch (FieldAimingException) { vignetted++; }
                    if (rays.Count == 4096) Flush();
                }
            }
            Flush();
        }
        return new SourceTrace(samples.ToArray(), rayCount, vignetted, perWave);

        void Flush()
        {
            if (rays.Count == 0) return;
            using var trace = Optic.SequentialRayTracer.Trace(new RealRayBundle(rays), TraceRequest.Selected([surfaceIndex]));
            var hits = trace.GetSurfaceSamples(surfaceIndex);
            for (var index = 0; index < rays.Count; index++)
            {
                if (hits[index] is not { Vignetted: false, Intensity: > 0 } hit)
                {
                    vignetted++;
                    continue;
                }
                var point = ImageSpaceAnalysisSupport.ToImageSpaceRayData(Optic, hit.ToRayTraceSample(), surface, coordinates);
                var weight = rays[index].Intensity * weights[index];
                if (double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(weight) && weight > 0)
                    samples.Add(new EnergySample(point.X * coordinates.MetricScale, point.Y * coordinates.MetricScale, weight));
            }
            rays.Clear(); weights.Clear();
        }
    }

    private static double RadicalInverse(int index, int basis)
    {
        var value = 0.0;
        var place = 1.0 / basis;
        while (index > 0)
        {
            value += (index % basis) * place;
            index /= basis;
            place /= basis;
        }
        return value;
    }
}
