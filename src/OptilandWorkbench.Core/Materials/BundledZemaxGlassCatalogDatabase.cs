using System.Reflection;

namespace OptilandWorkbench.Core.Materials;

internal static class BundledZemaxGlassCatalogDatabase
{
    private const string ResourceName =
        "OptilandWorkbench.Core.Materials.Data.zemax-glass-catalogs.ogdb";
    private static readonly object Gate = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_loaded)
            {
                return;
            }

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded glass database '{ResourceName}' was not found.");
            var bundle = OptilandGlassCatalogStore.LoadBundle(stream);
            foreach (var catalog in bundle.Catalogs)
            {
                ExternalGlassCatalogDatabase.RegisterIfMissing(catalog);
            }

            _loaded = true;
        }
    }
}
