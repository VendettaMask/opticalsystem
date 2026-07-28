using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace OptilandWorkbench.App.Services;

internal static class WindowsStarOptFileAssociation
{
    internal const string Extension = ".staropt";
    internal const string ProgId = "STARLabs.OpticalSystemDesign.Project";
    internal const string FriendlyName = "Optical System Design Project";

    public static bool TryRegister()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var registration = CreateRegistration(
                Environment.ProcessPath,
                typeof(App).Assembly.Location,
                AppContext.BaseDirectory);
            if (RegisterForCurrentUser(registration))
            {
                NativeMethods.NotifyAssociationChanged();
            }

            return true;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException
                or IOException
                or System.Security.SecurityException)
        {
            // File association is a convenience. A restricted registry must not
            // prevent the application from starting.
            return false;
        }
    }

    internal static FileAssociationRegistration CreateRegistration(
        string? processPath,
        string assemblyPath,
        string baseDirectory)
    {
        var iconPath = string.Join(
            '\\',
            baseDirectory.TrimEnd('\\', '/'),
            "Assets",
            "Brand",
            "AppIcon.ico");
        var openCommand = BuildOpenCommand(processPath, assemblyPath);
        return new FileAssociationRegistration(
            Extension,
            ProgId,
            FriendlyName,
            $"{Quote(iconPath)},0",
            openCommand);
    }

    internal static string BuildOpenCommand(string? processPath, string assemblyPath)
    {
        if (!string.IsNullOrWhiteSpace(processPath)
            && !IsDotnetHost(processPath))
        {
            return $"{Quote(processPath)} \"%1\"";
        }

        var dotnetPath = string.IsNullOrWhiteSpace(processPath) ? "dotnet" : processPath;
        return $"{Quote(dotnetPath)} {Quote(assemblyPath)} \"%1\"";
    }

    private static bool IsDotnetHost(string processPath)
    {
        var separator = Math.Max(
            processPath.LastIndexOf('/'),
            processPath.LastIndexOf('\\'));
        var fileName = processPath[(separator + 1)..];
        return fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);
    }

    [SupportedOSPlatform("windows")]
    private static bool RegisterForCurrentUser(FileAssociationRegistration registration)
    {
        var changed = false;
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes", writable: true);
        if (classes is null)
        {
            return false;
        }

        using (var extensionKey = classes.CreateSubKey(registration.Extension, writable: true))
        {
            changed |= SetValueIfChanged(extensionKey, null, registration.ProgId);
            changed |= SetValueIfChanged(
                extensionKey,
                "Content Type",
                "application/vnd.starlabs.staropt");
        }

        using (var typeKey = classes.CreateSubKey(registration.ProgId, writable: true))
        {
            changed |= SetValueIfChanged(typeKey, null, registration.FriendlyName);
        }

        using (var iconKey = classes.CreateSubKey(
                   $@"{registration.ProgId}\DefaultIcon",
                   writable: true))
        {
            changed |= SetValueIfChanged(iconKey, null, registration.IconReference);
        }

        using (var commandKey = classes.CreateSubKey(
                   $@"{registration.ProgId}\shell\open\command",
                   writable: true))
        {
            changed |= SetValueIfChanged(commandKey, null, registration.OpenCommand);
        }

        return changed;
    }

    [SupportedOSPlatform("windows")]
    private static bool SetValueIfChanged(RegistryKey key, string? name, string value)
    {
        if (string.Equals(key.GetValue(name) as string, value, StringComparison.Ordinal))
        {
            return false;
        }

        key.SetValue(name, value, RegistryValueKind.String);
        return true;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static class NativeMethods
    {
        private const uint AssociationChanged = 0x08000000;
        private const uint IdList = 0x0000;

        [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")]
        private static extern void SHChangeNotify(
            uint eventId,
            uint flags,
            nint item1,
            nint item2);

        public static void NotifyAssociationChanged()
        {
            SHChangeNotify(AssociationChanged, IdList, 0, 0);
        }
    }
}

internal sealed record FileAssociationRegistration(
    string Extension,
    string ProgId,
    string FriendlyName,
    string IconReference,
    string OpenCommand);
