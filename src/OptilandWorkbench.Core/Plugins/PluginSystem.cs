using System.Reflection;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.Core.Plugins;

public interface IOptilandPlugin
{
    string Name { get; }

    void Register(PluginRegistry registry);
}

public sealed class PluginRegistry
{
    private readonly Dictionary<string, Func<IGeometry>> _geometries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<Optic, BaseAnalysis>> _analyses = new(StringComparer.OrdinalIgnoreCase);

    public MaterialRegistry Materials { get; } = new();

    public IReadOnlyDictionary<string, Func<IGeometry>> Geometries => _geometries;

    public IReadOnlyDictionary<string, Func<Optic, BaseAnalysis>> Analyses => _analyses;

    public List<string> Warnings { get; } = new();

    public void RegisterGeometry(string key, Func<IGeometry> factory) => _geometries[key] = factory;

    public void RegisterMaterial(IMaterial material) => Materials.Register(material);

    public void RegisterAnalysis(string key, Func<Optic, BaseAnalysis> factory) => _analyses[key] = factory;
}

public sealed class PluginLoader
{
    public PluginRegistry LoadFromDirectory(string directory)
    {
        var registry = new PluginRegistry();
        if (!Directory.Exists(directory))
        {
            return registry;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(file);
                LoadFromAssembly(assembly, registry);
            }
            catch (Exception ex)
            {
                registry.Warnings.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return registry;
    }

    public PluginRegistry LoadFromAssembly(Assembly assembly, PluginRegistry? registry = null)
    {
        registry ??= new PluginRegistry();
        foreach (var type in GetPluginTypes(assembly, registry))
        {
            try
            {
                if (Activator.CreateInstance(type) is IOptilandPlugin plugin)
                {
                    plugin.Register(registry);
                }
            }
            catch (Exception ex)
            {
                registry.Warnings.Add($"{assembly.GetName().Name}/{type.FullName}: {ex.GetBaseException().Message}");
            }
        }

        return registry;
    }

    private static IEnumerable<Type> GetPluginTypes(Assembly assembly, PluginRegistry registry)
    {
        try
        {
            return assembly
                .GetTypes()
                .Where(type => typeof(IOptilandPlugin).IsAssignableFrom(type)
                    && type is { IsAbstract: false, IsInterface: false }
                    && type.GetConstructor(Type.EmptyTypes) is not null)
                .ToArray();
        }
        catch (ReflectionTypeLoadException ex)
        {
            registry.Warnings.Add($"{assembly.GetName().Name}: {ex.Message}");
            return ex.Types
                .Where(type => type is not null
                    && typeof(IOptilandPlugin).IsAssignableFrom(type)
                    && type is { IsAbstract: false, IsInterface: false }
                    && type.GetConstructor(Type.EmptyTypes) is not null)
                .Cast<Type>()
                .ToArray();
        }
    }
}
