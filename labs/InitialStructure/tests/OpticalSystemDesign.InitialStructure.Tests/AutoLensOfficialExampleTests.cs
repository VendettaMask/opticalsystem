using System.Text.Json;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class AutoLensOfficialExampleTests
{
    [Theory]
    [InlineData(UiLocalization.English)]
    [InlineData(UiLocalization.Japanese)]
    public void PresetNamesNotesAndNewCommandsHaveTranslations(string language)
    {
        foreach (var preset in AutoLensOfficialExamples.Create())
        {
            Assert.NotEqual(preset.Name, UiLocalization.Text(preset.Name, language));
            Assert.NotEqual(preset.Notes, UiLocalization.Text(preset.Notes, language));
        }
        foreach (var text in new[] { "AutoLens 官方目标预设", "载入预设", "已载入目标预设；尚未运行，不代表设计达标。" })
            Assert.NotEqual(text, UiLocalization.Text(text, language));
    }

    [Fact]
    public void EveryPresetHasValidNativeTargetsRealGlassesAndExplicitBoundedProtocol()
    {
        var presets = AutoLensOfficialExamples.Create();
        Assert.Equal(5, presets.Count);
        Assert.Equal(24, presets.Sum(preset => preset.ValidationSeeds.Count));
        Assert.Equal(5, presets.Select(preset => preset.Id).Distinct().Count());
        foreach (var preset in presets)
        {
            SpecificationValidator.Validate(preset.Specification);
            DesignFormSearch.Validate(preset.Specification, preset.Options.DesignSearch!);
            var preflight = FlatStartSearchService.Preflight(preset.Specification, preset.Options);
            Assert.Equal(preset.Options.AllowedGlassNames, preflight.UsableGlassNames);
            Assert.Equal(FlatStartOptimizationMethod.AutoLensManagedV1, preset.Specification.FlatStart!.OptimizationMethod);
            Assert.True(preset.Specification.FlatStart.UsePhysicalStop);
            Assert.Equal(4000, preset.Specification.Budget.MaximumEvaluations);
            Assert.Equal(TimeSpan.FromSeconds(60), preset.Specification.Budget.TimeLimit);
            Assert.Equal(preset.ValidationSeeds[0], preset.Specification.Budget.RandomSeed);
            var loaded = JsonSerializer.Deserialize<AutoLensExamplePreset>(JsonSerializer.Serialize(preset))!;
            Assert.Equal(ContentFingerprint.Compute(preset), ContentFingerprint.Compute(loaded));
            Assert.False(string.IsNullOrWhiteSpace(preset.Notes));
        }
    }

    [Fact]
    public void PublishedTargetsAndLocallyChosenSeedsAreNotSilentlyChanged()
    {
        var presets = AutoLensOfficialExamples.Create();
        var wide = presets.Single(preset => preset.Id == "wide-80");
        Assert.Equal(4.55, wide.Specification.EffectiveFocalLengthMillimeters);
        Assert.Equal(2, wide.Specification.FNumber);
        Assert.Equal(40, wide.Specification.MaximumFieldAngleDegrees);
        var seeds = presets.Single(preset => preset.Id == "wide-80-seeds");
        Assert.Equal(Enumerable.Range(1, 20).Select(seed => (long)seed), seeds.ValidationSeeds);
        Assert.Equal(ContentFingerprint.Compute(wide.Specification with { Name = seeds.Name, Budget = seeds.Specification.Budget }),
            ContentFingerprint.Compute(seeds.Specification));
        var camera = presets.Single(preset => preset.Id == "full-frame-50");
        Assert.Equal(50, camera.Specification.EffectiveFocalLengthMillimeters);
        Assert.Equal(2, camera.Specification.FNumber); // Body text, not the inconsistent F/3 alt text.
        Assert.Equal(43.3, camera.SensorDiagonalMillimeters);
        Assert.Equal(Math.Atan(43.3 / 100) * 180 / Math.PI, camera.Specification.MaximumFieldAngleDegrees, 12);
        Assert.Equal(1.2, presets.Single(preset => preset.Id == "ultra-camera-f12").Specification.FNumber);
        Assert.Equal(60, presets.Single(preset => preset.Id == "ultra-mobile-120").Specification.MaximumFieldAngleDegrees);
    }

    [Fact]
    public void EditorsCannotMutateTheNextCatalogInstance()
    {
        var first = AutoLensOfficialExamples.Create()[0];
        var next = AutoLensOfficialExamples.Create()[0];
        Assert.NotSame(first.Options.AllowedGlassNames, next.Options.AllowedGlassNames);
        Assert.NotSame(first.Specification.Wavelengths, next.Specification.Wavelengths);
        Assert.Equal("N-BK7", next.Options.AllowedGlassNames[0]);
    }
}
