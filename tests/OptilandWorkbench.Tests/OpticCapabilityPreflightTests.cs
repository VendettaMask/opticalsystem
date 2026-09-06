using System.Reflection;
using System.Text.Json;
using OptilandWorkbench.App.Manufacturing;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Tolerancing;
using OptilandWorkbench.Core.Visualization;

namespace OptilandWorkbench.Tests;

public sealed class OpticCapabilityPreflightTests
{
    [Fact]
    public void OpaqueGeometryRoundTripsPayloadWithoutBecomingAPlane()
    {
        var payload = CreatePayload();
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].Geometry = new OpaqueGeometryPayload(payload);

        var snapshot = optic.ToSnapshot();
        OpticSnapshotValidator.Validate(snapshot);
        var restored = Optic.FromSnapshot(snapshot);
        var geometry = Assert.IsType<OpaqueGeometryPayload>(restored.SurfaceGroup.Items[1].Geometry);

        Assert.Equal("VendorXYFreeform", geometry.OriginalType);
        Assert.Equal(
            JsonSerializer.Serialize(payload),
            JsonSerializer.Serialize(geometry.Payload));
        var sag = Assert.Throws<InvalidOperationException>(() => geometry.Sag(0, 0));
        var intersection = Assert.Throws<InvalidOperationException>(() =>
            geometry.DistanceToIntersection(new(0, 0, -1), new(0, 0, 1)));
        var normal = Assert.Throws<InvalidOperationException>(() => geometry.SurfaceNormal(new(0, 0, 0)));
        Assert.All(
            new[] { sag.Message, intersection.Message, normal.Message },
            message => Assert.Contains("VendorXYFreeform", message, StringComparison.Ordinal));
    }

    [Fact]
    public void UnsupportedGeometryIsBlockedByEveryComputationalPreflight()
    {
        var cases = new (OpticCapabilityOperation Operation, Action Run)[]
        {
            (OpticCapabilityOperation.RayTrace, () => CreateOpaqueOptic().SequentialRayTracer.RayGenerator.Generate()),
            (OpticCapabilityOperation.Analysis, () => CreateOpaqueOptic().Analyses.Create("First Order")),
            (OpticCapabilityOperation.Optimization, () => CreateOpaqueOptic().CreateOptimizationProblem()),
            (OpticCapabilityOperation.Tolerancing, () => new SensitivityAnalysis(CreateOpaqueOptic(), new Tolerancing())),
            (OpticCapabilityOperation.Export, () => StepCadExporter.Serialize(CreateOpaqueOptic())),
            (OpticCapabilityOperation.Export, () => OpticalFormatCatalog.Export(CreateOpaqueOptic(), ".zmx")),
            (OpticCapabilityOperation.Visualization, () => new Layout2DBuilder(CreateOpaqueOptic()).Build())
        };

        foreach (var item in cases)
        {
            var exception = Assert.Throws<OpticCapabilityException>(item.Run);
            Assert.Equal(item.Operation, exception.Operation);
            var issue = Assert.Single(exception.Issues);
            Assert.Equal(1, issue.SurfaceNumber);
            Assert.Equal("VendorXYFreeform", issue.OriginalType);
            Assert.Contains("opaque payload", issue.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("表面 1", exception.Message, StringComparison.Ordinal);
            Assert.Contains("VendorXYFreeform", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryPublicParaxialEntryPointBlocksOpaqueGeometry()
    {
        var paraxial = CreateOpaqueOptic().Paraxial;
        var methods = paraxial.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ThenBy(method => method.GetParameters().Length)
            .ToArray();

        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            var arguments = method.GetParameters()
                .Select(parameter => parameter.ParameterType switch
                {
                    var type when type == typeof(double) =>
                        parameter.Name?.Contains("wavelength", StringComparison.OrdinalIgnoreCase) == true
                            ? (object)0.5876
                            : 0.0,
                    var type when type == typeof(int) => 0,
                    var type when type == typeof(IReadOnlyList<double>) => new[] { 0.0 },
                    _ => throw new InvalidOperationException(
                        $"Test argument mapping is missing for {method.Name}.{parameter.Name}.")
                })
                .ToArray();

            var invocation = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(paraxial, arguments));
            var exception = Assert.IsType<OpticCapabilityException>(invocation.InnerException);
            Assert.Equal(OpticCapabilityOperation.Analysis, exception.Operation);
            Assert.Equal("Paraxial / First Order", exception.Context);
            Assert.Contains("表面 1", exception.Message, StringComparison.Ordinal);
            Assert.Contains("VendorXYFreeform", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UnknownSnapshotGeometryRequiresAndPreservesOpaquePayload()
    {
        var valid = Optic.CreateCookeTriplet().ToSnapshot();
        var surface = valid.Surfaces[1];
        var components = surface.Components!;
        var missingPayload = valid with
        {
            Surfaces = valid.Surfaces
                .Select((item, index) => index == 1
                    ? item with
                    {
                        Components = components with
                        {
                            GeometryKind = "VendorXYFreeform",
                            Geometry = null
                        }
                    }
                    : item)
                .ToList()
        };

        var missing = Assert.Throws<InvalidDataException>(() =>
            OpticSnapshotValidator.Validate(missingPayload));
        Assert.Contains("opaque component payload", missing.Message, StringComparison.OrdinalIgnoreCase);

        var opaquePayload = CreatePayload();
        var validOpaque = missingPayload with
        {
            Surfaces = missingPayload.Surfaces
                .Select((item, index) => index == 1
                    ? item with
                    {
                        Components = item.Components! with { Geometry = opaquePayload }
                    }
                    : item)
                .ToList()
        };
        OpticSnapshotValidator.Validate(validOpaque);
        Assert.IsType<OpaqueGeometryPayload>(Optic.FromSnapshot(validOpaque).SurfaceGroup.Items[1].Geometry);
    }

    [Fact]
    public async Task NativeFilesPreserveOpaqueGeometryPayload()
    {
        var optic = CreateOpaqueOptic();
        var directory = Path.Combine(Path.GetTempPath(), $"opaque-geometry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var jsonPath = Path.Combine(directory, "opaque.json");
        var projectPath = Path.Combine(directory, "opaque.staropt");
        try
        {
            await OpticJsonStore.SaveAsync(optic, jsonPath);
            await StarOptProjectStore.SaveAsync(
                new StarOptProjectDocument(new[] { optic }, 0),
                projectPath);

            var jsonGeometry = Assert.IsType<OpaqueGeometryPayload>(
                (await OpticJsonStore.LoadAsync(jsonPath)).SurfaceGroup.Items[1].Geometry);
            var projectGeometry = Assert.IsType<OpaqueGeometryPayload>(
                (await StarOptProjectStore.LoadAsync(projectPath))
                .Configurations[0]
                .SurfaceGroup.Items[1]
                .Geometry);
            Assert.Equal(
                JsonSerializer.Serialize(jsonGeometry.Payload),
                JsonSerializer.Serialize(projectGeometry.Payload));

            using var application = WorkbenchApplication.Create("blank");
            await application.Documents.OpenAsync(projectPath);
            var summary = application.Documents.GetSnapshot();
            Assert.True(double.IsNaN(summary.EffectiveFocalLength));
            Assert.True(double.IsNaN(summary.FNumber));
            Assert.True(double.IsNaN(summary.EntrancePupilDiameter));
            Assert.False(application.Prescription.GetSurfaces()[1].GeometryComputable);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ManufacturingRowsExposeAndBlockOpaqueGeometry()
    {
        var surface = CreateOpaqueOptic().SurfaceGroup.Items[1];
        var row = WorkbenchMapper.ToSurfaceDto(surface);

        Assert.False(row.GeometryComputable);
        Assert.Contains("VendorXYFreeform", row.GeometryKind, StringComparison.Ordinal);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            OpticalManufacturingModel.BuildElements(new[] { row }));
        Assert.Contains("制造数据/图纸", exception.Message, StringComparison.Ordinal);
        Assert.Contains("VendorXYFreeform", exception.Message, StringComparison.Ordinal);
    }

    private static Optic CreateOpaqueOptic()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].Geometry = new OpaqueGeometryPayload(CreatePayload());
        return optic;
    }

    private static ComponentSnapshot CreatePayload() => new(
        "VendorXYFreeform",
        new Dictionary<string, double>
        {
            ["normalizationRadius"] = 12.5,
            ["coefficient0"] = 0.125
        },
        new Dictionary<string, string>
        {
            ["vendor"] = "示例厂商"
        },
        new Dictionary<string, ComponentSnapshot>
        {
            ["basis"] = new ComponentSnapshot(
                "VendorBasis",
                new Dictionary<string, double> { ["order"] = 7 },
                new Dictionary<string, string> { ["name"] = "XY polynomial" })
        });
}
