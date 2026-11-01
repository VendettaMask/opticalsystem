using System.Text.Json;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

public sealed class ProductReportTests
{
    [Fact]
    public async Task OriginalF8AutomaticHandoffProducesPortableReadOnlyReportsWithoutAdditionalSearchWork()
    {
        // Original helper retains all original preparation caps and restricts only the subsequent main search to one trial.
        await new RealPipelineTests().OriginalF8StopsLoadsResumesAndHandsOffOnceBeforeQualifiedNativeReadback();
        var output = Environment.GetEnvironmentVariable("F8_REAL_PIPELINE_OUTPUT")!;
        await VerifyReport(output);
    }

    [Fact]
    public Task ReopenExistingOriginalProductEvidenceAndExportLatestReportsWithoutTracing() =>
        VerifyReport(Environment.GetEnvironmentVariable("F8_REAL_PIPELINE_OUTPUT")!);

    private static async Task VerifyReport(string output)
    {
        var mainPath = Directory.GetFiles(output, "*.autonomous.json").Single();
        var main = await new AutonomousSearchCheckpointStore().LoadAsync(mainPath);
        var preparationPath = Directory.GetFiles(output, "*.preparation.json", SearchOption.AllDirectories)
            .Single(path => Path.GetFileName(path) == main.RunId + ".preparation.json");
        var preparation = await new SeedPreparationCheckpointStore().LoadAsync(preparationPath);
        var mainHash = ContentFingerprint.Compute(main); var prepHash = ContentFingerprint.Compute(preparation);
        using var meter = SequentialTraceMeasurement.Begin();
        var study = AutonomousDesignStudy.Capture("Original F8 automatic product flow", main, preparation);
        var review = AutonomousDesignReview.Build(study);
        Assert.True(review.Candidates.Any(c => c.OriginalQualified));
        await AutonomousDesignReport.SaveMarkdownAsync(review, Path.Combine(output, "product-report.md"));
        var markdown = await File.ReadAllTextAsync(Path.Combine(output, "product-report.md"));
        Assert.Contains(preparation.PlanFingerprint, markdown); Assert.Contains(main.PlanFingerprint, markdown);
        await AutonomousDesignStudyFiles.SaveStudyAsync(study, Path.Combine(output, "product.design-study.json"));
        Assert.InRange(new FileInfo(Path.Combine(output, "product.design-study.json")).Length, 1, 64L * 1024 * 1024);
        await AutonomousDesignStudyFiles.SavePortfolioAsync(new() { Entries = [study] }, Path.Combine(output, "product.design-portfolio.json"));
        var loaded = await AutonomousDesignStudyFiles.LoadPortfolioAsync(Path.Combine(output, "product.design-portfolio.json"));
        Assert.Equal(ContentFingerprint.Compute(study), ContentFingerprint.Compute(loaded.Entries.Single()));
        Assert.Equal(mainHash, ContentFingerprint.Compute(main)); Assert.Equal(prepHash, ContentFingerprint.Compute(preparation));
        Assert.Equal(0, meter.RayCount);
        await File.WriteAllTextAsync(Path.Combine(output, "report-verification.json"), JsonSerializer.Serialize(new
        {
            MainFingerprint = mainHash, PreparationFingerprint = prepHash, OriginalQualifiedRecords = review.Candidates.Count(c => c.OriginalQualified),
            ReadOnlyReportRays = meter.RayCount, PlanAndAllBudgetCountersUnchanged = true, OriginalGatesUnchanged = true,
            MainWork = main.Consumed, PreparationWork = preparation.Consumed
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
