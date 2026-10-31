using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class RepairGuideAudit
{
    // Diagnostic reconstruction of the first stage in probe 15, before stop-band extrema
    // were anchored. It deliberately preserves that failed model for inspection.
    internal static async Task Run(string inputPath, string resultPath, string outputPath)
    {
        var d = await ExperimentStore.OpenAsync(inputPath); var result = await ExperimentStore.OpenAsync(resultPath);
        var anchor = d with { Layers = result.Result!.Run.TargetRepairStartLayers! };
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var gridMethod = typeof(DesignService).GetMethod("GeneralGrid", flags)!;
        var modelMethod = typeof(DesignService).GetMethod("GeneralConstraintSystem", flags)!;
        var assessMethod = typeof(DesignService).GetMethods(flags).Single(m => m.Name == "AssessGeneral" && m.GetParameters().Length == 2);
        var initial = (double[])typeof(DesignService).GetMethod("ObjectiveGrid", flags)!.Invoke(null, [d])!;
        var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
        CoherentThinFilmSolver Solver(FilmLayer[] layers) => new(materials[d.IncidentId], materials[d.SubstrateId], layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
        double[] Grid(IEnumerable<double> waves) => (double[])gridMethod.Invoke(null, [d, waves])!;
        var low = Math.Max(d.Target.MinimumNm, d.Target.CenterNm - 2 * d.Target.FwhmNm);
        var high = Math.Min(d.Target.MaximumNm, d.Target.CenterNm + 2 * d.Target.FwhmNm);
        var startSamples = new DesignService().Evaluate(anchor).Result!.Spectrum;
        var startPeak = ThinFilmSpectrum.RefineTransmissionPeak(Solver(anchor.Layers), startSamples.Where(s => s.WavelengthNanometers >= low && s.WavelengthNanometers <= high).ToArray(), d.Target.AngleDegrees, d.Target.Polarization);
        var guard = Math.Sqrt(Math.BitIncrement(1.0) - 1);
        double[] Fixed(int n) => Grid(initial.Concat(ThinFilmSpectrum.Grid(low, high, n)).Concat(d.Target.StopBands!.SelectMany(b => ThinFilmSpectrum.Grid(b.MinimumNm, b.MaximumNm, n / 16))));
        double[] AddPeak(double[] grid, double peak) => grid.Min(w => Math.Abs(w - peak)) > d.Target.FwhmNm * guard ? Grid(grid.Append(peak)) : grid;
        (double[] Values, int[] Groups, ThinFilmSample[] Samples) Read(FilmLayer[] layers, double[] grid)
        {
            var s = modelMethod.Invoke(null, [d, Solver(layers), grid, false, null])!; var type = s.GetType();
            return ((double[])type.GetProperty("Values")!.GetValue(s)!, ((Array)type.GetProperty("Keys")!.GetValue(s)!).Cast<object>().Select(k => (int)k.GetType().GetProperty("Group")!.GetValue(k)!).ToArray(), (ThinFilmSample[])type.GetProperty("Samples")!.GetValue(s)!);
        }
        static int Metric(int group) => group switch { 0 => 0, -2 or -3 => 1, -4 or -5 => 2, >= 6 => group - 3, _ => -1 };
        double[] Guide((double[] Values, int[] Groups, ThinFilmSample[] Samples) s)
        {
            var v = ((CoatingConstraintAssessment)assessMethod.Invoke(null, [d, s.Samples])!).Violations.ToArray();
            v[2] = Math.Max(0, s.Values.Where((v, i) => Metric(s.Groups[i]) == 2 && double.IsFinite(v)).DefaultIfEmpty(double.NegativeInfinity).Max() - guard); return v;
        }
        var fine = Read(anchor.Layers, AddPeak(Fixed(1024), startPeak.Peak.WavelengthNanometers));
        var coarse = Read(anchor.Layers, AddPeak(Fixed(512), startPeak.Peak.WavelengthNanometers));
        var v = Guide(fine); var cv = Guide(coarse); var goal = result.Result.Run.TargetRepairGoalIndex!.Value;
        var desired = v.Select((x, i) => i == goal ? 0 : Math.Max(0, x - 2 * Math.Abs(x - cv[i]) - guard)).ToArray();
        var traces = result.Result.Run.TargetRepairTrials!.Select(trial =>
        {
            var samples = Read(trial.Layers, Fixed(1024)).Samples;
            var peak = ThinFilmSpectrum.RefineTransmissionPeak(Solver(trial.Layers), samples.Where(s => s.WavelengthNanometers >= low && s.WavelengthNanometers <= high).ToArray(), d.Target.AngleDegrees, d.Target.Polarization);
            var s = Read(trial.Layers, AddPeak(Fixed(1024), peak.Peak.WavelengthNanometers));
            var rows = s.Values.Select((value, i) => new { Group = s.Groups[i], Value = value, Excess = Metric(s.Groups[i]) is var m && m >= 0 && double.IsFinite(value) ? value + (m switch { 0 => 1e-5 / d.Target.PeakTransmittance, 2 => d.Target.CenterToleranceNm / (10 * d.Target.FwhmNm), _ => 0 }) - desired[m] : (double?)null }).ToArray();
            return new { trial.Step, trial.Retained, trial.GoalIndex, Peak = peak.Peak, Rows = rows };
        }).ToArray();
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new { Scope = "Probe 15 initial stage, before stop-band extrema repair; not the final guide", Fine = v, Coarse = cv, Desired = desired, Traces = traces }, Experiment.JsonOptions));
    }
}
