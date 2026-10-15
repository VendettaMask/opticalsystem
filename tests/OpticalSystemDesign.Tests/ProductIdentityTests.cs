using System.Xml.Linq;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class ProductIdentityTests
{
    [Fact]
    public void ProductCodeBuildAndLaunchInputsDoNotUseTheRetiredBrand()
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var roots = new[] { "src", "labs/InitialStructure/src", "labs/CoatingDesign/src", "packaging", "scripts", ".github" };
        var inputs = roots.SelectMany(directory => Directory.EnumerateFiles(Path.Combine(root, directory), "*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(root))
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => Path.GetExtension(path) is ".cs" or ".csproj" or ".slnx" or ".props" or ".targets"
                or ".cmd" or ".command" or ".ps1" or ".sh" or ".iss" or ".plist" or ".yml" or ".axaml")
            .ToArray();
        Assert.NotEmpty(inputs);
        Assert.All(inputs, path =>
        {
            Assert.DoesNotContain(RetiredProductFixture.Brand, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(RetiredProductFixture.Brand, File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ProductAssembliesAndEmbeddedResourceNamesUseCurrentIdentity()
    {
        foreach (var assembly in new[] { typeof(Optic).Assembly, typeof(AppSettings).Assembly,
                     typeof(OpticalSystemDesign.Application.Services.WorkbenchApplication).Assembly })
        {
            Assert.StartsWith("OpticalSystemDesign.", assembly.GetName().Name);
            Assert.All(assembly.GetReferencedAssemblies(), reference =>
                Assert.DoesNotContain(RetiredProductFixture.Brand, reference.Name!, StringComparison.OrdinalIgnoreCase));
            Assert.All(assembly.GetManifestResourceNames(), resource =>
                Assert.DoesNotContain(RetiredProductFixture.Brand, resource, StringComparison.OrdinalIgnoreCase));
        }
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var solution = XDocument.Load(Path.Combine(root, "OpticalSystemDesign.slnx"));
        Assert.Equal(19, solution.Descendants("Project").Count());
        Assert.All(solution.Descendants("Project"), project => Assert.True(File.Exists(
            Path.Combine(root, project.Attribute("Path")!.Value))));
    }

    [Fact]
    public void ProfilesAndRecoveryUseOnlyTheCurrentProductDirectory()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpticalSystemDesign");
        var workspace = new WorkspaceSessionStore();
        Assert.Equal(Path.Combine(root, "workspace-default.json"), workspace.DefaultLayoutPath);
        Assert.Equal(Path.Combine(root, "Recovery"), DocumentRecoveryStore.DefaultDirectory);
    }

    [Fact]
    public void RetiredIdentityCompatibilityTableAndTypesAreNotShipped()
    {
        var core = typeof(Optic).Assembly;
        Assert.Null(core.GetType("OpticalSystemDesign.Core.Serialization.LegacyProductIdentity"));
        Assert.DoesNotContain(core.GetManifestResourceNames(), name => name.EndsWith("legacy-product.ini", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), "packaging", "legacy-product.ini")));
    }
}
