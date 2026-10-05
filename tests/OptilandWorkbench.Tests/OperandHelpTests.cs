using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class OperandHelpTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_OPERAND_HELP_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public void HierarchyCoversEveryPublishedCodeOnceAndSearchKeepsTheGrinFamily()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var operands = application.Optimization.GetMeritOperandTypes();
        var roots = OperandHelpProjection.BuildTree(operands);
        var leaves = roots.SelectMany(root => root.Children).SelectMany(family => family.Children).ToArray();
        Assert.Equal(operands.Count, leaves.Length);
        Assert.All(roots, node => Assert.Equal(OperandHelpLevel.Category, node.Level));
        Assert.All(roots.SelectMany(root => root.Children), node => Assert.Equal(OperandHelpLevel.Family, node.Level));
        Assert.All(leaves, node => Assert.Equal(OperandHelpLevel.Operand, node.Level));
        Assert.Equal(operands.Select(item => item.Code).Order(), leaves.Select(node => node.Operand!.Code).Order());
        Assert.DoesNotContain(roots.SelectMany(root => root.Children), family => family.Key == "unclassified");
        var materials = Assert.Single(roots, root => root.Title == "材料与介质");
        var grin = Assert.Single(materials.Children, family => family.Key == "grin-point");
        Assert.Equal(18, grin.Children.Count);
        Assert.All(grin.Operands, operand => Assert.False(operand.CompatibilityOnly));
        Assert.All(roots.SelectMany(root => root.Children), family => Assert.NotEmpty(family.Children));

        var matches = OperandHelpProjection.Filter(operands, "GRIN 前端 +X", OperandHelpSupportFilter.All);
        Assert.Equal(new[] { "I3GT", "I3LT", "I3VA" }, matches.Select(item => item.Code));
        var result = Assert.Single(OperandHelpProjection.BuildTree(matches));
        Assert.Equal("材料与介质", result.Title);
        Assert.Equal("grin-point", Assert.Single(result.Children).Key);
        Assert.Empty(OperandHelpProjection.Filter(operands, "I3GT", OperandHelpSupportFilter.CompatibilityOnly));
    }

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
        Assert.False(dimx.CompatibilityOnly);
        Assert.Equal("畸变与场曲", dimx.Category);
        Assert.Contains("指定视场", dimx.Calculation, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task OperandHelpPanelRendersDetailsAndAdaptsToNarrowWidth(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OperandHelpTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
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
                Assert.Contains(renderedText, text => text == "一阶与系统数据");
                Assert.Contains(renderedText, text => text!.Contains("焦距与光焦度", StringComparison.Ordinal));
                Capture("overview");

                var tree = panel.GetVisualDescendants().OfType<TreeView>().Single();
                var search = panel.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "搜索操作数");
                var support = panel.GetVisualDescendants().OfType<ComboBox>().Single();
                Assert.Contains("一级目录 · 功能大类", VisibleText());
                OpenChild("焦距与光焦度");
                Assert.Equal("focal", SelectedNode().Key);
                Assert.Contains("二级目录 · 操作数族", VisibleText());
                OpenChild("EFFL ·");
                Assert.Equal("EFFL", SelectedNode().Key);
                Assert.Contains("三级目录 · 具体操作数", VisibleText());
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Button>(), button =>
                    button.IsEffectivelyVisible && AutomationProperties.GetName(button)?.StartsWith("打开") == true);
                Assert.IsType<TreeViewItem>(tree.Items[0]).IsExpanded = false;

                search.Text = "I3GT";
                Refresh();
                var materialRoot = Assert.IsType<TreeViewItem>(Assert.Single(tree.Items));
                var grinGroup = Assert.IsType<TreeViewItem>(Assert.Single(materialRoot.Items));
                Assert.True(materialRoot.IsExpanded);
                Assert.True(grinGroup.IsExpanded);
                Assert.Contains("I3GT", AutomationProperties.GetName(Assert.IsType<TreeViewItem>(tree.SelectedItem)));
                Assert.Contains(VisibleText(), text => text!.Contains("可计算 · 范围见说明"));
                Assert.Contains(VisibleText(), text => text!.StartsWith("通用贡献"));
                Capture("grin-search");

                search.Text = string.Empty;
                Refresh();
                Assert.Contains("I3GT", AutomationProperties.GetName(Assert.IsType<TreeViewItem>(tree.SelectedItem)));
                materialRoot = tree.Items.OfType<TreeViewItem>().Single(item => (item.Tag as OperandHelpNode)?.Title == "材料与介质");
                grinGroup = materialRoot.Items.OfType<TreeViewItem>().Single(item => (item.Tag as OperandHelpNode)?.Key == "grin-point");
                tree.SelectedItem = grinGroup;
                grinGroup.Focus();
                grinGroup.IsExpanded = false;
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                Assert.True(grinGroup.IsExpanded);
                Refresh();
                Assert.Contains(VisibleText(), text => text!.Contains("6 个位置 × 3 种约束"));
                Assert.DoesNotContain(VisibleText(), text => text == "保留的原始参数");
                Capture("grin-family");
                var detailScroller = panel.GetVisualDescendants().OfType<ScrollViewer>()
                    .Single(item => AutomationProperties.GetName(item) == "操作数定义与计算说明");
                detailScroller.Offset = new Vector(0, 120);
                Refresh();
                Assert.True(detailScroller.Offset.Y > 0);
                OpenChild("I3GT ·");
                Assert.Equal(0, detailScroller.Offset.Y);
                Assert.Equal("I3GT", SelectedNode().Key);
                Assert.Contains("三级目录 · 具体操作数", VisibleText());
                Assert.DoesNotContain("保留的原始参数", VisibleText());

                search.Text = "I3GT";
                support.SelectedIndex = 2;
                Refresh();
                Assert.Empty(tree.Items);
                Assert.Contains(VisibleText(), text => text == "没有匹配的操作数");
                Assert.DoesNotContain(VisibleText(), text => text!.Contains("级目录"));
                support.SelectedIndex = 1;
                Refresh();
                Assert.NotEmpty(tree.Items);
                Assert.DoesNotContain(VisibleText(), text => text == "没有匹配的操作数");
                search.Text = "I3GT";
                Refresh();

                var responsive = panel.GetVisualDescendants().OfType<ResponsiveTwoPaneGrid>().Single();
                Assert.False(responsive.IsNarrow);
                responsive.InvalidateMeasure();
                responsive.Measure(new Size(680, 600));
                Assert.True(responsive.IsNarrow);
                window.Width = 680;
                panel.Width = 680;
                Refresh();
                Capture("narrow");

                string?[] VisibleText() => panel.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible).Select(text => text.Text).Where(text => text is not null).ToArray();

                OperandHelpNode SelectedNode() => Assert.IsType<OperandHelpNode>(Assert.IsType<TreeViewItem>(tree.SelectedItem).Tag);

                void OpenChild(string titlePrefix)
                {
                    var button = panel.GetVisualDescendants().OfType<Button>().Single(item =>
                        item.IsEffectivelyVisible && AutomationProperties.GetName(item)?.StartsWith($"打开{titlePrefix}", StringComparison.Ordinal) == true);
                    button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Refresh();
                }

                void Refresh()
                {
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                }

                void Capture(string name)
                {
                    var directory = Environment.GetEnvironmentVariable("OPTILAND_OPERAND_HELP_CAPTURE_DIR");
                    if (string.IsNullOrWhiteSpace(directory)) return;
                    Directory.CreateDirectory(directory);
                    Refresh();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, $"operand-help-{theme.ToLowerInvariant()}-{name}.png"), PngBitmapEncoderOptions.Default);
                }
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
