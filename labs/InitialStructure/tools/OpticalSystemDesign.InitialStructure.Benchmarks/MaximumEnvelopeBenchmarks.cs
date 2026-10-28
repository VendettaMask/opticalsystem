using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

internal static class MaximumEnvelopeBenchmarks
{
    private sealed record RayRow(string Id, string Group, int Wave, double Nanometers, double PupilX, double PupilY,
        double X, double Y, double Radius, double[] Gradient);
    private sealed record FieldRow(double X, double Y, double CentroidX, double CentroidY, double CoreMaximum,
        string Winner, string RunnerUp, double Gap, RayRow[] Rays);
    private sealed record Probe(string Name, double[] Coordinates, long Rays, FieldRow[] Fields);

    public static async Task<int> Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("--maximum-envelope-diagnose <frozen protocol> <saved v5 diagnosis> <saved v7 test> <new output>");
        var output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Never overwrite evidence.");
        Directory.CreateDirectory(output); Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        using var protocol = JsonDocument.Parse(await File.ReadAllTextAsync(args[0]));
        var path = protocol.RootElement.GetProperty("Endpoint").GetString()!;
        var bytes = await File.ReadAllBytesAsync(path);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != protocol.RootElement.GetProperty("EndpointSha256").GetString()) throw new InvalidDataException("Endpoint SHA changed.");
        var result = JsonSerializer.Deserialize<FixedTopologyResult>(bytes, TopologySamplingBenchmarks.Json)!;
        var inputBytes = await File.ReadAllBytesAsync(protocol.RootElement.GetProperty("Input").GetString()!);
        if (Convert.ToHexString(SHA256.HashData(inputBytes)).ToLowerInvariant() != protocol.RootElement.GetProperty("InputSha256").GetString()) throw new InvalidDataException("Input SHA changed.");
        using var input = JsonDocument.Parse(inputBytes);
        var options = JsonSerializer.Deserialize<FixedTopologyOptions>(input.RootElement.GetProperty("Full").GetRawText(), TopologySamplingBenchmarks.Json)!;
        var source = TopologyCompiler.Compile(result.Topology, result.Constraints);
        var binding = TopologyParameterBinder.Bind(source, result.Variables);
        var objective = new FixedTopologyObjective(source, binding, result.Targets, options.Sampling);
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
        using var v7 = JsonDocument.Parse(await File.ReadAllTextAsync(args[2]));
        var q = binding.InitialCoordinates.ToArray();
        var savedQ = saved.RootElement.GetProperty("Coordinates").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        if (q.Zip(savedQ, (a, b) => Math.Abs(a - b)).Max() > 1e-12) throw new InvalidDataException("Saved direction has another starting point.");
        var directions = new Dictionary<string, double[]>
        {
            ["v5"] = saved.RootElement.GetProperty("Rows")[0].GetProperty("Coordinates").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
            ["v7"] = v7.RootElement.GetProperty("part").GetProperty("Continuation").GetProperty("RejectedTrial").GetProperty("Coordinates").EnumerateArray().Select(x => x.GetDouble()).ToArray()
        };
        var pupils = ApertureSampler.GenerateGaussianQuadrature(options.Sampling.PupilRings, options.Sampling.PupilArms).ToArray();
        var checks = TopologyEvaluationPlan.Checks();
        var waves = source.Topology.Wavelengths.Where(w => w.Weight > 0).ToArray();
        var fields = objective.Fields.Select(f => (f.X, f.Y)).ToArray();
        var probes = new List<Probe>(); var failures = new List<string>();
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(500_000);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var timer = Stopwatch.StartNew();
        try
        {
            Capture("base", q, fields);
            foreach (var pair in directions)
                foreach (var fraction in new[] { .001, .01, .125, .25, .5, 1.0 })
                    Capture(pair.Key + ":" + fraction.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        q.Zip(pair.Value, (a, b) => a + fraction * (b - a)).ToArray(), fields);
            for (var column = 0; column < q.Length; column++)
                foreach (var sign in new[] { -1, 1 })
                {
                    var trial = q.ToArray(); trial[column] += sign * 1e-5;
                    if (trial[column] < binding.Slots[column].LowerCoordinate || trial[column] > binding.Slots[column].UpperCoordinate)
                    { failures.Add($"parameter {column}, sign {sign}: excluded by original bound, no trace"); continue; }
                    Capture($"difference:{column}:{sign}", trial, [(0.0, 1.0)]);
                }
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(output, "envelope.json"), JsonSerializer.Serialize(new
            {
                EndpointSha256 = protocol.RootElement.GetProperty("EndpointSha256").GetString(), result.Variables, source.Topology.Fields, source.Topology.Wavelengths,
                Sampling = options.Sampling, BoundRayQuota = 500_000, BoundSeconds = 60, ActualRays = quota.RayCount,
                Seconds = timer.Elapsed.TotalSeconds, Probes = probes, Failures = failures
            }, TopologySamplingBenchmarks.Json));
        }
        return probes.Count == 36 && failures.All(s => s.Contains("excluded by original bound")) ? 0 : 1;

        void Capture(string name, double[] coordinates, (double X, double Y)[] requestedFields)
        {
            deadline.Token.ThrowIfCancellationRequested(); var start = quota.RayCount;
            var optic = TopologyParameterBinder.Apply(source, binding, coordinates).CreateOptic();
            var integration = DifferentiableOpticalMetrics.EvaluateAtFields(optic, binding.CoreParameters, requestedFields, pupils, deadline.Token, usePhysicalStop: true);
            var boundary = DifferentiableOpticalMetrics.EvaluateAtFields(optic, binding.CoreParameters, requestedFields, checks, deadline.Token, usePhysicalStop: true);
            var fieldRows = new List<FieldRow>();
            for (var f = 0; f < requestedFields.Length; f++)
            {
                var field = integration.Fields[f]; var check = boundary.Fields[f]; var rays = new List<RayRow>();
                Add("integration", field.Rays, pupils); Add("boundary", check.Rays, checks);
                var ranked = rays.OrderByDescending(r => r.Radius).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
                var maximum = DifferentiableOpticalMetrics.MaximumRadiusWithChecks(field, check).Value;
                if (Math.Abs(maximum - ranked[0].Radius) > 1e-12) throw new InvalidDataException("Diagnostic radius disagrees with Core maximum definition.");
                fieldRows.Add(new(field.NormalizedFieldX, field.NormalizedFieldY, field.CentroidX.Value, field.CentroidY.Value,
                    maximum, ranked[0].Id, ranked[1].Id, ranked[0].Radius - ranked[1].Radius, rays.ToArray()));
                void Add(string group, IReadOnlyList<DifferentiableSpotRay> samples, PupilSample[] pupilSamples)
                {
                    for (var i = 0; i < samples.Count; i++)
                    {
                        var ray = samples[i]; var wave = i / pupilSamples.Length; var pupil = i % pupilSamples.Length;
                        // Diagnostic-only reduction of Core coordinates/tangents, checked against Core maximum above.
                        var radius = DifferentialScalar.Sqrt(DifferentialScalar.Square(ray.X - field.CentroidX) + DifferentialScalar.Square(ray.Y - field.CentroidY));
                        rays.Add(new($"{group}:wave{wave}:pupil{pupil}", group, wave, waves[wave].Nanometers, pupilSamples[pupil].X, pupilSamples[pupil].Y,
                            ray.X.Value, ray.Y.Value, radius.Value, radius.Gradient()));
                    }
                }
            }
            probes.Add(new(name, coordinates, quota.RayCount - start, fieldRows.ToArray()));
        }
    }
}
