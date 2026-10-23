using System.Reflection;

namespace OpticalSystemDesign.App.Services;

internal static class ProductVersionInfo
{
    internal static long OtaBuildSequence { get; } =
        long.TryParse(typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "OtaBuildSequence")?.Value,
            System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var sequence) && sequence > 0 ? sequence : 0;

    internal static string Current { get; } = Format(
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        typeof(App).Assembly.GetName().Version);

    internal static string Format(string? informationalVersion, Version? assemblyVersion)
    {
        var version = informationalVersion?.Split('+', 2)[0].Trim();
        if (!string.IsNullOrWhiteSpace(version)) return version;
        return assemblyVersion is null
            ? "未知"
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{Math.Max(0, assemblyVersion.Build)}";
    }
}
