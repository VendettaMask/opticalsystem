using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Visualization;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class BinocularViewerTests
{
    private static string Source => Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(),
        "src/OpticalSystemDesign.App/Assets/SequentialExamples/Afocal/Binocular_System.ZMX");

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task PhysicalFacesAndInternalReflectionsReachBothLayoutsWithoutChangingTrace(int configuration)
    {
        var original = (await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(Source)).Configurations[configuration];
        foreach (var optic in new[] { original, Optic.FromSnapshot(original.ToSnapshot()) })
        {
            var group = Assert.IsType<PolygonGroupGeometry>(optic.SurfaceGroup.Items[6].Geometry);
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 1, .2, .1, optic.Wavelengths[1].Micrometers, false);
            optic.SequentialRayTracer.ConfigureCache(new RayTraceCache(), 1, "binocular-path-test");
            using var ordinary = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FullHistory());
            using var detailed = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FullHistory() with { RecordCompoundInteractions = true });
            for (var surface = 0; surface < optic.SurfaceGroup.Items.Count; surface++)
            {
                Assert.True(ordinary.TryGetSample(0, surface, out var expected));
                Assert.True(detailed.TryGetSample(0, surface, out var actual));
                Assert.Equal(expected, actual with { CompoundInteractions = null });
            }
            Assert.True(detailed.TryGetSample(0, 6, out var compound));
            Assert.Equal(8, compound.CompoundInteractions!.Count);
            Assert.Equal(4, compound.CompoundInteractions.Count(hit => hit.InteractionKind == Core.Interactions.RayInteractionKind.TotalInternalReflection));
            var history = detailed.GetRaySamples(0).Where(sample => sample.HasValue)
                .Select(sample => sample!.Value.ToRayTraceSample()).ToArray();
            var builder = new Layout2DBuilder(optic);
            var segments = builder.BuildRaySegments(bundle.Rays[0], history).Where(segment => segment.TargetSurfaceNumber == 6).ToArray();
            Assert.Equal(8, segments.Count(segment => segment.TargetFaceIndex.HasValue));
            Assert.Equal(4, segments.Count(segment => segment.SegmentType == LayoutRaySegmentType.TotalInternalReflection));
            double length = 0;
            foreach (var segment in segments)
            {
                var dx = segment.End.X - segment.Start.X; var dy = segment.End.Y - segment.Start.Y; var dz = segment.End.Z - segment.Start.Z;
                var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                length += distance;
                if (distance > 1e-10) Assert.InRange(Math.Abs((dx * segment.Direction.X + dy * segment.Direction.Y + dz * segment.Direction.Z) / distance - 1), 0, 1e-10);
            }
            Assert.InRange(Math.Abs(length - compound.SegmentLength), 0, 1e-9);
            using var cachedAgain = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FullHistory());
            Assert.True(cachedAgain.TryGetSample(0, 6, out var ordinaryAgain));
            Assert.Null(ordinaryAgain.CompoundInteractions);
            Assert.True(ordinary.TryGetSample(0, 6, out var ordinaryFirst));
            Assert.Equal(ordinaryFirst, ordinaryAgain);
            var scene3 = builder.Build3D(options: new(RayCount: 3, FieldIndex: 0, WavelengthIndex: 1));
            var mesh = Assert.Single(scene3.Surfaces, surface => surface.IsPolygonGroup);
            Assert.Equal(group.Faces.Count, mesh.Faces.Count);
            Assert.Empty(mesh.Rim); // No invented circular port or lens wall.
            for (var i = 0; i < group.Faces.Count; i++)
                Assert.Equal(group.Faces[i].Vertices.Select(vertex => optic.SurfaceGroup.Items[6].CoordinateSystem.ToGlobalPoint(vertex))
                    .Select(vertex => new Layout3DPoint(vertex.X, vertex.Y, vertex.Z)), mesh.Faces[i].Points);
            var scene2 = builder.Build(options: new(RayCount: 3));
            Assert.Equal(group.Faces.Count, Assert.Single(scene2.Surfaces, surface => surface.SurfaceNumber == 6).Contours!.Count);
            var limited = builder.Build3D(options: new(FirstSurface: 6, LastSurface: 6, RayCount: 1));
            Assert.NotEmpty(limited.Rays);
            Assert.All(limited.Rays.SelectMany(ray => ray.Segments), segment =>
            {
                Assert.Equal(6, segment.SourceSurfaceNumber); Assert.Equal(6, segment.TargetSurfaceNumber);
            });
        }
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Source)!)!, "index.json")));
        var record = index.RootElement.GetProperty("Files").EnumerateArray().Single(entry => entry.GetProperty("RelativePath").GetString() == "Afocal/Binocular_System.ZMX");
        Assert.Equal(record.GetProperty("Sha256").GetString()!.ToLowerInvariant(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Source))));
    }

    [Fact]
    public async Task BothConfigurationsComposeInTheSharedSceneWithoutChangingTheWorkspace()
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(Source);
        var revision = app.Events.Revision;
        var selection = app.MultiConfiguration.GetNavigationState().ActiveIndex;
        foreach (var dimension in new[] { SceneDimension.TwoDimensional, SceneDimension.ThreeDimensional })
        {
            var scene = await app.Visualization.BuildSceneAsync(new VisualizationRequestDto(dimension, RayCount: 3, IncludeAllConfigurations: true));
            if (dimension == SceneDimension.ThreeDimensional)
            {
                var meshes = scene.ThreeDimensional!.Surfaces.Where(surface => surface.IsPolygonGroup).ToArray();
                Assert.Equal(new[] { 1, 2 }, meshes.Select(mesh => mesh.ConfigurationNumber));
                Assert.False(meshes[0].Faces.SelectMany(face => face.Points).SequenceEqual(meshes[1].Faces.SelectMany(face => face.Points)));
                Assert.Contains(scene.ThreeDimensional.Rays.SelectMany(ray => ray.Segments), segment => segment.SourceFaceIndex.HasValue);
            }
            else Assert.Equal(2, scene.TwoDimensional!.Surfaces.Count(surface => surface.Contours is { Count: > 0 }));
        }
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(selection, app.MultiConfiguration.GetNavigationState().ActiveIndex);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.Visualization.BuildSceneAsync(
            new VisualizationRequestDto(SceneDimension.ThreeDimensional, IncludeAllConfigurations: true), new CancellationToken(true)));
    }

    [Fact]
    public async Task Real2DViewerPaintsDisconnectedFaceContoursWithoutInventedConnectingEdges()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(BinocularViewerTests));
        var rendered = false;
        await session.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(Source);
            var dto = await app.Visualization.BuildSceneAsync(new VisualizationRequestDto(SceneDimension.TwoDimensional, RayCount: 3));
            var scene = dto.TwoDimensional!;
            var mesh = Assert.Single(scene.Surfaces, surface => surface.Contours is not null);
            Assert.All(mesh.Contours!, contour => Assert.Equal(contour[0], contour[^1]));
            var control = new OpticSceneControl { Scene = dto, ViewMode = OpticSceneViewMode.TwoDimensional, ShowRays = false };
            var window = new Window { Content = control, Width = 1100, Height = 650 };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var image = new RenderTargetBitmap(new PixelSize(1100, 650), new Vector(96, 96)); image.Render(window);
                var visible = Pixels(image);
                var output = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BINOCULAR_VIEWER_CAPTURE_DIR");
                if (output is not null) { Directory.CreateDirectory(output); image.Save(Path.Combine(output, "binocular-2d.png"), PngBitmapEncoderOptions.Default); }
                control.Scene = dto with
                {
                    TwoDimensional = scene with
                    { Surfaces = scene.Surfaces.Select(surface => surface with { Contours = null }).ToArray() }
                };
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var empty = new RenderTargetBitmap(new PixelSize(1100, 650), new Vector(96, 96)); empty.Render(window);
                Assert.True(visible.Zip(Pixels(empty)).Count(pair => pair.First != pair.Second) > 100,
                    "The polygon contours must reach the actual 2D renderer.");
            }
            finally { window.Close(); }
            rendered = true;
        }, CancellationToken.None);
        Assert.True(rendered, "The off-screen rendering callback must complete before the test returns.");
    }

    [Theory]
    [InlineData(OpticSceneRenderMode.Solid, false)]
    [InlineData(OpticSceneRenderMode.Transparent, false)]
    [InlineData(OpticSceneRenderMode.Wireframe, false)]
    [InlineData(OpticSceneRenderMode.Solid, true)]
    public async Task RealViewerPaintsPrismWithRaysHidden(OpticSceneRenderMode mode, bool cutaway)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(BinocularViewerTests));
        var rendered = false;
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(Source);
            var dto = await app.Visualization.BuildSceneAsync(new VisualizationRequestDto(SceneDimension.ThreeDimensional, RayCount: 3, IncludeAllConfigurations: true));
            var original = dto.ThreeDimensional!;
            var prismOnly = original with
            {
                Surfaces = original.Surfaces.Where(surface => surface.IsPolygonGroup).ToArray(),
                LensElements = original.LensElements.Where(element => element.FrontSurfaceNumber == 6 && element.BackSurfaceNumber == 6).ToArray(),
                Rays = []
            };
            var control = new OpticSceneControl
            {
                Scene = dto with { ThreeDimensional = prismOnly },
                ViewMode = OpticSceneViewMode.ThreeDimensional,
                VisualStyle = OpticSceneVisualStyle.SolidModel,
                RenderMode = mode,
                CutawayEnabled = cutaway,
                ShowRays = false
            };
            var window = new Window { Content = control, Width = 1100, Height = 650 };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var output = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BINOCULAR_VIEWER_CAPTURE_DIR");
                using var image = new RenderTargetBitmap(new PixelSize(1100, 650), new Vector(96, 96));
                image.Render(window);
                if (output is not null) { Directory.CreateDirectory(output); image.Save(Path.Combine(output, $"prism-{mode}-{cutaway}.png"), PngBitmapEncoderOptions.Default); }
                Assert.Contains(control.GlassMaterials, material => material.Material == "N-BK7");
                Assert.True(control.Bounds.Width > 1000);
                var visible = Pixels(image);
                control.Scene = dto with
                {
                    ThreeDimensional = prismOnly with
                    { Surfaces = prismOnly.Surfaces.Select(surface => surface with { Faces = [] }).ToArray(), LensElements = [] }
                };
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var empty = new RenderTargetBitmap(new PixelSize(1100, 650), new Vector(96, 96));
                empty.Render(window);
                Assert.True(visible.Zip(Pixels(empty)).Count(pair => pair.First != pair.Second) > 1000,
                    "Physical prism faces/edges must produce visible pixels even with rays hidden.");
                // Render the full delivered scene as the review artifact.
                control.Scene = dto; control.ShowRays = true; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                if (output is not null)
                {
                    using var full = new RenderTargetBitmap(new PixelSize(1100, 650), new Vector(96, 96));
                    full.Render(window); full.Save(Path.Combine(output, $"binocular-{mode}-{cutaway}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
            rendered = true;
        }, CancellationToken.None);
        Assert.True(rendered, "The off-screen rendering callback must complete before the test returns.");
    }

    private static byte[] Pixels(RenderTargetBitmap bitmap)
    {
        var bytes = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pointer, bytes.Length, bitmap.PixelSize.Width * 4);
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
}
