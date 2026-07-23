using System.Text.Json;
using System.Globalization;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.App;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Phase;

namespace OptilandWorkbench.Tests;

public sealed class AnalysisGuiContractTests
{
    [Fact]
    public void RealImageHeightDistortionExposesConvertedAngularModel()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;
        for (var index = 0; index < optic.Fields.Count; index++)
        {
            optic.Fields[index].X = 0;
            optic.Fields[index].Y = 4.5 * index / (optic.Fields.Count - 1.0);
        }

        var connector = new OptilandConnector(optic);
        var parameters = connector.GetAnalysisParameters("Distortion");
        var view = connector.BuildAnalysisView("Distortion", new Dictionary<string, string>
        {
            ["NumPoints"] = "3"
        });

        Assert.Contains(parameters, parameter => parameter.Key == "DistortionType");
        Assert.Contains(parameters, parameter => parameter.Key == "MaximumDistortion");
        Assert.Contains(parameters, parameter => parameter.Key == "WavelengthNumber");
        Assert.Contains(parameters, parameter => parameter.Key == "ScanDirection");
        Assert.Contains(parameters, parameter => parameter.Key == "DisplayMode");
        Assert.Contains(parameters, parameter => parameter.Key == "ReferenceFieldNumber");
        Assert.Contains(parameters, parameter => parameter.Key == "IgnoreVignettingFactors");
        Assert.Contains(view.Rows, row => row.Metric == "最大视场角 (deg)");
        Assert.Contains(view.Rows, row => row.Metric == "畸变模型" && row.Value == "f-tan");

        optic.FieldDefinition = FieldDefinitionKind.Angle;
        Assert.Contains(
            connector.GetAnalysisParameters("Distortion"),
            parameter => parameter.Key == "DistortionType");

        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;
        optic.SurfaceGroup.Items[0].Thickness = 100;
        Assert.DoesNotContain(
            connector.GetAnalysisParameters("Distortion"),
            parameter => parameter.Key == "DistortionType");
    }

    [Fact]
    public void GridDistortionExposesZemaxGridSettingsWithoutPythonDistortionModel()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var parameters = connector.GetAnalysisParameters("Grid Distortion");

        Assert.DoesNotContain(parameters, parameter => parameter.Key == "DistortionType");
        Assert.Contains(parameters, parameter => parameter.Key == "DisplayMode");
        Assert.Contains(parameters, parameter => parameter.Key == "NumPoints");
        Assert.Contains(parameters, parameter => parameter.Key == "Scale");
        Assert.Contains(parameters, parameter => parameter.Key == "SymmetricMagnification");
        Assert.Contains(parameters, parameter => parameter.Key == "WavelengthNumber");
        Assert.Contains(parameters, parameter => parameter.Key == "ReferenceFieldNumber");
        Assert.Contains(parameters, parameter => parameter.Key == "HeightWidthAspect");
        Assert.Contains(parameters, parameter => parameter.Key == "FieldWidth");
    }

    [Fact]
    public void LocalIconLibraryLoadsPinnedOfflineCatalog()
    {
        var requiredIcons = new[]
        {
            "save",
            "folder-open",
            "rotate-ccw",
            "cuboid",
            "panel-left",
            "panel-top",
            "clipboard-copy",
            "plus",
            "trash-2",
            "x",
            "maximize-2",
            "circle-question-mark"
        };

        Assert.Equal(1_748, LocalIconLibrary.Names.Count);
        Assert.All(requiredIcons, iconName => Assert.True(
            LocalIconLibrary.Contains(iconName),
            $"Local Lucide catalog is missing '{iconName}'."));
    }

    [Fact]
    public void ConnectorExposesAndAppliesAnalysisParameters()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());

        Assert.Equal(
            new[]
            {
                "光线迹点",
                "像差分析",
                "波前",
                "点扩散函数",
                "MTF 曲线",
                "RMS",
                "圈入能量",
                "扩展图像分析",
                "系统报告"
            },
            MainWindow.AnalysisRibbonCategories);
        Assert.Equal(37, MainWindow.AnalysisRibbonCommandsByCategory.Sum(group => group.Value.Count));
        Assert.All(MainWindow.AnalysisRibbonCategories, category =>
            Assert.NotEmpty(MainWindow.AnalysisRibbonCommandsByCategory[category]));
        Assert.Equal(
            new[] { "光线像差图", "标准点列图", "光迹图", "离焦点列图" },
            MainWindow.AnalysisRibbonCommandsByMenu["光线迹点"]);
        Assert.Equal(
            new[] { "傅里叶 MTF", "惠更斯 MTF", "几何 MTF" },
            MainWindow.AnalysisRibbonMenusByCategory["MTF 曲线"]);
        Assert.Contains("照度与辐射", MainWindow.AnalysisRibbonMenusByCategory["扩展图像分析"]);

        var descriptors = connector.GetAnalysisParameters("点扩散函数 PSF");
        Assert.Contains(descriptors, item => item.Key == "NumRays" && item.Kind == AnalysisParameterKind.Integer);
        Assert.Contains(descriptors, item => item.Key == "GridSize" && item.DefaultValue == "0");
        Assert.Equal("PSF", connector.CanonicalAnalysisKey("点扩散函数 PSF"));

        var settings = connector.MergeAnalysisSettings("点扩散函数 PSF", new Dictionary<string, string>
        {
            ["NumRays"] = "16",
            ["GridSize"] = "32",
            ["Ignored"] = "not persisted"
        });

        Assert.Equal("16", settings["NumRays"]);
        Assert.Equal("32", settings["GridSize"]);
        Assert.DoesNotContain("Ignored", settings.Keys);

        var footprintDescriptors = connector.GetAnalysisParameters("光迹图");
        Assert.Contains(footprintDescriptors, item => item.Key == "SurfaceNumber");
        Assert.Contains(footprintDescriptors, item => item.Key == "DeleteVignetted");
        Assert.Equal("Footprint Diagram", connector.CanonicalAnalysisKey("光迹图"));
        Assert.Equal("Through Focus", connector.CanonicalAnalysisKey("离焦点列图"));
        Assert.Equal("Spot Diagram", connector.CanonicalAnalysisKey("点列图"));
        Assert.Equal("Ray Fan", connector.CanonicalAnalysisKey("光线扇形图"));
        Assert.Equal("Through Focus", connector.CanonicalAnalysisKey("离焦扫描"));

        var view = connector.BuildAnalysisView("点扩散函数 PSF", settings);

        Assert.Equal("点扩散函数 PSF", view.Name);
        AssertRow(view, "方法", "FFT");
        AssertRow(view, "瞳面采样数", "16");
        AssertRow(view, "网格尺寸", "32");
        var series = Assert.Single(view.SeriesList);
        Assert.Equal(AnalysisSeriesKind.Heatmap, series.Kind);
        Assert.NotEmpty(series.Points);
    }

    [Fact]
    public void AppSettingsRoundTripsAnalysisSettings()
    {
        var settings = new AppSettings();
        settings.AnalysisSettings["PSF"] = new Dictionary<string, string>
        {
            ["NumRays"] = "16",
            ["GridSize"] = "32"
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal("16", restored.AnalysisSettings["PSF"]["NumRays"]);
        Assert.Equal("32", restored.AnalysisSettings["PSF"]["GridSize"]);
    }

    [Fact]
    public void AppSettingsRoundTripsDisplaySettings()
    {
        var settings = new AppSettings
        {
            DecimalPlaces = 4,
            UpperScientificExponent = 7,
            LowerScientificExponent = -5,
            Theme = "Dark",
            FontFamily = "Arial",
            FontShape = "BoldItalic",
            FontSize = 16
        };

        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings));

        Assert.NotNull(restored);
        Assert.Equal(4, restored.DecimalPlaces);
        Assert.Equal(7, restored.UpperScientificExponent);
        Assert.Equal(-5, restored.LowerScientificExponent);
        Assert.Equal("Dark", restored.Theme);
        Assert.Equal("Arial", restored.FontFamily);
        Assert.Equal("BoldItalic", restored.FontShape);
        Assert.Equal(16, restored.FontSize);
    }

    [Theory]
    [InlineData(12.345, "12.35")]
    [InlineData(9999, "9999")]
    [InlineData(10000, "1E+4")]
    [InlineData(0.01, "0.01")]
    [InlineData(0.001, "1E-3")]
    public void NumericDisplayFormatterHonorsPrecisionAndExponentThresholds(double value, string expected)
    {
        var options = new NumericDisplayOptions(2, 4, -3);

        Assert.Equal(expected, NumericDisplayFormatter.Format(value, options, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConnectorExposesAndAppliesApodizationSettings()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());

        Assert.Equal(
            new[] { "无", "均匀", "高斯", "余弦平方", "Hann", "多项式", "超高斯", "Tukey" },
            connector.ApodizationKinds);

        connector.SetApodization("超高斯", 0.7, 1.0);
        var superGaussian = Assert.IsType<SuperGaussianApodization>(connector.CurrentOptic.Apodization);
        Assert.Equal(0.7, superGaussian.Width, precision: 12);
        Assert.Equal(2.0, superGaussian.Exponent, precision: 12);

        connector.SetApodization("Tukey", 0.9, 1.5);
        var tukey = Assert.IsType<TukeyApodization>(connector.CurrentOptic.Apodization);
        Assert.Equal(0.9, tukey.Radius, precision: 12);
        Assert.Equal(1.0, tukey.Alpha, precision: 12);

        connector.SetApodization("无", 1, 1);
        Assert.Null(connector.CurrentOptic.Apodization);
    }

    [Fact]
    public void ConnectorCreatesPhaseInteractionWithSerializableProfile()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var surface = connector.CurrentOptic.SurfaceGroup.Items[1];

        connector.ApplySurfaceComponents(surface, "平面", "Air", "无镀膜", "相位", "无");

        var phase = Assert.IsType<PhaseInteractionModel>(surface.InteractionModel);
        Assert.IsType<ConstantPhaseProfile>(phase.Profile);
        var restored = Optic.FromSnapshot(connector.CurrentOptic.ToSnapshot());
        var restoredPhase = Assert.IsType<PhaseInteractionModel>(restored.SurfaceGroup.Items[1].InteractionModel);
        Assert.IsType<ConstantPhaseProfile>(restoredPhase.Profile);
    }

    [Fact]
    public void ConnectorCreatesDiffractiveInteractionWithGratingGeometry()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var surface = connector.CurrentOptic.SurfaceGroup.Items[1];

        connector.ApplySurfaceComponents(
            surface,
            "平面光栅",
            "Air",
            "无镀膜",
            "反射衍射",
            "无",
            gratingOrder: -2,
            gratingPeriodMicrometers: 0.85,
            grooveOrientationAngleDegrees: 30);

        Assert.True(Assert.IsType<DiffractiveInteractionModel>(surface.InteractionModel).IsReflective);
        var grating = Assert.IsType<PlaneGratingGeometry>(surface.Geometry);
        Assert.Equal(-2, grating.GratingOrder);
        Assert.Equal(0.85, grating.GratingPeriodMicrometers, precision: 12);
        Assert.Equal(Math.PI / 6, grating.GrooveOrientationAngleRadians, precision: 12);
        var restored = Optic.FromSnapshot(connector.CurrentOptic.ToSnapshot());
        Assert.True(Assert.IsType<DiffractiveInteractionModel>(restored.SurfaceGroup.Items[1].InteractionModel).IsReflective);
        var restoredGrating = Assert.IsType<PlaneGratingGeometry>(restored.SurfaceGroup.Items[1].Geometry);
        Assert.Equal(-2, restoredGrating.GratingOrder);
    }

    [Fact]
    public void ConnectorCreatesReflectiveThinLensWithEditableFocalLength()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var surface = connector.CurrentOptic.SurfaceGroup.Items[1];

        connector.ApplySurfaceComponents(
            surface,
            "平面",
            "Air",
            "无镀膜",
            "反射薄透镜",
            "无",
            thinLensFocalLength: -72.5);

        var thinLens = Assert.IsType<ThinLensInteractionModel>(surface.InteractionModel);
        Assert.True(thinLens.IsReflective);
        Assert.Equal(-72.5, thinLens.FocalLength, precision: 12);
        var restored = Optic.FromSnapshot(connector.CurrentOptic.ToSnapshot());
        var restoredThinLens = Assert.IsType<ThinLensInteractionModel>(
            restored.SurfaceGroup.Items[1].InteractionModel);
        Assert.True(restoredThinLens.IsReflective);
        Assert.Equal(-72.5, restoredThinLens.FocalLength, precision: 12);
    }

    [Fact]
    public void MirrorMaterialControlsStandardSurfaceReflection()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var surface = connector.CurrentOptic.SurfaceGroup.Items[1];

        surface.Material = "MIRROR";
        connector.CommitSurfaceEdit(surface, nameof(OpticalSurface.Material));

        Assert.Equal("MIRROR", surface.Material);
        Assert.True(surface.IsReflective);
        Assert.True(Assert.IsType<RefractiveReflectiveInteractionModel>(surface.InteractionModel).IsReflective);
        Assert.Equal(surface.MaterialBefore.Name, surface.MaterialAfter.Name);

        surface.Material = "N-BK7";
        connector.CommitSurfaceEdit(surface, nameof(OpticalSurface.Material));

        Assert.False(surface.IsReflective);
        Assert.False(Assert.IsType<RefractiveReflectiveInteractionModel>(surface.InteractionModel).IsReflective);
        Assert.Equal("N-BK7", surface.MaterialAfter.Name);
    }

    [Fact]
    public void AddingSurfacePreservesRichComponentsAndInsertsBeforeImage()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var richSurface = connector.Surfaces[1];
        var image = connector.Surfaces[^1];
        richSurface.Geometry = new EvenAsphereGeometry(44, -0.7, new[] { 1e-5, -2e-8 });
        richSurface.PhysicalAperture = new RectangularAperture(4, 3);
        richSurface.CoatingModel = new SimpleCoatingModel(0.82, 0.07);

        connector.AddSurface();

        Assert.Same(image, connector.Surfaces[^1]);
        var added = connector.Surfaces[^2];
        Assert.Equal("Surface", added.Label);
        var asphere = Assert.IsType<EvenAsphereGeometry>(richSurface.Geometry);
        Assert.Equal(new[] { 1e-5, -2e-8 }, asphere.Coefficients);
        Assert.IsType<RectangularAperture>(richSurface.PhysicalAperture);
        Assert.IsType<SimpleCoatingModel>(richSurface.CoatingModel);
    }

    [Fact]
    public void SurfaceTableEditsSynchronizeCompositionWithoutFlatteningAsphere()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var surface = connector.Surfaces[1];
        surface.Geometry = new EvenAsphereGeometry(surface.Radius, -0.5, new[] { 4e-6, -3e-9 });

        surface.Radius = 61.5;
        surface.Conic = -0.8;
        connector.CommitSurfaceEdit(surface, nameof(surface.Radius));
        var asphere = Assert.IsType<EvenAsphereGeometry>(surface.Geometry);
        Assert.Equal(61.5, asphere.Base.Radius, precision: 12);
        Assert.Equal(-0.8, asphere.Base.Conic, precision: 12);
        Assert.Equal(new[] { 4e-6, -3e-9 }, asphere.Coefficients);

        surface.Material = "N-F2";
        connector.CommitSurfaceEdit(surface, nameof(surface.Material));
        Assert.Equal("N-F2", surface.MaterialAfter.Name);

        surface.Coating = "MgF2";
        connector.CommitSurfaceEdit(surface, nameof(surface.Coating));
        var coating = Assert.IsType<ThinFilmStackCoating>(surface.CoatingModel);
        Assert.Equal("MgF2", Assert.Single(coating.Layers).MaterialName);
    }

    [Fact]
    public void ConnectorProtectsObjectAndImageSurfacesFromDeletion()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var initialCount = connector.Surfaces.Count;

        connector.RemoveSurface(connector.Surfaces[0]);
        connector.RemoveSurface(connector.Surfaces[^1]);

        Assert.Equal(initialCount, connector.Surfaces.Count);
        Assert.Contains("不能删除", connector.Status, StringComparison.Ordinal);

        connector.RemoveSurface(connector.Surfaces[1]);
        Assert.Equal(initialCount - 1, connector.Surfaces.Count);
    }

    [Fact]
    public void SystemSettingsApplyAsOneUndoableChange()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var originalKind = connector.CurrentOptic.Aperture.Kind;
        var originalValue = connector.CurrentOptic.Aperture.Value;
        var originalFieldDefinition = connector.CurrentOptic.FieldDefinition;

        connector.ApplySystemSettings(
            connector.CurrentOptic.Backend.Current.Name,
            "F 数",
            5.6,
            "物高",
            objectSpaceTelecentric: false,
            "高斯",
            0.72,
            1);

        Assert.Equal(ApertureKind.FNumber, connector.CurrentOptic.Aperture.Kind);
        Assert.Equal(5.6, connector.CurrentOptic.Aperture.Value, precision: 12);
        Assert.Equal(FieldDefinitionKind.ObjectHeight, connector.CurrentOptic.FieldDefinition);
        Assert.IsType<GaussianApodization>(connector.CurrentOptic.Apodization);

        Assert.True(connector.Undo());
        Assert.Equal(originalKind, connector.CurrentOptic.Aperture.Kind);
        Assert.Equal(originalValue, connector.CurrentOptic.Aperture.Value, precision: 12);
        Assert.Equal(originalFieldDefinition, connector.CurrentOptic.FieldDefinition);
        Assert.Null(connector.CurrentOptic.Apodization);
        Assert.False(connector.Undo());
    }

    [Fact]
    public void SystemApertureOptionsExposeFourPythonCompatibleModes()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        Assert.Equal(
            new[] { "入瞳直径", "像方 F 数", "物方数值孔径", "按光阑面尺寸浮动" },
            connector.ApertureKindNames);

        var stop = connector.CurrentOptic.SurfaceGroup.Items.Single(surface => surface.IsStop);
        connector.SetSystemAperture("按光阑面尺寸浮动", 999);

        Assert.Equal(ApertureKind.FloatByStopSize, connector.CurrentOptic.Aperture.Kind);
        Assert.Equal(stop.SemiDiameter, connector.CurrentOptic.Aperture.Value, precision: 12);
        stop.SemiDiameter = 4.5;
        Assert.Equal(9, connector.CurrentOptic.Paraxial.EstimateEntrancePupilDiameter(), precision: 12);
    }

    [Fact]
    public void WavelengthEditingMaintainsExactlyOnePrimary()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var selected = connector.Wavelengths[^1];
        selected.IsPrimary = true;

        connector.CommitSystemEdit(selected);

        Assert.Single(connector.Wavelengths, wavelength => wavelength.IsPrimary);
        Assert.True(selected.IsPrimary);

        connector.RemoveWavelength(selected);
        Assert.Single(connector.Wavelengths, wavelength => wavelength.IsPrimary);
    }

    [Fact]
    public void ConnectorKeepsAtLeastOneFieldAndWavelength()
    {
        var connector = new OptilandConnector(Optic.CreateBlank());

        connector.RemoveField(connector.Fields[0]);
        connector.RemoveWavelength(connector.Wavelengths[0]);

        Assert.Single(connector.Fields);
        Assert.Single(connector.Wavelengths);
        Assert.True(connector.Wavelengths[0].IsPrimary);
    }

    [Fact]
    public void ImageSimulationOffersMultipleSelectableSourceImages()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());

        var sourceImage = Assert.Single(
            connector.GetAnalysisParameters("Image Simulation"),
            parameter => parameter.Key == "SourceImage");

        Assert.Equal("彩色测试卡", sourceImage.DefaultValue);
        Assert.Equal(
            new[] { "彩色测试卡", "分辨率靶标", "畸变网格", "西门子星" },
            sourceImage.Choices);
    }

    [Fact]
    public void StructuralSurfaceEditsKeepRadiusPickupsOnTheirOriginalSurfaces()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var originalImageNumber = connector.Surfaces[^1].Number;
        connector.CurrentOptic.Pickups.LinkRadius(1, originalImageNumber, scale: 2);

        connector.AddSurface();

        var shifted = Assert.Single(connector.CurrentOptic.Pickups.RadiusPickups);
        Assert.Equal(1, shifted.SourceSurface);
        Assert.Equal(originalImageNumber + 1, shifted.TargetSurface);

        connector.RemoveSurface(connector.Surfaces[2]);
        shifted = Assert.Single(connector.CurrentOptic.Pickups.RadiusPickups);
        Assert.Equal(originalImageNumber, shifted.TargetSurface);

        connector.RemoveSurface(connector.Surfaces[1]);
        Assert.Empty(connector.CurrentOptic.Pickups.RadiusPickups);
    }

    [Fact]
    public async Task ActionManagerReportsCommandFailuresWithoutRethrowing()
    {
        var manager = new ActionManager();
        var action = manager.Register(
            "failing-action",
            "失败动作",
            "测试",
            () => Task.FromException(new InvalidOperationException("expected failure")));
        ActionExecutionFailedEventArgs? failure = null;
        manager.ExecutionFailed += (_, args) => failure = args;

        var succeeded = await manager.ExecuteAsync(action);

        Assert.False(succeeded);
        Assert.NotNull(failure);
        Assert.Same(action, failure.Action);
        Assert.Equal("expected failure", failure.Exception.Message);
    }

    private static void AssertRow(AnalysisView view, string metric, string value)
    {
        var row = Assert.Single(view.Rows, item => item.Metric == metric);
        Assert.Equal(value, row.Value);
    }
}
