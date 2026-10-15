using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Tests;

public sealed class CadExportFreeCadFixtureTests
{
    [Fact]
    public void WritesFreeCadIntegrationFixturesWhenRequested()
    {
        var outputDirectory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_STEP_FIXTURE_DIR");
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return;
        }

        Directory.CreateDirectory(outputDirectory);
        Write(outputDirectory, "cooke-3.step", Optic.CreateCookeTriplet());

        var asphere = Optic.CreateCookeTriplet();
        asphere.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(
            40,
            -0.4,
            new[] { 2e-6, -1e-9 });
        asphere.SurfaceGroup.Items[1].SemiDiameter = 3;
        Write(outputDirectory, "asphere-3.step", asphere);

        var biconic = Optic.CreateCookeTriplet();
        var front = biconic.SurfaceGroup.Items[1];
        var back = biconic.SurfaceGroup.Items[2];
        front.Geometry = new BiconicGeometry(45, 55, -0.2, -0.1);
        front.SemiDiameter = 3;
        back.SemiDiameter = 4;
        front.CoordinateSystem = front.CoordinateSystem with
        {
            Origin = front.CoordinateSystem.Origin + new Vector3D(1.25, -0.75, 0),
            RotationXDegrees = 3,
            RotationYDegrees = -2
        };
        back.CoordinateSystem = back.CoordinateSystem with
        {
            Origin = back.CoordinateSystem.Origin + new Vector3D(1.25, -0.75, 0),
            RotationXDegrees = 3,
            RotationYDegrees = -2
        };
        Write(outputDirectory, "biconic-offset-3.step", biconic);

        Write(outputDirectory, "tessar-cemented-4.step", Optic.CreateTessarLens());
    }

    private static void Write(string directory, string fileName, Optic optic)
    {
        var document = StepCadExporter.Build(
            optic,
            new StepCadExportOptions(
                SurfaceSamples: 9,
                AngularSamples: 32,
                ProductName: Path.GetFileNameWithoutExtension(fileName),
                CreatedUtc: new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero)));
        File.WriteAllText(Path.Combine(directory, fileName), document.Content);
    }
}
