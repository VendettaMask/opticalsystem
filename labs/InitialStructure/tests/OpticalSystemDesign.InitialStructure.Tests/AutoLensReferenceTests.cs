using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;
using OpticalSystemDesign.InitialStructure.Persistence;
using Xunit.Abstractions;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class AutoLensReferenceTests(ITestOutputHelper output)
{
    private static readonly JsonElement Fixture = ReadFixture();
    private static JsonElement ReadFixture()
    {
        using var stream = typeof(AutoLensReferenceTests).Assembly.GetManifestResourceStream(
            "OpticalSystemDesign.InitialStructure.Tests.Fixtures.autolens-e58-policy.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
    internal static InitialStructureSpecification Small(int budget = 100) => AutoLensReferenceExamples.Mobile().Specification is var source
        ? source with
        {
            Name = "Reference rule verification",
            MinimumElementCount = 1,
            MaximumElementCount = 1,
            FlatStart = source.FlatStart! with
            {
                AutoLens = source.FlatStart.AutoLens! with
                {
                    ComputeMode = DesignComputeMode.Cpu,
                    CurriculumIterations = 4,
                    RefinementIterations = 4,
                    TrainingSampling = new()
                    {
                        CurriculumRings = 2,
                        CurriculumRays = 4,
                        RefinementRings = 2,
                        RefinementArms = 2,
                        RefinementRays = 4,
                        RegularizationRings = 2,
                        RegularizationRays = 4,
                        ShapeRings = 2,
                        ShapeRays = 8,
                        FieldScanRings = 2,
                        FieldScanRays = 16
                    }
                }
            },
            Budget = source.Budget with { MaximumEvaluations = budget, TimeLimit = TimeSpan.FromMinutes(3) }
        } : throw new InvalidOperationException();

    [Fact]
    public void ReferenceDefaultIsPinnedMobileProtocolAndCatalog()
    {
        var preset = AutoLensReferenceExamples.Mobile();
        SpecificationValidator.Validate(preset.Specification);
        Assert.Equal(5, preset.Specification.EffectiveFocalLengthMillimeters);
        Assert.Equal(1.8, preset.Specification.FNumber);
        Assert.Equal(45, preset.Specification.MaximumFieldAngleDegrees);
        Assert.Equal(9, preset.Specification.FlatStart!.AutoLens!.EvenAsphereOrder);
        Assert.Equal(new[] { 656.0, 587, 486 }, preset.Specification.Wavelengths.Select(wave => wave.Nanometers));
        Assert.Equal(61, preset.Options.AllowedGlassNames.Count);
        var preflight = FlatStartSearchService.Preflight(preset.Specification, preset.Options);
        Assert.Equal(61, preflight.UsableGlassNames.Count);
        Assert.DoesNotContain(preflight.Diagnostics, item => item.Code == "search.glass-excluded");
        var plan = DesignFormSearch.Preview(preset.Specification, preset.Options);
        Assert.Equal(0, plan.DistinctSignForms);
        Assert.Equal(0, plan.NeighborhoodEvaluationsReserved);
        Assert.Equal(6000, plan.ScreeningEvaluationsPerRoot);
    }

    [Fact]
    public void PythonNumpyAndTorchStartupStreamsReproduceOriginalStartingPrescriptions()
    {
        foreach (var expected in Fixture.GetProperty("initialCases").EnumerateArray())
        {
            var source = AutoLensReferenceExamples.Mobile().Specification;
            var spec = source with { Budget = source.Budget with { RandomSeed = expected.GetProperty("seed").GetInt64() } };
            var optic = AutoLensReferenceStart.Create(spec, 6, null, AutoLensReferenceMaterials.ProjectionNames);
            for (var index = 0; index < expected.GetProperty("surfaces").GetArrayLength(); index++)
            {
                var original = expected.GetProperty("surfaces")[index];
                var actual = optic.SurfaceGroup.Items[index + 1];
                Close(original.GetProperty("d").GetDouble(), actual.CoordinateSystem.Origin.Z - 20000, 2e-10);
                Close(original.GetProperty("curvature").GetDouble(), actual.Radius == 0 ? 0 : 1 / actual.Radius);
                Close(original.GetProperty("radius").GetDouble(), actual.SemiDiameter, 2e-10);
                if (actual.MaterialAfter is AirMaterial) continue;
                var material = OpticalDesignParameters.RelaxedGlassCoordinates(actual.MaterialAfter);
                Close(original.GetProperty("nd").GetDouble(), material.Nd);
                Close(original.GetProperty("abbe").GetDouble(), material.Vd, 2e-10);
            }
        }
    }

    [Fact]
    public void UpstreamJsonHandoffRebuildsEveryPositionFromRoundedGaps()
    {
        using var stream = typeof(AutoLensReferenceTests).Assembly.GetManifestResourceStream(
            "OpticalSystemDesign.InitialStructure.Tests.Fixtures.autolens-e58-handoff.json")!;
        using var document = JsonDocument.Parse(stream);
        foreach (var sample in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = sample.GetProperty("input");
            var original = input.GetProperty("surfaces").EnumerateArray().ToArray();
            var expected = sample.GetProperty("reloaded");
            var optic = new Optic("Upstream isolated JSON handoff");
            AutoLensReferenceMaterials.Register(optic.Materials);
            var surfaces = new List<OpticalSurface>
            {
                new() { Label = "Object", Material = "Air", Thickness = AutoLensReferenceStart.ObjectDistance + original[0].GetProperty("d").GetDouble() }
            };
            for (var index = 0; index < original.Length; index++)
            {
                var next = index + 1 < original.Length ? original[index + 1].GetProperty("d").GetDouble() : input.GetProperty("sensor").GetDouble();
                surfaces.Add(new()
                {
                    Material = index == 1 ? "AutoLensE58:h-k9l" : "Air",
                    IsStop = index == 0,
                    SemiDiameter = original[index].GetProperty("radius").GetDouble(),
                    Thickness = next - original[index].GetProperty("d").GetDouble()
                });
            }
            surfaces.Add(new() { Label = "Image", Material = "Air" });
            optic.SurfaceGroup.ImportLegacySurfaces(surfaces, optic.Materials);
            for (var index = 1; index < original.Length; index++)
            {
                var curvature = original[index].GetProperty("curvature").GetDouble();
                optic.SurfaceGroup.Items[index + 1].Geometry = new EvenAsphereGeometry(curvature == 0 ? 0 : 1 / curvature,
                    original[index].GetProperty("conic").GetDouble(),
                    new[] { 0.0 }.Concat(original[index].GetProperty("coefficients").EnumerateArray().Select(value => value.GetDouble())).ToArray());
            }
            AutoLensReferenceHandoff.Apply(optic, AutoLensReferenceStart.ObjectDistance);
            for (var index = 0; index < original.Length; index++)
            {
                var target = expected.GetProperty("surfaces")[index];
                var surface = optic.SurfaceGroup.Items[index + 1];
                Close(target.GetProperty("d").GetDouble(), surface.CoordinateSystem.Origin.Z - AutoLensReferenceStart.ObjectDistance, 2e-10);
                Close(target.GetProperty("radius").GetDouble(), surface.SemiDiameter);
                if (index == 0) continue;
                var profile = OpticalDesignParameters.Profile(surface.Geometry);
                Close(target.GetProperty("curvature").GetDouble(), profile.Curvature);
                Close(target.GetProperty("conic").GetDouble(), profile.Conic);
                Assert.Equal(0, profile.Coefficients[0]);
                for (var coefficient = 0; coefficient < target.GetProperty("coefficients").GetArrayLength(); coefficient++)
                    Close(target.GetProperty("coefficients")[coefficient].GetDouble(), profile.Coefficients[coefficient + 1]);
            }
            Close(expected.GetProperty("sensor").GetDouble(), optic.SurfaceGroup.Items[^1].CoordinateSystem.Origin.Z - AutoLensReferenceStart.ObjectDistance, 2e-10);
            // The fixture must expose the original defect, not merely use already-rounded positions.
            Assert.True(Math.Abs(expected.GetProperty("surfaces")[2].GetProperty("d").GetDouble()
                - Math.Round(original[2].GetProperty("d").GetDouble(), 4)) > 9e-5);
        }
    }

    [Fact]
    public void UpstreamFrozenImageLossAndDetachedGradientsAgree()
    {
        foreach (var expected in Fixture.GetProperty("imageCases").EnumerateArray())
        {
            var fields = Fixture.GetProperty("imageInputs").EnumerateArray().Select(field =>
            {
                var input = new FixedInputRayField(field.GetProperty("ring").GetInt32(), field.GetProperty("arm").GetInt32(),
                    field.GetProperty("wave").GetInt32(), field.GetProperty("ideal")[0].GetDouble(), field.GetProperty("ideal")[1].GetDouble(), []);
                var rays = field.GetProperty("rays").EnumerateArray().Select(ray => new FixedInputSpotRay(
                    Linear(ray.GetProperty("x").GetDouble(), ray.GetProperty("gx")),
                    Linear(ray.GetProperty("y").GetDouble(), ray.GetProperty("gy")), 1, 0, ray.GetProperty("valid").GetBoolean())).ToArray();
                return new FixedInputSpotField(input, rays);
            }).ToArray();
            var actual = AutoLensReferenceLoss.Image(new(fields, DesignComputeExecution.Cpu), expected.GetProperty("centroid").GetBoolean());
            Close(expected.GetProperty("total").GetDouble(), actual.Value, 2e-11);
            for (var index = 0; index < 2; index++) Close(expected.GetProperty("gradient")[index].GetDouble(), actual.Derivative(index), 2e-10);
        }
        static DifferentialScalar Linear(double value, JsonElement gradient) => value
            + DifferentialScalar.Variable(0, 2, 0) * gradient[0].GetDouble() + DifferentialScalar.Variable(0, 2, 1) * gradient[1].GetDouble();
    }

    [Fact]
    public void UpstreamFrozenLinearBoundsProfilesMaterialsAndGradientsAgree()
    {
        foreach (var expected in Fixture.GetProperty("geometryCases").EnumerateArray())
        {
            var spec = Small();
            var optic = AutoLensReferenceStart.Create(spec, 1, null, AutoLensReferenceMaterials.ProjectionNames);
            optic.SurfaceGroup.Items[1].Thickness = .08;
            optic.SurfaceGroup.Items[2].Thickness = .3;
            optic.SurfaceGroup.Items[3].Thickness = 5.7;
            optic.SurfaceGroup.Items[2].Geometry = new EvenAsphereGeometry(1 / expected.GetProperty("curvature").GetDouble(), -.2, [0, 1e-5, 0, 0, 0, 0, 0, 0, 0]);
            optic.SurfaceGroup.Items[3].Geometry = new EvenAsphereGeometry(1 / -.29, .1, [0, -2e-5, 0, 0, 0, 0, 0, 0, 0]);
            optic.SurfaceGroup.Items[2].MaterialAfter = OpticalDesignParameters.CreateRelaxedGlass("Fixture", 1.95, 25);
            RotationalDesignGeometry.SetRadius(optic, 1, 1.4);
            RotationalDesignGeometry.SetRadius(optic, 2, 2.2);
            RotationalDesignGeometry.SetRadius(optic, 3, 2);
            OpticalDesignParameters.SynchronizeIncidentMedia(optic);
            optic.SurfaceGroup.Renumber();
            OpticalDesignParameter Position(int index) => new(index, OpticalDesignParameterKind.AxialPosition) { OriginOffset = 20000 };
            OpticalDesignParameter[] parameters = [Position(2), new(2, OpticalDesignParameterKind.Curvature), new(2, OpticalDesignParameterKind.Conic),
                new(2, OpticalDesignParameterKind.EvenAsphereCoefficient, CoefficientIndex: 1), new(2, OpticalDesignParameterKind.MaterialIndex),
                new(2, OpticalDesignParameterKind.MaterialAbbe), Position(3), new(3, OpticalDesignParameterKind.Curvature),
                new(3, OpticalDesignParameterKind.Conic), new(3, OpticalDesignParameterKind.EvenAsphereCoefficient, CoefficientIndex: 1), Position(4)];
            var actual = AutoLensReferenceLoss.Geometry(optic, parameters, compact: true, optimizeMaterials: true);
            Close(expected.GetProperty("total").GetDouble(), actual.Value, 5e-10);
            for (var index = 0; index < parameters.Length; index++)
                Close(expected.GetProperty("gradient")[index].GetDouble(), actual.Derivative(index), 5e-9);
        }
    }

    [Fact]
    public void UpstreamFrozenAdamMomentsAndBothSchedulesAgreeIncludingNonFiniteSanitization()
    {
        double[][] gradients = [[2, -1], [-3, .5], [double.NaN, double.PositiveInfinity], [.1, -.2], [0, -4]];
        foreach (var expected in Fixture.GetProperty("adamCases").EnumerateArray())
        {
            var curriculum = expected.GetProperty("curriculum").GetBoolean();
            var state = DifferentiableAdam.Initialize([.3, 2]);
            for (var iteration = 0; iteration < gradients.Length; iteration++)
            {
                var record = expected.GetProperty("records")[iteration];
                var rate = 1e-3 * DifferentiableAdam.ReferenceSchedule(iteration, curriculum ? 8 : 3000, curriculum);
                Close(record.GetProperty("rate").GetDouble(), rate);
                state = DifferentiableAdam.ReferenceStep(state, gradients[iteration], [rate, rate]);
                for (var slot = 0; slot < 2; slot++)
                {
                    Close(record.GetProperty("coordinates")[slot].GetDouble(), state.Coordinates[slot]);
                    Close(record.GetProperty("first")[slot].GetDouble(), state.FirstMoment[slot]);
                    Close(record.GetProperty("second")[slot].GetDouble(), state.SecondMoment[slot]);
                }
            }
        }
        Close(1, DifferentiableAdam.ReferenceSchedule(100, 3000, false));
        Close(.1, DifferentiableAdam.ReferenceSchedule(3000, 3000, false));
    }

    [Fact]
    public void PinnedRealDispersionAndNominalMaterialCoordinatesAgree()
    {
        var registry = new MaterialRegistry();
        AutoLensReferenceMaterials.Register(registry);
        foreach (var expected in Fixture.GetProperty("materials").EnumerateArray())
        {
            var material = registry.Resolve("AutoLensE58:" + expected.GetProperty("name").GetString());
            Assert.IsType<CatalogGlassMaterial>(material);
            var nominal = AutoLensReferenceMaterials.NominalCoordinates(material);
            Close(expected.GetProperty("nd").GetDouble(), nominal.Index);
            Close(expected.GetProperty("abbe").GetDouble(), nominal.Abbe);
            var wavelengths = new[] { 486.0, 587, 656 };
            for (var index = 0; index < wavelengths.Length; index++) Close(expected.GetProperty("indices")[index].GetDouble(), material.RefractiveIndex(wavelengths[index]));
        }
        var source = OpticalDesignParameters.CreateRelaxedGlass("Relaxed", 1.5168, 64.2);
        var match = AutoLensReferenceMaterials.Match(registry, source, AutoLensReferenceMaterials.ProjectionNames, [486, 587, 656]);
        Assert.Equal("AutoLensE58:h-k9l", match.Material.Name);
    }

    [Fact]
    public void AbsolutePositionsDoNotMoveDownstreamParametersAndRatesUsePhysicalUnits()
    {
        var optic = AutoLensReferenceStart.Create(Small(), 1, null, AutoLensReferenceMaterials.ProjectionNames);
        var parameters = AutoLensReferenceStart.Parameters(optic, Small(), materials: true);
        var point = OpticalDesignParameters.Read(optic, parameters);
        var slot = parameters.ToList().FindIndex(parameter => parameter.SurfaceIndex == 2 && parameter.Kind == OpticalDesignParameterKind.AxialPosition);
        var before = optic.SurfaceGroup.Items.Select(surface => surface.CoordinateSystem.Origin.Z).ToArray();
        point[slot] += .02;
        var updated = OpticalDesignParameters.Apply(optic.ToSnapshot(), parameters, point);
        Close(before[2] + .02, updated.SurfaceGroup.Items[2].CoordinateSystem.Origin.Z);
        Close(before[3], updated.SurfaceGroup.Items[3].CoordinateSystem.Origin.Z);
        Assert.DoesNotContain(parameters, parameter => parameter.Kind == OpticalDesignParameterKind.Thickness);
        foreach (var parameter in parameters)
        {
            var rate = AutoLensReferenceStart.LearningRate(optic, parameter);
            if (parameter.Kind == OpticalDesignParameterKind.Curvature) Close(1e-3, rate);
            if (parameter.Kind == OpticalDesignParameterKind.AxialPosition) Close(1e-4, rate);
            if (parameter.Kind == OpticalDesignParameterKind.EvenAsphereCoefficient)
                Close(1e-4, rate * Math.Pow(optic.SurfaceGroup.Items[parameter.SurfaceIndex].SemiDiameter, 2 * (parameter.CoefficientIndex + 1)));
        }
    }

    [Fact]
    public void FrozenFiniteInputDerivativesMatchFormalCorePerturbationsAndGpu()
    {
        var spec = Small();
        var optic = AutoLensReferenceStart.Create(spec, 1, null, AutoLensReferenceMaterials.ProjectionNames);
        var parameters = AutoLensReferenceStart.Parameters(optic, spec, materials: true);
        var inputs = FixedInputOpticalMetrics.SampleRingArm(optic, .05, 5, 2, 1, 3, .2, 42);
        var actual = FixedInputOpticalMetrics.Evaluate(optic, parameters, inputs, Math.Cos(20 * Math.PI / 180), DesignComputeMode.Cpu);
        Assert.All(actual.Fields.SelectMany(field => field.Rays), ray => Assert.True(ray.Valid));
        var point = OpticalDesignParameters.Read(optic, parameters);
        foreach (var slot in Enumerable.Range(0, parameters.Count).Where(index => parameters[index].Kind is OpticalDesignParameterKind.Curvature or OpticalDesignParameterKind.AxialPosition or OpticalDesignParameterKind.MaterialIndex))
        {
            const double step = 1e-5;
            var plus = point.ToArray(); plus[slot] += step;
            var minus = point.ToArray(); minus[slot] -= step;
            var positive = Nominal(OpticalDesignParameters.Apply(optic.ToSnapshot(), parameters, plus));
            var negative = Nominal(OpticalDesignParameters.Apply(optic.ToSnapshot(), parameters, minus));
            for (var field = 0; field < inputs.Fields.Count; field++)
                for (var ray = 0; ray < inputs.Fields[field].Rays.Count; ray++)
                {
                    var derivative = (positive[field][ray] - negative[field][ray]) / (2 * step);
                    Close(derivative, actual.Fields[field].Rays[ray].X.Derivative(slot), 3e-6);
                }
        }
        if (DesignGpu.Status.IsAvailable)
        {
            var gpu = FixedInputOpticalMetrics.Evaluate(optic, parameters, inputs, Math.Cos(20 * Math.PI / 180), DesignComputeMode.Gpu);
            Assert.True(gpu.Execution.GpuBatches > 0);
            Assert.Equal(inputs.Fields.Sum(field => field.Rays.Count), gpu.Execution.GpuRays);
            output.WriteLine(JsonSerializer.Serialize(gpu.Execution));
            for (var field = 0; field < inputs.Fields.Count; field++)
                for (var ray = 0; ray < inputs.Fields[field].Rays.Count; ray++)
                {
                    Close(actual.Fields[field].Rays[ray].X.Value, gpu.Fields[field].Rays[ray].X.Value);
                    for (var slot = 0; slot < parameters.Count; slot++)
                        Close(actual.Fields[field].Rays[ray].X.Derivative(slot), gpu.Fields[field].Rays[ray].X.Derivative(slot), 1e-8);
                }
        }
        double[][] Nominal(Optic value) => inputs.Fields.Select(field =>
        {
            using var trace = value.SequentialRayTracer.Trace(new RealRayBundle(field.Rays), TraceRequest.FinalOnly(false));
            return trace.GetSurfaceSamples(value.SurfaceGroup.Items.Count - 1).Select(sample => sample!.Value.Position.X).ToArray();
        }).ToArray();
    }

    [Fact]
    public void DerivativeWorkingSetIsBoundedBeforeTracingOrDeviceAllocation()
    {
        var spec = AutoLensReferenceExamples.Mobile().Specification;
        var optic = AutoLensReferenceStart.Create(spec, 6, null, AutoLensReferenceMaterials.ProjectionNames);
        var parameters = AutoLensReferenceStart.Parameters(optic, spec, true);
        var ray = new RealRay(new(0, 0, 0), new(0, 0, 1), 587);
        var reused = Enumerable.Repeat(ray, 4096).ToArray();
        var fields = Enumerable.Range(0, 64).Select(index => new FixedInputRayField(index, 0, 0, 0, 0, reused)).ToArray();
        Assert.Throws<ArgumentException>(() => FixedInputOpticalMetrics.Evaluate(optic, parameters, new(fields), .94, DesignComputeMode.Cpu));
    }

    [AutoLensCudaFact]
    public void DirectFiniteInputCudaMatchesAllCpuOutputsAndGradientsAcrossRepeatedEvaluations()
    {
        Assert.True(DesignGpu.Status.IsAvailable, DesignGpu.Status.UnavailableReason);
        var spec = Small();
        var optic = AutoLensReferenceStart.Create(spec, 1, null, AutoLensReferenceMaterials.ProjectionNames);
        var parameters = AutoLensReferenceStart.Parameters(optic, spec, materials: true);
        var inputs = FixedInputOpticalMetrics.SampleRingArm(optic, .05, 5, 2, 1, 96, .2, 42);
        var coordinates = OpticalDesignParameters.Read(optic, parameters);
        for (var index = 0; index < parameters.Count; index++)
        {
            if (parameters[index].Kind == OpticalDesignParameterKind.Conic) coordinates[index] = -.2;
            if (parameters[index].Kind == OpticalDesignParameterKind.EvenAsphereCoefficient && parameters[index].CoefficientIndex == 1)
                coordinates[index] = parameters[index].SurfaceIndex % 2 == 0 ? 1e-5 : -2e-5;
        }
        optic = OpticalDesignParameters.Apply(optic.ToSnapshot(), parameters, coordinates);
        var cpu = FixedInputOpticalMetrics.Evaluate(optic, parameters, inputs, .94, DesignComputeMode.Cpu);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var gpu = FixedInputOpticalMetrics.Evaluate(optic, parameters, inputs, .94, DesignComputeMode.Gpu);
            Assert.True(gpu.Execution.GpuBatches > 0);
            Assert.Equal("cuda-fixed-input-forward-v1", gpu.Execution.Backend);
            Assert.Equal(inputs.Fields.Sum(field => field.Rays.Count), gpu.Execution.GpuRays);
            for (var field = 0; field < cpu.Fields.Count; field++)
                for (var ray = 0; ray < cpu.Fields[field].Rays.Count; ray++)
                {
                    var a = cpu.Fields[field].Rays[ray]; var b = gpu.Fields[field].Rays[ray];
                    Assert.Equal(a.Valid, b.Valid);
                    Check(a.X, b.X); Check(a.Y, b.Y); Check(a.DirectionZ, b.DirectionZ); Check(a.BendPenalty, b.BendPenalty);
                }
        }
        void Check(DifferentialScalar a, DifferentialScalar b)
        {
            Close(a.Value, b.Value, 2e-10);
            for (var parameter = 0; parameter < parameters.Count; parameter++) Close(a.Derivative(parameter), b.Derivative(parameter), 2e-8);
        }
    }

    [Fact]
    public void FinitePhysicalGpuTraceKeepsObjectPointAndDisclosesCpuDirectionAiming()
    {
        if (!DesignGpu.Status.IsAvailable) return;
        var spec = Small();
        var optic = AutoLensReferenceStart.Create(spec, 1, null, AutoLensReferenceMaterials.ProjectionNames);
        PupilSample[] pupils = [new(0, 0, 1), new(.3, 0, 1), new(-.3, 0, 1), new(0, .3, 1), new(0, -.3, 1)];
        SampledSpotResult cpu;
        using (var session = new SequentialGpuSession(DesignComputeMode.Cpu))
            cpu = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, .05, pupils, includeSurfaceTransmission: false);
        SampledSpotResult gpu;
        SequentialGpuExecution execution;
        using (var session = new SequentialGpuSession(DesignComputeMode.Gpu))
        {
            gpu = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, .05, pupils, includeSurfaceTransmission: false);
            execution = session.Execution;
        }
        Assert.NotNull(cpu.Metrics);
        Assert.NotNull(gpu.Metrics);
        Close(cpu.Metrics.RmsSpotRadius, gpu.Metrics.RmsSpotRadius, 2e-8);
        Assert.Equal(cpu.VignettedRayCount, gpu.VignettedRayCount);
        Assert.True(execution.CpuAimingRays > 0);
        Assert.Equal(0, execution.AimRays);
        Assert.True(execution.PropagatedRays > 0);
        output.WriteLine(JsonSerializer.Serialize(execution));
    }

    [Fact]
    public void BudgetPauseKeepsFullCourseAndSerializedCacheResumesIdentically()
    {
        var source = Small(3);
        var spec = source with { FlatStart = source.FlatStart! with { AutoLens = source.FlatStart.AutoLens! with { CurriculumIterations = 2000, RefinementIterations = 3000 } } };
        var service = new FlatStartDesignService();
        var paused = service.Solve(spec, 1);
        output.WriteLine(string.Join("\n", paused.Diagnostics.Select(item => item.Code + ": " + item.Message)));
        var state = paused.AutoLensState!;
        Assert.Equal(AutoLensManagedPhase.Curriculum, state.Phase);
        Assert.Equal(2000, state.PlannedCurriculumIterations);
        Assert.Equal(3000, state.PlannedRefinementIterations);
        Assert.Equal(1, state.Adam.Iteration);
        Assert.Null(paused.Candidate);
        Assert.NotNull(state.ReferenceSampling);
        AutoLensManagedStateValidation.Validate(state);
        var json = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var restored = JsonSerializer.Deserialize<AutoLensManagedState>(JsonSerializer.Serialize(state, json), json)!;
        var extended = spec with { Budget = spec.Budget with { MaximumEvaluations = 6 } };
        var resumed = service.ResumeAutoLens(extended, paused.Bootstrap, restored, AutoLensReferenceMaterials.ProjectionNames);
        var continuous = service.Solve(extended, 1);
        Assert.Equal(continuous.AutoLensState!.Adam.Coordinates, resumed.AutoLensState!.Adam.Coordinates);
        Assert.Equal(continuous.AutoLensState.Adam.FirstMoment, resumed.AutoLensState.Adam.FirstMoment);
        Assert.Equal(continuous.AutoLensState.Adam.SecondMoment, resumed.AutoLensState.Adam.SecondMoment);
        Assert.Equal(ContentFingerprint.Compute(continuous.AutoLensState.ReferenceSampling!), ContentFingerprint.Compute(resumed.AutoLensState.ReferenceSampling!));
        Assert.Equal(ContentFingerprint.Compute(continuous.AutoLensState.Optic), ContentFingerprint.Compute(resumed.AutoLensState.Optic));
    }

    [Fact]
    public async Task CancellationBeforeDispatchRefundsKnownZeroWorkAndPreservesPlan()
    {
        using var cancellation = new CancellationTokenSource();
        var spec = Small();
        var options = AutoLensReferenceExamples.Mobile().Options with { MaximumEvaluationsPerTrial = 100 };
        var result = await new FlatStartSearchService().RunAsync(spec, options, cancellation.Token, checkpointSink: (saved, _) =>
        {
            FlatStartCheckpointValidation.Validate(saved);
            if (saved.Trials.Any(trial => trial.State == FamilyTrialState.Reserved)) cancellation.Cancel();
            return ValueTask.CompletedTask;
        });
        Assert.Equal(FlatStartSearchState.Cancelled, result.Checkpoint.State);
        Assert.Empty(result.Checkpoint.Trials);
        Assert.Equal(0, result.Checkpoint.ChargedEvaluations);
        Assert.Equal(0, result.Checkpoint.NextRootIndex);
        FlatStartCheckpointValidation.Validate(result.Checkpoint);
    }

    [Fact]
    public async Task ResumeReservesWorkAtomicallyAndKeepsPreviousStateWhenCancelledBeforeDispatch()
    {
        var shortSpec = Small(3);
        var shortOptions = AutoLensReferenceExamples.Mobile().Options with { MaximumEvaluationsPerTrial = 3 };
        var paused = (await new FlatStartSearchService().RunAsync(shortSpec, shortOptions)).Checkpoint;
        var previous = Assert.Single(paused.Trials).AutoLensState!;
        var spec = shortSpec with { Budget = shortSpec.Budget with { MaximumEvaluations = 100 } };
        var options = shortOptions with { MaximumEvaluationsPerTrial = 100 };
        var checkpoint = paused with
        {
            Specification = spec,
            Options = options,
            SpecificationFingerprint = ContentFingerprint.Compute(spec),
            OptionsFingerprint = ContentFingerprint.Compute(options),
            RootEvaluationQuota = 100,
            Trials = [paused.Trials[0] with { AllocatedEvaluations = 100 }]
        };
        using var cancellation = new CancellationTokenSource();
        var reserved = false;
        var result = await new FlatStartSearchService().RunAsync(spec, options, cancellation.Token, checkpoint, (saved, _) =>
        {
            FlatStartCheckpointValidation.Validate(saved);
            if (saved.Trials.Any(trial => trial.ReservedReferenceState is not null)) { reserved = true; cancellation.Cancel(); }
            return ValueTask.CompletedTask;
        });
        Assert.True(reserved);
        Assert.Equal(FlatStartSearchState.Cancelled, result.Checkpoint.State);
        Assert.Equal(paused.ChargedEvaluations, result.Checkpoint.ChargedEvaluations);
        Assert.Equal(previous.Adam.Coordinates, result.Checkpoint.Trials[0].AutoLensState!.Adam.Coordinates);
        Assert.Null(result.Checkpoint.Trials[0].ReservedReferenceState);
    }

    [Fact]
    public async Task CompleteSmallProtocolIncludesBothInclusiveLoopsAndPersistsCatalogEndpoint()
    {
        var spec = Small();
        var options = AutoLensReferenceExamples.Mobile().Options with { MaximumEvaluationsPerTrial = 100 };
        var result = await new FlatStartSearchService().RunAsync(spec, options);
        var trial = Assert.Single(result.Checkpoint.Trials);
        output.WriteLine(string.Join("\n", trial.Diagnostics.Select(item => item.Code + ": " + item.Message)));
        Assert.Equal(FamilyTrialState.Completed, trial.State);
        Assert.Equal(AutoLensManagedPhase.Completed, trial.AutoLensState!.Phase);
        Assert.Equal(5, trial.AutoLensState.TrainingSteps.Count(step => step.Phase == AutoLensManagedPhase.Curriculum));
        Assert.Equal(5, trial.AutoLensState.TrainingSteps.Count(step => step.Phase == AutoLensManagedPhase.CatalogRefinement));
        Assert.All(trial.AutoLensState.TrainingSteps, step => Assert.True(step.IsPreUpdateObservation));
        Assert.NotNull(trial.FinalValidation);
        Assert.NotNull(trial.Candidate);
        Assert.Null(trial.AutoLensState.FullTargetIncumbent);
        Assert.Equal(0, trial.AutoLensState.TargetPolishEvaluationCount);
        Assert.All(Optic.FromSnapshot(trial.Candidate.Optic).SurfaceGroup.Items,
            surface => Assert.True(surface.MaterialAfter is AirMaterial or CatalogGlassMaterial));
        FlatStartCheckpointValidation.Validate(result.Checkpoint);
        var directory = Path.Combine(Path.GetTempPath(), "autolens-reference-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "checkpoint.json");
        var store = new FlatStartSearchCheckpointStore();
        await store.SaveAsync(result.Checkpoint, path);
        var loaded = await store.LoadAsync(path);
        Assert.Equal(trial.AutoLensState.Adam.Coordinates, loaded.Trials[0].AutoLensState!.Adam.Coordinates);
    }

    [AutoLensCudaFact]
    public void DirectCudaProtocolCompletesBothPhasesAndRestoresItsRecordedBackend()
    {
        Assert.True(DesignGpu.Status.IsAvailable, DesignGpu.Status.UnavailableReason);
        var source = Small();
        var spec = source with { FlatStart = source.FlatStart! with { AutoLens = source.FlatStart.AutoLens! with { ComputeMode = DesignComputeMode.Gpu } } };
        var result = new FlatStartDesignService().Solve(spec, 1, allowedGlassNames: AutoLensReferenceMaterials.ProjectionNames);
        var state = Assert.IsType<AutoLensManagedState>(result.AutoLensState);
        Assert.Equal(AutoLensManagedPhase.Completed, state.Phase);
        Assert.Equal(5, state.TrainingSteps.Count(step => step.Phase == AutoLensManagedPhase.Curriculum));
        Assert.Equal(5, state.TrainingSteps.Count(step => step.Phase == AutoLensManagedPhase.CatalogRefinement));
        Assert.Equal("cuda-fixed-input-forward-v1", state.LastComputeExecution!.Backend);
        Assert.True(state.GpuBatches > 0);
        Assert.NotNull(result.FinalValidation);
        Assert.NotNull(result.Candidate);
        Assert.Equal(0, state.TargetPolishEvaluationCount);
        var json = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var restored = JsonSerializer.Deserialize<AutoLensManagedState>(JsonSerializer.Serialize(state, json), json)!;
        AutoLensManagedStateValidation.Validate(restored);
        Assert.Equal(state.LastComputeExecution, restored.LastComputeExecution);
        Assert.Equal(state.Adam.Coordinates, restored.Adam.Coordinates);
    }

    [AutoLensCudaFact]
    public void DirectCudaInvalidRayStaysMaskedAndDoesNotCountAsACompletedGradient()
    {
        Assert.True(DesignGpu.Status.IsAvailable, DesignGpu.Status.UnavailableReason);
        var spec = Small();
        var optic = AutoLensReferenceStart.Create(spec, 1, null, AutoLensReferenceMaterials.ProjectionNames);
        var parameters = AutoLensReferenceStart.Parameters(optic, spec, materials: true);
        var input = new FixedInputRaySet([new(0, 0, 0, 0, 0, [new(new(1000, 0, 0), new(0, 0, 1), 587)])]);
        var result = FixedInputOpticalMetrics.Evaluate(optic, parameters, input, .94, DesignComputeMode.Gpu);
        Assert.False(Assert.Single(Assert.Single(result.Fields).Rays).Valid);
        Assert.Equal("cuda-fixed-input-forward-v1", result.Execution.Backend);
        Assert.Equal(1, result.Execution.GpuBatches);
        Assert.Equal(0, result.Execution.GpuRays);
    }

    private static void Close(double expected, double actual, double tolerance = 2e-12) =>
        Assert.True(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance * Math.Max(1, Math.Abs(expected)), $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}");
}
