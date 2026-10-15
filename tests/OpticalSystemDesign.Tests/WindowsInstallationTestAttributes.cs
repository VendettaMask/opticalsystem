namespace OpticalSystemDesign.Tests;

// Report an explicit platform skip outside Windows; do not count a no-op as
// successful validation of native sharing, process termination or NTFS junctions.
public sealed class WindowsInstallationFactAttribute : FactAttribute
{
    public WindowsInstallationFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires Windows file sharing and NTFS.";
    }
}

public sealed class WindowsInstallationTheoryAttribute : TheoryAttribute
{
    public WindowsInstallationTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires Windows file sharing and NTFS.";
    }
}
