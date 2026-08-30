namespace OptilandWorkbench.Core.Optimization;

public enum ZemaxOperandSupportLevel
{
    Executable,
    CompatibilityOnly
}

public sealed record ZemaxOperandDescriptor(
    string Code,
    string Category,
    ZemaxOperandSupportLevel SupportLevel,
    IReadOnlyList<string> ParameterSlots);

public static class ZemaxOperandRegistry
{
    private const string RequiredSequentialCodes = """
        ABCD ABGT ABLT ABSO ACOS AMAG ANAC ANAR ANAX ANAY ANCX ANCY ASIN ASTI ATAN AXCL BFSD BIOC BIOD BLNK BLTH BSER CEGT CEHX CEHY CELT CENX CENY CEVA CIGT CILT CIVA CMFV CMGT CMLT CMVA CNAX CNAY CNPX CNPY CODA COGT COLT COMA CONF CONS COSI COVA CTGT CTLT CTVA CVGT CVIG CVLT CVOL CVVA DENC DENF DIFF DIMX DISA DISC DISG DIST DIVB DIVI DLTN DMFS DMGT DMLT DMVA DXDX DXDY DYDX DYDY EFFL EFLX EFLY EFNO ENDX ENPP EPDI EQUA ERFP ETGT ETLT ETVA EXPD EXPP FCGS FCGT FCUR FDMO FDRE FICL FICP FOUC FTGT FTLT GBPD GBPP GBPR GBPS GBPW GBPZ GBSD GBSP GBSR GBSS GBSW GCOS GENC GENF GLCA GLCB GLCC GLCR GLCX GLCY GLCZ GMTA GMTS GMTT GOTO GPIM GPRT GPRX GPRY GPSX GPSY GRMN GRMX GTCE HHCN IMAE IMSF INDX INGT INLT INVA ISFN ISNA LACL LINV LOGE LOGT LONA LPTD MAXX MCOG MCOL MCOV MECA MECS MECT MINN MNAB MNCA MNCG MNCT MNCV MNDT MNEA MNEG MNET MNIN MNPD MNRE MNRI MNSD MSWA MSWS MSWT MTFA MTFS MTFT MTHA MTHS MTHT MXAB MXCA MXCG MXCT MXCV MXDT MXEA MXEG MXET MXIN MXPD MXRE MXRI MXSD NORD NORX NORY NORZ OBSN OMMI OMMX OMSD OOFF OPDC OPDM OPDX OPGT OPLT OPTH OPVA OSCD OSUM PANA PANB PANC PARA PARB PARC PARR PARX PARY PARZ PATX PATY PETC PETZ PIMH PLEN PMAG PMGT PMLT PMVA POPD POPI POWF POWP POWR PRIM PROB PROD QSUM RAED RAEN RAGA RAGB RAGC RAGX RAGY RAGZ RAID RAIN RANG REAA REAB REAC REAR REAX REAY REAZ RECI RELI RENA RENB RENC RETX RETY RGLA RSCE RSCH RSRE RSRH RWCE RWCH RWRE RWRH SAGX SAGY SCUR SDRV SFNO SINE SKIN SKIS SMIA SPCH SPHA SQRT SSAG STHI SUMM SVIG TANG TCGT TCLT TCVA TFNO TGTH TMAS TOLR TOTR TRAC TRAD TRAE TRAI TRAR TRAX TRAY TRCX TRCY TTGT TTHI TTLT TTVA UDOC UDOP USYM VOLU WFNO WLEN XDGT XDLT XDVA XENC XENF XNEA XNEG XNET XXEA XXEG XXET YNIP ZERN ZPLM ZTHI
        """;

    private static readonly IReadOnlySet<string> ExecutableCodes = new HashSet<string>(
        new[]
        {
            "BLNK", "DMFS", "RSCE", "RSCH", "RSRE", "RSRH",
            "OPDX", "OPDM", "OPDC", "TRAC", "TRAR", "TRCX", "TRCY",
            "TRAX", "TRAY", "ANAC", "ANAR", "ANCX", "ANCY", "ANAX", "ANAY",
            "MECS", "MECT", "REAX", "REAY", "EFFL", "TOTR"
        },
        StringComparer.Ordinal);

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
                    ["Int1", "Int2", "Data1", "Data2", "Data3", "Data4"]),
                StringComparer.Ordinal);

    public static IReadOnlyList<ZemaxOperandDescriptor> Descriptors { get; } =
        ByCode.Values.OrderBy(descriptor => descriptor.Code, StringComparer.Ordinal).ToArray();

    public static bool TryGet(string? code, out ZemaxOperandDescriptor descriptor) =>
        ByCode.TryGetValue((code ?? string.Empty).Trim().ToUpperInvariant(), out descriptor!);

    public static ZemaxOperandDescriptor Get(string code) =>
        TryGet(code, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"Unknown required Zemax operand '{code}'.");
}
