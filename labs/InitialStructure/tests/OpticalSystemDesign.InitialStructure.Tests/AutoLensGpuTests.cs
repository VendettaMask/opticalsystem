using System.Diagnostics;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using Xunit.Abstractions;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class AutoLensCudaFactAttribute : FactAttribute
{
    public AutoLensCudaFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_GPU_VALIDATION") != "1")
            Skip = "Requires explicit CUDA hardware validation.";
    }
}

public sealed class AutoLensCudaTheoryAttribute : TheoryAttribute
{
    public AutoLensCudaTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_GPU_VALIDATION") != "1")
            Skip = "Requires explicit CUDA hardware validation.";
    }
}

public sealed class AutoLensGpuTests(ITestOutputHelper output)
{
    [AutoLensCudaTheory]
    [InlineData(2)]
    [InlineData(5)]
    public void GeneratedMultiElementSeedEveryGpuHitMatchesCpu(int order)
    {
        var original=AutoLensManagedTests.Specification(16);
        var spec=original with {FlatStart=original.FlatStart! with {AutoLens=original.FlatStart.AutoLens! with {EvenAsphereOrder=order}}};
        var seed=new FlatStartDesignService().Solve(spec,3,allowedGlassNames:["N-BK7","N-F2","N-SF6"]).AutoLensState!;
        var optic=Optic.FromSnapshot(seed.Optic);
        var cpu=Trace(DesignComputeMode.Cpu); var gpu=Trace(DesignComputeMode.Gpu);
        for (var i=0;i<cpu.Length;i++)
        {
            Assert.NotNull(cpu[i]); Assert.NotNull(gpu[i]);
            var a=cpu[i]!.Value; var b=gpu[i]!.Value;
            Near(a.Position.X,b.Position.X,"X"); Near(a.Position.Y,b.Position.Y,"Y"); Near(a.Position.Z,b.Position.Z,"Z");
            Near(a.Direction.X,b.Direction.X,"L"); Near(a.Direction.Y,b.Direction.Y,"M"); Near(a.Direction.Z,b.Direction.Z,"N");
            void Near(double x,double y,string name) => Assert.True(Math.Abs(x-y)<=1e-9*Math.Max(1,Math.Abs(x)),
                $"Surface {i}, {name}: CPU {x:R}, GPU {y:R}; n {a.RefractiveIndexBefore:R}/{b.RefractiveIndexBefore:R} -> {a.RefractiveIndexAfter:R}/{b.RefractiveIndexAfter:R}");
        }
        OpticalSystemDesign.Core.Rays.RayTraceSampleValue?[] Trace(DesignComputeMode mode)
        {
            using var scope=new SequentialGpuSession(mode);
            var wave=optic.Wavelengths.Single(w=>w.IsPrimary).Micrometers;
            var bundle=optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0,.7,wave,[new(.3,.2,1)],aimAtStop:true);
            using var traced=optic.SequentialRayTracer.Trace(bundle,TraceRequest.Selected(Enumerable.Range(0,optic.SurfaceGroup.Items.Count)));
            return Enumerable.Range(0,optic.SurfaceGroup.Items.Count).Select(i=>traced.TryGetSample(0,i,out var hit) ? (OpticalSystemDesign.Core.Rays.RayTraceSampleValue?)hit : null).ToArray();
        }
    }

    [AutoLensCudaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealGpuSolvePersistsDeviceWorkAndRetainsIndependentPhysicalAcceptance(bool cemented)
    {
        Assert.True(DesignGpu.Status.IsAvailable, DesignGpu.Status.UnavailableReason);
        var original = AutoLensManagedTests.Specification(60);
        var specification = original with { FlatStart = original.FlatStart! with
        { AutoLens = original.FlatStart.AutoLens! with { ComputeMode = DesignComputeMode.Gpu, CurriculumIterations = 1,
            RefinementIterations = 1, CementedGaps = cemented ? [0] : [] } } };
        var timer = Stopwatch.StartNew();
        var result = new FlatStartDesignService().Solve(specification, 3, allowedGlassNames: ["N-BK7", "N-F2", "N-SF6"]);
        timer.Stop();
        var state = Assert.IsType<AutoLensManagedState>(result.AutoLensState);
        AutoLensManagedStateValidation.Validate(state);
        if (state.GpuBatches==0) Record("gpu-pilot-errors",result.Diagnostics);
        Assert.True(state.GpuBatches > 0);
        Assert.True(state.GpuRays > 0);
        Assert.Equal("cuda-reverse-tape-v1", state.LastComputeExecution!.Backend);
        Assert.Equal(0, state.TargetPolishEvaluationCount);
        Assert.NotEmpty(state.TrainingSteps);
        Assert.All(state.TrainingSteps, step => Assert.True(step.AfterLoss < step.BeforeLoss));
        var candidate = Assert.IsType<CandidateSnapshot>(result.Candidate);
        var fresh = new FlatStartDesignProblem(specification, 3, candidate.Optic)
            .Evaluate(Optic.FromSnapshot(candidate.Optic), FlatStartDesignProblem.FullStage, true, default, true);
        ComparePhysical(fresh,result.FinalValidation!);
        Assert.True(state.PropagationExecution!.AimRays>0);
        Assert.True(state.PropagationExecution.PropagatedRays>0);
        Assert.Equal(fresh.MeetsTargets, candidate.Status == CandidateStatus.LabAccepted);
        var jsonOptions = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var restored = JsonSerializer.Deserialize<AutoLensManagedState>(JsonSerializer.Serialize(state, jsonOptions), jsonOptions)!;
        AutoLensManagedStateValidation.Validate(restored);
        Assert.Equal(state.LastComputeExecution, restored.LastComputeExecution);
        Assert.Equal(state.GpuRays, restored.GpuRays);
        Assert.Throws<InvalidDataException>(() => AutoLensManagedStateValidation.Validate(restored with { GpuBatches = -1 }));
        Assert.Throws<InvalidDataException>(() => AutoLensManagedStateValidation.Validate(restored with
        { LastComputeExecution = restored.LastComputeExecution! with { Backend = "cpu-forward-v1" } }));
        var resumed = new FlatStartDesignService().ResumeAutoLens(specification, result.Bootstrap, restored, ["N-BK7", "N-F2", "N-SF6"]);
        Assert.Equal(state.GpuRays, resumed.AutoLensState!.GpuRays);
        Assert.Equal(state.Adam.Coordinates, resumed.AutoLensState.Adam.Coordinates);
        double? cpuMilliseconds = null;
        if (!cemented)
        {
            var cpuSpec = specification with { FlatStart = specification.FlatStart with
            { AutoLens = specification.FlatStart.AutoLens! with { ComputeMode = DesignComputeMode.Cpu } } };
            var cpuTimer = Stopwatch.StartNew();
            var cpu = new FlatStartDesignService().Solve(cpuSpec, 3, allowedGlassNames: ["N-BK7", "N-F2", "N-SF6"]);
            cpuTimer.Stop(); cpuMilliseconds = cpuTimer.Elapsed.TotalMilliseconds;
            Assert.NotNull(cpu.Candidate);
            Assert.Equal(cpu.FinalValidation!.MeetsTargets, fresh.MeetsTargets);
            var cpuRms = cpu.FinalValidation.Fields.Max(field => field.RmsRadiusMillimeters);
            var gpuRms = fresh.Fields.Max(field => field.RmsRadiusMillimeters);
            Assert.NotNull(cpuRms); Assert.NotNull(gpuRms);
            Assert.InRange(Math.Abs(cpuRms.Value - gpuRms.Value), 0, 1e-7);
        }
        Record(cemented ? "gpu-solve-cemented" : "gpu-solve", new { Device = DesignGpu.Status.DeviceName, ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds,
            CpuElapsedMilliseconds = cpuMilliseconds, FullSolveSpeedup = cpuMilliseconds / timer.Elapsed.TotalMilliseconds,
            result.EvaluationCount, state.GpuBatches, state.GpuRays, state.GpuMilliseconds, state.LastComputeExecution, state.PropagationExecution,
            Accepted = fresh.MeetsTargets, state.TrainingSteps });
    }

    [AutoLensCudaFact]
    public void BenchmarkSamePhysicalDesignOnCpuAndCuda()
    {
        Assert.True(DesignGpu.Status.IsAvailable, DesignGpu.Status.UnavailableReason);
        var original = AutoLensManagedTests.Specification(16);
        var spec = original with { FlatStart = original.FlatStart! with
        { AutoLens = original.FlatStart.AutoLens! with { EvenAsphereOrder = 5, CurriculumIterations = 1, RefinementIterations = 1 } } };
        var seed = new FlatStartDesignService().Solve(spec, 3, allowedGlassNames: ["N-BK7", "N-F2", "N-SF6"]).AutoLensState!;
        var optic = Optic.FromSnapshot(seed.Optic);
        var parameters = seed.Parameters;
        var pupils = ApertureSampler.GenerateGaussianQuadrature(3, 12).ToArray();
        double[] fields = [-1, -.5, 0, .5, 1];
        var cpu = Evaluate(DesignComputeMode.Cpu);
        var gpu = Evaluate(DesignComputeMode.Gpu);
        var maximumGradientError = 0.0;
        for (var field = 0; field < cpu.Fields.Count; field++)
        {
            Assert.InRange(Math.Abs(cpu.Fields[field].RmsRadiusMillimeters.Value-gpu.Fields[field].RmsRadiusMillimeters.Value),0,1e-9);
            for (var parameter = 0; parameter < parameters.Count; parameter++)
            {
                var a = cpu.Fields[field].RmsRadiusMillimeters.Derivative(parameter);
                var b = gpu.Fields[field].RmsRadiusMillimeters.Derivative(parameter);
                maximumGradientError = Math.Max(maximumGradientError, Math.Abs(a - b) / Math.Max(1, Math.Abs(a)));
            }
        }
        Assert.InRange(maximumGradientError, 0, 1e-6);
        var cpuTimes = new List<double>(); var gpuTimes = new List<double>();
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var timer = Stopwatch.StartNew(); Evaluate(DesignComputeMode.Cpu); cpuTimes.Add(timer.Elapsed.TotalMilliseconds);
            timer.Restart(); Evaluate(DesignComputeMode.Gpu); gpuTimes.Add(timer.Elapsed.TotalMilliseconds);
        }
        var cpuMedian = cpuTimes.Order().ElementAt(1); var gpuMedian = gpuTimes.Order().ElementAt(1);
        Record("gpu-benchmark", new { Device = DesignGpu.Status.DeviceName, Parameters = parameters.Count, Fields = fields.Length,
            Pupils = pupils.Length, CpuMilliseconds = cpuTimes, GpuMilliseconds = gpuTimes, CpuMedianMilliseconds = cpuMedian,
            GpuMedianMilliseconds = gpuMedian, Speedup = cpuMedian / gpuMedian, MaximumNormalizedGradientError = maximumGradientError,
            GpuExecution = gpu.Execution, Scope = "One identical optical-gradient evaluation including selected-backend aiming and formal roots; independent physical acceptance and complete search are not timed here." });

        DifferentiableOpticalResult Evaluate(DesignComputeMode mode) =>
            DifferentiableOpticalMetrics.Evaluate(optic, parameters, fields, pupils, computeMode: mode);
    }

    [AutoLensCudaFact]
    public void BenchmarkWarmedCompleteSolveWithoutTimingKernelInitialization()
    {
        Assert.True(DesignGpu.Status.IsAvailable, DesignGpu.Status.UnavailableReason);
        var original = AutoLensManagedTests.Specification(60);
        var specification = original with { FlatStart = original.FlatStart! with
        { AutoLens = original.FlatStart.AutoLens! with { CurriculumIterations = 1, RefinementIterations = 1 } } };
        Solve(DesignComputeMode.Cpu); Solve(DesignComputeMode.Gpu);
        var cpuTimes = new List<double>(); var gpuTimes = new List<double>();
        for (var repeat = 0; repeat < 3; repeat++)
        {
            // Alternate order to avoid consistently favoring the second warmed execution.
            foreach (var mode in repeat % 2 == 0 ? new[] { DesignComputeMode.Cpu, DesignComputeMode.Gpu }
                : new[] { DesignComputeMode.Gpu, DesignComputeMode.Cpu })
            {
                var timer = Stopwatch.StartNew(); var result = Solve(mode); timer.Stop();
                (mode == DesignComputeMode.Cpu ? cpuTimes : gpuTimes).Add(timer.Elapsed.TotalMilliseconds);
                Assert.Equal(0, result.AutoLensState!.TargetPolishEvaluationCount);
                if (mode == DesignComputeMode.Gpu)
                {
                    Assert.True(result.AutoLensState.GpuRays > 0);
                    Assert.True(result.AutoLensState.PropagationExecution!.PropagatedRays>0);
                    Record("gpu-warmed-solve-work",result.AutoLensState.PropagationExecution);
                }
            }
        }
        var cpuMedian = cpuTimes.Order().ElementAt(1); var gpuMedian = gpuTimes.Order().ElementAt(1);
        Record("gpu-warmed-solve", new { Device = DesignGpu.Status.DeviceName,
            CpuMilliseconds = cpuTimes, GpuMilliseconds = gpuTimes, CpuMedianMilliseconds = cpuMedian,
            GpuMedianMilliseconds = gpuMedian, Speedup = cpuMedian / gpuMedian,
            Scope = "Same three-element short complete solve (curriculum 1, refinement 1), including dense independent physical acceptance on the selected backend; both paths warmed before three alternating timings. Not a multi-trial search or universal speedup." });

        FlatStartDesignResult Solve(DesignComputeMode mode) => new FlatStartDesignService().Solve(
            specification with { FlatStart = specification.FlatStart with
            { AutoLens = specification.FlatStart.AutoLens! with { ComputeMode = mode } } },
            3, allowedGlassNames: ["N-BK7", "N-F2", "N-SF6"]);
    }

    [AutoLensCudaFact]
    public async Task BenchmarkCompleteMultiStartSearchWithTheSameBudgetAndSchedule()
    {
        var original=AutoLensManagedTests.Specification(160);
        var specification=original with {Budget=original.Budget with {InitialSeedCount=2,MaximumParallelism=1},
            FlatStart=original.FlatStart! with {AutoLens=original.FlatStart.AutoLens! with {CurriculumIterations=2,RefinementIterations=3}}};
        var options=new FlatStartSearchOptions {MaximumEvaluationsPerTrial=60,AllowedGlassNames=["N-BK7","N-F2","N-SF6"]};
        await Run(DesignComputeMode.Cpu); await Run(DesignComputeMode.Gpu);
        var cpuTimes=new List<double>();var gpuTimes=new List<double>();
        var cpuWork=new List<object>();var gpuWork=new List<object>();
        for (var repeat=0;repeat<3;repeat++)
        {
            FlatStartSearchResult? cpu=null,gpu=null;
            foreach (var mode in repeat%2==0 ? new[]{DesignComputeMode.Cpu,DesignComputeMode.Gpu} : new[]{DesignComputeMode.Gpu,DesignComputeMode.Cpu})
            {
                var timer=Stopwatch.StartNew();var result=await Run(mode);timer.Stop();
                (mode==DesignComputeMode.Cpu ? cpuTimes : gpuTimes).Add(timer.Elapsed.TotalMilliseconds);
                var record=new {result.Checkpoint.ChargedEvaluations,Trials=result.Checkpoint.Trials.Count,
                    GpuAimRays=result.Checkpoint.Trials.Sum(t=>t.AutoLensState?.PropagationExecution?.AimRays??0),
                    GpuTraceRays=result.Checkpoint.Trials.Sum(t=>t.AutoLensState?.PropagationExecution?.PropagatedRays??0),
                    CpuRecoveryRays=result.Checkpoint.Trials.Sum(t=>t.AutoLensState?.PropagationExecution?.CpuRecoveryRays??0)};
                (mode==DesignComputeMode.Cpu ? cpuWork : gpuWork).Add(record);
                if (mode==DesignComputeMode.Cpu) cpu=result;
                else {gpu=result;Assert.True(record.GpuTraceRays>0);}
                FlatStartCheckpointValidation.Validate(result.Checkpoint);
            }
            Assert.Equal(cpu!.Checkpoint.ChargedEvaluations,gpu!.Checkpoint.ChargedEvaluations);
            Assert.Equal(cpu.Checkpoint.Trials.Select(t=>t.Operation),gpu.Checkpoint.Trials.Select(t=>t.Operation));
            Assert.Equal(cpu.Checkpoint.Trials.Select(t=>t.CompletedEvaluations),gpu.Checkpoint.Trials.Select(t=>t.CompletedEvaluations));
        }
        var cpuMedian=cpuTimes.Order().ElementAt(1);var gpuMedian=gpuTimes.Order().ElementAt(1);
        Record("gpu-full-search",new {Device=DesignGpu.Status.DeviceName,CpuMilliseconds=cpuTimes,GpuMilliseconds=gpuTimes,
            CpuMedianMilliseconds=cpuMedian,GpuMedianMilliseconds=gpuMedian,Speedup=cpuMedian/gpuMedian,CpuWork=cpuWork,GpuWork=gpuWork,
            Scope="Desktop search service: two initial roots, budget 160, per-trial cap 60, one worker, fixed random seed and identical curriculum/refinement limits. Three warmed alternating complete searches, including planning, screening/refinement, physical validation and candidate selection; no UI rendering or file-write timing."});

        Task<FlatStartSearchResult> Run(DesignComputeMode mode)=>new FlatStartSearchService().RunAsync(specification with
            {FlatStart=specification.FlatStart with {AutoLens=specification.FlatStart.AutoLens! with {ComputeMode=mode}}},options);
    }

    private static void ComparePhysical(DesignEvaluation cpu,DesignEvaluation gpu)
    {
        Assert.Equal(cpu.MeetsTargets,gpu.MeetsTargets); Assert.Equal(cpu.Fields.Count,gpu.Fields.Count);
        Assert.Equal(cpu.Violations.Select(v=>v.Code),gpu.Violations.Select(v=>v.Code));
        for (var i=0;i<cpu.Fields.Count;i++)
        {
            Assert.Equal(cpu.Fields[i].ValidRays,gpu.Fields[i].ValidRays);
            Assert.Equal(cpu.Fields[i].CheckValidRays,gpu.Fields[i].CheckValidRays);
            Assert.InRange(Math.Abs(cpu.Fields[i].RmsRadiusMillimeters!.Value-gpu.Fields[i].RmsRadiusMillimeters!.Value),0,1e-8);
        }
    }

    private void Record(string name, object record)
    {
        var text = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(text);
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_GPU_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), text);
    }
}
