using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class OperandHelpTests
{
    [Fact]
    public void OptimizationServicePublishesCompleteOperandReferenceText()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var operands = application.Optimization.GetMeritOperandTypes();

        Assert.NotEmpty(operands);
        Assert.All(operands, operand =>
        {
            Assert.False(string.IsNullOrWhiteSpace(operand.Category));
            Assert.False(string.IsNullOrWhiteSpace(operand.Description));
            Assert.False(string.IsNullOrWhiteSpace(operand.Calculation));
        });

        var rsce = Assert.Single(operands, operand => operand.Code == "RSCE");
        Assert.False(rsce.CompatibilityOnly);
        Assert.Equal("像质与波前", rsce.Category);
        Assert.Contains("sqrt", rsce.Calculation, StringComparison.Ordinal);
        Assert.Contains("强度", rsce.Calculation, StringComparison.Ordinal);

        var dimx = Assert.Single(operands, operand => operand.Code == "DIMX");
        Assert.True(dimx.CompatibilityOnly);
        Assert.Equal("Zemax 兼容保留", dimx.Category);
        Assert.Contains("不会执行", dimx.Calculation, StringComparison.Ordinal);
    }

    [Fact]
    public void OperandReferenceProjectionSearchesDescriptionsCalculationsAndParameters()
    {
        var operands = new[]
        {
            Entry("RSCE", "RMS 点列半径", "点列定义", "像质与波前", "强度加权质心 sqrt"),
            Entry("DIMX", "最大畸变", "畸变定义", "Zemax 兼容保留", "不会执行", compatibilityOnly: true),
            Entry(
                "REAX",
                "实际光线 X",
                "光线定义",
                "实际光线",
                "追迹光线",
                parameters: [new MeritOperandParameterDto("Int1", "Surface", "Surface", "surface", true)])
        };

        Assert.Equal(
            "RSCE",
            Assert.Single(OperandHelpProjection.Filter(
                operands,
                "强度 质心",
                OperandHelpSupportFilter.All)).Code);
        Assert.Equal(
            "REAX",
            Assert.Single(OperandHelpProjection.Filter(
                operands,
                "Surface surface",
                OperandHelpSupportFilter.Executable)).Code);
        Assert.Equal(
            "DIMX",
            Assert.Single(OperandHelpProjection.Filter(
                operands,
                null,
                OperandHelpSupportFilter.CompatibilityOnly)).Code);
    }

    [Fact]
    public void OperandHelpIsARestorableWorkspaceDocument()
    {
        Assert.Equal("operand-help", WorkspaceDocumentTypes.OperandHelp);
        Assert.True(WorkspaceDocumentTypes.IsKnown(WorkspaceDocumentTypes.OperandHelp));
        var constructor = Assert.Single(typeof(OperandHelpPanel).GetConstructors());
        Assert.Equal(
            new[] { typeof(IOptimizationService) },
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task OperandHelpPanelRendersDetailsAndAdaptsToNarrowWidth()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OptilandWorkbench.App.App));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create("cooke");
            var panel = new OperandHelpPanel(application.Optimization);
            var window = new Window { Width = 1000, Height = 700, Content = panel };

            try
            {
                window.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                var renderedText = panel.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(text => text.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray();
                Assert.Contains(renderedText, text => text!.Contains("ABCD · Zemax ABCD", StringComparison.Ordinal));
                Assert.Contains(renderedText, text => text!.Contains("不会执行", StringComparison.Ordinal));

                var responsive = panel.GetVisualDescendants().OfType<ResponsiveTwoPaneGrid>().Single();
                Assert.False(responsive.IsNarrow);
                responsive.InvalidateMeasure();
                responsive.Measure(new Size(680, 600));
                Assert.True(responsive.IsNarrow);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static MeritOperandTypeDto Entry(
        string code,
        string name,
        string description,
        string category,
        string calculation,
        bool compatibilityOnly = false,
        IReadOnlyList<MeritOperandParameterDto>? parameters = null) =>
        new(
            code,
            name,
            description,
            parameters,
            compatibilityOnly,
            category,
            calculation);
}
