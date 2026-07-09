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
                foreach (var type in assembly.GetTypes().Where(type => typeof(IOptilandPlugin).IsAssignableFrom(type) && !type.IsAbstract))
                {
                    if (Activator.CreateInstance(type) is IOptilandPlugin plugin)
                    {
                        plugin.Register(registry);
                    }
                }
            }
            catch (Exception ex)
            {
                registry.Warnings.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return registry;
    }
}
