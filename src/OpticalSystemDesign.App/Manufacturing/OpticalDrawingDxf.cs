using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;
using netDxf.Units;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using static OpticalSystemDesign.App.Manufacturing.OpticalDrawingRendererCore;

namespace OpticalSystemDesign.App.Manufacturing;

internal sealed class OpticalDrawingDxf
{
    private readonly DxfDocument document = new(DxfVersion.AutoCad2013) { BuildDimensionBlocks = true };
    private readonly Layer outline = new("OUTLINE");
    private readonly Layer axis = new("AXIS") { Linetype = Linetype.Center };
    private readonly Layer dimensions = new("DIMENSIONS");
    private readonly Layer notes = new("NOTES");
    private readonly double textHeight;
    private readonly double minX, maxX, minY, maxY;

    private OpticalDrawingDxf(IEnumerable<Vector2> points)
    {
        using var vertices = points.GetEnumerator();
        if (!vertices.MoveNext())
            throw new InvalidDataException(UiLocalization.Text("没有可导出的镜片轮廓。"));

        minX = maxX = vertices.Current.X;
        minY = maxY = vertices.Current.Y;
        do
        {
            var point = vertices.Current;
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new InvalidDataException(UiLocalization.Text("镜片轮廓包含无效坐标。"));
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y);
            maxY = Math.Max(maxY, point.Y);
        } while (vertices.MoveNext());

        textHeight = Math.Max(maxX - minX, maxY - minY) / 36;
        if (textHeight <= 0)
            throw new InvalidDataException(UiLocalization.Text("没有可导出的镜片轮廓。"));
        document.DrawingVariables.InsUnits = DrawingUnits.Millimeters;
        document.Viewport.ViewCenter = new Vector2((minX + maxX) / 2, (minY + maxY) / 2);
        document.Viewport.ViewHeight = textHeight * 64;
        document.Entities.Add(new Line(new Vector2(minX - 4 * textHeight, 0),
            new Vector2(maxX + 4 * textHeight, 0))
        { Layer = axis, LinetypeScale = textHeight / 6 });
    }

    internal static void Export(string path, OpticalDrawingSheet sheet)
    {
        var errors = sheet.Validate().Concat(ValidateTemplateLayout(sheet)).ToArray();
        if (errors.Length > 0)
            throw new ArgumentException(string.Join("；", errors), nameof(sheet));

        var profiles = new List<ManufacturingComponentProfile>();
        double vertex = 0;
        foreach (var component in sheet.Element.Components)
        {
            var profile = BuildCadProfile(component, vertex);
            profiles.Add(profile);
            vertex = profile.BackVertexZ;
        }
        var drawing = new OpticalDrawingDxf(profiles.SelectMany(p => p.Boundary).Select(p => new Vector2(p.Z, p.Y)));
        foreach (var profile in profiles)
        {
            var front = SurfaceVertices(profile.Component.FrontSurface, profile.Front, reverse: false);
            var back = SurfaceVertices(profile.Component.BackSurface, profile.Back, reverse: true);
            drawing.document.Entities.Add(new Polyline2D(front.Concat(back), true) { Layer = drawing.outline });
        }
        drawing.AddElementDimensions(sheet, profiles);
        drawing.AddNotes(TitleLines(sheet.DrawingNumber, sheet.PartName, sheet.Revision, sheet.Designer,
            sheet.Reviewer, sheet.Standard).Concat(SpecificationLines(sheet)));
        drawing.Save(path);
    }

    // The contour includes straight shoulders when one face has a smaller diameter.
    // Preserve those segments separately from the spherical arc.
    private static IEnumerable<Polyline2DVertex> SurfaceVertices(SurfaceRowDto surface,
        IReadOnlyList<ManufacturingProfilePoint> samples, bool reverse)
    {
        var points = reverse ? samples.Reverse().ToArray() : samples;
        if (surface.GeometryDefinition?.Kind is not (null or "standard" or "plane") || surface.Conic != 0)
            return points.Select(p => new Polyline2DVertex(p.Z, p.Y));

        var extended = points.Count == ManufacturingSurfaceSamples + 2;
        var start = points[extended ? 1 : 0];
        var end = points[extended ? ^2 : ^1];
        var middle = points[points.Count / 2];
        // DXF bulge is tan(signed included angle / 4). Two half-arcs retain the vertex snap.
        var bulge = Math.Tan(Math.Atan((start.Z - middle.Z) / start.Y) / 2);
        var vertices = new List<Polyline2DVertex>(5);
        if (extended) vertices.Add(new(points[0].Z, points[0].Y));
        vertices.Add(new(start.Z, start.Y, bulge));
        vertices.Add(new(middle.Z, middle.Y, bulge));
        vertices.Add(new(end.Z, end.Y));
        if (extended) vertices.Add(new(points[^1].Z, points[^1].Y));
        return vertices;
    }

    internal static void Export(string path, OpticalSystemDrawingSheet sheet)
    {
        var lenses = BuildSystemLensGeometry(sheet.Scene);
        if (lenses.Count != sheet.Scene.LensElements.Count || lenses.Any(l => l.Boundary.Count != l.Element.Boundary.Count))
            throw new InvalidDataException(UiLocalization.Text("镜片轮廓包含无效坐标。"));
        var drawing = new OpticalDrawingDxf(lenses.SelectMany(l => l.Boundary).Select(p => new Vector2(p.Z, p.Y)));
        var style = drawing.DimensionStyle("AirGap");
        for (var index = 0; index < lenses.Count; index++)
        {
            var lens = lenses[index];
            drawing.document.Entities.Add(new Polyline2D(lens.Boundary.Select(p => new Vector2(p.Z, p.Y)), true)
            { Layer = drawing.outline });
            if (index == 0) continue;
            var left = lenses[index - 1].BackVertexZ;
            if (lens.FrontVertexZ - left > 1e-6)
                drawing.HorizontalDimension(left, lens.FrontVertexZ,
                    drawing.minY - (4 + 4 * (index % 2)) * drawing.textHeight, style);
        }
        drawing.AddNotes(TitleLines(sheet.DrawingNumber, sheet.PartName, sheet.Revision, sheet.Designer,
            sheet.Reviewer, sheet.Standard).Concat(lenses.Select(l =>
                $"S{l.Element.FrontSurfaceNumber}–S{l.Element.BackSurfaceNumber}  {l.Element.Material}")));
        drawing.Save(path);
    }

    private void AddElementDimensions(OpticalDrawingSheet sheet, List<ManufacturingComponentProfile> profiles)
    {
        var widest = profiles.MaxBy(p => p.ProfileSemiDiameter)!;
        var edge = widest.Front[^1];
        var diameter = DimensionStyle("OuterDiameter", sheet.DiameterUpperDeviation, sheet.DiameterLowerDeviation);
        diameter.DimPrefix = "Ø";
        AddDimension(new AlignedDimension(new Vector2(edge.Z, -edge.Y), new Vector2(edge.Z, edge.Y),
            edge.Z - minX + 4 * textHeight, diameter));
        HorizontalDimension(0, profiles[^1].BackVertexZ, minY - 6 * textHeight,
            DimensionStyle("CenterThickness", sheet.CenterThicknessUpperDeviation, sheet.CenterThicknessLowerDeviation));
        if (profiles.Count > 1)
        {
            var componentStyle = DimensionStyle("ComponentThickness");
            foreach (var p in profiles)
                HorizontalDimension(p.FrontVertexZ, p.BackVertexZ, maxY + 4 * textHeight, componentStyle);
        }
        for (var index = 0; index <= profiles.Count; index++)
        {
            var first = index == 0;
            var p = first ? profiles[0] : profiles[index - 1];
            var surface = first ? p.Component.FrontSurface : p.Component.BackSurface;
            var curve = first ? p.Front : p.Back;
            var vertex = first ? p.FrontVertexZ : p.BackVertexZ;
            var standard = surface.GeometryDefinition?.Kind == "standard" ||
                (surface.GeometryDefinition is null && surface.GeometryKind is "standard" or "标准球面/圆锥");
            if (!standard || surface.Conic != 0 || !double.IsFinite(surface.Radius) || Math.Abs(surface.Radius) < 1e-12)
                continue;
            var tolerance = sheet.Standard == OpticalDrawingStandard.Iso10110 ? RadiusTolerance(sheet, index) : 0;
            var style = DimensionStyle($"RadiusS{index + 1}", tolerance, -tolerance);
            var center = new Vector2(vertex + surface.Radius, 0);
            var contact = curve[index % 2 == 0 ? curve.Count * 3 / 4 : curve.Count / 4];
            var tip = new Vector2(contact.Z, contact.Y);
            var radius = new RadialDimension(center, tip, style);
            radius.TextReferencePoint = tip + (tip - center) * (3 * textHeight / Math.Abs(surface.Radius));
            AddDimension(radius);
        }
    }

    private void HorizontalDimension(double left, double right, double y, DimensionStyle style) =>
        AddDimension(new AlignedDimension(new Vector2(left, 0), new Vector2(right, 0), y, style));

    private void AddDimension(Dimension dimension)
    {
        dimension.Layer = dimensions;
        document.Entities.Add(dimension);
        // netDxf creates the geometry block with nominal text only. Some viewers display
        // that block directly, while CAD editors regenerate it from the dimension style.
        var style = dimension.Style;
        var label = (dimension is RadialDimension ? "R" : style.DimPrefix) +
            dimension.Measurement.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        var tolerance = style.Tolerances;
        if (tolerance.DisplayMethod == DimensionStyleTolerancesDisplayMethod.Symmetrical)
            label += FormattableString.Invariant($" ±{tolerance.UpperLimit:0.######}");
        else if (tolerance.DisplayMethod == DimensionStyleTolerancesDisplayMethod.Deviation)
            label += FormattableString.Invariant($"  {tolerance.UpperLimit:+0.######;-0.######;0}/{-tolerance.LowerLimit:+0.######;-0.######;0}");
        foreach (var text in dimension.Block.Entities.OfType<MText>())
            text.Value = label;
    }

    private static double RadiusTolerance(OpticalDrawingSheet sheet, int index) =>
        index == 0 ? sheet.FrontRadiusTolerance : index == sheet.Element.Components.Count
            ? sheet.BackRadiusTolerance : Math.Max(sheet.FrontRadiusTolerance, sheet.BackRadiusTolerance);

    private DimensionStyle DimensionStyle(string name, double upper = 0, double lower = 0)
    {
        var style = new DimensionStyle(name)
        {
            TextHeight = textHeight,
            ArrowSize = textHeight,
            TextOffset = textHeight * 0.3,
            ExtLineOffset = textHeight * 0.2,
            ExtLineExtend = textHeight * 0.4,
            LengthPrecision = 6,
            DecimalSeparator = '.',
            SuppressLinearTrailingZeros = true
        };
        if (upper != 0 || lower != 0)
        {
            style.Tolerances.DisplayMethod = upper == -lower
                ? DimensionStyleTolerancesDisplayMethod.Symmetrical : DimensionStyleTolerancesDisplayMethod.Deviation;
            style.Tolerances.UpperLimit = upper;
            style.Tolerances.LowerLimit = -lower; // DXF subtracts DIMTM from nominal.
            style.Tolerances.Precision = 6;
            style.Tolerances.SuppressLinearTrailingZeros = true;
        }
        return style;
    }

    private static IEnumerable<string> TitleLines(string number, string name, string revision,
        string designer, string reviewer, OpticalDrawingStandard standard) =>
        [name, $"{UiLocalization.Text("图号")}  {number}    {UiLocalization.Template($"版本 {revision}")}",
         $"{UiLocalization.Text("设计")}  {designer}    {UiLocalization.Text("审核")}  {reviewer}",
         $"{StandardDesignation(standard)}    mm    1:1"];

    private static IEnumerable<string> SpecificationLines(OpticalDrawingSheet sheet)
    {
        var legacy = sheet.Standard == OpticalDrawingStandard.GbT13323_1991;
        var surfaces = sheet.Element.Surfaces;
        for (var i = 0; i < surfaces.Count; i++)
        {
            var radius = legacy ? Gb1991RadiusIndication(surfaces[i]) : RadiusDimensionText(surfaces[i].Radius, RadiusTolerance(sheet, i));
            yield return $"S{i + 1}   {radius}";
            if (!legacy)
                foreach (var line in SurfaceSpecificationLines(sheet, surfaces[i], i == 0))
                    yield return line;
        }
        if (legacy)
        {
            if (!sheet.Element.IsCemented)
                foreach (var (label, value) in Gb1991MaterialSpecificationRows(sheet))
                    yield return $"{label}   {value}";
            foreach (var (label, value) in Gb1991PartSpecificationRows(sheet))
                yield return $"{label}   {value}";
            foreach (var line in Gb1991TechnicalRequirementLines(sheet)) yield return line;
            foreach (var item in Gb1991RoughnessAnnotations(sheet))
                if (item.RaMicrometers is { } ra)
                    yield return $"{item.Scope}  Ra {ra:G12} µm" +
                        (item.SamplingLengthMillimeters is { } length ? $"   l={length:G12} mm" : "");
        }
        else if (sheet.Element.IsCemented)
        {
            for (var i = 0; i < sheet.Element.Components.Count; i++)
            {
                yield return CementedComponentLabel(i);
                var material = sheet.ComponentMaterialData?.ElementAtOrDefault(i) ?? (i == 0 ? sheet.MaterialData : null);
                foreach (var line in ComponentMaterialSpecificationLines(sheet, sheet.Element.Components[i], material))
                    yield return line;
            }
        }
        else
        {
            foreach (var line in MaterialSpecificationLines(sheet)) yield return line;
        }
    }

    private void AddNotes(IEnumerable<string> lines)
    {
        var y = maxY;
        foreach (var line in lines.SelectMany(s => s.ReplaceLineEndings("\n").Split('\n')))
        {
            if (line.Length != 0)
                document.Entities.Add(new Text(line, new Vector2(maxX + 8 * textHeight, y), textHeight) { Layer = notes });
            y -= textHeight * 1.8;
        }
    }

    private void Save(string path) =>
        BoundedApplicationFile.WriteAtomic(path, BoundedApplicationFile.MaximumExportBytes, "Optical drawing DXF", stream =>
        {
            if (!document.Save(stream))
                throw new IOException(UiLocalization.Text("无法写入 DXF 图纸。"));
        });
}
