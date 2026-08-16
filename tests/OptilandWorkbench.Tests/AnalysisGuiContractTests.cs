using System.Text.Json;
using System.Globalization;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
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
using ContractLensLibraryService = OptilandWorkbench.Application.Contracts.ILensLibraryService;
using ContractMaterialCatalogService = OptilandWorkbench.Application.Contracts.IMaterialCatalogService;
using AnalysisContracts = OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.Tests;

public sealed class AnalysisGuiContractTests
{
    [Fact]
    public void DesktopNativeProjectPickerOnlyOffersStarOpt()
    {
        Assert.Equal(new[] { "*.staropt" }, MainWindow.NativeProjectFilePatterns);
    }

    [Fact]
    public void AnalysisSettingsUseCompactOverlayLayoutInsteadOfHorizontalWrapPanel()
    {
        var parameterPanelField = typeof(AnalysisPanel).GetField(
            "_parameterPanel",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(parameterPanelField);
        Assert.Equal(typeof(Avalonia.Controls.StackPanel), parameterPanelField.FieldType);
    }

    [Fact]
    public void MaterialAndLensLibrariesAreSeparateDocuments()
    {
        var materialConstructor = Assert.Single(typeof(MaterialLibraryPanel).GetConstructors());
        Assert.Equal(
            new[] { typeof(ContractMaterialCatalogService) },
            materialConstructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.NotNull(typeof(LensLibraryPanel).GetConstructor(new[]
        {
            typeof(ContractLensLibraryService),
            typeof(Func<string, Task>)
        }));
        Assert.NotNull(typeof(CommercialLensCatalogPanel).GetConstructor(new[]
        {
            typeof(ContractLensLibraryService),
            typeof(Func<string, Task>)
        }));
        Assert.Equal("lens-library", WorkspaceDocumentTypes.LensLibrary);
        Assert.Equal("stock-lens-catalog", WorkspaceDocumentTypes.StockLensCatalog);
        Assert.Equal("stock-lens-matching", WorkspaceDocumentTypes.StockLensMatching);
        Assert.True(WorkspaceDocumentTypes.IsKnown(WorkspaceDocumentTypes.StockLensCatalog));
        Assert.True(WorkspaceDocumentTypes.IsKnown(WorkspaceDocumentTypes.StockLensMatching));
        Assert.NotNull(typeof(StockLensMatchingPanel).GetConstructor(new[]
        {
            typeof(AnalysisContracts.IOpticalDocumentService),
            typeof(ContractLensLibraryService),
            typeof(AnalysisContracts.IWorkspaceEventStream)
        }));
        Assert.EndsWith(
            Path.Combine("Zemax", "Stockcat"),
            MainWindow.InstalledZemaxStockCatalogDirectory(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CombinedFieldCurvatureAndDistortionExposesConvertedAngularModel()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;
        for (var index = 0; index < optic.Fields.Count; index++)
        {
            optic.Fields[index].X = 0;
            optic.Fields[index].Y = 4.5 * index / (optic.Fields.Count - 1.0);
        }

        var connector = new OptilandConnector(optic);
        var parameters = connector.GetAnalysisParameters("Field Curvature and Distortion");
        var view = connector.BuildAnalysisView(
            "Field Curvature and Distortion",
            new Dictionary<string, string>());

        Assert.Contains(parameters, parameter => parameter.Key == "DistortionType");
        Assert.Contains(parameters, parameter => parameter.Key == "MaximumDistortion");
        Assert.Contains(parameters, parameter => parameter.Key == "WavelengthNumber");
        Assert.DoesNotContain(parameters, parameter => parameter.Key == "NumPoints");
        Assert.Contains(parameters, parameter => parameter.Key == "ScanDirection");
        Assert.Contains(parameters, parameter => parameter.Key == "DisplayMode");
        Assert.Contains(parameters, parameter => parameter.Key == "ReferenceFieldNumber");
        Assert.Contains(parameters, parameter => parameter.Key == "IgnoreVignettingFactors");
        var distortionPane = Assert.Single(view.PlotPanes, pane => pane.Title == "Distortion");
        Assert.Equal("Real Image Height (mm)", distortionPane.Series[0].YAxisLabel);
        Assert.Equal(4.5, distortionPane.Series[0].Points.Last().Y, precision: 9);
        var curvatureParameters = connector.GetAnalysisParameters("Field Curvature");
        Assert.Contains(curvatureParameters, parameter => parameter.Key == "WavelengthNumber");
        Assert.Contains(curvatureParameters, parameter => parameter.Key == "ScanDirection");
        Assert.Contains(curvatureParameters, parameter => parameter.Key == "IgnoreVignettingFactors");
        Assert.Contains(view.Rows, row => row.Metric == "畸变.最大视场角 (deg)");
        Assert.Contains(view.Rows, row => row.Metric == "畸变.畸变模型" && row.Value == "f-tan");

        optic.FieldDefinition = FieldDefinitionKind.Angle;
        Assert.Contains(
            connector.GetAnalysisParameters("Field Curvature and Distortion"),
            parameter => parameter.Key == "DistortionType");

        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;
        optic.SurfaceGroup.Items[0].Thickness = 100;
        Assert.DoesNotContain(
            connector.GetAnalysisParameters("Field Curvature and Distortion"),
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
    public void IncidentAngleVsImageHeightMatchesReferenceEntrySettingsAndCurves()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var parameters = connector.GetAnalysisParameters("Angle vs Image Height");

        Assert.Equal(
            new[] { "FieldDensity", "WavelengthNumber" },
            parameters.Select(parameter => parameter.Key));
        Assert.Equal("20", parameters[0].DefaultValue);
        Assert.Equal(AnalysisParameterKind.Choice, parameters[1].Kind);
        Assert.Equal("2", parameters[1].DefaultValue);

        var view = connector.BuildAnalysisView(
            "Angle vs Image Height",
            new Dictionary<string, string>
            {
                ["FieldDensity"] = "20",
                ["WavelengthNumber"] = "2"
            });

        Assert.Equal(
            new[] { "较小光瞳点光线", "主光线", "较大光瞳点光线" },
            view.SeriesList.Select(series => series.Name));
        Assert.Equal(new[] { 0, 2, 3 }, view.SeriesList.Select(series => series.ColorIndex));
        Assert.All(view.SeriesList, series =>
        {
            Assert.Equal(21, series.Points.Count);
            Assert.All(series.Points, point =>
            {
                Assert.True(double.IsFinite(point.X));
                Assert.True(double.IsFinite(point.Y));
            });
            Assert.Equal(0, series.Points[0].X, 6);
            Assert.True(series.Points[^1].X > series.Points[0].X);
        });
        Assert.Equal("像高：毫米", view.SeriesList[0].XAxisLabel);
        Assert.Equal("入射角（度）", view.SeriesList[0].YAxisLabel);
        Assert.Equal("入射角 vs. 像高", view.PlotOptions.Title);
        Assert.False(view.PlotOptions.ShowHorizontalZeroLine);
        Assert.True(view.PlotOptions.HideTopAndRightAxes);
        Assert.True(view.PlotOptions.ShowLegend);
        Assert.True(view.PlotOptions.LegendBelow);
    }

    [Fact]
    public void RayFanExposesZemaxSettings()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var parameters = connector.GetAnalysisParameters("Ray Fan");

        Assert.Equal(
            new[]
            {
                "PlotScaleMicrometers",
                "NumberOfRays",
                "UseDashes",
                "VignettedPupil",
                "CheckApertures",
                "WavelengthNumber",
                "FieldNumber",
                "TangentialAberration",
                "SagittalAberration",
                "SurfaceNumber"
            },
            parameters.Select(parameter => parameter.Key));
        Assert.Equal("20", parameters.Single(parameter => parameter.Key == "NumberOfRays").DefaultValue);
        Assert.Equal("所有", parameters.Single(parameter => parameter.Key == "WavelengthNumber").DefaultValue);
        Assert.Equal("所有", parameters.Single(parameter => parameter.Key == "FieldNumber").DefaultValue);
        Assert.Equal("像面", parameters.Single(parameter => parameter.Key == "SurfaceNumber").DefaultValue);
    }

    [Fact]
    public void SpotDiagramExposesReferenceSettings()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var parameters = connector.GetAnalysisParameters("Spot Diagram");

        Assert.Equal(
            new[]
            {
                "RayDensity",
                "Pattern",
                "ColorRaysBy",
                "Reference",
                "UsePolarization",
                "DirectionCosines",
                "ShowAiryDisk",
                "WavelengthNumber",
                "FieldNumber",
                "SurfaceNumber",
                "DisplayScale",
                "PlotScaleMicrometers",
                "ScatterRays",
                "UseSymbols"
            },
            parameters.Select(parameter => parameter.Key));
        Assert.Equal("6", parameters.Single(parameter => parameter.Key == "RayDensity").DefaultValue);
        Assert.Equal("六边", parameters.Single(parameter => parameter.Key == "Pattern").DefaultValue);
        Assert.Equal("主光线", parameters.Single(parameter => parameter.Key == "Reference").DefaultValue);
        Assert.Equal("所有", parameters.Single(parameter => parameter.Key == "WavelengthNumber").DefaultValue);
        Assert.Equal("所有", parameters.Single(parameter => parameter.Key == "FieldNumber").DefaultValue);
        Assert.Equal("像面", parameters.Single(parameter => parameter.Key == "SurfaceNumber").DefaultValue);
        Assert.Equal("true", parameters.Single(parameter => parameter.Key == "UseSymbols").DefaultValue);
    }

    [Fact]
    public void SpotDiagramAppliesSelectionScaleDirectionAndAirySettings()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var view = connector.BuildAnalysisView("Spot Diagram", new Dictionary<string, string>
        {
            ["RayDensity"] = "3",
            ["Pattern"] = "六边",
            ["Reference"] = "主光线",
            ["WavelengthNumber"] = "1",
            ["FieldNumber"] = "2",
            ["SurfaceNumber"] = "像面",
            ["DirectionCosines"] = "false",
            ["ShowAiryDisk"] = "true",
            ["PlotScaleMicrometers"] = "50",
            ["UseSymbols"] = "true"
        });

        var pane = Assert.Single(view.PlotPanes);
        Assert.Equal(2, pane.Series.Count);
        Assert.Equal("艾里斑", pane.Series[1].Name);
        Assert.Equal(-0.05, pane.PlotOptions.XMinimum);
        Assert.Equal(0.05, pane.PlotOptions.XMaximum);
        Assert.True(pane.PlotOptions.HideTickLabels);
        Assert.Contains(view.Rows, row => row.Metric == "光线密度" && row.Value == "3");
        Assert.Contains(view.Rows, row => row.Metric == "艾里斑半径 (mm)");
    }

    [Fact]
    public void FullFieldSpotDiagramUsesAbsoluteImagePositionsAndWavelengthLegend()
    {
        var optic = Optic.CreateCookeTriplet();
        var connector = new OptilandConnector(optic);
        var parameters = connector.GetAnalysisParameters("Full Field Spot Diagram");

        Assert.Equal(
            new[]
            {
                "RayDensity",
                "Pattern",
                "ColorRaysBy",
                "Reference",
                "Magnification",
                "UsePolarization",
                "ShowAiryDisk",
                "WavelengthNumber",
                "FieldNumber",
                "SurfaceNumber",
                "DisplayScale",
                "PlotScaleMicrometers",
                "ScatterRays",
                "UseSymbols"
            },
            parameters.Select(parameter => parameter.Key));
        Assert.Equal("1", parameters.Single(parameter => parameter.Key == "Magnification").DefaultValue);

        var view = connector.BuildAnalysisView("Full Field Spot Diagram");

        Assert.Equal(3, view.SeriesList.Count);
        Assert.Equal(3, view.SeriesList.Select(series => series.Name).Distinct().Count());
        Assert.All(view.SeriesList, series =>
        {
            Assert.Equal("X (µm)", series.XAxisLabel);
            Assert.Equal("Y (µm)", series.YAxisLabel);
            Assert.DoesNotContain("Field", series.Name, StringComparison.OrdinalIgnoreCase);
        });
        Assert.True(view.PlotOptions.ShowLegend);
        Assert.True(view.PlotOptions.HideTickLabels);
        Assert.True(view.PlotOptions.DefaultSquareViewport);
        Assert.True(
            view.SeriesList.SelectMany(series => series.Points).Max(point => point.Y)
            - view.SeriesList.SelectMany(series => series.Points).Min(point => point.Y) > 10);
        Assert.Contains(view.Rows, row => row.Metric == "RMS 半径 (µm)");
        Assert.Contains(view.Rows, row => row.Metric == "GEO 半径 (µm)");
        Assert.Contains(view.Rows, row => row.Metric == "缩放标尺 (µm)");

        var mapperType = typeof(OptilandConnector).Assembly.GetType(
            "OptilandWorkbench.Application.Services.WorkbenchMapper");
        Assert.NotNull(mapperType);
        var mapMethod = mapperType.GetMethod(
            "ToAnalysisViewDto",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(mapMethod);
        var viewDto = Assert.IsType<AnalysisContracts.AnalysisViewDto>(
            mapMethod.Invoke(null, new object[] { view }));
        var buildMethod = typeof(AnalysisPanel).GetMethod(
            "BuildSinglePlot",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(buildMethod);
        var layout = Assert.IsType<Avalonia.Controls.Grid>(
            buildMethod.Invoke(null, new object[] { viewDto }));
        var squareHost = Assert.Single(layout.Children.OfType<OptionalSquarePlotHost>());
        Assert.True(squareHost.IsSquare);

        var fieldColoredView = connector.BuildAnalysisView(
            "Full Field Spot Diagram",
            new Dictionary<string, string> { ["ColorRaysBy"] = "视场" });
        Assert.Equal(optic.Fields.Count, fieldColoredView.SeriesList.Count);
        Assert.All(fieldColoredView.SeriesList, series =>
        {
            Assert.StartsWith("field:", series.LegendKey, StringComparison.Ordinal);
            Assert.DoesNotContain("µm", series.Name, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MatrixSpotDiagramUsesFieldRowsAndWavelengthColumns()
    {
        var optic = Optic.CreateCookeTriplet();
        var connector = new OptilandConnector(optic);
        var parameters = connector.GetAnalysisParameters("Matrix Spot Diagram");

        Assert.Equal(
            new[]
            {
                "RayDensity",
                "Pattern",
                "ColorRaysBy",
                "Reference",
                "UsePolarization",
                "DirectionCosines",
                "ShowAiryDisk",
                "WavelengthNumber",
                "FieldNumber",
                "SurfaceNumber",
                "DisplayScale",
                "PlotScaleMicrometers",
                "ScatterRays",
                "UseSymbols",
                "IgnoreLateralColor"
            },
            parameters.Select(parameter => parameter.Key));
        Assert.Equal("false", parameters.Single(
            parameter => parameter.Key == "IgnoreLateralColor").DefaultValue);

        var view = connector.BuildAnalysisView("Matrix Spot Diagram", new Dictionary<string, string>
        {
            ["IgnoreLateralColor"] = "true"
        });

        Assert.Equal(optic.Wavelengths.Count, view.PlotPaneColumns);
        Assert.Equal(optic.Fields.Count * optic.Wavelengths.Count, view.PlotPanes.Count);
        Assert.Equal(
            optic.Wavelengths.Select(wavelength => $"{wavelength.Micrometers:0.0000} µm"),
            view.PlotPanes.Take(optic.Wavelengths.Count).Select(pane => pane.Title));
        Assert.All(view.PlotPanes, pane =>
        {
            Assert.Matches(@"^-?\d+\.\d{4} mm$", pane.Footer);
            Assert.Empty(pane.PlotOptions.Title);
            Assert.True(pane.PlotOptions.HideTickLabels);
            Assert.Null(pane.Metrics);
            Assert.Single(pane.Series);
        });
        Assert.Contains(view.Rows, row => row.Metric == "缩放标尺 (µm)");
        Assert.Contains(view.Rows, row =>
            row.Metric == "忽略垂轴色差" && row.Value == "True");
    }

    [Fact]
    public void MatrixSpotWavelengthLegendUsesSelectableUnifiedLabels()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var view = connector.BuildAnalysisView("Matrix Spot Diagram", new Dictionary<string, string>
        {
            ["RayDensity"] = "3"
        });
        var mapperType = typeof(OptilandConnector).Assembly.GetType(
            "OptilandWorkbench.Application.Services.WorkbenchMapper");
        Assert.NotNull(mapperType);
        var mapMethod = mapperType.GetMethod(
            "ToAnalysisViewDto",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(mapMethod);
        var viewDto = Assert.IsType<OptilandWorkbench.Application.Contracts.AnalysisViewDto>(
            mapMethod.Invoke(null, new object[] { view }));
        var method = typeof(AnalysisPanel).GetMethod(
            "BuildMatrixSpotPanePlot",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var layout = Assert.IsType<Avalonia.Controls.Grid>(method.Invoke(
            null,
            new object[] { viewDto.PlotPanes, viewDto.PlotPaneColumns }));
        var legend = Assert.IsType<Avalonia.Controls.StackPanel>(layout.Children[1]);
        Assert.Equal(3, legend.Children.Count);
        var firstToggle = Assert.IsType<Avalonia.Controls.CheckBox>(legend.Children[0]);
        var content = Assert.IsType<Avalonia.Controls.StackPanel>(firstToggle.Content);
        var legendLabel = Assert.IsType<Avalonia.Controls.TextBlock>(content.Children[1]);
        Assert.Equal(
            "0.4800 µm",
            legendLabel.Text);
        Assert.Equal(AnalysisPlotControl.PlotTextSize, legendLabel.FontSize);

        var matrix = Assert.IsType<Avalonia.Controls.Grid>(layout.Children[0]);
        Assert.Empty(matrix.Children.OfType<Avalonia.Controls.Viewbox>());
        Assert.True(double.IsNaN(matrix.Width));
        Assert.True(double.IsNaN(matrix.Height));
        var plotHosts = matrix.Children.OfType<OptionalSquarePlotHost>().ToArray();
        var plots = plotHosts
            .Select(host => Assert.IsType<AnalysisPlotControl>(host.Child))
            .ToArray();
        Assert.Equal(viewDto.PlotPanes.Count, plots.Length);
        Assert.All(plotHosts, host =>
        {
            Assert.True(host.IsSquare);
            Assert.True(double.IsNaN(host.Width));
            Assert.True(double.IsNaN(host.Height));
            var plot = Assert.IsType<AnalysisPlotControl>(host.Child);
            Assert.All(plot.Series, series =>
            {
                Assert.Empty(series.XAxisLabel);
                Assert.Empty(series.YAxisLabel);
                Assert.Equal(AnalysisContracts.AnalysisAxisQuantity.Unspecified, series.XQuantity);
                Assert.Equal(AnalysisContracts.AnalysisAxisQuantity.Unspecified, series.YQuantity);
            });
        });
        Assert.All(
            matrix.Children.OfType<Avalonia.Controls.TextBlock>(),
            label => Assert.Equal(AnalysisPlotControl.PlotTextSize, label.FontSize));
        var firstWavelengthPlots = plotHosts
            .Where(host => Avalonia.Controls.Grid.GetColumn(host) == 1)
            .Select(host => Assert.IsType<AnalysisPlotControl>(host.Child))
            .ToArray();
        Assert.NotEmpty(firstWavelengthPlots);
        Assert.All(firstWavelengthPlots, plot => Assert.NotEmpty(plot.Series));

        firstToggle.IsChecked = false;

        Assert.All(firstWavelengthPlots, plot => Assert.Empty(plot.Series));
    }

    [Fact]
    public void ConfigurationMatrixSpotGroupsAllWavelengthsInsideEachFieldByConfigurationCell()
    {
        var optic = Optic.CreateCookeTriplet();
        var connector = new OptilandConnector(optic);
        var view = connector.BuildAnalysisView(
            "Configuration Matrix Spot Diagram",
            new Dictionary<string, string>
            {
                ["RayDensity"] = "3"
            });
        Assert.Equal(1, view.PlotPaneColumns);
        Assert.Equal(optic.Fields.Count, view.PlotPanes.Count);
        Assert.All(
            view.PlotPanes,
            pane => Assert.Equal(optic.Wavelengths.Count, pane.Series.Count));
        var mapperType = typeof(OptilandConnector).Assembly.GetType(
            "OptilandWorkbench.Application.Services.WorkbenchMapper");
        Assert.NotNull(mapperType);
        var mapMethod = mapperType.GetMethod(
            "ToAnalysisViewDto",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(mapMethod);
        var viewDto = Assert.IsType<OptilandWorkbench.Application.Contracts.AnalysisViewDto>(
            mapMethod.Invoke(null, new object[] { view }));
        var method = typeof(AnalysisPanel).GetMethod(
            "BuildConfigurationMatrixSpotPanePlot",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var layout = Assert.IsType<Avalonia.Controls.Grid>(method.Invoke(
            null,
            new object[] { viewDto.PlotPanes, viewDto.PlotPaneColumns }));
        var matrix = Assert.IsType<Avalonia.Controls.Grid>(layout.Children[0]);

        Assert.Empty(matrix.Children.OfType<Avalonia.Controls.Viewbox>());
        Assert.True(double.IsNaN(matrix.Width));
        Assert.True(double.IsNaN(matrix.Height));
        Assert.Equal(viewDto.PlotPaneColumns + 1, matrix.ColumnDefinitions.Count);
        Assert.Equal(
            (viewDto.PlotPanes.Count / viewDto.PlotPaneColumns) + 1,
            matrix.RowDefinitions.Count);
        Assert.Equal(
            viewDto.PlotPanes.Count,
            matrix.Children.OfType<OptionalSquarePlotHost>().Count());

        var headers = matrix.Children
            .OfType<Avalonia.Controls.TextBlock>()
            .Where(label => Avalonia.Controls.Grid.GetRow(label) == 0
                && Avalonia.Controls.Grid.GetColumn(label) > 0)
            .Select(label => label.Text)
            .ToArray();
        Assert.Equal(
            new[] { "结构 1" },
            headers);

        var rowLabels = matrix.Children
            .OfType<Avalonia.Controls.TextBlock>()
            .Where(label => Avalonia.Controls.Grid.GetColumn(label) == 0
                && Avalonia.Controls.Grid.GetRow(label) > 0)
            .Select(label => label.Text)
            .ToArray();
        Assert.Equal(optic.Fields.Count, rowLabels.Length);
        Assert.All(rowLabels, label =>
        {
            Assert.DoesNotContain("结构 1", label);
            Assert.DoesNotContain("参考", label);
            Assert.DoesNotContain("µm", label);
        });

        var legend = Assert.IsType<Avalonia.Controls.StackPanel>(layout.Children[1]);
        Assert.Equal(optic.Wavelengths.Count, legend.Children.Count);
        var plots = matrix.Children
            .OfType<OptionalSquarePlotHost>()
            .Select(host => Assert.IsType<AnalysisPlotControl>(host.Child))
            .ToArray();
        Assert.Equal(optic.Fields.Count, plots.Length);
        Assert.All(plots, plot => Assert.Equal(optic.Wavelengths.Count, plot.Series.Count));

        var firstToggle = Assert.IsType<Avalonia.Controls.CheckBox>(legend.Children[0]);
        firstToggle.IsChecked = false;

        Assert.All(plots, plot => Assert.Equal(optic.Wavelengths.Count - 1, plot.Series.Count));
    }

    [Fact]
    public void AnalysisControlDispatchIgnoresLocalizedOrRenamedTitle()
    {
        var view = new AnalysisContracts.AnalysisViewDto(
            "任意修改后的标题",
            Array.Empty<AnalysisContracts.AnalysisRowDto>(),
            string.Empty,
            Array.Empty<AnalysisContracts.AnalysisSeriesDto>(),
            new AnalysisContracts.AnalysisPlotOptionsDto(),
            Array.Empty<AnalysisContracts.AnalysisPlotPaneDto>(),
            1,
            PresentationKind: AnalysisContracts.AnalysisPresentationKind.WavefrontMap);
        var method = typeof(AnalysisPanel).GetMethod(
            "IsWavefrontMapView",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.True(Assert.IsType<bool>(method.Invoke(null, new object[] { view })));
    }

    [Fact]
    public void PlotScalingAndCsvExportUseTypedUnitsInsteadOfAxisTitles()
    {
        var millimeters = new AnalysisContracts.AnalysisSeriesDto(
            "横轴 (µm)",
            "值",
            new[] { new AnalysisContracts.AnalysisPointDto(1, 2) },
            XQuantity: AnalysisContracts.AnalysisAxisQuantity.Coordinate,
            XUnit: AnalysisContracts.AnalysisAxisUnit.Millimeter,
            YQuantity: AnalysisContracts.AnalysisAxisQuantity.Intensity,
            YUnit: AnalysisContracts.AnalysisAxisUnit.Dimensionless);
        var micrometers = millimeters with
        {
            Name = "micrometer source",
            Points = new[] { new AnalysisContracts.AnalysisPointDto(1000, 3) },
            XUnit = AnalysisContracts.AnalysisAxisUnit.Micrometer
        };

        var normalized = AnalysisPlotControl.NormalizeSeriesUnits(new[] { millimeters, micrometers });
        Assert.Equal(1, normalized[1].Points[0].X, precision: 12);
        Assert.Equal(AnalysisContracts.AnalysisAxisUnit.Millimeter, normalized[1].XUnit);
        Assert.Equal(
            "横轴 (mm)",
            AnalysisAxisFormatting.FormatLabel(
                millimeters.XAxisLabel,
                millimeters.XQuantity,
                millimeters.XUnit));

        var view = new AnalysisContracts.AnalysisViewDto(
            "typed units",
            Array.Empty<AnalysisContracts.AnalysisRowDto>(),
            string.Empty,
            new[] { millimeters },
            new AnalysisContracts.AnalysisPlotOptionsDto(),
            Array.Empty<AnalysisContracts.AnalysisPlotPaneDto>(),
            1);
        var csv = AnalysisCsvFormatter.Format(view);

        Assert.Contains("\"Coordinate\",\"mm\",\"1\"", csv);
        Assert.DoesNotContain("横轴 (µm)", csv);
    }

    [Fact]
    public void FootprintLegendAndDefaultFollowTheSelectedColorBasis()
    {
        var optic = Optic.CreateCookeTriplet();
        var connector = new OptilandConnector(optic);
        var colorDescriptor = Assert.Single(
            connector.GetAnalysisParameters("Footprint Diagram"),
            descriptor => descriptor.Key == "ColorRaysBy");

        Assert.Equal("视场", colorDescriptor.DefaultValue);
        Assert.Equal(new[] { "视场", "波长" }, colorDescriptor.Choices);

        var view = connector.BuildAnalysisView(
            "Footprint Diagram",
            new Dictionary<string, string>
            {
                ["RayDensity"] = "1"
            });
        Assert.True(view.PlotOptions.DefaultSquareViewport);
        var mapperType = typeof(OptilandConnector).Assembly.GetType(
            "OptilandWorkbench.Application.Services.WorkbenchMapper");
        Assert.NotNull(mapperType);
        var mapMethod = mapperType.GetMethod(
            "ToAnalysisViewDto",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(mapMethod);
        var viewDto = Assert.IsType<OptilandWorkbench.Application.Contracts.AnalysisViewDto>(
            mapMethod.Invoke(null, new object[] { view }));
        viewDto = viewDto with
        {
            PresentationKind = AnalysisContracts.AnalysisPresentationKind.FootprintDiagram
        };
        var method = typeof(AnalysisPanel).GetMethod(
            "BuildSinglePlot",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var layout = Assert.IsType<Avalonia.Controls.Grid>(
            method.Invoke(null, new object[] { viewDto }));
        var legend = Assert.Single(layout.Children.OfType<Avalonia.Controls.StackPanel>());
        var labels = legend.Children
            .OfType<Avalonia.Controls.CheckBox>()
            .Select(checkBox =>
            {
                var content = Assert.IsType<Avalonia.Controls.StackPanel>(checkBox.Content);
                return Assert.IsType<Avalonia.Controls.TextBlock>(content.Children[1]).Text
                    ?? string.Empty;
            })
            .ToArray();
        var unit = optic.FieldDefinition == FieldDefinitionKind.Angle ? "°" : "mm";
        var expected = optic.Fields.Select((field, index) =>
            $"F{index + 1}  ({field.X.ToString("0.####", CultureInfo.InvariantCulture)}, " +
            $"{field.Y.ToString("0.####", CultureInfo.InvariantCulture)}) {unit}");

        Assert.Equal(expected, labels);
        Assert.DoesNotContain(labels, label => label.Contains("µm", StringComparison.Ordinal));

        var squareHost = Assert.Single(layout.Children.OfType<OptionalSquarePlotHost>());
        Assert.True(squareHost.IsSquare);
        var plot = Assert.IsType<AnalysisPlotControl>(squareHost.Child);
        var firstToggle = Assert.IsType<Avalonia.Controls.CheckBox>(legend.Children[0]);
        firstToggle.IsChecked = false;

        Assert.DoesNotContain(plot.Series, series => series.LegendKey == "field:1");
        Assert.Contains(plot.Series, series => series.Name == "Surface aperture");

        var summaryMethod = typeof(AnalysisPanel).GetMethod(
            "BuildCompactAnalysisSummary",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(summaryMethod);
        var summary = summaryMethod.Invoke(null, new object[] { viewDto });
        Assert.NotNull(summary);
        var lines = Assert.IsType<string>(summary.GetType().GetProperty("Lines")?.GetValue(summary));
        Assert.Contains("光线 X 最小 =", lines);
        Assert.Contains("光线 Y 最大 =", lines);
        Assert.Contains("最大半径 =", lines);
        Assert.Contains("波长 =", lines);
        Assert.Contains("图例对应于视场位置", lines);
    }

    [Fact]
    public void ExternalLegendUsesThePlotPaletteForEveryColorIndex()
    {
        foreach (var colorIndex in Enumerable.Range(0, 11))
        {
            var legendBrush = Assert.IsType<Avalonia.Media.SolidColorBrush>(
                AnalysisPanel.SeriesBrush(colorIndex));
            Assert.Equal(
                AnalysisPlotControl.SeriesColor(colorIndex),
                legendBrush.Color);
        }

        Assert.Equal(
            Avalonia.Media.Color.FromRgb(214, 39, 40),
            AnalysisPlotControl.SeriesColor(3));
    }

    [Fact]
    public void WavelengthSeriesUseStableOpticalEngineeringColorsInsteadOfCategoryOrder()
    {
        static AnalysisContracts.AnalysisSeriesDto Series(string name, int colorIndex) => new(
            string.Empty,
            string.Empty,
            Array.Empty<AnalysisContracts.AnalysisPointDto>(),
            Name: name,
            ColorIndex: colorIndex);

        var blue = AnalysisPlotControl.SeriesColor(Series("0.4861 µm", 0));
        var green = AnalysisPlotControl.SeriesColor(Series("0.5876 µm", 1));
        var red = AnalysisPlotControl.SeriesColor(Series("0.6563 µm", 2));
        var keyedRed = AnalysisPlotControl.SeriesColor(Series("arbitrary", 2) with
        {
            LegendKey = "wavelength:0.6563",
            LegendLabel = "0.6563 µm"
        });

        Assert.True(blue.B > blue.G && blue.G > blue.R);
        Assert.True(green.G > green.R && green.G > green.B);
        Assert.True(red.R > red.G && red.R > red.B);
        Assert.Equal(Avalonia.Media.Color.FromRgb(0, 140, 255), blue);
        Assert.Equal(Avalonia.Media.Color.FromRgb(0, 200, 83), green);
        Assert.Equal(Avalonia.Media.Color.FromRgb(255, 52, 48), red);
        Assert.Equal(red, keyedRed);
        Assert.NotEqual(AnalysisPlotControl.SeriesColor(2), red);

        var fieldCoded = Series("0.6563 µm", 2) with { LegendKey = "field:2" };
        Assert.Equal(
            AnalysisPlotControl.SeriesColor(fieldCoded.ColorIndex),
            AnalysisPlotControl.SeriesColor(fieldCoded));

        var infrared = AnalysisPlotControl.WavelengthColor(1064);
        Assert.Equal(Avalonia.Media.Color.FromRgb(126, 132, 145), infrared);
    }

    [Fact]
    public void MultiPanePlotsKeepConfiguredShapeWithoutExposingAUiToggle()
    {
        var bounds = OptionalSquarePlotHost.SquareBounds(new Avalonia.Size(420, 180), true);
        Assert.Equal(new Avalonia.Rect(120, 0, 180, 180), bounds);
        Assert.Equal(
            new Avalonia.Rect(0, 0, 420, 180),
            OptionalSquarePlotHost.SquareBounds(new Avalonia.Size(420, 180), false));

        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var view = connector.BuildAnalysisView("Pupil Aberration");
        var mapperType = typeof(OptilandConnector).Assembly.GetType(
            "OptilandWorkbench.Application.Services.WorkbenchMapper");
        Assert.NotNull(mapperType);
        var mapMethod = mapperType.GetMethod(
            "ToAnalysisViewDto",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(mapMethod);
        var viewDto = Assert.IsType<OptilandWorkbench.Application.Contracts.AnalysisViewDto>(
            mapMethod.Invoke(null, new object[] { view }));
        var method = typeof(AnalysisPanel).GetMethod(
            "BuildPanePlot",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var layout = Assert.IsType<Avalonia.Controls.Grid>(method.Invoke(
            null,
            new object[] { viewDto.PlotPanes, viewDto.PlotPaneColumns, false }));
        var paneGrid = Assert.IsType<Avalonia.Controls.Grid>(layout.Children[0]);
        var hosts = paneGrid.Children.OfType<OptionalSquarePlotHost>().ToArray();
        Assert.Equal(viewDto.PlotPanes.Count, hosts.Length);
        Assert.All(hosts, host => Assert.False(host.IsSquare));

        Assert.Empty(layout.Children.OfType<Avalonia.Controls.CheckBox>());
    }

    [Theory]
    [InlineData("Ray Fan", AnalysisContracts.AnalysisPresentationKind.RayFan)]
    [InlineData("Pupil Aberration", AnalysisContracts.AnalysisPresentationKind.PupilAberration)]
    public void PairedFanAnalysesGroupXYWithinEachFieldAndCenterIncompleteRows(
        string analysisName,
        AnalysisContracts.AnalysisPresentationKind expectedPresentationKind)
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var view = connector.BuildAnalysisView(analysisName);
        var mapperType = typeof(OptilandConnector).Assembly.GetType(
            "OptilandWorkbench.Application.Services.WorkbenchMapper");
        Assert.NotNull(mapperType);
        var mapMethod = mapperType.GetMethod(
            "ToAnalysisViewDto",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(mapMethod);
        var mappedViewDto = Assert.IsType<OptilandWorkbench.Application.Contracts.AnalysisViewDto>(
            mapMethod.Invoke(null, new object[] { view }));
        var viewDto = mappedViewDto with
        {
            PresentationKind = WorkbenchAnalysisCatalog.PresentationKind(analysisName)
        };
        Assert.NotEmpty(viewDto.PlotPanes);
        Assert.Equal(expectedPresentationKind, viewDto.PresentationKind);
        var method = typeof(AnalysisPanel).GetMethod(
            "BuildPairedFanPanePlot",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var layout = Assert.IsType<Avalonia.Controls.Grid>(method.Invoke(
            null,
            new object[] { viewDto.PlotPanes, true }));
        var fieldGrid = Assert.IsType<Avalonia.Controls.Grid>(layout.Children[0]);
        var cards = FanFieldCards(fieldGrid);
        var hosts = cards.SelectMany(CardHosts).ToArray();

        Assert.Equal(3, cards.Length);
        Assert.Equal(viewDto.PlotPanes.Count, hosts.Length);
        Assert.All(cards, AssertFanFieldCard);
        Assert.All(hosts, host => Assert.True(host.IsSquare));
        AssertFanFieldCardPositions(cards, (0, 0), (0, 2), (1, 1));

        Assert.Empty(layout.Children.OfType<Avalonia.Controls.CheckBox>());

        var dedicatedPanes = Enumerable.Range(0, 10)
            .Select(index => viewDto.PlotPanes[index % viewDto.PlotPanes.Count] with
            {
                Title = $"Field {index / 2 + 1}"
            })
            .ToArray();
        var dedicatedLayout = Assert.IsType<Avalonia.Controls.Grid>(method.Invoke(
            null,
            new object[] { dedicatedPanes, true }));
        var dedicatedFieldGrid = Assert.IsType<Avalonia.Controls.Grid>(dedicatedLayout.Children[0]);
        var dedicatedCards = FanFieldCards(dedicatedFieldGrid);
        var dedicatedHosts = dedicatedCards.SelectMany(CardHosts).ToArray();

        Assert.Equal(5, dedicatedCards.Length);
        Assert.Equal(10, dedicatedHosts.Length);
        Assert.All(dedicatedCards, card =>
        {
            Assert.True(double.IsNaN(card.Width));
            Assert.True(double.IsNaN(card.Height));
            AssertFanFieldCard(card);
        });
        Assert.All(dedicatedHosts, host => Assert.True(host.IsSquare));
        AssertFanFieldCardPositions(
            dedicatedCards,
            (0, 0),
            (0, 2),
            (0, 4),
            (1, 1),
            (1, 3));
    }

    private static Avalonia.Controls.Grid[] FanFieldCards(Avalonia.Controls.Grid fieldGrid)
    {
        return fieldGrid.Children
            .OfType<Avalonia.Controls.Grid>()
            .ToArray();
    }

    private static OptionalSquarePlotHost[] CardHosts(Avalonia.Controls.Grid card)
    {
        return card.Children
            .OfType<OptionalSquarePlotHost>()
            .OrderBy(Avalonia.Controls.Grid.GetColumn)
            .ToArray();
    }

    private static void AssertFanFieldCard(Avalonia.Controls.Grid card)
    {
        var hosts = CardHosts(card);
        Assert.Equal(2, hosts.Length);
        var yPlot = Assert.IsType<AnalysisPlotControl>(hosts[0].Child);
        var xPlot = Assert.IsType<AnalysisPlotControl>(hosts[1].Child);
        Assert.All(yPlot.Series, series => Assert.Equal("P_y", series.XAxisLabel));
        Assert.All(xPlot.Series, series => Assert.Equal("P_x", series.XAxisLabel));
    }

    private static void AssertFanFieldCardPositions(
        IReadOnlyList<Avalonia.Controls.Grid> cards,
        params (int Row, int Column)[] expectedPositions)
    {
        Assert.Equal(expectedPositions.Length, cards.Count);
        for (var index = 0; index < cards.Count; index++)
        {
            Assert.Equal(expectedPositions[index].Row, Avalonia.Controls.Grid.GetRow(cards[index]));
            Assert.Equal(expectedPositions[index].Column, Avalonia.Controls.Grid.GetColumn(cards[index]));
            Assert.Equal(2, Avalonia.Controls.Grid.GetColumnSpan(cards[index]));
        }
    }

    [Fact]
    public void AnalysisResultTabsUseCompactSharedTypography()
    {
        var method = typeof(AnalysisPanel).GetMethod(
            "AnalysisResultTab",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);

        var content = new Avalonia.Controls.Border();
        var tab = Assert.IsType<Avalonia.Controls.TabItem>(
            method.Invoke(null, new object[] { "绘图", content }));

        Assert.Equal("绘图", tab.Header);
        Assert.Same(content, tab.Content);
        Assert.Equal(AnalysisPlotControl.PlotTextSize, tab.FontSize);
        Assert.Equal(30, tab.MinHeight);
        Assert.Equal(new Avalonia.Thickness(12, 4), tab.Padding);
    }

    [Fact]
    public void AnalysisFooterUsesOneSharedHeightAcrossRegularAndPupilLayouts()
    {
        Assert.Equal(132, AnalysisPanel.AnalysisFooterHeight);
        var view = new AnalysisContracts.AnalysisViewDto(
            "test",
            Array.Empty<AnalysisContracts.AnalysisRowDto>(),
            string.Empty,
            Array.Empty<AnalysisContracts.AnalysisSeriesDto>(),
            new AnalysisContracts.AnalysisPlotOptionsDto(),
            Array.Empty<AnalysisContracts.AnalysisPlotPaneDto>(),
            1);
        var document = new AnalysisContracts.OpticalDocumentSnapshot(
            "test",
            null,
            0,
            string.Empty,
            false,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            0);
        var generatedAt = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.FromHours(8));
        var regularFactory = typeof(AnalysisPanel).GetMethod(
            "BuildAnalysisTitleBlock",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var pupilFactory = typeof(AnalysisPanel).GetMethod(
            "BuildPupilAberrationTitleBlock",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(regularFactory);
        Assert.NotNull(pupilFactory);
        var regular = Assert.IsType<Avalonia.Controls.Border>(
            regularFactory.Invoke(null, new object[] { view, document, generatedAt }));
        var pupil = Assert.IsType<Avalonia.Controls.Border>(
            pupilFactory.Invoke(null, new object[]
            {
                view with
                {
                    PresentationKind = AnalysisContracts.AnalysisPresentationKind.PupilAberration
                },
                generatedAt
            }));

        Assert.Equal(
            AnalysisPanel.AnalysisFooterHeight,
            Assert.IsType<Avalonia.Controls.Grid>(regular.Child).Height);
        Assert.Equal(
            AnalysisPanel.AnalysisFooterHeight,
            Assert.IsType<Avalonia.Controls.Grid>(pupil.Child).Height);
    }

    [Fact]
    public void PupilAberrationSummaryMatchesTheCompactSharedScaleLayout()
    {
        var optic = Optic.CreateCookeTriplet();
        var connector = new OptilandConnector(optic);
        var view = connector.BuildAnalysisView("Pupil Aberration");
        var mapperType = typeof(OptilandConnector).Assembly.GetType(
            "OptilandWorkbench.Application.Services.WorkbenchMapper");
        Assert.NotNull(mapperType);
        var mapMethod = mapperType.GetMethod(
            "ToAnalysisViewDto",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(mapMethod);
        var viewDto = Assert.IsType<AnalysisContracts.AnalysisViewDto>(
            mapMethod.Invoke(null, new object[] { view })) with
        {
            PresentationKind = AnalysisContracts.AnalysisPresentationKind.PupilAberration
        };
        var summaryMethod = typeof(AnalysisPanel).GetMethod(
            "BuildPupilAberrationTitleBlock",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(summaryMethod);
        var generatedAt = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.FromHours(8));
        var summary = Assert.IsAssignableFrom<Avalonia.Controls.Control>(
            summaryMethod.Invoke(null, new object[] { viewDto, generatedAt }));
        var texts = DescendantText(summary);
        var maximumScale = viewDto.PlotPanes
            .Select(pane => pane.PlotOptions.YMaximum ?? 0)
            .Max();

        var expectedTexts = new List<string>
        {
            viewDto.Name,
            "2026/8/3",
            $"最大缩放比例： ± {maximumScale.ToString("0.00E+00", CultureInfo.InvariantCulture)} Percent."
        };
        expectedTexts.AddRange(optic.Wavelengths.Select(wavelength =>
            wavelength.Micrometers.ToString("0.000", CultureInfo.InvariantCulture)));
        expectedTexts.Add("面：像面");

        Assert.Equal(expectedTexts, texts);
    }

    private static string[] DescendantText(Avalonia.Controls.Control root)
    {
        var texts = new List<string>();
        Visit(root);
        return texts.ToArray();

        void Visit(Avalonia.Controls.Control control)
        {
            if (control is Avalonia.Controls.TextBlock { Text: { } text }
                && !string.IsNullOrWhiteSpace(text))
            {
                texts.Add(text);
            }

            foreach (var child in control switch
            {
                Avalonia.Controls.Panel panel => panel.Children.OfType<Avalonia.Controls.Control>(),
                Avalonia.Controls.Decorator { Child: Avalonia.Controls.Control child } => new[] { child },
                Avalonia.Controls.ContentControl { Content: Avalonia.Controls.Control child } => new[] { child },
                _ => Array.Empty<Avalonia.Controls.Control>()
            })
            {
                Visit(child);
            }
        }
    }

    private static OptionalSquarePlotHost[] FindSquareHosts(Avalonia.Controls.Control root)
    {
        var hosts = new List<OptionalSquarePlotHost>();
        Visit(root);
        return hosts.ToArray();

        void Visit(Avalonia.Controls.Control control)
        {
            if (control is OptionalSquarePlotHost host)
            {
                hosts.Add(host);
            }

            foreach (var child in ChildControls(control))
            {
                Visit(child);
            }
        }

        static IEnumerable<Avalonia.Controls.Control> ChildControls(Avalonia.Controls.Control control)
        {
            return control switch
            {
                Avalonia.Controls.Panel panel => panel.Children.OfType<Avalonia.Controls.Control>(),
                Avalonia.Controls.Decorator { Child: Avalonia.Controls.Control child } => new[] { child },
                Avalonia.Controls.ContentControl { Content: Avalonia.Controls.Control child } => new[] { child },
                _ => Array.Empty<Avalonia.Controls.Control>()
            };
        }
    }

    [Fact]
    public void SystemPropertiesOffersCurrentAndAvailableGlassCatalogTransfers()
    {
        using var application = WorkbenchApplication.Create("cooke");
        using var panel = new SystemPropertiesPanel(
            application.Prescription,
            application.Materials,
            application.Events);
        var currentField = typeof(SystemPropertiesPanel).GetField(
            "_currentGlassCatalogs",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var availableField = typeof(SystemPropertiesPanel).GetField(
            "_availableGlassCatalogs",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var addMethod = typeof(SystemPropertiesPanel).GetMethod(
            "AddSelectedGlassCatalog",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(currentField);
        Assert.NotNull(availableField);
        Assert.NotNull(addMethod);
        var current = Assert.IsType<Avalonia.Controls.ListBox>(currentField.GetValue(panel));
        var available = Assert.IsType<Avalonia.Controls.ListBox>(availableField.GetValue(panel));
        var currentNames = Assert.IsAssignableFrom<IEnumerable<string>>(current.ItemsSource).ToArray();
        var availableNames = Assert.IsAssignableFrom<IEnumerable<string>>(available.ItemsSource).ToArray();
        Assert.NotEmpty(currentNames);
        Assert.NotEmpty(availableNames);
        Assert.Empty(currentNames.Intersect(availableNames, StringComparer.OrdinalIgnoreCase));

        available.SelectedItem = availableNames[0];
        addMethod.Invoke(panel, null);

        Assert.Contains(
            availableNames[0],
            application.Prescription.GetGlassCatalogs(),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void StandardSpotDiagramUsesCompactRowAndThreeByThreeSymmetricGrid()
    {
        Assert.Equal((Columns: 1, Rows: 1), AnalysisPanel.StandardSpotGridSize(1));
        Assert.Equal((Columns: 2, Rows: 1), AnalysisPanel.StandardSpotGridSize(2));
        Assert.Equal((Columns: 3, Rows: 1), AnalysisPanel.StandardSpotGridSize(3));
        Assert.Equal((Columns: 3, Rows: 3), AnalysisPanel.StandardSpotGridSize(5));
        Assert.Equal(
            new[] { (Column: 0, Row: 0), (Column: 1, Row: 0), (Column: 2, Row: 0) },
            Enumerable.Range(0, 3)
                .Select(index => AnalysisPanel.StandardSpotGridPosition(3, index)));

        var fiveFieldPositions = Enumerable.Range(0, 5)
            .Select(index => AnalysisPanel.StandardSpotGridPosition(5, index))
            .ToArray();
        Assert.Equal(
            new[]
            {
                (Column: 0, Row: 0),
                (Column: 2, Row: 0),
                (Column: 1, Row: 1),
                (Column: 0, Row: 2),
                (Column: 2, Row: 2)
            },
            fiveFieldPositions);

        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var view = connector.BuildAnalysisView("Spot Diagram", new Dictionary<string, string>
        {
            ["RayDensity"] = "3"
        });
        var mapperType = typeof(OptilandConnector).Assembly.GetType(
            "OptilandWorkbench.Application.Services.WorkbenchMapper");
        Assert.NotNull(mapperType);
        var mapMethod = mapperType.GetMethod(
            "ToAnalysisViewDto",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(mapMethod);
        var viewDto = Assert.IsType<OptilandWorkbench.Application.Contracts.AnalysisViewDto>(
            mapMethod.Invoke(null, new object[] { view }));
        var method = typeof(AnalysisPanel).GetMethod(
            "BuildStandardSpotPanePlot",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var layout = Assert.IsType<Avalonia.Controls.Grid>(
            method.Invoke(null, new object[] { viewDto.PlotPanes }));
        var fieldGrid = Assert.IsType<Avalonia.Controls.Grid>(layout.Children[0]);
        Assert.Empty(fieldGrid.Children.OfType<Avalonia.Controls.Viewbox>());
        Assert.True(double.IsNaN(fieldGrid.Width));
        Assert.True(double.IsNaN(fieldGrid.Height));
        Assert.Equal(3, fieldGrid.ColumnDefinitions.Count);
        Assert.Single(fieldGrid.RowDefinitions);
        Assert.Equal(viewDto.PlotPanes.Count, fieldGrid.Children.Count);
        Assert.All(fieldGrid.Children.OfType<Avalonia.Controls.Grid>(), card =>
        {
            var host = Assert.Single(card.Children.OfType<OptionalSquarePlotHost>());
            Assert.True(host.IsSquare);
            Assert.True(double.IsNaN(host.Width));
            Assert.True(double.IsNaN(host.Height));
            var plot = Assert.IsType<AnalysisPlotControl>(host.Child);
            Assert.All(plot.Series, series =>
            {
                Assert.False(string.IsNullOrWhiteSpace(series.XAxisLabel));
                Assert.False(string.IsNullOrWhiteSpace(series.YAxisLabel));
            });
        });
    }

    [Fact]
    public void ThroughFocusSpotExposesReferenceSettings()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var parameters = connector.GetAnalysisParameters("Through Focus");

        Assert.Equal(
            new[]
            {
                "RayDensity",
                "Pattern",
                "ColorRaysBy",
                "Reference",
                "DefocusStepMicrometers",
                "UsePolarization",
                "ShowAiryDisk",
                "WavelengthNumber",
                "FieldNumber",
                "SurfaceNumber",
                "DisplayScale",
                "PlotScaleMicrometers",
                "ScatterRays",
                "UseSymbols"
            },
            parameters.Select(parameter => parameter.Key));
        Assert.Equal("6", parameters.Single(parameter => parameter.Key == "RayDensity").DefaultValue);
        Assert.Equal("六边", parameters.Single(parameter => parameter.Key == "Pattern").DefaultValue);
        Assert.Equal("主光线", parameters.Single(parameter => parameter.Key == "Reference").DefaultValue);
        Assert.Equal(
            "50",
            parameters.Single(parameter => parameter.Key == "DefocusStepMicrometers").DefaultValue);
        Assert.Equal("像面", parameters.Single(parameter => parameter.Key == "SurfaceNumber").DefaultValue);
    }

    [Fact]
    public void ThroughFocusSpotBuildsVisibleFiveColumnMatrix()
    {
        var optic = Optic.CreateCookeTriplet();
        var connector = new OptilandConnector(optic);
        var view = connector.BuildAnalysisView("Through Focus", new Dictionary<string, string>
        {
            ["RayDensity"] = "3",
            ["Pattern"] = "六边",
            ["Reference"] = "主光线",
            ["DefocusStepMicrometers"] = "50",
            ["WavelengthNumber"] = "所有",
            ["FieldNumber"] = "所有",
            ["SurfaceNumber"] = "像面",
            ["PlotScaleMicrometers"] = "0",
            ["UseSymbols"] = "true"
        });

        Assert.Equal(5, view.PlotPaneColumns);
        Assert.Equal(optic.Fields.Count * 5, view.PlotPanes.Count);
        Assert.Contains("Defocus: -0.100 mm", view.PlotPanes[0].Title);
        Assert.Contains("Defocus: +0.100 mm", view.PlotPanes[4].Title);
        Assert.Equal(optic.Fields.Count, view.PlotPanes.Count(pane => pane.Metrics is { Count: 2 }));
        Assert.All(
            Enumerable.Range(0, optic.Fields.Count),
            fieldIndex => Assert.Equal(
                new[] { "RMS 半径", "GEO 半径" },
                view.PlotPanes[(fieldIndex * 5) + 2].Metrics!.Select(metric => metric.Label)));
        Assert.Contains(view.Rows, row => row.Metric == "缩放标尺 (µm)");
        Assert.All(view.PlotPanes, pane =>
        {
            Assert.True(pane.PlotOptions.HideTickLabels);
            Assert.NotEmpty(pane.Footer);
            Assert.Contains(pane.Series, series => series.Points.Count > 0);
        });

        var mapperType = typeof(OptilandConnector).Assembly.GetType(
            "OptilandWorkbench.Application.Services.WorkbenchMapper");
        Assert.NotNull(mapperType);
        var mapMethod = mapperType.GetMethod(
            "ToAnalysisViewDto",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(mapMethod);
        var viewDto = Assert.IsType<AnalysisContracts.AnalysisViewDto>(
            mapMethod.Invoke(null, new object[] { view }));
        var buildMethod = typeof(AnalysisPanel).GetMethod(
            "BuildThroughFocusSpotPanePlot",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(buildMethod);
        var layout = Assert.IsType<Avalonia.Controls.Grid>(buildMethod.Invoke(
            null,
            new object[] { viewDto.PlotPanes, viewDto.PlotPaneColumns }));
        var matrix = Assert.IsType<Avalonia.Controls.Grid>(layout.Children[0]);
        Assert.Empty(matrix.Children.OfType<Avalonia.Controls.Viewbox>());
        Assert.True(double.IsNaN(matrix.Width));
        Assert.True(double.IsNaN(matrix.Height));
        var plotHosts = matrix.Children.OfType<OptionalSquarePlotHost>().ToArray();
        var plots = plotHosts
            .Select(host => Assert.IsType<AnalysisPlotControl>(host.Child))
            .ToArray();

        Assert.Equal(view.PlotPanes.Count, plots.Length);
        Assert.All(plotHosts, host =>
        {
            Assert.True(host.IsSquare);
            Assert.True(double.IsNaN(host.Width));
            Assert.True(double.IsNaN(host.Height));
            var plot = Assert.IsType<AnalysisPlotControl>(host.Child);
            Assert.True(plot.PlotOptions.HideTickLabels);
            Assert.All(plot.Series, series =>
            {
                Assert.Empty(series.XAxisLabel);
                Assert.Empty(series.YAxisLabel);
                Assert.Equal(AnalysisContracts.AnalysisAxisQuantity.Unspecified, series.XQuantity);
                Assert.Equal(AnalysisContracts.AnalysisAxisQuantity.Unspecified, series.YQuantity);
            });
        });
        Assert.All(
            matrix.Children.OfType<Avalonia.Controls.TextBlock>(),
            label => Assert.Equal(AnalysisPlotControl.PlotTextSize, label.FontSize));
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
    public void AnalysisRibbonCategoriesAreDropdownsWithoutPrimaryActions()
    {
        var factory = typeof(MainWindow).GetMethod(
            "RibbonAnalysisMenuButton",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(factory);
        Assert.Equal(typeof(Avalonia.Controls.DropDownButton), factory.ReturnType);
    }

    [Fact]
    public void AnalysisRibbonDropdownIndicatorIsSmallSolidTriangleBelowLabel()
    {
        var factory = typeof(MainWindow).GetMethod(
            "RibbonDropDownCommandContent",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(factory);
        var content = Assert.IsType<Avalonia.Controls.Grid>(
            factory.Invoke(null, new object[] { "image", "扩展图像分析" }));
        Assert.Equal(3, content.RowDefinitions.Count);
        Assert.True(double.IsNaN(content.Width));
        Assert.True(double.IsNaN(content.Height));
        Assert.Equal(66, content.MinWidth);
        Assert.Equal(52, content.MinHeight);

        var arrow = Assert.Single(content.Children.OfType<Avalonia.Controls.Shapes.Polygon>());
        Assert.Equal(2, Avalonia.Controls.Grid.GetRow(arrow));
        Assert.Equal(6, arrow.Width);
        Assert.Equal(4, arrow.Height);
        Assert.Equal(0.72, arrow.Opacity);
        Assert.Equal(3, arrow.Points.Count);

        var hoverBinder = typeof(MainWindow).GetMethod(
            "AttachRibbonCommandHover",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(hoverBinder);
        Assert.Equal(
            typeof(Avalonia.Controls.Button),
            hoverBinder.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void RibbonHoverOverridesTheFluentTemplatePresenter()
    {
        var selector = OptilandWorkbench.App.App
            .RibbonCommandPointerOverSelector(null)
            .ToString();

        Assert.Contains(":is(Button).ribbon-command:pointerover", selector, StringComparison.Ordinal);
        Assert.Contains("/template/", selector, StringComparison.Ordinal);
        Assert.Contains("ContentPresenter#PART_ContentPresenter", selector, StringComparison.Ordinal);
    }

    [Fact]
    public void RibbonTabsHaveTemplateLevelPointerOverFeedback()
    {
        var selector = OptilandWorkbench.App.App
            .RibbonTabPointerOverSelector(null)
            .ToString();
        Assert.Contains("TabItem.ribbon-tab:pointerover", selector, StringComparison.Ordinal);
        Assert.Contains("/template/", selector, StringComparison.Ordinal);
        Assert.Contains("Border#PART_LayoutRoot", selector, StringComparison.Ordinal);

        var factory = typeof(MainWindow).GetMethod(
            "RibbonTab",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(factory);
        var tab = Assert.IsType<Avalonia.Controls.TabItem>(
            factory.Invoke(null, new object[] { "分析", new Avalonia.Controls.Border() }));
        Assert.Contains("ribbon-tab", tab.Classes);
    }

    [Fact]
    public void RibbonMenuItemsHaveTemplateLevelPointerOverFeedback()
    {
        var selector = OptilandWorkbench.App.App
            .RibbonMenuItemPointerOverSelector(null)
            .ToString();
        Assert.Contains("MenuItem.ribbon-menu-item:pointerover", selector, StringComparison.Ordinal);
        Assert.Contains("/template/", selector, StringComparison.Ordinal);
        Assert.Contains("Border#PART_LayoutRoot", selector, StringComparison.Ordinal);

        var hoverBinder = typeof(MainWindow).GetMethod(
            "AttachRibbonMenuItemHover",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(hoverBinder);
        Assert.Equal(
            typeof(Avalonia.Controls.MenuItem),
            hoverBinder.GetParameters()[0].ParameterType);
        Assert.Equal(
            typeof(OptilandWorkbench.App.Controls.LocalIconLabel),
            hoverBinder.GetParameters()[1].ParameterType);
    }

    [Fact]
    public void SolidBlueSelectionStatesShareTheBrandAccent()
    {
        Assert.Equal(
            Avalonia.Media.Color.FromRgb(0, 122, 255),
            OptilandWorkbench.App.App.BrandAccentColor);
        Assert.Contains(
            "DockSurfaceHeaderActiveBrush",
            OptilandWorkbench.App.App.UnifiedDockAccentResourceKeys);
        Assert.Contains(
            "DockTabActiveBackgroundBrush",
            OptilandWorkbench.App.App.UnifiedDockAccentResourceKeys);

        var selector = OptilandWorkbench.App.App
            .DataGridSelectedRowSelector(null)
            .ToString();
        Assert.Equal("DataGridRow:selected", selector);
    }

    [Fact]
    public void PanePlotsKeepXAxisLabelsVisibleWhenTickLabelsAreHidden()
    {
        Assert.Equal(18, AnalysisPlotControl.XAxisLabelOffset(hideTickLabels: true));
        Assert.Equal(35, AnalysisPlotControl.XAxisLabelOffset(hideTickLabels: false));
        Assert.True(
            AnalysisPlotControl.XAxisLabelOffset(hideTickLabels: true) <
            AnalysisPlotControl.XAxisLabelOffset(hideTickLabels: false));
    }

    [Fact]
    public void CompactPlotsPreserveMarginsWheneverAxisLabelsArePresent()
    {
        Assert.False(AnalysisPlotControl.CanUseMinimalAxisMargins(
            compact: true,
            hideAxes: false,
            hideTickLabels: true,
            xAxisLabel: "X (mm)",
            yAxisLabel: "Y (mm)"));
        Assert.False(AnalysisPlotControl.CanUseMinimalAxisMargins(
            compact: true,
            hideAxes: false,
            hideTickLabels: true,
            xAxisLabel: "P_y",
            yAxisLabel: string.Empty));
        Assert.True(AnalysisPlotControl.CanUseMinimalAxisMargins(
            compact: true,
            hideAxes: false,
            hideTickLabels: true,
            xAxisLabel: string.Empty,
            yAxisLabel: string.Empty));
    }

    [Fact]
    public void ConnectorExposesAndAppliesAnalysisParameters()
    {
        var optic = Optic.CreateCookeTriplet();
        var connector = new OptilandConnector(optic);

        Assert.Equal(
            new[]
            {
                "光线迹点",
                "报告",
                "像差分析",
                "波前",
                "点扩散函数",
                "MTF 曲线",
                "RMS",
                "圈入能量",
                "扩展图像分析"
            },
            MainWindow.AnalysisRibbonCategories);
        Assert.Equal(71, MainWindow.AnalysisRibbonCommandsByCategory.Sum(group => group.Value.Count));
        Assert.All(MainWindow.AnalysisRibbonCategories, category =>
        {
            Assert.NotEmpty(MainWindow.AnalysisRibbonCommandsByCategory[category]);
            Assert.Equal(
                new[] { category },
                MainWindow.AnalysisRibbonMenusByCategory[category]);
        });
        Assert.Equal(65, MainWindow.AnalysisRibbonCommandsByMenu.Sum(menu => menu.Value.Count));
        var commandIds = MainWindow.AnalysisRibbonCommandIdsByMenu
            .SelectMany(menu => menu.Value)
            .ToArray();
        Assert.Equal(
            commandIds.Length,
            commandIds.Distinct(StringComparer.Ordinal).Count());

        var displayNames = MainWindow.AnalysisRibbonDisplayNames.Values;
        Assert.Contains("FFT PSF", displayNames);
        Assert.Contains("FFT PSF截面图", displayNames);
        Assert.Contains("傅里叶 MTF VS 视场", displayNames);
        Assert.Contains("Zernike", displayNames);
        Assert.Contains("Jones 瞳", displayNames);
        Assert.Contains("Y-Ybar", displayNames);
        Assert.Equal(
            new[] { "RMS vs. 视场", "RMS vs. 波长", "RMS vs. 离焦", "二维视场RMS图" },
            MainWindow.AnalysisRibbonCommandsByMenu["RMS"]);
        var rmsVsFieldParameters = connector.GetAnalysisParameters("RMS vs. 视场");
        Assert.Contains(rmsVsFieldParameters, parameter => parameter.Key == "Method");
        Assert.Contains(rmsVsFieldParameters, parameter => parameter.Key == "Data");
        Assert.Contains(rmsVsFieldParameters, parameter => parameter.Key == "Reference");
        Assert.Contains(rmsVsFieldParameters, parameter => parameter.Key == "ShowDiffractionLimit");
        Assert.Contains(rmsVsFieldParameters, parameter => parameter.Key == "UsePolarization");
        Assert.Contains(rmsVsFieldParameters, parameter => parameter.Key == "RemoveVignetting");
        var rmsWavefrontView = connector.BuildAnalysisView(
            "RMS vs. 视场",
            new Dictionary<string, string>
            {
                ["Data"] = "wavefront",
                ["Method"] = "RA"
            });
        Assert.Contains(rmsWavefrontView.Rows, row => row.Metric == "数据" && row.Value == "wavefront");
        Assert.Equal(
            new[] { "\u884d\u5c04", "\u51e0\u4f55", "\u51e0\u4f55\u7ebf/\u8fb9\u7f18\u6269\u6563", "\u6269\u5c55\u5149\u6e90" },
            MainWindow.AnalysisRibbonCommandsByMenu["\u5708\u5165\u80fd\u91cf"]);
        Assert.Equal(
            new[]
            {
                "图像模拟",
                "几何图像分析",
                "几何位图图像分析",
                "光源分析",
                "部分相干图像分析",
                "扩展图像分析",
                "相对照度",
                "IMA和BIM图片浏览器",
                "位图文件查看器"
            },
            MainWindow.AnalysisRibbonCommandsByMenu["扩展图像分析"]);
        Assert.Equal(
            "Diffraction Encircled Energy",
            connector.CanonicalAnalysisKey("\u884d\u5c04"));
        Assert.Equal(
            "Geometric Line Edge Spread",
            connector.CanonicalAnalysisKey("\u51e0\u4f55\u7ebf/\u8fb9\u7f18\u6269\u6563"));
        Assert.Equal(
            "Extended Source Encircled Energy",
            connector.CanonicalAnalysisKey("\u6269\u5c55\u5149\u6e90"));
        Assert.Equal(
            new[]
            {
                "单光线追迹",
                "标准点列图",
                "光迹图",
                "离焦点列图",
                "全视场点列图",
                "矩阵点列图",
                "结构矩阵点列图",
                "Y-Ybar",
                "渐晕图",
                "入射角 vs. 像高"
            },
            MainWindow.AnalysisRibbonCommandsByMenu["光线迹点"]);
        Assert.Equal(
            new[]
            {
                "表面数据报告",
                "系统数据报告",
                "分类数据报告",
                "系统数据摘要",
                "基面数据"
            },
            MainWindow.AnalysisRibbonCommandsByMenu["报告"]);
        Assert.Equal(
            new[]
            {
                "光线像差图",
                "光瞳像差",
                "全视场像差",
                "场曲/畸变",
                "网格畸变",
                "轴向像差",
                "垂轴色差",
                "色焦移",
                "赛德尔系数",
                "赛德尔图"
            },
            MainWindow.AnalysisRibbonCommandsByMenu["像差分析"]);
        Assert.Equal(
            new[]
            {
                "光程差图",
                "波前图",
                "干涉图",
                "傅科分析",
                "对比度损失图",
                "Zernike Fringe系数",
                "Zernike Standard系数",
                "Zernike Annular系数",
                "Zernike系数 vs. 视场"
            },
            MainWindow.AnalysisRibbonCommandsByMenu["波前"]);
        Assert.Equal(
            new[]
            {
                "FFT PSF",
                "FFT PSF截面图",
                "FFT 线/边缘扩散",
                "惠更斯PSF",
                "惠更斯PSF截面图"
            },
            MainWindow.AnalysisRibbonCommandsByMenu["点扩散函数"]);
        Assert.Equal("Wavefront Map", connector.CanonicalAnalysisKey("波前图"));
        var wavefrontMapParameters = connector.GetAnalysisParameters("波前图");
        Assert.Equal(
            new[]
            {
                "Sampling",
                "Rotation",
                "DisplayScale",
                "Apodization",
                "ReferenceChiefRay",
                "UseExitPupilShape",
                "WavelengthNumber",
                "FieldNumber",
                "SurfaceNumber",
                "DisplayAs",
                "RemoveTilt",
                "PupilSx",
                "PupilSy",
                "PupilSr"
            },
            wavefrontMapParameters.Select(parameter => parameter.Key));
        Assert.Equal("64 x 64", wavefrontMapParameters
            .Single(parameter => parameter.Key == "Sampling").DefaultValue);
        var wavefrontMapView = connector.BuildAnalysisView(
            "波前图",
            new Dictionary<string, string>
            {
                ["Sampling"] = "16 x 16",
                ["WavelengthNumber"] = "1",
                ["FieldNumber"] = "1"
            });
        Assert.Equal("波前图", wavefrontMapView.Name);
        Assert.Equal(AnalysisSeriesKind.Heatmap, Assert.Single(wavefrontMapView.SeriesList).Kind);
        Assert.Equal("Interferogram", connector.CanonicalAnalysisKey("干涉图"));
        var interferogramView = connector.BuildAnalysisView("干涉图");
        Assert.Equal("干涉图", interferogramView.Name);
        Assert.Equal(AnalysisSeriesKind.Heatmap, Assert.Single(interferogramView.SeriesList).Kind);
        Assert.True(interferogramView.PlotOptions.EqualAspect);
        Assert.True(interferogramView.PlotOptions.DefaultSquareViewport);
        Assert.Equal("Foucault Analysis", connector.CanonicalAnalysisKey("傅科分析"));
        var foucaultParameters = connector.GetAnalysisParameters("傅科分析");
        Assert.Equal(
            new[]
            {
                "Sampling",
                "Type",
                "DisplayAs",
                "KnifeEdge",
                "DataSource",
                "WavelengthNumber",
                "FieldNumber",
                "YPositionMicrometers",
                "UsePolarization"
            },
            foucaultParameters.Select(parameter => parameter.Key));
        Assert.Equal("32 x 32", foucaultParameters
            .Single(parameter => parameter.Key == "Sampling").DefaultValue);
        var foucaultView = connector.BuildAnalysisView(
            "傅科分析",
            new Dictionary<string, string>
            {
                ["Sampling"] = "16 x 16",
                ["WavelengthNumber"] = "1",
                ["FieldNumber"] = "1"
            });
        Assert.Equal("傅科分析", foucaultView.Name);
        var foucaultSeries = Assert.Single(foucaultView.SeriesList);
        Assert.Equal(AnalysisSeriesKind.Heatmap, foucaultSeries.Kind);
        Assert.All(foucaultSeries.Points, point => Assert.InRange(point.Value!.Value, 0, 1));
        Assert.Equal("Contrast Loss Map", connector.CanonicalAnalysisKey("对比度损失图"));
        var contrastLossParameters = connector.GetAnalysisParameters("对比度损失图");
        Assert.Equal(
            new[] { "Sampling", "Frequency", "Normalize", "WavelengthNumber", "FieldNumber", "ShowOPD" },
            contrastLossParameters.Select(parameter => parameter.Key));
        var contrastLossView = connector.BuildAnalysisView(
            "对比度损失图",
            new Dictionary<string, string>
            {
                ["Sampling"] = "8",
                ["WavelengthNumber"] = "1",
                ["FieldNumber"] = "1"
            });
        Assert.Equal("对比度损失图", contrastLossView.Name);
        Assert.Equal(2, contrastLossView.PlotPanes.Count);
        Assert.All(contrastLossView.PlotPanes, pane =>
            Assert.Equal(AnalysisSeriesKind.Heatmap, Assert.Single(pane.Series).Kind));
        Assert.Contains(contrastLossView.Rows, row => row.Metric == "方法" && row.Value == "Moore-Elliott");
        Assert.Equal("Zernike Fringe", connector.CanonicalAnalysisKey("Zernike Fringe系数"));
        var zernikeFringeParameters = connector.GetAnalysisParameters("Zernike Fringe系数");
        Assert.Equal(
            new[] { "PupilSampling", "ZernikeTerms", "WavelengthNumber", "FieldNumber" },
            zernikeFringeParameters.Select(parameter => parameter.Key));
        Assert.Equal("32 x 32", zernikeFringeParameters
            .Single(parameter => parameter.Key == "PupilSampling").DefaultValue);
        Assert.Equal(37, zernikeFringeParameters
            .Single(parameter => parameter.Key == "ZernikeTerms").Maximum);
        Assert.Equal("1 - 轴上视场", zernikeFringeParameters
            .Single(parameter => parameter.Key == "FieldNumber").DefaultValue);
        var zernikeFringeView = connector.BuildAnalysisView(
            "Zernike Fringe系数",
            new Dictionary<string, string>
            {
                ["PupilSampling"] = "32 x 32",
                ["ZernikeTerms"] = "12",
                ["WavelengthNumber"] = "1",
                ["FieldNumber"] = "1 - 轴上视场"
            });
        Assert.Equal("Zernike Fringe系数", zernikeFringeView.Name);
        Assert.Contains("使用 Zernike Fringe 多项式", zernikeFringeView.ReportText);
        Assert.Contains("RMS 匹配误差", zernikeFringeView.ReportText);
        Assert.Contains("Z   1", zernikeFringeView.ReportText);
        Assert.Contains(":  1", zernikeFringeView.ReportText);
        Assert.Contains("COS (A)", zernikeFringeView.ReportText);
        Assert.Equal("Zernike Standard", connector.CanonicalAnalysisKey("Zernike Standard系数"));
        var zernikeStandardParameters = connector.GetAnalysisParameters("Zernike Standard系数");
        Assert.Equal(
            new[] { "NumRings", "ZernikeTerms", "WavelengthNumber", "FieldNumber" },
            zernikeStandardParameters.Select(parameter => parameter.Key));
        var zernikeStandardView = connector.BuildAnalysisView(
            "Zernike Standard系数",
            new Dictionary<string, string>
            {
                ["NumRings"] = "5",
                ["ZernikeTerms"] = "12",
                ["WavelengthNumber"] = "1",
                ["FieldNumber"] = "1 - 轴上视场"
            });
        Assert.Equal("Zernike Standard系数", zernikeStandardView.Name);
        Assert.Contains("使用 Zernike Standard 多项式", zernikeStandardView.ReportText);
        Assert.Contains("来自集合光线", zernikeStandardView.ReportText);
        Assert.Contains("来自集合匹配系数", zernikeStandardView.ReportText);
        Assert.Contains("Z   2", zernikeStandardView.ReportText);
        Assert.Contains("4^(1/2) (p) * COS (A)", zernikeStandardView.ReportText);
        Assert.Contains("6^(1/2) (p^2) * COS (2A)", zernikeStandardView.ReportText);
        Assert.Equal("Zernike Annular", connector.CanonicalAnalysisKey("Zernike Annular系数"));
        var zernikeAnnularParameters = connector.GetAnalysisParameters("Zernike Annular系数");
        Assert.Equal(
            new[] { "NumRings", "ZernikeTerms", "ObscurationRatio", "WavelengthNumber", "FieldNumber" },
            zernikeAnnularParameters.Select(parameter => parameter.Key));
        Assert.Equal("0.5", zernikeAnnularParameters
            .Single(parameter => parameter.Key == "ObscurationRatio").DefaultValue);
        var zernikeAnnularView = connector.BuildAnalysisView(
            "Zernike Annular系数",
            new Dictionary<string, string>
            {
                ["NumRings"] = "5",
                ["ZernikeTerms"] = "12",
                ["ObscurationRatio"] = "0.5",
                ["WavelengthNumber"] = "1",
                ["FieldNumber"] = "1 - 轴上视场"
            });
        Assert.Equal("Zernike Annular系数", zernikeAnnularView.Name);
        Assert.Contains("使用 Zernike Annular 多项式", zernikeAnnularView.ReportText);
        Assert.Contains("遮光", zernikeAnnularView.ReportText);
        Assert.Contains("0.5000", zernikeAnnularView.ReportText);
        Assert.Contains("Z   1", zernikeAnnularView.ReportText);
        Assert.DoesNotContain("COS (A)", zernikeAnnularView.ReportText);
        Assert.NotEqual(zernikeStandardView.ReportText, zernikeAnnularView.ReportText);
        Assert.NotEqual(
            zernikeStandardView.Rows.Single(row => row.Metric.StartsWith("Z4 ", StringComparison.Ordinal)).Value,
            zernikeAnnularView.Rows.Single(row => row.Metric.StartsWith("Z4 ", StringComparison.Ordinal)).Value);
        Assert.Equal("Zernike vs Field", connector.CanonicalAnalysisKey("Zernike系数 vs. 视场"));
        var zernikeVsFieldParameters = connector.GetAnalysisParameters("Zernike系数 vs. 视场");
        Assert.Equal(
            new[] { "FieldDensity", "NumRings", "ZernikeTerms", "WavelengthNumber" },
            zernikeVsFieldParameters.Select(parameter => parameter.Key));
        var zernikeVsFieldView = connector.BuildAnalysisView(
            "Zernike系数 vs. 视场",
            new Dictionary<string, string>
            {
                ["FieldDensity"] = "5",
                ["NumRings"] = "5",
                ["ZernikeTerms"] = "8",
                ["WavelengthNumber"] = "1"
            });
        Assert.Equal("Zernike系数 vs. 视场", zernikeVsFieldView.Name);
        Assert.Equal(8, zernikeVsFieldView.SeriesList.Count);
        Assert.Equal(Enumerable.Range(1, 8).Select(index => index.ToString()),
            zernikeVsFieldView.SeriesList.Select(series => series.Name));
        Assert.All(zernikeVsFieldView.SeriesList, series =>
        {
            Assert.Equal(6, series.Points.Count);
            Assert.Equal("视场为 度", series.XAxisLabel);
            Assert.Equal("波前差 (waves)", series.YAxisLabel);
        });
        Assert.Equal("Zernike Fringe系数项 vs. 视场", zernikeVsFieldView.PlotOptions.Title);
        Assert.True(zernikeVsFieldView.PlotOptions.ShowLegend);
        Assert.True(zernikeVsFieldView.PlotOptions.LegendBelow);
        Assert.Equal("Optical Path Difference", connector.CanonicalAnalysisKey("光程差图"));
        var opticalPathDifferenceParameters = connector.GetAnalysisParameters("光程差图");
        Assert.Equal(
            new[]
            {
                "GraphScale",
                "NumberOfRays",
                "UseDashes",
                "VignettedPupil",
                "CheckApertures",
                "WavelengthNumber",
                "FieldNumber",
                "SurfaceNumber"
            },
            opticalPathDifferenceParameters.Select(parameter => parameter.Key));
        Assert.Equal("20", opticalPathDifferenceParameters
            .Single(parameter => parameter.Key == "NumberOfRays").DefaultValue);
        var opticalPathDifferenceView = connector.BuildAnalysisView(
            "光程差图",
            new Dictionary<string, string>
            {
                ["NumberOfRays"] = "3",
                ["WavelengthNumber"] = "1",
                ["FieldNumber"] = "1"
            });
        Assert.Equal(2, opticalPathDifferenceView.PlotPanes.Count);
        Assert.Equal(2, opticalPathDifferenceView.PlotPaneColumns);
        Assert.All(opticalPathDifferenceView.PlotPanes, pane =>
            Assert.All(pane.Series, series => Assert.Equal(7, series.Points.Count)));
        Assert.Equal("Pupil Aberration", connector.CanonicalAnalysisKey("光瞳像差"));
        Assert.Equal("Full Field Aberration", connector.CanonicalAnalysisKey("全视场像差"));
        var fullFieldAberrationParameters = connector.GetAnalysisParameters("全视场像差");
        Assert.Equal(
            new[]
            {
                "FieldShape", "XFieldWidth", "YFieldWidth", "MaximumTerm",
                "Aberration", "FieldNumber", "WavelengthNumber", "XFieldSamples", "YFieldSamples",
                "PupilSampling", "DisplayAs", "DisplayMode"
            },
            fullFieldAberrationParameters.Select(parameter => parameter.Key));
        var fullFieldAberrationView = connector.BuildAnalysisView(
            "全视场像差",
            new Dictionary<string, string>
            {
                ["XFieldSamples"] = "5",
                ["YFieldSamples"] = "5",
                ["PupilSampling"] = "8 x 8",
                ["MaximumTerm"] = "9"
            });
        var fullFieldAberrationSeries = Assert.Single(fullFieldAberrationView.SeriesList);
        Assert.All(fullFieldAberrationSeries.Points, point => Assert.True(point.Value.HasValue));
        Assert.StartsWith("X视场，单位：", fullFieldAberrationSeries.XAxisLabel);
        Assert.StartsWith("Y视场，单位：", fullFieldAberrationSeries.YAxisLabel);
        Assert.Equal("Field Curvature and Distortion", connector.CanonicalAnalysisKey("场曲/畸变"));
        Assert.Equal("Field Curvature and Distortion", connector.CanonicalAnalysisKey("畸变"));
        Assert.Equal("Field Curvature and Distortion", connector.CanonicalAnalysisKey("Distortion"));
        var fieldCurvatureAndDistortionParameters = connector.GetAnalysisParameters("场曲/畸变");
        Assert.Contains(fieldCurvatureAndDistortionParameters, parameter => parameter.Key == "MaximumCurvature");
        Assert.Contains(fieldCurvatureAndDistortionParameters, parameter => parameter.Key == "MaximumDistortion");
        Assert.Contains(
            fieldCurvatureAndDistortionParameters,
            parameter => parameter.Key == "DistortionType"
                && parameter.Choices is not null
                && parameter.Choices.Contains("Calibrated F-Theta")
                && parameter.Choices.Contains("Calibrated F-Tan(Theta)")
                && parameter.Choices.Contains("SMIA-TV"));
        var fieldCurvatureAndDistortionView = connector.BuildAnalysisView(
            "场曲/畸变",
            new Dictionary<string, string>
            {
                ["DistortionType"] = "SMIA-TV"
            });
        Assert.Equal("场曲/畸变", fieldCurvatureAndDistortionView.Name);
        Assert.Equal(2, fieldCurvatureAndDistortionView.PlotPanes.Count);
        Assert.Equal(new[] { "Field Curvature", "Distortion" }, fieldCurvatureAndDistortionView.PlotPanes.Select(pane => pane.Title));
        Assert.Contains(fieldCurvatureAndDistortionView.Rows, row => row.Metric == "畸变.SMIA-TV 畸变 (%)");
        Assert.Equal("Axial Aberration", connector.CanonicalAnalysisKey("轴向像差"));
        Assert.Equal("Axial Aberration", connector.CanonicalAnalysisKey("轴向色差"));
        var axialParameters = connector.GetAnalysisParameters("轴向像差");
        Assert.Equal(
            new[] { "GraphScale", "WavelengthNumber", "UseDashes" },
            axialParameters.Select(parameter => parameter.Key));
        Assert.Equal(
            "所有",
            axialParameters.Single(parameter => parameter.Key == "WavelengthNumber").DefaultValue);
        var axialView = connector.BuildAnalysisView("轴向像差");
        Assert.Equal(optic.Wavelengths.Count, axialView.SeriesList.Count);
        Assert.All(axialView.SeriesList, series =>
        {
            Assert.Equal("毫米", series.XAxisLabel);
            Assert.Equal("归一化光瞳坐标", series.YAxisLabel);
        });
        Assert.Equal("Lateral Color", connector.CanonicalAnalysisKey("垂轴色差"));
        var lateralColorParameters = connector.GetAnalysisParameters("垂轴色差");
        Assert.Equal(
            new[] { "GraphScale", "AllWavelengths", "UseRealRays", "ShowAiryDisk" },
            lateralColorParameters.Select(parameter => parameter.Key));
        var lateralColorView = connector.BuildAnalysisView("垂轴色差");
        Assert.Contains(lateralColorView.SeriesList, series => series.Name == "最短的-最长的");
        Assert.Contains(lateralColorView.SeriesList, series => series.Name == "艾里斑");
        Assert.Equal("µm", lateralColorView.SeriesList[0].XAxisLabel);
        Assert.Equal("Color Focus Shift", connector.CanonicalAnalysisKey("色焦移"));
        var colorFocusParameters = connector.GetAnalysisParameters("色焦移");
        Assert.Equal(
            new[] { "MaximumShift", "PupilZone" },
            colorFocusParameters.Select(parameter => parameter.Key));
        var colorFocusView = connector.BuildAnalysisView("色焦移");
        Assert.Single(colorFocusView.SeriesList);
        Assert.Equal("焦移：µm", colorFocusView.SeriesList[0].XAxisLabel);
        Assert.Equal("波长：µm", colorFocusView.SeriesList[0].YAxisLabel);
        Assert.False(colorFocusView.PlotOptions.DefaultSquareViewport);
        Assert.Equal("Seidel Coefficients", connector.CanonicalAnalysisKey("赛德尔系数"));
        var seidelParameters = connector.GetAnalysisParameters("赛德尔系数");
        var seidelWavelength = Assert.Single(seidelParameters);
        Assert.Equal("WavelengthNumber", seidelWavelength.Key);
        var seidelView = connector.BuildAnalysisView("赛德尔系数");
        Assert.Equal(
            new[] { "表面", "SPHA S1", "COMA S2", "ASTI S3", "FCUR S4", "DIST S5", "CLA (CL)", "CTR (CT)" },
            seidelView.Table?.Columns);
        Assert.Contains(seidelView.Table!.Rows, row => row[0] == "累计");
        Assert.Contains("赛德尔像差系数", seidelView.ReportText);
        Assert.Equal("Seidel Diagram", connector.CanonicalAnalysisKey("赛德尔图"));
        var seidelDiagramParameters = connector.GetAnalysisParameters("赛德尔图");
        Assert.Equal(
            new[] { "WavelengthNumber", "MaximumAberration", "GridInterval" },
            seidelDiagramParameters.Select(parameter => parameter.Key));
        var seidelDiagramView = connector.BuildAnalysisView("赛德尔图");
        Assert.Equal(7, seidelDiagramView.SeriesList.Count);
        Assert.Equal("总和", seidelDiagramView.Table?.Rows[^1][0]);
        Assert.Equal(
            new[]
            {
                "傅里叶 MTF",
                "傅里叶离焦 MTF",
                "傅里叶 MTF VS 视场",
                "惠更斯 MTF",
                "惠更斯离焦 MTF",
                "惠更斯 MTF VS 视场",
                "几何 MTF",
                "几何离焦 MTF",
                "几何 MTF VS 视场"
            },
            MainWindow.AnalysisRibbonCommandsByMenu["MTF 曲线"]);
        Assert.Equal(
            new[]
            {
                "图像模拟",
                "几何图像分析",
                "几何位图图像分析",
                "光源分析",
                "部分相干图像分析",
                "扩展图像分析",
                "相对照度",
                "IMA和BIM图片浏览器",
                "位图文件查看器"
            },
            MainWindow.AnalysisRibbonCommandsByMenu["扩展图像分析"]);
        Assert.Contains("报告", MainWindow.AnalysisRibbonCategories);
        var surfaceReport = connector.BuildAnalysisView("表面数据报告");
        Assert.Equal(
            new[] { "面", "标签", "类型", "曲率半径", "厚度", "材料", "半口径", "圆锥系数", "光阑", "镀膜" },
            surfaceReport.Table?.Columns);
        Assert.Equal(optic.SurfaceGroup.Items.Count, surfaceReport.Table?.Rows.Count);
        var systemReport = connector.BuildAnalysisView("系统数据报告");
        Assert.Equal(new[] { "分类", "项目", "值" }, systemReport.Table?.Columns);
        Assert.Contains(systemReport.Table!.Rows, row => row[1] == "有效焦距 (mm)");
        var classifiedReport = connector.BuildAnalysisView("分类数据报告");
        Assert.Equal(new[] { "分类", "项目", "数量", "表面序号" }, classifiedReport.Table?.Columns);
        Assert.Contains(classifiedReport.Table!.Rows, row => row[0] == "材料");
        Assert.Equal("First Order", connector.CanonicalAnalysisKey("一级像差/一阶量"));
        Assert.Equal("Prescription Report", connector.CanonicalAnalysisKey("处方报告"));
        Assert.NotEmpty(connector.BuildAnalysisView("全视场点列图").SeriesList);
        Assert.NotEmpty(connector.BuildAnalysisView("矩阵点列图").PlotPanes);
        Assert.NotEmpty(connector.BuildAnalysisView("结构矩阵点列图").PlotPanes);
        var cardinalView = connector.BuildAnalysisView("基面数据");
        Assert.Equal(new[] { "基面量", "物空间", "像空间" }, cardinalView.Table?.Columns);
        Assert.Equal(6, cardinalView.Table?.Rows.Count);
        Assert.All(cardinalView.Table!.Rows, row =>
        {
            Assert.True(double.TryParse(row[1], CultureInfo.InvariantCulture, out _));
            Assert.True(double.TryParse(row[2], CultureInfo.InvariantCulture, out _));
        });
        var cardinalParameters = connector.GetAnalysisParameters("Cardinal Points Data");
        var referenceSurface = Assert.Single(cardinalParameters);
        Assert.Equal("ReferenceSurfaceNumber", referenceSurface.Key);
        Assert.Equal(
            optic.SurfaceGroup.Items[^1].Number.ToString(CultureInfo.InvariantCulture),
            referenceSurface.DefaultValue);
        var selectedReference = optic.SurfaceGroup.Items[1].Number;
        var referencedCardinalView = connector.BuildAnalysisView(
            "Cardinal Points Data",
            new Dictionary<string, string>
            {
                ["ReferenceSurfaceNumber"] = selectedReference.ToString(CultureInfo.InvariantCulture)
            });
        Assert.Contains(referencedCardinalView.Rows, row =>
            row.Metric == "参考面"
            && row.Value == selectedReference.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(2, connector.BuildAnalysisView("渐晕图").SeriesList.Count);

        var descriptors = connector.GetAnalysisParameters("点扩散函数 PSF");
        Assert.Equal(
            new[]
            {
                "Sampling",
                "Display",
                "Rotation",
                "ImageDeltaMicrometers",
                "UsePolarization",
                "WavelengthNumber",
                "FieldNumber",
                "Type",
                "DisplayAs",
                "SurfaceNumber",
                "Normalized"
            },
            descriptors.Select(item => item.Key));
        Assert.Equal("64 x 64", descriptors.Single(item => item.Key == "Sampling").DefaultValue);
        Assert.Equal("128 x 128", descriptors.Single(item => item.Key == "Display").DefaultValue);
        Assert.Equal("所有", descriptors.Single(item => item.Key == "WavelengthNumber").DefaultValue);
        var fftPsfDisplay = descriptors.Single(item => item.Key == "DisplayAs");
        Assert.Equal("伪彩色", fftPsfDisplay.DefaultValue);
        Assert.Equal(new[] { "伪彩色", "等高线", "表面" }, fftPsfDisplay.Choices);
        Assert.Equal("PSF", connector.CanonicalAnalysisKey("点扩散函数 PSF"));
        Assert.Equal("PSF", connector.CanonicalAnalysisKey("FFT PSF"));
        Assert.Equal("FFT PSF Cross Section", connector.CanonicalAnalysisKey("FFT PSF截面图"));
        Assert.Equal("FFT Line Edge Spread", connector.CanonicalAnalysisKey("FFT 线/边缘扩散"));
        Assert.Equal("Huygens PSF", connector.CanonicalAnalysisKey("惠更斯PSF"));
        Assert.Equal(
            "Huygens PSF Cross Section",
            connector.CanonicalAnalysisKey("惠更斯PSF截面图"));

        var huygensPsfDescriptors = connector.GetAnalysisParameters("惠更斯PSF");
        Assert.Equal(
            new[]
            {
                "PupilSampling",
                "ImageSampling",
                "ImageDeltaMicrometers",
                "Rotation",
                "UsePolarization",
                "UseCentroid",
                "WavelengthNumber",
                "FieldNumber",
                "Type",
                "DisplayAs",
                "Normalized"
            },
            huygensPsfDescriptors.Select(item => item.Key));
        Assert.Equal("32 x 32", huygensPsfDescriptors[0].DefaultValue);
        Assert.Equal("32 x 32", huygensPsfDescriptors[1].DefaultValue);
        Assert.Equal("所有", huygensPsfDescriptors[6].DefaultValue);
        Assert.Equal("伪彩色", huygensPsfDescriptors[9].DefaultValue);
        Assert.Equal(new[] { "伪彩色", "等高线", "表面" }, huygensPsfDescriptors[9].Choices);

        var huygensPsf = connector.BuildAnalysisView(
            "惠更斯PSF",
            new Dictionary<string, string>
            {
                ["PupilSampling"] = "16 x 16",
                ["ImageSampling"] = "16 x 16",
                ["ImageDeltaMicrometers"] = "0",
                ["WavelengthNumber"] = "所有",
                ["FieldNumber"] = "1 - 轴上视场",
                ["Type"] = "线性",
                ["DisplayAs"] = "表面",
                ["Normalized"] = "false",
                ["UseCentroid"] = "false"
            });
        Assert.Equal("惠更斯PSF", huygensPsf.Name);
        Assert.Equal("复色光惠更斯PSF", huygensPsf.PlotOptions.Title);
        var huygensSurface = Assert.Single(huygensPsf.SeriesList);
        Assert.Equal(AnalysisSeriesKind.Heatmap, huygensSurface.Kind);
        Assert.Equal(16 * 16, huygensSurface.Points.Count);
        Assert.All(huygensSurface.Points, point => Assert.InRange(point.Value ?? 0, 0, 1));

        var fftCrossSectionDescriptors = connector.GetAnalysisParameters("FFT PSF截面图");
        Assert.Equal(
            new[]
            {
                "Sampling",
                "Row",
                "GraphScaleMicrometers",
                "UsePolarization",
                "WavelengthNumber",
                "FieldNumber",
                "Type",
                "Normalized"
            },
            fftCrossSectionDescriptors.Select(item => item.Key));
        Assert.Equal("64 x 64", fftCrossSectionDescriptors[0].DefaultValue);
        Assert.Equal("中心", fftCrossSectionDescriptors[1].DefaultValue);
        Assert.Equal("X-线性", fftCrossSectionDescriptors[6].DefaultValue);

        var fftCrossSection = connector.BuildAnalysisView(
            "FFT PSF截面图",
            new Dictionary<string, string>
            {
                ["Sampling"] = "32 x 32",
                ["WavelengthNumber"] = "所有",
                ["FieldNumber"] = "1 - 轴上视场",
                ["Type"] = "X-线性",
                ["Normalized"] = "false"
            });
        var fftProfile = Assert.Single(fftCrossSection.SeriesList);
        Assert.Equal(AnalysisSeriesKind.Line, fftProfile.Kind);
        Assert.Equal("X 截面", fftProfile.Name);
        Assert.Equal("相对辐射照度", fftProfile.YAxisLabel);
        Assert.NotEmpty(fftProfile.Points);
        Assert.InRange(fftProfile.Points.Max(point => point.Y), 0, 1);
        Assert.Equal("PSF截面图", fftCrossSection.PlotOptions.Title);

        var spreadDescriptors = connector.GetAnalysisParameters("FFT 线/边缘扩散");
        Assert.Equal(
            new[]
            {
                "Sampling",
                "Spread",
                "GraphScaleMicrometers",
                "UsePolarization",
                "WavelengthNumber",
                "FieldNumber",
                "Type",
                "UseCoherentPsf"
            },
            spreadDescriptors.Select(item => item.Key));
        Assert.Equal("线", spreadDescriptors[1].DefaultValue);
        Assert.Equal("X-线性", spreadDescriptors[6].DefaultValue);

        var lineSpread = connector.BuildAnalysisView(
            "FFT 线/边缘扩散",
            new Dictionary<string, string>
            {
                ["Sampling"] = "32 x 32",
                ["Spread"] = "线",
                ["WavelengthNumber"] = "所有",
                ["FieldNumber"] = "1 - 轴上视场",
                ["Type"] = "X-线性"
            });
        var line = Assert.Single(lineSpread.SeriesList);
        Assert.Equal("线扩散函数", line.Name);
        Assert.Equal("Y-位置 µm", line.XAxisLabel);
        Assert.Equal("相对辐射照度", line.YAxisLabel);
        Assert.Equal("FFT 线扩散函数", lineSpread.PlotOptions.Title);
        Assert.InRange(line.Points.Max(point => point.Y), 0.999999, 1.000001);

        var edgeSpread = connector.BuildAnalysisView(
            "FFT 线/边缘扩散",
            new Dictionary<string, string>
            {
                ["Sampling"] = "32 x 32",
                ["Spread"] = "边缘",
                ["WavelengthNumber"] = "所有",
                ["FieldNumber"] = "1 - 轴上视场",
                ["Type"] = "X-线性"
            });
        var edge = Assert.Single(edgeSpread.SeriesList).Points;
        Assert.True(edge.Zip(edge.Skip(1), (left, right) => right.Y >= left.Y).All(value => value));
        Assert.Equal("FFT 边缘扩散函数", edgeSpread.PlotOptions.Title);

        var huygensCrossSection = connector.BuildAnalysisView(
            "惠更斯PSF截面图",
            new Dictionary<string, string>
            {
                ["NumRays"] = "3",
                ["ImageSize"] = "8",
                ["PixelPitchMillimeters"] = "0.005"
            });
        Assert.Equal(2, huygensCrossSection.SeriesList.Count);
        Assert.All(huygensCrossSection.SeriesList, profile => Assert.NotEmpty(profile.Points));

        foreach (var analysisName in new[] { "傅里叶 MTF", "惠更斯 MTF", "几何 MTF" })
        {
            var mtfDescriptors = connector.GetAnalysisParameters(analysisName);
            Assert.Contains(mtfDescriptors, item =>
                item.Key == "MaximumFrequency"
                && item.Kind == AnalysisParameterKind.Double
                && item.Minimum == 0
                && item.Maximum == 10000);
        }

        var fftMtfDescriptors = connector.GetAnalysisParameters("傅里叶 MTF");
        Assert.Equal(
            new[]
            {
                "Sampling",
                "MaximumFrequency",
                "WavelengthNumber",
                "FieldNumber",
                "SurfaceNumber",
                "Type",
                "ShowDiffractionLimit",
                "UsePolarization",
                "UseDashes"
            },
            fftMtfDescriptors.Select(item => item.Key));
        Assert.Equal(
            new[] { "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192", "16384" },
            fftMtfDescriptors.Single(item => item.Key == "Sampling").Choices);
        Assert.Equal("0", fftMtfDescriptors.Single(item => item.Key == "MaximumFrequency").DefaultValue);
        Assert.Equal("0", fftMtfDescriptors.Single(item => item.Key == "SurfaceNumber").DefaultValue);
        Assert.Equal(
            new[] { "调制", "实部", "虚部", "相位", "方波" },
            fftMtfDescriptors.Single(item => item.Key == "Type").Choices);

        var fftThroughFocusDescriptors = connector.GetAnalysisParameters("傅里叶离焦 MTF");
        Assert.Equal(
            new[]
            {
                "Sampling",
                "DeltaFocus",
                "Frequency",
                "NumberOfSteps",
                "WavelengthNumber",
                "FieldNumber",
                "Type",
                "UsePolarization",
                "UseDashes"
            },
            fftThroughFocusDescriptors.Select(item => item.Key));
        Assert.Equal(
            new[] { "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192", "16384" },
            fftThroughFocusDescriptors.Single(item => item.Key == "Sampling").Choices);
        Assert.Equal("0", fftThroughFocusDescriptors.Single(item => item.Key == "Frequency").DefaultValue);
        Assert.Equal("5", fftThroughFocusDescriptors.Single(item => item.Key == "NumberOfSteps").DefaultValue);
        Assert.Equal(
            new[] { "调制", "实部", "虚部", "相位", "方波" },
            fftThroughFocusDescriptors.Single(item => item.Key == "Type").Choices);

        foreach (var (analysisName, defaultFrequency) in new[]
        {
            ("惠更斯离焦 MTF", "20"),
            ("几何离焦 MTF", "50")
        })
        {
            var throughFocusDescriptors = connector.GetAnalysisParameters(analysisName);
            Assert.Contains(throughFocusDescriptors, item => item.Key == "DeltaFocus" && item.DefaultValue == "0.1");
            Assert.Contains(throughFocusDescriptors, item => item.Key == "Steps" && item.DefaultValue == "5");
            Assert.Contains(throughFocusDescriptors, item => item.Key == "SpatialFrequency" && item.DefaultValue == defaultFrequency);
            Assert.Contains(throughFocusDescriptors, item => item.Key == "WavelengthNumber" && item.DefaultValue == "0");
            Assert.Contains(throughFocusDescriptors, item => item.Key == "FieldNumber" && item.DefaultValue == "0");
            Assert.DoesNotContain(throughFocusDescriptors, item => item.Key is "FocusStep" or "FocusPlaneCount");
        }

        var settings = connector.MergeAnalysisSettings("点扩散函数 PSF", new Dictionary<string, string>
        {
            ["Sampling"] = "32 x 32",
            ["Display"] = "64 x 64",
            ["WavelengthNumber"] = "所有",
            ["FieldNumber"] = "1 - 轴上视场",
            ["Ignored"] = "not persisted"
        });

        Assert.Equal("32 x 32", settings["Sampling"]);
        Assert.Equal("64 x 64", settings["Display"]);
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

        Assert.Equal("FFT PSF", view.Name);
        AssertRow(view, "方法", "FFT");
        AssertRow(view, "瞳面采样数", "32");
        AssertRow(view, "网格尺寸", "64");
        AssertRow(view, "波长序号", "0");
        AssertRow(view, "显示为", "伪彩色");
        Assert.Equal("复色光FFT PSF", view.PlotOptions.Title);
        var series = Assert.Single(view.SeriesList);
        Assert.Equal(AnalysisSeriesKind.Heatmap, series.Kind);
        Assert.NotEmpty(series.Points);
        Assert.All(series.Points, point => Assert.InRange(point.Value ?? 0, 0, 1));
    }

    [Fact]
    public void AppSettingsRoundTripsAnalysisSettings()
    {
        var settings = new AppSettings();
        settings.AnalysisSettings["PSF"] = new Dictionary<string, string>
        {
            ["Sampling"] = "64 x 64",
            ["Display"] = "128 x 128"
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal("64 x 64", restored.AnalysisSettings["PSF"]["Sampling"]);
        Assert.Equal("128 x 128", restored.AnalysisSettings["PSF"]["Display"]);
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
        var parameters = connector.GetAnalysisParameters("Image Simulation");
        var sourceMode = Assert.Single(parameters, parameter => parameter.Key == "SourceMode");
        var sourceFile = Assert.Single(parameters, parameter => parameter.Key == "SourceFile");
        var aberrationMode = Assert.Single(parameters, parameter => parameter.Key == "AberrationMode");
        var wavelength = Assert.Single(parameters, parameter => parameter.Key == "WavelengthNumber");
        var field = Assert.Single(parameters, parameter => parameter.Key == "FieldNumber");
        Assert.Equal(2, sourceMode.Choices?.Count);
        Assert.Equal("File", sourceFile.Kind.ToString());
        Assert.Equal("几何的", aberrationMode.DefaultValue);
        Assert.Equal(new[] { "衍射", "几何的", "无" }, aberrationMode.Choices);
        Assert.Equal("RGB", wavelength.DefaultValue);
        Assert.StartsWith("1 - ", field.DefaultValue);
        Assert.Contains(parameters, parameter => parameter.Key == "FieldHeight");
        Assert.Contains(parameters, parameter => parameter.Key == "Oversampling");
        Assert.Contains(parameters, parameter => parameter.Key == "SourceFlip");
        Assert.Contains(parameters, parameter => parameter.Key == "SourceRotation");
        Assert.Contains(parameters, parameter => parameter.Key == "GuardBand");
        Assert.Contains(parameters, parameter => parameter.Key == "RelativeIllumination");
        Assert.Contains(parameters, parameter => parameter.Key == "UsePolarization");
        Assert.Contains(parameters, parameter => parameter.Key == "ApplyFixedApertures");
        Assert.Contains(parameters, parameter => parameter.Key == "DisplayAs");
        Assert.Contains(parameters, parameter => parameter.Key == "Reference");
        Assert.Contains(parameters, parameter => parameter.Key == "ImageFlip");
        Assert.Contains(parameters, parameter => parameter.Key == "PixelSize");
        Assert.Contains(parameters, parameter => parameter.Key == "DetectorXPixels");
        Assert.Contains(parameters, parameter => parameter.Key == "DetectorYPixels");
        Assert.Contains(parameters, parameter => parameter.Key == "CompressFrame");
        Assert.Contains(parameters, parameter => parameter.Key == "OutputFile");

        var outputFile = Path.Combine(
            Path.GetTempPath(),
            $"optiland-image-simulation-{Guid.NewGuid():N}.png");
        try
        {
            var view = connector.BuildAnalysisView(
                "Image Simulation",
                new Dictionary<string, string>
                {
                    ["Oversampling"] = "2 x",
                    ["GuardBand"] = "8",
                    ["FieldHeight"] = "1.5",
                    ["RelativeIllumination"] = "false",
                    ["AberrationMode"] = "几何的",
                    ["PsfSize"] = "16 x 16",
                    ["NumRays"] = "8 x 8",
                    ["SourceFlip"] = "水平",
                    ["SourceRotation"] = "90°",
                    ["ImageFlip"] = "垂直",
                    ["DisplayAs"] = "源位图",
                    ["DetectorXPixels"] = "20",
                    ["DetectorYPixels"] = "18",
                    ["PixelSize"] = "0.01",
                    ["OutputFile"] = outputFile
                });
            Assert.Contains(view.Rows, row => row.Metric == "过采样" && row.Value == "2");
            Assert.Contains(view.Rows, row => row.Metric == "保护带" && row.Value == "8");
            Assert.Contains(view.Rows, row => row.Metric == "像差模式" && row.Value == "Geometric");
            Assert.Contains(view.Rows, row => row.Metric == "旋转位图 (°)" && row.Value == "90");
            Assert.Contains(view.Rows, row => row.Metric == "输出形状" && row.Value == "(1, 3, 18, 20)");
            var pane = Assert.Single(view.PlotPanes);
            Assert.Equal("Source Bitmap", pane.Title);
            using var exported = SkiaSharp.SKBitmap.Decode(outputFile);
            Assert.NotNull(exported);
            Assert.Equal(20, exported.Width);
            Assert.Equal(18, exported.Height);
        }
        finally
        {
            File.Delete(outputFile);
        }
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
