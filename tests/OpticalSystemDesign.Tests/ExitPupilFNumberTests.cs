using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class ExitPupilFNumberTests
{
    private const string DefocusedLens = """
        MODE SEQ
        UNIT MM
        ENPD 2
        FTYP 0 0 2 1 0 0 0
        YFLN 0 2
        WAVM 1 .5875618 1
        GLRS 1 1
        SURF 0
          DISZ INFINITY
        SURF 1
          TYPE PARAXIAL
          PARM 1 500
          PARM 2 1
          STOP
          DISZ 300
          DIAM 1 1 0 0 1
        SURF 2
        """;

    [Fact]
    public void ExitPupilModeUsesThePrescribedDistanceThroughImportSnapshotAndExport()
    {
        var imported = OpticalFormatCatalog.Import(DefocusedLens, ".zmx");
        var restored = Optic.FromSnapshot(imported.ToSnapshot());
        var exported = OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(restored, ".zmx"), ".zmx");
        foreach (var optic in new[] { imported, restored, exported })
        {
            Assert.True(optic.UseExitPupilFNumber);
            Assert.Equal(1, optic.GlobalReferenceSurfaceNumber);
            var wave = optic.Wavelengths[0];
            Assert.Equal(150, DiffractionEngine.WorkingFNumber(optic, (0, 0), wave), 10);
            Assert.Equal((150d, 150d), DiffractionEngine.WorkingFNumbers(optic, (0, 1), wave));
            var data = new FftPsfCrossSectionAnalysis(optic, 128, wavelengthNumber: 1).GenerateData();
            Assert.Equal(AnalysisOutcome.Success, data.Outcome);
            Assert.Equal(wave.Micrometers * 150 * 126 / 512, Assert.IsType<double>(data.Values["ImageDeltaMicrometers"]), 10);
        }
    }

    [Fact]
    public void OlderPrescriptionsAndSnapshotsDefaultToRayTracing()
    {
        var optic = OpticalFormatCatalog.Import(DefocusedLens.Replace("GLRS 1 1", "GLRS 1"), ".zmx");
        Assert.False(optic.UseExitPupilFNumber);
        Assert.False(Optic.FromSnapshot(optic.ToSnapshot()).UseExitPupilFNumber);
        var value = DiffractionEngine.WorkingFNumber(optic, (0, 0), optic.Wavelengths[0]);
        Assert.InRange(value, 250, 250.001);
    }

    [Fact]
    public void ContactPlaneLacksAFocalCurvatureAndDistortionReference()
    {
        const string plane = """
            MODE SEQ
            UNIT MM
            ENPD 10
            FTYP 0 0 2 1 0 0 0
            YFLN 0 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              DISZ 0
            SURF 2
            """;
        var optic = OpticalFormatCatalog.Import(plane, ".zmx");
        var data = new FieldCurvatureAndDistortionAnalysis(optic).GenerateData();
        Assert.Equal(AnalysisOutcome.NotApplicable, data.Outcome);
        Assert.Equal(FocalImageDomain.CollimatedPlaneTrain, data.Values[FocalImageDomain.ResultKey]);
        var energy = new DiffractionEncircledEnergyAnalysis(optic).GenerateData();
        Assert.Equal(AnalysisOutcome.NotApplicable, energy.Outcome);
        Assert.Equal(FocalImageDomain.CollimatedPlaneTrain, energy.Values[FocalImageDomain.ResultKey]);
        var separated = OpticalFormatCatalog.Import(plane.Replace("DISZ 0", "DISZ 10"), ".zmx");
        Assert.False(FocalImageDomain.IsCollimatedPlaneAtImage(separated));
    }
}
