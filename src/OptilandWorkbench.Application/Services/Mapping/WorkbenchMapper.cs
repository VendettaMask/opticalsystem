using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Visualization;
using ContractAnalysisColorMap = OptilandWorkbench.Application.Contracts.AnalysisColorMap;
using ContractAnalysisAxisQuantity = OptilandWorkbench.Application.Contracts.AnalysisAxisQuantity;
using ContractAnalysisAxisUnit = OptilandWorkbench.Application.Contracts.AnalysisAxisUnit;
using ContractAnalysisLineStyle = OptilandWorkbench.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OptilandWorkbench.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OptilandWorkbench.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OptilandWorkbench.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OptilandWorkbench.Application.Contracts.AnalysisSeriesKind;

namespace OptilandWorkbench.Application.Services;

internal static class WorkbenchMapper
{
    internal static T? ElementAtOrDefault<T>(IList<T> items, int index) where T : class
    {
        return index >= 0 && index < items.Count ? items[index] : null;
    }

    internal static SurfaceRowDto ToSurfaceDto(OpticalSurface surface)
    {
        var grating = surface.Geometry as IGratingGeometry;
        var thinLens = surface.InteractionModel as ThinLensInteractionModel;
        return new SurfaceRowDto(
            surface.Number,
            surface.Label,
            surface.Radius,
            surface.Thickness,
            surface.Material,
            surface.Coating,
            surface.SemiDiameter,
            surface.Conic,
            surface.IsStop,
            GeometryKind(surface),
            CoatingKind(surface),
            InteractionKind(surface),
            PhysicalApertureKind(surface),
            grating?.GratingOrder ?? 1,
            grating?.GratingPeriodMicrometers ?? 1,
            (grating?.GrooveOrientationAngleRadians ?? 0) * 180 / Math.PI,
            thinLens?.FocalLength ?? 50,
            surface.RadiusVariable,
            surface.ThicknessVariable,
            surface.SemiDiameterFixed,
            surface.Geometry is not INonComputableGeometry,
            new SurfaceInspectionDto(
                surface.ScatteringModel?.Kind ?? "none",
                surface.CoordinateSystem.Origin.X,
                surface.CoordinateSystem.Origin.Y,
                surface.CoordinateSystem.Origin.Z,
                surface.CoordinateSystem.RotationXDegrees,
                surface.CoordinateSystem.RotationYDegrees,
                surface.CoordinateSystem.RotationZDegrees));
    }

    internal static string GeometryKind(OpticalSurface surface) => surface.Geometry switch
    {
        PlaneGeometry => "平面",
        StandardGeometry => "标准球面/圆锥",
        PlaneGratingGeometry => "平面光栅",
        StandardGratingGeometry => "标准曲面光栅",
        EvenAsphereGeometry => "偶次非球面",
        OddAsphereGeometry => "奇次非球面",
        BiconicGeometry => "双圆锥",
        ToroidalGeometry => "环形面",
        PolynomialGeometry => "XY 多项式",
        ChebyshevGeometry => "Chebyshev 曲面",
        ZernikeGeometry => "Zernike 曲面",
        ForbesQGeometry => "Forbes Q 曲面",
        INonComputableGeometry opaque => $"不支持：{opaque.OriginalType}",
        _ => $"其他：{surface.Geometry.Kind}"
    };

    internal static string CoatingKind(OpticalSurface surface)
    {
        return surface.CoatingModel is ApproximateTransmissionRippleCoating stack
            ? stack.Layers.Count > 1
                ? "Experimental：交替层透过率起伏近似（非薄膜求解）"
                : "Experimental：单层透过率起伏近似（非薄膜求解）"
            : "无镀膜";
    }

    internal static string InteractionKind(OpticalSurface surface) => surface.InteractionModel switch
    {
        ThinLensInteractionModel model when model.IsReflective => "反射薄透镜",
        ThinLensInteractionModel => "薄透镜",
        DiffractiveInteractionModel model when model.IsReflective => "反射衍射",
        DiffractiveInteractionModel => "衍射",
        PhaseInteractionModel => "相位",
        RefractiveReflectiveInteractionModel model when model.IsReflective => "反射",
        _ => "折射"
    };

    internal static string PhysicalApertureKind(OpticalSurface surface) => surface.PhysicalAperture switch
    {
        AnnularAperture => "环形",
        OffsetRadialAperture => "偏心圆",
        RectangularAperture => "矩形",
        EllipticalAperture => "椭圆",
        FileAperture => "多边形",
        PolygonAperture => "多边形",
        BooleanAperture => "组合孔径",
        null => "无",
        _ => "圆形"
    };

    internal static (string Kind, double First, double Second) ToApodizationSettings(IApodizationModel? apodization)
    {
        return apodization switch
        {
            ZemaxApodization value => (value.Type switch
            {
                ZemaxApodizationType.Uniform => "均匀（Zemax）",
                ZemaxApodizationType.Gaussian => "高斯（Zemax）",
                ZemaxApodizationType.CosineCubed => "余弦立方（Zemax）",
                _ => throw new InvalidOperationException("Unknown Zemax apodization type.")
            }, value.Factor, 1),
            UniformApodization => ("均匀", 1, 1),
            GaussianApodization value => ("高斯", value.Sigma, 1),
            CosineSquaredApodization value => ("余弦平方", value.Radius, 1),
            HannApodization value => ("Hann", value.Diameter, 1),
            PolynomialApodization value => ("多项式", value.Radius, value.Power),
            SuperGaussianApodization value => ("超高斯", value.Width, value.Exponent),
            TukeyApodization value => ("Tukey", value.Radius, value.Alpha),
            _ => ("无", 1, 1)
        };
    }

    internal static AnalysisViewDto ToAnalysisViewDto(AnalysisView view)
    {
        return new AnalysisViewDto(
            view.Name,
            view.Rows.Select(row => new AnalysisRowDto(row.Metric, row.Value)).ToArray(),
            view.ReportText,
            view.SeriesList.Select(ToSeriesDto).ToArray(),
            ToPlotOptionsDto(view.PlotOptions),
            view.PlotPanes.Select(pane => new AnalysisPlotPaneDto(
                pane.Title,
                pane.Series.Select(ToSeriesDto).ToArray(),
                ToPlotOptionsDto(pane.PlotOptions),
                pane.Metrics?.Select(metric => new AnalysisPlotMetricDto(
                    metric.Label,
                    metric.Value,
                    metric.Unit)).ToArray(),
                pane.Footer)).ToArray(),
            view.PlotPaneColumns,
            view.Table is null
                ? null
                : new AnalysisTableDto(
                    view.Table.Columns,
                    view.Table.Rows,
                    view.Table.RowGroups),
            InterferogramSummary: view.InterferogramSummary,
            Outcome: view.Outcome switch
            {
                Core.Analysis.AnalysisOutcome.Success => Contracts.AnalysisOutcome.Success,
                Core.Analysis.AnalysisOutcome.Unavailable => Contracts.AnalysisOutcome.Unavailable,
                Core.Analysis.AnalysisOutcome.NotApplicable => Contracts.AnalysisOutcome.NotApplicable,
                _ => throw new ArgumentOutOfRangeException(nameof(view))
            },
            OutcomeReason: view.OutcomeReason);
    }

    internal static AnalysisSeriesDto ToSeriesDto(AnalysisSeries series)
    {
        return new AnalysisSeriesDto(
            series.XAxisLabel,
            series.YAxisLabel,
            series.Points.Select(point => new AnalysisPointDto(
                point.X,
                point.Y,
                point.Label,
                point.Value,
                point.Red,
                point.Green,
                point.Blue)).ToArray(),
            (ContractAnalysisSeriesKind)(int)series.Kind,
            series.Name,
            (ContractAnalysisLineStyle)(int)series.LineStyle,
            series.ColorIndex,
            series.ShowMarkers,
            series.LineWidth,
            (ContractAnalysisMarkerStyle)(int)series.MarkerStyle,
            series.MarkerSize,
            series.Opacity,
            series.ValueLabel,
            (ContractAnalysisColorMap)(int)series.ColorMap,
            series.ValueMinimum,
            series.ValueMaximum,
            series.LegendKey,
            series.LegendLabel,
            MapAxisQuantity(series.XQuantity),
            (ContractAnalysisAxisUnit)(int)series.XUnit,
            MapAxisQuantity(series.YQuantity),
            (ContractAnalysisAxisUnit)(int)series.YUnit,
            MapAxisQuantity(series.ValueQuantity),
            (ContractAnalysisAxisUnit)(int)series.ValueUnit);
    }

    private static ContractAnalysisAxisQuantity MapAxisQuantity(Core.Analysis.AnalysisAxisQuantity quantity) =>
        quantity == Core.Analysis.AnalysisAxisQuantity.NormalizedField
            ? ContractAnalysisAxisQuantity.NormalizedField
            : (ContractAnalysisAxisQuantity)(int)quantity;

    internal static AnalysisPlotOptionsDto ToPlotOptionsDto(AnalysisPlotOptions options)
    {
        return new AnalysisPlotOptionsDto(
            options.Title,
            options.SymmetricX,
            options.EqualAspect,
            options.ShowVerticalZeroLine,
            options.ShowHorizontalZeroLine,
            (ContractAnalysisLineStyle)(int)options.VerticalZeroLineStyle,
            options.VerticalZeroLineWidth,
            options.XMinimum,
            options.XMaximum,
            options.YMinimum,
            options.YMaximum,
            options.ShowLegend,
            options.HideTopAndRightAxes,
            options.DottedGrid,
            options.GridOpacity,
            options.HideAxes,
            HideTickLabels: options.HideTickLabels,
            LegendBelow: options.LegendBelow,
            DefaultSquareViewport: options.DefaultSquareViewport);
    }

    internal static Scene2Dto ToScene2Dto(Layout2DScene scene)
    {
        ScenePoint2Dto Point(Layout2DPoint point) => new(point.Z, point.Y);
        return new Scene2Dto(
            scene.Surfaces.Select(surface => new SceneSurface2Dto(
                surface.SurfaceNumber,
                surface.Label,
                surface.IsStop,
                surface.IsReferencePlane,
                surface.Points.Select(Point).ToArray(),
                surface.IsStandaloneStop)).ToArray(),
            scene.LensElements.Select(element => new SceneLensElement2Dto(
                element.FrontSurfaceNumber,
                element.BackSurfaceNumber,
                element.Material,
                element.Boundary.Select(Point).ToArray())).ToArray(),
            scene.LensEdges.Select(edge => new SceneLensEdge2Dto(
                edge.FrontSurfaceNumber,
                edge.BackSurfaceNumber,
                Point(edge.Start),
                Point(edge.End))).ToArray(),
            scene.Rays.Select(ray => new SceneRay2Dto(
                ray.RayNumber,
                ray.FieldIndex,
                ray.PupilIndex,
                ray.WavelengthIndex,
                ray.WavelengthNanometers,
                ray.Vignetted,
                ray.FinalIntensity,
                ray.Points.Select(Point).ToArray(),
                ray.Segments.Select(segment => new SceneRaySegment2Dto(
                    Point(segment.Start),
                    Point(segment.End),
                    new SceneRayDirection2Dto(segment.Direction.Z, segment.Direction.Y),
                    ToSceneRaySegmentType(segment.SegmentType),
                    ToSceneRayInteractionType(segment.InteractionType),
                    segment.SourceSurfaceNumber,
                    segment.TargetSurfaceNumber)).ToArray())).ToArray(),
            scene.ZMin,
            scene.ZMax,
            scene.YExtent);
    }

    internal static Scene3Dto ToScene3Dto(Layout3DScene scene)
    {
        ScenePoint3Dto Point(Layout3DPoint point) => new(point.X, point.Y, point.Z);
        return new Scene3Dto(
            scene.Surfaces.Select(surface => new SceneSurface3Dto(
                surface.SurfaceNumber,
                surface.Label,
                surface.IsStop,
                surface.IsReferencePlane,
                 surface.Material,
                 surface.Rim.Select(Point).ToArray(),
                 surface.MeridianY.Select(Point).ToArray(),
                 surface.MeridianX.Select(Point).ToArray(),
                 surface.Faces.Select(face => new SceneSurfaceFace3Dto(
                     face.Points.Select(Point).ToArray())).ToArray(),
                 surface.IsStandaloneStop)).ToArray(),
            scene.LensElements.Select(element => new SceneLensElement3Dto(
                element.FrontSurfaceNumber,
                element.BackSurfaceNumber,
                element.Material,
                element.RefractiveIndex,
                element.FrontRim.Select(Point).ToArray(),
                element.BackRim.Select(Point).ToArray(),
                element.FrontFaces.Select(face => new SceneSurfaceFace3Dto(
                    face.Points.Select(Point).ToArray())).ToArray(),
                element.BackFaces.Select(face => new SceneSurfaceFace3Dto(
                    face.Points.Select(Point).ToArray())).ToArray(),
                element.MeridianBoundary.Select(Point).ToArray())).ToArray(),
            scene.Rays.Select(ray => new SceneRay3Dto(
                ray.RayNumber,
                ray.FieldIndex,
                ray.PupilIndex,
                ray.WavelengthIndex,
                ray.WavelengthNanometers,
                ray.Vignetted,
                ray.FinalIntensity,
                ray.Points.Select(Point).ToArray(),
                ray.Segments.Select(segment => new SceneRaySegment3Dto(
                    Point(segment.Start),
                    Point(segment.End),
                    new SceneRayDirection3Dto(
                        segment.Direction.X,
                        segment.Direction.Y,
                        segment.Direction.Z),
                    ToSceneRaySegmentType(segment.SegmentType),
                    ToSceneRayInteractionType(segment.InteractionType),
                    segment.SourceSurfaceNumber,
                    segment.TargetSurfaceNumber)).ToArray())).ToArray(),
            scene.XExtent,
            scene.YExtent,
            scene.ZMin,
            scene.ZMax);
    }

    private static SceneRaySegmentType ToSceneRaySegmentType(LayoutRaySegmentType type) => type switch
    {
        LayoutRaySegmentType.Incident => SceneRaySegmentType.Incident,
        LayoutRaySegmentType.Transmitted => SceneRaySegmentType.Transmitted,
        LayoutRaySegmentType.Reflected => SceneRaySegmentType.Reflected,
        LayoutRaySegmentType.TotalInternalReflection => SceneRaySegmentType.TotalInternalReflection,
        _ => SceneRaySegmentType.Unspecified
    };

    private static SceneRayInteractionType ToSceneRayInteractionType(
        LayoutRayInteractionType type) => type switch
        {
            LayoutRayInteractionType.Refractive => SceneRayInteractionType.Refractive,
            LayoutRayInteractionType.Reflective => SceneRayInteractionType.Reflective,
            LayoutRayInteractionType.Diffractive => SceneRayInteractionType.Diffractive,
            LayoutRayInteractionType.ThinLens => SceneRayInteractionType.ThinLens,
            LayoutRayInteractionType.Phase => SceneRayInteractionType.Phase,
            _ => SceneRayInteractionType.None
        };
}
