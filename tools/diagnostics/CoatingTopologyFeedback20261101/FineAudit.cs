using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class FineAudit
{
    public static async Task Main(string[] args)
    {
        var rows=new List<object>();var service=new DesignService();
        foreach(var file in Directory.GetFiles(args[0],"*.coating.json").Order(StringComparer.Ordinal))
        {
            var d=await ExperimentStore.OpenAsync(file);var mats=d.Materials.ToDictionary(m=>m.Id,m=>m.Resolve(new MaterialRegistry()));
            var solver=new CoherentThinFilmSolver(mats[d.IncidentId],mats[d.SubstrateId],d.Layers.Select(l=>new CoherentFilm(mats[l.MaterialId],l.ThicknessNm)));
            foreach(var intervals in new[]{400,1600,3200})
            {
                var value=service.Evaluate(d with{Settings=d.Settings with{PreviewIntervals=intervals},Result=null,Candidates=null,Tolerance=null});
                rows.Add(new{File=Path.GetFileName(file),Scope="Same original gates; additional sampling validation only",PreviewIntervals=intervals,value.Result!.Metrics,value.Result.Checks,value.Result.Passed,value.Result.SamplingConverged,Od=d.Target.StopBands!.Select(b=>value.Result.Spectrum.Where(s=>s.WavelengthNanometers>=b.MinimumNm&&s.WavelengthNanometers<=b.MaximumNm).Min(s=>s.Power.OpticalDensity)).ToArray()});
            }
            foreach(var intervals in new[]{8192,32768})
            {
                var waves=ThinFilmSpectrum.Grid(d.Target.MinimumNm,d.Target.MaximumNm,intervals).Concat(d.Target.StopBands!.SelectMany(b=>new[]{b.MinimumNm,b.MaximumNm}))
                    .Concat(new[]{d.Target.CenterNm-2*d.Target.FwhmNm,d.Target.CenterNm+2*d.Target.FwhmNm}).Distinct().Order().ToArray();
                var samples=ThinFilmSpectrum.Sample(solver,waves,d.Target.AngleDegrees,d.Target.Polarization);
                var band=samples.Where(s=>Math.Abs(s.WavelengthNanometers-d.Target.CenterNm)<=2*d.Target.FwhmNm).ToArray();
                var peak=ThinFilmSpectrum.RefineTransmissionPeak(solver,band,d.Target.AngleDegrees,d.Target.Polarization);
                var metrics=ThinFilmSpectrum.Measure(band.Append(peak.Peak).OrderBy(s=>s.WavelengthNanometers).ToArray());
                var od=d.Target.StopBands!.Select(b=>ThinFilmSpectrum.RefineTransmissionPeak(solver,samples.Where(s=>s.WavelengthNanometers>=b.MinimumNm&&s.WavelengthNanometers<=b.MaximumNm).ToArray(),d.Target.AngleDegrees,d.Target.Polarization).Peak.Power.OpticalDensity).ToArray();
                rows.Add(new{File=Path.GetFileName(file),Scope="Independent uniform Core grid with locally refined physical extrema; not a replacement acceptance protocol",Intervals=intervals,Metrics=metrics,RefinedPeak=peak.Peak,Od=od});
            }
        }
        await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(rows,Experiment.JsonOptions));Console.WriteLine($"Saved {rows.Count} additional sampling checks.");
    }
}
