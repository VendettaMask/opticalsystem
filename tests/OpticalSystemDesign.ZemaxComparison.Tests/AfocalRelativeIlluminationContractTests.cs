using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalRelativeIlluminationContractTests
{
    private static JsonElement Manifest() => JsonFiles.Read<JsonElement>(Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-ri-requests.json"));
    private static string Archive => Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-psf-contract/source-evidence.zip");
    private static readonly Lazy<bool> Verified = new(() =>
    {
        using var file = File.OpenRead(Archive);
        Assert.Equal(Manifest().GetProperty("sourceArchiveSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(file)));
        return true;
    });
    public static IEnumerable<object[]> Cases() => Manifest().GetProperty("cases").EnumerateArray().Select(r => new object[] { r.GetProperty("id").GetString()! });

    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalRequestKeepsItsIdentityAndCannotBecomeANumericalPass(string id)
    {
        Assert.True(Verified.Value);
        var row = Manifest().GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        Assert.Equal(row.GetProperty("fingerprint").GetString(), request.Fingerprint);
        using var zip = ZipFile.OpenRead(Archive);
        var model = Regex.Match(id, @"^\d{4}-(?<model>.+?)-primary-").Groups["model"].Value;
        using var input = zip.GetEntry("artifacts/validation/afocal-recompare-20261026/baseline-v4/models/" + model + ".json")!.Open();
        using var bytes = new MemoryStream(); input.CopyTo(bytes);
        Assert.Equal(row.GetProperty("snapshotSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(bytes.ToArray(), JsonFiles.Options)!);
        Assert.True(optic.ImageSpaceAfocal);
        var before = JsonSerializer.Serialize(optic.ToSnapshot(), JsonFiles.Options);
        var data = new WorkbenchRuntime(optic).BuildAnalysisData("Relative Illumination", request.WorkbenchSettings);
        Assert.Equal(AnalysisOutcome.NotApplicable, data.Outcome);
        Assert.Contains("afocal image space", data.OutcomeReason);
        Assert.Empty(data.PlotSeries);
        Assert.False(data.Values.ContainsKey("RelativeIllumination"));
        Assert.False(data.Values.ContainsKey("EffectiveFNumbers"));
        Assert.Equal(before, JsonSerializer.Serialize(optic.ToSnapshot(), JsonFiles.Options));
        using var native = zip.GetEntry("artifacts/validation/afocal-recompare-20261026/expanded/" + id + "/native/error.json")!.Open();
        using var nativeError = JsonDocument.Parse(native);
        Assert.Equal("NativeInvalidResult", nativeError.RootElement.GetProperty("errorCode").GetString());
        var path = Path.Combine(Path.GetTempPath(), "afocal-ri-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            JsonFiles.Write(path, data);
            Assert.Throws<InvalidDataException>(() => ResultNormalizer.Workbench(path, AnalysisComparisonRegistry.Get("Relative Illumination"), request));
        }
        finally { File.Delete(path); }
    }
}
