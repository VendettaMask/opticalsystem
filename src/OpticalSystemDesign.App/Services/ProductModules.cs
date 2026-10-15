using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.App.Services;

internal sealed record ProductModuleVerification(string[] Modules, string[] Libraries, bool InProcess);

internal static class ProductModules
{
    // Strong type references make a missing mandatory module fail validation.
    // No filesystem discovery, optional loading or external process is involved.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static ProductModuleVerification Verify()
    {
        var product = typeof(MainWindow).Assembly;
        if (typeof(Laboratories.InitialStructure.MainWindow).Assembly != product
            || typeof(Laboratories.CoatingDesign.MainWindow).Assembly != product)
            throw new InvalidDataException("设计模块必须内置于主程序。");
        var libraries = new[]
        {
            typeof(FlatStartSearchService).Assembly,
            typeof(FlatStartRunLibrary).Assembly,
            typeof(OpticalSystemDesign.InitialStructure.Contracts.InitialStructureSpecification).Assembly,
            typeof(DesignService).Assembly,
            typeof(Modules.InitialStructurePresentation).Assembly
        };
        return new(["InitialStructure", "CoatingDesign"],
            libraries.Select(assembly => assembly.GetName().Name!).ToArray(), true);
    }
}
