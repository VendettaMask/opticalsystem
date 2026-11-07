using System.Globalization;
using System.Text.Json;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

// Independent geometric ray comparison and a separately scoped check of the
// values the frozen plot actually reports. Neither can replace its verdict.
internal static class FanCoverageProbe
{
    internal static void Run(string study, string nativeRoot)
    {
        var rows = JsonFiles.Read<Row[]>(Path.Combine(study, "rows.json"));
        var models = JsonFiles.Read<Model[]>(Path.Combine(study, "models.json"));
        var indices = JsonFiles.Read<int[]>(Path.Combine(nativeRoot, "frozen-fan-indices.json"));
        var output = Path.Combine(study, "explicit-fan-coverage");
        if (Directory.Exists(output)) throw new IOException("Use a fresh fan coverage output directory.");
        Directory.CreateDirectory(output);
        var report = new List<object>();
        foreach (var index in indices)
        {
            var fan = rows[index - 1];
            var opd = rows[index];
            if (fan.Index != index || fan.Key != "Ray Fan" || opd.Key != "Optical Path Difference"
                || fan.LensIndex != opd.LensIndex || fan.Configuration != opd.Configuration)
                throw new InvalidDataException("Expected one Ray Fan and its paired OPD request.");
            var nativeDirectory = Path.Combine(nativeRoot, index.ToString(CultureInfo.InvariantCulture));
            var nativePath = Path.Combine(nativeDirectory, "point-reference.json");
            var source = models[fan.LensIndex - 1];
            if (!File.Exists(nativePath))
            {
                foreach (var item in new[] { fan, opd })
                    report.Add(new { item.Index, item.Lens, item.Key, error = "No explicit native batch result." });
                continue;
            }
            var native = JsonFiles.Read<JsonElement>(nativePath);
            var nativeRequestPath = Path.Combine(nativeRoot, "requests", index + ".json");
            var nativeRequest = JsonFiles.Read<CanonicalAnalysisRequest>(nativeRequestPath);
            var fanJob = JsonFiles.Read<WorkbenchJob>(fan.Job!);
            var opdJob = JsonFiles.Read<WorkbenchJob>(opd.Job!);
            if (native.GetProperty("inputSha256").GetString() != source.SourceSha256
                || nativeRequest.Fingerprint != fanJob.Request.Fingerprint
                || nativeRequest.Field != opdJob.Request.Field || nativeRequest.Wavelength != opdJob.Request.Wavelength
                || nativeRequest.Surface != -1 || opdJob.Request.Surface != -1
                || nativeRequest.RayCount != opdJob.Request.RayCount)
                throw new InvalidDataException($"Explicit fan reference does not identify original pair {index}.");
            var capturedModel = JsonFiles.Read<JsonElement>(Path.Combine(nativeDirectory, "model.json"));
            var field = capturedModel.GetProperty("fields").EnumerateArray().Single(f =>
                f.GetProperty("number").GetInt32() == nativeRequest.Field).GetProperty("data");
            var expectedField = nativeRequest.DefinedFields[nativeRequest.Field - 1];
            if (Math.Abs(field.GetProperty("X").GetDouble() - expectedField[0]) > 1e-12
                || Math.Abs(field.GetProperty("Y").GetDouble() - expectedField[1]) > 1e-12)
                throw new InvalidDataException("Explicit fan field coordinates differ.");
            double FieldValue(string name) => field.GetProperty(name).GetDouble();
            var pupil = new PupilVignetting(FieldValue("VDX"), FieldValue("VDY"),
                FieldValue("VCX"), FieldValue("VCY"), FieldValue("VAN"));
            foreach (var row in new[] { fan, opd })
            {
                var directory = Path.Combine(output, row.Index.ToString("D4", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(directory);
                var job = row == fan ? fanJob : opdJob;
                var request = job.Request;
                try
                {
                    var entry = AnalysisComparisonRegistry.Get(row.Key);
                    WorkbenchExecutor.Execute(job with { OutputDirectory = directory });
                    var workbenchPath = Path.Combine(directory, "data.json");
                    var actual = ResultNormalizer.Workbench(workbenchPath, entry, request);
                    var explicitReference = NativeExplicitFanNormalizer.Read(nativePath, entry, request,
                        bool.Parse(request.WorkbenchSettings["VignettedPupil"]) ? pupil : null,
                        allowGeometricChief: true);
                    var plotPath = Path.Combine(row.LegacyPath, "native", "data.json");
                    var plot = ResultNormalizer.Zemax(plotPath, entry, request, workbenchPath);
                    var physicalRun = Compare(row, request, entry, actual, explicitReference, Path.Combine(directory, "physical"));
                    var masks = new List<object>();
                    var plotOnly = new NumericResult();
                    var eligiblePlot = true;
                    foreach (var plotted in plot.Series)
                    {
                        var workbench = actual.Series.Single(series => series.Id == plotted.Id);
                        var batch = explicitReference.Series.Single(series => series.Id == plotted.Id);
                        if (!SameKnots(workbench.X, plotted.X) || !SameKnots(batch.X, plotted.X))
                            throw new InvalidDataException("Explicit fan and published plot must share each physical knot.");
                        var physicalMismatches = Enumerable.Range(0, batch.Y.Length).Where(i =>
                            double.IsFinite(workbench.Y[i]) != double.IsFinite(batch.Y[i])).ToArray();
                        var omitted = Enumerable.Range(0, batch.Y.Length).Where(i =>
                            double.IsFinite(batch.Y[i]) && !double.IsFinite(plotted.Y[i])).ToArray();
                        var extra = Enumerable.Range(0, batch.Y.Length).Where(i =>
                            !double.IsFinite(batch.Y[i]) && double.IsFinite(plotted.Y[i])).ToArray();
                        var transition = omitted.All(i => i > 0 && !double.IsFinite(batch.Y[i - 1]));
                        eligiblePlot &= physicalMismatches.Length == 0 && extra.Length == 0 && transition;
                        masks.Add(new { plotted.Id, knots = plotted.X.Length, physicalMismatches,
                            omitted, extra, allOmissionsAtSegmentStart = transition });
                        plotOnly.Series.Add(workbench with
                        {
                            Y = workbench.Y.Select((value, i) => double.IsFinite(plotted.Y[i]) ? value : double.NaN).ToArray()
                        });
                    }
                    var reported = eligiblePlot
                        ? Compare(row, request, entry, plotOnly, plot, Path.Combine(directory, "reported-knots"))
                        : null;
                    report.Add(new { row.Index, row.Lens, row.Key, row.Configuration,
                        physical = physicalRun.Conclusion, physicalRun.Reason, physicalMetrics = physicalRun.Metrics,
                        reportedKnots = reported?.Conclusion, reportedMetrics = reported?.Metrics, masks });
                }
                catch (Exception e) when (e is InvalidOperationException or ArgumentException or InvalidDataException)
                {
                    report.Add(new { row.Index, row.Lens, row.Key, row.Configuration, error = e.Message });
                }
            }
        }
        JsonFiles.Write(Path.Combine(output, "results.json"), report);
    }

    private static bool SameKnots(double[] a, double[] b) => a.Length == b.Length
        && a.Zip(b).All(pair => double.IsFinite(pair.First) && double.IsFinite(pair.Second)
            && Math.Abs(pair.First - pair.Second) <= 1e-12 * Math.Max(1, Math.Abs(pair.Second)));

    private static AnalysisRun Compare(Row row, CanonicalAnalysisRequest request, AnalysisComparisonEntry entry,
        NumericResult actual, NumericResult reference, string directory)
    {
        Directory.CreateDirectory(directory);
        var run = new AnalysisRun { Key = row.Key, Directory = directory, Request = request };
        try { ComparisonRunner.Compare(run, entry, row.Settings, actual, reference, directory); }
        catch (InvalidDataException e) { run.Conclusion = Conclusion.Incomparable; run.Reason = e.Message; }
        return run;
    }
}
