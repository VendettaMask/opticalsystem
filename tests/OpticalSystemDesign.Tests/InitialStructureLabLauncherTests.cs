using OpticalSystemDesign.Validation;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis.Assembly;
using OpticalSystemDesign.Core.Analysis.Imaging;
using CoatingDesignWindow = OpticalSystemDesign.App.Laboratories.CoatingDesign.MainWindow;
using InitialStructureWindow = OpticalSystemDesign.App.Laboratories.InitialStructure.MainWindow;

namespace OpticalSystemDesign.Tests;

public sealed class BuiltInModuleDeliveryTests
{
    [Fact]
    public void AllFourLaboratoriesBelongToTheMainExecutableAssembly()
    {
        Assert.Same(typeof(MainWindow).Assembly, typeof(InitialStructureWindow).Assembly);
        Assert.Same(typeof(MainWindow).Assembly, typeof(CoatingDesignWindow).Assembly);
        Assert.Same(typeof(MainWindow).Assembly, typeof(CameraImagingWindow).Assembly);
        Assert.Same(typeof(MainWindow).Assembly, typeof(OpticalAssemblyWindow).Assembly);
        var modules = ProductModules.Verify();
        Assert.True(modules.InProcess);
        Assert.Equal(new[] { "InitialStructure", "CoatingDesign", "CameraImaging", "OpticalAssembly" }, modules.Modules);
        Assert.Equal(7, modules.Libraries.Distinct().Count());
        Assert.Equal("OpticalSystemDesign.CameraImaging.Engine", typeof(CameraSensorPipeline).Assembly.GetName().Name);
        Assert.Equal("OpticalSystemDesign.OpticalAssembly.Engine", typeof(AssemblyConjugates).Assembly.GetName().Name);
    }

    [Fact]
    public void RootSolutionAndMainBuildIncludeEveryModule()
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var solution = EvidenceFiles.ReadAllText(Path.Combine(root, "OpticalSystemDesign.slnx"));
        var project = EvidenceFiles.ReadAllText(Path.Combine(root, "src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj"));
        var application = EvidenceFiles.ReadAllText(Path.Combine(root, "src/OpticalSystemDesign.Application/OpticalSystemDesign.Application.csproj"));
        foreach (var module in new[] { "InitialStructure.Engine", "InitialStructure.Persistence", "CoatingDesign.Engine" })
        {
            Assert.Contains("OpticalSystemDesign." + module, solution);
            Assert.Contains("OpticalSystemDesign." + module, project);
        }
        foreach (var lab in new[] { "CameraImaging", "OpticalAssembly" })
        {
            var labProject = $"labs/{lab}/src/OpticalSystemDesign.{lab}.Engine/OpticalSystemDesign.{lab}.Engine.csproj";
            Assert.Contains(labProject, solution);
            Assert.Contains(labProject, application);
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
        Assert.False(EvidenceFiles.Exists(Path.Combine(root, "labs", module, "src",
            "OpticalSystemDesign." + module + ".App", "OpticalSystemDesign." + module + ".App.csproj")));
        Assert.False(EvidenceFiles.Exists(Path.Combine(root, "src/OpticalSystemDesign.App/Services", module + "LabLauncher.cs")));
        Assert.DoesNotContain(typeof(MainWindow).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "OpticalSystemDesign." + module + ".App");
    }

    [Fact]
    public void PublishRejectsMissingModulesAndIncludesTheirNotices()
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var publish = EvidenceFiles.ReadAllText(Path.Combine(root, "scripts/publish-windows.ps1"));
        foreach (var library in ProductModules.Verify().Libraries) Assert.Contains(library + ".dll", publish);
        foreach (var notice in new[] { "TFStudio-LICENSE.txt", "tmmcore-LICENSE.txt", "MATERIAL-NOTICE.txt", "AutoLens-LICENSE.txt", "AutoLens-NOTICE.txt", "ILGPU-LICENSE.txt", "ILGPU-THIRD-PARTY-LICENSE.txt", "ILGPU-NOTICE.txt", "ILGPU-ALGORITHMS-LICENSE.txt", "ILGPU-ALGORITHMS-THIRD-PARTY-LICENSE.txt" })
            Assert.Contains(notice, publish);
        var installer = EvidenceFiles.ReadAllText(Path.Combine(root, "packaging/windows/OpticalSystemDesign.iss"));
        Assert.DoesNotContain("[Components]", installer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AutoLensCpuExperimentShipsExactLicenseAndHasNoForeignRuntimeReference()
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        foreach (var name in new[] { "AutoLens-LICENSE.txt", "AutoLens-NOTICE.txt" })
            Assert.Equal(EvidenceFiles.ReadAllBytes(Path.Combine(root, "third_party/autolens", name)),
                EvidenceFiles.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Licenses", name)));
        foreach (var name in new[] { "ILGPU-LICENSE.txt", "ILGPU-THIRD-PARTY-LICENSE.txt", "ILGPU-NOTICE.txt", "ILGPU-ALGORITHMS-LICENSE.txt", "ILGPU-ALGORITHMS-THIRD-PARTY-LICENSE.txt" })
            Assert.Equal(EvidenceFiles.ReadAllBytes(Path.Combine(root, "third_party/ilgpu", name)),
                EvidenceFiles.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Licenses", name)));
        Assert.True(EvidenceFiles.Exists(Path.Combine(AppContext.BaseDirectory, "ILGPU.dll")));
        Assert.True(EvidenceFiles.Exists(Path.Combine(AppContext.BaseDirectory, "ILGPU.Algorithms.dll")));
        var references = typeof(OpticalSystemDesign.InitialStructure.Engine.FlatStartSearchService).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name!.Contains("Python", StringComparison.OrdinalIgnoreCase)
            || reference.Name.Contains("Torch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LaboratoryEnginesUseTheSameProductCore()
    {
        var core = typeof(Optic).Assembly.GetName().Name;
        foreach (var library in new[]
        {
            typeof(OpticalSystemDesign.InitialStructure.Engine.FlatStartSearchService).Assembly,
            typeof(OpticalSystemDesign.CoatingDesign.Engine.DesignService).Assembly,
            typeof(CameraSensorPipeline).Assembly,
            typeof(AssemblyConjugates).Assembly
        })
            Assert.Contains(library.GetReferencedAssemblies(), reference => reference.Name == core);
    }
}
