using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.Application.Contracts;
using Dto = OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialFieldViewTests
{
    [Theory]
    [InlineData(179d)] [InlineData(-179d)] [InlineData(30d)]
    public void PhaseAndComplexComponentsComeFromOneFrozenFieldWithoutSummingModes(double phase)
    {
        var frame = Frame(phase); var frozen = JsonSerializer.Serialize(frame);
        var view = NonSequentialFieldViewBuilder.Extract(frame, 1, "laser", 0, NonSequentialFieldComponent.X, NonSequentialFieldViewKind.PhaseDegrees);
        var index = Array.FindIndex(view.Valid.ToArray(), v => v); Assert.True(index >= 0);
        Assert.Equal(phase, view.Values[index], 7);
        Assert.Contains(view.Valid, v => !v); Assert.All(view.Values.Where(double.IsNaN), v => Assert.True(double.IsNaN(v)));
        var real = NonSequentialFieldViewBuilder.Extract(frame, 1, "laser", 0, NonSequentialFieldComponent.X, NonSequentialFieldViewKind.Real);
        var imaginary = NonSequentialFieldViewBuilder.Extract(frame, 1, "laser", 0, NonSequentialFieldComponent.X, NonSequentialFieldViewKind.Imaginary);
        Assert.Equal(Math.Cos(phase * Math.PI / 180), real.Values[index], 8); Assert.Equal(Math.Sin(phase * Math.PI / 180), imaginary.Values[index], 8);
        Assert.Equal("sqrt(W)", real.Unit); Assert.Equal("deg", view.Unit);
        var second = NonSequentialFieldViewBuilder.Extract(frame, 1, "laser", 1, NonSequentialFieldComponent.X, NonSequentialFieldViewKind.PhaseDegrees);
        Assert.All(second.Valid, v => Assert.False(v)); Assert.All(second.Values, v => Assert.True(double.IsNaN(v)));
        Assert.Equal(frozen, JsonSerializer.Serialize(frame));
    }
    [Fact]
    public void ExactCancellationAndInvalidSelectionsHaveNoFabricatedPhase()
    {
        var frame = Frame(0, cancellation: true);
        var view = NonSequentialFieldViewBuilder.Extract(frame, 1, "laser", 0, NonSequentialFieldComponent.X, NonSequentialFieldViewKind.PhaseDegrees);
        Assert.All(view.Valid, v => Assert.False(v)); Assert.All(view.Values, v => Assert.True(double.IsNaN(v)));
        Assert.Throws<ArgumentException>(() => NonSequentialFieldViewBuilder.Extract(frame, 0, "laser", 0, NonSequentialFieldComponent.X, NonSequentialFieldViewKind.PhaseDegrees));
        Assert.Throws<ArgumentException>(() => NonSequentialFieldViewBuilder.Extract(frame, 1, "laser", 2, NonSequentialFieldComponent.X, NonSequentialFieldViewKind.PhaseDegrees));
        var original = frame.CoherentByWavelength![1];
        frame = frame with { CoherentByWavelength = new Dictionary<int, NonSequentialCoherentFrame>
            { [1] = original with { Modes = [original.Modes[0], original.Modes[0] with { CoherenceGroup = "other" }] } } };
        Assert.Throws<ArgumentException>(() => NonSequentialFieldViewBuilder.Extract(frame, 1, null, 0, NonSequentialFieldComponent.X, NonSequentialFieldViewKind.PhaseDegrees));
    }
    [Fact]
    public void PhaseDisplayKeepsWrappedAnglesHolesAndRawCsvMetadata()
    {
        var values = new[] { 179d, -179, double.NaN, 0 };
        var display = NonSequentialDetectorDisplay.Transform(values, 2, 2, "deg", DetectorDisplayNormalization.Absolute, 0, false, null, null, DetectorDisplayDomain.Phase);
        Assert.Equal(179, display.Values[0]); Assert.Equal(-179, display.Values[1]); Assert.True(double.IsNaN(display.Values[2]));
        Assert.Equal(-180, display.ValueMinimum); Assert.Equal(180, display.ValueMaximum);
        Assert.Throws<ArgumentException>(() => NonSequentialDetectorDisplay.Transform(values, 2, 2, "deg", DetectorDisplayNormalization.Absolute, 1, false, null, null, DetectorDisplayDomain.Phase));
        Assert.Throws<ArgumentException>(() => NonSequentialDetectorDisplay.Transform(values, 2, 2, "deg", DetectorDisplayNormalization.Peak, 0, false, null, null, DetectorDisplayDomain.Phase));
        var selection = new NonSequentialFieldSelection(1, "a,b", 0, NonSequentialFieldComponent.X, NonSequentialFieldViewKind.PhaseDegrees);
        var dto = new Dto.NonSequentialDetectorViewDto(Guid.NewGuid(), "test", 2, 2, -1, 1, -1, 1, "mm", "mm", "deg", values, [], [], new(1, 1, 179, 0, 0, 0, 0, 1), false, "test")
            { DataType = Dto.NonSequentialDetectorDataType.FieldPhase, FieldSelection = selection, ValidPixels = [true, true, false, true] };
        var csv = NonSequentialDetectorDisplay.ExportCsv(dto);
        Assert.Contains("#CoherenceGroup,\"a,b\"", csv); Assert.Contains("#ValueUnit,\"deg\"", csv); Assert.Contains("value,valid", csv);
        Assert.Contains(",,0", csv); Assert.DoesNotContain("NaN", csv);
        Assert.Equal((AnalysisAxisQuantity.Angle, AnalysisAxisUnit.Degree), NonSequentialDetectorDisplay.ValueAxis(Dto.NonSequentialDetectorDataType.FieldPhase, false));
        foreach (var type in Enum.GetValues<Dto.NonSequentialDetectorDataType>()) _ = NonSequentialDetectorDisplay.ValueAxis(type, false);
    }
    private static NonSequentialDetectorFrame Frame(double phase, bool cancellation = false)
    {
        var source = NonSequentialObjectDefinition.Create(Core.NonSequential.NonSequentialObjectKind.SourceRay) with
            { Parameters = new Core.NonSequential.SourceRayParameters { Physics = new(new(false, 1, 0), "laser", phase) } };
        var detector = NonSequentialObjectDefinition.Create(Core.NonSequential.NonSequentialObjectKind.DetectorRectangle) with
            { LocalCoordinateSystem = new(new(0, 0, 1)), Parameters = new Core.NonSequential.DetectorRectangleParameters(4, 4, 2, 2) };
        var objects = new List<NonSequentialObjectDefinition> { source, detector };
        if (cancellation) objects.Insert(1, source with { Id = Guid.NewGuid(), Parameters = new Core.NonSequential.SourceRayParameters { Physics = new(new(false, 1, 0), "laser", phase + 180) } });
        var doc = new NonSequentialDocument("phase", [new("test", 1000, 1, true)], objects, "Vacuum", new(UsePolarization: true, CoherentDetection: true));
        return new NonSequentialDocumentTracer().Trace(doc, Optic.CreateBlank().Materials).Detectors.Single();
    }
}
