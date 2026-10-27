using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class RepresentativeReductionBenchmarks
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    public static async Task<int> AuditFailure(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--reduction-failure-audit <qualified-control checkpoint> <new output directory>");
        var directory = Path.GetFullPath(args[1]);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Use a new output directory.");
        Directory.CreateDirectory(directory); Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(args[0]);
        var source = state.Candidates.Single(item => item.Id == "seed-0");
        var reduced = state.Candidates.Where(item => item.Topology.Elements.Length < source.Topology.Elements.Length && item.Assessment.Completed).OrderBy(item => item.Score).First();
        var checks = new List<object>();
        foreach (var (candidate, name) in new[] { (source, "qualified-source"), (reduced, "unqualified-reduced") })
        {
            var path = await AutonomousDesignFiles.ExportAsync(candidate, state.Plan.Constraints, Path.Combine(directory, name + ".staropt"));
            using var quota = SequentialTraceMeasurement.BeginCpuBudget(800_000);
            var imported = await AutonomousDesignFiles.ImportSeedAsync(path, state.Plan.Constraints);
            var compiled = TopologyCompiler.Compile(imported, state.Plan.Constraints); var optic = compiled.CreateOptic();
            var assessment = TopologyValidator.Validate(compiled, state.Plan.Targets);
            if (!assessment.Completed || assessment.MeetsTargets != candidate.Assessment.MeetsTargets
                || Math.Abs(assessment.Fields.Max(field => field.RmsRadiusMillimeters)!.Value - candidate.Assessment.Fields.Max(field => field.RmsRadiusMillimeters)!.Value) > 1e-10
                || !assessment.Violations.Select(item => item.Code).SequenceEqual(candidate.Assessment.Violations.Select(item => item.Code)))
                throw new InvalidDataException("Native re-import did not reproduce the qualified/failed endpoint.");
            var fields = assessment.Fields.Select((field, index) =>
            {
                var angle = FieldCoordinates.Denormalize(optic.Fields, field.Field.X, field.Field.Y);
                return new
                {
                    Index = index,
                    XDegrees = angle.X,
                    YDegrees = angle.Y,
                    field.RmsRadiusMillimeters,
                    field.MaximumRadiusMillimeters,
                    field.ValidRays,
                    field.AttemptedRays,
                    field.CheckValidRays,
                    field.CheckAttemptedRays,
                    field.DistortionFraction
                };
            }).ToArray();
            checks.Add(new { Name = name, candidate.Id, Elements = imported.Elements.Length, Assessment = assessment, Fields = fields, AdditionalAuditRays = quota.RayCount });
            Console.WriteLine($"{name}: {imported.Elements.Length} elements; pass={assessment.MeetsTargets}; original failed gates reproduced; audit rays={quota.RayCount}");
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "native-checks.json"), JsonSerializer.Serialize(checks, Json));
        return 0;
    }
    public static async Task<int> Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--reduction-representative <frozen protocol> <new output directory>");
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Existing evidence is never overwritten.");
        using var process = Process.GetCurrentProcess(); process.PriorityClass = ProcessPriorityClass.BelowNormal;
        var bytes = await File.ReadAllBytesAsync(args[0]); using var protocol = JsonDocument.Parse(bytes); var root = protocol.RootElement;
        if (root.GetProperty("Version").GetInt32() != 1 || root.GetProperty("Cases").GetArrayLength() is < 1 or > 8) throw new InvalidDataException("Unsupported representative protocol.");
        var modes = root.GetProperty("Modes").EnumerateArray().Select(item => item.GetString()).ToArray();
        var precisionComparison = modes.SequenceEqual(new[] { "coarse", "accurate" });
        var axialComparison = modes.SequenceEqual(new[] { "curvature", "axial" });
        var wideComparison = modes.SequenceEqual(new[] { "v3", "maximum", "joint" });
        if (wideComparison && root.GetProperty("JointTransmissionPolicyVersion").GetInt32() != 4) throw new InvalidDataException("The joint restoration identity must be version 4.");
        var transmissionComparison = modes.SequenceEqual(new[] { "control", "local", "transmission", "both" }) || modes.SequenceEqual(new[] { "control", "both" }) || precisionComparison || axialComparison || wideComparison;
        if (transmissionComparison && (!root.TryGetProperty("TransmissionPolicyVersion", out var version) || version.GetInt32() != 3))
            throw new InvalidDataException("Unversioned transmission protocols are historical evidence. Use the explicit current v3 protocol for a fresh run.");
        if (!root.GetProperty("Seeds").EnumerateArray().Select(item => item.GetInt64()).SequenceEqual(new long[] { 42, 101 })
            || !root.GetProperty("Groups").EnumerateArray().Select(item => item.GetString()).SequenceEqual(new[] { "C", "D" })
            || !(modes.SequenceEqual(new[] { "legacy", "fixed" }) || modes.SequenceEqual(new[] { "fixed", "boundary" })
                || modes.SequenceEqual(new[] { "budget", "boundary-budget" }) || transmissionComparison))
            throw new InvalidDataException("Frozen representative pairing changed.");
        var budget = root.GetProperty("Budget"); var training = root.GetProperty("Training");
        foreach (var task in root.GetProperty("Cases").EnumerateArray())
            if (!task.TryGetProperty("HalfFieldDegrees", out var halfField) || !halfField.TryGetDouble(out var angle) || !double.IsFinite(angle))
                throw new InvalidDataException("Each frozen task requires finite HalfFieldDegrees summary metadata before any optical work.");
        if (budget.GetProperty("MaximumTrials").GetInt32() != 6 || budget.GetProperty("MaximumFunctionEvaluations").GetInt32() != 600
            || budget.GetProperty("MaximumGradientEvaluations").GetInt32() != 100 || budget.GetProperty("MaximumTracedRays").GetInt64() != 2_000_000
            || budget.GetProperty("TimeLimitSeconds").GetInt32() != 60) throw new InvalidDataException("Frozen short-run caps changed.");
        Directory.CreateDirectory(output); await File.WriteAllBytesAsync(Path.Combine(output, "frozen-protocol.json"), bytes);
        var binaries = new[] { typeof(AutonomousSearchService).Assembly, typeof(AutonomousSearchPlan).Assembly, typeof(Optic).Assembly, Assembly.GetExecutingAssembly() }
            .ToDictionary(item => item.GetName().Name!, item => Hash(File.ReadAllBytes(item.Location)));
        var runs = new List<object>(); var paired = new Dictionary<string, string>();
        foreach (var task in root.GetProperty("Cases").EnumerateArray())
        {
            var sourcePath = task.GetProperty("Source").GetProperty("RepositoryPath").GetString()!;
            if (Hash(await File.ReadAllBytesAsync(sourcePath)) != task.GetProperty("OriginalSha256").GetString()) throw new InvalidDataException("Original source bytes changed.");
            foreach (var seed in root.GetProperty("Seeds").EnumerateArray().Select(item => item.GetInt64()))
            {
                var template = task.GetProperty("SourceTopology").Deserialize<DesignTopology>(Json)!;
                var source = root.TryGetProperty("ApplyCurvaturePerturbation", out var perturb) && !perturb.GetBoolean() ? template : Perturb(template, seed);
                var target = root.GetProperty("Targets").Deserialize<TopologyTargets>(Json)! with
                { EffectiveFocalLengthMillimeters = task.GetProperty("FocalTargetMillimeters").GetDouble(), FNumber = task.GetProperty("FNumberTarget").GetDouble() };
                foreach (var group in root.GetProperty("Groups").EnumerateArray().Select(item => item.GetString()!))
                    foreach (var mode in root.GetProperty("Modes").EnumerateArray().Select(item => item.GetString()!))
                    {
                        var fixedPolicy = mode != "legacy";
                        var stage = new FixedTopologyOptions
                        {
                            ComputeMode = DesignComputeMode.Cpu,
                            PerformFinalAssessment = false,
                            RequireDescent = fixedPolicy ? true : null,
                            TransmissionFirst = precisionComparison || axialComparison || wideComparison || transmissionComparison && mode is "transmission" or "both" ? true : null,
                            TransmissionPolicyVersion = mode == "joint" ? 4 : transmissionComparison && mode != "control" ? 3 : null,
                            Sampling = training.Deserialize<TopologyTrainingSampling>(Json)! with
                            {
                                IncludeBoundaryClearance = transmissionComparison || mode.StartsWith("boundary", StringComparison.Ordinal) ? true : null,
                                IncludeBoundaryMaximum = wideComparison && mode != "v3" ? true : null,
                                PupilRings = mode == "accurate" ? root.GetProperty("AccurateSampling").GetProperty("PupilRings").GetInt32() : training.GetProperty("PupilRings").GetInt32(),
                                PupilArms = mode == "accurate" ? root.GetProperty("AccurateSampling").GetProperty("PupilArms").GetInt32() : training.GetProperty("PupilArms").GetInt32()
                            }
                        };
                        var plan = new AutonomousSearchPlan
                        {
                            Seeds = [source],
                            Targets = target,
                            Options = new()
                            {
                                CheckStageFeasibility = fixedPolicy ? true : null,
                                ProtectCompensationRayBudget = transmissionComparison || mode.Contains("budget", StringComparison.Ordinal) ? true : null,
                                AxialCompensation = wideComparison || mode == "axial" ? root.GetProperty("AxialCompensation").Deserialize<TopologyAxialCompensationOptions>(Json) : null,
                                Seed = seed,
                                MaximumTrials = budget.GetProperty("MaximumTrials").GetInt32(),
                                MaximumFunctionEvaluations = budget.GetProperty("MaximumFunctionEvaluations").GetInt32(),
                                MaximumGradientEvaluations = budget.GetProperty("MaximumGradientEvaluations").GetInt32(),
                                MaximumTracedRays = budget.GetProperty("MaximumTracedRays").GetInt64(),
                                MaximumStageTracedRays = budget.GetProperty("MaximumStageTracedRays").GetInt64(),
                                FinalValidationRayReserve = budget.GetProperty("FinalValidationRayReserve").GetInt64(),
                                TimeLimit = TimeSpan.FromSeconds(budget.GetProperty("TimeLimitSeconds").GetInt32()),
                                CurvatureBoundPerMillimeter = task.GetProperty("CurvatureBoundPerMillimeter").GetDouble(),
                                Recovery = new() { MaximumFunctionEvaluations = training.GetProperty("RecoveryFunctions").GetInt32(), ProfileRadialSamples = stage.Sampling.ProfileRadialSamples },
                                Local = Stage(training.GetProperty("LocalUpdates").GetInt32()) with { ActiveParameterLimit = precisionComparison || axialComparison || wideComparison || transmissionComparison && mode is "local" or "both" ? 2 : null },
                                Full = Stage(training.GetProperty("FullUpdates").GetInt32()),
                                Expansion = new() { EnableGradualDeletion = group == "D", EnableSplit = false, EnableMerge = false, EnableMaterial = false, EnableProfile = false }
                            }
                        };
                        var pairKey = task.GetProperty("Id").GetString() + ":" + seed;
                        var identity = ContentFingerprint.Compute(new { plan.Seeds, plan.Targets, plan.Constraints, Budget = budget.GetRawText(), Sampling = stage.Sampling with { IncludeBoundaryClearance = null, IncludeBoundaryMaximum = null, PupilRings = training.GetProperty("PupilRings").GetInt32(), PupilArms = training.GetProperty("PupilArms").GetInt32() }, plan.Options.CurvatureBoundPerMillimeter });
                        if (paired.TryGetValue(pairKey, out var previous) && previous != identity) throw new InvalidDataException("Paired task settings changed.");
                        paired[pairKey] = identity;
                        var id = $"repr-{task.GetProperty("Id").GetString()}-{seed}-{group}-{mode}";
                        var directory = Path.Combine(output, id); Directory.CreateDirectory(directory);
                        await AutonomousDesignFiles.SavePlanAsync(plan, Path.Combine(directory, "task.topology-plan.json"));
                        var store = new AutonomousSearchCheckpointStore(); var path = Path.Combine(directory, "run.autonomous.json");
                        var initial = AutonomousSearchService.Create(id, plan) with { Revision = 1 }; await store.SaveAsync(initial, path, 0); var revision = initial.Revision;
                        try
                        {
                            var endpoint = await Task.Run(() => AutonomousSearchService.Run(initial, state =>
                            { store.SaveAsync(state, path, revision).AsTask().GetAwaiter().GetResult(); revision = state.Revision; }));
                            if (ContentFingerprint.Compute(endpoint) != ContentFingerprint.Compute(await store.LoadAsync(path))) throw new InvalidDataException("Endpoint persistence mismatch.");
                            if (endpoint.Consumed.FunctionEvaluations != endpoint.Events.Sum(item => item.Work.FunctionEvaluations)
                                || endpoint.Consumed.TracedRays != endpoint.Events.Sum(item => item.Work.TracedRays)) throw new InvalidDataException("Actual work ledger mismatch.");
                            var reduced = endpoint.AcceptedArchive.Where(item => item.Topology.Elements.Length < template.Elements.Length).ToArray();
                            var exportChecks = new List<object>();
                            foreach (var candidate in endpoint.AcceptedArchive) await AutonomousDesignFiles.ExportAsync(candidate, plan.Constraints, Path.Combine(directory, candidate.Id + ".staropt"));
                            if ((axialComparison || wideComparison) && endpoint.Candidates.Where(item => !item.Assessment.MeetsTargets).OrderBy(item => item.Score).FirstOrDefault() is { } failed)
                                await AutonomousDesignFiles.ExportAsync(failed, plan.Constraints, Path.Combine(directory, "best-failed.staropt"));
                            if (reduced.OrderBy(item => item.Topology.Elements.Length).ThenBy(item => item.Score).FirstOrDefault() is { } minimum)
                            {
                                using var quota = SequentialTraceMeasurement.BeginCpuBudget(plan.Options.FinalValidationRayReserve);
                                var imported = await AutonomousDesignFiles.ImportSeedAsync(Path.Combine(directory, minimum.Id + ".staropt"), plan.Constraints);
                                var verified = TopologyValidator.Validate(TopologyCompiler.Compile(imported, plan.Constraints), plan.Targets);
                                exportChecks.Add(new { minimum.Id, Elements = imported.Elements.Length, Assessment = verified, AdditionalAuditRays = quota.RayCount });
                                if (!verified.MeetsTargets || imported.Elements.Length != minimum.Topology.Elements.Length) throw new InvalidDataException("Reduced export re-import failed original targets.");
                            }
                            await File.WriteAllTextAsync(Path.Combine(directory, "export-checks.json"), JsonSerializer.Serialize(exportChecks, Json));
                            var start = endpoint.Candidates.FirstOrDefault(item => item.Id == "seed-0");
                            runs.Add(new
                            {
                                Task = task.GetProperty("Id").GetString(),
                                SourceName = task.GetProperty("Source").GetProperty("Name").GetString(),
                                Seed = seed,
                                Group = group,
                                Mode = mode,
                                Id = id,
                                SourceElements = template.Elements.Length,
                                OriginalHalfField = task.GetProperty("HalfFieldDegrees").GetDouble(),
                                SourceFingerprint = ContentFingerprint.Compute(source),
                                PairedTaskFingerprint = identity,
                                endpoint.PlanFingerprint,
                                Status = endpoint.Status.ToString(),
                                endpoint.TrialCursor,
                                endpoint.Consumed,
                                PendingPhase = endpoint.Pending?.Phase.ToString(),
                                SeedAssessment = start is null ? null : Summary(start),
                                ReducedSuccess = reduced.Length > 0,
                                MinimumAcceptedElements = endpoint.AcceptedArchive.IsEmpty ? (int?)null : endpoint.AcceptedArchive.Min(item => item.Topology.Elements.Length),
                                Archive = endpoint.AcceptedArchive.Select(Summary).ToArray(),
                                ReducedCandidates = endpoint.Candidates.Where(item => item.Topology.Elements.Length < template.Elements.Length).Select(Summary).ToArray(),
                                BestEndpoint = endpoint.Candidates.OrderByDescending(item => item.Assessment.MeetsTargets).ThenBy(item => item.Score).Select(Summary).FirstOrDefault(),
                                DeletionTrials = endpoint.Events.Where(item => item.Operation == "delete").Select(item => item.TrialId).Distinct().Count(),
                                RecoveryOutcomes = endpoint.Events.Where(item => item.Recovery is not null).GroupBy(item => item.Recovery!.Termination.ToString()).ToDictionary(item => item.Key, item => item.Count()),
                                FailureEvents = endpoint.Events.Where(item => item.Outcome is not ("Accepted" or "Recovered" or "UpdateLimit" or "Converged" or "TransitionFeasible"))
                                    .Select(item => new
                                    {
                                        item.Sequence,
                                        item.TrialId,
                                        item.ParentId,
                                        item.Operation,
                                        Phase = item.Phase.ToString(),
                                        item.Outcome,
                                        item.Detail,
                                        RecoveryDiagnostics = item.Recovery?.Diagnostics,
                                        OptimizationDiagnostics = item.Optimization?.Diagnostics
                                    }).ToArray(),
                                ExportAuditRays = exportChecks.Count == 0 ? 0L : ((JsonElement)JsonSerializer.SerializeToElement(exportChecks[0], Json)).GetProperty("AdditionalAuditRays").GetInt64()
                            });
                            Console.WriteLine($"{id}: {endpoint.Status}; seed-pass={start?.Assessment.MeetsTargets}; reduced-pass={reduced.Length > 0}; min accepted={endpoint.AcceptedArchive.Select(item => item.Topology.Elements.Length).DefaultIfEmpty().Min()}; F/G/rays={endpoint.Consumed.FunctionEvaluations}/{endpoint.Consumed.GradientEvaluations}/{endpoint.Consumed.TracedRays}");
                        }
                        catch (Exception error) when (error is IOException or ArgumentException or ArithmeticException or InvalidOperationException or NotSupportedException)
                        { runs.Add(new { Task = task.GetProperty("Id").GetString(), Seed = seed, Group = group, Mode = mode, Id = id, Status = "Failed", Failure = error.ToString(), OriginalRecordPreserved = path }); Console.WriteLine($"{id}: FAILED {error.Message}"); }
                        await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
                        {
                            ProtocolSha256 = Hash(bytes),
                            Binaries = binaries,
                            CpuProcessors = Environment.ProcessorCount,
                            Priority = process.PriorityClass.ToString(),
                            PairedInputsVerified = true,
                            Runs = runs
                        }, Json));
                        FixedTopologyOptions Stage(int updates) => stage with { MaximumUpdates = updates, MaximumFunctionEvaluations = updates + 1, MaximumGradientEvaluations = updates + 1 };
                    }
            }
        }
        return 0;
    }
    private static object Summary(AutonomousSearchCandidate candidate)
    {
        var assessment = candidate.Assessment;
        return new
        {
            candidate.Id,
            candidate.ParentId,
            Elements = candidate.Topology.Elements.Length,
            assessment.Completed,
            assessment.MeetsTargets,
            assessment.EffectiveFocalLengthMillimeters,
            assessment.FNumber,
            WorstRmsMillimeters = assessment.Fields.IsEmpty || assessment.Fields.Any(item => item.RmsRadiusMillimeters is null) ? null : assessment.Fields.Max(item => item.RmsRadiusMillimeters),
            WorstMaximumMillimeters = assessment.Fields.IsEmpty || assessment.Fields.Any(item => item.MaximumRadiusMillimeters is null) ? null : assessment.Fields.Max(item => item.MaximumRadiusMillimeters),
            MinimumValidFraction = assessment.Fields.IsEmpty ? (double?)null : assessment.Fields.Min(item => item.AttemptedRays == 0 ? 0 : (double)item.ValidRays / item.AttemptedRays),
            MinimumCheckValidFraction = assessment.Fields.IsEmpty ? (double?)null : assessment.Fields.Min(item => item.CheckAttemptedRays == 0 ? 0 : (double)item.CheckValidRays / item.CheckAttemptedRays),
            LostCheckRays = assessment.Fields.Sum(item => item.CheckAttemptedRays - item.CheckValidRays),
            FieldCount = assessment.Fields.Length,
            assessment.Violations,
            assessment.Failure,
            assessment.OpticFingerprint
        };
    }
    private static DesignTopology Perturb(DesignTopology source, long seed)
    {
        var random = (ulong)seed;
        double Factor() { random ^= random << 13; random ^= random >> 7; random ^= random << 17; return 1 + ((random >> 11) * (1.0 / (1UL << 53)) * 2 - 1) * .001; }
        return source with
        {
            Elements = source.Elements.Select(element => element with
            {
                Front = element.Front with { Profile = element.Front.Profile with { CurvaturePerMillimeter = element.Front.Profile.CurvaturePerMillimeter * Factor() } },
                Back = element.Back with { Profile = element.Back.Profile with { CurvaturePerMillimeter = element.Back.Profile.CurvaturePerMillimeter * Factor() } }
            }).ToImmutableArray()
        };
    }
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    public static async Task<int> Prepare(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--reduction-prepare <inventory.json> <new output directory>");
        var directory = Path.GetFullPath(args[1]);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Use a new output directory.");
        Directory.CreateDirectory(directory); using var input = JsonDocument.Parse(await File.ReadAllBytesAsync(args[0]));
        var results = new List<object>();
        foreach (var item in input.RootElement.EnumerateArray().Where(item => item.TryGetProperty("IndependentStop", out var stop) && stop.GetBoolean()
            && item.GetProperty("Infinite").GetBoolean() && item.GetProperty("CementedInterfaces").GetInt32() == 0))
        {
            var source = item.GetProperty("Source"); var path = source.GetProperty("RepositoryPath").GetString()!;
            var id = source.GetProperty("Id").GetString()!;
            try
            {
                var project = await StarOptProjectStore.LoadAsync(path); var original = project.Configurations[project.ActiveConfigurationIndex];
                var snapshot = original.ToSnapshot();
                if ((snapshot.RadiusPickups?.Count ?? 0) + (snapshot.ThicknessPickups?.Count ?? 0) + (snapshot.SemiDiameterPickups?.Count ?? 0) != 0)
                    throw new NotSupportedException("Pickups excluded from this physical evaluation preparation.");
                var beforeFocal = original.Paraxial.EstimateEffectiveFocalLength(); var beforeF = original.Paraxial.EstimateFNumber();
                var stopIndex = original.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
                var wavelength = original.Wavelengths.First(wave => wave.IsPrimary).Nanometers / 1000;
                var radius = Math.Abs(original.Paraxial.TraceNormalizedPupil(0, [1.0], wavelength).Heights[stopIndex][0]);
                if (!double.IsFinite(radius) || radius <= .001) throw new InvalidDataException("No finite positive Core-calibrated physical stop.");
                var frozen = snapshot with
                {
                    MeritOperands = null,
                    ReferenceOpd = null,
                    SolveSettings = new(0, false),
                    RayAimingEnabled = true,
                    Surfaces = snapshot.Surfaces.Select(surface => surface with { RadiusVariable = false, ThicknessVariable = false, MechanicalSemiDiameterSolveCode = 0 }).ToList()
                };
                var optic = Optic.FromSnapshot(frozen);
                optic.Aperture.Kind = ApertureKind.FloatByStopSize; optic.Aperture.Value = radius * 2;
                for (var index = 0; index < optic.SurfaceGroup.Items.Count; index++)
                {
                    var surface = optic.SurfaceGroup.Items[index];
                    surface.PhysicalAperture = index == 0 || index == optic.SurfaceGroup.Items.Count - 1 ? null : new CircularAperture(index == stopIndex ? radius : surface.SemiDiameter);
                    if (index == stopIndex) surface.SemiDiameter = radius;
                    surface.SemiDiameterFixed = true;
                }
                optic.InvalidateRayTraceCache();
                var topology = TopologyCompiler.Import(optic.ToSnapshot()).Topology;
                var compiled = TopologyCompiler.Compile(topology); var final = compiled.CreateOptic();
                if (Math.Abs(final.Paraxial.EstimateFNumber() / beforeF - 1) > 1e-9 || Math.Abs(final.Paraxial.EstimateEffectiveFocalLength() / beforeFocal - 1) > 1e-9)
                    throw new InvalidDataException("Physical variant changed first-order focal length or F number.");
                var output = Path.Combine(directory, id); Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(Path.Combine(output, "source.optic.json"), JsonSerializer.Serialize(snapshot, Json));
                await File.WriteAllTextAsync(Path.Combine(output, "prepared.optic.json"), JsonSerializer.Serialize(optic.ToSnapshot(), Json));
                await File.WriteAllTextAsync(Path.Combine(output, "seed.topology.json"), JsonSerializer.Serialize(topology, Json));
                results.Add(new
                {
                    Source = source.Clone(),
                    Supported = true,
                    SourceSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant(),
                    Elements = topology.Elements.Length,
                    Focal = beforeFocal,
                    FNumber = beforeF,
                    HalfField = FieldCoordinates.MaximumRadius(final.Fields),
                    CoreStopRadius = radius,
                    OriginalAiming = snapshot.RayAimingEnabled,
                    PreparedAiming = true,
                    SourceSnapshotFingerprint = ContentFingerprint.Compute(snapshot),
                    PreparedFingerprint = compiled.OpticFingerprint,
                    Changes = "Freeze editor merit/variable/solve and OPD-reference state; original radii/material/environment/coordinates/fields/spectrum retained. Core-calibrated stop-sized pupil, circular physical face limits from original semi-diameters, aiming enabled. This is an explicit physical evaluation variant, not an unmodified native import."
                });
                Console.WriteLine($"prepared {source.GetProperty("Name").GetString()}: {topology.Elements.Length} elements, F/{beforeF:F3}, half field {FieldCoordinates.MaximumRadius(final.Fields):F2}");
            }
            catch (Exception error) when (error is IOException or ArgumentException or ArithmeticException or InvalidOperationException or NotSupportedException)
            { results.Add(new { Source = source.Clone(), Supported = false, Error = error.Message, Code = error is TopologyValidationException validation ? validation.Code.ToString() : error.GetType().Name }); }
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "preparation.json"), JsonSerializer.Serialize(results, Json));
        return 0;
    }
    public static async Task<int> Inventory(string[] args)
    {
        if (args.Length != 2 || File.Exists(args[1])) throw new ArgumentException("--reduction-inventory <inventory input> <new output file>");
        using var input = JsonDocument.Parse(await File.ReadAllBytesAsync(args[0]));
        var entries = new List<object>();
        foreach (var item in input.RootElement.GetProperty("Candidates").EnumerateArray())
        {
            var path = item.GetProperty("RepositoryPath").GetString()!;
            try
            {
                var project = await StarOptProjectStore.LoadAsync(path);
                var optic = project.Configurations[project.ActiveConfigurationIndex]; var snapshot = optic.ToSnapshot();
                var surfaces = optic.SurfaceGroup.Items; var stop = surfaces.Single(surface => surface.IsStop);
                string? code = null, message = null; DesignTopology? topology = null;
                try { topology = TopologyCompiler.Import(snapshot).Topology; }
                catch (TopologyValidationException error) { code = error.Code.ToString(); message = error.Message; }
                entries.Add(new
                {
                    Source = item.Clone(),
                    DirectImport = topology is not null,
                    Code = code,
                    Message = message,
                    Topology = topology,
                    Focal = optic.Paraxial.EstimateEffectiveFocalLength(),
                    FNumber = optic.Paraxial.EstimateFNumber(),
                    HalfField = FieldCoordinates.MaximumRadius(optic.Fields),
                    IndependentStop = stop.MaterialBefore is AirMaterial && stop.MaterialAfter is AirMaterial && stop.Geometry is PlaneGeometry,
                    CementedInterfaces = surfaces.Count(surface => surface.MaterialBefore is CatalogGlassMaterial && surface.MaterialAfter is CatalogGlassMaterial),
                    Infinite = double.IsPositiveInfinity(snapshot.Surfaces[0].Thickness),
                    snapshot.Aperture,
                    snapshot.FieldDefinition,
                    snapshot.RayAimingEnabled,
                    Pickups = (snapshot.RadiusPickups?.Count ?? 0) + (snapshot.ThicknessPickups?.Count ?? 0) + (snapshot.SemiDiameterPickups?.Count ?? 0),
                    MeritRows = snapshot.MeritOperands?.Count ?? 0,
                    snapshot.ReferenceOpd,
                    Surfaces = surfaces.Select((surface, index) => new
                    {
                        Index = index,
                        surface.IsStop,
                        Geometry = surface.Geometry.GetType().Name,
                        Before = surface.MaterialBefore.Name,
                        After = surface.MaterialAfter.Name,
                        surface.SemiDiameter,
                        PhysicalAperture = surface.PhysicalAperture?.GetType().Name
                    }).ToArray()
                });
            }
            catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
            { entries.Add(new { Source = item.Clone(), LoadFailure = error.Message }); }
        }
        await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(entries, Json));
        Console.WriteLine($"Inspected {entries.Count} catalog candidates without tracing or training.");
        return 0;
    }
}
