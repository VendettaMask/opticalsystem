using System.Globalization;
using System.Text;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Phase;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Core.FileIO;

internal static partial class ZemaxZmxReader
{
    public static Optic Import(string text)
    {
        return ImportConfigurationSet(text).ActiveOptic;
    }

    public static ZemaxZmxImportResult ImportConfigurationSet(string text, string? assetDirectory = null)
    {
        var document = Parse(text, assetDirectory);
        Validate(document);

        var configurations = Enumerable.Range(0, Math.Max(1, document.ConfigurationCount))
            .Select(configurationIndex => BuildOptic(document, configurationIndex))
            .ToArray();
        var activeConfigurationIndex = DetectActiveConfiguration(document);
        return new ZemaxZmxImportResult(
            configurations[activeConfigurationIndex],
            configurations,
            activeConfigurationIndex);
    }

    private static Optic BuildOptic(ZemaxDocument document, int configurationIndex)
    {
        var optic = new Optic(document.Name);
        optic.Materials.SetPreferredGlassCatalogs(document.GlassCatalogs);
        var configuredSurfaces = ConfigureSurfaces(document, configurationIndex);
        var converted = ConvertSurfaces(optic, configuredSurfaces, document.GlassCatalogs);
        InstallConvertedSurfaces(optic, converted);
        var sourceSurfaceNumbers = configuredSurfaces
            .Where(IsImportedSurface)
            .Select(surface => surface.Number).ToArray();
        optic.GlobalReferenceSurfaceNumber = Array.IndexOf(sourceSurfaceNumbers, document.GlobalReferenceSurfaceNumber);
        if (optic.GlobalReferenceSurfaceNumber < 0)
            throw new NotSupportedException($"全局参考面 {document.GlobalReferenceSurfaceNumber} 不存在或为坐标断点；不能转换到物理表面。");
        InstallSemiDiameterPickups(optic, configuredSurfaces);
        InstallCurvaturePickups(optic, configuredSurfaces);
        InstallThicknessPickups(optic, configuredSurfaces);

        ConfigureAperture(optic, document, configurationIndex);
        optic.Apodization = document.Apodization?.Clone();
        optic.RayAimingEnabled = document.RayAimingEnabled;
        var configuredAfocal = document.ConfigurationDouble("AFOC", 0, configurationIndex);
        if (configuredAfocal.HasValue && configuredAfocal.Value is not 0 and not 1)
            throw new InvalidDataException("AFOC must be zero (focal) or one (afocal).");
        optic.ImageSpaceAfocal = configuredAfocal.HasValue ? configuredAfocal.Value == 1 : document.AfocalImageSpace;
        optic.ReferenceOpd = document.ReferenceOpd;
        ConfigureFields(optic, document, configurationIndex);
        ConfigureWavelengths(optic, document, configurationIndex);
        ApplyCoordinateBreakChiefDecenters(optic, document, configuredSurfaces);
        // Keep the stored prescription when dispersion is unavailable.
        // A ray-based solve must not prevent the rest of the document from importing.
        if (!optic.SurfaceGroup.Items.Any(surface => surface.MaterialAfter is UnresolvedMaterial))
        {
            ApplyCurvatureSolves(optic, configuredSurfaces, document.GlassCatalogs);
            ApplyThicknessSolves(optic, configuredSurfaces, document.GlassCatalogs);
        }
        ConfigureMeritFunction(optic, document, configuredSurfaces);
        return optic;
    }

    private static void InstallConvertedSurfaces(
        Optic optic,
        IReadOnlyList<ConvertedSurface> converted)
    {
        optic.SurfaceGroup.Replace(converted.Select(item => item.Surface));
        foreach (var item in converted)
        {
            var surface = optic.SurfaceGroup.Items[item.Index];
            surface.Geometry = item.Geometry;
            surface.MaterialBefore = item.MaterialBefore;
            surface.MaterialAfter = item.MaterialAfter;
            surface.InteractionModel = item.Interaction.Clone();
            surface.CoordinateSystem = item.CoordinateSystem;
        }
    }

    private static void InstallSemiDiameterPickups(
        Optic optic,
        IReadOnlyList<ZemaxSurface> sourceSurfaces)
    {
        var sourceToImported = sourceSurfaces
            .Where(IsImportedSurface)
            .Select((surface, index) => (surface.Number, index))
            .ToDictionary(item => item.Number, item => item.index);
        foreach (var surface in sourceSurfaces.Where(surface => surface.SemiDiameterSolveCode == 2))
        {
            if (!sourceToImported.TryGetValue(surface.Number, out var target)
                || !sourceToImported.TryGetValue(surface.SemiDiameterPickupSource, out var source)
                || source >= target)
                continue;
            optic.Pickups.SetSemiDiameterPickup(source, target, surface.SemiDiameterPickupScale);
        }
        optic.Pickups.ApplyAll();
    }

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        var sampleLength = Math.Min(bytes.Length, 512);
        var evenZeros = 0;
        var oddZeros = 0;
        for (var index = 0; index < sampleLength; index++)
        {
            if (bytes[index] != 0)
            {
                continue;
            }

            if ((index & 1) == 0)
            {
                evenZeros++;
            }
            else
            {
                oddZeros++;
            }
        }

        if (oddZeros > sampleLength / 8 && oddZeros > evenZeros * 2)
        {
            return Encoding.Unicode.GetString(bytes);
        }

        if (evenZeros > sampleLength / 8 && evenZeros > oddZeros * 2)
        {
            return Encoding.BigEndianUnicode.GetString(bytes);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static ZemaxDocument Parse(string text, string? assetDirectory = null)
    {
        var document = new ZemaxDocument();
        ZemaxSurface? current = null;

        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('!'))
            {
                continue;
            }

            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                continue;
            }

            var command = tokens[0].ToUpperInvariant();
            switch (command)
            {
                case "UNIT":
                    ReadUnit(document, tokens);
                    break;
                case "NAME":
                    document.Name = tokens.Length > 1
                        ? string.Join(" ", tokens.Skip(1)).Trim('"')
                        : document.Name;
                    break;
                case "MODE":
                    if (tokens.Length < 2 || !tokens[1].Equals("SEQ", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new NotSupportedException("Only sequential Zemax ZMX files (MODE SEQ) are supported.");
                    }

                    document.SequentialModeSeen = true;
                    break;
                case "FNUM":
                    ReadFNumber(document, tokens);
                    break;
                case "ENPD":
                    document.UpsertAperture(
                        "EPD",
                        ApertureKind.EntrancePupilDiameter,
                        ScaleLength(document, RequiredDouble(tokens, 1, command)));
                    break;
                case "OBNA":
                    ReadObjectNumericalAperture(document, tokens);
                    break;
                case "FLOA":
                    document.FloatingStop = true;
                    break;
                case "GFAC":
                    var factor = RequiredDouble(tokens, 1, command);
                    var apodizationType = RequiredInt(tokens, 2, command);
                    if (apodizationType is < 0 or > 2)
                    {
                        throw new NotSupportedException($"Zemax GFAC apodization type {apodizationType} is not supported.");
                    }

                    document.Apodization = new ZemaxApodization((ZemaxApodizationType)apodizationType, factor);
                    break;
                case "RAIM":
                    document.RayAimingEnabled = tokens.Length > 2
                        && RequiredInt(tokens, 2, command) != 0;
                    break;
                case "GLRS":
                    document.GlobalReferenceSurfaceNumber = RequiredInt(tokens, 1, command);
                    break;
                case "FTYP":
                    ReadConfiguration(document, tokens);
                    break;
                case "ROPD":
                    var referenceOpd = RequiredInt(tokens, 1, command);
                    if (!Enum.IsDefined(typeof(ReferenceOpdMode), referenceOpd))
                        throw new InvalidDataException("Unknown Zemax ROPD reference.");
                    document.ReferenceOpd = (ReferenceOpdMode)referenceOpd;
                    break;
                case "NSOH":
                    if (current is not null) current.PolygonObjectCount++;
                    if (current is not null && current.Type == "NONSEQCO" && tokens.Length >= 5
                        && tokens[1] == "NSC_POBJ" && tokens[2] == "0" && tokens[3] == "0")
                    {
                        current.PolygonAssetDirectory = assetDirectory;
                        current.PolygonLengthScale = document.LengthScaleToMillimeters;
                        current.PolygonPayload = ReadPolygonAsset(assetDirectory, tokens[4]);
                    }
                    break;
                case "NSOP":
                    if (current?.PolygonPayload is not null)
                    {
                        current.PolygonPose = Enumerable.Range(1, 6).Select(i => RequiredDouble(tokens, i, command)).ToArray();
                        current.PolygonMaterial = tokens[7];
                    }
                    break;
                case "NSOD":
                    if (current?.PolygonPayload is not null && tokens.Length > 2 && tokens[1] == "1")
                        current.PolygonScale = RequiredDouble(tokens, 2, command);
                    if (current?.PolygonPayload is not null && tokens.Length > 2 && tokens[1] == "2")
                        current.PolygonIsVolume = RequiredDouble(tokens, 2, command) != 0;
                    break;
                case "NSCS":
                    if (current?.PolygonPayload is not null && tokens.Length > 3
                        && ((tokens[1] == "1" && tokens[3].Trim('"') is not "_NONE_" and not "")
                            || (tokens[1] == "2" && tokens[3] != "0")))
                        throw new NotSupportedException("Polygon face coating/scattering overrides are not supported.");
                    break;
                case "XFLN":
                    document.FieldX = ReadValues(tokens, 1, document.FieldCount);
                    break;
                case "YFLN":
                    document.FieldY = ReadValues(tokens, 1, document.FieldCount);
                    break;
                case "FWGN":
                    document.FieldWeights = ReadValues(tokens, 1, document.FieldCount);
                    break;
                case "FCOM":
                    ReadFieldComment(document, line, tokens);
                    break;
                case "VCXN":
                    document.VignetteX = ReadValues(tokens, 1, document.FieldCount);
                    break;
                case "VCYN":
                    document.VignetteY = ReadValues(tokens, 1, document.FieldCount);
                    break;
                case "APMN" when tokens.Length <= 2 && current is not null:
                    RequireSurface(current, command).MinimumAperture =
                        Math.Abs(ScaleLength(document, RequiredDouble(tokens, 1, command)));
                    break;
                case "APMX" when tokens.Length <= 2 && current is not null:
                    RequireSurface(current, command).MaximumAperture =
                        Math.Abs(ScaleLength(document, RequiredDouble(tokens, 1, command)));
                    break;
                case "CLAP":
                    var circular = RequireSurface(current, command);
                    circular.CircularAperture = true;
                    circular.CircularObscuration = false;
                    circular.MinimumAperture = Math.Abs(ScaleLength(document, RequiredDouble(tokens, 1, command)));
                    circular.MaximumAperture = Math.Abs(ScaleLength(document, RequiredDouble(tokens, 2, command)));
                    break;
                case "OBSC":
                    var obscuration = RequireSurface(current, command);
                    obscuration.CircularObscuration = true;
                    obscuration.CircularAperture = false;
                    obscuration.MinimumAperture = ScaleLength(document, RequiredDouble(tokens, 1, command));
                    obscuration.MaximumAperture = ScaleLength(document, RequiredDouble(tokens, 2, command));
                    break;
                case "OBDC":
                    var apertureOffset = RequireSurface(current, command);
                    apertureOffset.ApertureOffsetX = ScaleLength(document, RequiredDouble(tokens, 1, command));
                    apertureOffset.ApertureOffsetY = ScaleLength(document, RequiredDouble(tokens, 2, command));
                    break;
                case "VDXN":
                    document.VignetteDecenterX = ReadValues(tokens, 1, document.FieldCount);
                    break;
                case "VDYN":
                    document.VignetteDecenterY = ReadValues(tokens, 1, document.FieldCount);
                    break;
                case "VANN":
                    document.VignetteAngle = ReadValues(tokens, 1, document.FieldCount);
                    break;
                case "WAVM":
                    ReadWavelength(document, tokens);
                    break;
                case "PWAV":
                    document.PrimaryWavelengthIndex = RequiredInt(tokens, 1, command) - 1;
                    break;
                case "DMFS":
                    document.MeritOperands.Add(new MeritOperandDefinition
                    {
                        Enabled = false,
                        Type = "DMFS"
                    });
                    break;
                case "BLNK":
                    document.MeritOperands.Add(new MeritOperandDefinition
                    {
                        Enabled = false,
                        Type = "BLNK",
                        Comment = line.Length > command.Length
                            ? line[command.Length..].Trim().Trim('"')
                            : string.Empty
                    });
                    break;
                case "OPDX":
                case "OPDM":
                case "OPDC":
                case "TRAC":
                case "TRAR":
                case "TRCX":
                case "TRCY":
                case "TRAX":
                case "TRAY":
                case "ANAC":
                case "ANAR":
                case "ANCX":
                case "ANCY":
                case "ANAX":
                case "ANAY":
                case "REAX":
                case "REAY":
                case "REAR":
                case "RANG":
                    ReadPupilRayMeritOperand(document, tokens, command);
                    break;
                case "RSCE":
                case "RSCH":
                case "RSRE":
                case "RSRH":
                    ReadRmsSpotMeritOperand(document, tokens, command);
                    break;
                case "CENX":
                case "CENY":
                case "MECS":
                case "MECT":
                case "EFFL":
                case "EFLX":
                case "EFLY":
                case "ENPP":
                case "EPDI":
                case "EXPP":
                case "EXPD":
                case "ISNA":
                case "ISFN":
                    ReadStandardMeritOperand(document, tokens, command);
                    break;
                // These native parameter layouts still require captured-file validation.
                case "RRET":
                case "CMGT":
                case "CMLT":
                case "CMVA":
                case "CIGT":
                case "CILT":
                case "CIVA":
                case "CEGT":
                case "CELT":
                case "CEVA":
                case "CODA":
                case "HYLD":
                case "DLTN":
                case "GRMN":
                case "GRMX":
                case "I1GT":
                case "I1LT":
                case "I1VA":
                case "I2GT":
                case "I2LT":
                case "I2VA":
                case "I3GT":
                case "I3LT":
                case "I3VA":
                case "I4GT":
                case "I4LT":
                case "I4VA":
                case "I5GT":
                case "I5LT":
                case "I5VA":
                case "I6GT":
                case "I6LT":
                case "I6VA":
                case "BFSD":
                case "TSAG":
                case "RELI":
                case "EFNO":
                case "DIST":
                case "DISA":
                case "MECA":
                case "ZERN":
                case "FDMO":
                case "FDRE":
                case "PRIM":
                case "SVIG":
                case "IMSF":
                case "SPHS":
                case "PSLP":
                case "DPHS":
                case "QSLP":
                case "DENC":
                case "DENF":
                case "SSAG":
                case "SSLP":
                case "SCRV":
                case "GENC":
                case "GENF":
                case "ERFP":
                case "VOLU":
                case "TMAS":
                case "DSAG":
                case "DSLP":
                case "DCRV":
                // The native surface TCE record has not been validated against a capture.
                case "TCVA":
                case "TCGT":
                case "TCLT":
                    ReadPreservedMeritOperand(document, tokens, command);
                    break;
                case "MTHA":
                case "MTHS":
                case "MTHT":
                case "MTHN":
                case "MTHX":
                case "MNRE":
                case "MNRI":
                case "MXRE":
                case "MXRI":
                    // Native extended MFE text layout has not been validated against a capture.
                    // Keep the exact record instead of silently losing the seventh parameter.
                    ReadPreservedMeritOperand(document, tokens, command);
                    break;
                case "TFNO":
                case "SFNO":
                case "WFNO":
                case "WLEN":
                case "INDX":
                case "POWR":
                case "PMAG":
                case "PETZ":
                    ReadStandardMeritOperand(document, tokens, command);
                    break;
                case "TOTR":
                    ReadSystemMeritOperand(document, tokens, command);
                    break;
                case "TTHI":
                case "TGTH":
                    ReadRangeThicknessMeritOperand(document, tokens, command);
                    break;
                case "ETGT":
                case "ETLT":
                case "ETVA":
                case "FTGT":
                case "FTLT":
                case "STHI":
                case "TTGT":
                case "TTLT":
                case "TTVA":
                    ReadSurfaceBoundaryMeritOperand(document, tokens, command);
                    break;
                case "CTGT":
                case "CTLT":
                case "CTVA":
                case "CVGT":
                case "CVLT":
                case "CVVA":
                case "COGT":
                case "COLT":
                case "COVA":
                    ReadSurfaceBoundaryMeritOperand(document, tokens, command);
                    break;
                case "MNCA":
                case "MXCA":
                case "MNEA":
                case "MXEA":
                case "MNCG":
                case "MXCG":
                case "MNEG":
                case "MXEG":
                case "MNCT":
                case "MXCT":
                case "MNET":
                case "MXET":
                case "MNCV":
                case "MXCV":
                case "MNSD":
                case "MXSD":
                case "MNIN":
                case "MXIN":
                case "MNAB":
                case "MXAB":
                case "XNEA":
                case "XXEA":
                case "XNEG":
                case "XXEG":
                case "XNET":
                case "XXET":
                    ReadRangeThicknessMeritOperand(document, tokens, command);
                    break;
                case "CONS":
                case "SINE":
                case "COSI":
                case "TANG":
                case "ASIN":
                case "ACOS":
                case "ATAN":
                case "ABSO":
                case "SQRT":
                case "RECI":
                case "LOGE":
                case "LOGT":
                case "SUMM":
                case "PROB":
                case "PROD":
                case "DIVB":
                case "DIVI":
                case "DIFF":
                case "EQUA":
                case "MAXX":
                case "MINN":
                case "OSUM":
                case "QSUM":
                case "OPGT":
                case "OPLT":
                case "ABGT":
                case "ABLT":
                case "OPVA":
                    ReadMathMeritOperand(document, tokens, command);
                    break;
                case "MCOV":
                case "MCOG":
                case "MCOL":
                case "CONF":
                case "ZTHI":
                    ReadPreservedMeritOperand(document, tokens, command);
                    break;
                case "MNUM":
                    document.ConfigurationCount = CheckedConfigurationCount(
                        RequiredInt(tokens, 1, command),
                        command);
                    break;
                case "THIC":
                case "CRVT":
                case "APER":
                case "APMN":
                case "APMX":
                case "XFIE":
                case "YFIE":
                case "WAVE":
                case "WLWT":
                case "GLSS":
                case "STPS":
                case "PRAM":
                case "EDVA":
                case "NCOM":
                case "AFOC":
                    ReadConfigurationOperand(document, tokens, command);
                    break;
                case "GCAT":
                    document.GlassCatalogs.AddRange(tokens.Skip(1));
                    break;
                case "SURF":
                    current = new ZemaxSurface(RequiredInt(tokens, 1, command), document.LengthScaleToMillimeters, assetDirectory);
                    document.Surfaces.Add(current);
                    break;
                case "TYPE":
                    var typedSurface = RequireSurface(current, command);
                    typedSurface.Type = RequiredToken(tokens, 1, command).ToUpperInvariant();
                    typedSurface.TypeArguments = string.Join(" ", tokens.Skip(2));
                    break;
                case "PARM":
                    var parameterNumber = RequiredInt(tokens, 1, command);
                    if (parameterNumber < 0)
                    {
                        throw new InvalidDataException("Zemax PARM indices must be non-negative integers.");
                    }

                    // Some native surface types use PARM 0. Keep it separately
                    // from the zero-based PARM 1..N geometry coefficients.
                    var parameterIndex = parameterNumber - 1;
                    var parameterSurface = RequireSurface(current, command);
                    if (parameterNumber >= 13 && parameterSurface.Type is "FZERNSAG" or "CHEBYSHV" or "BINARY_2" or "BINARY_3" or "BIRE__IN")
                    {
                        parameterSurface.ExtraData[parameterNumber - 12] = ScaleSurfaceExtraData(document,
                            parameterSurface.Type, parameterNumber - 12, RequiredDouble(tokens, 2, command));
                        break;
                    }
                    parameterSurface.Parameters[parameterIndex] = ScaleSurfaceParameter(
                        document,
                        parameterSurface.Type,
                        parameterIndex,
                        RequiredDouble(tokens, 2, command), parameterSurface.TypeArguments);
                    break;
                case "XDAT":
                    var extraIndex = RequiredInt(tokens, 1, command);
                    if (extraIndex < 1) throw new InvalidDataException("Zemax XDAT indices must be positive integers.");
                    var extraSurface = RequireSurface(current, command);
                    var extraValue = RequiredDouble(tokens, 2, command);
                    extraSurface.ExtraData[extraIndex] = ScaleSurfaceExtraData(document, extraSurface.Type, extraIndex, extraValue);
                    break;
                case "GDAT":
                    var gridSurface = RequireSurface(current, command);
                    if (tokens.Length != 11) throw new InvalidDataException("Grid sag requires its complete GDAT header.");
                    gridSurface.GridSagHeader = Enumerable.Range(1, 10).Select(i => RequiredDouble(tokens, i, command)).ToArray();
                    var gridSize = gridSurface.GridSagHeader[0] * gridSurface.GridSagHeader[1];
                    if (gridSize < 25 || gridSize > 100_000) throw new InvalidDataException("Grid sag data exceeds the bounded grid size.");
                    break;
                case "GARR":
                    var gridDataSurface = RequireSurface(current, command); var gridIndex = RequiredInt(tokens, 1, command);
                    if (gridIndex < 0 || gridIndex >= 100_000 || gridDataSurface.GridSagPoints.ContainsKey(gridIndex))
                        throw new InvalidDataException("Invalid or duplicate grid sag point.");
                    var noData = RequiredInt(tokens, 6, command);
                    if (noData is not (0 or 1)) throw new InvalidDataException("Invalid grid sag nodata flag.");
                    double GridValue(int column) { var value = RequiredDouble(tokens, column, command); return double.IsFinite(value) ? value : 0; }
                    gridDataSurface.GridSagPoints[gridIndex] = new(GridValue(2), GridValue(3), GridValue(4), GridValue(5), noData == 1);
                    break;
                case "CURV":
                    var curvature = RequiredDouble(tokens, 1, command);
                    var curvedSurface = RequireSurface(current, command);
                    curvedSurface.Radius = RadiusFromCurvature(
                        curvature,
                        document.LengthScaleToMillimeters);
                    if (tokens.Length > 3 && RequiredInt(tokens, 2, command) is 2 or 11)
                        curvedSurface.CurvatureSolve = new ZemaxCurvatureSolve(
                            RequiredInt(tokens, 2, command), RequiredDouble(tokens, 3, command));
                    if (tokens.Length > 4 && RequiredInt(tokens, 2, command) == 4)
                    {
                        if (tokens.Length > 5 && RequiredInt(tokens, 5, command) != 0)
                            throw new NotSupportedException("Cross-column curvature pickup is not mapped.");
                        curvedSurface.CurvaturePickup = new(RequiredInt(tokens, 3, command), RequiredDouble(tokens, 4, command));
                    }
                    break;
                case "DISZ":
                    RequireSurface(current, command).Thickness = RequiredDistance(document, tokens, 1, command);
                    break;
                case "PZUP":
                    if (tokens.Length > 4 && RequiredInt(tokens, 4, command) != 0)
                        throw new NotSupportedException("Cross-column thickness pickup is not mapped.");
                    RequireSurface(current, command).ThicknessPickup = new(
                        RequiredInt(tokens, 1, command), RequiredDouble(tokens, 2, command),
                        ScaleLength(document, RequiredDouble(tokens, 3, command)));
                    break;
                case "MAZH":
                    RequireSurface(current, command).MarginalRayHeightSolve = new ZemaxMarginalRayHeightSolve(
                        RequiredDouble(tokens, 1, command),
                        tokens.Length > 2 ? RequiredDouble(tokens, 2, command) : 0);
                    break;
                case "CONI":
                    RequireSurface(current, command).Conic = RequiredDouble(tokens, 1, command);
                    break;
                case "GLAS":
                    ReadGlass(RequireSurface(current, command), tokens);
                    break;
                case "STOP":
                    RequireSurface(current, command).IsStop = true;
                    break;
                case "MIRR":
                    if (tokens.Length == 1)
                    {
                        RequireSurface(current, command).IsMirror = true;
                        RequireSurface(current, command).ExplicitMirror = true;
                    }
                    break;
                case "PPAR":
                    var pickupSurface = RequireSurface(current, command);
                    var targetParameter = RequiredInt(tokens, 1, command) - 1;
                    if (targetParameter < 0) throw new InvalidDataException("PPAR requires a positive parameter number.");
                    pickupSurface.ParameterPickups[targetParameter] = new(
                        RequiredInt(tokens, 2, command), RequiredDouble(tokens, 3, command),
                        RequiredDouble(tokens, 4, command), tokens.Length > 5 ? RequiredInt(tokens, 5, command) : 0);
                    break;
                case "DIAM":
                    ReadSemiDiameter(document, RequireSurface(current, command), tokens);
                    break;
                case "FLAP":
                    RequireSurface(current, command).FloatingAperture = true;
                    break;
                case "MEMA":
                    ReadMechanicalSemiDiameter(document, RequireSurface(current, command), tokens);
                    break;
                case "COMM":
                    RequireSurface(current, command).Comment = tokens.Length > 1
                        ? string.Join(" ", tokens.Skip(1)).Trim('"')
                        : string.Empty;
                    break;
                default:
                    if (ZemaxOperandRegistry.TryGet(command, out var operandDescriptor))
                    {
                        if (operandDescriptor.SupportLevel == ZemaxOperandSupportLevel.Executable)
                            ReadSystemMeritOperand(document, tokens, command);
                        else
                            ReadPreservedMeritOperand(document, tokens, command);
                    }
                    break;
            }
        }

        return document;
    }

    private static void Validate(ZemaxDocument document)
    {
        if (!document.SequentialModeSeen)
        {
            throw new InvalidDataException("The Zemax document does not declare MODE SEQ.");
        }

        if (document.Apertures.Count == 0 && !document.FloatingStop)
        {
            throw new InvalidDataException("The Zemax document does not define a supported system aperture.");
        }

        if (document.Surfaces.Count(surface => surface.Type != "COORDBRK") < 2)
        {
            throw new InvalidDataException("A Zemax document must contain at least object and image surfaces.");
        }

        var stopCount = document.Surfaces.Count(surface => surface.IsStop);
        if (stopCount > 1)
        {
            throw new InvalidDataException("A Zemax document may contain only one aperture stop.");
        }
    }

    private static IReadOnlyList<ZemaxSurface> ConfigureSurfaces(
        ZemaxDocument document,
        int configurationIndex)
    {
        var configuredStop = document.ConfigurationDouble("STPS", 0, configurationIndex);
        var configuredSurfaces = document.Surfaces.Select(source =>
        {
            var material = document.ConfigurationText("GLSS", source.Number, configurationIndex)
                ?? source.Material;
            var configured = new ZemaxSurface(source.Number, source.LengthScaleToMillimeters, source.AssetDirectory)
            {
                Type = source.Type,
                TypeArguments = source.TypeArguments,
                PolygonAssetDirectory = source.PolygonAssetDirectory,
                PolygonObjectCount = source.PolygonObjectCount,
                PolygonIsVolume = source.PolygonIsVolume,
                PolygonLengthScale = source.PolygonLengthScale,
                PolygonPayload = document.ConfigurationText("NCOM", source.Number, configurationIndex) is { } polygonName
                    ? ReadPolygonAsset(source.PolygonAssetDirectory, polygonName)
                    : source.PolygonPayload,
                PolygonPose = source.PolygonPose,
                PolygonMaterial = source.PolygonMaterial,
                PolygonScale = source.PolygonScale,
                RetainReferencePlane = source.Type == "COORDBRK" && (ScaleConfiguredDistance(document,
                    document.ConfigurationDouble("THIC", source.Number, configurationIndex), source.Thickness) != 0
                    || document.MeritOperands.Any(operand =>
                    !operand.CompatibilityOnly && operand.Surface == source.Number
                    && operand.Type is "REAX" or "REAY" or "REAR" or "REAZ" or "REAA" or "REAB" or "REAC")),
                Comment = source.Comment,
                Radius = RadiusFromConfiguredCurvature(
                    document,
                    document.ConfigurationDouble("CRVT", source.Number, configurationIndex),
                    source.Radius),
                Thickness = ScaleConfiguredDistance(
                    document,
                    document.ConfigurationDouble(
                    "THIC",
                    source.Number,
                    configurationIndex),
                    source.Thickness),
                Conic = source.Conic,
                Material = string.IsNullOrWhiteSpace(material) ? "Air" : material,
                RefractiveIndex = source.RefractiveIndex,
                AbbeNumber = source.AbbeNumber,
                SemiDiameter = source.CircularAperture ? ScaleConfiguredLength(
                    document,
                    document.ConfigurationDouble(
                    "APMX",
                    source.Number,
                    configurationIndex),
                    source.SemiDiameter) : source.SemiDiameter,
                CircularAperture = source.CircularAperture,
                CircularObscuration = source.CircularObscuration,
                ApertureOffsetX = ScaleConfiguredLength(document, document.ConfigurationDouble("APDX", source.Number, configurationIndex), source.ApertureOffsetX) ?? 0,
                ApertureOffsetY = ScaleConfiguredLength(document, document.ConfigurationDouble("APDY", source.Number, configurationIndex), source.ApertureOffsetY) ?? 0,
                MaximumAperture = ScaleConfiguredOptionalLength(document,
                    document.ConfigurationDouble("APMX", source.Number, configurationIndex), source.MaximumAperture),
                MinimumAperture = ScaleConfiguredOptionalLength(
                    document,
                    document.ConfigurationDouble(
                    "APMN",
                    source.Number,
                    configurationIndex),
                    source.MinimumAperture),
                SemiDiameterFixed = source.SemiDiameterFixed,
                FloatingAperture = source.FloatingAperture,
                SemiDiameterSolveCode = source.SemiDiameterSolveCode,
                SemiDiameterPickupSource = source.SemiDiameterPickupSource,
                SemiDiameterPickupScale = source.SemiDiameterPickupScale,
                MechanicalSemiDiameter = source.MechanicalSemiDiameter,
                MechanicalSemiDiameterSolveCode = source.MechanicalSemiDiameterSolveCode,
                IsStop = configuredStop.HasValue
                    ? source.Number == (int)Math.Round(configuredStop.Value)
                    : source.IsStop,
                IsMirror = source.ExplicitMirror || material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase),
                ExplicitMirror = source.ExplicitMirror
            };
            configured.MarginalRayHeightSolve = source.MarginalRayHeightSolve;
            configured.CurvatureSolve = source.CurvatureSolve;
            configured.CurvaturePickup = source.CurvaturePickup;
            configured.ThicknessPickup = source.ThicknessPickup;
            configured.MaterialPickupSource = source.MaterialPickupSource;
            foreach (var parameter in source.Parameters)
            {
                configured.Parameters[parameter.Key] = parameter.Value;
            }
            foreach (var extra in source.ExtraData) configured.ExtraData[extra.Key] = extra.Value;
            foreach (var operand in document.ConfigurationValues("EDVA", source.Number, configurationIndex))
                if (operand.AuxiliaryIndex >= 0 && TryParseDouble(operand.Value, out var extraValue))
                {
                    var index = operand.AuxiliaryIndex + 1;
                    configured.ExtraData[index] = ScaleSurfaceExtraData(document, configured.Type, index, extraValue);
                }
            configured.GridSagHeader = source.GridSagHeader?.ToArray();
            foreach (var point in source.GridSagPoints) configured.GridSagPoints[point.Key] = point.Value;

            foreach (var operand in document.ConfigurationValues(
                         "PRAM",
                         source.Number,
                         configurationIndex))
            {
                if (operand.AuxiliaryIndex >= 0 && TryParseDouble(operand.Value, out var value))
                {
                    if (operand.AuxiliaryIndex >= 12 && configured.Type is "FZERNSAG" or "CHEBYSHV" or "BINARY_2" or "BINARY_3" or "BIRE__IN")
                    {
                        var index = operand.AuxiliaryIndex - 11;
                        configured.ExtraData[index] = ScaleSurfaceExtraData(document, configured.Type, index, value);
                        continue;
                    }
                    configured.Parameters[operand.AuxiliaryIndex] = ScaleSurfaceParameter(
                        document,
                        configured.Type,
                        operand.AuxiliaryIndex,
                        value, configured.TypeArguments);
                }
            }

            return configured;
        }).ToArray();
        ApplyParameterPickups(document, configuredSurfaces);
        ApplyCurvaturePickups(configuredSurfaces);
        ApplyThicknessAndMaterialPickups(configuredSurfaces);
        return configuredSurfaces;
    }

    private static void ApplyThicknessAndMaterialPickups(IReadOnlyList<ZemaxSurface> surfaces)
    {
        var byNumber = surfaces.ToDictionary(s => s.Number);
        var thicknessDone = new HashSet<int>(); var thicknessActive = new HashSet<int>();
        var materialDone = new HashSet<int>(); var materialActive = new HashSet<int>();
        double Thickness(int number)
        {
            if (!byNumber.TryGetValue(number, out var surface)) throw new InvalidDataException("Missing thickness pickup source.");
            if (thicknessDone.Contains(number) || surface.ThicknessPickup is not { } pickup) return surface.Thickness;
            if (!thicknessActive.Add(number)) throw new InvalidDataException("Cyclic Zemax thickness pickup.");
            var value = pickup.Offset + pickup.Scale * Thickness(pickup.SourceSurface);
            if (!double.IsFinite(value)) throw new InvalidDataException("Nonfinite Zemax thickness pickup.");
            surface.Thickness = value;
            thicknessActive.Remove(number); thicknessDone.Add(number); return value;
        }
        ZemaxSurface Material(int number)
        {
            if (!byNumber.TryGetValue(number, out var surface)) throw new InvalidDataException("Missing material pickup source.");
            if (materialDone.Contains(number) || surface.MaterialPickupSource is not { } sourceNumber) return surface;
            if (!materialActive.Add(number)) throw new InvalidDataException("Cyclic Zemax material pickup.");
            var source = Material(sourceNumber);
            surface.Material = source.Material; surface.RefractiveIndex = source.RefractiveIndex; surface.AbbeNumber = source.AbbeNumber;
            surface.IsMirror = surface.ExplicitMirror || source.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
            materialActive.Remove(number); materialDone.Add(number); return surface;
        }
        foreach (var surface in surfaces) { Thickness(surface.Number); Material(surface.Number); }
    }

    private static void InstallThicknessPickups(Optic optic, IReadOnlyList<ZemaxSurface> surfaces)
    {
        var map = surfaces.Where(IsImportedSurface).Select((s, i) => (s.Number, Index: i)).ToDictionary(p => p.Number, p => p.Index);
        foreach (var s in surfaces.Where(s => s.ThicknessPickup is not null))
            if (map.TryGetValue(s.Number, out var target) && map.TryGetValue(s.ThicknessPickup!.SourceSurface, out var source))
                optic.Pickups.SetThicknessPickup(source, target, s.ThicknessPickup.Scale, s.ThicknessPickup.Offset);
        optic.Pickups.ApplyAll();
    }

    private static void ApplyParameterPickups(ZemaxDocument document, IReadOnlyList<ZemaxSurface> configured)
    {
        var byNumber = configured.ToDictionary(s => s.Number);
        var original = document.Surfaces.ToDictionary(s => s.Number);
        var done = new HashSet<(int Surface, int Parameter)>();
        var active = new HashSet<(int Surface, int Parameter)>();
        bool Extra(ZemaxSurface s, int p) => p >= 12 && s.Type is "FZERNSAG" or "CHEBYSHV" or "BINARY_2" or "BINARY_3" or "BIRE__IN";
        double Get(ZemaxSurface s, int p) => Extra(s, p) ? s.ExtraData.GetValueOrDefault(p - 11) : s.Parameters.GetValueOrDefault(p);
        double Scale(ZemaxSurface s, int p, double v) => Extra(s, p)
            ? ScaleSurfaceExtraData(document, s.Type, p - 11, v) : ScaleSurfaceParameter(document, s.Type, p, v, s.TypeArguments);
        double Resolve(int surface, int parameter)
        {
            var key = (surface, parameter);
            var target = byNumber[surface];
            if (done.Contains(key) || !original[surface].ParameterPickups.TryGetValue(parameter, out var pickup) || pickup.SourceSurface < 0)
                return Get(target, parameter);
            if (!active.Add(key)) throw new InvalidDataException("Cyclic Zemax parameter pickup.");
            if (!byNumber.TryGetValue(pickup.SourceSurface, out var source)) throw new InvalidDataException("Missing Zemax parameter pickup source.");
            var sourceParameter = pickup.SourceParameter == 0 ? parameter : pickup.SourceParameter - 1;
            var sourceScale = Scale(source, sourceParameter, 1);
            var raw = Resolve(pickup.SourceSurface, sourceParameter) / sourceScale;
            var value = Scale(target, parameter, raw * pickup.Scale + pickup.Offset);
            if (!double.IsFinite(value)) throw new InvalidDataException("Nonfinite Zemax parameter pickup.");
            if (Extra(target, parameter)) target.ExtraData[parameter - 11] = value;
            else target.Parameters[parameter] = value;
            active.Remove(key); done.Add(key); return value;
        }
        foreach (var source in document.Surfaces)
            foreach (var parameter in source.ParameterPickups.Keys) Resolve(source.Number, parameter);
    }

    private static void ApplyCoordinateBreakChiefDecenters(Optic optic, ZemaxDocument document,
        IReadOnlyList<ZemaxSurface> surfaces)
    {
        var original = document.Surfaces.ToDictionary(s => s.Number);
        var origin = Vector3D.Zero; var rotation = Matrix3x3.Identity; var previous = -1;
        foreach (var surface in surfaces)
        {
            if (surface.Type == "COORDBRK")
            {
                foreach (var (parameter, solve) in original[surface.Number].ParameterPickups)
                {
                    if (solve.SourceSurface != -1 || parameter is not (0 or 1)) continue;
                    var field = checked((int)solve.Scale) - 1;
                    var wave = checked((int)solve.Offset) - 1;
                    if (previous < 0 || field < 0 || field >= optic.Fields.Count || wave >= optic.Wavelengths.Count)
                        throw new InvalidDataException("Invalid coordinate-break chief-ray solve field or wavelength.");
                    var wavelength = wave < 0 ? optic.Wavelengths.First(w => w.IsPrimary) : optic.Wavelengths[wave];
                    var source = optic.SequentialRayTracer.RayGenerator.GenerateAtPhysicalField(
                        optic.Fields[field].X, optic.Fields[field].Y, 0, 0, wavelength.Micrometers, optic.RayAimingEnabled).Rays.Single();
                    var sample = optic.SequentialRayTracer.TraceToSurface(source, previous);
                    if (sample is null || sample.Vignetted) throw new InvalidDataException("Chief ray cannot reach coordinate-break solve.");
                    // Decenters use the incoming frame for order 0, the rotated
                    // frame for order 1. In a coincident grazing frame retain
                    // the incident point instead of dividing roundoff by zero.
                    var frame = surface.Parameter(5) == 0 ? rotation : rotation * RotationMatrix(
                        -surface.Parameter(2), -surface.Parameter(3), -surface.Parameter(4)).Transpose();
                    var point = frame.Transpose().Transform(sample.Position - origin);
                    var direction = frame.Transpose().Transform(sample.Direction);
                    if (Math.Abs(direction.Z) > 1e-12) point -= direction * (point.Z / direction.Z);
                    else if (Math.Abs(point.Z) > 1e-8) throw new InvalidDataException("Chief ray is parallel to coordinate-break solve plane.");
                    surface.Parameters[parameter] = parameter == 0 ? point.X : point.Y;
                    ApplyParameterPickups(document, surfaces);
                    InstallConvertedSurfaces(optic, ConvertSurfaces(optic, surfaces, document.GlassCatalogs));
                    optic.Pickups.ApplyAll();
                }
                ApplyCoordinateBreak(surface, ref origin, ref rotation);
                if (surface.RetainReferencePlane) previous++;
            }
            else
            {
                previous++;
                if (previous > 0 && !double.IsInfinity(surface.Thickness)) origin += rotation.Transform(new Vector3D(0, 0, surface.Thickness));
            }
        }
    }

    private static void ApplyCurvaturePickups(IReadOnlyList<ZemaxSurface> surfaces)
    {
        var byNumber = surfaces.ToDictionary(s => s.Number);
        var active = new HashSet<int>(); var done = new HashSet<int>();
        double Resolve(int number)
        {
            if (!byNumber.TryGetValue(number, out var surface)) throw new InvalidDataException("Missing curvature pickup source.");
            if (done.Contains(number) || surface.CurvaturePickup is not { } pickup) return surface.Radius;
            if (!active.Add(number)) throw new InvalidDataException("Cyclic Zemax curvature pickup.");
            if (!double.IsFinite(pickup.Scale)) throw new InvalidDataException("Invalid curvature pickup scale.");
            var radius = Resolve(pickup.SourceSurface);
            surface.Radius = pickup.Scale == 0 || radius == 0 || double.IsInfinity(radius) ? double.PositiveInfinity : radius / pickup.Scale;
            active.Remove(number); done.Add(number); return surface.Radius;
        }
        foreach (var surface in surfaces) Resolve(surface.Number);
    }

    private static void InstallCurvaturePickups(Optic optic, IReadOnlyList<ZemaxSurface> surfaces)
    {
        var map = surfaces.Where(IsImportedSurface).Select((s, i) => (s.Number, Index: i)).ToDictionary(p => p.Number, p => p.Index);
        foreach (var s in surfaces.Where(s => s.CurvaturePickup is not null))
            if (map.TryGetValue(s.Number, out var target) && map.TryGetValue(s.CurvaturePickup!.SourceSurface, out var source))
                optic.Pickups.SetCurvaturePickup(source, target, s.CurvaturePickup.Scale);
        optic.Pickups.ApplyAll();
    }

    private static int DetectActiveConfiguration(ZemaxDocument document)
    {
        // Plain sequential ZMX exports do not provide a reliable persisted UI-active
        // multi-configuration selection. Keep imports deterministic and explainable
        // by selecting configuration 1 instead of guessing from operand values.
        return 0;
    }

    private static bool IsImportedSurface(ZemaxSurface surface) =>
        surface.Type != "COORDBRK" || surface.RetainReferencePlane;

    private static IReadOnlyList<ConvertedSurface> ConvertSurfaces(
        Optic optic,
        IReadOnlyList<ZemaxSurface> sourceSurfaces,
        IReadOnlyList<string> glassCatalogs)
    {
        var result = new List<ConvertedSurface>();
        var origin = Vector3D.Zero;
        var rotation = Matrix3x3.Identity;
        IMaterial previousMaterial = optic.Materials.Resolve("Air");

        foreach (var source in sourceSurfaces)
        {
            var incomingRotation = rotation;
            if (source.Type == "COORDBRK")
            {
                ApplyCoordinateBreak(source, ref origin, ref rotation, !source.RetainReferencePlane);
                if (!source.RetainReferencePlane) continue;
            }

            if (source.Type == "NONSEQCO" && source.PolygonPayload is not null)
            {
                if (source.PolygonLengthScale != 1)
                    throw new NotSupportedException("Executable polygon groups currently require millimeter lens units; other units remain unsupported rather than being interpreted as millimeters.");
                origin += rotation.Transform(new Vector3D(source.Parameter(0), source.Parameter(1), source.Parameter(2)));
                rotation *= RotationMatrix(source.Parameter(3), source.Parameter(4), source.Parameter(5));
            }
            var index = result.Count;
            var surfaceOrigin = index == 0 ? ObjectSurfaceOrigin(source.Thickness) : origin;
            var coordinate = CoordinateFrom(surfaceOrigin, rotation);
            source.CollimatedParaxialMode = source.Type == "PARAXIAL" && source.Parameter(1) == 0
                && ParaxialPhaseRules.IsPlaneWavePrefix(result.Select(s => s.Surface).ToArray());
            // A referenced coordinate break is transparent at its transformed plane.
            var referencePlane = source.RetainReferencePlane;
            var geometry = source.Type == "NONSEQCO" && source.PolygonPayload is not null
                ? CreatePolygonGroup(source, optic, glassCatalogs)
                : referencePlane ? new PlaneGeometry(rotation.Transpose().Transform(incomingRotation.Transform(new Vector3D(0, 0, 1)))) : CreateGeometry(source);
            var isReflective = !referencePlane && (source.IsMirror || source.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase));
            IInteractionModel interaction = geometry is PlaneGeometry && IsComputableParaxial(source)
                ? new ParaxialLensInteractionModel(source.Parameter(0) == 0 ? 0 : 1 / source.Parameter(0),
                    source.Parameter(0) == 0 ? 0 : 1 / source.Parameter(0), (int)source.Parameter(1))
                : geometry is PlaneGeometry && IsComputableParaxialXY(source)
                    ? new ParaxialLensInteractionModel(source.Parameter(0), source.Parameter(1), 1)
                : IsComputableBirefringent(source)
                    ? new BirefringentInteractionModel(source.Type == "BIRE_OUT", source.Type == "BIRE__IN" ? source.Parameter(-1) : 0)
                : IsComputableGrating(source)
                    ? new RuledGratingInteractionModel(source.Parameter(0), (int)source.Parameter(1), isReflective)
                    : source.Type == "BINARY_2" && geometry is EvenAsphereGeometry
                        ? new DiffractivePhaseInteractionModel(CreateBinary2Phase(source), isReflective)
                    : source.Type == "BINARY_3" && geometry is ConcentricZoneGeometry
                        ? new DiffractivePhaseInteractionModel(CreateBinary3Phase(source), isReflective)
                    : source.Type == "HOLOGRM2" && geometry is PlaneGeometry
                        ? new DiffractivePhaseInteractionModel(CreateHologram2Phase(source), isReflective)
                    : new RefractiveReflectiveInteractionModel(isReflective);
            IMaterial materialAfter;
            if (isReflective || referencePlane)
            {
                materialAfter = previousMaterial.Clone();
            }
            else
            {
                materialAfter = source.Type switch
                {
                    "GRINSUR1" when source.Parameter(3) == 0 => CreateGradient1Material(source),
                    "GRINSUR9" when source.Parameter(1) == 0 && source.Parameter(2) == 0 => CreateGradient9Material(source),
                    "BIRE__IN" when IsComputableBirefringent(source) => CreateBirefringentMaterial(optic, source, glassCatalogs),
                    "GRINSU11" when source.GridGradientProfile is not null => CreateGridGradientMaterial(source),
                    _ => ResolveGlass(optic, source, glassCatalogs)
                };
            }

            var legacyRadius = referencePlane || double.IsInfinity(source.Radius)
                || geometry is TiltedPlaneGeometry or TernaryMirrorArrayGeometry ? 0 : source.Radius;
            var thickness = source.Thickness;
            var semiDiameter = source.SemiDiameter is { } configuredSemiDiameter
                && double.IsFinite(configuredSemiDiameter)
                    ? Math.Max(0.1, Math.Abs(configuredSemiDiameter))
                    : 10;
            // Curvature alone does not make a refracting surface: curved dummy
            // surfaces can carry a fixed DIAM without defining a floating stop.
            var hasOpticalInteraction = isReflective || !string.Equals(previousMaterial.Name, materialAfter.Name,
                StringComparison.OrdinalIgnoreCase);
            var hasOpticalPower = !referencePlane && HasSurfacePower(source)
                && (hasOpticalInteraction || interaction is ParaxialLensInteractionModel);
            var physicalAperture = referencePlane ? null : CreatePhysicalAperture(source, semiDiameter,
                hasOpticalPower || source.IsStop && hasOpticalInteraction);
            var surface = new OpticalSurface
            {
                Number = index,
                ThermalExpansionPpmPerC = null,
                Label = SurfaceLabel(source, index, sourceSurfaces),
                Radius = legacyRadius,
                Thickness = thickness,
                Material = isReflective ? "MIRROR" : materialAfter.Name,
                SemiDiameter = semiDiameter,
                MechanicalSemiDiameterSolveCode = source.MechanicalSemiDiameterSolveCode,
                SemiDiameterFixed = source.SemiDiameterFixed,
                Conic = referencePlane || geometry is TiltedPlaneGeometry or TernaryMirrorArrayGeometry or ChebyshevSagGeometry ? 0 : source.Conic,
                IsStop = source.IsStop,
                IsReflective = isReflective,
                Geometry = geometry,
                MaterialBefore = previousMaterial.Clone(),
                MaterialAfter = materialAfter.Clone(),
                InteractionModel = interaction,
                PhysicalAperture = physicalAperture,
                SemiDiameterDefinesPhysicalAperture = physicalAperture is CircularAperture && !source.CircularAperture
                    && !source.IsStop
                    && source.SemiDiameterFixed
                    && HasSurfacePower(source),
                CoordinateSystem = coordinate
            };
            if (source.MechanicalSemiDiameter is { } mechanicalSemiDiameter)
                surface.MechanicalSemiDiameter = mechanicalSemiDiameter;
            result.Add(new ConvertedSurface(
                index,
                surface,
                geometry,
                previousMaterial.Clone(),
                materialAfter.Clone(),
                isReflective,
                coordinate, interaction));

            previousMaterial = materialAfter.Clone();
            if (index == 0)
            {
                origin = Vector3D.Zero;
                rotation = Matrix3x3.Identity;
            }
            else if (!double.IsInfinity(source.Thickness))
            {
                origin += rotation.Transform(new Vector3D(0, 0, source.Thickness));
            }
        }

        return result;
    }

    private static IPhysicalAperture? CreatePhysicalAperture(
        ZemaxSurface source, double semiDiameter, bool hasImplicitFloatingAperture)
    {
        if (source.CircularObscuration)
        {
            var inner = source.MinimumAperture ?? 0;
            var outer = source.MaximumAperture ?? throw new NotSupportedException("Missing circular obscuration maximum radius.");
            return new CircularObscurationAperture(outer, inner, source.ApertureOffsetX, source.ApertureOffsetY);
        }
        // APMN/APMX values alone do not activate a surface aperture. Native
        // CurrentType=None retains a full pupil even when these dormant values
        // are present. CLAP is the explicit circular/annular aperture declaration.
        if (source.CircularAperture)
        {
            var inner = source.MinimumAperture ?? 0;
            var outer = source.MaximumAperture ?? semiDiameter;
            if (!double.IsFinite(inner) || !double.IsFinite(outer) || inner < 0 || outer <= 0 || inner > outer)
                throw new NotSupportedException("Invalid explicit Zemax circular aperture bounds.");
            return inner > 0 ? new AnnularAperture(outer, inner) : new CircularAperture(outer);
        }

        // Automatic DIAM is a ray-footprint estimate, including at the stop;
        // it must not become a physical clipping boundary. A fixed DIAM on a
        // powered surface or interacting stop defines a floating aperture. A dummy stop can
        // set the system pupil without defining a surface aperture: native
        // OpticStudio reports ApertureData.CurrentType=None in that case.
        // MEMA only describes the part edge and is deliberately excluded.
        if (source.FloatingAperture || source.SemiDiameterFixed && hasImplicitFloatingAperture)
        {
            return new CircularAperture(semiDiameter);
        }

        return null;
    }

    private static bool HasSurfacePower(ZemaxSurface surface) =>
        (surface.Type == "PARAXIAL" && surface.Parameter(0) != 0)
        || (double.IsFinite(surface.Radius) && Math.Abs(surface.Radius) > 1e-12)
        || (surface.Type.Equals("TOROIDAL", StringComparison.OrdinalIgnoreCase)
            && double.IsFinite(surface.Parameter(1))
            && Math.Abs(surface.Parameter(1)) > 1e-12);

    private static void ApplyCoordinateBreak(
        ZemaxSurface source,
        ref Vector3D origin,
        ref Matrix3x3 rotation,
        bool includeThickness = true)
    {
        var decenter = new Vector3D(source.Parameter(0), source.Parameter(1), 0);
        if (source.Parameter(5) == 0)
        {
            origin += rotation.Transform(decenter);
            rotation *= RotationMatrix(source.Parameter(2), source.Parameter(3), source.Parameter(4));
        }
        else
        {
            rotation *= RotationMatrix(-source.Parameter(2), -source.Parameter(3), -source.Parameter(4)).Transpose();
            origin += rotation.Transform(decenter);
        }
        if (includeThickness && !double.IsInfinity(source.Thickness))
        {
            origin += rotation.Transform(new Vector3D(0, 0, source.Thickness));
        }
    }

    private static IGeometry CreateGeometry(ZemaxSurface surface)
    {
        if (surface.Type is "STANDARD" or "EVENASPH" or "ODDASPHE" or "TOROIDAL"
            && surface.Parameter(-1) != 0)
        {
            throw new NotSupportedException(
                $"Nonzero Zemax PARM 0 on TYPE {surface.Type} is not supported (SURF {surface.Number}).");
        }
        var radius = double.IsInfinity(surface.Radius) ? 0 : surface.Radius;
        return surface.Type switch
        {
            "STANDARD" => Math.Abs(radius) < 1e-15
                ? new PlaneGeometry()
                : new StandardGeometry(radius, surface.Conic),
            "PARAXIAL" when IsComputableParaxial(surface) => new PlaneGeometry(),
            "PARAX_XY" when IsComputableParaxialXY(surface) => new PlaneGeometry(),
            "BIRE__IN" or "BIRE_OUT" when IsComputableBirefringent(surface) => radius == 0 ? new PlaneGeometry() : new StandardGeometry(radius, surface.Conic),
            "DGRATING" when IsComputableGrating(surface) => radius == 0 ? new PlaneGeometry() : new StandardGeometry(radius, surface.Conic),
            "BINARY_2" => CreateBinary2Geometry(surface, radius),
            "BINARY_3" => CreateBinary3Geometry(surface, radius),
            "GRID_SAG" => CreateGridSagGeometry(surface, radius),
            "GRINSU11" => CreateGridGradientGeometry(surface),
            "HOLOGRM2" when radius == 0 && IsComputableHologram2(surface) => new PlaneGeometry(),
            "IRREGULA" when surface.SemiDiameter is > 0
                && !surface.Parameters.Any(p => p.Key is < 0 or > 7 && p.Value != 0)
                && !surface.ExtraData.Any(p => p.Value != 0) => new IrregularGeometry(radius, surface.Conic, surface.SemiDiameter.Value,
                    surface.Parameter(0), surface.Parameter(1), surface.Parameter(2), surface.Parameter(3),
                    surface.Parameter(4), surface.Parameter(5), surface.Parameter(6), surface.Parameter(7)),
            "EVENASPH" => new EvenAsphereGeometry(
                radius,
                surface.Conic,
                Enumerable.Range(0, 8).Select(surface.Parameter).ToArray()),
            "ODDASPHE" => new OddAsphereGeometry(
                radius,
                surface.Conic,
                Enumerable.Range(0, 8).Select(surface.Parameter).ToArray()),
            "TOROIDAL" => CreateToroidalGeometry(surface),
            "TILTSURF" => CreateTiltedGeometry(surface),
            "FZERNSAG" => CreateFringeSagGeometry(surface),
            "CHEBYSHV" => CreateChebyshevSagGeometry(surface),
            "GRINSUR1" when surface.Parameter(3) == 0 => CreateGradient1Geometry(surface, radius),
            "GRINSUR9" when surface.Parameter(1) == 0 && surface.Parameter(2) == 0 => CreateGradient9Geometry(surface, radius),
            "USERSURF" when surface.TypeArguments.Equals("US_MEMS.DLL", StringComparison.OrdinalIgnoreCase) => CreateMirrorArrayGeometry(surface),
            _ => CreateOpaqueZemaxGeometry(surface)
        };
    }

    private static bool IsComputableParaxial(ZemaxSurface surface) => surface.Type == "PARAXIAL"
        && !surface.IsMirror && !surface.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)
        && (surface.Parameter(1) is 1 or 2 || surface.Parameter(1) == 0 && surface.CollimatedParaxialMode)
        && !surface.Parameters.Any(p => p.Key is not (0 or 1) && p.Value != 0)
        && !surface.ExtraData.Any(p => p.Value != 0);

    private static bool IsComputableGrating(ZemaxSurface surface) => surface.Type == "DGRATING"
        && surface.Parameter(1) == Math.Truncate(surface.Parameter(1)) && Math.Abs(surface.Parameter(1)) <= 1_000_000
        && !surface.Parameters.Any(p => p.Key is not (0 or 1) && p.Value != 0)
        && !surface.ExtraData.Any(p => p.Value != 0);

    private static bool IsComputableParaxialXY(ZemaxSurface surface) => surface.Type == "PARAX_XY"
        && !surface.IsMirror && !surface.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)
        && !surface.Parameters.Any(p => p.Key is not (0 or 1) && p.Value != 0)
        && !surface.ExtraData.Any(p => p.Value != 0);

    private static IGeometry CreateBinary2Geometry(ZemaxSurface surface, double radius)
    {
        if (surface.Parameters.Any(p => p.Key is < -1 or > 7 && p.Value != 0)) return CreateOpaqueZemaxGeometry(surface);
        _ = CreateBinary2Phase(surface);
        return new EvenAsphereGeometry(radius, surface.Conic, Enumerable.Range(0, 8).Select(surface.Parameter).ToArray());
    }

    private static IGeometry CreateGridSagGeometry(ZemaxSurface surface, double radius)
    {
        var header = surface.GridSagHeader;
        // The embedded GDAT unit/transform extension has only been verified in
        // millimeter sources with its ordinary 0/0/0/1 flags. Preserve other cases.
        if (surface.LengthScaleToMillimeters != 1 || header is not { Length: 10 }
            || header[6] is not (0 or 1) || header[7] != 0 || header[8] != 0 || header[9] != 1
            || surface.Parameter(-1) is not (0 or 1) || surface.ExtraData.GetValueOrDefault(1) != 0
            || surface.Parameters.Any(p => p.Key is < -1 or > 9 && p.Value != 0)) return CreateOpaqueZemaxGeometry(surface);
        var nx = header[0]; var ny = header[1];
        if (nx != Math.Truncate(nx) || ny != Math.Truncate(ny) || nx is < 5 or > 4096 || ny is < 5 or > 4096
            || nx * ny > 100_000 || surface.GridSagPoints.Count != nx * ny)
            throw new InvalidDataException("Grid sag has incomplete or invalid embedded data.");
        return new GridSagGeometry(radius, surface.Conic, Enumerable.Range(0, 8).Select(surface.Parameter).ToArray(),
            (int)nx, (int)ny, Math.Abs(header[2]), Math.Abs(header[3]), header[4], header[5], header[6] == 1,
            Enumerable.Range(0, (int)(nx * ny)).Select(i => surface.GridSagPoints[i]).ToArray());
    }

    private static NormalizedRadialPhaseProfile CreateBinary2Phase(ZemaxSurface surface)
    {
        var count = surface.ExtraData.GetValueOrDefault(1);
        var order = surface.Parameters.GetValueOrDefault(-1, 1);
        if (count != Math.Truncate(count) || count is < 0 or > 240 || order != Math.Truncate(order) || Math.Abs(order) > 1_000_000)
            throw new InvalidDataException("Invalid Binary 2 phase term count or diffraction order.");
        return new NormalizedRadialPhaseProfile(surface.ExtraData.GetValueOrDefault(2, 1),
            Enumerable.Range(3, (int)count).Select(i => surface.ExtraData.GetValueOrDefault(i)).ToArray(), (int)order);
    }

    private static bool IsComputableHologram2(ZemaxSurface surface) =>
        surface.Parameter(7) == Math.Truncate(surface.Parameter(7)) && Math.Abs(surface.Parameter(7)) <= 1_000_000
        && !surface.Parameters.Any(p => p.Key is < 0 or > 7 && p.Value != 0)
        && !surface.ExtraData.Any(p => p.Value != 0);

    private static TwoPointHologramPhaseProfile CreateHologram2Phase(ZemaxSurface surface) => new(
        new(surface.Parameter(0), surface.Parameter(1), surface.Parameter(2)),
        new(surface.Parameter(3), surface.Parameter(4), surface.Parameter(5)), surface.Parameter(6) * 1000, (int)surface.Parameter(7));

    private static IGeometry CreateTiltedGeometry(ZemaxSurface surface)
    {
        if (surface.Parameters.Any(p => p.Key is not (0 or 1) && p.Value != 0))
            throw new NotSupportedException("Nonzero unsupported parameter on a Zemax tilted surface.");
        return new TiltedPlaneGeometry(surface.Parameter(0), surface.Parameter(1));
    }

    private static IGeometry CreateFringeSagGeometry(ZemaxSurface surface)
    {
        var count = surface.ExtraData.GetValueOrDefault(1);
        if (count != Math.Truncate(count) || count is < 0 or > 37
            || surface.Parameter(-1) is not (0 or 1)
            || surface.Parameters.Any(p => p.Key is < -1 or > 9 && p.Value != 0))
            throw new NotSupportedException("Unsupported Fringe sag parameter definition.");
        return new FringeSagGeometry(double.IsInfinity(surface.Radius) ? 0 : surface.Radius, surface.Conic,
            Enumerable.Range(0, 8).Select(surface.Parameter).ToArray(), surface.ExtraData.GetValueOrDefault(2, 1),
            Enumerable.Range(3, (int)count).Select(i => surface.ExtraData.GetValueOrDefault(i)).ToArray(),
            surface.Parameter(8), surface.Parameter(9), surface.Parameter(-1) == 1);
    }

    private static IGeometry CreateChebyshevSagGeometry(ZemaxSurface surface)
    {
        var xOrder = surface.Parameter(0); var yOrder = surface.Parameter(1);
        if (xOrder is < 0 or > 14 || yOrder is < 0 or > 14
            || xOrder != Math.Truncate(xOrder) || yOrder != Math.Truncate(yOrder)
            || surface.Parameters.Any(p => p.Key is < 0 or > 3 && p.Value != 0))
            throw new NotSupportedException("Unsupported Chebyshev sag parameter definition.");
        var count = ((int)xOrder + 1) * ((int)yOrder + 1) - 1;
        return new ChebyshevSagGeometry(double.IsInfinity(surface.Radius) ? 0 : surface.Radius, (int)xOrder, (int)yOrder,
            surface.Parameter(2), surface.Parameter(3), Enumerable.Range(1, count)
                .Select(i => surface.ExtraData.GetValueOrDefault(i)).ToArray());
    }

    private static IGeometry CreateGradient1Geometry(ZemaxSurface surface, double radius)
    {
        if (surface.Parameters.Any(p => p.Key is < 0 or > 3 && p.Value != 0))
            throw new NotSupportedException("Unsupported Gradient 1 surface parameter definition.");
        return radius == 0 ? new PlaneGeometry() : new StandardGeometry(radius, surface.Conic);
    }

    private static GradientIndexMaterial CreateGradient1Material(ZemaxSurface surface)
    {
        var step = surface.Parameter(0);
        if (!double.IsFinite(step) || step <= 0)
            throw new InvalidDataException("Gradient 1 requires an explicit positive maximum integration step.");
        var options = new GradientIndexIntegrationOptions(MaximumStep: step);
        // The path budget cannot be reached sooner than the existing attempt budget:
        // each accepted arc-length step is at most MaximumStep.
        var maximumPath = step * options.MaximumAttempts;
        return new GradientIndexMaterial("Gradient 1", new Gradient1IndexProfile(surface.Parameter(1), surface.Parameter(2)),
            maximumPath, options);
    }

    private static IGeometry CreateGradient9Geometry(ZemaxSurface surface, double radius)
    {
        if (surface.Parameters.Any(p => p.Key is < 0 or > 2 && p.Value != 0))
            throw new NotSupportedException("Unsupported Gradient 9 surface parameter definition.");
        return radius == 0 ? new PlaneGeometry() : new StandardGeometry(radius, surface.Conic);
    }

    private static IMaterial CreateGradient9Material(ZemaxSurface surface)
    {
        var profile = Gradient9Catalog.Resolve(surface.Material, surface.AssetDirectory, surface.LengthScaleToMillimeters);
        if (profile is null) return new UnresolvedMaterial("Gradient 9 " + surface.Material);
        var step = surface.Parameter(0);
        if (!double.IsFinite(step) || step <= 0)
            throw new InvalidDataException("Gradient 9 requires an explicit positive maximum integration step.");
        var options = new GradientIndexIntegrationOptions(MaximumStep: step);
        return new GradientIndexMaterial(surface.Material, profile, step * options.MaximumAttempts, options);
    }

    private static IGeometry CreateMirrorArrayGeometry(ZemaxSurface surface)
    {
        if (surface.Parameters.Any(p => p.Key is < 0 or > 7 && p.Value != 0)
            || surface.ExtraData.Any(p => p.Key > 200 && p.Value != 0))
            throw new NotSupportedException("Unsupported US_MEMS sample-contract parameter.");
        int RoundedPixelCount(int index)
        {
            var value = Math.Floor(.5 + surface.Parameter(index));
            if (value < 0 || value > int.MaxValue) throw new InvalidDataException("Invalid mirror-array pixel count.");
            return (int)value;
        }
        int State(int index)
        {
            var value = surface.ExtraData.GetValueOrDefault(index);
            if (value != Math.Truncate(value) || value < 0 || value > int.MaxValue)
                throw new InvalidDataException("Mirror-array state words must be nonnegative integers.");
            return (int)value;
        }
        const double radians = Math.PI / 180;
        var count = Math.Max(0, surface.ExtraData.Keys.DefaultIfEmpty(1).Max() - 1);
        return new TernaryMirrorArrayGeometry(RoundedPixelCount(0), RoundedPixelCount(1), surface.Parameter(2), surface.Parameter(3),
            Enumerable.Range(4, 3).Select(i => surface.Parameter(i) * radians).ToArray(), surface.Parameter(7) * radians, State(1),
            Enumerable.Range(2, Math.Min(count, 199)).Select(State).ToArray());
    }

    private static IGeometry CreateOpaqueZemaxGeometry(ZemaxSurface surface)
    {
        var numbers = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["radius"] = surface.Radius,
            ["conic"] = surface.Conic
        };
        foreach (var parameter in surface.Parameters.OrderBy(item => item.Key))
        {
            numbers[$"parm{parameter.Key + 1}"] = parameter.Value;
        }
        foreach (var extra in surface.ExtraData) numbers[$"xdat{extra.Key}"] = extra.Value;
        if (surface.GridSagHeader is { } grid)
            for (var i = 0; i < grid.Length; i++) numbers[$"gdat{i}"] = grid[i];
        foreach (var point in surface.GridSagPoints)
        {
            numbers[$"gridSag{point.Key}"] = point.Value.Sag; numbers[$"gridDx{point.Key}"] = point.Value.Dx;
            numbers[$"gridDy{point.Key}"] = point.Value.Dy; numbers[$"gridDxy{point.Key}"] = point.Value.Dxy;
            numbers[$"gridNoData{point.Key}"] = point.Value.NoData ? 1 : 0;
        }

        var text = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["zemax.type"] = surface.Type,
            ["zemax.type.arguments"] = surface.TypeArguments,
            ["opticalsystemdesign.blockingReason"] =
                $"Zemax 面型 TYPE {surface.Type} 尚未映射到可计算几何；原始曲率、圆锥常数和 PARM 参数已保留为只读数据。"
        };
        return new OpaqueGeometryPayload(new ComponentSnapshot($"Zemax TYPE {surface.Type}", numbers, text));
    }

    private static IGeometry CreateToroidalGeometry(ZemaxSurface surface)
    {
        if (Math.Abs(surface.Conic) > 1e-14
            || Enumerable.Range(2, 6).Any(index => Math.Abs(surface.Parameter(index)) > 1e-14))
        {
            throw new NotSupportedException(
                $"Zemax toroidal conic or polynomial terms are not supported (SURF {surface.Number}).");
        }

        var radiusY = double.IsInfinity(surface.Radius) ? 0 : surface.Radius;
        var radiusX = Math.Abs(surface.Parameter(1)) < 1e-15
            ? double.PositiveInfinity
            : surface.Parameter(1);
        return new ToroidalGeometry(radiusY, radiusX);
    }

    private static void ConfigureAperture(
        Optic optic,
        ZemaxDocument document,
        int configurationIndex)
    {
        if (document.FloatingStop)
        {
            var stop = optic.SurfaceGroup.Items.FirstOrDefault(surface => surface.IsStop)
                ?? throw new InvalidDataException("FLOA requires a Zemax STOP surface.");
            optic.Aperture.Kind = ApertureKind.FloatByStopSize;
            optic.Aperture.Value = stop.SemiDiameter;
            return;
        }

        var aperture = document.Apertures[0];
        optic.Aperture.Kind = aperture.Kind;
        var configuredValue = document.ConfigurationDouble("APER", 0, configurationIndex);
        optic.Aperture.Value = configuredValue.HasValue
            ? ScaleApertureValue(document, aperture.Kind, configuredValue.Value)
            : aperture.Value;
    }

    private static void ConfigureFields(
        Optic optic,
        ZemaxDocument document,
        int configurationIndex)
    {
        optic.FieldDefinition = document.FieldType switch
        {
            0 => FieldDefinitionKind.Angle,
            1 => FieldDefinitionKind.ObjectHeight,
            2 => FieldDefinitionKind.ParaxialImageHeight,
            3 => FieldDefinitionKind.RealImageHeight,
            4 => throw new NotSupportedException("Zemax theodolite-angle fields are not supported."),
            _ => FieldDefinitionKind.Angle
        };
        optic.ObjectSpaceTelecentric = document.ObjectSpaceTelecentric;

        var count = Math.Max(document.FieldCount, Math.Max(document.FieldX.Count, document.FieldY.Count));
        if (count == 0)
        {
            optic.Fields.Add(new FieldPoint { Label = "On axis", Weight = 1 });
            return;
        }

        var fields = Enumerable.Range(0, count)
            .Select(index => new ParsedField(
                ScaleFieldValue(
                    document,
                    document.ConfigurationDouble("XFIE", index + 1, configurationIndex)
                        ?? ValueAt(document.FieldX, index)),
                ScaleFieldValue(
                    document,
                    document.ConfigurationDouble("YFIE", index + 1, configurationIndex)
                        ?? ValueAt(document.FieldY, index)),
                ValueAt(document.FieldWeights, index, 1),
                ValueAt(document.VignetteX, index),
                ValueAt(document.VignetteY, index),
                ValueAt(document.VignetteDecenterX, index),
                ValueAt(document.VignetteDecenterY, index),
                ValueAt(document.VignetteAngle, index),
                document.FieldComments.GetValueOrDefault(index + 1, string.Empty)))
            .ToArray();

        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            optic.Fields.Add(new FieldPoint
            {
                Label = !string.IsNullOrWhiteSpace(field.Label)
                    ? field.Label
                    : Math.Abs(field.X) < 1e-14 && Math.Abs(field.Y) < 1e-14
                        ? "On axis"
                        : $"Field {index + 1}",
                X = field.X,
                Y = field.Y,
                Weight = field.Weight,
                VignetteFactorX = field.VignetteX,
                VignetteFactorY = field.VignetteY,
                VignetteDecenterX = field.VignetteDecenterX,
                VignetteDecenterY = field.VignetteDecenterY,
                VignetteAngleDegrees = field.VignetteAngle
            });
        }
    }

    private static void ConfigureWavelengths(
        Optic optic,
        ZemaxDocument document,
        int configurationIndex)
    {
        var wavelengths = document.Wavelengths
            .OrderBy(wavelength => wavelength.Index)
            .Take(document.WavelengthCount > 0 ? document.WavelengthCount : int.MaxValue)
            .ToArray();
        if (wavelengths.Length == 0)
        {
            optic.Wavelengths.Add(new Wavelength
            {
                Label = "d",
                Nanometers = 587.5618,
                Weight = 1,
                IsPrimary = true
            });
            return;
        }

        var primary = document.PrimaryWavelengthIndex;
        if (primary < 0 || primary >= wavelengths.Length)
        {
            primary = 0;
        }

        for (var index = 0; index < wavelengths.Length; index++)
        {
            var wavelengthNumber = wavelengths[index].Index + 1;
            optic.Wavelengths.Add(new Wavelength
            {
                Label = $"W{index + 1}",
                Nanometers = (document.ConfigurationDouble(
                    "WAVE",
                    wavelengthNumber,
                    configurationIndex) ?? wavelengths[index].Micrometers) * 1000.0,
                Weight = document.ConfigurationDouble(
                    "WLWT",
                    wavelengthNumber,
                    configurationIndex) ?? wavelengths[index].Weight,
                IsPrimary = index == primary
            });
        }
    }

    private static void ApplyThicknessSolves(
        Optic optic,
        IReadOnlyList<ZemaxSurface> configuredSurfaces,
        IReadOnlyList<string> glassCatalogs)
    {
        var physicalSurfaces = configuredSurfaces
            .Where(IsImportedSurface)
            .ToArray();
        var wavelength = (optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? optic.Wavelengths.FirstOrDefault())?.Micrometers ?? 0.5875618;

        for (var surfaceIndex = 0; surfaceIndex < physicalSurfaces.Length - 1; surfaceIndex++)
        {
            var source = physicalSurfaces[surfaceIndex];
            var solve = source.MarginalRayHeightSolve;
            if (solve is null)
            {
                continue;
            }

            double height;
            double slope;
            if (Math.Abs(solve.PupilZone) <= 1e-15)
            {
                var marginal = optic.Paraxial.MarginalRay(wavelength);
                height = marginal.Heights[surfaceIndex][0];
                slope = marginal.Slopes[surfaceIndex][0];
            }
            else
            {
                var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
                    0,
                    0,
                    0,
                    solve.PupilZone,
                    wavelength,
                    aimAtStop: true);
                var history = optic.SequentialRayTracer.Trace(bundle).RayHistories.Single();
                if (surfaceIndex >= history.Count)
                {
                    throw new InvalidDataException(
                        $"Zemax MAZH solve on surface {source.Number} could not trace its pupil-zone ray.");
                }

                var sample = history[surfaceIndex];
                var localPosition = optic.SurfaceGroup.Items[surfaceIndex]
                    .CoordinateSystem.ToLocalPoint(sample.Position);
                var localDirection = optic.SurfaceGroup.Items[surfaceIndex]
                    .CoordinateSystem.ToLocalDirection(sample.Direction);
                height = localPosition.Y;
                slope = localDirection.Y / Math.Max(1e-30, localDirection.Z);
            }

            if (!double.IsFinite(slope) || Math.Abs(slope) <= 1e-15)
            {
                throw new InvalidDataException(
                    $"Zemax MAZH solve on surface {source.Number} has zero marginal-ray slope.");
            }

            var solvedThickness = (solve.Height - height) / slope;
            if (!double.IsFinite(solvedThickness))
            {
                throw new InvalidDataException(
                    $"Zemax MAZH solve on surface {source.Number} produced a non-finite thickness.");
            }

            source.Thickness = solvedThickness;
            var converted = ConvertSurfaces(optic, configuredSurfaces, glassCatalogs);
            InstallConvertedSurfaces(optic, converted);
        }
    }

    private static void ConfigureMeritFunction(Optic optic, ZemaxDocument document, IReadOnlyList<ZemaxSurface> configuredSurfaces)
    {
        var sourceToImported = configuredSurfaces
            .Where(IsImportedSurface)
            .Select((surface, index) => (surface.Number, index))
            .ToDictionary(surface => surface.Number, surface => surface.index);
        optic.MeritFunctionOperands.Clear();
        foreach (var operand in document.MeritOperands)
        {
            var imported = operand.Clone();
            if (!imported.CompatibilityOnly && ZemaxOperandRegistry.TryGet(imported.Type, out var descriptor))
            {
                // Use typed slots: sampling, field, wavelength and operand-row
                // indices must never be interpreted as physical surface numbers.
                for (var slot = 0; slot < imported.ZemaxIntegerParameters.Length; slot++)
                {
                    if (!descriptor.UsesSlotAs($"Int{slot + 1}", ZemaxOperandParameterValueKind.Surface)
                        && !descriptor.UsesSlotAs($"Int{slot + 1}", ZemaxOperandParameterValueKind.EndSurface))
                        continue;
                    var sourceNumber = imported.ZemaxIntegerParameters[slot];
                    // Preserve operand-specific zero conventions (image/all/object).
                    if (sourceNumber == 0) continue;
                    if (!sourceToImported.TryGetValue(sourceNumber, out var mapped))
                        throw new NotSupportedException($"{imported.Type} 指向不存在或已折叠的坐标断点面，当前尚未支持该参考。");
                    imported.ZemaxIntegerParameters[slot] = mapped;
                    if (slot == 0) imported.Surface = mapped;
                    if (slot == 1) imported.Wavelength = mapped;
                }
            }
            optic.MeritFunctionOperands.Add(imported);
        }
    }

    private static void ReadPupilRayMeritOperand(
        ZemaxDocument document,
        IReadOnlyList<string> tokens,
        string command)
    {
        document.MeritOperands.Add(new MeritOperandDefinition
        {
            Type = command,
            Surface = RequiredInt(tokens, 1, command),
            Wavelength = RequiredInt(tokens, 2, command),
            Hx = RequiredDouble(tokens, 3, command),
            Hy = RequiredDouble(tokens, 4, command),
            Px = RequiredDouble(tokens, 5, command),
            Py = RequiredDouble(tokens, 6, command),
            Target = RequiredDouble(tokens, 7, command),
            Weight = RequiredDouble(tokens, 8, command),
            ZemaxIntegerParameters = ReadZemaxIntegerParameters(tokens, command),
            ZemaxDataParameters = ReadZemaxDataParameters(tokens, command)
        });
    }

    private static void ReadStandardMeritOperand(
        ZemaxDocument document,
        IReadOnlyList<string> tokens,
        string command)
    {
        var operand = new MeritOperandDefinition
        {
            Type = command,
            Surface = RequiredInt(tokens, 1, command),
            Wavelength = RequiredInt(tokens, 2, command),
            Field = RequiredInt(tokens, 3, command),
            Target = RequiredDouble(tokens, 7, command),
            Weight = RequiredDouble(tokens, 8, command),
            ZemaxIntegerParameters = ReadZemaxIntegerParameters(tokens, command),
            ZemaxDataParameters = ReadZemaxDataParameters(tokens, command)
        };
        if (command is "SFNO" or "TFNO") operand.Field = RequiredInt(tokens, 1, command);
        if (command is "MECS" or "MECT")
        {
            operand.SpatialFrequency = RequiredDouble(tokens, 4, command);
            operand.Px = RequiredDouble(tokens, 5, command);
            operand.Py = RequiredDouble(tokens, 6, command);
        }

        document.MeritOperands.Add(operand);
    }

    private static void ReadRmsSpotMeritOperand(
        ZemaxDocument document,
        IReadOnlyList<string> tokens,
        string command)
    {
        document.MeritOperands.Add(new MeritOperandDefinition
        {
            Type = command,
            PupilRings = Math.Max(1, RequiredInt(tokens, 1, command)),
            Wavelength = RequiredInt(tokens, 2, command),
            Hx = RequiredDouble(tokens, 3, command),
            Hy = RequiredDouble(tokens, 4, command),
            Target = RequiredDouble(tokens, 7, command),
            Weight = RequiredDouble(tokens, 8, command),
            ZemaxIntegerParameters = ReadZemaxIntegerParameters(tokens, command),
            ZemaxDataParameters = ReadZemaxDataParameters(tokens, command)
        });
    }

    private static void ReadRangeThicknessMeritOperand(
        ZemaxDocument document,
        IReadOnlyList<string> tokens,
        string command)
    {
        document.MeritOperands.Add(new MeritOperandDefinition
        {
            Type = command,
            Surface = RequiredInt(tokens, 1, command),
            Wavelength = RequiredInt(tokens, 2, command),
            Hx = RequiredDouble(tokens, 3, command),
            Hy = RequiredDouble(tokens, 4, command),
            Px = RequiredDouble(tokens, 5, command),
            Py = RequiredDouble(tokens, 6, command),
            Target = RequiredDouble(tokens, 7, command),
            Weight = RequiredDouble(tokens, 8, command),
            ZemaxIntegerParameters = ReadZemaxIntegerParameters(tokens, command),
            ZemaxDataParameters = ReadZemaxDataParameters(tokens, command)
        });
    }

    private static void ReadSystemMeritOperand(
        ZemaxDocument document,
        IReadOnlyList<string> tokens,
        string command)
    {
        document.MeritOperands.Add(new MeritOperandDefinition
        {
            Type = command,
            Surface = RequiredInt(tokens, 1, command),
            Wavelength = RequiredInt(tokens, 2, command),
            Hx = RequiredDouble(tokens, 3, command),
            Hy = RequiredDouble(tokens, 4, command),
            Px = RequiredDouble(tokens, 5, command),
            Py = RequiredDouble(tokens, 6, command),
            Target = RequiredDouble(tokens, 7, command),
            Weight = RequiredDouble(tokens, 8, command),
            ZemaxIntegerParameters = ReadZemaxIntegerParameters(tokens, command),
            ZemaxDataParameters = ReadZemaxDataParameters(tokens, command)
        });
    }

    private static void ReadMathMeritOperand(
        ZemaxDocument document,
        IReadOnlyList<string> tokens,
        string command)
    {
        document.MeritOperands.Add(new MeritOperandDefinition
        {
            Type = command,
            Surface = RequiredInt(tokens, 1, command),
            Wavelength = RequiredInt(tokens, 2, command),
            Hx = RequiredDouble(tokens, 3, command),
            Hy = RequiredDouble(tokens, 4, command),
            Px = RequiredDouble(tokens, 5, command),
            Py = RequiredDouble(tokens, 6, command),
            Target = RequiredDouble(tokens, 7, command),
            Weight = RequiredDouble(tokens, 8, command),
            ZemaxIntegerParameters = ReadZemaxIntegerParameters(tokens, command),
            ZemaxDataParameters = ReadZemaxDataParameters(tokens, command)
        });
    }

    private static void ReadSurfaceBoundaryMeritOperand(
        ZemaxDocument document,
        IReadOnlyList<string> tokens,
        string command)
    {
        document.MeritOperands.Add(new MeritOperandDefinition
        {
            Type = command,
            Surface = RequiredInt(tokens, 1, command),
            Wavelength = RequiredInt(tokens, 2, command),
            Hx = RequiredDouble(tokens, 3, command),
            Hy = RequiredDouble(tokens, 4, command),
            Px = RequiredDouble(tokens, 5, command),
            Py = RequiredDouble(tokens, 6, command),
            Target = RequiredDouble(tokens, 7, command),
            Weight = RequiredDouble(tokens, 8, command),
            ZemaxIntegerParameters = ReadZemaxIntegerParameters(tokens, command),
            ZemaxDataParameters = ReadZemaxDataParameters(tokens, command)
        });
    }

    private static void ReadPreservedMeritOperand(
        ZemaxDocument document,
        IReadOnlyList<string> tokens,
        string command)
    {
        document.MeritOperands.Add(new MeritOperandDefinition
        {
            Enabled = false,
            CompatibilityOnly = true,
            Type = command,
            Surface = RequiredInt(tokens, 1, command),
            Wavelength = RequiredInt(tokens, 2, command),
            Hx = RequiredDouble(tokens, 3, command),
            Hy = RequiredDouble(tokens, 4, command),
            Px = RequiredDouble(tokens, 5, command),
            Py = RequiredDouble(tokens, 6, command),
            Target = RequiredDouble(tokens, 7, command),
            Weight = RequiredDouble(tokens, 8, command),
            Comment = $"Zemax 只读记录：{string.Join(" ", tokens.Skip(1))}",
            ZemaxIntegerParameters = ReadZemaxIntegerParameters(tokens, command),
            ZemaxDataParameters = ReadZemaxDataParameters(tokens, command)
        });
    }

    private static int[] ReadZemaxIntegerParameters(
        IReadOnlyList<string> tokens,
        string command) =>
    [
        RequiredInt(tokens, 1, command),
        RequiredInt(tokens, 2, command)
    ];

    private static double[] ReadZemaxDataParameters(
        IReadOnlyList<string> tokens,
        string command) =>
    [
        RequiredDouble(tokens, 3, command),
        RequiredDouble(tokens, 4, command),
        RequiredDouble(tokens, 5, command),
        RequiredDouble(tokens, 6, command)
    ];

    private static void ReadFieldComment(
        ZemaxDocument document,
        string line,
        IReadOnlyList<string> tokens)
    {
        var index = RequiredInt(tokens, 1, "FCOM");
        if (index <= 0)
        {
            throw new InvalidDataException("Zemax FCOM indices are one-based positive integers.");
        }

        var commentStart = line.IndexOf(tokens[1], StringComparison.Ordinal) + tokens[1].Length;
        var comment = commentStart < line.Length
            ? line[commentStart..].Trim().Trim('"')
            : string.Empty;
        document.FieldComments[index] = comment;
    }

    private static double RadiusFromConfiguredCurvature(
        ZemaxDocument document,
        double? curvature,
        double fallbackRadius)
    {
        if (!curvature.HasValue)
        {
            return fallbackRadius;
        }

        return RadiusFromCurvature(curvature.Value, document.LengthScaleToMillimeters);
    }

    private static void ReadConfigurationOperand(
        ZemaxDocument document,
        IReadOnlyList<string> tokens,
        string command)
    {
        var target = RequiredInt(tokens, 1, command);
        var configurationIndex = RequiredInt(tokens, 2, command) - 1;
        if (configurationIndex < 0)
        {
            throw new InvalidDataException($"Zemax {command} configuration indices are one-based positive integers.");
        }

        var value = RequiredToken(tokens, 3, command).Trim('"');
        var auxiliaryIndex = command is "PRAM" or "EDVA"
            && tokens.Count > 5
            ? RequiredInt(tokens, 5, command) - 1
            : -1;
        var requiredConfigurationCount = CheckedConfigurationCount(
            configurationIndex + 1,
            command);
        document.ConfigurationOperands.Add(new ZemaxConfigurationOperand(
            command,
            target,
            configurationIndex,
            value,
            auxiliaryIndex));
        document.ConfigurationCount = Math.Max(document.ConfigurationCount, requiredConfigurationCount);
    }

    private static int CheckedConfigurationCount(int count, string command)
    {
        if (count < 1)
        {
            return 1;
        }

        if (count > StarOptProjectStore.MaximumConfigurationCount)
        {
            throw new InvalidDataException(
                $"Zemax {command} declares {count} configurations, which exceeds the supported limit "
                + $"of {StarOptProjectStore.MaximumConfigurationCount}.");
        }

        return count;
    }

    private static void ReadUnit(ZemaxDocument document, IReadOnlyList<string> tokens)
    {
        var unit = RequiredToken(tokens, 1, "UNIT").Trim().Trim('"').ToUpperInvariant();
        document.UnitName = unit;
        document.LengthScaleToMillimeters = unit switch
        {
            "MM" or "MILLIMETER" or "MILLIMETERS" => 1.0,
            "CM" or "CENTIMETER" or "CENTIMETERS" => 10.0,
            "M" or "METER" or "METERS" => 1000.0,
            "IN" or "INCH" or "INCHES" => 25.4,
            "UM" or "MICRON" or "MICRONS" or "MICROMETER" or "MICROMETERS" => 0.001,
            "NM" or "NANOMETER" or "NANOMETERS" => 0.000001,
            _ => throw new InvalidDataException($"Zemax UNIT '{unit}' is not supported.")
        };
    }

    private static void ReadFNumber(ZemaxDocument document, IReadOnlyList<string> tokens)
    {
        var subtype = RequiredInt(tokens, 2, "FNUM");
        if (subtype != 0)
        {
            throw new NotSupportedException("Zemax paraxial-image F-number (FNUM subtype 1) is not supported.");
        }

        document.UpsertAperture("imageFNO", ApertureKind.FNumber, RequiredDouble(tokens, 1, "FNUM"));
    }

    private static void ReadObjectNumericalAperture(ZemaxDocument document, IReadOnlyList<string> tokens)
    {
        var subtype = RequiredInt(tokens, 2, "OBNA");
        if (subtype != 0)
        {
            throw new NotSupportedException("Zemax object-cone-angle apertures (OBNA subtype 1) are not supported.");
        }

        document.UpsertAperture("objectNA", ApertureKind.NumericalAperture, RequiredDouble(tokens, 1, "OBNA"));
    }

    private static void ReadConfiguration(ZemaxDocument document, IReadOnlyList<string> tokens)
    {
        document.FieldType = RequiredInt(tokens, 1, "FTYP");
        document.ObjectSpaceTelecentric = RequiredInt(tokens, 2, "FTYP") == 1;
        document.FieldCount = RequiredInt(tokens, 3, "FTYP");
        document.WavelengthCount = RequiredInt(tokens, 4, "FTYP");
        document.AfocalImageSpace = tokens.Count > 7 && RequiredInt(tokens, 7, "FTYP") == 1;
    }

    private static void ReadWavelength(ZemaxDocument document, IReadOnlyList<string> tokens)
    {
        var index = RequiredInt(tokens, 1, "WAVM") - 1;
        if (index < 0)
        {
            throw new InvalidDataException("Zemax WAVM indices are one-based positive integers.");
        }

        var wavelength = new ZemaxWavelength(
            index,
            RequiredDouble(tokens, 2, "WAVM"),
            tokens.Count > 3 ? RequiredDouble(tokens, 3, "WAVM") : 1);
        var existing = document.Wavelengths.FindIndex(item => item.Index == index);
        if (existing >= 0)
        {
            document.Wavelengths[existing] = wavelength;
        }
        else
        {
            document.Wavelengths.Add(wavelength);
        }
    }

    private static void ReadGlass(ZemaxSurface surface, IReadOnlyList<string> tokens)
    {
        surface.Material = RequiredToken(tokens, 1, "GLAS");
        surface.IsMirror = surface.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
        if (tokens.Count > 3 && RequiredInt(tokens, 2, "GLAS") == 2)
            surface.MaterialPickupSource = RequiredInt(tokens, 3, "GLAS");
        if (tokens.Count > 5
            && TryParseDouble(tokens[4], out var index)
            && TryParseDouble(tokens[5], out var abbe))
        {
            surface.RefractiveIndex = index;
            surface.AbbeNumber = abbe;
        }
    }

    private static void ReadSemiDiameter(
        ZemaxDocument document,
        ZemaxSurface surface,
        IReadOnlyList<string> tokens)
    {
        surface.SemiDiameter = Math.Abs(ScaleLength(document, RequiredDouble(tokens, 1, "DIAM")));
        var solveCode = tokens.Count > 2
            ? RequiredInt(tokens, 2, "DIAM")
            : 0;

        // OpticStudio solve codes: 0 = automatic, 1 = user defined, 2 = pickup.
        surface.SemiDiameterSolveCode = solveCode;
        surface.SemiDiameterFixed = solveCode != 0;
        if (solveCode == 2)
        {
            surface.SemiDiameterPickupSource = tokens.Count > 3 ? RequiredInt(tokens, 3, "DIAM") : 0;
            surface.SemiDiameterPickupScale = tokens.Count > 5 ? RequiredDouble(tokens, 5, "DIAM") : 1;
        }
    }

    private static void ReadMechanicalSemiDiameter(
        ZemaxDocument document,
        ZemaxSurface surface,
        IReadOnlyList<string> tokens)
    {
        surface.MechanicalSemiDiameter = Math.Abs(
            ScaleLength(document, RequiredDouble(tokens, 1, "MEMA")));
        surface.MechanicalSemiDiameterSolveCode = tokens.Count > 2
            ? RequiredInt(tokens, 2, "MEMA")
            : 0;
    }

    private static IMaterial ResolveGlass(
        Optic optic,
        ZemaxSurface surface,
        IReadOnlyList<string> glassCatalogs)
    {
        var material = NormalizeMaterial(surface.Material);
        if (optic.Materials.TryResolveExternalGlass(material, glassCatalogs, out var externalGlass))
        {
            return externalGlass;
        }

        if (optic.Materials.TryResolve(material, glassCatalogs, out var resolved))
        {
            return resolved;
        }

        // CORNING3 uses the compact C7980 name; the bundled catalog stores
        // Corning 7980 as C79-80. This is an explicit catalog alias, not an nd/Vd approximation.
        if (material.Equals("C7980", StringComparison.OrdinalIgnoreCase)
            && glassCatalogs.Any(catalog => Path.GetFileNameWithoutExtension(catalog)
                .Equals("CORNING3", StringComparison.OrdinalIgnoreCase))
            && optic.Materials.TryResolve("C79-80", glassCatalogs, out var corning7980))
        {
            return corning7980;
        }

        var unresolved = new UnresolvedMaterial(material, string.Join(", ", glassCatalogs));
        optic.Materials.Register(unresolved);
        return unresolved;
    }

    private static ZemaxSurface RequireSurface(ZemaxSurface? surface, string command)
    {
        return surface ?? throw new InvalidDataException($"Zemax {command} appears before the first SURF record.");
    }

    private static string RequiredToken(IReadOnlyList<string> tokens, int index, string command)
    {
        return index < tokens.Count
            ? tokens[index]
            : throw new InvalidDataException($"Zemax {command} is missing operand {index}.");
    }

    private static int RequiredInt(IReadOnlyList<string> tokens, int index, string command)
    {
        var token = RequiredToken(tokens, index, command);
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException($"Zemax {command} value '{token}' is not an integer.");
    }

    private static double RequiredDouble(IReadOnlyList<string> tokens, int index, string command)
    {
        var token = RequiredToken(tokens, index, command);
        return TryParseDouble(token, out var value)
            ? value
            : throw new InvalidDataException($"Zemax {command} value '{token}' is not numeric.");
    }

    private static double RequiredDistance(
        ZemaxDocument document,
        IReadOnlyList<string> tokens,
        int index,
        string command)
    {
        var token = RequiredToken(tokens, index, command);
        return token.Equals("INFINITY", StringComparison.OrdinalIgnoreCase)
            ? double.PositiveInfinity
            : ScaleLength(document, RequiredDouble(tokens, index, command));
    }

    private static double ScaleConfiguredDistance(
        ZemaxDocument document,
        double? configuredValue,
        double fallback) =>
        configuredValue.HasValue ? ScaleDistance(document, configuredValue.Value) : fallback;

    private static double? ScaleConfiguredOptionalLength(
        ZemaxDocument document,
        double? configuredValue,
        double? fallback) =>
        configuredValue.HasValue ? ScaleLength(document, configuredValue.Value) : fallback;

    private static double? ScaleConfiguredLength(
        ZemaxDocument document,
        double? configuredValue,
        double? fallback) =>
        configuredValue.HasValue ? ScaleLength(document, configuredValue.Value) : fallback;

    private static double ScaleDistance(ZemaxDocument document, double value) =>
        double.IsInfinity(value) ? value : ScaleLength(document, value);

    private static double ScaleLength(ZemaxDocument document, double value) =>
        value * document.LengthScaleToMillimeters;

    private static double ScaleFieldValue(ZemaxDocument document, double value) =>
        document.FieldType is 1 or 2 or 3 ? ScaleLength(document, value) : value;

    private static double ScaleApertureValue(
        ZemaxDocument document,
        ApertureKind kind,
        double value) =>
        kind == ApertureKind.EntrancePupilDiameter ? ScaleLength(document, value) : value;

    private static double ScaleSurfaceParameter(
        ZemaxDocument document,
        string type,
        int zeroBasedIndex,
        double value,
        string typeArguments = "")
    {
        var scale = document.LengthScaleToMillimeters;
        if (Math.Abs(scale - 1.0) <= 1e-15)
        {
            return value;
        }

        return type switch
        {
            "PARAXIAL" when zeroBasedIndex == 0 => value * scale,
            "PARAX_XY" when zeroBasedIndex is 0 or 1 => value / scale,
            "BIRE__IN" when zeroBasedIndex == -1 => value * scale,
            "BINARY_3" when zeroBasedIndex is 0 or 2 or 3 => value * scale,
            "HOLOGRM2" when zeroBasedIndex is >= 0 and < 6 => value * scale,
            "IRREGULA" when zeroBasedIndex is 0 or 1 or 4 or 5 or 6 => value * scale,
            "EVENASPH" or "FZERNSAG" or "BINARY_2" when zeroBasedIndex is >= 0 and < 8 => ScalePowerCoefficient(value, scale, 2 * (zeroBasedIndex + 1)),
            "FZERNSAG" when zeroBasedIndex is 8 or 9 => value * scale,
            "CHEBYSHV" when zeroBasedIndex is 2 or 3 => value * scale,
            "GRINSUR1" when zeroBasedIndex == 0 => value * scale,
            "GRINSUR1" when zeroBasedIndex == 2 => value / (scale * scale),
            "GRINSUR1" when zeroBasedIndex == 3 => value / scale,
            "GRINSUR9" when zeroBasedIndex == 0 => value * scale,
            "GRINSU11" when zeroBasedIndex == 0 => value * scale,
            "USERSURF" when zeroBasedIndex is 2 or 3 && typeArguments.Equals("US_MEMS.DLL", StringComparison.OrdinalIgnoreCase) => value * scale,
            "ODDASPHE" => ScalePowerCoefficient(value, scale, zeroBasedIndex + 1),
            "TOROIDAL" when zeroBasedIndex == 1 => value * scale,
            "COORDBRK" when zeroBasedIndex is 0 or 1 => value * scale,
            _ => value
        };
    }

    private static double ScaleSurfaceExtraData(ZemaxDocument document, string type, int index, double value) => type switch
    {
        "BINARY_2" when index == 2 => value * document.LengthScaleToMillimeters,
        "BINARY_3" when index >= 2 && (index - 2) % 2 == 0 => ScalePowerCoefficient(value, document.LengthScaleToMillimeters, 2 * ((index - 2) / 4 + 1)),
        "FZERNSAG" when index >= 2 => value * document.LengthScaleToMillimeters,
        "CHEBYSHV" => value / document.LengthScaleToMillimeters,
        _ => value
    };

    private static double ScalePowerCoefficient(double coefficient, double lengthScale, int power) =>
        coefficient * Math.Pow(lengthScale, 1 - power);

    private static double RadiusFromCurvature(double curvature, double lengthScaleToMillimeters) =>
        Math.Abs(curvature) < 1e-15
            ? double.PositiveInfinity
            : lengthScaleToMillimeters / curvature;

    private static bool TryParseDouble(string token, out double value)
    {
        return double.TryParse(
            token.Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static List<double> ReadValues(IReadOnlyList<string> tokens, int start, int expectedCount)
    {
        var count = expectedCount > 0 ? Math.Min(expectedCount, tokens.Count - start) : tokens.Count - start;
        return Enumerable.Range(start, Math.Max(0, count))
            .Select(index => RequiredDouble(tokens, index, tokens[0]))
            .ToList();
    }

    private static double ValueAt(IReadOnlyList<double> values, int index, double fallback = 0)
    {
        return index < values.Count ? values[index] : fallback;
    }

    private static Vector3D ObjectSurfaceOrigin(double thickness)
    {
        return double.IsInfinity(thickness)
            ? Vector3D.Zero
            : new Vector3D(0, 0, -thickness);
    }

    private static string SurfaceLabel(ZemaxSurface surface, int index, IReadOnlyList<ZemaxSurface> source)
    {
        if (!string.IsNullOrWhiteSpace(surface.Comment))
        {
            return surface.Comment;
        }

        if (index == 0)
        {
            return "Object";
        }

        if (index == source.Count(IsImportedSurface) - 1)
        {
            return "Image";
        }

        return surface.RetainReferencePlane ? $"Coordinate break {surface.Number} reference plane"
            : surface.IsStop ? "Aperture stop" : $"Surface {index}";
    }

    private static string NormalizeMaterial(string material)
    {
        return string.IsNullOrWhiteSpace(material)
            || material.Equals("AIR", StringComparison.OrdinalIgnoreCase)
            ? "Air"
            : material.Trim();
    }

    private static CoordinateSystem CoordinateFrom(Vector3D origin, Matrix3x3 rotation)
    {
        var (rx, ry, rz) = EulerDegrees(rotation);
        return new CoordinateSystem(origin, rx, ry, rz);
    }

    private static Matrix3x3 RotationMatrix(double rxDegrees, double ryDegrees, double rzDegrees)
    {
        var rx = rxDegrees * Math.PI / 180.0;
        var ry = ryDegrees * Math.PI / 180.0;
        var rz = rzDegrees * Math.PI / 180.0;
        var cx = Math.Cos(rx);
        var sx = Math.Sin(rx);
        var cy = Math.Cos(ry);
        var sy = Math.Sin(ry);
        var cz = Math.Cos(rz);
        var sz = Math.Sin(rz);
        var x = new Matrix3x3(1, 0, 0, 0, cx, -sx, 0, sx, cx);
        var y = new Matrix3x3(cy, 0, sy, 0, 1, 0, -sy, 0, cy);
        var z = new Matrix3x3(cz, -sz, 0, sz, cz, 0, 0, 0, 1);
        return z * y * x;
    }

    private static (double X, double Y, double Z) EulerDegrees(Matrix3x3 matrix)
    {
        var y = Math.Asin(Math.Clamp(-matrix.M31, -1, 1));
        double x;
        double z;
        if (Math.Abs(Math.Cos(y)) > 1e-10)
        {
            x = Math.Atan2(matrix.M32, matrix.M33);
            z = Math.Atan2(matrix.M21, matrix.M11);
        }
        else
        {
            x = 0;
            z = Math.Atan2(-matrix.M12, matrix.M22);
        }

        const double toDegrees = 180.0 / Math.PI;
        return (x * toDegrees, y * toDegrees, z * toDegrees);
    }

    private sealed class ZemaxDocument
    {
        public string Name { get; set; } = "Imported Zemax ZMX";
        public bool SequentialModeSeen { get; set; }
        public bool FloatingStop { get; set; }
        public ZemaxApodization? Apodization { get; set; }
        public string UnitName { get; set; } = "MM";
        public double LengthScaleToMillimeters { get; set; } = 1.0;
        public List<ZemaxAperture> Apertures { get; } = new();
        public int FieldType { get; set; }
        public int FieldCount { get; set; }
        public int WavelengthCount { get; set; }
        public bool ObjectSpaceTelecentric { get; set; }

        public bool RayAimingEnabled { get; set; }

        public int GlobalReferenceSurfaceNumber { get; set; } = 1;
        public bool AfocalImageSpace { get; set; }

        public ReferenceOpdMode ReferenceOpd { get; set; } = ReferenceOpdMode.ExitPupil;
        public List<double> FieldX { get; set; } = new();
        public List<double> FieldY { get; set; } = new();
        public List<double> FieldWeights { get; set; } = new();
        public List<double> VignetteX { get; set; } = new();
        public List<double> VignetteY { get; set; } = new();
        public List<double> VignetteDecenterX { get; set; } = new();
        public List<double> VignetteDecenterY { get; set; } = new();
        public List<double> VignetteAngle { get; set; } = new();
        public Dictionary<int, string> FieldComments { get; } = new();
        public int PrimaryWavelengthIndex { get; set; }
        public List<ZemaxWavelength> Wavelengths { get; } = new();
        public List<string> GlassCatalogs { get; } = new();
        public List<ZemaxSurface> Surfaces { get; } = new();
        public int ConfigurationCount { get; set; } = 1;
        public List<ZemaxConfigurationOperand> ConfigurationOperands { get; } = new();
        public List<MeritOperandDefinition> MeritOperands { get; } = new();

        public void UpsertAperture(string key, ApertureKind kind, double value)
        {
            var existing = Apertures.FindIndex(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            var aperture = new ZemaxAperture(key, kind, value);
            if (existing >= 0)
            {
                Apertures[existing] = aperture;
            }
            else
            {
                Apertures.Add(aperture);
            }
        }

        public double? ConfigurationDouble(string command, int target, int configurationIndex)
        {
            var operand = ConfigurationOperands.FindLast(item =>
                item.Command.Equals(command, StringComparison.OrdinalIgnoreCase)
                && item.Target == target
                && item.ConfigurationIndex == configurationIndex);
            return operand is not null && TryParseDouble(operand.Value, out var value)
                ? value
                : null;
        }

        public string? ConfigurationText(string command, int target, int configurationIndex)
        {
            return ConfigurationOperands.FindLast(item =>
                item.Command.Equals(command, StringComparison.OrdinalIgnoreCase)
                && item.Target == target
                && item.ConfigurationIndex == configurationIndex)?.Value;
        }

        public IEnumerable<ZemaxConfigurationOperand> ConfigurationValues(
            string command,
            int target,
            int configurationIndex)
        {
            return ConfigurationOperands.Where(item =>
                item.Command.Equals(command, StringComparison.OrdinalIgnoreCase)
                && item.Target == target
                && item.ConfigurationIndex == configurationIndex);
        }
    }

    private sealed class ZemaxSurface(int number, double lengthScaleToMillimeters = 1, string? assetDirectory = null)
    {
        public GridGradientIndexProfile? GridGradientProfile { get; set; }
        public double LengthScaleToMillimeters { get; } = lengthScaleToMillimeters;
        public string? AssetDirectory { get; } = assetDirectory;
        public string TypeArguments { get; set; } = "";
        public int Number { get; } = number;
        public string Type { get; set; } = "STANDARD";
        public string Comment { get; set; } = string.Empty;
        public double Radius { get; set; } = double.PositiveInfinity;
        public double Thickness { get; set; }
        public double Conic { get; set; }
        public string Material { get; set; } = "Air";
        public double? RefractiveIndex { get; set; }
        public double? AbbeNumber { get; set; }
        public double? SemiDiameter { get; set; }
        public double? MechanicalSemiDiameter { get; set; }
        public int MechanicalSemiDiameterSolveCode { get; set; }
        public double? MinimumAperture { get; set; }
        public double? MaximumAperture { get; set; }
        public bool CircularAperture { get; set; }
        public bool CircularObscuration { get; set; }
        public double ApertureOffsetX { get; set; }
        public double ApertureOffsetY { get; set; }
        public bool SemiDiameterFixed { get; set; }
        public bool FloatingAperture { get; set; }
        public int SemiDiameterSolveCode { get; set; }
        public int SemiDiameterPickupSource { get; set; }
        public double SemiDiameterPickupScale { get; set; } = 1;
        public bool IsStop { get; set; }
        public bool IsMirror { get; set; }
        public bool ExplicitMirror { get; set; }
        public Dictionary<int, ZemaxParameterPickup> ParameterPickups { get; } = [];
        public bool RetainReferencePlane { get; set; }

        public string? PolygonPayload { get; set; }
        public string? PolygonAssetDirectory { get; set; }
        public int PolygonObjectCount { get; set; }
        public bool PolygonIsVolume { get; set; }
        public double PolygonLengthScale { get; set; } = 1;
        public double[] PolygonPose { get; set; } = new double[6];
        public string PolygonMaterial { get; set; } = "Air";
        public double PolygonScale { get; set; } = 1;
        public ZemaxMarginalRayHeightSolve? MarginalRayHeightSolve { get; set; }
        public ZemaxCurvatureSolve? CurvatureSolve { get; set; }
        public ZemaxCurvaturePickup? CurvaturePickup { get; set; }
        public ZemaxThicknessPickup? ThicknessPickup { get; set; }
        public int? MaterialPickupSource { get; set; }
        public Dictionary<int, double> Parameters { get; } = new();
        public Dictionary<int, double> ExtraData { get; } = new();
        public double[]? GridSagHeader { get; set; }
        public Dictionary<int, GridSagPoint> GridSagPoints { get; } = new();
        public bool CollimatedParaxialMode { get; set; }

        public double Parameter(int index) => Parameters.GetValueOrDefault(index);
    }

    private sealed record ZemaxAperture(string Key, ApertureKind Kind, double Value);
    private sealed record ZemaxParameterPickup(int SourceSurface, double Scale, double Offset, int SourceParameter);
    private sealed record ZemaxCurvaturePickup(int SourceSurface, double Scale);
    private sealed record ZemaxThicknessPickup(int SourceSurface, double Scale, double Offset);
    private sealed record ZemaxMarginalRayHeightSolve(double Height, double PupilZone);
    private sealed record ZemaxWavelength(int Index, double Micrometers, double Weight);
    private sealed record ZemaxConfigurationOperand(
        string Command,
        int Target,
        int ConfigurationIndex,
        string Value,
        int AuxiliaryIndex);
    private sealed record ParsedField(
        double X,
        double Y,
        double Weight,
        double VignetteX,
        double VignetteY,
        double VignetteDecenterX,
        double VignetteDecenterY,
        double VignetteAngle,
        string Label);
    private sealed record ConvertedSurface(
        int Index,
        OpticalSurface Surface,
        IGeometry Geometry,
        IMaterial MaterialBefore,
        IMaterial MaterialAfter,
        bool IsReflective,
        CoordinateSystem CoordinateSystem,
        IInteractionModel Interaction);
}
