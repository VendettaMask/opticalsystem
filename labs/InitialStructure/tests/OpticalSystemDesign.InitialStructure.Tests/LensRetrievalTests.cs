using System.Collections.Immutable;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class LensRetrievalTests
{
    [Theory]
    [InlineData(.5)]
    [InlineData(2)]
    public void CoreUniformScalingPreservesAngularOpticsAndUsesDimensionallyCorrectAsphereCoefficients(double factor)
    {
        var model = CementedTopologyTests.Example();
        var front = model.Elements[0].Front with
        {
            Profile = model.Elements[0].Front.Profile with
            { Kind = TopologyProfileKind.EvenAsphere, MaximumEvenPower = 6, Terms = [new(4, 1e-6), new(6, -1e-9)] }
        };
        model = model with { Elements = model.Elements.SetItem(0, model.Elements[0] with { Front = front }) };
        var source = TopologyCompiler.Compile(model); var original = source.CreateOptic();
        var scaled = CenteredPrescriptionScaling.Scale(source.Snapshot, factor);
        var before = CenteredPrescriptionScaling.Describe(original, source.Elements[^1].BackIndex);
        var after = CenteredPrescriptionScaling.Describe(scaled, source.Elements[^1].BackIndex);
        Assert.Equal(before.EffectiveFocalLengthMillimeters * factor, after.EffectiveFocalLengthMillimeters, 11);
        Assert.Equal(before.FNumber, after.FNumber, 11);
        Assert.Equal(before.BackWorkingSpaceMillimeters * factor, after.BackWorkingSpaceMillimeters, 11);
        Assert.Equal(before.MaximumHalfFieldDegrees, after.MaximumHalfFieldDegrees);
        var geometry = scaled.SurfaceGroup.Items[source.Elements[0].FrontIndex].Geometry;
        Assert.Equal(original.SurfaceGroup.Items[source.Elements[0].FrontIndex].Geometry.Sag(2, 1) * factor,
            geometry.Sag(2 * factor, factor), 12);
        var samples = ApertureSampler.GenerateGaussianQuadrature(2, 8);
        var a = SpotMetricEvaluator.EvaluatePupilSamples(original, 0, .5, samples, includeSurfaceTransmission: false);
        var b = SpotMetricEvaluator.EvaluatePupilSamples(scaled, 0, .5, samples, includeSurfaceTransmission: false);
        Assert.NotNull(a.Metrics); Assert.NotNull(b.Metrics);
        Assert.Equal(a.VignettedRayCount, b.VignettedRayCount);
        Assert.Equal(a.Metrics.RmsSpotRadius * factor, b.Metrics.RmsSpotRadius, 9);
        Assert.Equal(source.OpticFingerprint, ContentFingerprint.Compute(source.Snapshot));
    }

    [Fact]
    public void ExactExistingInputIsReusedAndOriginalDenseAssessmentIsIndependentlyReproducible()
    {
        var (plan, options, _) = Specification();
        var result = ParaxialSeedSearch.Run(plan, options);
        var trial = Assert.Single(result.Retrieval!.Trials);
        Assert.True(trial.MatchesOriginalInput);
        Assert.Equal(1, trial.Transform!.UniformScale);
        Assert.NotNull(trial.Assessment); Assert.True(trial.Assessment.Completed);
        Assert.Equal(plan.Targets, trial.Assessment.Targets); Assert.Equal(plan.Constraints, trial.Assessment.StructuralConstraints);
        var independent = TopologyValidator.Validate(TopologyCompiler.Compile(trial.Topology!, plan.Constraints), plan.Targets);
        Assert.Equal(ContentFingerprint.Compute(independent), ContentFingerprint.Compute(trial.Assessment));
        Assert.Equal(plan.Seeds[0].Stop, trial.Topology!.Stop); Assert.Equal(plan.Seeds[0].Image, trial.Topology.Image);
        Assert.Equal(plan.Seeds[0].Reference, trial.Topology.Reference);
        Assert.Equal(21, trial.Assessment.Fields.Length);
        Assert.All(trial.Assessment.Fields, field => Assert.Equal(960, field.AttemptedRays));
        Assert.Equal(result.Work.FunctionEvaluations, result.Retrieval.Trials.Sum(t => t.Work.FunctionEvaluations));
        Assert.Equal(result.Work.TracedRays, result.Retrieval.Trials.Sum(t => t.Work.TracedRays));
        Assert.Equal(result.Work.GeometryProbes, result.Retrieval.Trials.Sum(t => t.Work.GeometryProbes));
        Assert.Equal(result.Work.ValidationEvaluations, result.Retrieval.Trials.Sum(t => t.Work.ValidationEvaluations));
        Assert.All(result.Seeds, seed => Assert.Equal(ContentFingerprint.Compute(plan.Seeds[0]), ContentFingerprint.Compute(seed)));
        Assert.Equal(1, result.Retrieval.UnknownAcquisitionCosts);
        Assert.Equal(1, result.Retrieval.LibraryIndexingWork.FunctionEvaluations);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("scale")]
    [InlineData("pupil")]
    [InlineData("count")]
    [InlineData("stale")]
    [InlineData("descriptor")]
    public void IncompatibleOrAlteredLibraryEntriesNeverReachOriginalAcceptance(string defect)
    {
        var (plan, options, entry) = Specification();
        if (defect == "field") entry = Reindex(entry.Topology with { Fields = [new("Axis", 0, 0)] }, entry.Constraints);
        if (defect == "pupil") entry = Reindex(entry.Topology with { Stop = entry.Topology.Stop with { ClearSemiDiameterMillimeters = .1 } }, entry.Constraints);
        if (defect == "count") entry = Reindex(entry.Topology with { Elements = entry.Topology.Elements.RemoveAt(1) }, entry.Constraints);
        if (defect == "stale") entry = entry with { SourceOpticFingerprint = new string('a', 64) };
        if (defect == "descriptor") entry = entry with { Descriptor = entry.Descriptor with { EffectiveFocalLengthMillimeters = entry.Descriptor.EffectiveFocalLengthMillimeters * 1.1 } };
        options = options with { Retrieval = options.Retrieval! with { Library = new() { Entries = [entry] }, MinimumScale = defect == "scale" ? 2 : .5 } };
        var result = ParaxialSeedSearch.Run(plan, options);
        Assert.Empty(result.Seeds); Assert.Empty(result.Retrieval!.QualifiedEntryIds);
        Assert.Equal(0, result.Work.ValidationEvaluations); Assert.Equal(0, result.Work.TracedRays);
        Assert.Null(Assert.Single(result.Retrieval.Trials).Assessment);
    }

    [Theory]
    [InlineData("material")]
    [InlineData("protected")]
    [InlineData("axial")]
    [InlineData("back-space")]
    public void TransferRespectsOriginalMaterialProtectionAxialAndMechanicalPermissions(string defect)
    {
        var (plan, options, entry) = Specification(); var model = entry.Topology;
        if (defect == "protected") plan = plan with { Seeds = [model with { Elements = model.Elements.SetItem(0, model.Elements[0] with { Protected = true }) }] };
        var lens = model.Elements[0];
        model = model with { Elements = model.Elements.SetItem(0, lens with { FrontZMillimeters = lens.FrontZMillimeters + .2 }) };
        if (defect == "material")
        {
            model = model with { Elements = model.Elements.SetItem(0, model.Elements[0] with { Material = "N-F2" }) };
            plan = plan with { Options = plan.Options with { Expansion = null } };
        }
        if (defect == "axial") plan = plan with { Options = plan.Options with { AxialCompensation = null } };
        if (defect == "back-space") plan = plan with { Seeds = [plan.Seeds[0] with { Image = plan.Seeds[0].Image with { ZMillimeters = plan.Seeds[0].Elements[^1].BackZMillimeters + plan.Constraints.MinimumBackFocusMillimeters } }] };
        if (defect == "back-space") model = model with { Elements = model.Elements.SetItem(model.Elements.Length - 1, model.Elements[^1] with { ThicknessMillimeters = model.Elements[^1].ThicknessMillimeters + .25 }) };
        entry = Reindex(model, entry.Constraints);
        options = options with { Retrieval = options.Retrieval! with { Library = new() { Entries = [entry] } } };
        var result = ParaxialSeedSearch.Run(plan, options);
        Assert.Empty(result.Seeds); Assert.Empty(result.Retrieval!.QualifiedEntryIds);
        Assert.Contains(result.Retrieval.Trials, t => t.Outcome == "TransferRejected");
        Assert.Equal(0, result.Work.TracedRays);
    }

    [Fact]
    public void OriginalFaceAperturesArePreservedAndClippingCannotBeAcceptedFromLibraryMetadata()
    {
        var (plan, options, _) = Specification();
        var lens = plan.Seeds[0].Elements[0];
        plan = plan with
        {
            Seeds = [plan.Seeds[0] with { Elements = plan.Seeds[0].Elements.SetItem(0,
            lens with { Front = lens.Front with { ClearSemiDiameterMillimeters = .05 } }) }]
        };
        var result = ParaxialSeedSearch.Run(plan, options); var trial = Assert.Single(result.Retrieval!.Trials);
        Assert.NotNull(trial.Assessment); Assert.True(trial.Assessment.Completed);
        Assert.False(trial.Assessment.MeetsTargets); Assert.Empty(result.Retrieval.QualifiedEntryIds);
        Assert.Equal(.05, trial.Topology!.Elements[0].Front.ClearSemiDiameterMillimeters);
        Assert.Contains(trial.Assessment.Violations, v => v.Code.Contains("lost", StringComparison.Ordinal));
        Assert.Contains(trial.Transform!.FixedApertures, a => a.TaskClearMillimeters == .05);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void BudgetsAndCancellationKeepAllActualWorkAndCannotPublishPartialQualification(int mode)
    {
        var (plan, options, _) = Specification(); using var cancel = new CancellationTokenSource();
        options = mode switch
        {
            1 => options with { MaximumTracedRays = 1 },
            2 => options with { MaximumEvaluationsPerStart = 2 },
            _ => options
        };
        var result = ParaxialSeedSearch.Run(plan, options, cancel.Token, (_, _) => { if (mode == 3) cancel.Cancel(); });
        Assert.Empty(result.Seeds); Assert.Empty(result.Retrieval!.QualifiedEntryIds);
        Assert.InRange(result.Work.TracedRays, 0, options.MaximumTracedRays);
        Assert.InRange(result.Work.FunctionEvaluations, 0, options.MaximumFunctionEvaluations);
        Assert.Equal(result.Work.FunctionEvaluations, result.Retrieval.Trials.Sum(t => t.Work.FunctionEvaluations));
        Assert.All(result.Retrieval.Trials, trial => Assert.InRange(trial.Work.FunctionEvaluations, 0, options.MaximumEvaluationsPerStart));
        Assert.Equal(0, result.Work.ValidationEvaluations);
        if (mode == 3) Assert.Equal(ParaxialSeedTermination.Cancelled, result.Termination);
    }

    [Fact]
    public void ScaledExistingLensIsTransferredInsideTheOriginalTaskFrameAndApertures()
    {
        var (plan, options, entry) = Specification();
        var source = TopologyCompiler.Compile(entry.Topology, entry.Constraints);
        var bounds = plan.Constraints with
        {
            MinimumCenterThicknessMillimeters = .2,
            MinimumAirGapMillimeters = .1,
            MinimumBackFocusMillimeters = 30,
            MaximumMechanicalTrackMillimeters = 130
        };
        var scaled = TopologyCompiler.Import(CenteredPrescriptionScaling.Scale(source.Snapshot, 2).ToSnapshot(), bounds);
        plan = plan with
        {
            Seeds = [scaled.Topology],
            Constraints = bounds,
            Targets = plan.Targets with { EffectiveFocalLengthMillimeters = 100, MaximumOpticalTrackMillimeters = 130 }
        };
        options = options with { Retrieval = options.Retrieval! with { MaximumScale = 3 } };
        var result = ParaxialSeedSearch.Run(plan, options); var trial = Assert.Single(result.Retrieval!.Trials);
        Assert.NotNull(trial.Transform); Assert.Equal(2, trial.Transform.UniformScale, 10);
        Assert.NotNull(trial.Assessment); Assert.True(trial.Assessment.Completed, trial.Assessment.Failure);
        Assert.Equal(plan.Targets, trial.Assessment.Targets); Assert.Equal(plan.Constraints, trial.Assessment.StructuralConstraints);
        Assert.Equal(plan.Seeds[0].Image, trial.Topology!.Image); Assert.Equal(plan.Seeds[0].Stop, trial.Topology.Stop);
        Assert.All(trial.Topology.Elements.Zip(plan.Seeds[0].Elements), pair =>
        {
            Assert.Equal(pair.Second.Front.ClearSemiDiameterMillimeters, pair.First.Front.ClearSemiDiameterMillimeters);
            Assert.Equal(pair.Second.Back.ClearSemiDiameterMillimeters, pair.First.Back.ClearSemiDiameterMillimeters);
        });
    }

    [Fact]
    public void IncompleteDenseAssessmentCannotEscapeAfterConservativeEstimateIsExceeded()
    {
        var (plan, options, _) = Specification();
        options = options with { MaximumTracedRays = 22500, Retrieval = options.Retrieval! with { FinalRayRecoveryFactor = 1 } };
        var result = ParaxialSeedSearch.Run(plan, options); var trial = Assert.Single(result.Retrieval!.Trials);
        Assert.NotNull(trial.Assessment); Assert.False(trial.Assessment.Completed);
        Assert.Empty(result.Seeds); Assert.Empty(result.Retrieval.QualifiedEntryIds);
        Assert.InRange(result.Work.TracedRays, 1, 22500);
        Assert.Equal(1, result.Work.ValidationEvaluations);
        Assert.Equal(trial.Work.TracedRays, result.Work.TracedRays);
    }

    [Fact]
    public void GlobalFunctionBudgetStopsBeforeUnfundedDenseAssessment()
    {
        var (plan, options, _) = Specification();
        var result = ParaxialSeedSearch.Run(plan, options with { MaximumFunctionEvaluations = 2 });
        Assert.Equal(ParaxialSeedTermination.BudgetExhausted, result.Termination);
        Assert.Equal(2, result.Work.FunctionEvaluations); Assert.Equal(0, result.Work.ValidationEvaluations);
        Assert.Empty(result.Seeds); Assert.Empty(result.Retrieval!.QualifiedEntryIds);
    }

    [Fact]
    public async Task LibraryRoundTripIsBoundedChecksummedAndDoesNotPerformOpticalWorkOnLoad()
    {
        var (_, _, entry) = Specification(); var file = Path.Combine(Path.GetTempPath(), "library-" + Guid.NewGuid().ToString("N") + ".lens-library.json");
        try
        {
            var library = new LensLibraryDocument { Entries = [entry] }; await LensLibraryFiles.SaveAsync(library, file);
            using var rays = SequentialTraceMeasurement.Begin();
            var restored = await LensLibraryFiles.LoadAsync(file);
            Assert.Equal(ContentFingerprint.Compute(library), ContentFingerprint.Compute(restored)); Assert.Equal(0, rays.RayCount);
            var text = await File.ReadAllTextAsync(file); await File.WriteAllTextAsync(file, text.Replace("test-source", "modified-source", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidDataException>(() => LensLibraryFiles.LoadAsync(file));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void IndexRoundoffDoesNotAlterScaleOrRelaxOriginalPhysicalGates()
    {
        var (plan, options, entry) = Specification();
        var perturbed = entry with { Descriptor = entry.Descriptor with { EffectiveFocalLengthMillimeters = entry.Descriptor.EffectiveFocalLengthMillimeters + 1e-13 } };
        options = options with { Retrieval = options.Retrieval! with { Library = new() { Entries = [perturbed] } } };
        var result = ParaxialSeedSearch.Run(plan, options); var trial = Assert.Single(result.Retrieval!.Trials);
        Assert.NotNull(trial.Assessment); Assert.True(trial.Assessment.Completed);
        Assert.Equal(1, trial.Transform!.UniformScale); Assert.Equal(plan.Targets, trial.Assessment.Targets);
    }

    [Fact]
    public void SharedSourceAcquisitionCostIsCountedOnceAndUnsupportedDescriptorOrScaleInputsFail()
    {
        var (plan, options, entry) = Specification();
        var work = new AutonomousSearchWork { FunctionEvaluations = 99, TracedRays = 999, Elapsed = TimeSpan.FromSeconds(1) };
        entry = entry with { SourceAcquisitionWork = work };
        options = options with { Retrieval = options.Retrieval! with { Library = new() { Entries = [entry, entry with { Id = "same-source-second-entry" }] }, MaximumCandidates = 2 } };
        var result = ParaxialSeedSearch.Run(plan, options);
        Assert.Equal(99, result.Retrieval!.KnownSourceAcquisitionWork.FunctionEvaluations);
        Assert.Equal(999, result.Retrieval.KnownSourceAcquisitionWork.TracedRays);
        Assert.Equal(0, result.Retrieval.UnknownAcquisitionCosts);
        Assert.Single(result.Seeds);
        var source = TopologyCompiler.Compile(entry.Topology, entry.Constraints);
        Assert.Throws<NotSupportedException>(() => CenteredPrescriptionScaling.Describe(source.CreateOptic(), source.SurfaceIndices[source.Topology.Stop.Id]));
        Assert.Throws<NotSupportedException>(() => CenteredPrescriptionScaling.Scale(source.Snapshot with { SolveSettings = new(20, true) }, 2));
    }

    [Fact]
    public void EmptyProvenanceAndDuplicatesAreRejectedAndNullOptionKeepsTheLegacySerializedShape()
    {
        var (_, _, entry) = Specification();
        Assert.Throws<ArgumentException>(() => LensLibraryService.Validate(new() { Entries = [entry, entry] }));
        Assert.Throws<ArgumentException>(() => LensLibraryService.Validate(new() { Entries = [entry with { Provenance = entry.Provenance with { UsageAuthority = "" } }] }));
        Assert.DoesNotContain("retrieval", JsonSerializer.Serialize(new ParaxialSeedSearchOptions(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    internal static (AutonomousSearchPlan Plan, ParaxialSeedSearchOptions Options, LensLibraryEntry Entry) Specification()
    {
        var (plan, _) = StructureEvolutionTests.Specification();
        var entry = Reindex(plan.Seeds[0], plan.Constraints);
        return (plan, new()
        {
            MaximumStarts = 4,
            MaximumEvaluationsPerStart = 10,
            MaximumFunctionEvaluations = 100,
            MaximumTracedRays = 1_000_000,
            TimeLimit = TimeSpan.FromSeconds(60),
            Retrieval = new() { Library = new() { Entries = [entry] } }
        }, entry);
    }
    private static LensLibraryEntry Reindex(DesignTopology topology, TopologyConstraints constraints) => LensLibraryService.Index("test-source",
        new(LensSourceKind.ProjectAuthored, "Authored test fixture", "repository-authored unit fixture", "Project-authored verification only", ContentFingerprint.Compute(topology)),
        TopologyCompiler.Compile(topology, constraints));
}
