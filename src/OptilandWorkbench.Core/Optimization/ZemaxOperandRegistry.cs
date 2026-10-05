namespace OptilandWorkbench.Core.Optimization;

public enum ZemaxOperandSupportLevel
{
    Executable,
    CompatibilityOnly
}

public enum ZemaxOperandParameterValueKind
{
    Integer,
    RowReference,
    RowRangeEnd,
    Flag,
    Surface,
    EndSurface,
    Field,
    Wavelength,
    SignedWavelength,
    NormalizedField,
    PupilCoordinate,
    SpatialFrequency,
    Numeric
}

public sealed record ZemaxOperandParameterDescriptor(
    string Slot,
    string DisplayName,
    ZemaxOperandParameterValueKind ValueKind,
    string Unit = "");

public sealed record ZemaxOperandDescriptor(
    string Code,
    string Category,
    ZemaxOperandSupportLevel SupportLevel,
    IReadOnlyList<ZemaxOperandParameterDescriptor> Parameters)
{
    public IReadOnlyList<string> ParameterSlots { get; } =
        Parameters.Select(parameter => parameter.Slot).ToArray();

    public bool UsesSlotAs(string slot, ZemaxOperandParameterValueKind valueKind) =>
        Parameters.Any(parameter => string.Equals(parameter.Slot, slot, StringComparison.Ordinal)
            && parameter.ValueKind == valueKind);
}

public static class ZemaxOperandRegistry
{
    private const string RequiredSequentialCodes = """
        ABCD ABGT ABLT ABSO ACOS AMAG ANAC ANAR ANAX ANAY ANCX ANCY ASIN ASTI ATAN AXCL BFSD BIOC BIOD BIPF BLNK BLTH BSER CARD CEGT CEHX CEHY CELT CENX CENY CEVA CIGT CILT CIVA CMFV CMGT CMLT CMVA CNAX CNAY CNPX CNPY CODA COGT COLT COMA CONF CONS COSA COSI COVA CTGT CTLT CTVA CVGT CVIG CVLT CVOL CVVA DCRV DENC DENF DIFF DIMX DISA DISC DISG DIST DIVB DIVI DLTN DMFS DMGT DMLT DMVA DPHS DSAG DSLP DXDX DXDY DYDX DYDY EFFL EFLA EFLX EFLY EFNO ENDX ENPP EPDI EQUA ERFP ETGT ETLT ETVA EXPD EXPP FCGS FCGT FCUR FDMO FDRE FICL FICP FOUC FTGT FTLT GAOI GBPD GBPP GBPR GBPS GBPW GBPZ GBSD GBSP GBSR GBSS GBSW GCOS GENC GENF GLCA GLCB GLCC GLCR GLCX GLCY GLCZ GMTA GMTN GMTS GMTT GMTX GOTO GPIM GPRT GPRX GPRY GPSX GPSY GRMN GRMX GSCE GSCH GSRE GSRH GTCE HACG HHCN HYLD I1GT I1LT I1VA I2GT I2LT I2VA I3GT I3LT I3VA I4GT I4LT I4VA I5GT I5LT I5VA I6GT I6LT I6VA IMAE IMSF INDX ISFN ISNA LACL LINV LOGE LOGT LONA LPTD MAXX MCOG MCOL MCOV MECA MECS MECT MINN MNAB MNAI MNCA MNCG MNCT MNCV MNDT MNEA MNEG MNET MNIN MNPD MNRE MNRI MNSD MSWA MSWN MSWS MSWT MSWX MTFA MTFN MTFS MTFT MTFX MTHA MTHN MTHS MTHT MTHX MWCE MWCH MWRE MWRH MXAB MXAI MXCA MXCG MXCT MXCV MXDT MXEA MXEG MXET MXIN MXPD MXRE MXRI MXSD NORD NORX NORY NORZ OBSN OOFF OGSS OPDC OPDM OPDX OPGT OPLT OPTH OPVA OSCD OSUM PANA PANB PANC PARA PARB PARC PARR PARX PARY PARZ PATX PATY PETC PETZ PIMH PLEN PMAG PMGT PMLT PMVA POPD POPI POWF POWP POWR PRIM PROB PROD PSLP QOAC QSLP QSUM RAED RAEN RAGA RAGB RAGC RAGX RAGY RAGZ RAID RAIN RANG REAA REAB REAC REAR REAX REAY REAZ RECI RELI RENA RENB RENC REQS RETX RETY RGLA RRET RSCE RSCH RSRE RSRH RWCE RWCH RWRE RWRH SAGX SAGY SCRV SCUR SDRV SFNO SINE SKIN SKIS SMIA SPCH SPHA SPHD SPHS SQRT SSAG SSLP STHI STRH SUMM SVIG TANG TCGT TCLT TCVA TFNO TGTH TMAS TOLR TOTR TRAC TRAD TRAE TRAI TRAN TRAR TRAX TRAY TRCX TRCY TSAG TTGT TTHI TTLT TTVA UDOC USYM VOLU WFNO WLEN XENC XENF XNEA XNEG XNET XXEA XXEG XXET YNIP ZERN ZPLM ZTHI
        """;

    private static readonly IReadOnlySet<string> ExecutableCodes = new HashSet<string>(
        new[]
        {
            "RRET",
            "CODA", "CMGT", "CMLT", "CMVA", "CIGT", "CILT", "CIVA", "CEGT", "CELT", "CEVA",
            "HYLD", "DLTN", "GRMN", "GRMX", "I1GT", "I1LT", "I1VA", "I2GT", "I2LT", "I2VA", "I3GT", "I3LT", "I3VA", "I4GT", "I4LT", "I4VA", "I5GT", "I5LT", "I5VA", "I6GT", "I6LT", "I6VA",
            "TSAG", "BFSD", "RELI", "EFNO",
            "ABCD", "DIST", "DISA", "DISG", "DIMX", "SMIA", "FCGS", "FCGT",
            "MNRE", "MNRI", "MXRE", "MXRI", "TFNO",
            "CONF", "ZTHI", "MCOV", "MCOG", "MCOL", "PRIM", "SVIG", "CVIG", "IMSF", "FDMO", "FDRE", "REQS",
            "SPHS", "PSLP", "DPHS", "QSLP",
            "ZERN",
            "DENC", "DENF",
            "SSAG", "SSLP", "SCRV",
            "GENC", "GENF", "ERFP",
            "VOLU", "TMAS", "DSAG", "DSLP", "DCRV",
            "STRH", "CEHX", "CEHY",
            "MTHA", "MTHS", "MTHT", "MTHN", "MTHX",
            "GBPD", "GBPP", "GBPR", "GBPS", "GBPW", "GBPZ",
            "BLNK", "DMFS", "RSCE", "RSCH", "RSRE", "RSRH",
            "OPDX", "OPDM", "OPDC", "TRAC", "TRAR", "TRCX", "TRCY",
            "TRAX", "TRAY", "ANAC", "ANAR", "ANCX", "ANCY", "ANAX", "ANAY",
            "MECA", "MECS", "MECT", "REAX", "REAY", "REAR", "RANG", "EFFL", "TOTR", "TTHI",
            "TCVA", "TCGT", "TCLT",
            "CTGT", "MXEG", "PMAG", "PETZ",
            "OPGT", "OPLT", "ABGT", "ABLT", "OPVA",
            "CTLT", "CTVA", "CVGT", "CVLT", "CVVA", "COGT", "COLT", "COVA",
            "ETGT", "ETLT", "ETVA", "FTGT", "FTLT", "STHI",
            "MNCA", "MXCA", "MNEA", "MXEA", "MNCG", "MXCG", "MNEG",
            "MNCT", "MXCT", "MNET", "MXET", "MNCV", "MXCV", "MNSD", "MXSD",
            "XNEA", "XXEA", "XNEG", "XXEG", "XNET", "XXET", "TGTH",
            "TTGT", "TTLT", "TTVA",
            "EFLX", "EFLY", "ENPP", "EPDI", "EXPP", "EXPD", "ISNA", "ISFN", "SFNO", "WFNO",
            "WLEN", "INDX", "MNIN", "MXIN", "MNAB", "MXAB", "POWR",
            "CONS", "SINE", "COSI", "TANG", "ASIN", "ACOS", "ATAN", "ABSO", "SQRT",
            "RECI", "LOGE", "LOGT", "SUMM", "PROD", "DIVB", "DIVI", "DIFF",
            "EQUA", "MAXX", "MINN", "OSUM", "PROB", "QSUM",
            "GOTO", "ENDX", "OOFF", "SKIN", "SKIS", "USYM",
            "REAZ", "REAA", "REAB", "REAC", "RENA", "RENB", "RENC", "RETX", "RETY",
            "RAID", "RAIN", "RAED", "RAEN", "OPTH", "PLEN",
            "SAGX", "SAGY", "NORX", "NORY", "NORZ", "NORD", "GTCE", "MNPD", "MXPD",
            "AMAG", "LINV", "PIMH", "OBSN",
            "PARX", "PARY", "PARZ", "PARR", "PARA", "PARB", "PARC", "PATX", "PATY", "PANA", "PANB", "PANC", "YNIP",
            "DMGT", "DMLT", "DMVA", "MNDT", "MXDT", "BLTH", "EFLA",
            "MNAI", "MXAI", "PMVA", "PMGT", "PMLT", "GCOS", "CVOL", "CARD",
            "SCUR", "SDRV", "TRAI", "BSER", "LONA", "AXCL", "LACL", "SPCH",
            "SPHA", "COMA", "ASTI", "FCUR", "PETC",
            "GLCX", "GLCY", "GLCZ", "GLCA", "GLCB", "GLCC", "GLCR",
            "RAGX", "RAGY", "RAGZ", "RAGA", "RAGB", "RAGC", "DXDX", "DXDY", "DYDX", "DYDY",
            "CENX", "CENY", "CNPX", "CNPY", "CNAX", "CNAY", "GSCE", "GSCH", "GSRE", "GSRH",
            "RWCE", "RWCH", "RWRE", "RWRH", "MWCE", "MWCH", "MWRE", "MWRH",
            "GMTA", "GMTS", "GMTT", "GMTN", "GMTX", "MTFA", "MTFS", "MTFT", "MTFN", "MTFX", "MSWA", "MSWS", "MSWT", "MSWN", "MSWX"
        },
        StringComparer.Ordinal);

    private static readonly string[] PupilRayOperandCodes =
    [
        "HYLD",
        "OPDX", "OPDM", "OPDC",
        "RAGX", "RAGY", "RAGZ", "RAGA", "RAGB", "RAGC",
        "TRAC", "TRAR", "TRCX", "TRCY", "TRAX", "TRAY", "TRAI",
        "ANAC", "ANAR", "ANCX", "ANCY", "ANAX", "ANAY",
        "REAX", "REAY", "REAR", "RANG", "REAZ", "REAA", "REAB", "REAC",
        "RENA", "RENB", "RENC", "RETX", "RETY", "RAID", "RAIN", "RAED", "RAEN", "OPTH",
        "PARX", "PARY", "PARZ", "PARR", "PARA", "PARB", "PARC", "PATX", "PATY", "PANA", "PANB", "PANC"
    ];

    private static readonly string[] RmsOperandCodes =
    [
        "RSCE", "RSCH", "RSRE", "RSRH"
    ];

    private static readonly string[] CenterThicknessRangeOperandCodes =
    [
        "MNCA", "MXCA", "MNCG", "MXCG", "MNCT", "MXCT"
    ];

    private static readonly string[] EdgeThicknessRangeOperandCodes =
    [
        "MNEA", "MXEA", "MNEG", "MXEG", "MNET", "MXET",
        "XNEA", "XXEA", "XNEG", "XXEG", "XNET", "XXET"
    ];

    private static readonly string[] UnaryRowMathOperandCodes =
    [
        "SINE", "COSI", "TANG", "ASIN", "ACOS", "ATAN", "ABSO", "SQRT", "RECI", "LOGE", "LOGT"
    ];

    private static readonly string[] BinaryRowMathOperandCodes =
    [
        "DIFF", "DIVI", "SUMM", "PROD"
    ];

    private static readonly string[] ScaledRowMathOperandCodes =
    [
        "DIVB", "PROB"
    ];

    private static readonly string[] RowRangeMathOperandCodes =
    [
        "EQUA", "MAXX", "MINN", "OSUM", "QSUM"
    ];

    private static readonly string[] RowBoundaryMathOperandCodes =
    [
        "OPGT", "OPLT", "ABGT", "ABLT", "OPVA"
    ];

    private static readonly string[] SurfaceScalarOperandCodes =
    [
        "CTGT", "CTLT", "CTVA", "TCVA", "TCGT", "TCLT",
        "CVGT", "CVLT", "CVVA",
        "COGT", "COLT", "COVA", "SAGX", "SAGY", "GTCE", "GCOS"
    ];

    private static readonly string[] SingleEdgeThicknessOperandCodes =
    [
        "ETGT", "ETLT", "ETVA",
        "TTGT", "TTLT", "TTVA"
    ];

    private static readonly string[] FullThicknessOperandCodes =
    [
        "FTGT", "FTLT"
    ];

    private static readonly string[] SpecialThicknessOperandCodes =
    [
        "STHI"
    ];

    private static readonly string[] RangeCurvatureOperandCodes =
    [
        "MNCV", "MXCV"
    ];

    private static readonly string[] RangeSemiDiameterOperandCodes =
    [
        "MNSD", "MXSD"
    ];

    private static readonly string[] GlassRangeOperandCodes =
    [
        "MNIN", "MXIN", "MNAB", "MXAB", "MNPD", "MXPD"
    ];

    private static readonly string[] SurfacePowerOperandCodes =
    [
        "POWR", "YNIP", "EFLA"
    ];

    private static readonly string[] SumThicknessOperandCodes =
    [
        "TTHI", "TGTH", "ZTHI"
    ];

    private static readonly string[] TotalTrackOperandCodes =
    [
        "TOTR"
    ];

    private static readonly string[] EffectiveFocalLengthRangeOperandCodes =
    [
        "EFLX", "EFLY"
    ];

    private static readonly string[] FirstOrderOperandCodes =
    [
        "EFFL", "AMAG", "LINV", "PIMH", "BSER"
    ];

    private static readonly string[] FirstOrderNoParameterOperandCodes =
    [
        "ENPP", "EPDI", "EXPP", "EXPD", "ISNA", "ISFN", "WFNO", "OBSN"
    ];

    private static readonly IReadOnlyDictionary<string, ZemaxOperandDescriptor> ByCode =
        RequiredSequentialCodes
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToDictionary(
                code => code,
                code => new ZemaxOperandDescriptor(
                    code,
                    "Zemax sequential",
                    ExecutableCodes.Contains(code)
                        ? ZemaxOperandSupportLevel.Executable
                        : ZemaxOperandSupportLevel.CompatibilityOnly,
                    ParametersFor(code)),
                StringComparer.Ordinal);

    public static IReadOnlyList<ZemaxOperandDescriptor> Descriptors { get; } =
        ByCode.Values.OrderBy(descriptor => descriptor.Code, StringComparer.Ordinal).ToArray();

    /// <summary>Names retained in the API but explicitly marked Unused in the 2026 R1 manual.</summary>
    public static bool IsDocumentedUnused(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant()
        is "BIPF" or "COSA" or "HACG" or "QOAC" or "TRAN";

    public static bool IsGradientIndexControl(string code) => code is "DLTN" or "GRMN" or "GRMX"
        || code.Length == 4 && code[0] == 'I' && code[1] is >= '1' and <= '6' && code[2..] is "GT" or "LT" or "VA";

    public static bool TryGet(string? code, out ZemaxOperandDescriptor descriptor) =>
        ByCode.TryGetValue((code ?? string.Empty).Trim().ToUpperInvariant(), out descriptor!);

    public static ZemaxOperandDescriptor Get(string code) =>
        TryGet(code, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"Unknown required Zemax operand '{code}'.");

    public static bool IsCoatingLayerConstraint(string code) => code is
        "CMGT" or "CMLT" or "CMVA" or "CIGT" or "CILT" or "CIVA" or "CEGT" or "CELT" or "CEVA";

    private static IReadOnlyList<ZemaxOperandParameterDescriptor> ParametersFor(string code)
    {
        if (code is "MCOV" or "MCOG" or "MCOL")
            return
            [
                new("Int1", "MCE operand row (>0)", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Configuration (>0)", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        if (IsCoatingLayerConstraint(code))
            return
            [
                new("Int1", code.EndsWith("VA", StringComparison.Ordinal) ? "Surface (>0)" : "Surface (0=all)", ZemaxOperandParameterValueKind.Surface),
                new("Int2", code.EndsWith("VA", StringComparison.Ordinal) ? "Layer (>0)" : "Layer (0=all)", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        if (code == "CODA")
            return
            [
                new("Int1", "Surface (0=image)", ZemaxOperandParameterValueKind.Surface),
                new("Int2", "Wavelength (>0)", ZemaxOperandParameterValueKind.Wavelength),
                new("Data1", "Field (>0)", ZemaxOperandParameterValueKind.Field),
                new("Data2", "Px", ZemaxOperandParameterValueKind.PupilCoordinate),
                new("Data3", "Py", ZemaxOperandParameterValueKind.PupilCoordinate),
                new("Data4", "Data", ZemaxOperandParameterValueKind.Integer)
            ];
        if (code == "RRET")
            return
            [
                new("Int1", "Rings (1..32)", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength (0=all)", ZemaxOperandParameterValueKind.Wavelength),
                new("Data1", "Hx", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data2", "Hy", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data3", "Jx", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Jy", ZemaxOperandParameterValueKind.Numeric),
                new("Data5", "X-Phase", ZemaxOperandParameterValueKind.Numeric, "deg"),
                new("Data6", "Y-Phase", ZemaxOperandParameterValueKind.Numeric, "deg")
            ];
        if (IsGradientIndexControl(code))
            return
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Wavelength (>0)", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        if (code == "ZERN")
            return
            [
                new("Int1", "Term (-8..37/231)", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wave", ZemaxOperandParameterValueKind.Wavelength),
                new("Data1", "Sampling (1..5)", ZemaxOperandParameterValueKind.Integer),
                new("Data2", "Field", ZemaxOperandParameterValueKind.Field),
                new("Data3", "Type (0 Fringe, 1 Standard, 2 Annular)", ZemaxOperandParameterValueKind.Integer),
                new("Data4", "Epsilon", ZemaxOperandParameterValueKind.Numeric),
                new("Data5", "Vertex (0)", ZemaxOperandParameterValueKind.Flag)
            ];
        if (code is "FDMO" or "FDRE")
            return
            [
                new("Int1", "Field", ZemaxOperandParameterValueKind.Field),
                new("Int2", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Data1", code == "FDMO" ? "Hx" : "Unused", code == "FDMO" ? ZemaxOperandParameterValueKind.NormalizedField : ZemaxOperandParameterValueKind.Numeric),
                new("Data2", code == "FDMO" ? "Hy" : "Unused", code == "FDMO" ? ZemaxOperandParameterValueKind.NormalizedField : ZemaxOperandParameterValueKind.Numeric),
                new("Data3", code == "FDMO" ? "VDX" : "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", code == "FDMO" ? "VDY" : "Unused", ZemaxOperandParameterValueKind.Numeric),
                .. code == "FDMO" ? new[]
                {
                    new ZemaxOperandParameterDescriptor("Data5", "VCX", ZemaxOperandParameterValueKind.Numeric),
                    new ZemaxOperandParameterDescriptor("Data6", "VCY", ZemaxOperandParameterValueKind.Numeric)
                } : Array.Empty<ZemaxOperandParameterDescriptor>()
            ];
        if (code == "REQS")
            return new[] { "Int1", "Int2", "Data1", "Data2", "Data3", "Data4" }
                .Select(slot => new ZemaxOperandParameterDescriptor(slot, "Unused", ZemaxOperandParameterValueKind.Numeric)).ToArray();
        if (code is "SPHS" or "PSLP")
            return
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface),
                new("Int2", "Mode (0/1 mm, 2 normalized)", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "X / Xn", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Y / Yn", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", code == "SPHS" ? "Data (0)" : "Remove (0/1)", ZemaxOperandParameterValueKind.Integer),
                new("Data4", code == "SPHS" ? "Remove (0/1)" : "Orientation (0..4)", ZemaxOperandParameterValueKind.Integer)
            ];
        if (code is "DPHS" or "QSLP")
            return
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface),
                new("Int2", "Data (1..12)", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Sampling (1..5)", ZemaxOperandParameterValueKind.Integer),
                new("Data2", "Remove (0/1)", ZemaxOperandParameterValueKind.Integer),
                new("Data3", code == "QSLP" ? "Orientation (0..4)" : "Unused", code == "QSLP" ? ZemaxOperandParameterValueKind.Integer : ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        if (code is "DENC" or "DENF")
            return
            [
                new("Int1", "Pupil sampling (1..4)", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength (0=all)", ZemaxOperandParameterValueKind.Wavelength),
                new("Data1", "Field", ZemaxOperandParameterValueKind.Field),
                new("Data2", "Type (1..4)", ZemaxOperandParameterValueKind.Integer),
                new("Data3", "Reference / algorithm (0..5)", ZemaxOperandParameterValueKind.Integer),
                new("Data4", code == "DENC" ? "Fraction" : "Distance", ZemaxOperandParameterValueKind.Numeric, code == "DENC" ? "" : "µm"),
                new("Data5", "Huygens image sampling (1..4)", ZemaxOperandParameterValueKind.Integer),
                new("Data6", "Huygens image delta (0=default)", ZemaxOperandParameterValueKind.Numeric, "µm")
            ];
        if (code == "TSAG")
            return
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface),
                new("Int2", "Mode (0 flat edge, 1 equation)", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "X", ZemaxOperandParameterValueKind.Numeric, "mm"),
                new("Data2", "Y", ZemaxOperandParameterValueKind.Numeric, "mm"),
                new("Data3", "Z", ZemaxOperandParameterValueKind.Numeric, "mm"),
                new("Data4", "Tilt About X", ZemaxOperandParameterValueKind.Numeric, "deg"),
                new("Data5", "Tilt About Y", ZemaxOperandParameterValueKind.Numeric, "deg"),
                new("Data6", "Tilt About Z", ZemaxOperandParameterValueKind.Numeric, "deg")
            ];
        if (code is "SSAG" or "SSLP" or "SCRV")
            return
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface),
                new("Int2", "Mode (0 mechanical, 1 clear)", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "X", ZemaxOperandParameterValueKind.Numeric, "mm"),
                new("Data2", "Y", ZemaxOperandParameterValueKind.Numeric, "mm"),
                new("Data3", "Off-axis", ZemaxOperandParameterValueKind.Flag),
                new("Data4", "Remove", ZemaxOperandParameterValueKind.Integer),
                new("Data5", "BFS", ZemaxOperandParameterValueKind.Integer),
                .. code == "SSAG" ? Array.Empty<ZemaxOperandParameterDescriptor>() : new[]
                { new ZemaxOperandParameterDescriptor("Data6", code == "SSLP" ? "Orientation (0..4)" : "Orientation (0..3)", ZemaxOperandParameterValueKind.Integer) }
            ];
        if (code is "GENC" or "GENF" or "ERFP")
            return
            [
                new("Int1", "Sampling (1..5)", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength (0=all)", ZemaxOperandParameterValueKind.Wavelength),
                new("Data1", "Field", ZemaxOperandParameterValueKind.Field),
                new("Data2", code == "ERFP" ? "Type (0..3)" : "Type (1..4)", ZemaxOperandParameterValueKind.Integer),
                new("Data3", code == "ERFP" ? "Fraction" : "Reference (0..3)", code == "ERFP" ? ZemaxOperandParameterValueKind.Numeric : ZemaxOperandParameterValueKind.Integer),
                new("Data4", code == "ERFP" ? "Max Radius (0 only)" : code == "GENC" ? "Fraction" : "Distance", ZemaxOperandParameterValueKind.Numeric, code == "GENC" ? "" : "µm"),
                .. code == "ERFP" ? Array.Empty<ZemaxOperandParameterDescriptor>() : new[]
                { new ZemaxOperandParameterDescriptor("Data5", "No diffraction limit", ZemaxOperandParameterValueKind.Flag) }
            ];
        if (code is "VOLU" or "TMAS")
            return
            [
                new("Int1", "Start surface", ZemaxOperandParameterValueKind.Surface),
                new("Int2", "End surface", ZemaxOperandParameterValueKind.EndSurface),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Mode", ZemaxOperandParameterValueKind.Flag),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        if (code is "DSAG" or "DSLP" or "DCRV")
            return
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface),
                new("Int2", "Profile data (1..8)", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Samp (33..513)", ZemaxOperandParameterValueKind.Integer),
                new("Data2", "Off-axis", ZemaxOperandParameterValueKind.Flag),
                new("Data3", "Remove", ZemaxOperandParameterValueKind.Integer),
                new("Data4", "BFS", ZemaxOperandParameterValueKind.Integer),
                .. code == "DSAG" ? Array.Empty<ZemaxOperandParameterDescriptor>() : new[]
                { new ZemaxOperandParameterDescriptor("Data5", code == "DSLP" ? "Orientation (0..4)" : "Orientation (0..3)", ZemaxOperandParameterValueKind.Integer) }
            ];
        if (code == "SVIG")
            return
            [
                new("Int1", "Precision", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        if (code is "CONF" or "PRIM" or "CVIG" or "IMSF")
            return
            [
                new("Int1", code == "CONF" ? "Configuration (>0)" : code == "PRIM" ? "Wavelength" : code == "IMSF" ? "Surface (0=restore)" : "Unused",
                    code == "PRIM" ? ZemaxOperandParameterValueKind.Wavelength : code == "IMSF" ? ZemaxOperandParameterValueKind.Surface : ZemaxOperandParameterValueKind.Integer),
                new("Int2", code == "IMSF" ? "Refocus" : "Unused", code == "IMSF" ? ZemaxOperandParameterValueKind.Flag : ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        if (code is "GBPD" or "GBPP" or "GBPR" or "GBPS" or "GBPW" or "GBPZ")
            return
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface),
                new("Int2", "Wavelength (0=primary)", ZemaxOperandParameterValueKind.Wavelength),
                new("Data1", "Use X", ZemaxOperandParameterValueKind.Flag),
                new("Data2", "Embedded waist radius", ZemaxOperandParameterValueKind.Numeric, "mm"),
                new("Data3", "Surface 1 to waist", ZemaxOperandParameterValueKind.Numeric, "mm"),
                new("Data4", "M squared", ZemaxOperandParameterValueKind.Numeric)
            ];
        if (code is "MNRE" or "MNRI" or "MXRE" or "MXRI")
            return
            [
                new("Int1", "Start surface", ZemaxOperandParameterValueKind.Surface),
                new("Int2", "End surface", ZemaxOperandParameterValueKind.EndSurface),
                new("Data1", "Wavelength (0=primary)", ZemaxOperandParameterValueKind.Wavelength),
                new("Data2", "Hx", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data3", "Hy", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data4", "Px", ZemaxOperandParameterValueKind.PupilCoordinate),
                new("Data5", "Py", ZemaxOperandParameterValueKind.PupilCoordinate)
            ];
        if (code is "CEHX" or "CEHY")
            return
            [
                new("Int1", "Wavelength (0=all)", ZemaxOperandParameterValueKind.Wavelength),
                new("Int2", "Field", ZemaxOperandParameterValueKind.Field),
                new("Data1", "Polarization (0 only)", ZemaxOperandParameterValueKind.Flag),
                new("Data2", "Pupil sampling (1..4)", ZemaxOperandParameterValueKind.Integer),
                new("Data3", "Image sampling (1..4)", ZemaxOperandParameterValueKind.Integer),
                new("Data4", "All configurations (0 only)", ZemaxOperandParameterValueKind.Flag)
            ];
        if (code == "STRH")
            return
            [
                new("Int1", "Sampling (1..2)", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength (0=all)", ZemaxOperandParameterValueKind.Wavelength),
                new("Data1", "Field", ZemaxOperandParameterValueKind.Field),
                new("Data2", "Polarization (0 only)", ZemaxOperandParameterValueKind.Flag),
                new("Data3", "All configurations (0 only)", ZemaxOperandParameterValueKind.Flag),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        if (code is "MTHA" or "MTHS" or "MTHT" or "MTHN" or "MTHX")
            return
            [
                new("Int1", "Sampling (1..2)", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength (0=all)", ZemaxOperandParameterValueKind.Wavelength),
                new("Data1", "Field", ZemaxOperandParameterValueKind.Field),
                new("Data2", "Frequency", ZemaxOperandParameterValueKind.SpatialFrequency, "cycles/mm"),
                new("Data3", "Polarization (0 only)", ZemaxOperandParameterValueKind.Flag),
                new("Data4", "All configurations (0 only)", ZemaxOperandParameterValueKind.Flag),
                new("Data5", "Image delta (0=automatic)", ZemaxOperandParameterValueKind.Numeric, "µm")
            ];
        if (code is "SFNO" or "TFNO")
            return
            [
                new("Int1", "Field (0=axial)", ZemaxOperandParameterValueKind.Field),
                new("Int2", "Wavelength (0=primary)", ZemaxOperandParameterValueKind.Wavelength),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        if (code is "GMTA" or "GMTS" or "GMTT" or "GMTN" or "GMTX" or "MTFA" or "MTFS" or "MTFT" or "MTFN" or "MTFX" or "MSWA" or "MSWS" or "MSWT" or "MSWN" or "MSWX")
        {
            var geometric = code.StartsWith("GMT", StringComparison.Ordinal);
            var complex = code is "MTFA" or "MTFS" or "MTFT";
            return
            [
                new("Int1", "Sampling (1..5)", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength (0=all)", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", geometric ? "Field" : "Field (0=diffraction limit)", ZemaxOperandParameterValueKind.Field),
                new("Data2", "Frequency", ZemaxOperandParameterValueKind.SpatialFrequency, "cycles/mm; afocal cycles/mrad"),
                new("Data3", geometric ? "!Scale" : "Grid (1 only)", ZemaxOperandParameterValueKind.Flag),
                new("Data4", geometric ? "Grid (1 only)" : complex ? "Data Type (0..3)" : "Unused",
                    geometric ? ZemaxOperandParameterValueKind.Flag : complex ? ZemaxOperandParameterValueKind.Integer : ZemaxOperandParameterValueKind.Numeric)
            ];
        }
        if (code is "CENX" or "CENY" or "CNPX" or "CNPY" or "CNAX" or "CNAY")
        {
            var numberedField = code is "CENX" or "CENY";
            return
            [
                new("Int1", "Surface (0=image)", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Wavelength (0=all)", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", numberedField ? "Field" : "Hx", numberedField ? ZemaxOperandParameterValueKind.Field : ZemaxOperandParameterValueKind.NormalizedField),
                new("Data2", numberedField ? "Polarization (0 only)" : "Hy", numberedField ? ZemaxOperandParameterValueKind.Flag : ZemaxOperandParameterValueKind.NormalizedField),
                new("Data3", numberedField ? "Sampling" : "Polarization (0 only)", numberedField ? ZemaxOperandParameterValueKind.Integer : ZemaxOperandParameterValueKind.Flag),
                new("Data4", numberedField ? "Unused" : "Sampling", ZemaxOperandParameterValueKind.Integer)
            ];
        }
        if (code is "RWCE" or "RWCH" or "RWRE" or "RWRH" or "MWCE" or "MWCH" or "MWRE" or "MWRH")
        {
            return
            [
                new("Int1", code[2] == 'C' ? "Rings" : "Sampling", ZemaxOperandParameterValueKind.Integer),
                new("Int2", code[0] == 'R' ? "Wavelength (0=all)" : "Wavelength (positive)", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Hx", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data2", "Hy", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }
        if (code is "GSCE" or "GSCH" or "GSRE" or "GSRH")
        {
            return
            [
                new("Int1", code is "GSCE" or "GSCH" ? "Rings" : "Sampling", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength (positive)", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Hx", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data2", "Hy", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }
        if (code is "GLCX" or "GLCY" or "GLCZ" or "GLCA" or "GLCB" or "GLCC" or "GLCR")
        {
            return
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", code == "GLCR" ? "Data (1..9)" : "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }
        if (code is "DXDX" or "DXDY" or "DYDX" or "DYDY")
        {
            return
            [
                new("Int1", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Hx", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data2", "Hy", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data3", "Px", ZemaxOperandParameterValueKind.PupilCoordinate),
                new("Data4", "Py", ZemaxOperandParameterValueKind.PupilCoordinate)
            ];
        }
        if (code is "SPHA" or "COMA" or "ASTI" or "FCUR")
        {
            return
            [
                new("Int1", "Surface (0=total)", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }
        if (code is "LONA" or "AXCL" or "LACL" or "SPCH")
        {
            return
            [
                new("Int1", code == "LONA" ? "Unused" : code == "AXCL" ? "Wave1" : "Minw",
                    code == "LONA" ? ZemaxOperandParameterValueKind.Integer : ZemaxOperandParameterValueKind.Wavelength, code == "LONA" ? "" : "wave"),
                new("Int2", code == "LONA" ? "Wave" : code == "AXCL" ? "Wave2" : "Maxw", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", code == "LACL" ? "Unused" : "Zone", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }
        if (code is "SCUR" or "SDRV")
        {
            return
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Data", ZemaxOperandParameterValueKind.Flag),
                new("Data1", "X", ZemaxOperandParameterValueKind.Numeric, "lens"),
                new("Data2", "Y", ZemaxOperandParameterValueKind.Numeric, "lens"),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }
        if (code is "MNAI" or "MXAI")
        {
            return
            [
                new("Int1", "Surface (0=all)", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Wavelength (0=all)", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Field (0=all)", ZemaxOperandParameterValueKind.Field),
                new("Data2", "Symmetry", ZemaxOperandParameterValueKind.Flag),
                new("Data3", "Data", ZemaxOperandParameterValueKind.Flag),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }
        if (code is "PMVA" or "PMGT" or "PMLT")
        {
            return
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Parameter", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }
        if (code == "CARD")
        {
            return
            [
                new("Int1", "Start surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "End surface", ZemaxOperandParameterValueKind.EndSurface, "surface"),
                new("Data1", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data2", "Orientation", ZemaxOperandParameterValueKind.Flag),
                new("Data3", "Data", ZemaxOperandParameterValueKind.Flag),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }
        if (code is "DMGT" or "DMLT" or "DMVA" or "BLTH" or "MNDT" or "MXDT" or "CVOL")
        {
            return
            [
                new("Int1", code is "MNDT" or "MXDT" or "CVOL" ? "Start surface" : "Surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", code == "BLTH" ? "Axis code" : code is "MNDT" or "MXDT" or "CVOL" ? "End surface" : "Unused",
                    code == "BLTH" ? ZemaxOperandParameterValueKind.Flag : code is "MNDT" or "MXDT" or "CVOL"
                        ? ZemaxOperandParameterValueKind.EndSurface : ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Mode", ZemaxOperandParameterValueKind.Flag),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }

        if (code is "NORX" or "NORY" or "NORZ" or "NORD")
        {
            return
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "X", ZemaxOperandParameterValueKind.Numeric, "lens"),
                new("Data2", "Y", ZemaxOperandParameterValueKind.Numeric, "lens"),
                new("Data3", code == "NORD" ? "Unused" : "Global", ZemaxOperandParameterValueKind.Flag),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }

        if (code == "PLEN")
        {
            return
            [
                new("Int1", "Start surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "End surface", ZemaxOperandParameterValueKind.EndSurface, "surface"),
                new("Data1", "Hx", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data2", "Hy", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data3", "Px", ZemaxOperandParameterValueKind.PupilCoordinate),
                new("Data4", "Py", ZemaxOperandParameterValueKind.PupilCoordinate)
            ];
        }

        if (PupilRayOperandCodes.Contains(code, StringComparer.Ordinal))
        {
            return
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Hx", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data2", "Hy", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data3", "Px", ZemaxOperandParameterValueKind.PupilCoordinate),
                new("Data4", "Py", ZemaxOperandParameterValueKind.PupilCoordinate)
            ];
        }

        if (RmsOperandCodes.Contains(code, StringComparer.Ordinal))
        {
            return
            [
                new("Int1", "Rings", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Hx", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data2", "Hy", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }

        if (UnaryRowMathOperandCodes.Contains(code, StringComparer.Ordinal))
        {
            return
            [
                new("Int1", "Operand row", ZemaxOperandParameterValueKind.RowReference, "row"),
                new("Int2", "Flag", ZemaxOperandParameterValueKind.Flag),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }

        if (BinaryRowMathOperandCodes.Contains(code, StringComparer.Ordinal))
        {
            return
            [
                new("Int1", "Operand row 1", ZemaxOperandParameterValueKind.RowReference, "row"),
                new("Int2", "Operand row 2", ZemaxOperandParameterValueKind.RowReference, "row"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }

        if (ScaledRowMathOperandCodes.Contains(code, StringComparer.Ordinal))
        {
            return
            [
                new("Int1", "Operand row", ZemaxOperandParameterValueKind.RowReference, "row"),
                new("Int2", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Factor", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }

        if (RowRangeMathOperandCodes.Contains(code, StringComparer.Ordinal))
        {
            return
            [
                new("Int1", "First operand row", ZemaxOperandParameterValueKind.RowReference, "row"),
                new("Int2", "Last operand row", ZemaxOperandParameterValueKind.RowRangeEnd, "row"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }

        if (RowBoundaryMathOperandCodes.Contains(code, StringComparer.Ordinal))
        {
            return
            [
                new("Int1", "Operand row", ZemaxOperandParameterValueKind.RowReference, "row"),
                new("Int2", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ];
        }

        if (code is "MECA" or "MECS" or "MECT")
        {
            return
            [
                new("Int1", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Field", ZemaxOperandParameterValueKind.Field),
                new("Data2", "Spatial frequency", ZemaxOperandParameterValueKind.SpatialFrequency, "lp/mm"),
                new("Data3", "Px", ZemaxOperandParameterValueKind.PupilCoordinate),
                new("Data4", "Py", ZemaxOperandParameterValueKind.PupilCoordinate)
            ];
        }

        return code switch
        {
            _ when SurfaceScalarOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ when SingleEdgeThicknessOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Edge code", ZemaxOperandParameterValueKind.Flag),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Mode", ZemaxOperandParameterValueKind.Flag),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ when FullThicknessOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Mode", ZemaxOperandParameterValueKind.Flag),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ when SpecialThicknessOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "X", ZemaxOperandParameterValueKind.Numeric, "lens"),
                new("Data2", "Y", ZemaxOperandParameterValueKind.Numeric, "lens"),
                new("Data3", "Mode", ZemaxOperandParameterValueKind.Flag),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ when CenterThicknessRangeOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Start surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "End surface", ZemaxOperandParameterValueKind.EndSurface, "surface"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ when EdgeThicknessRangeOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Start surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "End surface", ZemaxOperandParameterValueKind.EndSurface, "surface"),
                new("Data1", "Zone", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Mode", ZemaxOperandParameterValueKind.Flag),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ when RangeCurvatureOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Start surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "End surface", ZemaxOperandParameterValueKind.EndSurface, "surface"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ when RangeSemiDiameterOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Start surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "End surface", ZemaxOperandParameterValueKind.EndSurface, "surface"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ when GlassRangeOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Start surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "End surface", ZemaxOperandParameterValueKind.EndSurface, "surface"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ when SurfacePowerOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ when SumThicknessOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Start surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "End surface", ZemaxOperandParameterValueKind.EndSurface, "surface"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ when EffectiveFocalLengthRangeOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Start surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "End surface", ZemaxOperandParameterValueKind.EndSurface, "surface"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ when FirstOrderOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ when FirstOrderNoParameterOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ when TotalTrackOperandCodes.Contains(code, StringComparer.Ordinal) =>
            [
                new("Int1", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            "WLEN" =>
            [
                new("Int1", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            "INDX" =>
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            "PMAG" or "PETZ" or "PETC" =>
            [
                new("Int1", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            "DIST" =>
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface, "surface"),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Absolute", ZemaxOperandParameterValueKind.Flag),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            "DISA" =>
            [
                new("Int1", "Ref Field", ZemaxOperandParameterValueKind.Field),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Field", ZemaxOperandParameterValueKind.Field),
                new("Data2", "Data", ZemaxOperandParameterValueKind.Integer),
                new("Data3", "A", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "B", ZemaxOperandParameterValueKind.Numeric),
                new("Data5", "C", ZemaxOperandParameterValueKind.Numeric),
                new("Data6", "D", ZemaxOperandParameterValueKind.Numeric)
            ],
            "ABCD" =>
            [
                new("Int1", "Ref Field", ZemaxOperandParameterValueKind.Field),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Data", ZemaxOperandParameterValueKind.Integer),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            "DISG" =>
            [
                new("Int1", "Ref Field", ZemaxOperandParameterValueKind.Field),
                new("Int2", "Signed Wave", ZemaxOperandParameterValueKind.SignedWavelength, "±wave"),
                new("Data1", "Hx", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data2", "Hy", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data3", "Px", ZemaxOperandParameterValueKind.PupilCoordinate),
                new("Data4", "Py", ZemaxOperandParameterValueKind.PupilCoordinate)
            ],
            "SMIA" =>
            [
                new("Int1", "Ref Field", ZemaxOperandParameterValueKind.Field),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "X Width", ZemaxOperandParameterValueKind.Numeric, "field"),
                new("Data2", "Y Width", ZemaxOperandParameterValueKind.Numeric, "field"),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            "FCGS" or "FCGT" =>
            [
                new("Int1", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Hx", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data2", "Hy", ZemaxOperandParameterValueKind.NormalizedField),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            "DIMX" =>
            [
                new("Int1", "Field", ZemaxOperandParameterValueKind.Field),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Absolute", ZemaxOperandParameterValueKind.Flag),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            "BFSD" =>
            [
                new("Int1", "Surface", ZemaxOperandParameterValueKind.Surface),
                new("Int2", "BFS data (0..7)", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Minimum radius", ZemaxOperandParameterValueKind.Numeric, "mm"),
                new("Data2", "Maximum radius", ZemaxOperandParameterValueKind.Numeric, "mm"),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            "EFNO" or "RELI" =>
            [
                new("Int1", "Sampling", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Field", ZemaxOperandParameterValueKind.Field),
                new("Data2", "Polarization", ZemaxOperandParameterValueKind.Flag),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            "CONS" =>
            [
                new("Int1", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            "GOTO" or "SKIN" or "SKIS" =>
            [
                new("Int1", "Operand row", ZemaxOperandParameterValueKind.RowReference, "row"),
                new("Int2", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            "ENDX" or "OOFF" or "USYM" =>
            [
                new("Int1", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Unused", ZemaxOperandParameterValueKind.Numeric)
            ],
            _ =>
            [
                new("Int1", "Int1", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Int2", ZemaxOperandParameterValueKind.Integer),
                new("Data1", "Data1", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Data2", ZemaxOperandParameterValueKind.Numeric),
                new("Data3", "Data3", ZemaxOperandParameterValueKind.Numeric),
                new("Data4", "Data4", ZemaxOperandParameterValueKind.Numeric)
            ]
        };
    }
}
