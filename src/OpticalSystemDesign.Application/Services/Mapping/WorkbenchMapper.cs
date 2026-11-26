using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Phase;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Visualization;
using ContractAnalysisAxisQuantity = OpticalSystemDesign.Application.Contracts.AnalysisAxisQuantity;
using ContractAnalysisAxisUnit = OpticalSystemDesign.Application.Contracts.AnalysisAxisUnit;
using ContractAnalysisColorMap = OpticalSystemDesign.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OpticalSystemDesign.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OpticalSystemDesign.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OpticalSystemDesign.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OpticalSystemDesign.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OpticalSystemDesign.Application.Contracts.AnalysisSeriesKind;

namespace OpticalSystemDesign.Application.Services;

internal static partial class WorkbenchMapper
{
    internal static T? ElementAtOrDefault<T>(IList<T> items, int index) where T : class
    {
        return index >= 0 && index < items.Count ? items[index] : null;
    }

    internal static SurfaceRowDto ToSurfaceDto(OpticalSurface surface)
    {
        var grating = surface.Geometry as IGratingGeometry;
        var thinLens = surface.InteractionModel as ThinLensInteractionModel;
        var geometry = ComponentSnapshotFactory.FromGeometry(surface.Geometry);
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
                surface.CoordinateSystem.RotationZDegrees),
            MechanicalSemiDiameter: surface.MechanicalSemiDiameter,
            ThermalExpansionPpmPerC: surface.ThermalExpansionPpmPerC,
            ChipZone: surface.ChipZone,
            GeometryDefinition: new SurfaceGeometryDefinitionDto(geometry.Kind, geometry.Numbers),
            DrawingAperture: new SurfaceDrawingApertureDto(surface.PhysicalAperture?.Kind ?? "none",
                (surface.PhysicalAperture as CircularAperture)?.Radius),
            IsCoordinateReference: surface.InteractionModel is CoordinateReferenceInteractionModel,
            IsReflective: surface.IsReflective,
            SurfaceColor: surface.SurfaceColor,
            Drawing: ToDrawingDto(surface.Drawing),
            ShapeVariables: surface.ShapeVariables.ToArray(),
            ParaxialLens: surface.InteractionModel is ParaxialLensInteractionModel paraxial
                ? new(paraxial.PowerX, paraxial.PowerY, paraxial.OpdMode) : null,
            Birefringent: surface.InteractionModel is BirefringentInteractionModel crystalInterface
                ? surface.MaterialAfter is UniaxialMaterial crystal && !crystalInterface.IsExit
                    ? new(false, crystalInterface.AxisDisplayLength, crystal.Axis.X, crystal.Axis.Y, crystal.Axis.Z,
                        crystal.TraceExtraordinary, crystal.ParaxialIgnore)
                    : new(crystalInterface.IsExit, crystalInterface.AxisDisplayLength)
                : null,
            DiffractiveSurfaceType: DiffractiveSurfaceType(surface),
            GradientIndexSurfaceType: GradientIndexSurfaceType(surface));
    }

    internal static string? DiffractiveSurfaceType(OpticalSurface surface) => (surface.Geometry, surface.InteractionModel) switch
    {
        (EvenAsphereGeometry, DiffractivePhaseInteractionModel { Profile: NormalizedRadialPhaseProfile }) => "二元面 2",
        (ConcentricZoneGeometry, DiffractivePhaseInteractionModel { Profile: DualZoneRadialPhaseProfile }) => "二元面 3",
        (PlaneGeometry, DiffractivePhaseInteractionModel { Profile: TwoPointHologramPhaseProfile }) => "全息面 2",
        (PlaneGeometry or StandardGeometry, RuledGratingInteractionModel) => "衍射光栅",
        _ => null
    };

    internal static string? GradientIndexSurfaceType(OpticalSurface surface) =>
        !surface.IsReflective && surface.InteractionModel is RefractiveReflectiveInteractionModel
        && surface.MaterialAfter is GradientIndexMaterial gradient ? gradient.Profile switch
        {
            Gradient1IndexProfile => "梯度折射率面 1",
            Gradient2IndexProfile => "梯度折射率面 2",
            Gradient3IndexProfile => "梯度折射率面 3",
            Gradient4IndexProfile => "梯度折射率面 4",
            Gradient5IndexProfile => "梯度折射率面 5",
            Gradient9IndexProfile => "梯度折射率面 9",
            GridGradientIndexProfile => "网格梯度折射率面",
            _ => null
        } : null;

    internal static string GeometryKind(OpticalSurface surface) => surface.Geometry switch
    {
        PlaneGeometry => "平面",
        TiltedPlaneGeometry => "倾斜面",
        IrregularGeometry => "不规则面",
        GridSagGeometry => "网格矢高面",
        FringeSagGeometry => "Zernike Fringe 矢高面",
        ChebyshevSagGeometry => "Chebyshev 矢高面",
        TernaryMirrorArrayGeometry => "微镜阵列",
        PolygonGroupGeometry => "非顺序多边形组",
        ForbesNormalSlopeGeometry => "Forbes 法向斜率面",
        StandardGeometry => "标准球面/圆锥",
        PlaneGratingGeometry => "平面光栅",
        StandardGratingGeometry => "标准曲面光栅",
        EvenAsphereGeometry => "偶次非球面",
        OddAsphereGeometry => "奇次非球面",
        NormalizedPolynomialGeometry polynomial => polynomial.Basis switch
        {
            SagPolynomialBasis.EvenRadial => "扩展偶次非球面",
            SagPolynomialBasis.Radial => "扩展奇次非球面",
            _ => "扩展 XY 多项式"
        },
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
        CoordinateReferenceInteractionModel { IsIgnoredSurface: true } => "忽略面",
        CoordinateReferenceInteractionModel => "坐标间断",
        BirefringentInteractionModel crystal => crystal.IsExit ? "双折射输出" : "双折射输入",
        ParaxialLensInteractionModel => "近轴透镜",
        ThinLensInteractionModel model when model.IsReflective => "反射薄透镜",
        ThinLensInteractionModel => "薄透镜",
        DiffractiveInteractionModel model when model.IsReflective => "反射衍射",
        DiffractiveInteractionModel => "衍射",
        DiffractivePhaseInteractionModel model => model.IsReflective ? "反射衍射" : "衍射",
        RuledGratingInteractionModel model => model.IsReflective ? "反射衍射" : "衍射",
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
                pane.Footer,
                pane.MatrixCell is null ? null : new AnalysisMatrixCellDto(
                    pane.MatrixCell.ConfigurationNumber, pane.MatrixCell.FieldNumber, pane.MatrixCell.FieldLabel))).ToArray(),
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
            OutcomeReason: view.OutcomeReason,
            ReportSections: view.ReportSections?.Select(section => new AnalysisReportSectionDto(section.Title, section.Label, section.Unit,
                new AnalysisTableDto(section.Table.Columns, section.Table.Rows, section.Table.RowGroups))).ToArray())
        {
            FullFieldSpotSummary = view.FullFieldSpotSummary,
            ColorFocusShiftSummary = view.ColorFocusShiftSummary,
            FoucaultSummary = view.FoucaultSummary,
            PsfImageSummary = view.PsfImageSummary,
            DiffractionEnergySummary = view.DiffractionEnergySummary,
            TelecentricitySummary = view.TelecentricitySummary
        };
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
        quantity == Core.Analysis.AnalysisAxisQuantity.FieldNumber ? ContractAnalysisAxisQuantity.FieldNumber :
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
            DefaultSquareViewport: options.DefaultSquareViewport,
            SpatialDisplayScaleX: options.SpatialDisplayScaleX,
            SpatialDisplayScaleY: options.SpatialDisplayScaleY,
            LogarithmicIntensity: options.LogarithmicIntensity,
            SpatialGeometry: options.SpatialGeometry?.Select(p => new AnalysisSpatialPointDto(
                p.SourceX, p.SourceY, p.X, p.Y, p.Z, p.PhysicalX, p.PhysicalY, p.PhysicalZ,
                p.FitResidualMillimeters)).ToArray());
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
                surface.IsStandaloneStop,
                SurfaceColor: surface.SurfaceColor, Opacity: surface.Opacity, HideEdges: surface.HideEdges,
                HideSurface: surface.HideSurface,
                LocalFrame: surface.LocalFrame is not { } frame ? null : new SceneLocalFrame2Dto(
                    new(frame.Origin.Z, frame.Origin.Y), new(frame.XAxisEnd.Z, frame.XAxisEnd.Y),
                    new(frame.YAxisEnd.Z, frame.YAxisEnd.Y), new(frame.ZAxisEnd.Z, frame.ZAxisEnd.Y), frame.DrawAxes, frame.DrawOrigin),
                Contours: surface.Contours?.Select(contour => (IReadOnlyList<ScenePoint2Dto>)contour.Select(Point).ToArray()).ToArray())).ToArray(),
            scene.LensElements.Select(element => new SceneLensElement2Dto(
                element.FrontSurfaceNumber,
                element.BackSurfaceNumber,
                element.Material,
                element.Boundary.Select(Point).ToArray(), Opacity: element.Opacity)).ToArray(),
            scene.LensEdges.Select(edge => new SceneLensEdge2Dto(
                edge.FrontSurfaceNumber,
                edge.BackSurfaceNumber,
                Point(edge.Start),
                Point(edge.End), Opacity: edge.Opacity)).ToArray(),
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
                    segment.TargetSurfaceNumber, segment.SourceFaceIndex, segment.TargetFaceIndex)).ToArray())).ToArray(),
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
                 surface.IsStandaloneStop,
                 IsImageSurface: surface.IsImageSurface,
                 SurfaceColor: surface.SurfaceColor, Opacity: surface.Opacity, HideEdges: surface.HideEdges,
                 HideSurface: surface.HideSurface,
                 LocalFrame: surface.LocalFrame is not { } frame ? null : new SceneLocalFrame3Dto(
                     Point(frame.Origin), Point(frame.XAxisEnd), Point(frame.YAxisEnd), Point(frame.ZAxisEnd), frame.DrawAxes, frame.DrawOrigin), IsPolygonGroup: surface.IsPolygonGroup)).ToArray(),
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
                element.MeridianBoundary.Select(Point).ToArray(), Opacity: element.Opacity)).ToArray(),
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
                    segment.TargetSurfaceNumber, segment.SourceFaceIndex, segment.TargetFaceIndex)).ToArray())).ToArray(),
            scene.XExtent,
            scene.YExtent,
            scene.ZMin,
            scene.ZMax)
        {
            FitBounds = EncloseScenePoints(scene.Surfaces.SelectMany(s => s.Rim.Concat(s.MeridianX).Concat(s.MeridianY)
                    .Concat(s.Faces.SelectMany(f => f.Points)))
                .Concat(scene.Surfaces.Where(s => s.LocalFrame is not null).SelectMany(s => s.LocalFrame!.DrawAxes
                    ? new[] { s.LocalFrame.Origin, s.LocalFrame.XAxisEnd, s.LocalFrame.YAxisEnd, s.LocalFrame.ZAxisEnd }
                    : new[] { s.LocalFrame.Origin }))
                .Concat(scene.LensElements.SelectMany(e => e.FrontRim.Concat(e.BackRim)
                    .Concat(e.FrontFaces.Concat(e.BackFaces).SelectMany(f => f.Points))))
                .Concat(scene.Rays.SelectMany(r => r.Points)).Select(Point))
        };
    }

    internal static SceneBounds3Dto? EncloseScenePoints(IEnumerable<ScenePoint3Dto> source)
    {
        var points = source.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z)).ToArray();
        return points.Length == 0 ? null : new(new(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
            new(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)));
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
