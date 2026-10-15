using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Core;
using InitialStructureWindow = OpticalSystemDesign.App.Laboratories.InitialStructure.MainWindow;
using CoatingDesignWindow = OpticalSystemDesign.App.Laboratories.CoatingDesign.MainWindow;

namespace OpticalSystemDesign.Tests;

public sealed class BuiltInModuleDeliveryTests
{
    [Fact]
    public void BothWorkspacesBelongToTheMainExecutableAssembly()
    {
        Assert.Same(typeof(MainWindow).Assembly, typeof(InitialStructureWindow).Assembly);
        Assert.Same(typeof(MainWindow).Assembly, typeof(CoatingDesignWindow).Assembly);
        var modules = ProductModules.Verify();
        Assert.True(modules.InProcess);
        Assert.Equal(new[] { "InitialStructure", "CoatingDesign" }, modules.Modules);
        Assert.Equal(5, modules.Libraries.Distinct().Count());
    }

    [Fact]
    public void RootSolutionAndMainBuildIncludeEveryModule()
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "OpticalSystemDesign.slnx"));
        var project = File.ReadAllText(Path.Combine(root, "src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj"));
        foreach (var module in new[] { "InitialStructure.Engine", "InitialStructure.Persistence", "CoatingDesign.Engine" })
        {
            Assert.Contains("OpticalSystemDesign." + module, solution);
            Assert.Contains("OpticalSystemDesign." + module, project);
        }
        Assert.Contains("OpticalSystemDesign.CoatingDesign.Tests", solution);
        Assert.Contains("OpticalSystemDesign.InitialStructure.Tests", solution);
    }

    [Theory]
    [InlineData("InitialStructure")]
    [InlineData("CoatingDesign")]
    public void IndependentApplicationsAndExternalLaunchersAreRetired(string module)
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        Assert.False(File.Exists(Path.Combine(root, "labs", module, "src",
            "OpticalSystemDesign." + module + ".App", "OpticalSystemDesign." + module + ".App.csproj")));
        Assert.False(File.Exists(Path.Combine(root, "src/OpticalSystemDesign.App/Services", module + "LabLauncher.cs")));
        Assert.DoesNotContain(typeof(MainWindow).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "OpticalSystemDesign." + module + ".App");
    }

    [Fact]
    public void PublishRejectsMissingModulesAndIncludesTheirNotices()
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var publish = File.ReadAllText(Path.Combine(root, "scripts/publish-windows.ps1"));
        foreach (var library in ProductModules.Verify().Libraries) Assert.Contains(library + ".dll", publish);
        foreach (var notice in new[] { "TFStudio-LICENSE.txt", "tmmcore-LICENSE.txt", "MATERIAL-NOTICE.txt" })
            Assert.Contains(notice, publish);
        var installer = File.ReadAllText(Path.Combine(root, "packaging/windows/OpticalSystemDesign.iss"));
        Assert.DoesNotContain("[Components]", installer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SearchAndCoatingUseTheSameProductCore()
    {
        var core = typeof(Optic).Assembly.GetName().Name;
        foreach (var library in new[]
        {
            typeof(OpticalSystemDesign.InitialStructure.Engine.FlatStartSearchService).Assembly,
            typeof(OpticalSystemDesign.CoatingDesign.Engine.DesignService).Assembly
        })
            Assert.Contains(library.GetReferencedAssemblies(), reference => reference.Name == core);
    }
}
