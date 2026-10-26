using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using Xunit.Abstractions;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class AutonomousTopologyFoundationTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void CompiledPrescriptionMatchesIndependentCoreAssemblyAndFreezesSmallBaselines(int count, bool asphere)
    {
        var topology = Example(count, asphere);
        var compiled = TopologyCompiler.Compile(topology);
        var optic = compiled.CreateOptic();
        var manual = IndependentCoreAssembly(count, asphere);
        Assert.Equal(AutonomousTopologyIdentity.Name, compiled.Topology.Algorithm);
        Assert.Equal(count * 2 + 4, optic.SurfaceGroup.Items.Count);
        Assert.True(double.IsPositiveInfinity(optic.SurfaceGroup.Items[0].Thickness));
        Assert.Equal(55, compiled.Topology.MechanicalTrackMillimeters);
        Assert.Equal(52, compiled.Topology.OpticalTrackMillimeters);
        Assert.Equal(2 * count + 3, compiled.SurfaceIndices["image"]);
        foreach (var mapping in compiled.Elements)
        {
            var element = topology.Elements.Single(item => item.Id == mapping.ElementId);
            var front = optic.SurfaceGroup.Items[mapping.FrontIndex];
            var back = optic.SurfaceGroup.Items[mapping.BackIndex];
            Assert.Equal(element.FrontZMillimeters, front.CoordinateSystem.Origin.Z);
            Assert.Equal(element.BackZMillimeters, back.CoordinateSystem.Origin.Z);
            Assert.IsType<AirMaterial>(front.MaterialBefore);
            Assert.IsType<CatalogGlassMaterial>(front.MaterialAfter);
            Assert.Equal(front.MaterialAfter.Name, back.MaterialBefore.Name);
            Assert.IsType<AirMaterial>(back.MaterialAfter);
            Assert.Equal(8, Assert.IsType<CircularAperture>(front.PhysicalAperture).Radius);
            Assert.Equal(7.5, Assert.IsType<CircularAperture>(back.PhysicalAperture).Radius);
        }
        Assert.All(compiled.Materials, material => Assert.False(string.IsNullOrWhiteSpace(material.ComponentFingerprint)));
        AssertSameOptics(manual, optic);
        var samples = ApertureSampler.GenerateGaussianQuadrature(4, 16);
        var metrics = topology.Fields.Select((field, index) =>
        {
            var sampled = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, index, samples, includeSurfaceTransmission: false);
            Assert.NotNull(sampled.Metrics);
            return new { field.Label, sampled.RayCount, sampled.VignettedRayCount, sampled.Metrics };
        }).ToArray();
        output.WriteLine($"{count} elements, asphere={asphere}, EFL={optic.Paraxial.EstimateEffectiveFocalLength():R}, RMS={metrics[0].Metrics!.RmsSpotRadius:R}");
        Evidence($"baseline-{count}", new
        {
            Algorithm = AutonomousTopologyIdentity.Name,
            Stage = "M0-M1 compilation only",
            Topology = topology,
            CoreSnapshot = compiled.Snapshot,
            compiled.TopologyFingerprint,
            compiled.OpticFingerprint,
            compiled.Materials,
            Sampling = new { Rings = 4, Arms = 16, Reference = "centroid", IncludeSurfaceTransmission = false },
            EffectiveFocalLengthMillimeters = optic.Paraxial.EstimateEffectiveFocalLength(),
            Metrics = metrics,
            CoreAssembly = typeof(Optic).Assembly.ManifestModule.ModuleVersionId,
            EngineAssembly = typeof(TopologyCompiler).Assembly.ManifestModule.ModuleVersionId
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void ImportRoundTripPreservesOpticsAndUsesAnExplicitMechanicalFrame(double origin)
    {
        var source = TopologyCompiler.Compile(Example(3, true));
        var imported = TopologyCompiler.Import(source.Snapshot, referenceZMillimeters: origin);
        Assert.Equal(origin, imported.Topology.Reference.ZMillimeters);
        Assert.Equal(55 + origin, imported.Topology.Image.ZMillimeters);
        Assert.Equal(3 + origin, imported.Topology.Elements[0].FrontZMillimeters);
        Assert.Equal(-origin, imported.GlobalToCoreZOffset);
        Assert.Equal(55, imported.Topology.MechanicalTrackMillimeters);
        AssertSameOptics(source.CreateOptic(), imported.CreateOptic());
        var json = JsonSerializer.Serialize(imported.Topology, JsonOptions);
        var restored = JsonSerializer.Deserialize<DesignTopology>(json, JsonOptions)!;
        Assert.Equal(imported.TopologyFingerprint, TopologyCompiler.Compile(restored).TopologyFingerprint);
    }

    [Fact]
    public void MutableCoreCopiesAndReturnedSnapshotsCannotAlterTheParent()
    {
        var compiled = TopologyCompiler.Compile(Example(2));
        var fingerprint = compiled.OpticFingerprint;
        var copy = compiled.CreateOptic();
        copy.SurfaceGroup.Items[3].Radius = 1;
        copy.Fields[0].Weight = 7;
        var snapshot = compiled.Snapshot;
        snapshot.Surfaces.Clear();
        Assert.Equal(fingerprint, ContentFingerprint.Compute(compiled.Snapshot));
        Assert.Equal(45, compiled.CreateOptic().SurfaceGroup.Items[3].Radius);
    }

    [Theory]
    [InlineData(.1)]
    [InlineData(10000.123)]
    public void FractionalMechanicalCoordinatesSurviveIdentityUpdates(double origin)
    {
        var model = Example(2, true);
        model = model with
        {
            Reference = new("reference", origin),
            Stop = new("stop", origin + .567, 2),
            Image = new("image", origin + 55.87654321),
            Elements = model.Elements.SetItem(0, model.Elements[0] with { FrontZMillimeters = origin + 3.123456789, ThicknessMillimeters = 1.987654321 })
                .SetItem(1, model.Elements[1] with { FrontZMillimeters = origin + 10.345678901, ThicknessMillimeters = 2.123456789, Protected = true })
        };
        var source = TopologyCompiler.Compile(model);
        var identity = TopologyCompiler.Update(source, source.Snapshot);
        Assert.Equal(source.TopologyFingerprint, identity.TopologyFingerprint);
        var binding = TopologyParameterBinder.Bind(source, [new("lens-1:front", OpticalDesignParameterKind.Curvature, 0, .1)]);
        var after = TopologyParameterBinder.Apply(source, binding, binding.InitialCoordinates);
        Assert.Equal(source.TopologyFingerprint, after.TopologyFingerprint);
    }

    [Fact]
    public void InsertDeleteKeepStableIdsParentDataAndEveryRetainedVertex()
    {
        var parent = Example(2);
        var parentFingerprint = ContentFingerprint.Compute(parent);
        var insert = TopologyOperators.Insert(parent, "lens-1:back", "lens-2:front", NewLens(), .5);
        Assert.True(insert.Succeeded, insert.Failure?.Message);
        var child = insert.Topology!;
        Assert.Equal(3, child.Elements.Length);
        Assert.Equal(6.5, child.Elements[1].FrontZMillimeters);
        Assert.Same(parent.Elements[0], child.Elements[0]);
        Assert.Same(parent.Elements[1], child.Elements[2]);
        Assert.Equal(parentFingerprint, ContentFingerprint.Compute(parent));
        Assert.Equal(parentFingerprint, insert.Change!.ParentFingerprint);
        Assert.Equal("new-lens", Assert.Single(insert.Change.CreatedElementIds));
        var back = TopologyOperators.Delete(child, "new-lens");
        Assert.True(back.Succeeded);
        Assert.Equal(parentFingerprint, ContentFingerprint.Compute(back.Topology!));
        AssertSameOptics(TopologyCompiler.Compile(parent).CreateOptic(), TopologyCompiler.Compile(back.Topology!).CreateOptic());
        var firstDeleted = TopologyOperators.Delete(parent, "lens-1").Topology!;
        Assert.Equal(parent.MechanicalTrackMillimeters, firstDeleted.MechanicalTrackMillimeters);
        Assert.Equal(45, firstDeleted.OpticalTrackMillimeters);
        Assert.Same(parent.Elements[1], Assert.Single(firstDeleted.Elements));
        Evidence("insert-delete", new { Parent = parent, Insert = insert, Delete = back, FirstDeleted = firstDeleted });
    }

    [Fact]
    public void IndependentStopSplitsAirIntervalsAndCannotBeCrossedByInsertion()
    {
        var parent = Example(2) with { Stop = new("stop", 7, 2) };
        var intervals = TopologyValidation.AirIntervals(parent);
        Assert.Contains(intervals, interval => interval.LeftSurfaceId == "lens-1:back" && interval.RightSurfaceId == "stop" && interval.LengthMillimeters == 2);
        Assert.Contains(intervals, interval => interval.LeftSurfaceId == "stop" && interval.RightSurfaceId == "lens-2:front" && interval.LengthMillimeters == 3);
        Assert.Equal(TopologyFailureCode.UnknownBoundary, TopologyOperators.Insert(parent, "lens-1:back", "lens-2:front", NewLens(), .5).Failure!.Code);
        Assert.Equal(TopologyFailureCode.NoSpace, TopologyOperators.Insert(parent, "lens-1:back", "stop", NewLens(), .5).Failure!.Code);
        var success = TopologyOperators.Insert(parent, "stop", "lens-2:front", NewLens() with { ThicknessMillimeters = 1 }, .5);
        Assert.True(success.Succeeded, success.Failure?.Message);
        var optic = TopologyCompiler.Compile(success.Topology!).CreateOptic();
        Assert.Equal(7, optic.SurfaceGroup.Items.Single(surface => surface.IsStop).CoordinateSystem.Origin.Z);
        AssertSameOptics(optic, TopologyCompiler.Import(optic.ToSnapshot()).CreateOptic());
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    public void FirstAndLastInsertionRespectEndClearancesWithoutMovingExistingElements(bool first, double fraction)
    {
        var parent = Example(2);
        var constraints = new TopologyConstraints { MinimumBackFocusMillimeters = 1 };
        var child = TopologyOperators.Insert(parent, first ? "stop" : "lens-2:back", first ? "lens-1:front" : "image",
            NewLens() with { ThicknessMillimeters = 1 }, fraction, constraints);
        Assert.True(child.Succeeded, child.Failure?.Message);
        foreach (var retained in parent.Elements) Assert.Same(retained, child.Topology!.Elements.Single(element => element.Id == retained.Id));
        Assert.True(55 - child.Topology!.Elements[^1].BackZMillimeters >= 1);
        Assert.Equal(55, child.Topology.Image.ZMillimeters);
        var compiled = TopologyCompiler.Compile(child.Topology, constraints);
        Assert.Equal(55, compiled.CreateOptic().SurfaceGroup.Items[^1].CoordinateSystem.Origin.Z);
        var deleted = TopologyOperators.Delete(child.Topology, "new-lens", constraints);
        Assert.Equal(ContentFingerprint.Compute(parent), ContentFingerprint.Compute(deleted.Topology!));
    }

    [Fact]
    public void ExplicitSmallFaceApertureReallyBlocksCoreRaysAfterCompileAndImport()
    {
        var parent = Example(2);
        var first = parent.Elements[0];
        var model = parent with
        {
            Elements = parent.Elements.SetItem(0, first with
            {
                Front = first.Front with
                { ClearSemiDiameterMillimeters = .1, MechanicalSemiDiameterMillimeters = 9 }
            })
        };
        var compiled = TopologyCompiler.Compile(model);
        foreach (var value in new[] { compiled, TopologyCompiler.Import(compiled.Snapshot) })
        {
            var result = SpotMetricEvaluator.EvaluatePupilSamples(value.CreateOptic(), 0, 0, [new(.5, 0, 1)], includeSurfaceTransmission: false);
            Assert.True(result.RayCount > 0);
            Assert.Equal(result.RayCount, result.VignettedRayCount);
            Assert.Null(result.Metrics);
        }
    }

    [Fact]
    public void AxialOnlyPlaneUpdateKeepsItsDeclaredKindAndUnprotectedImageIsExplicitlyBound()
    {
        var parent = Example(2);
        var first = parent.Elements[0];
        var model = parent with
        {
            Image = parent.Image with { Protected = false },
            Elements = parent.Elements.SetItem(0, first with { Front = first.Front with { Profile = new() { Kind = TopologyProfileKind.Plane } } })
        };
        var source = TopologyCompiler.Compile(model);
        var binding = TopologyParameterBinder.Bind(source,
            [new("lens-1:front", OpticalDesignParameterKind.AxialPosition, 2, 4), new("image", OpticalDesignParameterKind.AxialPosition, 54, 56)]);
        var updated = TopologyParameterBinder.Apply(source, binding, [3.1, 56]);
        Assert.Equal(TopologyProfileKind.Plane, updated.Topology.Elements[0].Front.Profile.Kind);
        Assert.Equal(5, updated.Topology.Elements[0].BackZMillimeters, 12);
        Assert.Equal(56, updated.Topology.Image.ZMillimeters);
        Assert.IsType<PlaneGeometry>(updated.CreateOptic().SurfaceGroup.Items[3].Geometry);
    }

    [Fact]
    public void ProtectedElementsAndElementLimitsRejectStructuralEdits()
    {
        var parent = Example(2);
        var protectedParent = parent with { Elements = parent.Elements.SetItem(0, parent.Elements[0] with { Protected = true }) };
        Assert.Equal(TopologyFailureCode.ProtectedElement, TopologyOperators.Delete(protectedParent, "lens-1").Failure!.Code);
        Assert.Equal(TopologyFailureCode.ElementLimit, TopologyOperators.Delete(parent, "lens-1", new() { MinimumElementCount = 2 }).Failure!.Code);
        Assert.Equal(TopologyFailureCode.ElementLimit, TopologyOperators.Insert(parent, "lens-1:back", "lens-2:front", NewLens(), .5, new() { MaximumElementCount = 2 }).Failure!.Code);
        Assert.Equal(TopologyFailureCode.DuplicateId, TopologyOperators.Insert(parent, "lens-1:back", "lens-2:front", NewLens() with { Id = "lens-1" }, .5).Failure!.Code);
        Assert.Equal(TopologyFailureCode.UnknownElement, TopologyOperators.Delete(parent, "absent").Failure!.Code);
    }

    [Fact]
    public void AxialAndAllEvenPowersUseCoreBindingsWithoutMovingOtherVertices()
    {
        var source = TopologyCompiler.Compile(Shift(Example(3, true), 100));
        var requests = new List<TopologyVariableRequest>
        {
            new("lens-1:front", OpticalDesignParameterKind.AxialPosition, 102, 104, 10),
            new("lens-1:front", OpticalDesignParameterKind.Curvature, 0, .1, .01),
            new("lens-1:front", OpticalDesignParameterKind.Conic, -2, 2)
        };
        for (var power = 4; power <= 18; power += 2)
            requests.Add(new("lens-1:front", OpticalDesignParameterKind.EvenAsphereCoefficient, -1, 1, Math.Pow(10, -power), power));
        var binding = TopologyParameterBinder.Bind(source, requests);
        Assert.Equal(10.3, binding.InitialCoordinates[0], 12);
        Assert.Equal(8, binding.Slots[^1].CoreParameter.CoefficientIndex);
        var identity = TopologyParameterBinder.Apply(source, binding, binding.InitialCoordinates);
        AssertSameOptics(source.CreateOptic(), identity.CreateOptic());
        var coordinates = binding.InitialCoordinates.ToArray();
        coordinates[0] += .01; // A physical 0.1 mm change in the global frame.
        coordinates[1] += .1;
        for (var index = 3; index < coordinates.Length; index++) coordinates[index] += .25;
        var updated = TopologyParameterBinder.Apply(source, binding, coordinates);
        Assert.Equal(103.1, updated.Topology.Elements[0].FrontZMillimeters, 11);
        Assert.Equal(105, updated.Topology.Elements[0].BackZMillimeters, 11);
        Assert.Same(source.Topology.Elements[1].Front, updated.Topology.Elements[1].Front);
        Assert.Equal(source.Topology.Elements[1], updated.Topology.Elements[1]);
        Assert.Equal(source.Topology.Stop, updated.Topology.Stop);
        Assert.Equal(source.Topology.Image, updated.Topology.Image);
        Assert.NotEqual(source.CreateOptic().Paraxial.EstimateEffectiveFocalLength(), updated.CreateOptic().Paraxial.EstimateEffectiveFocalLength());
        var read = OpticalDesignParameters.Read(updated.CreateOptic(), binding.CoreParameters);
        for (var index = 0; index < read.Length; index++) Assert.Equal(coordinates[index], read[index], 10);
        var profile = OpticalDesignParameters.Profile(updated.CreateOptic().SurfaceGroup.Items[source.SurfaceIndices["lens-1:front"]].Geometry);
        Assert.Equal(0, profile.Coefficients[0]);
        for (var power = 4; power <= 18; power += 2)
        {
            var expected = coordinates[power / 2 + 1] * Math.Pow(10, -power);
            Assert.InRange(Math.Abs(expected - profile.Coefficients[power / 2 - 1]), 0, 1e-12 * Math.Abs(expected) + 1e-32);
        }
    }

    [Fact]
    public void ZeroStepPreservesSparseProfileRequestedMaterialAndProtectedElementMetadata()
    {
        var model = Example(2, true);
        model = model with { Elements = model.Elements.SetItem(1, model.Elements[1] with { Protected = true }) };
        var source = TopologyCompiler.Compile(model);
        var binding = TopologyParameterBinder.Bind(source, [new("lens-1:front", OpticalDesignParameterKind.Curvature, 0, .1)]);
        var updated = TopologyParameterBinder.Apply(source, binding, binding.InitialCoordinates);
        Assert.Same(model.Elements[0].Front, updated.Topology.Elements[0].Front);
        Assert.Same(model.Elements[1].Back, updated.Topology.Elements[1].Back);
        Assert.Equal(source.TopologyFingerprint, updated.TopologyFingerprint);
        Assert.Equal(3, updated.Topology.Elements[0].Front.Profile.Terms.Length);
        Assert.Equal("N-F2", updated.Topology.Elements[1].Material);
    }

    [Fact]
    public void BindingsRejectProtectedUnsupportedAndStaleCoordinates()
    {
        var model = Example(2, true);
        var source = TopologyCompiler.Compile(model);
        var binding = TopologyParameterBinder.Bind(source, [new("lens-1:front", OpticalDesignParameterKind.Curvature, 0, .1)]);
        Failure(TopologyFailureCode.ProtectedElement, () => TopologyParameterBinder.Bind(source, [new("stop", OpticalDesignParameterKind.AxialPosition, -1, 1)]));
        Failure(TopologyFailureCode.ProtectedElement, () => TopologyParameterBinder.Bind(source, [new("image", OpticalDesignParameterKind.AxialPosition, 50, 60)]));
        Failure(TopologyFailureCode.UnsupportedVariable, () => TopologyParameterBinder.Bind(source, [new("lens-1:front", OpticalDesignParameterKind.EvenAsphereCoefficient, -1, 1, Power: 2)]));
        Failure(TopologyFailureCode.UnsupportedVariable, () => TopologyParameterBinder.Bind(source, [new("lens-1:front", OpticalDesignParameterKind.Thickness, 0, 10)]));
        Failure(TopologyFailureCode.InvalidVariableBounds, () => TopologyParameterBinder.Apply(source, binding, [.2]));
        var shifted = TopologyCompiler.Compile(Shift(model, 100));
        Failure(TopologyFailureCode.IdentityMismatch, () => TopologyParameterBinder.Apply(shifted, binding, binding.InitialCoordinates));
        var inserted = TopologyCompiler.Compile(TopologyOperators.Insert(model, "lens-1:back", "lens-2:front", NewLens(), .5).Topology!);
        Failure(TopologyFailureCode.IdentityMismatch, () => TopologyParameterBinder.Apply(inserted, binding, binding.InitialCoordinates));
        var changedMaterial = TopologyCompiler.Compile(model with { Elements = model.Elements.SetItem(0, model.Elements[0] with { Material = "N-F2" }) });
        Failure(TopologyFailureCode.IdentityMismatch, () => TopologyParameterBinder.Apply(changedMaterial, binding, binding.InitialCoordinates));
        var movement = TopologyParameterBinder.Bind(source, [new("lens-2:front", OpticalDesignParameterKind.AxialPosition, 0, 20)]);
        Failure(TopologyFailureCode.AirGap, () => TopologyParameterBinder.Apply(source, movement, [4]));
    }

    [Fact]
    public void UpdateRejectsSettingsIdentityAndProtectedChangesInsteadOfSilentlyDiscardingThem()
    {
        var source = TopologyCompiler.Compile(Example(2) with { Elements = Example(2).Elements.SetItem(1, Example(2).Elements[1] with { Protected = true }) });
        var optic = source.CreateOptic();
        optic.Fields[0].Weight = 2;
        Failure(TopologyFailureCode.IdentityMismatch, () => TopologyCompiler.Update(source, optic.ToSnapshot()));
        optic = source.CreateOptic();
        optic.SurfaceGroup.Items[5].Radius = 30;
        Failure(TopologyFailureCode.ProtectedElement, () => TopologyCompiler.Update(source, optic.ToSnapshot()));
        optic = source.CreateOptic();
        optic.SurfaceGroup.Items[3].Label = "changed";
        Failure(TopologyFailureCode.IdentityMismatch, () => TopologyCompiler.Update(source, optic.ToSnapshot()));
    }

    [Fact]
    public void UnsupportedImportFeaturesAreExplicitFailures()
    {
        var source = TopologyCompiler.Compile(Example(2));
        var finite = source.Snapshot;
        finite.Surfaces[0] = finite.Surfaces[0] with { Thickness = 100 };
        Failure(TopologyFailureCode.UnsupportedConjugate, () => TopologyCompiler.Import(finite));
        Failure(TopologyFailureCode.UnsupportedFeature, () => TopologyCompiler.Import(source.Snapshot with { ImageSpaceAfocal = true }));
        Failure(TopologyFailureCode.UnsupportedFeature, () => TopologyCompiler.Import(source.Snapshot with { SolveSettings = new(30, true) }));
        var tilt = source.Snapshot;
        tilt.Surfaces[3] = tilt.Surfaces[3] with { CoordinateSystem = tilt.Surfaces[3].CoordinateSystem! with { RotationYDegrees = 1 } };
        Failure(TopologyFailureCode.UnsupportedFeature, () => TopologyCompiler.Import(tilt));
        var clipped = source.CreateOptic();
        clipped.SurfaceGroup.Items[3].PhysicalAperture = null;
        Failure(TopologyFailureCode.InvalidAperture, () => TopologyCompiler.Import(clipped.ToSnapshot()));
        var cemented = source.CreateOptic();
        cemented.SurfaceGroup.Items[4].MaterialAfter = cemented.Materials.Resolve("N-F2");
        OpticalDesignParameters.SynchronizeIncidentMedia(cemented);
        Failure(TopologyFailureCode.UnsupportedFeature, () => TopologyCompiler.Import(cemented.ToSnapshot()));
        var substituted = source.CreateOptic();
        substituted.SurfaceGroup.Items[3].MaterialAfter = new CatalogGlassMaterial("N-BK7", "Test", "tabulated n", 400, 700,
            refractiveIndexWavelengthsNanometers: [400, 700], refractiveIndices: [1.8, 1.7]);
        OpticalDesignParameters.SynchronizeIncidentMedia(substituted);
        Failure(TopologyFailureCode.UnsupportedMaterial, () => TopologyCompiler.Import(substituted.ToSnapshot()));
    }

    [Fact]
    public void StructuralInvalidInputsFailBeforeCoreCanClampOrReorderThem()
    {
        var source = Example(2);
        Assert.Equal(TopologyFailureCode.InvalidIdentity, TopologyCompiler.TryCompile(source with { Algorithm = "UpstreamE58" }).Failure!.Code);
        Assert.Equal(TopologyFailureCode.InvalidAperture, TopologyCompiler.TryCompile(source with { Stop = new("stop", 0, .0001) }).Failure!.Code);
        Assert.Equal(TopologyFailureCode.StopInsideElement, TopologyCompiler.TryCompile(source with { Stop = new("stop", 4, 2) }).Failure!.Code);
        Assert.Equal(TopologyFailureCode.AirGap, TopologyCompiler.TryCompile(source with { Elements = source.Elements.Reverse().ToImmutableArray() }).Failure!.Code);
        var element = source.Elements[0];
        var badProfile = element.Front with { Profile = new() { Kind = TopologyProfileKind.EvenAsphere, MaximumEvenPower = 18, Terms = [new(2, .001)] } };
        Assert.Equal(TopologyFailureCode.InvalidProfile, TopologyCompiler.TryCompile(source with { Elements = source.Elements.SetItem(0, element with { Front = badProfile }) }).Failure!.Code);
        Assert.Equal(TopologyFailureCode.TransitionNotAllowed, TopologyCompiler.TryCompile(source with { Elements = source.Elements.SetItem(0, element with { Transition = true }) }).Failure!.Code);
        Assert.Equal(TopologyFailureCode.UnsupportedMaterial, TopologyCompiler.TryCompile(source with { Elements = source.Elements.SetItem(0, element with { Material = "unknown-topology-test-glass" }) }).Failure!.Code);
        Assert.Equal(TopologyFailureCode.UnsupportedMaterial, TopologyCompiler.TryCompile(source, new() { AllowedGlassNames = ["N-BK7"] }).Failure!.Code);
    }

    private static DesignTopology Example(int count, bool asphere = false)
    {
        var first = Element("lens-1", 3, 45, -55, "N-BK7");
        if (asphere) first = first with
        {
            Front = first.Front with
            {
                Profile = first.Front.Profile with
                { Kind = TopologyProfileKind.EvenAsphere, Conic = -.25, MaximumEvenPower = 18, Terms = [new(4, 1e-7), new(10, -1e-14), new(18, 1e-22)] }
            }
        };
        var elements = ImmutableArray.Create(first, Element("lens-2", 10, -65, 70, "N-F2"));
        if (count == 3) elements = elements.Add(Element("lens-3", 18, 60, -50, "N-BK7"));
        return new() { Name = $"Autonomous topology foundation {count}", Stop = new("stop", 0, 2), Image = new("image", 55), Elements = elements };
    }

    private static LensElement Element(string id, double z, double frontRadius, double backRadius, string glass) => new()
    {
        Id = id,
        FrontSurfaceId = id + ":front",
        BackSurfaceId = id + ":back",
        FrontZMillimeters = z,
        ThicknessMillimeters = 2,
        Material = glass,
        Front = new() { Profile = new() { CurvaturePerMillimeter = 1 / frontRadius }, ClearSemiDiameterMillimeters = 8, MechanicalSemiDiameterMillimeters = 9 },
        Back = new() { Profile = new() { CurvaturePerMillimeter = 1 / backRadius }, ClearSemiDiameterMillimeters = 7.5 }
    };

    private static LensElement NewLens() => Element("new-lens", 0, 120, -120, "N-BK7");

    private static DesignTopology Shift(DesignTopology model, double shift) => model with
    {
        Reference = model.Reference with { ZMillimeters = model.Reference.ZMillimeters + shift },
        Stop = model.Stop with { ZMillimeters = model.Stop.ZMillimeters + shift },
        Image = model.Image with { ZMillimeters = model.Image.ZMillimeters + shift },
        Elements = model.Elements.Select(element => element with { FrontZMillimeters = element.FrontZMillimeters + shift }).ToImmutableArray()
    };

    // Independently prescribed surface table: no topology compiler, importer, parameter binder or inferred air gaps.
    private static Optic IndependentCoreAssembly(int count, bool asphere)
    {
        var optic = new Optic("Independent foundation reference");
        List<OpticalSurface> table =
        [
            new() { Label = "Object", Thickness = double.PositiveInfinity, Material = "Air" },
            new() { Label = "reference", Thickness = 0, Material = "Air" },
            new() { Label = "stop", Thickness = 3, Material = "Air", IsStop = true, SemiDiameter = 2 },
            new() { Label = "front1", Radius = 45, Thickness = 2, Material = "N-BK7", SemiDiameter = 8 },
            new() { Label = "back1", Radius = -55, Thickness = 5, Material = "Air", SemiDiameter = 7.5 },
            new() { Label = "front2", Radius = -65, Thickness = 2, Material = "N-F2", SemiDiameter = 8 },
            new() { Label = "back2", Radius = 70, Thickness = count == 3 ? 6 : 43, Material = "Air", SemiDiameter = 7.5 }
        ];
        if (count == 3)
        {
            table.Add(new() { Label = "front3", Radius = 60, Thickness = 2, Material = "N-BK7", SemiDiameter = 8 });
            table.Add(new() { Label = "back3", Radius = -50, Thickness = 35, Material = "Air", SemiDiameter = 7.5 });
        }
        table.Add(new() { Label = "image", Thickness = 0, Material = "Air" });
        optic.SurfaceGroup.ImportLegacySurfaces(table, optic.Materials);
        foreach (var surface in optic.SurfaceGroup.Items.Skip(2).SkipLast(1))
        {
            surface.PhysicalAperture = new CircularAperture(surface.SemiDiameter);
            surface.SemiDiameterFixed = true;
        }
        if (asphere) optic.SurfaceGroup.Items[3].Geometry = new EvenAsphereGeometry(45, -.25, [0, 1e-7, 0, 0, -1e-14, 0, 0, 0, 1e-22]);
        OpticalDesignParameters.SynchronizeIncidentMedia(optic);
        optic.Aperture.Kind = ApertureKind.FloatByStopSize; optic.Aperture.Value = 4;
        optic.Fields.Clear(); optic.Fields.Add(new() { Label = "Axis", Weight = 1 }); optic.Fields.Add(new() { Label = "Edge", YAngleDegrees = 5, Weight = 1 });
        optic.Wavelengths.Clear();
        optic.Wavelengths.Add(new() { Label = "F", Nanometers = 486.1, Weight = 1, IsPrimary = false });
        optic.Wavelengths.Add(new() { Label = "d", Nanometers = 587.6, Weight = 1, IsPrimary = true });
        optic.Wavelengths.Add(new() { Label = "C", Nanometers = 656.3, Weight = 1, IsPrimary = false });
        optic.RayAimingEnabled = true;
        return optic;
    }

    private static void AssertSameOptics(Optic expected, Optic actual)
    {
        Assert.Equal(expected.Paraxial.EstimateEffectiveFocalLength(), actual.Paraxial.EstimateEffectiveFocalLength(), 11);
        Assert.Equal(expected.Paraxial.EstimateEntrancePupilDiameter(), actual.Paraxial.EstimateEntrancePupilDiameter(), 11);
        var samples = ApertureSampler.GenerateGaussianQuadrature(4, 16);
        foreach (var field in new[] { 0.0, .5, 1.0 })
        {
            var a = SpotMetricEvaluator.EvaluatePupilSamples(expected, 0, field, samples, includeSurfaceTransmission: false);
            var b = SpotMetricEvaluator.EvaluatePupilSamples(actual, 0, field, samples, includeSurfaceTransmission: false);
            Assert.NotNull(a.Metrics); Assert.NotNull(b.Metrics);
            Assert.Equal(a.RayCount, b.RayCount); Assert.Equal(a.VignettedRayCount, b.VignettedRayCount);
            Assert.Equal(a.Metrics!.RmsSpotRadius, b.Metrics!.RmsSpotRadius, 11);
            Assert.Equal(a.Metrics.MaximumSpotRadius, b.Metrics.MaximumSpotRadius, 11);
            for (var wave = 0; wave < a.Wavelengths.Count; wave++)
            {
                Assert.Equal(a.Wavelengths[wave].Rays.Count, b.Wavelengths[wave].Rays.Count);
                for (var ray = 0; ray < a.Wavelengths[wave].Rays.Count; ray++)
                {
                    Assert.Equal(a.Wavelengths[wave].Rays[ray].X, b.Wavelengths[wave].Rays[ray].X, 11);
                    Assert.Equal(a.Wavelengths[wave].Rays[ray].Y, b.Wavelengths[wave].Rays[ray].Y, 11);
                }
            }
        }
    }

    private static void Failure(TopologyFailureCode expected, Action action) => Assert.Equal(expected, Assert.Throws<TopologyValidationException>(action).Code);

    private static void Evidence(string name, object value)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TOPOLOGY_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, JsonOptions));
    }
}
