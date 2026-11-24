using System.Reflection;
using System.Text.Json;

namespace OpticalSystemDesign.Tests;

public sealed class NumericalAcceptanceScopeTests
{
    [Fact]
    public void CompiledRetirementMetadataMatchesTheReviewedRegistry()
    {
        using var policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "acceptance-policy.json")));
        var expected = policy.RootElement.GetProperty("retiredTests").EnumerateArray()
            .Where(entry => entry.GetProperty("file").GetString()!.StartsWith("tests/OpticalSystemDesign.Tests/", StringComparison.Ordinal))
            .Select(entry => entry.GetProperty("type").GetString() + "." + entry.GetProperty("method").GetString()).Order().ToArray();
        var actual = typeof(NumericalAcceptanceScopeTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributesData().Any(attribute => attribute.AttributeType == typeof(TraitAttribute)
                && (string?)attribute.ConstructorArguments[0].Value == "AcceptanceScope"
                && (string?)attribute.ConstructorArguments[1].Value == "RetiredReference"))
            .Select(method => method.DeclaringType!.Name + "." + method.Name).Order().ToArray();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(typeof(MultiLensEncircledEnergyTests))]
    [InlineData(typeof(ExpandedSamplingEnergyTests))]
    [InlineData(typeof(PolychromaticCumulativeEnergyTests))]
    [InlineData(typeof(HighSamplingPolychromaticEnergyTests))]
    public void MixedArchivesKeepEverySelectedRowInExactlyOneScope(Type type)
    {
        static string Key(object[] row) => $"{row[0]}/{row[1]}";
        IEnumerable<object[]> Rows(string name, BindingFlags visibility) =>
            (IEnumerable<object[]>)type.GetMethod(name, visibility | BindingFlags.Static)!.Invoke(null, null)!;

        var all = Rows("AllNativeCurves", BindingFlags.NonPublic).Select(Key).ToHashSet();
        var current = Rows("NativeCurves", BindingFlags.Public).Select(Key).ToHashSet();
        var retired = Rows("RetiredNativeCurves", BindingFlags.Public).Select(Key).ToHashSet();
        Assert.NotEmpty(current);
        Assert.NotEmpty(retired);
        Assert.Empty(current.Intersect(retired));
        Assert.True(all.SetEquals(current.Concat(retired)));
        Assert.All(retired, row => Assert.True(row.StartsWith("0852", StringComparison.Ordinal)
            || row.StartsWith("matrix/0852", StringComparison.Ordinal)
            || row.StartsWith("controls/base-fnum", StringComparison.Ordinal)
            || row.StartsWith("slit-0852", StringComparison.Ordinal)));
        Assert.Contains(current, row => row.Contains("0014", StringComparison.Ordinal) || row.Contains("apo-poly", StringComparison.Ordinal));
    }
}
