using System.Numerics;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class PupilFoucaultExtensionTests
{
    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(-1, 1, 1.4)]
    public void ScalarPlaneWaveHasAnalyticPhaseAndNoArtificialPowerLoss(int fx, int fy, double index)
    {
        const int n = 8; const double pitch = .6, lambda = 1, distance = .37;
        var field = ScalarMode(n, fx, fy);
        var output = AngularSpectrumEngine.PropagateScalar(field, pitch, pitch, lambda, index, distance);
        var kz = Math.Sqrt(Math.Pow(2 * Math.PI * index / lambda, 2) - Math.Pow(2 * Math.PI * fx / (n * pitch), 2)
            - Math.Pow(2 * Math.PI * fy / (n * pitch), 2));
        var phase = Complex.Exp(Complex.ImaginaryOne * kz * distance);
        for (var r = 0; r < n; r++)
        for (var c = 0; c < n; c++) Assert.InRange((output[r, c] - field[r, c] * phase).Magnitude, 0, 3e-13);
    }

    [Fact]
    public void ScalarRandomFieldMatchesIndependentDirectFourierPropagation()
    {
        var input = new Complex[8, 8];
        for (var r = 0; r < 8; r++)
        for (var c = 0; c < 8; c++) input[r, c] = Complex.FromPolarCoordinates(.3 + .02 * r * c, .7 * r - .31 * c);
        var actual = AngularSpectrumEngine.PropagateScalar(input, .18, .27, .7, 1.2, .15);
        var expected = DirectScalar(input, .18, .27, .7, 1.2, .15);
        for (var r = 0; r < 8; r++)
        for (var c = 0; c < 8; c++) Assert.InRange((actual[r, c] - expected[r, c]).Magnitude, 0, 2e-12);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EvanescentModeDecaysInsteadOfGrowingAndCarriesNoNormalFlux(bool vector)
    {
        const double pitch = .1, distance = .2;
        var input = ScalarMode(8, 1, 0);
        var decay = Math.Exp(-Math.Sqrt(Math.Pow(2 * Math.PI / (8 * pitch), 2) - 4 * Math.PI * Math.PI) * distance);
        if (!vector)
        {
            var actual = AngularSpectrumEngine.PropagateScalar(input, pitch, pitch, 1, 1, distance);
            Assert.Equal(decay, actual[0, 0].Magnitude, 12);
        }
        else
        {
            var field = VectorMode(input, new(0, 1, 0));
            var actual = AngularSpectrumEngine.PropagateVector(field, pitch, pitch, 1, 1, distance, false);
            Assert.Equal(decay, actual.Electric[0, 0].Y.Magnitude, 12);
            Assert.InRange(Math.Abs(actual.NormalFluxIntegral), 0, 2e-13);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void ObliquePWaveRetainsLongitudinalFieldAndCorrectNormalPoyntingFlux(int order)
    {
        const int n = 8; const double pitch = .2, lambda = 1, distance = .4;
        var sx = order * lambda / (n * pitch); var cz = Math.Sqrt(1 - sx * sx);
        var input = VectorMode(ScalarMode(n, order, 0), new(cz, 0, -sx));
        var actual = AngularSpectrumEngine.PropagateVector(input, pitch, pitch, lambda, 1, distance, false);
        var phase = Complex.Exp(Complex.ImaginaryOne * 2 * Math.PI * cz * distance / lambda);
        for (var r = 0; r < n; r++)
        for (var c = 0; c < n; c++)
        {
            Assert.InRange((actual.Electric[r, c].X - input[r, c].X * phase).Magnitude, 0, 2e-13);
            Assert.InRange((actual.Electric[r, c].Z - input[r, c].Z * phase).Magnitude, 0, 2e-13);
            Assert.InRange((sx * actual.Electric[r, c].X + cz * actual.Electric[r, c].Z).Magnitude, 0, 2e-13);
        }
        Assert.Equal(n * n * pitch * pitch, actual.ElectricNormIntegral, 12);
        Assert.Equal(n * n * pitch * pitch * cz, actual.NormalFluxIntegral, 12);
    }

    [Fact]
    public void ExplicitProjectionUsesAllThreeInputComponentsAndStrictModeRejectsInconsistentFields()
    {
        var input = VectorMode(ScalarMode(8, 1, 0), new(0, 0, 1));
        Assert.Throws<ArgumentException>(() => AngularSpectrumEngine.PropagateVector(input, .2, .2, 1, 1, 0, false));
        var result = AngularSpectrumEngine.PropagateVector(input, .2, .2, 1, 1, 0);
        const double sx = .625; var cz = Math.Sqrt(1 - sx * sx);
        Assert.Equal(-sx * cz, result.Electric[0, 0].X.Real, 12);
        Assert.Equal(sx * sx, result.Electric[0, 0].Z.Real, 12);
        Assert.InRange((sx * result.Electric[0, 0].X + cz * result.Electric[0, 0].Z).Magnitude, 0, 1e-13);
        Assert.True(result.ProjectionDifferenceNormIntegral > 0);
        var again = AngularSpectrumEngine.PropagateVector(result.Electric, .2, .2, 1, 1, 0, false);
        Assert.InRange(again.ProjectionDifferenceNormIntegral, 0, 1e-24);
    }

    [Fact]
    public void HomogeneousAbsorptionMatchesAnalyticComplexWaveNumberWithoutAnInterfaceFallback()
    {
        var field = VectorMode(ScalarMode(8, 0, 0), new(1, 0, 0));
        var index = new Complex(1.3, .07); const double distance = .4;
        var result = AngularSpectrumEngine.PropagateVector(field, .5, .5, 1, index, distance, false);
        var expected = Complex.Exp(Complex.ImaginaryOne * 2 * Math.PI * index * distance);
        Assert.InRange((result.Electric[0, 0].X - expected).Magnitude, 0, 1e-13);
        Assert.Equal(16 * 1.3 * expected.Magnitude * expected.Magnitude, result.NormalFluxIntegral, 12);
    }

    [Fact]
    public void ScalarDistanceCompositionAndCommonPhysicalScalePreserveTheField()
    {
        var input = ScalarMode(8, -1, 1);
        var direct = AngularSpectrumEngine.PropagateScalar(input, .6, .7, 1, 1.3, .3);
        var first = AngularSpectrumEngine.PropagateScalar(input, .6, .7, 1, 1.3, .1);
        var composed = AngularSpectrumEngine.PropagateScalar(first, .6, .7, 1, 1.3, .2);
        var scaled = AngularSpectrumEngine.PropagateScalar(input, 6, 7, 10, 1.3, 3);
        for (var r = 0; r < 8; r++)
        for (var c = 0; c < 8; c++)
        {
            Assert.InRange((direct[r, c] - composed[r, c]).Magnitude, 0, 3e-13);
            Assert.InRange((direct[r, c] - scaled[r, c]).Magnitude, 0, 3e-13);
        }
    }

    [Fact]
    public void PropagatorsDoNotMutateInputsAndRejectInvalidOrBackwardData()
    {
        var input = ScalarMode(8, 1, 0); var saved = (Complex[,])input.Clone();
        _ = AngularSpectrumEngine.PropagateScalar(input, .4, .4, 1, 1, .3);
        Assert.Equal(saved.Cast<Complex>(), input.Cast<Complex>());
        Assert.Throws<ArgumentOutOfRangeException>(() => AngularSpectrumEngine.PropagateScalar(input, .4, .4, 1, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AngularSpectrumEngine.PropagateScalar(input, .4, .4, 1, new(1, -.1), 1));
        Assert.Throws<ArgumentException>(() => AngularSpectrumEngine.PropagateScalar(new Complex[8, 16], .4, .4, 1, 1, 1));
        input[1, 1] = new(double.NaN, 0);
        Assert.Throws<ArgumentException>(() => AngularSpectrumEngine.PropagateScalar(input, .4, .4, 1, 1, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PropagationHonorsCancellation(bool vector)
    {
        using var scope = ComputationCancellation.Push(new CancellationToken(true));
        if (vector) Assert.Throws<OperationCanceledException>(() => AngularSpectrumEngine.PropagateVector(new ComplexElectricField[8, 8], .4, .4, 1, 1, 1));
        else Assert.Throws<OperationCanceledException>(() => AngularSpectrumEngine.PropagateScalar(new Complex[8, 8], .4, .4, 1, 1, 1));
    }

    [Theory]
    [InlineData(FoucaultKnifeEdge.HorizontalAbove, 1)]
    [InlineData(FoucaultKnifeEdge.HorizontalBelow, -1)]
    [InlineData(FoucaultKnifeEdge.VerticalLeft, -1)]
    [InlineData(FoucaultKnifeEdge.VerticalRight, 1)]
    public void AfocalSpatialKnifeSupportsAllDirectionsAndOpenClosedLimits(FoucaultKnifeEdge knife, int sign)
    {
        var optic = CollimatedPlate(); var wave = optic.Wavelengths.Single();
        foreach (var mode in new[] { FoucaultPropagationMode.SpatialAngularSpectrum, FoucaultPropagationMode.VectorAngularSpectrum })
        {
            var open = FoucaultEngine.Compute(optic, (0, 0), wave, 16, knife, sign * 1e6,
                propagationMode: mode, knifeDistanceMillimeters: 2, observationDistanceMillimeters: 3);
            var dark = FoucaultEngine.Compute(optic, (0, 0), wave, 16, knife, -sign * 1e6, propagationMode: mode);
            Assert.Equal(mode, open.PropagationMode); Assert.Equal(0, dark.OutputPower);
            Assert.All(dark.Intensity.Cast<double>(), value => Assert.Equal(0, value));
            Assert.Equal(open.InputPower, open.OutputPower, 9);
            Assert.True(open.ObservationHalfWidthMicrometers > 0);
        }
    }

    [Fact]
    public void AfocalUnpolarizedVectorShadowIsTheIncoherentMeanOfSystemInputStates()
    {
        var optic = CollimatedPlate(); var wave = optic.Wavelengths.Single();
        FoucaultResult Run() => FoucaultEngine.Compute(optic, (0, 0), wave, 16, FoucaultKnifeEdge.VerticalLeft, 0,
            true, FoucaultPropagationMode.VectorAngularSpectrum, 1, 2);
        optic.Polarization = new(Unpolarized: true); var mean = Run();
        optic.Polarization = new(Unpolarized: false, Jx: 1, Jy: 0); var x = Run();
        optic.Polarization = new(Unpolarized: false, Jx: 0, Jy: 1); var y = Run();
        for (var r = 0; r < 16; r++)
        for (var c = 0; c < 16; c++) Assert.Equal((x.Intensity[r, c] + y.Intensity[r, c]) / 2, mean.Intensity[r, c], 10);
        Assert.Equal((x.OutputPower + y.OutputPower) / 2, mean.OutputPower, 10);
        Assert.Equal(6, mean.CoherentChannels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpatialCoatingAndBulkTransmissionEnterExactlyOnce(bool vector)
    {
        var optic = CollimatedPlate(); var wave = optic.Wavelengths.Single();
        var mode = vector ? FoucaultPropagationMode.VectorAngularSpectrum : FoucaultPropagationMode.SpatialAngularSpectrum;
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(.6);
        var a = FoucaultEngine.Compute(optic, (0, 0), wave, 16, FoucaultKnifeEdge.HorizontalAbove, 0, vector, mode);
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(.3);
        var b = FoucaultEngine.Compute(optic, (0, 0), wave, 16, FoucaultKnifeEdge.HorizontalAbove, 0, vector, mode);
        Assert.Equal(a.InputPower / 2, b.InputPower, 9); Assert.Equal(a.OutputPower / 2, b.OutputPower, 9);
        for (var r = 0; r < 16; r++)
        for (var c = 0; c < 16; c++) Assert.Equal(a.Intensity[r, c] / 2, b.Intensity[r, c], 9);
    }

    [Fact]
    public void SpatialScalarBulkAbsorptionIsNotAppliedAgainDuringOutputPropagation()
    {
        var optic = CollimatedPlate(); var wave = optic.Wavelengths.Single();
        FoucaultResult Run() => FoucaultEngine.Compute(optic, (0, 0), wave, 16,
            FoucaultKnifeEdge.VerticalRight, 1e6, propagationMode: FoucaultPropagationMode.SpatialAngularSpectrum,
            knifeDistanceMillimeters: 2, observationDistanceMillimeters: 3);
        var transparent = Run();
        const double extinction = 1e-7;
        var material = new ConstantIndexMaterial("scalar absorption fixture", 1.5, extinction);
        optic.Materials.Register(material); optic.SurfaceGroup.Items[1].MaterialAfter = material;
        optic.SurfaceGroup.Items[2].MaterialBefore = material;
        var absorbing = Run();
        var expected = Math.Exp(-4 * Math.PI * extinction * 2 / (wave.Micrometers * .001));
        Assert.Equal(expected, absorbing.InputPower / transparent.InputPower, 10);
        Assert.Equal(expected, absorbing.OutputPower / transparent.OutputPower, 10);
    }

    [Fact]
    public void SpatialParametersHavePhysicalUnitsAndDoNotChangeTheLensMode()
    {
        var optic = CollimatedPlate();
        var result = new FoucaultAnalysis(optic, sampling: 16, propagationMode: FoucaultPropagationMode.VectorAngularSpectrum,
            knifeDistanceMillimeters: 2, observationDistanceMillimeters: 3).GenerateData();
        Assert.True(optic.ImageSpaceAfocal);
        Assert.Equal(AnalysisAxisUnit.Micrometer, result.Series!.XUnit);
        Assert.Equal(AnalysisAxisQuantity.Coordinate, result.Series.YQuantity);
        Assert.Equal("相对电场能量密度", result.Series.ValueLabel);
        Assert.Equal("VectorAngularSpectrum", result.Values["PropagationMode"]);
        Assert.Contains("Poynting", result.Values["SpatialPowerModel"].ToString());
        Assert.Equal(2d, result.Values["KnifeDistanceMillimeters"]);
        Assert.Equal(3d, result.Values["ObservationDistanceMillimeters"]);
        Assert.Throws<NotSupportedException>(() => new FoucaultAnalysis(optic, sampling: 16).GenerateData());
        Assert.Throws<ArgumentException>(() => FoucaultEngine.Compute(optic, (0, 0), optic.Wavelengths.Single(), 16,
            FoucaultKnifeEdge.VerticalLeft, 0, true, FoucaultPropagationMode.SpatialAngularSpectrum));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FoucaultAnalysis(optic, knifeDistanceMillimeters: -1));
    }

    [Fact]
    public void AbsorbingInterfaceAndInsufficientPhysicalSamplingRemainExplicitErrors()
    {
        var optic = CollimatedPlate(); var wave = optic.Wavelengths.Single();
        var absorbing = new ConstantIndexMaterial("absorbing fixture", 1.5, .001);
        optic.Materials.Register(absorbing); optic.SurfaceGroup.Items[1].MaterialAfter = absorbing;
        optic.SurfaceGroup.Items[2].MaterialBefore = absorbing;
        Assert.Throws<NotSupportedException>(() => FoucaultEngine.Compute(optic, (0, 0), wave, 16,
            FoucaultKnifeEdge.VerticalLeft, 0, true, FoucaultPropagationMode.VectorAngularSpectrum));
        var focused = LensWithCurvedStop(); focused.ImageSpaceAfocal = true;
        Assert.Throws<AnalysisDataUnavailableException>(() => FoucaultEngine.Compute(focused, (0, 0), focused.Wavelengths.Single(),
            8, FoucaultKnifeEdge.VerticalLeft, 0, propagationMode: FoucaultPropagationMode.VectorAngularSpectrum));
    }

    [Fact]
    public void ThreeDimensionalStopImagePreservesPhysicalWavefrontAndRetainsCurvedDepth()
    {
        var optic = LensWithCurvedStop();
        var original = new WavefrontAnalysis(optic, pupilSampling: 8, useExitPupilShape: false).GenerateData();
        var projected = new WavefrontAnalysis(optic, pupilSampling: 8,
            exitPupilGeometry: ExitPupilGeometryMode.GeometricStopImage).GenerateData();
        Assert.Equal(original.Series!.Points, projected.Series!.Points);
        Assert.Equal(original.Values["RmsWaves"], projected.Values["RmsWaves"]);
        Assert.Equal(original.Values["ReferenceOpticalPathLength"], projected.Values["ReferenceOpticalPathLength"]);
        var geometry = Assert.IsAssignableFrom<IReadOnlyList<AnalysisSpatialPoint>>(projected.PlotOptions!.SpatialGeometry);
        Assert.Equal(projected.Series.Points.Count, geometry.Count);
        Assert.True(geometry.Max(p => p.PhysicalZ) - geometry.Min(p => p.PhysicalZ) > .005);
        Assert.All(geometry, p => Assert.InRange(p.FitResidualMillimeters, 0, 1e-8));
        Assert.Equal("Not evaluated", projected.Values["ExitPupilWorkingFNumberX"]);
        foreach (var p in geometry)
        {
            var ray = optic.SequentialRayTracer.TraceGenericSurfaceSample(0, 0, p.SourceX, p.SourceY,
                optic.Wavelengths.Single().Micrometers, 2)!;
            Assert.InRange((new Vector3D(p.PhysicalX, p.PhysicalY, p.PhysicalZ) - ray.Position).Length, 0, 2e-6);
        }
    }

    [Fact]
    public void ConjugateFitRecoversKnownThreeDimensionalPointAndReportsSkewInsteadOfInventingExactFocus()
    {
        var target = new Vector3D(3, -2, 7);
        var points = new[] { new Vector3D(0, 0, 0), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(-1, -1, 0) };
        var lines = points.Select(p => (p, target - p)).ToArray();
        var fit = ExitPupilGeometryEngine.FitLines(lines);
        Assert.InRange((fit.Point - target).Length, 0, 2e-13); Assert.InRange(fit.Residual, 0, 2e-13);
        lines[0] = (new(.01, 0, 0), lines[0].Item2);
        Assert.True(ExitPupilGeometryEngine.FitLines(lines).Residual > .001);
        Assert.Throws<AnalysisDataUnavailableException>(() => ExitPupilGeometryEngine.FitLines(new[]
            { (new Vector3D(0, 0, 0), new Vector3D(0, 0, 1)), (new Vector3D(1, 0, 0), new Vector3D(0, 0, 1)) }));
    }

    [Fact]
    public void PlaneParallelPlateStopImageMatchesIndependentConjugateAndTiltedDetectorProjection()
    {
        var optic = CollimatedPlate(); var wave = optic.Wavelengths.Single();
        var pupils = new[] { (.4, .2), (-.3, .5), (.1, -.6) };
        var image = ExitPupilGeometryEngine.Compute(optic, (0, 0), wave, pupils);
        var front = optic.SurfaceGroup.Items[1].CoordinateSystem.Origin;
        // A stop on the front of a 2-mm, n=1.5 parallel plate is imaged at
        // z = 2 - 2/1.5, with unit lateral magnification (paraxial pencil limit).
        Assert.Equal(front.Z + 2 - 2 / 1.5, image.Center.Z, 6);
        for (var i = 0; i < pupils.Length; i++)
        {
            Assert.Equal(front.X + pupils[i].Item1, image.Points[i].PhysicalX, 6);
            Assert.Equal(front.Y + pupils[i].Item2, image.Points[i].PhysicalY, 6);
        }
        var detector = optic.SurfaceGroup.Items[^1];
        detector.CoordinateSystem = new CoordinateSystem(detector.CoordinateSystem.Origin, 20, 0, 0);
        var tilted = ExitPupilGeometryEngine.Compute(optic, (0, 0), wave, pupils);
        Assert.True(tilted.Points.Max(p => p.Z) - tilted.Points.Min(p => p.Z) > .3);
        for (var i = 0; i < pupils.Length; i++)
        {
            var a = image.Points[i]; var b = tilted.Points[i];
            Assert.InRange((new Vector3D(a.PhysicalX, a.PhysicalY, a.PhysicalZ)
                - new Vector3D(b.PhysicalX, b.PhysicalY, b.PhysicalZ)).Length, 0, 2e-6);
        }
    }

    [Fact]
    public void GeometricPupilMovesRigidlyWithoutChangingItsShapeOrPhysicalWavefront()
    {
        var optic = LensWithCurvedStop(); var wave = optic.Wavelengths.Single(); var pupils = new[] { (0d, .2), (.4, -.3), (-.3, .5) };
        var original = ExitPupilGeometryEngine.Compute(optic, (0, 0), wave, pupils);
        var shift = new Vector3D(10, -5, 2);
        foreach (var surface in optic.SurfaceGroup.Items)
            surface.CoordinateSystem = surface.CoordinateSystem with { Origin = surface.CoordinateSystem.Origin + shift };
        var moved = ExitPupilGeometryEngine.Compute(optic, (0, 0), wave, pupils);
        foreach (var pair in original.Points.Zip(moved.Points))
        {
            Assert.Equal(pair.First.X, pair.Second.X, 6); Assert.Equal(pair.First.Y, pair.Second.Y, 6);
            Assert.Equal(pair.First.Z, pair.Second.Z, 6);
            Assert.InRange((new Vector3D(pair.Second.PhysicalX - pair.First.PhysicalX,
                pair.Second.PhysicalY - pair.First.PhysicalY, pair.Second.PhysicalZ - pair.First.PhysicalZ) - shift).Length, 0, 1e-5);
        }
    }

    [Fact]
    public void PostStopCurvedInterfaceHasAnalyticAxialConjugateAndNonlinearLateralPupilDistortion()
    {
        var optic = CollimatedPlate(); optic.ImageSpaceAfocal = false;
        optic.SurfaceGroup.Items[2].Radius = -12;
        var pupils = new[] { (.001, 0d), (.1, 0d), (.8, 0d), (0d, .5) };
        var result = ExitPupilGeometryEngine.Compute(optic, (0, 0), optic.Wavelengths.Single(), pupils);
        var front = optic.SurfaceGroup.Items[1].CoordinateSystem.Origin;
        // n2/s' + n1/s = (n2-n1)/R, with s=2, n1=1.5, n2=1, R=-12.
        var imageDistance = 1 / ((1 - 1.5) / -12 - 1.5 / 2);
        var magnification = -1.5 * imageDistance / 2;
        Assert.Equal(front.Z + 2 + imageDistance, result.Center.Z, 6);
        Assert.InRange(Math.Abs((result.Points[0].PhysicalX - front.X) / .001 - magnification), 0, 1e-6);
        var near = (result.Points[1].PhysicalX - front.X) / .1;
        var edge = (result.Points[2].PhysicalX - front.X) / .8;
        Assert.True(Math.Abs(near - edge) > 1e-4);
        Assert.True(result.MaximumFitResidualMillimeters > 0);
    }

    [Fact]
    public void VectorGridBudgetAndUnrepresentableWaveNumbersAreRejectedBeforeLargeAllocation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AngularSpectrumEngine.PropagateVector(
            new ComplexElectricField[1024, 1024], .1, .1, .001, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AngularSpectrumEngine.PropagateScalar(
            new Complex[8, 8], double.Epsilon, .1, 1, 1, 0));
        Assert.Throws<ArgumentNullException>(() => AngularSpectrumEngine.PropagateScalar(null!, .1, .1, 1, 1, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.3)]
    public void CircularSourceBoundaryWithThreeValidCornersPreservesBothMirrorSymmetries(double halfWidth)
    {
        var optic = CollimatedPlate();
        var open = FoucaultEngine.Compute(optic, (0, 0), optic.Wavelengths.Single(), 16,
            FoucaultKnifeEdge.VerticalRight, 1e6, propagationMode: FoucaultPropagationMode.SpatialAngularSpectrum,
            observationHalfWidthMillimeters: halfWidth);
        for (var row = 0; row < 16; row++)
        for (var column = 0; column < 16; column++)
        {
            Assert.Equal(open.Intensity[row, column], open.Intensity[row, 15 - column], 10);
            Assert.Equal(open.Intensity[row, column], open.Intensity[15 - row, column], 10);
        }
    }

    internal static Optic CollimatedPlate()
    {
        var optic = new Optic("Collimated plane-parallel test plate");
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 2;
        optic.Wavelengths.Clear(); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint()); optic.ImageSpaceAfocal = true;
        var material = new ConstantIndexMaterial("test plate", 1.5);
        optic.Materials.Register(material);
        optic.SurfaceGroup.ImportLegacySurfaces([
            new OpticalSurface { Label = "Object", Thickness = double.PositiveInfinity, Material = "Air", SemiDiameter = 5 },
            new OpticalSurface { Label = "Plate front", Thickness = 2, Material = "N-BK7", SemiDiameter = 2, IsStop = true },
            new OpticalSurface { Label = "Plate back", Thickness = 3, Material = "Air", SemiDiameter = 2 },
            new OpticalSurface { Label = "Image", Material = "Air", SemiDiameter = 5 }
        ]);
        optic.SurfaceGroup.Items[1].MaterialAfter = material;
        optic.SurfaceGroup.Items[2].MaterialBefore = material;
        return optic;
    }
    internal static Optic LensWithCurvedStop()
    {
        var optic = CollimatedPlate(); optic.ImageSpaceAfocal = false;
        optic.SurfaceGroup.Items[1].Radius = 30; optic.SurfaceGroup.Items[1].IsStop = false;
        optic.SurfaceGroup.Items[2].Radius = -30; optic.SurfaceGroup.Items[2].IsStop = true;
        return optic;
    }
    private static Complex[,] ScalarMode(int n, int fx, int fy)
    {
        var result = new Complex[n, n];
        for (var r = 0; r < n; r++)
        for (var c = 0; c < n; c++) result[r, c] = Complex.Exp(Complex.ImaginaryOne * 2 * Math.PI * (fx * c + fy * r) / n);
        return result;
    }
    private static ComplexElectricField[,] VectorMode(Complex[,] mode, Vector3D direction)
    {
        var result = new ComplexElectricField[mode.GetLength(0), mode.GetLength(1)];
        for (var r = 0; r < mode.GetLength(0); r++)
        for (var c = 0; c < mode.GetLength(1); c++) result[r, c] = ComplexElectricField.FromReal(direction) * mode[r, c];
        return result;
    }
    private static Complex[,] DirectScalar(Complex[,] field, double dx, double dy, double lambda, double index, double distance)
    {
        const int n = 8; var spectrum = new Complex[n, n]; var result = new Complex[n, n];
        for (var fy = 0; fy < n; fy++)
        for (var fx = 0; fx < n; fx++)
        {
            for (var r = 0; r < n; r++)
            for (var c = 0; c < n; c++) spectrum[fy, fx] += field[r, c] * Complex.Exp(-Complex.ImaginaryOne * 2 * Math.PI * (fx * c + fy * r) / n);
            var kx = 2 * Math.PI * (fx < n / 2 ? fx : fx - n) / (n * dx);
            var ky = 2 * Math.PI * (fy < n / 2 ? fy : fy - n) / (n * dy);
            var kz = Complex.Sqrt(Math.Pow(2 * Math.PI * index / lambda, 2) - kx * kx - ky * ky);
            spectrum[fy, fx] *= Complex.Exp(Complex.ImaginaryOne * kz * distance);
        }
        for (var r = 0; r < n; r++)
        for (var c = 0; c < n; c++)
        for (var fy = 0; fy < n; fy++)
        for (var fx = 0; fx < n; fx++) result[r, c] += spectrum[fy, fx] * Complex.Exp(Complex.ImaginaryOne * 2 * Math.PI * (fx * c + fy * r) / n) / (n * n);
        return result;
    }
}
