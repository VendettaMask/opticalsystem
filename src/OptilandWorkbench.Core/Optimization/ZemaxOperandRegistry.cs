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
            "BLNK", "DMFS", "RSCE", "RSCH", "RSRE", "RSRH",
            "OPDX", "OPDM", "OPDC", "TRAC", "TRAR", "TRCX", "TRCY",
            "TRAX", "TRAY", "ANAC", "ANAR", "ANCX", "ANCY", "ANAX", "ANAY",
            "MECS", "MECT", "REAX", "REAY", "REAR", "RANG", "EFFL", "TOTR", "TTHI",
            "CTGT", "MXEG", "PMAG", "PETZ",
            "OPGT", "OPLT", "ABGT", "ABLT", "OPVA",
            "CTLT", "CTVA", "CVGT", "CVLT", "CVVA", "COGT", "COLT", "COVA",
            "ETGT", "ETLT", "ETVA", "FTGT", "FTLT", "STHI",
            "MNCA", "MXCA", "MNEA", "MXEA", "MNCG", "MXCG", "MNEG",
            "MNCT", "MXCT", "MNET", "MXET", "MNCV", "MXCV", "MNSD", "MXSD",
            "XNEA", "XXEA", "XNEG", "XXEG", "XNET", "XXET", "TGTH",
            "TTGT", "TTLT", "TTVA",
            "EFLX", "EFLY", "ENPP", "EPDI", "EXPP", "EXPD", "ISNA", "ISFN", "SFNO", "WFNO",
            "WLEN", "INDX",
            "CONS", "SINE", "COSI", "TANG", "ASIN", "ACOS", "ATAN", "ABSO", "SQRT",
            "RECI", "LOGE", "LOGT", "SUMM", "PROD", "DIVI", "DIFF", "MAXX", "MINN",
            "GOTO", "ENDX", "OOFF", "SKIN", "SKIS", "USYM"
        },
        StringComparer.Ordinal);

    private static readonly string[] PupilRayOperandCodes =
    [
        "OPDX", "OPDM", "OPDC",
        "TRAC", "TRAR", "TRCX", "TRCY", "TRAX", "TRAY",
        "ANAC", "ANAR", "ANCX", "ANCY", "ANAX", "ANAY",
        "REAX", "REAY", "REAR", "RANG"
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

    private static readonly string[] RowRangeMathOperandCodes =
    [
        "MAXX", "MINN"
    ];

    private static readonly string[] RowBoundaryMathOperandCodes =
    [
        "OPGT", "OPLT", "ABGT", "ABLT", "OPVA"
    ];

    private static readonly string[] SurfaceScalarOperandCodes =
    [
        "CTGT", "CTLT", "CTVA",
        "CVGT", "CVLT", "CVVA",
        "COGT", "COLT", "COVA"
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

    private static readonly string[] SumThicknessOperandCodes =
    [
        "TTHI", "TGTH"
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
        "EFFL"
    ];

    private static readonly string[] FirstOrderNoParameterOperandCodes =
    [
        "ENPP", "EPDI", "EXPP", "EXPD", "ISNA", "ISFN", "SFNO", "WFNO"
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

    public static bool TryGet(string? code, out ZemaxOperandDescriptor descriptor) =>
        ByCode.TryGetValue((code ?? string.Empty).Trim().ToUpperInvariant(), out descriptor!);

    public static ZemaxOperandDescriptor Get(string code) =>
        TryGet(code, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"Unknown required Zemax operand '{code}'.");

    private static IReadOnlyList<ZemaxOperandParameterDescriptor> ParametersFor(string code)
    {
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

        if (code is "MECS" or "MECT")
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
            "PMAG" or "PETZ" =>
            [
                new("Int1", "Unused", ZemaxOperandParameterValueKind.Integer),
                new("Int2", "Wavelength", ZemaxOperandParameterValueKind.Wavelength, "wave"),
                new("Data1", "Unused", ZemaxOperandParameterValueKind.Numeric),
                new("Data2", "Unused", ZemaxOperandParameterValueKind.Numeric),
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
