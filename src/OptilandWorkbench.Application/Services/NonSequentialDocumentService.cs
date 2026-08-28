using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Coordinates;
using CoreBoxParameters = OptilandWorkbench.Core.NonSequential.BoxParameters;
using CoreCylinderParameters = OptilandWorkbench.Core.NonSequential.CylinderParameters;
using CoreDetectorRectangleParameters = OptilandWorkbench.Core.NonSequential.DetectorRectangleParameters;
using CoreDocument = OptilandWorkbench.Core.NonSequential.NonSequentialDocument;
using CoreObjectDefinition = OptilandWorkbench.Core.NonSequential.NonSequentialObjectDefinition;
using CoreObjectKind = OptilandWorkbench.Core.NonSequential.NonSequentialObjectKind;
using CoreObjectParameters = OptilandWorkbench.Core.NonSequential.NonSequentialObjectParameters;
using CorePlaneRectangleParameters = OptilandWorkbench.Core.NonSequential.PlaneRectangleParameters;
using CoreSourceGaussianParameters = OptilandWorkbench.Core.NonSequential.SourceGaussianParameters;
using CoreSourceEllipseParameters = OptilandWorkbench.Core.NonSequential.SourceEllipseParameters;
using CoreSourceParameters = OptilandWorkbench.Core.NonSequential.SourceParameters;
using CoreSourcePointParameters = OptilandWorkbench.Core.NonSequential.SourcePointParameters;
using CoreSourceRayParameters = OptilandWorkbench.Core.NonSequential.SourceRayParameters;
using CoreSourceRadialParameters = OptilandWorkbench.Core.NonSequential.SourceRadialParameters;
using CoreSourceRadialSample = OptilandWorkbench.Core.NonSequential.SourceRadialSample;
using CoreSourceRectangleParameters = OptilandWorkbench.Core.NonSequential.SourceRectangleParameters;
using CoreSourceTwoAngleParameters = OptilandWorkbench.Core.NonSequential.SourceTwoAngleParameters;
using CoreSourceVolumeCylinderParameters = OptilandWorkbench.Core.NonSequential.SourceVolumeCylinderParameters;
using CoreSourceVolumeEllipseParameters = OptilandWorkbench.Core.NonSequential.SourceVolumeEllipseParameters;
using CoreSourceVolumeRectangleParameters = OptilandWorkbench.Core.NonSequential.SourceVolumeRectangleParameters;
using CoreSurfaceSourceAngularDistribution = OptilandWorkbench.Core.NonSequential.NonSequentialSurfaceSourceAngularDistribution;
using CoreVolumeSourceAngularDistribution = OptilandWorkbench.Core.NonSequential.NonSequentialVolumeSourceAngularDistribution;
using CoreSphereParameters = OptilandWorkbench.Core.NonSequential.SphereParameters;
using CoreStandardLensParameters = OptilandWorkbench.Core.NonSequential.StandardLensParameters;
using CoreMeshObjectParameters = OptilandWorkbench.Core.NonSequential.MeshObjectParameters;
using CoreMeshUnit = OptilandWorkbench.Core.NonSequential.NonSequentialMeshUnit;
using CoreStlImporter = OptilandWorkbench.Core.NonSequential.NonSequentialStlImporter;
using CoreSurfaceBehavior = OptilandWorkbench.Core.NonSequential.NonSequentialSurfaceBehavior;
using CoreTraceSettings = OptilandWorkbench.Core.NonSequential.NonSequentialTraceSettings;
using CoreWavelength = OptilandWorkbench.Core.NonSequential.NonSequentialWavelength;

namespace OptilandWorkbench.Application.Services;

internal sealed class NonSequentialDocumentService : WorkbenchServiceBase, INonSequentialDocumentService
{
    private readonly INonSequentialAnalysisService _analysisService;

    public NonSequentialDocumentService(
        WorkspaceCoordinator workspace,
        INonSequentialAnalysisService analysisService) : base(workspace)
    {
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
    }

    public NonSequentialDocumentDto GetDocument()
    {
        lock (Gate)
        {
            var document = Runtime.CurrentNonSequentialDocument;
            return new NonSequentialDocumentDto(
                document.Name,
                document.AmbientMaterial,
                document.Wavelengths.Select((item, index) => new NonSequentialWavelengthDto(
                    index,
                    item.Label,
                    item.Nanometers,
                    item.Weight,
                    item.IsPrimary)).ToArray(),
                document.Objects.Select((item, index) => ToRow(document, item, index)).ToArray(),
                ToDto(document.TraceSettings));
        }
    }

    public IReadOnlyList<NonSequentialObjectKind> GetObjectKinds() =>
        Enum.GetValues<NonSequentialObjectKind>().Where(item => item != NonSequentialObjectKind.Mesh).ToArray();

    public NonSequentialObjectParameters GetDefaultParameters(NonSequentialObjectKind kind) =>
        ToDto(CoreObjectDefinition.DefaultParameters(ToCore(kind)));

    public IReadOnlyList<string> GetMaterialNames()
    {
        lock (Gate)
        {
            return Runtime.CurrentOptic.Materials.Names
                .Append("Air")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public Guid AddObject(NonSequentialObjectKind kind, int? insertionIndex = null) =>
        MutateTransactional(WorkspaceChangeCategory.NonSequential, () =>
        {
            var document = Runtime.CurrentNonSequentialDocument.Clone();
            var item = CoreObjectDefinition.Create(ToCore(kind));
            document.Insert(insertionIndex ?? document.Objects.Count, item);
            ValidateMaterials(document);
            Runtime.ReplaceNonSequentialDocument(document, $"已添加非序列对象“{item.Name}”。");
            return item.Id;
        });

    public Guid DuplicateObject(Guid id) => MutateTransactional(
        WorkspaceChangeCategory.NonSequential,
        () =>
        {
            var document = Runtime.CurrentNonSequentialDocument.Clone();
            var sourceIndex = document.Objects.ToList().FindIndex(item => item.Id == id);
            if (sourceIndex < 0)
            {
                throw new KeyNotFoundException($"Non-sequential object '{id}' was not found.");
            }

            var source = document.Objects[sourceIndex];
            var copy = source with { Id = Guid.NewGuid(), Name = $"{source.Name} 副本" };
            document.Insert(sourceIndex + 1, copy);
            Runtime.ReplaceNonSequentialDocument(document, $"已复制非序列对象“{source.Name}”。");
            return copy.Id;
        });

    public Guid PasteObject(NonSequentialObjectUpdateDto template, int insertionIndex) =>
        MutateTransactional(WorkspaceChangeCategory.NonSequential, () =>
        {
            var document = Runtime.CurrentNonSequentialDocument.Clone();
            var pasted = new CoreObjectDefinition(
                Guid.NewGuid(),
                $"{template.Name} 副本",
                ToCore(template.Kind),
                template.Enabled,
                template.Visible,
                new CoordinateSystem(
                    new(template.X, template.Y, template.Z),
                    template.TiltXDegrees,
                    template.TiltYDegrees,
                    template.TiltZDegrees),
                template.ReferenceObjectId,
                template.ContainingObjectId,
                ToCore(template.Parameters));
            document.Insert(insertionIndex, pasted);
            ValidateMaterials(document);
            Runtime.ReplaceNonSequentialDocument(document, $"已粘贴非序列对象“{pasted.Name}”。");
            return pasted.Id;
        });

    public void DeleteObject(Guid id) => MutateTransactional(
        WorkspaceChangeCategory.NonSequential,
        () =>
        {
            var document = Runtime.CurrentNonSequentialDocument.Clone();
            var name = document.Objects.FirstOrDefault(item => item.Id == id)?.Name
                ?? throw new KeyNotFoundException($"Non-sequential object '{id}' was not found.");
            document.Remove(id);
            Runtime.ReplaceNonSequentialDocument(document, $"已删除非序列对象“{name}”。");
        });

    public void MoveObject(Guid id, int destinationIndex) => MutateTransactional(
        WorkspaceChangeCategory.NonSequential,
        () =>
        {
            var document = Runtime.CurrentNonSequentialDocument.Clone();
            document.Move(id, destinationIndex);
            Runtime.ReplaceNonSequentialDocument(document, "已调整非序列对象顺序。");
        });

    public void UpdateObject(NonSequentialObjectUpdateDto update) => MutateTransactional(
        WorkspaceChangeCategory.NonSequential,
        () =>
        {
            var document = Runtime.CurrentNonSequentialDocument.Clone();
            var existing = document.Objects.FirstOrDefault(item => item.Id == update.Id)
                ?? throw new KeyNotFoundException($"Non-sequential object '{update.Id}' was not found.");
            var coreKind = ToCore(update.Kind);
            var parameters = existing.Kind == coreKind
                ? ToCore(update.Parameters)
                : CoreObjectDefinition.DefaultParameters(coreKind);
            var replacement = new CoreObjectDefinition(
                existing.Id,
                update.Name,
                coreKind,
                update.Enabled,
                update.Visible,
                new CoordinateSystem(
                    new(update.X, update.Y, update.Z),
                    update.TiltXDegrees,
                    update.TiltYDegrees,
                    update.TiltZDegrees),
                update.ReferenceObjectId,
                update.ContainingObjectId,
                parameters);
            document.Replace(existing.Id, replacement);
            ValidateMaterials(document);
            Runtime.ReplaceNonSequentialDocument(document, $"已更新非序列对象“{replacement.Name}”。");
        });

    public void UpdateWavelengths(IReadOnlyList<NonSequentialWavelengthDto> wavelengths) =>
        MutateTransactional(WorkspaceChangeCategory.NonSequential, () =>
        {
            var document = Runtime.CurrentNonSequentialDocument.Clone();
            document.ReplaceWavelengths(wavelengths.Select(item => new CoreWavelength(
                item.Label,
                item.Nanometers,
                item.Weight,
                item.IsPrimary)));
            Runtime.ReplaceNonSequentialDocument(document, "已更新非序列波长表。");
        });

    public NonSequentialConversionResultDto ConvertFromSequential() => MutateTransactional(
        WorkspaceChangeCategory.NonSequential,
        () =>
        {
            var optic = Runtime.CurrentOptic;
            OpticCapabilityPreflight.EnsureSupported(
                optic,
                OpticCapabilityOperation.Conversion,
                "顺序模式转非序列模式");
            var warnings = new List<string>();
            var converted = new List<CoreObjectDefinition>();
            var surfaces = optic.SurfaceGroup.Items;
            var consumedAsLensBoundary = new HashSet<int>();
            for (var index = 1; index < surfaces.Count - 1; index++)
            {
                var front = surfaces[index];
                var material = front.MaterialAfter.Name;
                if (IsAmbient(material) || material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var back = surfaces[index + 1];
                if (!double.IsFinite(front.Thickness) || front.Thickness <= 0
                    || !Aligned(front.CoordinateSystem, back.CoordinateSystem))
                {
                    warnings.Add($"表面 {front.Number}–{back.Number} 无法表示为同轴正厚度标准镜片，已跳过。");
                    continue;
                }

                converted.Add(new CoreObjectDefinition(
                    Guid.NewGuid(),
                    $"镜片 S{front.Number}–S{back.Number} {material}",
                    CoreObjectKind.StandardLens,
                    true,
                    true,
                    front.CoordinateSystem,
                    null,
                    null,
                    new CoreStandardLensParameters(
                        front.Radius,
                        back.Radius,
                        front.Conic,
                        back.Conic,
                        front.Thickness,
                        Math.Max(front.SemiDiameter, back.SemiDiameter),
                        material)));
                consumedAsLensBoundary.Add(index);
                consumedAsLensBoundary.Add(index + 1);
            }

            for (var index = 1; index < surfaces.Count - 1; index++)
            {
                var surface = surfaces[index];
                if (consumedAsLensBoundary.Contains(index) || surface.IsStop)
                {
                    continue;
                }

                if (!surface.IsPlane)
                {
                    warnings.Add($"曲面 {surface.Number} 不是可表示的独立平面对象，已跳过。");
                    continue;
                }

                converted.Add(new CoreObjectDefinition(
                    Guid.NewGuid(),
                    string.IsNullOrWhiteSpace(surface.Label) ? $"表面 {surface.Number}" : surface.Label,
                    CoreObjectKind.PlaneRectangle,
                    true,
                    true,
                    surface.CoordinateSystem,
                    null,
                    null,
                    new CorePlaneRectangleParameters(
                        surface.SemiDiameter * 2,
                        surface.SemiDiameter * 2,
                        surface.IsReflective
                            ? CoreSurfaceBehavior.Reflective
                            : CoreSurfaceBehavior.Refractive,
                        surface.MaterialBefore.Name,
                        surface.MaterialAfter.Name)));
            }

            var image = surfaces[^1];
            converted.Add(new CoreObjectDefinition(
                Guid.NewGuid(),
                "像面探测器",
                CoreObjectKind.DetectorRectangle,
                true,
                true,
                image.CoordinateSystem,
                null,
                null,
                new CoreDetectorRectangleParameters(
                    image.SemiDiameter * 2,
                    image.SemiDiameter * 2)));
            warnings.Add("转换不会推断光源；请添加对象型光源后再追迹。");

            var wavelengths = optic.Wavelengths.Select(item => new CoreWavelength(
                item.Label,
                item.Nanometers,
                item.Weight,
                item.IsPrimary));
            var document = new CoreDocument(
                $"{optic.Name} 非序列场景",
                wavelengths.ToArray(),
                converted,
                "Air",
                Runtime.CurrentNonSequentialDocument.TraceSettings);
            ValidateMaterials(document);
            Runtime.ReplaceNonSequentialDocument(document, $"已从顺序配置转换 {converted.Count} 个非序列对象。");
            return new NonSequentialConversionResultDto(converted.Count, warnings);
        });

    public async Task<NonSequentialMeshImportResultDto> ImportStlAsync(
        string path,
        NonSequentialMeshImportOptionsDto options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var imported = await Task.Run(
            () => CoreStlImporter.Import(bytes, Path.GetFileName(path), (CoreMeshUnit)(int)options.Unit),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return MutateTransactional(WorkspaceChangeCategory.NonSequential, () =>
        {
            var document = Runtime.CurrentNonSequentialDocument.Clone();
            var assetId = document.AddMeshAsset(imported);
            var asset = document.FindMeshAsset(assetId);
            var item = new CoreObjectDefinition(
                Guid.NewGuid(),
                Path.GetFileNameWithoutExtension(path),
                CoreObjectKind.Mesh,
                true,
                true,
                CoordinateSystem.Global,
                null,
                null,
                new CoreMeshObjectParameters(
                    assetId,
                    ToCore(options.Behavior),
                    options.Material,
                    options.TwoSided));
            document.Insert(options.InsertionIndex ?? document.Objects.Count, item);
            ValidateMaterials(document);
            Runtime.ReplaceNonSequentialDocument(document, $"已导入 STL 网格“{item.Name}”，{asset.TriangleCount} 个三角形。");
            return new NonSequentialMeshImportResultDto(
                item.Id,
                asset.Id,
                item.Name,
                asset.VertexCount,
                asset.TriangleCount,
                asset.IsClosed,
                asset.IsManifold,
                asset.SignedVolumeCubicMillimeters,
                asset.Warnings ?? Array.Empty<string>());
        });
    }

    public async Task<NonSequentialTraceRunResultDto> TraceAsync(
        NonSequentialTraceRunRequestDto request,
        CancellationToken cancellationToken = default)
        => await _analysisService.TraceAsync(request, cancellationToken).ConfigureAwait(false);

    public NonSequentialRayDatabaseDto OpenRayDatabase(string path, string? pathFilterExpression = null)
        => _analysisService.OpenRayDatabase(path, pathFilterExpression);

    private void ValidateMaterials(CoreDocument document)
    {
        _ = Runtime.CurrentOptic.Materials.Resolve(document.AmbientMaterial);
        foreach (var material in document.Objects.SelectMany(MaterialNames).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _ = Runtime.CurrentOptic.Materials.Resolve(material);
        }
    }

    private static IEnumerable<string> MaterialNames(CoreObjectDefinition item) => item.Parameters switch
    {
        CorePlaneRectangleParameters plane => new[] { plane.MaterialBefore, plane.MaterialAfter },
        CoreSphereParameters sphere => new[] { sphere.Material },
        CoreCylinderParameters cylinder => new[] { cylinder.Material },
        CoreBoxParameters box => new[] { box.Material },
        CoreStandardLensParameters lens => new[] { lens.Material },
        CoreMeshObjectParameters mesh => new[] { mesh.Material },
        _ => Array.Empty<string>()
    };

    private static NonSequentialObjectRowDto ToRow(CoreDocument document, CoreObjectDefinition item, int index) => new(
        item.Id,
        index + 1,
        item.Enabled,
        item.Visible,
        ToDto(item.Kind),
        item.Name,
        Role(item.Kind),
        item.ReferenceObjectId,
        item.ContainingObjectId,
        item.LocalCoordinateSystem.Origin.X,
        item.LocalCoordinateSystem.Origin.Y,
        item.LocalCoordinateSystem.Origin.Z,
        item.LocalCoordinateSystem.RotationXDegrees,
        item.LocalCoordinateSystem.RotationYDegrees,
        item.LocalCoordinateSystem.RotationZDegrees,
        MaterialNames(item).FirstOrDefault() ?? string.Empty,
        ToDto(item.Parameters, document),
        Summary(item.Parameters));

    private static string Role(CoreObjectKind kind) => kind switch
    {
        CoreObjectKind.SourceRay or CoreObjectKind.SourcePoint
            or CoreObjectKind.SourceRectangle or CoreObjectKind.SourceGaussian
            or CoreObjectKind.SourceEllipse or CoreObjectKind.SourceTwoAngle or CoreObjectKind.SourceRadial
            or CoreObjectKind.SourceVolumeRectangle or CoreObjectKind.SourceVolumeEllipse
            or CoreObjectKind.SourceVolumeCylinder => "光源",
        CoreObjectKind.DetectorRectangle => "探测器",
        _ => "几何对象"
    };

    private static string Summary(CoreObjectParameters parameters) => parameters switch
    {
        CoreSourceParameters source => $"{source.PowerWatts:0.###} W，λ{source.WavelengthNumber}，{source.AnalysisRayCount} 条",
        CorePlaneRectangleParameters plane => $"{plane.WidthMillimeters:0.###} × {plane.HeightMillimeters:0.###} mm，{plane.Behavior}",
        CoreSphereParameters sphere => $"R {sphere.RadiusMillimeters:0.###} mm",
        CoreCylinderParameters cylinder => $"R {cylinder.RadiusMillimeters:0.###} × {cylinder.LengthMillimeters:0.###} mm",
        CoreBoxParameters box => $"{box.WidthMillimeters:0.###} × {box.HeightMillimeters:0.###} × {box.LengthMillimeters:0.###} mm",
        CoreStandardLensParameters lens => $"CT {lens.CenterThicknessMillimeters:0.###}，SD {lens.SemiDiameterMillimeters:0.###} mm",
        CoreMeshObjectParameters mesh => $"STL 资产 {mesh.MeshAssetId:N}，{mesh.Behavior}",
        CoreDetectorRectangleParameters detector => $"{detector.WidthMillimeters:0.###} × {detector.HeightMillimeters:0.###} mm，{detector.PixelsX} × {detector.PixelsY}",
        _ => parameters.GetType().Name
    };

    private static NonSequentialTraceSettings ToDto(CoreTraceSettings value) => new(
        value.LayoutRaysPerSource,
        value.AnalysisRaysPerSource,
        value.MaximumTotalSourceRays,
        value.MaximumSegmentsPerRay,
        value.MaximumActiveBranches,
        value.MinimumRelativeIntensity,
        value.RandomSeed,
        value.SplitFresnelRays);

    private static NonSequentialObjectKind ToDto(CoreObjectKind value) =>
        (NonSequentialObjectKind)(int)value;

    private static CoreObjectKind ToCore(NonSequentialObjectKind value) =>
        (CoreObjectKind)(int)value;

    private static NonSequentialSurfaceBehavior ToDto(CoreSurfaceBehavior value) =>
        (NonSequentialSurfaceBehavior)(int)value;

    private static CoreSurfaceBehavior ToCore(NonSequentialSurfaceBehavior value) =>
        (CoreSurfaceBehavior)(int)value;

    private static NonSequentialVector3 ToDto(OptilandWorkbench.Core.Backend.Vector3D value) =>
        new(value.X, value.Y, value.Z);

    private static OptilandWorkbench.Core.Backend.Vector3D ToCore(NonSequentialVector3 value) =>
        new(value.X, value.Y, value.Z);

    private static NonSequentialObjectParameters ToDto(CoreObjectParameters value, CoreDocument? document = null) => value switch
    {
        CoreSourceRayParameters source => new SourceRayParameters(
            source.PowerWatts, source.WavelengthNumber, ToDto(source.Origin), ToDto(source.Direction)),
        CoreSourcePointParameters source => new SourcePointParameters(
            source.PowerWatts, source.WavelengthNumber, source.ConeHalfAngleDegrees,
            source.LayoutRayCount, source.AnalysisRayCount),
        CoreSourceRectangleParameters source => new SourceRectangleParameters(
            source.WidthMillimeters, source.HeightMillimeters, source.AngularHalfAngleDegrees,
            source.PowerWatts, source.WavelengthNumber, source.LayoutRayCount, source.AnalysisRayCount,
            (NonSequentialSurfaceSourceAngularDistribution)(int)source.AngularDistribution,
            source.SourceDistanceMillimeters, source.CosineExponent, source.GaussianX, source.GaussianY,
            source.SourceX, source.SourceY, source.MinimumXHalfWidthMillimeters, source.MinimumYHalfWidthMillimeters),
        CoreSourceGaussianParameters source => new SourceGaussianParameters(
            source.WaistXMillimeters, source.WaistYMillimeters, source.DivergenceHalfAngleDegrees,
            source.PowerWatts, source.WavelengthNumber, source.LayoutRayCount, source.AnalysisRayCount),
        CoreSourceEllipseParameters source => new SourceEllipseParameters(
            source.WidthMillimeters, source.HeightMillimeters, source.AngularHalfAngleDegrees,
            source.PowerWatts, source.WavelengthNumber, source.LayoutRayCount, source.AnalysisRayCount,
            (NonSequentialSurfaceSourceAngularDistribution)(int)source.AngularDistribution,
            source.SourceDistanceMillimeters, source.CosineExponent, source.GaussianX, source.GaussianY,
            source.SourceX, source.SourceY, source.MinimumXHalfWidthMillimeters, source.MinimumYHalfWidthMillimeters),
        CoreSourceTwoAngleParameters source => new SourceTwoAngleParameters(
            source.WidthMillimeters, source.HeightMillimeters,
            (NonSequentialSourceApertureShape)(int)source.Shape,
            source.AngularHalfAngleXDegrees, source.AngularHalfAngleYDegrees,
            source.PowerWatts, source.WavelengthNumber, source.LayoutRayCount, source.AnalysisRayCount),
        CoreSourceRadialParameters source => new SourceRadialParameters(
            source.Distribution.Select(sample => new SourceRadialSample(
                sample.AngleDegrees, sample.RelativeIntensity)).ToArray(),
            source.PowerWatts, source.WavelengthNumber, source.LayoutRayCount, source.AnalysisRayCount),
        CoreSourceVolumeRectangleParameters source => new SourceVolumeRectangleParameters(
            source.WidthMillimeters, source.HeightMillimeters, source.DepthMillimeters,
            source.AngularHalfAngleDegrees, source.PowerWatts, source.WavelengthNumber,
            source.LayoutRayCount, source.AnalysisRayCount,
            (NonSequentialVolumeSourceAngularDistribution)(int)source.AngularDistribution),
        CoreSourceVolumeEllipseParameters source => new SourceVolumeEllipseParameters(
            source.SemiAxisXMillimeters, source.SemiAxisYMillimeters, source.SemiAxisZMillimeters,
            source.AngularHalfAngleDegrees, source.PowerWatts, source.WavelengthNumber,
            source.LayoutRayCount, source.AnalysisRayCount,
            (NonSequentialVolumeSourceAngularDistribution)(int)source.AngularDistribution),
        CoreSourceVolumeCylinderParameters source => new SourceVolumeCylinderParameters(
            source.RadiusXMillimeters, source.RadiusYMillimeters, source.LengthMillimeters,
            source.AngularHalfAngleDegrees, source.PowerWatts, source.WavelengthNumber,
            source.LayoutRayCount, source.AnalysisRayCount,
            (NonSequentialVolumeSourceAngularDistribution)(int)source.AngularDistribution),
        CorePlaneRectangleParameters plane => new PlaneRectangleParameters(
            plane.WidthMillimeters, plane.HeightMillimeters, ToDto(plane.Behavior),
            plane.MaterialBefore, plane.MaterialAfter),
        CoreSphereParameters sphere => new SphereParameters(
            sphere.RadiusMillimeters, sphere.Material, ToDto(sphere.Behavior)),
        CoreCylinderParameters cylinder => new CylinderParameters(
            cylinder.RadiusMillimeters, cylinder.LengthMillimeters, cylinder.Material, ToDto(cylinder.Behavior)),
        CoreBoxParameters box => new BoxParameters(
            box.WidthMillimeters, box.HeightMillimeters, box.LengthMillimeters, box.Material, ToDto(box.Behavior)),
        CoreStandardLensParameters lens => new StandardLensParameters(
            lens.FrontRadiusMillimeters, lens.BackRadiusMillimeters, lens.FrontConic, lens.BackConic,
            lens.CenterThicknessMillimeters, lens.SemiDiameterMillimeters, lens.Material),
        CoreMeshObjectParameters mesh when document is not null => ToMeshDto(document.FindMeshAsset(mesh.MeshAssetId), mesh),
        CoreDetectorRectangleParameters detector => new DetectorRectangleParameters(
            detector.WidthMillimeters, detector.HeightMillimeters, detector.PixelsX, detector.PixelsY,
            detector.FrontOnly, detector.Absorb),
        _ => throw new InvalidDataException($"Unsupported non-sequential parameter type '{value.GetType().Name}'.")
    };

    private static CoreObjectParameters ToCore(NonSequentialObjectParameters value) => value switch
    {
        SourceRayParameters source => new CoreSourceRayParameters(
            source.PowerWatts, source.WavelengthNumber, ToCore(source.Origin), ToCore(source.Direction)),
        SourcePointParameters source => new CoreSourcePointParameters(
            source.PowerWatts, source.WavelengthNumber, source.ConeHalfAngleDegrees,
            source.LayoutRayCount, source.AnalysisRayCount),
        SourceRectangleParameters source => new CoreSourceRectangleParameters(
            source.WidthMillimeters, source.HeightMillimeters, source.AngularHalfAngleDegrees,
            source.PowerWatts, source.WavelengthNumber, source.LayoutRayCount, source.AnalysisRayCount,
            (CoreSurfaceSourceAngularDistribution)(int)source.AngularDistribution,
            source.SourceDistanceMillimeters, source.CosineExponent, source.GaussianX, source.GaussianY,
            source.SourceX, source.SourceY, source.MinimumXHalfWidthMillimeters, source.MinimumYHalfWidthMillimeters),
        SourceGaussianParameters source => new CoreSourceGaussianParameters(
            source.WaistXMillimeters, source.WaistYMillimeters, source.DivergenceHalfAngleDegrees,
            source.PowerWatts, source.WavelengthNumber, source.LayoutRayCount, source.AnalysisRayCount),
        SourceEllipseParameters source => new CoreSourceEllipseParameters(
            source.WidthMillimeters, source.HeightMillimeters, source.AngularHalfAngleDegrees,
            source.PowerWatts, source.WavelengthNumber, source.LayoutRayCount, source.AnalysisRayCount,
            (CoreSurfaceSourceAngularDistribution)(int)source.AngularDistribution,
            source.SourceDistanceMillimeters, source.CosineExponent, source.GaussianX, source.GaussianY,
            source.SourceX, source.SourceY, source.MinimumXHalfWidthMillimeters, source.MinimumYHalfWidthMillimeters),
        SourceTwoAngleParameters source => new CoreSourceTwoAngleParameters(
            source.WidthMillimeters, source.HeightMillimeters,
            (OptilandWorkbench.Core.NonSequential.NonSequentialSourceApertureShape)(int)source.Shape,
            source.AngularHalfAngleXDegrees, source.AngularHalfAngleYDegrees,
            source.PowerWatts, source.WavelengthNumber, source.LayoutRayCount, source.AnalysisRayCount),
        SourceRadialParameters source => new CoreSourceRadialParameters(
            source.Samples.Select(sample => new CoreSourceRadialSample(
                sample.AngleDegrees, sample.RelativeIntensity)).ToArray(),
            source.PowerWatts, source.WavelengthNumber, source.LayoutRayCount, source.AnalysisRayCount),
        SourceVolumeRectangleParameters source => new CoreSourceVolumeRectangleParameters(
            source.WidthMillimeters, source.HeightMillimeters, source.DepthMillimeters,
            source.AngularHalfAngleDegrees, source.PowerWatts, source.WavelengthNumber,
            source.LayoutRayCount, source.AnalysisRayCount,
            (CoreVolumeSourceAngularDistribution)(int)source.AngularDistribution),
        SourceVolumeEllipseParameters source => new CoreSourceVolumeEllipseParameters(
            source.SemiAxisXMillimeters, source.SemiAxisYMillimeters, source.SemiAxisZMillimeters,
            source.AngularHalfAngleDegrees, source.PowerWatts, source.WavelengthNumber,
            source.LayoutRayCount, source.AnalysisRayCount,
            (CoreVolumeSourceAngularDistribution)(int)source.AngularDistribution),
        SourceVolumeCylinderParameters source => new CoreSourceVolumeCylinderParameters(
            source.RadiusXMillimeters, source.RadiusYMillimeters, source.LengthMillimeters,
            source.AngularHalfAngleDegrees, source.PowerWatts, source.WavelengthNumber,
            source.LayoutRayCount, source.AnalysisRayCount,
            (CoreVolumeSourceAngularDistribution)(int)source.AngularDistribution),
        PlaneRectangleParameters plane => new CorePlaneRectangleParameters(
            plane.WidthMillimeters, plane.HeightMillimeters, ToCore(plane.Behavior),
            plane.MaterialBefore, plane.MaterialAfter),
        SphereParameters sphere => new CoreSphereParameters(
            sphere.RadiusMillimeters, sphere.Material, ToCore(sphere.Behavior)),
        CylinderParameters cylinder => new CoreCylinderParameters(
            cylinder.RadiusMillimeters, cylinder.LengthMillimeters, cylinder.Material, ToCore(cylinder.Behavior)),
        BoxParameters box => new CoreBoxParameters(
            box.WidthMillimeters, box.HeightMillimeters, box.LengthMillimeters, box.Material, ToCore(box.Behavior)),
        StandardLensParameters lens => new CoreStandardLensParameters(
            lens.FrontRadiusMillimeters, lens.BackRadiusMillimeters, lens.FrontConic, lens.BackConic,
            lens.CenterThicknessMillimeters, lens.SemiDiameterMillimeters, lens.Material),
        MeshObjectParameters mesh => new CoreMeshObjectParameters(
            mesh.MeshAssetId, ToCore(mesh.Behavior), mesh.Material, mesh.TwoSided),
        DetectorRectangleParameters detector => new CoreDetectorRectangleParameters(
            detector.WidthMillimeters, detector.HeightMillimeters, detector.PixelsX, detector.PixelsY,
            detector.FrontOnly, detector.Absorb),
        _ => throw new InvalidDataException($"Unsupported non-sequential parameter DTO '{value.GetType().Name}'.")
    };

    private static bool IsAmbient(string material) =>
        material.Equals("Air", StringComparison.OrdinalIgnoreCase)
        || material.Equals("Vacuum", StringComparison.OrdinalIgnoreCase);

    private static bool Aligned(CoordinateSystem front, CoordinateSystem back) =>
        Math.Abs(front.Origin.X - back.Origin.X) <= 1e-9
        && Math.Abs(front.Origin.Y - back.Origin.Y) <= 1e-9
        && Math.Abs(front.RotationXDegrees - back.RotationXDegrees) <= 1e-9
        && Math.Abs(front.RotationYDegrees - back.RotationYDegrees) <= 1e-9
        && Math.Abs(front.RotationZDegrees - back.RotationZDegrees) <= 1e-9;

    private static MeshObjectParameters ToMeshDto(
        OptilandWorkbench.Core.NonSequential.NonSequentialMeshAsset asset,
        CoreMeshObjectParameters parameters) => new(
            asset.Id,
            ToDto(parameters.Behavior),
            parameters.Material,
            parameters.TwoSided,
            asset.OriginalFileName,
            asset.Sha256,
            asset.VertexCount,
            asset.TriangleCount,
            asset.IsClosed,
            asset.Warnings ?? Array.Empty<string>());

    private sealed class CancellationTraceSink(
        OptilandWorkbench.Core.NonSequential.INonSequentialTraceSink inner,
        CancellationToken cancellationToken)
        : OptilandWorkbench.Core.NonSequential.INonSequentialTraceSink
    {
        public void OnBranch(OptilandWorkbench.Core.NonSequential.NonSequentialRayBranch branch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            inner.OnBranch(branch);
        }
    }
}
