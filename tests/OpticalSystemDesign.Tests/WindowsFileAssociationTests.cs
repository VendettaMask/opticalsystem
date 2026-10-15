using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.Tests;

public sealed class WindowsFileAssociationTests
{
    [Fact]
    public void StartupRequestFindsSampleAndStarOptDocument()
    {
        var request = StartupRequest.Parse(new[]
        {
            "--sample=cooke",
            @".\designs\camera.staropt"
        });

        Assert.Equal("cooke", request.Sample);
        Assert.Equal(
            Path.GetFullPath(@".\designs\camera.staropt"),
            request.DocumentPath);
    }

    [Fact]
    public void StartupRequestIgnoresUnassociatedDocuments()
    {
        var request = StartupRequest.Parse(new[]
        {
            "--trace",
            "camera.zmx"
        });

        Assert.Null(request.Sample);
        Assert.Null(request.DocumentPath);
    }

    [Fact]
    public void RegistrationUsesBundledIconAndExecutable()
    {
        var registration = WindowsStarOptFileAssociation.CreateRegistration(
            @"C:\Optical System Design\OpticalSystemDesign.App.exe",
            @"C:\Optical System Design\OpticalSystemDesign.App.dll",
            @"C:\Optical System Design");

        Assert.Equal(".staropt", registration.Extension);
        Assert.Equal(
            "\"C:\\Optical System Design\\Assets\\Brand\\AppIcon.ico\",0",
            registration.IconReference);
        Assert.Equal(
            "\"C:\\Optical System Design\\OpticalSystemDesign.App.exe\" \"%1\"",
            registration.OpenCommand);
    }

    [Fact]
    public void RegistrationSupportsDotnetHostedDevelopmentRuns()
    {
        var command = WindowsStarOptFileAssociation.BuildOpenCommand(
            @"C:\Program Files\dotnet\dotnet.exe",
            @"D:\Projects\opticalsystem\OpticalSystemDesign.App.dll");

        Assert.Equal(
            "\"C:\\Program Files\\dotnet\\dotnet.exe\" " +
            "\"D:\\Projects\\opticalsystem\\OpticalSystemDesign.App.dll\" \"%1\"",
            command);
    }

    [Fact]
    public void MacBundleDeclaresStarOptDocumentIcon()
    {
        var plist = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "macos-Info.plist"));

        Assert.Contains("<string>com.starlabs.staropt</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>staropt</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<key>CFBundleTypeIconFile</key>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>AppIcon.icns</string>", plist, StringComparison.Ordinal);
    }
}
