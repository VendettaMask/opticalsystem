using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class AllocationAudit
{
    internal static async Task ExportPhysics(string directory, string output)
    {
        var cases = new List<object>();
        foreach (var path in Directory.GetFiles(directory, "*.coating.json").Order(StringComparer.Ordinal))
        {
            var d = await ExperimentStore.OpenAsync(path); var r = d.Result!;
            var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
            var solver = new CoherentThinFilmSolver(materials[d.IncidentId], materials[d.SubstrateId], d.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
            var waves = ThinFilmSpectrum.Grid(d.Target.MinimumNm, d.Target.MaximumNm, 8)
                .Concat(new double?[] { r.Metrics.PeakWavelengthNanometers, r.Metrics.LeftHalfMaximumNanometers, r.Metrics.RightHalfMaximumNanometers }
                    .Where(w => w.HasValue).Select(w => w!.Value))
                .Concat((d.Target.StopBands ?? []).Select(b => r.Spectrum.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm)
                    .MaxBy(s => s.Power.LogTransmittance)!.WavelengthNanometers))
                .Append(r.Spectrum.MinBy(s => s.Power.Reflectance)!.WavelengthNanometers)
                .Append(r.Spectrum.MaxBy(s => s.Power.Reflectance)!.WavelengthNanometers);
            if (d.Target.Kind == DesignKind.NarrowBand) waves = waves
                .Append(Math.Max(d.Target.MinimumNm, d.Target.CenterNm - 2 * d.Target.FwhmNm))
                .Append(Math.Min(d.Target.MaximumNm, d.Target.CenterNm + 2 * d.Target.FwhmNm));
            foreach (var wave in waves.Distinct().Order()) foreach (var pol in Enum.GetValues<ThinFilmPolarization>())
            {
                double[] Index(string id) => [materials[id].RefractiveIndex(wave), materials[id].ExtinctionCoefficient(wave)];
                cases.Add(new { d.Name, Lambda = wave, Angle = d.Target.AngleDegrees, Pol = pol.ToString(), N0 = Index(d.IncidentId), Ns = Index(d.SubstrateId),
                    Layers = d.Layers.Select(l => new { N = Index(l.MaterialId), D = l.ThicknessNm }), Core = solver.Evaluate(wave, d.Target.AngleDegrees, pol) });
            }
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(cases, Experiment.JsonOptions));
        Console.WriteLine($"Exported {cases.Count} exact-index physics points, including the metric-domain endpoints.");
    }

    internal static async Task Recheck(string inputs, string results, string output)
    {
        Directory.CreateDirectory(output); var service = new DesignService(); var summary = new List<object>();
        foreach (var path in Directory.GetFiles(inputs, "*.coating.json").Order(StringComparer.Ordinal))
        {
            var input = await ExperimentStore.OpenAsync(path); var original = await ExperimentStore.OpenAsync(Path.Combine(results, Path.GetFileName(path)));
            var before = service.Evaluate(input); var watch = Stopwatch.StartNew();
            var after = service.Evaluate(original, original.Result!.Run); watch.Stop();
            await ExperimentStore.SaveAsync(after, Path.Combine(output, Path.GetFileName(path)));
            double[] Od(Experiment d) => (d.Target.StopBands ?? []).Select(b => d.Result!.Spectrum
                .Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity)).ToArray();
            double Loss(Experiment d) => d.Target.Kind == DesignKind.Antireflection ? d.Result!.Metrics.MaximumReflectance
                : d.Target.Kind == DesignKind.HighReflector ? 1 - d.Result!.Metrics.MinimumReflectance : d.Result!.Merit;
            summary.Add(new { input.Name, InputFingerprint = input.Fingerprint(), input.Target, input.Settings,
                BeforeLoss = Loss(before), AfterLoss = Loss(after), BeforePassed = before.Result!.Passed, AfterPassed = after.Result!.Passed,
                BeforeMetrics = before.Result.Metrics, AfterMetrics = after.Result.Metrics, BeforeBlockingOd = Od(before), AfterBlockingOd = Od(after),
                after.Result.Checks, after.Result.Run, Seconds = watch.Elapsed.TotalSeconds,
                OriginalReportedPassed = original.Result!.Passed, OriginalReportedMetrics = original.Result.Metrics,
                OriginalReportedMerit = original.Result.Merit,
                IdenticalReportedMetrics = original.Result.Metrics == after.Result.Metrics,
                IdenticalReportedSpectrum = original.Result.Spectrum.SequenceEqual(after.Result.Spectrum),
                IdenticalReportedChecks = original.Result.Checks.SequenceEqual(after.Result.Checks),
                Scope = "Cold evaluation under one final sampling protocol; historical optimizer work unchanged" });
        }
        await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, Experiment.JsonOptions));
        Console.WriteLine($"Cold-rechecked {summary.Count} original stacks with unchanged targets.");
    }

    internal static async Task SamplingTrace(string path, string output)
    {
        var d = await ExperimentStore.OpenAsync(path); var t = d.Target;
        var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
        var solver = new CoherentThinFilmSolver(materials[d.IncidentId], materials[d.SubstrateId], d.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
        var minimum = Math.Max(t.MinimumNm, t.CenterNm - 2 * t.FwhmNm); var maximum = Math.Min(t.MaximumNm, t.CenterNm + 2 * t.FwhmNm);
        var gridMethod = typeof(DesignService).GetMethod("GridWithAnchors", BindingFlags.Static | BindingFlags.NonPublic)!;
        ThinFilmBandMetrics Metrics(ThinFilmSample[] samples, bool anchored) => ThinFilmSpectrum.Measure(samples.Where(s => anchored
            ? s.WavelengthNanometers >= minimum && s.WavelengthNanometers <= maximum : Math.Abs(s.WavelengthNanometers - t.CenterNm) <= 2 * t.FwhmNm).ToArray());
        ThinFilmSample[] Scan(int count, bool anchored)
        {
            var grid = (double[])gridMethod.Invoke(null, [d, count])!;
            var samples = ThinFilmSpectrum.Sample(solver, anchored ? grid.Append(minimum).Append(maximum) : grid, t.AngleDegrees, t.Polarization);
            var m = Metrics(samples, anchored); var step = (t.MaximumNm - t.MinimumNm) / count; var points = new List<double>();
            foreach (var center in new double?[] { m.PeakWavelengthNanometers, m.LeftHalfMaximumNanometers, m.RightHalfMaximumNanometers }) if (center.HasValue)
                points.AddRange(ThinFilmSpectrum.Grid(Math.Max(t.MinimumNm, center.Value - 2 * step), Math.Min(t.MaximumNm, center.Value + 2 * step), 32));
            return samples.Concat(ThinFilmSpectrum.Sample(solver, points, t.AngleDegrees, t.Polarization)).GroupBy(s => s.WavelengthNanometers).Select(g => g.First()).OrderBy(s => s.WavelengthNanometers).ToArray();
        }
        var rows = new List<object>();
        foreach (var anchored in new[] { false, true })
        {
            ThinFilmBandMetrics? previous = null;
            var initial = Math.Max(d.Settings.PreviewIntervals, (int)Math.Ceiling((t.MaximumNm - t.MinimumNm) / t.FwhmNm * 64));
            for (var attempt = 0; attempt <= 4; attempt++)
            {
                var count = initial * (1 << attempt); var samples = Scan(count, anchored); var m = Metrics(samples, anchored);
                rows.Add(new { Anchored = anchored, Intervals = count, Samples = samples.Length, Metrics = m,
                    MinimumSample = samples.First(s => s.WavelengthNanometers >= minimum).WavelengthNanometers,
                    MaximumSample = samples.Last(s => s.WavelengthNanometers <= maximum).WavelengthNanometers,
                    DeltaRMinimum = previous is null ? (double?)null : Math.Abs(m.MinimumReflectance - previous.MinimumReflectance),
                    DeltaRMaximum = previous is null ? (double?)null : Math.Abs(m.MaximumReflectance - previous.MaximumReflectance),
                    DeltaPeak = previous is null ? (double?)null : Math.Abs(m.PeakTransmittance - previous.PeakTransmittance),
                    DeltaWidth = previous is null ? (double?)null : Math.Abs((m.FwhmNanometers ?? double.NaN) - (previous.FwhmNanometers ?? double.NaN)) });
                previous = m;
            }
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(rows, Experiment.JsonOptions));
    }
}
