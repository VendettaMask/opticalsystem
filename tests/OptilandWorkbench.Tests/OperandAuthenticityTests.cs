using System.Text.Json;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Optimization;

namespace OptilandWorkbench.Tests;

public sealed class OperandAuthenticityTests
{
    [Fact]
    public void EveryRegisteredCodeExistsInIndependentOfficial2026R1ApiCatalog()
    {
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "zemax-2026-r1-official-merit-operand-catalog.json")));
        var official = source.RootElement.GetProperty("codes").EnumerateArray().Select(v => v.GetString()!).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(448, official.Count);
        Assert.All(ZemaxOperandRegistry.Descriptors, item => Assert.Contains(item.Code, official));
        var extensions = MeritFunctionCatalog.Types.Where(t => !official.Contains(t.Code)).Select(t => t.Code).Order().ToArray();
        Assert.Equal(new[] { "FNUM", "RADI", "RWFE", "THIC" }, extensions);
        Assert.All(extensions, code => Assert.False(ZemaxOperandRegistry.TryGet(code, out _)));
        var unused = source.RootElement.GetProperty("documentedUnused").EnumerateArray().Select(v => v.GetString()!).Order().ToArray();
        Assert.Equal(unused, ZemaxOperandRegistry.Descriptors.Where(d => ZemaxOperandRegistry.IsDocumentedUnused(d.Code)).Select(d => d.Code).Order());
    }

    [Theory]
    [InlineData("FNUM")]
    [InlineData("RADI")]
    [InlineData("RWFE")]
    [InlineData("THIC")]
    public void LocalExtensionsAreClearlyIdentifiedInPublishedEditorMetadata(string code)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == code);
        Assert.Equal("本程序扩展", type.Category); Assert.Contains("本程序", type.DisplayName);
        Assert.Contains("不是 Zemax", type.Calculation); Assert.Contains("本程序自定义", type.Description);
        Assert.False(type.CompatibilityOnly);
    }

    [Theory]
    [InlineData("BIPF")]
    [InlineData("COSA")]
    [InlineData("HACG")]
    [InlineData("QOAC")]
    [InlineData("TRAN")]
    public void DocumentedUnusedNamesStayPreservedButNeverClaimMissingComputations(string code)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == code);
        Assert.True(type.CompatibilityOnly); Assert.Equal("Zemax 未用名称", type.Category);
        Assert.Contains("未用", type.DisplayName); Assert.Contains("不列入待实现功能", type.Calculation);
        var value = MeritFunctionCatalog.Evaluate(Optic.CreateBlank(), new() { Type = code });
        Assert.NotEmpty(value.Error); Assert.True(double.IsNaN(value.Value));
    }

    [Theory]
    [InlineData("OGSS")]
    [InlineData("SPHD")]
    public void ApiOnlyNamesDoNotAcquireInventedDefinitions(string code)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == code);
        Assert.True(type.CompatibilityOnly); Assert.Equal("Zemax 定义待核实", type.Category);
        Assert.Contains("未取得公开计算定义", type.Calculation);
    }
}
