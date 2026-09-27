using System.Text.Json;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Optimization;

namespace OptilandWorkbench.Tests;

public sealed class OptimizationRouteTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(83.123456789)]
    [InlineData(-83.123456789)]
    [InlineData(double.PositiveInfinity)]
    public void CreatingCurvatureVariablePreservesPrescriptionAndIncludesBothSigns(double radius)
    {
        var surface = new OpticalSurface { Radius = radius };
        var geometry = surface.Geometry;
        var variable = SurfaceCurvatureParameter.Create(surface);

        Assert.Equal(radius, surface.Radius);
        Assert.Same(geometry, surface.Geometry);
        Assert.True(variable.LowerBound < 0 && variable.UpperBound > 0);
        SurfaceCurvatureParameter.Write(surface, variable.Value);
        Assert.Equal(radius, surface.Radius);
        Assert.Same(geometry, surface.Geometry);
    }

    [Fact]
    public void CurvatureCanPassThroughPlaneWithoutAnArtificialSmallRadius()
    {
        var surface = new OpticalSurface { Radius = 80 };
        var variable = SurfaceCurvatureParameter.Create(surface);
        variable.Value = 0;
        Assert.Equal(0, surface.Radius);
        Assert.IsType<PlaneGeometry>(surface.Geometry);
        variable.Value = -1.0 / 80;
        Assert.Equal(-80, surface.Radius, 12);
        Assert.IsType<StandardGeometry>(surface.Geometry);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(80, -0.0125)]
    [InlineData(-80, 0.0125)]
    public void MarkedOptimizationPreservesPlaneAndCanCrossCurvatureSign(double radius, double target)
    {
        var runtime = new WorkbenchRuntime(Optic.CreateDemo());
        var surface = runtime.Surfaces[2];
        surface.Radius = radius;
        surface.RadiusVariable = true;
        runtime.CurrentOptic.MeritFunctionOperands.Add(new()
        {
            Type = "CVVA",
            Surface = surface.Number,
            Target = target
        });

        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 50);

        Assert.Equal(Math.Pow((radius == 0 ? 0 : 1 / radius) - target, 2), result.InitialMerit, 12);
        Assert.Equal(target, SurfaceCurvatureParameter.Read(runtime.Surfaces[2]), 8);
        if (radius == 0) Assert.Equal(0, runtime.Surfaces[2].Radius);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void ExistingZeroOrSignedSpacingIsNotClampedAtOptimizationEntry(double thickness)
    {
        var runtime = new WorkbenchRuntime(Optic.CreateDemo());
        runtime.Surfaces[2].Thickness = thickness;
        runtime.Surfaces[2].ThicknessVariable = true;
        runtime.CurrentOptic.MeritFunctionOperands.Add(new()
        {
            Type = "THIC",
            Surface = 2,
            Target = thickness
        });

        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 1);

        Assert.Equal(0, result.InitialMerit);
        Assert.Equal(thickness, runtime.Surfaces[2].Thickness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultObjectiveMatchesCoreSignedRayMeritAndDoesNotPopulateEditor(bool singleSurface)
    {
        var runtime = new WorkbenchRuntime(Optic.CreateCookeTriplet());
        runtime.Surfaces[1].RadiusVariable = true;
        runtime.Surfaces[2].RadiusVariable = !singleSurface;
        var definitions = MeritFunctionCatalog.CreateDefaultRmsSpot(runtime.CurrentOptic);
        var expected = runtime.CurrentOptic.CreateOptimizationProblem();
        foreach (var operand in MeritFunctionCatalog.CreateOperands(runtime.CurrentOptic, definitions))
            expected.AddOperand(operand);
        var initial = expected.SumSquared();

        var result = singleSurface
            ? runtime.OptimizeSurfaceRadius(runtime.Surfaces[1], "Damped Least Squares", 2)
            : runtime.OptimizeMarkedVariables("Damped Least Squares", 2);

        Assert.True(expected.Operands.Count > 2);
        Assert.Equal(initial, result.InitialMerit, 10);
        Assert.True(result.FinalMerit < initial);
        Assert.Equal(expected.SumSquared(), result.FinalMerit, 10);
        Assert.Empty(runtime.CurrentOptic.MeritFunctionOperands);
    }

    [Theory]
    [InlineData("Damped Least Squares")]
    [InlineData("Nelder-Mead")]
    [InlineData("Coordinate Pattern Search")]
    [InlineData("Momentum Gradient Descent")]
    [InlineData("Greedy Random Perturbation")]
    public void InvalidInitialMeritReportsCauseAndRestoresDocument(string optimizer)
    {
        var runtime = new WorkbenchRuntime(Optic.CreateDemo());
        runtime.Surfaces[1].RadiusVariable = true;
        runtime.Surfaces[2].Radius = 0.001;
        runtime.CurrentOptic.MeritFunctionOperands.Add(new()
        {
            Type = "REAY",
            Surface = runtime.Surfaces[^1].Number,
            Field = 1,
            Wavelength = 1,
            Py = 1
        });
        var initial = JsonSerializer.Serialize(runtime.CurrentOptic.ToSnapshot());

        var error = Assert.Throws<OptimizationEvaluationException>(() =>
            runtime.OptimizeMarkedVariables(optimizer, 2));

        Assert.Contains("REAY", error.OperandName);
        Assert.Contains("surface", error.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(initial, JsonSerializer.Serialize(runtime.CurrentOptic.ToSnapshot()));
        Assert.False(runtime.CanUndo);
    }

    [Fact]
    public void UntraceableRayNeverBecomesAFinitePenalty()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].Radius = 0.001;
        var definition = new MeritOperandDefinition
        {
            Type = "REAY",
            Surface = optic.SurfaceGroup.Items[^1].Number,
            Field = 1,
            Wavelength = 1,
            Py = 1
        };
        var inspected = MeritFunctionCatalog.Evaluate(optic, definition);
        Assert.NotEmpty(inspected.Error);
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.CreateOperand(optic, definition).Evaluate());
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(optic, new[] { definition }));
    }

    [Fact]
    public void DlsUsesValidSideOfDerivativeAtOpticalDomainBoundary()
    {
        double x = 0;
        var problem = new OptimizationProblem();
        problem.AddVariable(new DelegateVariable("x", () => x, value => x = value, -2, 2));
        problem.AddOperand(new("residual", 0, 1, () => x <= 0
            ? x + 1 : throw new OptimizationEvaluationException("ray", "outside domain")));

        var result = new DampedLeastSquaresOptimizer().Optimize(problem, 30);

        Assert.Equal(-1, x, 6);
        Assert.True(result.FinalMerit < 1e-12);
    }

    [Fact]
    public void DlsRejectsInvalidTrialAndContinuesFromValidState()
    {
        double x = 0.01;
        var invalid = 0;
        var problem = new OptimizationProblem();
        problem.AddVariable(new DelegateVariable("x", () => x, value => x = value, -1, 1));
        problem.AddOperand(new("residual", 0, 1, () =>
        {
            if (x is >= 0 and <= 0.75) return x * x - 0.25;
            invalid++;
            throw new OptimizationEvaluationException("ray", "outside domain");
        }));

        var result = new DampedLeastSquaresOptimizer().Optimize(problem, 60);

        Assert.True(invalid > 0);
        Assert.NotEmpty(result.Warnings);
        Assert.Equal(0.5, x, 6);
        Assert.True(result.FinalMerit < 1e-12);
    }

    [Fact]
    public void DlsDoesNotInventZeroDerivativeWhenEveryProbeFails()
    {
        double x = 0;
        var problem = new OptimizationProblem();
        problem.AddVariable(new DelegateVariable("x", () => x, value => x = value, -1, 1));
        problem.AddOperand(new("residual", 0, 1, () => x == 0
            ? 1 : throw new OptimizationEvaluationException("ray", "unavailable")));

        var error = Assert.Throws<OptimizationEvaluationException>(() =>
            new DampedLeastSquaresOptimizer().Optimize(problem));

        Assert.Contains("finite-difference", error.Reason);
        Assert.Equal(0, x);
    }

    [Theory]
    [InlineData("Damped Least Squares")]
    [InlineData("Momentum Gradient Descent")]
    public void NonFiniteNumericsCannotProduceSuccessfulGradientTermination(string optimizer)
    {
        double x = 0;
        var problem = new OptimizationProblem();
        problem.AddVariable(new DelegateVariable("x", () => x, value => x = value, -1, 1, 1e-10));
        problem.AddOperand(new("large", 0, 1, () => 1e150 + 1e160 * x));

        Assert.Throws<InvalidOperationException>(() => OptimizerCatalog.Create(optimizer).Optimize(problem));
    }
}
