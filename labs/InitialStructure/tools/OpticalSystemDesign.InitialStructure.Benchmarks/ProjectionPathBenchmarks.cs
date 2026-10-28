using System.Diagnostics;
using System.Text.Json;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

internal static class ProjectionPathBenchmarks
{
    public static async Task<int> Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--projection-path-audit <saved paired directory> <new output>");
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Never overwrite evidence.");
        Directory.CreateDirectory(output);
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        using var measured = SequentialTraceMeasurement.BeginCpuBudget(1);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var timer = Stopwatch.StartNew(); var rows = new List<object>(); var localCandidates = new Dictionary<int, double[][]>();
        foreach (var version in new[] { 5, 11 })
        {
            var directory = Path.Combine(args[0], "v" + version);
            using var input = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "input.json")));
            var local = await Read("local"); var full = await Read("full");
            var options = JsonSerializer.Deserialize<FixedTopologyOptions>(input.RootElement.GetProperty("Local").GetRawText(), TopologySamplingBenchmarks.Json)!;
            if (options.TransmissionPolicyVersion != version || options.ActiveParameterLimit != 2 || local.Steps.Length != 4
                || local.Steps.Take(3).Any(s => s.Accepted) || !local.Steps[3].Accepted) throw new InvalidDataException("Saved path cannot be reconstructed from one initial Jacobian.");
            var source = TopologyCompiler.Compile(JsonSerializer.Deserialize<DesignTopology>(input.RootElement.GetProperty("Source").GetRawText(), TopologySamplingBenchmarks.Json)!, local.Constraints);
            var binding = TopologyParameterBinder.Bind(source, local.Variables);
            var objective = new FixedTopologyObjective(source, binding, local.Targets, options.Sampling);
            var candidates = new List<double[]>(); var rate = options.LearningRate;
            foreach (var saved in local.Steps)
            {
                var events = new List<TopologyProjectionAttempt>();
                var policy = new TopologyTransmissionCompensation(objective.ResidualIds, version, events.Add);
                var proposed = policy.Propose(local.InitialEvaluation!, binding.InitialCoordinates, binding, saved.ActiveParameters!.Value,
                    rate / options.LearningRate, deadline.Token);
                candidates.Add(proposed.Coordinates);
                rows.Add(new { Version = version, Phase = "local", saved.Attempt, saved.Accepted, Rate = rate, Events = events, Coordinates = proposed.Coordinates });
                if (saved.Accepted && !proposed.Coordinates.SequenceEqual(local.Continuation!.Adam.Coordinates)) throw new InvalidDataException("Replayed accepted coordinates changed.");
                rate = saved.Accepted ? Math.Min(options.LearningRate, rate * 2) : rate * .5;
            }
            localCandidates[version] = candidates.ToArray();
            var fullOptions = JsonSerializer.Deserialize<FixedTopologyOptions>(input.RootElement.GetProperty("Full").GetRawText(), TopologySamplingBenchmarks.Json)!;
            if (fullOptions.TransmissionPolicyVersion != version || fullOptions.ActiveParameterLimit is not null
                || full.Steps.Any(s => s.ActiveParameters!.Value.Length != 12)) throw new InvalidDataException("Unexpected full-phase configuration.");
            var fullSource = TopologyCompiler.Compile(local.Topology, local.Constraints);
            var fullBinding = TopologyParameterBinder.Bind(fullSource, full.Variables);
            var fullObjective = new FixedTopologyObjective(fullSource, fullBinding, full.Targets, fullOptions.Sampling);
            var fullEvents = new List<TopologyProjectionAttempt>();
            var fullPolicy = new TopologyTransmissionCompensation(fullObjective.ResidualIds, version, fullEvents.Add);
            var first = fullPolicy.Propose(full.InitialEvaluation!, fullBinding.InitialCoordinates, fullBinding, Enumerable.Range(0, 12).ToArray(), 1, deadline.Token);
            rows.Add(new { Version = version, Phase = "full-first", Events = fullEvents, Coordinates = first.Coordinates,
                InitialCoordinates = fullBinding.InitialCoordinates,
                AllRecordedAttemptsDimension = 12, RecordedAttempts = full.Steps.Length, Termination = full.Termination.ToString(),
                ResumeUsed = false });
            async Task<FixedTopologyResult> Read(string name) => JsonSerializer.Deserialize<FixedTopologyResult>(await File.ReadAllTextAsync(Path.Combine(directory, name + ".json")), TopologySamplingBenchmarks.Json)!;
        }
        var sameCandidates = localCandidates[5].Zip(localCandidates[11], (a, b) => a.SequenceEqual(b)).All(v => v);
        if (!sameCandidates || measured.RayCount != 0) throw new InvalidDataException("Candidate equivalence or zero-trace audit failed.");
        await File.WriteAllTextAsync(Path.Combine(output, "audit.json"), JsonSerializer.Serialize(new { SameLocalCandidates = sameCandidates,
            TracedRays = measured.RayCount, Seconds = timer.Elapsed.TotalSeconds, TimeCapSeconds = 60, Rows = rows }, TopologySamplingBenchmarks.Json));
        return 0;
    }
}
