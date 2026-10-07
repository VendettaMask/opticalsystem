using OptilandWorkbench.ZemaxComparison.Diagnostics;

namespace OptilandWorkbench.ZemaxComparison.Tests;

public sealed class RayAuditContractTests
{
    private static RayAuditJob Valid => new()
    {
        Source = "source.zmx", SourceSha256 = new string('a', 64), Output = "fresh-output",
        Fields = [[0, 0], [0, 1]]
    };

    [Theory]
    [InlineData("GQ", 0)]
    [InlineData("GQ", 21)]
    [InlineData("RA", 16)]
    [InlineData("RA", 1024)]
    [InlineData("unknown", 6)]
    public void UnsupportedIntegrationRulesCannotProduceAnAudit(string method, int density)
        => Assert.Throws<ArgumentException>(() => (Valid with { Method = method, RayDensity = density }).CreateSamples());

    [Fact]
    public void InvalidFieldScopeAndSourceIdentityAreRejected()
    {
        foreach (var job in new[] { Valid with { SourceSha256 = null! }, Valid with { SourceSha256 = "wrong" },
            Valid with { Fields = null! }, Valid with { Fields = [[double.NaN, 0]] },
            Valid with { Fields = [[1.1, 0]] }, Valid with { Fields = [[0]] }, Valid with { Configuration = 0 } })
            Assert.Throws<ArgumentException>(() => job.CreateSamples());
        Assert.Throws<ArgumentException>(() => Valid.ValidateSurfaceBudget(1));
        Assert.Throws<ArgumentException>(() => (Valid with { Method = "RA", RayDensity = 256 }).ValidateSurfaceBudget(100));
        Valid.ValidateSurfaceBudget(100);
    }

    [Fact]
    public async Task SourceHashMismatchDoesNotCreateAnEvidenceDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ray-audit-contract-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.zmx");
            await File.WriteAllTextAsync(source, "VERS 260127\nMODE SEQ\n");
            var job = Valid with { Source = source, Output = Path.Combine(root, "audit") };
            var path = Path.Combine(root, "job.json");
            JsonFiles.Write(path, job);
            await Assert.ThrowsAsync<InvalidDataException>(() => RayAuditRunner.Run(path, CancellationToken.None));
            Assert.False(Directory.Exists(job.Output));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UnknownJobMembersCannotSilentlyChangeAControl()
    {
        var path = Path.Combine(Path.GetTempPath(), "ray-audit-job-" + Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, "{\"source\":\"source.zmx\",\"sourceSha256\":\"" + new string('a',64)
                + "\",\"output\":\"output\",\"removeVignettingFactros\":true}");
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => RayAuditRunner.Run(path, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }
}
