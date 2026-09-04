using System.Globalization;
using System.Xml.Linq;

namespace OptilandWorkbench.App.Manufacturing;

internal sealed record OpticalDrawingTemplate(
    string Id,
    string Name,
    OpticalDrawingStandard Standard,
    OpticalDrawingPageTemplate Page,
    OpticalDrawingGeometryTemplate Geometry,
    OpticalDrawingTitleBlockTemplate TitleBlock,
    OpticalDrawingSpecificationTemplate Specification)
{
    public IReadOnlyList<string> FieldBindings =>
        TitleBlock.FieldBindings
            .Concat(Specification.FieldBindings)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

internal sealed record OpticalDrawingPageTemplate(
    float OuterMargin,
    float InnerMargin,
    float SpecificationTop,
    float TitleTop);

internal sealed record OpticalDrawingGeometryTemplate(
    float HorizontalInset,
    float TopInset,
    float SpecificationGap,
    float SystemHorizontalInset,
    float SystemTopInset,
    float SystemTitleGap);

internal sealed record OpticalDrawingTitleBlockTemplate(
    string Kind,
    IReadOnlyList<string> FieldBindings);

internal sealed record OpticalDrawingSpecificationTemplate(
    string Kind,
    float HeaderHeight,
    float SubheaderHeight,
    float MaterialWidthRatio,
    float SurfaceWidth,
    float ApertureWidth,
    string? Title,
    float TitleHeight,
    float SectionWidth,
    float ItemWidth,
    float MaterialHeight,
    float PartHeaderHeight,
    IReadOnlyList<OpticalDrawingColumnTemplate> Columns,
    IReadOnlyList<string> SurfaceFields,
    IReadOnlyList<string> MaterialFields,
    IReadOnlyList<string> ComponentMaterialFields,
    IReadOnlyList<string> Gb2009MaterialFields,
    IReadOnlyList<string> Gb2009SurfaceRequirementFields,
    IReadOnlyList<OpticalDrawingMaterialRowTemplate> Gb1991MaterialRows,
    IReadOnlyList<string> Gb1991PartRequirementFields)
{
    public IReadOnlyList<string> FieldBindings =>
        Columns.SelectMany(column => column.Fields)
            .Concat(SurfaceFields)
            .Concat(MaterialFields)
            .Concat(ComponentMaterialFields)
            .Concat(Gb2009MaterialFields)
            .Concat(Gb2009SurfaceRequirementFields)
            .Concat(Gb1991MaterialRows.Select(row => row.ValueBinding))
            .Concat(Gb1991PartRequirementFields)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

internal sealed record OpticalDrawingColumnTemplate(
    string Role,
    string Title,
    float WidthRatio,
    IReadOnlyList<string> Fields);

internal sealed record OpticalDrawingMaterialRowTemplate(
    string Item,
    string ValueBinding);

internal static class OpticalDrawingTemplateCatalog
{
    private static readonly string[] TemplateResourceNames =
    {
        "OptilandWorkbench.App.Assets.DrawingTemplates.iso-10110.xml",
        "OptilandWorkbench.App.Assets.DrawingTemplates.gb-13323-1991.xml",
        "OptilandWorkbench.App.Assets.DrawingTemplates.gb-13323-2009.xml"
    };

    private static readonly Lazy<IReadOnlyDictionary<OpticalDrawingStandard, OpticalDrawingTemplate>> Templates =
        new(LoadTemplates);

    public static IReadOnlyList<OpticalDrawingTemplate> All => Templates.Value.Values
        .OrderBy(template => template.Standard)
        .ToArray();

    public static OpticalDrawingTemplate For(OpticalDrawingStandard standard) =>
        Templates.Value.TryGetValue(standard, out var template)
            ? template
            : Templates.Value[OpticalDrawingStandard.Iso10110];

    private static IReadOnlyDictionary<OpticalDrawingStandard, OpticalDrawingTemplate> LoadTemplates()
    {
        var assembly = typeof(OpticalDrawingTemplateCatalog).Assembly;
        var templates = new Dictionary<OpticalDrawingStandard, OpticalDrawingTemplate>();
        foreach (var resourceName in TemplateResourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException($"Optical drawing template resource '{resourceName}' was not found.");
            var document = XDocument.Load(stream, LoadOptions.SetLineInfo);
            var template = Parse(document.Root
                ?? throw new InvalidDataException($"Optical drawing template '{resourceName}' has no root element."));
            templates[template.Standard] = template;
        }

        foreach (var standard in Enum.GetValues<OpticalDrawingStandard>())
        {
            if (!templates.ContainsKey(standard))
            {
                throw new InvalidDataException($"No optical drawing XML template is registered for '{standard}'.");
            }
        }

        return templates;
    }

    private static OpticalDrawingTemplate Parse(XElement root)
    {
        if (root.Name.LocalName != "opticalDrawingTemplate")
        {
            throw new InvalidDataException("Optical drawing template root must be <opticalDrawingTemplate>.");
        }

        var id = Required(root, "id");
        var name = Required(root, "name");
        var standard = Enum.Parse<OpticalDrawingStandard>(Required(root, "standard"), ignoreCase: true);
        var page = root.Element("page")
            ?? throw new InvalidDataException($"Optical drawing template '{id}' is missing <page>.");
        var geometry = root.Element("geometry")
            ?? throw new InvalidDataException($"Optical drawing template '{id}' is missing <geometry>.");
        var titleBlock = root.Element("titleBlock")
            ?? throw new InvalidDataException($"Optical drawing template '{id}' is missing <titleBlock>.");
        var specification = root.Element("specification")
            ?? throw new InvalidDataException($"Optical drawing template '{id}' is missing <specification>.");

        return new OpticalDrawingTemplate(
            id,
            name,
            standard,
            ParsePage(page),
            ParseGeometry(geometry),
            ParseTitleBlock(titleBlock),
            ParseSpecification(specification));
    }

    private static OpticalDrawingPageTemplate ParsePage(XElement element) => new(
        Float(element, "outerMargin", 12),
        Float(element, "innerMargin", 18),
        Float(element, "specificationTop", 576),
        Float(element, "titleTop", 754));

    private static OpticalDrawingGeometryTemplate ParseGeometry(XElement element) => new(
        Float(element, "horizontalInset", 12),
        Float(element, "topInset", 12),
        Float(element, "specificationGap", 7),
        Float(element, "systemHorizontalInset", 14),
        Float(element, "systemTopInset", 14),
        Float(element, "systemTitleGap", 12));

    private static OpticalDrawingTitleBlockTemplate ParseTitleBlock(XElement element) => new(
        Required(element, "kind"),
        ReadFields(element));

    private static OpticalDrawingSpecificationTemplate ParseSpecification(XElement element)
    {
        var kind = Required(element, "kind");
        var gb1991 = element.Element("gb1991");
        var gb2009 = element.Element("gb2009");
        return new OpticalDrawingSpecificationTemplate(
            kind,
            Float(element, "headerHeight", 24),
            Float(gb2009, "subheaderHeight", 19),
            Float(gb2009, "materialWidthRatio", 0.34f),
            Float(gb2009, "surfaceWidth", 42),
            Float(gb2009, "apertureWidth", 80),
            String(gb1991, "title"),
            Float(gb1991, "titleHeight", 22),
            Float(gb1991, "sectionWidth", 66),
            Float(gb1991, "itemWidth", 82),
            Float(gb1991, "materialHeight", 66),
            Float(gb1991, "partHeaderHeight", 17),
            ParseColumns(element.Element("columns")),
            ReadFields(element.Element("surfaceFields")),
            ReadFields(element.Element("materialFields")),
            ReadFields(element.Element("componentMaterialFields")),
            ReadFields(gb2009?.Element("materialFields")),
            ReadFields(gb2009?.Element("surfaceRequirementFields")),
            ParseMaterialRows(gb1991?.Element("materialRows")),
            ReadFields(gb1991?.Element("partRequirementFields")));
    }

    private static IReadOnlyList<OpticalDrawingColumnTemplate> ParseColumns(XElement? element) =>
        element is null
            ? Array.Empty<OpticalDrawingColumnTemplate>()
            : element.Elements("column")
                .Select(column => new OpticalDrawingColumnTemplate(
                    Required(column, "role"),
                    Required(column, "title"),
                    Float(column, "widthRatio", 0),
                    ReadFields(column)))
                .ToArray();

    private static IReadOnlyList<OpticalDrawingMaterialRowTemplate> ParseMaterialRows(XElement? element) =>
        element is null
            ? Array.Empty<OpticalDrawingMaterialRowTemplate>()
            : element.Elements("row")
                .Select(row => new OpticalDrawingMaterialRowTemplate(
                    Required(row, "item"),
                    Required(row, "valueBinding")))
                .ToArray();

    private static IReadOnlyList<string> ReadFields(XElement? element) =>
        element is null
            ? Array.Empty<string>()
            : element.Elements("field")
                .Select(field => Required(field, "binding"))
                .ToArray();

    private static string Required(XElement element, string attribute) =>
        String(element, attribute) is { Length: > 0 } value
            ? value
            : throw new InvalidDataException(
                $"Optical drawing template element <{element.Name.LocalName}> is missing required '{attribute}'.");

    private static string? String(XElement? element, string attribute) =>
        element?.Attribute(attribute)?.Value.Trim();

    private static float Float(XElement? element, string attribute, float fallback)
    {
        var value = String(element, attribute);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return float.Parse(value, CultureInfo.InvariantCulture);
    }
}
