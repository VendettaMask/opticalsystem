using System.Text.Json;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.ZemaxComparison.Normalization;

public static class ResultNormalizer
{
    public static NumericResult CaptureWorkbench(string path)
    {
        var data = JsonFiles.Read<AnalysisData>(path);
        var result = new NumericResult { Semantics = "Canonical Workbench output; adapter equivalence has not yet been evaluated. Typed axes retained; scalar fields without typed units remain structured report fields." };
        result.Reports.Add(new(data.Values.ToDictionary(p => p.Key, p => p.Value is JsonElement value ? value.GetRawText() : JsonSerializer.Serialize(p.Value, JsonFiles.Options)),
            data.ReportText ?? "", data.OutcomeReason is null ? [] : [data.OutcomeReason]));
        var groups = data.PlotPanes is { Count: > 0 } ? data.PlotPanes.Select(p => p.Series).ToArray() : [data.PlotSeries];
        for (var pane = 0; pane < groups.Length; pane++)
            for (var index = 0; index < groups[pane].Count; index++)
            {
                var series = groups[pane][index]; var id = $"pane:{pane}:series:{index}";
                if (series.Points.Count == 0) continue;
                if (series.Kind is AnalysisSeriesKind.Line or AnalysisSeriesKind.Scatter or AnalysisSeriesKind.Bar or AnalysisSeriesKind.ColoredLine)
                    result.Series.Add(new(id, series.Name, series.Points.Select(p => p.X).ToArray(), series.Points.Select(p => p.Y).ToArray(),
                        new(series.XQuantity.ToString(), series.XUnit.ToString()), new(series.YQuantity.ToString(), series.YUnit.ToString())));
                else if (series.Points.All(p => p.Value.HasValue))
                {
                    var x = series.Points.Select(p => p.X).Distinct().Order().ToArray(); var y = series.Points.Select(p => p.Y).Distinct().Order().ToArray();
                    if ((long)x.Length * y.Length > 4_000_000)
                    {
                        result.Transformations.Add($"{id}: irregular raster retained in raw output; rectangular expansion exceeds validation memory limit");
                        continue;
                    }
                    var values = y.Select(_ => new double?[x.Length]).ToArray();
                    foreach (var point in series.Points) values[Array.BinarySearch(y, point.Y)][Array.BinarySearch(x, point.X)] = point.Value;
                    result.Grids.Add(new(id, x, y, values, new(series.XQuantity.ToString(), series.XUnit.ToString()),
                        new(series.YQuantity.ToString(), series.YUnit.ToString()), new(series.ValueQuantity.ToString(), series.ValueUnit.ToString())));
                }
            }
        return result;
    }

    public static NumericResult Workbench(string path, AnalysisComparisonEntry entry, CanonicalAnalysisRequest request)
    {
        var data = JsonFiles.Read<AnalysisData>(path);
        if (data.Outcome != AnalysisOutcome.Success) throw new InvalidDataException($"{data.Outcome}: {data.OutcomeReason}");
        if (entry.ZemaxSettingsMapper == "contract") return ExtendedResultNormalizer.Workbench(data, request);
        var result = new NumericResult { Semantics = Semantics(entry) };
        if (entry.ZemaxSettingsMapper == "first-order")
        {
            foreach (var key in new[] { "EffectiveFocalLength", "FNumber" })
                result.Scalars.Add(new(key, ((JsonElement)data.Values[key]).GetDouble(), key == "FNumber" ? "Dimensionless" : "Millimeter"));
            return result;
        }
        if (entry.ZemaxSettingsMapper == "spot")
        {
            var pane = data.PlotPanes?.Single() ?? throw new InvalidDataException("Expected one selected spot field");
            if (pane.Metrics?.Count != 2 || pane.Series.Count != 1 || pane.Series[0].XUnit != AnalysisAxisUnit.Millimeter)
                throw new InvalidDataException("Spot scalar contract requires two native metrics and focal millimeter coordinates");
            result.Scalars.Add(new("RmsSpotRadius", pane.Metrics[0].Value, "Micrometer"));
            result.Scalars.Add(new("GeoSpotRadius", pane.Metrics[1].Value, "Micrometer"));
            result.Transformations.Add("SpotDiagramAnalysis native metric positions 0=RMS, 1=GEO; labels are display-only; one monochromatic field.");
            return result;
        }
        if (entry.ResultKind == ResultKind.Grid2D)
        {
            var series = data.PlotSeries.Single();
            var xs = series.Points.Select(p => p.X).Distinct().Order().ToArray();
            var ys = series.Points.Select(p => p.Y).Distinct().Order().ToArray();
            if (entry.ZemaxSettingsMapper == "wavefront")
            {
                xs = Enumerable.Range(0, request.PupilSampling).Select(i => (i - request.PupilSampling / 2.0) / (request.PupilSampling / 2.0 - 1)).ToArray();
                ys = xs.ToArray();
                result.Transformations.Add("Wavefront even-grid physical pupil convention: (index-N/2)/(N/2-1), shared with committed golden test. Missing/vignetted samples remain null.");
            }
            var z = ys.Select(_ => new double?[xs.Length]).ToArray();
            var offset = 0d;
            if (entry.ZemaxSettingsMapper == "wavefront")
            {
                var wavelength = ((JsonElement)data.Values["WavelengthMicrometers"]).GetDouble();
                var opticalMean = ((JsonElement)data.Values["MeanOpticalPathDifference"]).GetDouble() / (wavelength * 1e-3);
                // WavefrontAnalysis subtracts its own minimum solely for display. Recover the signed physical OPD
                // from its unrounded optical-path mean, independent of every Zemax value.
                offset = opticalMean - series.Points.Average(p => p.Value!.Value);
                result.Transformations.Add($"Undo Workbench display-only minimum offset: add {offset:R} waves, derived solely from native MeanOpticalPathDifference / wavelength minus displayed mean; retain signed chief-reference OPD, no fit to Zemax.");
            }
            foreach (var p in series.Points)
            {
                var ix = Array.FindIndex(xs, x => Math.Abs(x - p.X) < 1e-10); var iy = Array.FindIndex(ys, y => Math.Abs(y - p.Y) < 1e-10);
                if (ix < 0 || iy < 0 || z[iy][ix].HasValue) throw new InvalidDataException("Grid sample is duplicate or off the explicit physical lattice");
                z[iy][ix] = p.Value is { } v && double.IsFinite(v) ? v + offset : null;
            }
            result.Grids.Add(new("grid", xs, ys, z, new(series.XQuantity.ToString(), series.XUnit.ToString()),
                new(series.YQuantity.ToString(), series.YUnit.ToString()), new(series.ValueQuantity.ToString(), series.ValueUnit.ToString())));
            return result;
        }
        var curves = data.PlotPanes is { Count: > 0 } ? data.PlotPanes.SelectMany(p => p.Series).ToArray() : data.PlotSeries.ToArray();
        if (entry.CanonicalAnalysisKey == "Pupil Aberration")
        {
            var panes = data.PlotPanes ?? throw new InvalidDataException("Missing pupil aberration panes");
            var fieldCount = ((JsonElement)data.Values["FieldCount"]).GetInt32();
            var waveCount = ((JsonElement)data.Values["WavelengthCount"]).GetInt32();
            if (panes.Count != fieldCount * 2 || panes.Any(p => p.Series.Count != waveCount))
                throw new InvalidDataException("Pupil aberration field/wavelength output contract changed");
            curves = [panes[(request.Field - 1) * 2].Series[request.Wavelength - 1], panes[(request.Field - 1) * 2 + 1].Series[request.Wavelength - 1]];
            result.Transformations.Add($"Select field {request.Field}, wavelength {request.Wavelength} from canonical all-field/all-wavelength pupil fan output; preserve native field ordering, do not infer indices from names.");
        }
        if (curves.Length != 2) throw new InvalidDataException("Expected exactly two ordered tangential/sagittal curves for one field and wavelength");
        for (var i = 0; i < 2; i++)
        {
            var curve = curves[i];
            var id = i == 0 ? "tangential" : "sagittal";
            var item = new Series1DResult(id, curve.Name, curve.Points.Select(p => p.X).ToArray(), curve.Points.Select(p => p.Y).ToArray(),
                new(curve.XQuantity.ToString(), curve.XUnit.ToString()), new(curve.YQuantity.ToString(), curve.YUnit.ToString()));
            result.Series.Add(PhysicalNormalization.Convert(item, entry.XAxis!, entry.YAxis!, result.Transformations));
        }
        result.Transformations.Add("Canonical one-field/one-wavelength output contract: native series/pane 0=tangential, 1=sagittal. No label parsing.");
        return result;
    }

    public static NumericResult Zemax(string path, AnalysisComparisonEntry entry, CanonicalAnalysisRequest request)
    {
        if (entry.ZemaxSettingsMapper == "contract") return ExtendedResultNormalizer.Zemax(path, request);
        using var doc = JsonDocument.Parse(File.ReadAllText(path)); var root = doc.RootElement;
        var result = new NumericResult { Semantics = Semantics(entry) };
        if (entry.ZemaxSettingsMapper == "first-order")
        {
            foreach (var p in root.GetProperty("scalars").EnumerateObject())
                result.Scalars.Add(new(p.Name, p.Value.GetDouble(), p.Name == "FNumber" ? "Dimensionless" : "Millimeter"));
            return result;
        }
        if (entry.ZemaxSettingsMapper == "spot")
        {
            var spot = root.GetProperty("spot");
            result.Scalars.Add(new("RmsSpotRadius", spot.GetProperty("rms").GetDouble(), "Micrometer"));
            result.Scalars.Add(new("GeoSpotRadius", spot.GetProperty("geo").GetDouble(), "Micrometer"));
            result.Transformations.Add("IAR_SpotDataResultMatrix focal RMS/GEO metrics are reported in micrometers for MM lens units (verified against native text export); no extra x1000 conversion or point-cloud registration.");
            return result;
        }
        if (entry.ResultKind == ResultKind.Grid2D)
        {
            var grids = root.GetProperty("dataGrids");
            if (grids.GetArrayLength() != 1) throw new InvalidDataException("Expected exactly one native numeric grid");
            var g = grids[0]; var nx = g.GetProperty("nx").GetInt32(); var ny = g.GetProperty("ny").GetInt32();
            var dx = g.GetProperty("dx").GetDouble(); var dy = g.GetProperty("dy").GetDouble();
            var minX = g.GetProperty("minX").GetDouble(); var minY = g.GetProperty("minY").GetDouble();
            var x = Enumerable.Range(0, nx).Select(i => minX + i * dx).ToArray(); var y = Enumerable.Range(0, ny).Select(i => minY + i * dy).ToArray();
            var pupil = entry.ZemaxSettingsMapper == "wavefront";
            if (pupil)
            {
                if (nx != request.PupilSampling || nx != ny || nx % 2 != 0 || Math.Abs(minX + 1) > 1e-10 || Math.Abs(dx - 2d / (nx - 1)) > 1e-10)
                    throw new InvalidDataException("Unrecognized native even WavefrontMap coordinate metadata");
                x = Enumerable.Range(0, nx).Select(i => (i - nx / 2d) / (nx / 2d - 1)).ToArray(); y = x.ToArray();
                result.Transformations.Add("ZOS WavefrontMap even N grid uses chief-ray index N/2 and pupil denominator N/2-1; decode sample coordinates using existing Zemax golden contract, not display-grid MinX/Dx. No fitted shift.");
            }
            var z = g.GetProperty("values").EnumerateArray().Select(row => row.EnumerateArray().Select(Finite).ToArray()).ToArray();
            var xy = pupil ? new Axis("PupilCoordinate", "Dimensionless") : new Axis("ImageHeight", "Micrometer");
            result.Grids.Add(new("grid", x, y, z, xy, xy, pupil ? new("WavefrontError", "Wave") : new("Irradiance", "Dimensionless")));
            return result;
        }
        var groups = root.GetProperty("dataSeries");
        var mtf = entry.ZemaxSettingsMapper.Contains("mtf", StringComparison.Ordinal);
        if (groups.GetArrayLength() != (mtf ? 1 : 2)) throw new InvalidDataException("Unexpected native curve group count for selected field and wavelength");
        for (var i = 0; i < 2; i++)
        {
            var group = groups[mtf ? 0 : i];
            var ys = group.GetProperty("y");
            var width = ys[0].GetArrayLength();
            if (ys.EnumerateArray().Any(row => row.GetArrayLength() != width)) throw new InvalidDataException("Ragged native curve columns");
            var active = Enumerable.Range(0, width).Where(col => ys.EnumerateArray().Any(row => Finite(row[col]).HasValue)).ToArray();
            if (active.Length != (mtf ? 2 : 1)) throw new InvalidDataException("Unexpected active native column count for explicit monochromatic selection");
            var column = active[mtf ? i : 0];
            var x = group.GetProperty("x").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            var y = ys.EnumerateArray().Select(row => row[column].GetDouble()).ToArray();
            result.Series.Add(new(i == 0 ? "tangential" : "sagittal", "Native column " + i, x, y, entry.XAxis!, entry.YAxis!));
            result.Transformations.Add($"Native group {(mtf ? 0 : i)} column {column}; other monochromatic wavelength slots are absent/null, never treated as zero.");
        }
        result.Transformations.Add(mtf ? "IAS MTF Modulation: native columns 0=T, 1=S; explicit selected field/wavelength, cycles/mm."
            : "IAS_Fan: native groups 0=T, 1=S; one selected wavelength per group, normalized pupil. No localized label parsing.");
        return result;
    }
    private static double? Finite(JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
    private static string Semantics(AnalysisComparisonEntry e) => e.ZemaxSettingsMapper switch
    {
        "wavefront" => "Signed chief-reference OPD in waves; wavelength reference sphere; no tilt removal; even pupil mask; Workbench display-only minimum offset undone from native optical-path metadata, no fitted piston adjustment",
        "fft-psf" or "huygens-psf" => "Native physical PSF irradiance; Normalize=false; chief ray; no post-capture renormalization",
        "spot" => "Monochromatic spot radii about chief ray, hexapolar rays, apertures retained",
        _ => "Native physical output with explicit CapturedSettings"
    };
}
