using System.Globalization;
using System.Text;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Core.FileIO;

internal static class ZemaxZmxReader
{
    public static Optic Import(string text)
    {
        return ImportConfigurationSet(text).ActiveOptic;
    }

    public static ZemaxZmxImportResult ImportConfigurationSet(string text)
    {
        var document = Parse(text);
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

        ConfigureAperture(optic, document, configurationIndex);
        optic.RayAimingEnabled = document.RayAimingEnabled;
        ConfigureFields(optic, document, configurationIndex);
        ConfigureWavelengths(optic, document, configurationIndex);
        ApplyThicknessSolves(optic, configuredSurfaces, document.GlassCatalogs);
        ConfigureMeritFunction(optic, document);
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
            surface.InteractionModel = new RefractiveReflectiveInteractionModel(item.IsReflective);
            surface.CoordinateSystem = item.CoordinateSystem;
        }
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

    private static ZemaxDocument Parse(string text)
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
                    document.UpsertAperture("EPD", ApertureKind.EntrancePupilDiameter, RequiredDouble(tokens, 1, command));
                    break;
                case "OBNA":
                    ReadObjectNumericalAperture(document, tokens);
                    break;
                case "FLOA":
                    document.FloatingStop = true;
                    break;
                case "RAIM":
                    document.RayAimingEnabled = tokens.Length > 2
                        && RequiredInt(tokens, 2, command) != 0;
                    break;
                case "FTYP":
                    ReadConfiguration(document, tokens);
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
                    RequireSurface(current, command).MinimumAperture = Math.Abs(RequiredDouble(tokens, 1, command));
                    break;
                case "APMX" when tokens.Length <= 2 && current is not null:
                    RequireSurface(current, command).SemiDiameter = Math.Abs(RequiredDouble(tokens, 1, command));
                    break;
                case "VDXN":
                case "VDYN":
                case "VANN":
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
                    ReadPupilRayMeritOperand(document, tokens, command);
                    break;
                case "MECS":
                case "MECT":
                case "EFFL":
                    ReadStandardMeritOperand(document, tokens, command);
                    break;
                case "CONF":
                case "RANG":
                case "CONS":
                case "PROD":
                case "OPLT":
                case "MNCA":
                case "MXCA":
                case "MNEA":
                case "MNCG":
                case "MXCG":
                case "MNEG":
                case "MXEG":
                case "TTHI":
                case "CTGT":
                case "PMAG":
                case "REAR":
                case "DIMX":
                case "PETZ":
                case "SINE":
                case "DIVI":
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
                    ReadConfigurationOperand(document, tokens, command);
                    break;
                case "GCAT":
                    document.GlassCatalogs.AddRange(tokens.Skip(1));
                    break;
                case "SURF":
                    current = new ZemaxSurface(RequiredInt(tokens, 1, command));
                    document.Surfaces.Add(current);
                    break;
                case "TYPE":
                    RequireSurface(current, command).Type = RequiredToken(tokens, 1, command).ToUpperInvariant();
                    break;
                case "PARM":
                    var parameterIndex = RequiredInt(tokens, 1, command) - 1;
                    if (parameterIndex < 0)
                    {
                        throw new InvalidDataException("Zemax PARM indices are one-based positive integers.");
                    }

                    RequireSurface(current, command).Parameters[parameterIndex] = RequiredDouble(tokens, 2, command);
                    break;
                case "CURV":
                    var curvature = RequiredDouble(tokens, 1, command);
                    RequireSurface(current, command).Radius = Math.Abs(curvature) < 1e-15
                        ? double.PositiveInfinity
                        : 1.0 / curvature;
                    break;
                case "DISZ":
                    RequireSurface(current, command).Thickness = RequiredDistance(tokens, 1, command);
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
                    }
                    break;
                case "DIAM":
                    ReadSemiDiameter(RequireSurface(current, command), tokens);
                    break;
                case "COMM":
                    RequireSurface(current, command).Comment = tokens.Length > 1
                        ? string.Join(" ", tokens.Skip(1)).Trim('"')
                        : string.Empty;
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

        if (document.AfocalImageSpace)
        {
            throw new NotSupportedException("Zemax afocal image space is not supported.");
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
        return document.Surfaces.Select(source =>
        {
            var material = document.ConfigurationText("GLSS", source.Number, configurationIndex)
                ?? source.Material;
            var configured = new ZemaxSurface(source.Number)
            {
                Type = source.Type,
                Comment = source.Comment,
                Radius = RadiusFromConfiguredCurvature(
                    document.ConfigurationDouble("CRVT", source.Number, configurationIndex),
                    source.Radius),
                Thickness = document.ConfigurationDouble(
                    "THIC",
                    source.Number,
                    configurationIndex) ?? source.Thickness,
                Conic = source.Conic,
                Material = string.IsNullOrWhiteSpace(material) ? "Air" : material,
                RefractiveIndex = source.RefractiveIndex,
                AbbeNumber = source.AbbeNumber,
                SemiDiameter = document.ConfigurationDouble(
                    "APMX",
                    source.Number,
                    configurationIndex) ?? source.SemiDiameter,
                MinimumAperture = document.ConfigurationDouble(
                    "APMN",
                    source.Number,
                    configurationIndex) ?? source.MinimumAperture,
                SemiDiameterFixed = source.SemiDiameterFixed,
                IsStop = configuredStop.HasValue
                    ? source.Number == (int)Math.Round(configuredStop.Value)
                    : source.IsStop,
                IsMirror = source.IsMirror || material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)
            };
            configured.MarginalRayHeightSolve = source.MarginalRayHeightSolve;
            foreach (var parameter in source.Parameters)
            {
                configured.Parameters[parameter.Key] = parameter.Value;
            }

            foreach (var operand in document.ConfigurationValues(
                         "PRAM",
                         source.Number,
                         configurationIndex))
            {
                if (operand.AuxiliaryIndex >= 0 && TryParseDouble(operand.Value, out var value))
                {
                    configured.Parameters[operand.AuxiliaryIndex] = value;
                }
            }

            return configured;
        }).ToArray();
    }

    private static int DetectActiveConfiguration(ZemaxDocument document)
    {
        // Plain sequential ZMX exports do not provide a reliable persisted UI-active
        // multi-configuration selection. Keep imports deterministic and explainable
        // by selecting configuration 1 instead of guessing from operand values.
        return 0;
    }

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
            if (source.Type == "COORDBRK")
            {
                ApplyCoordinateBreak(source, ref origin, ref rotation);
                continue;
            }

            var index = result.Count;
            var surfaceOrigin = index == 0
                ? ObjectSurfaceOrigin(source.Thickness)
                : origin;
            var coordinate = CoordinateFrom(surfaceOrigin, rotation);
            var geometry = CreateGeometry(source);
            var isReflective = source.IsMirror || source.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
            IMaterial materialAfter;
            if (isReflective)
            {
                materialAfter = previousMaterial.Clone();
            }
            else
            {
                materialAfter = ResolveGlass(optic, source, glassCatalogs);
            }

            var legacyRadius = double.IsInfinity(source.Radius) ? 0 : source.Radius;
            var thickness = source.Thickness;
            var semiDiameter = source.SemiDiameter is { } configuredSemiDiameter
                && double.IsFinite(configuredSemiDiameter)
                    ? Math.Max(0.1, Math.Abs(configuredSemiDiameter))
                    : 10;
            var physicalAperture = CreatePhysicalAperture(source, semiDiameter);
            var surface = new OpticalSurface
            {
                Number = index,
                Label = SurfaceLabel(source, index, sourceSurfaces),
                Radius = legacyRadius,
                Thickness = thickness,
                Material = isReflective ? "MIRROR" : materialAfter.Name,
                SemiDiameter = semiDiameter,
                SemiDiameterFixed = source.SemiDiameterFixed,
                Conic = source.Conic,
                IsStop = source.IsStop,
                IsReflective = isReflective,
                Geometry = geometry,
                MaterialBefore = previousMaterial.Clone(),
                MaterialAfter = materialAfter.Clone(),
                InteractionModel = new RefractiveReflectiveInteractionModel(isReflective),
                PhysicalAperture = physicalAperture,
                CoordinateSystem = coordinate
            };
            result.Add(new ConvertedSurface(
                index,
                surface,
                geometry,
                previousMaterial.Clone(),
                materialAfter.Clone(),
                isReflective,
                coordinate));

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

    private static IPhysicalAperture? CreatePhysicalAperture(ZemaxSurface source, double semiDiameter)
    {
        if (source.MinimumAperture is not { } minimumAperture
            || !double.IsFinite(minimumAperture)
            || Math.Abs(minimumAperture) <= 1e-12)
        {
            return null;
        }

        return new AnnularAperture(semiDiameter, Math.Abs(minimumAperture));
    }

    private static void ApplyCoordinateBreak(
        ZemaxSurface source,
        ref Vector3D origin,
        ref Matrix3x3 rotation)
    {
        if (Math.Abs(source.Parameter(5)) > 1e-12)
        {
            throw new NotSupportedException(
                $"Zemax coordinate-break order flags are not supported (SURF {source.Number}, PARM 6).");
        }

        var decenter = new Vector3D(source.Parameter(0), source.Parameter(1), 0);
        origin += rotation.Transform(decenter);
        rotation *= RotationMatrix(source.Parameter(2), source.Parameter(3), source.Parameter(4));
        if (!double.IsInfinity(source.Thickness))
        {
            origin += rotation.Transform(new Vector3D(0, 0, source.Thickness));
        }
    }

    private static IGeometry CreateGeometry(ZemaxSurface surface)
    {
        var radius = double.IsInfinity(surface.Radius) ? 0 : surface.Radius;
        return surface.Type switch
        {
            "STANDARD" => Math.Abs(radius) < 1e-15
                ? new PlaneGeometry()
                : new StandardGeometry(radius, surface.Conic),
            "EVENASPH" => new EvenAsphereGeometry(
                radius,
                surface.Conic,
                Enumerable.Range(0, 8).Select(surface.Parameter).ToArray()),
            "ODDASPHE" => new OddAsphereGeometry(
                radius,
                surface.Conic,
                Enumerable.Range(0, 8).Select(surface.Parameter).ToArray()),
            "TOROIDAL" => CreateToroidalGeometry(surface),
            _ => throw new NotSupportedException(
                $"Zemax surface type '{surface.Type}' is not supported (SURF {surface.Number}).")
        };
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
        optic.Aperture.Value = document.ConfigurationDouble("APER", 0, configurationIndex)
            ?? aperture.Value;
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
                document.ConfigurationDouble("XFIE", index + 1, configurationIndex)
                    ?? ValueAt(document.FieldX, index),
                document.ConfigurationDouble("YFIE", index + 1, configurationIndex)
                    ?? ValueAt(document.FieldY, index),
                ValueAt(document.FieldWeights, index, 1),
                ValueAt(document.VignetteX, index),
                ValueAt(document.VignetteY, index),
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
                VignetteFactorY = field.VignetteY
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
            .Where(surface => surface.Type != "COORDBRK")
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

    private static void ConfigureMeritFunction(Optic optic, ZemaxDocument document)
    {
        optic.MeritFunctionOperands.Clear();
        foreach (var operand in document.MeritOperands)
        {
            optic.MeritFunctionOperands.Add(operand.Clone());
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
            Weight = RequiredDouble(tokens, 8, command)
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
            Weight = RequiredDouble(tokens, 8, command)
        };
        if (command is "MECS" or "MECT")
        {
            operand.SpatialFrequency = RequiredDouble(tokens, 4, command);
            operand.Px = RequiredDouble(tokens, 5, command);
            operand.Py = RequiredDouble(tokens, 6, command);
        }

        document.MeritOperands.Add(operand);
    }

    private static void ReadPreservedMeritOperand(
        ZemaxDocument document,
        IReadOnlyList<string> tokens,
        string command)
    {
        document.MeritOperands.Add(new MeritOperandDefinition
        {
            Enabled = false,
            Type = command,
            Surface = RequiredInt(tokens, 1, command),
            Wavelength = RequiredInt(tokens, 2, command),
            Hx = RequiredDouble(tokens, 3, command),
            Hy = RequiredDouble(tokens, 4, command),
            Px = RequiredDouble(tokens, 5, command),
            Py = RequiredDouble(tokens, 6, command),
            Target = RequiredDouble(tokens, 7, command),
            Weight = RequiredDouble(tokens, 8, command),
            Comment = $"Zemax 只读记录：{string.Join(" ", tokens.Skip(1))}"
        });
    }

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

    private static double RadiusFromConfiguredCurvature(double? curvature, double fallbackRadius)
    {
        if (!curvature.HasValue)
        {
            return fallbackRadius;
        }

        return Math.Abs(curvature.Value) < 1e-15
            ? double.PositiveInfinity
            : 1.0 / curvature.Value;
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
        var auxiliaryIndex = command.Equals("PRAM", StringComparison.OrdinalIgnoreCase)
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
        if (tokens.Count > 5
            && TryParseDouble(tokens[4], out var index)
            && TryParseDouble(tokens[5], out var abbe))
        {
            surface.RefractiveIndex = index;
            surface.AbbeNumber = abbe;
        }
    }

    private static void ReadSemiDiameter(
        ZemaxSurface surface,
        IReadOnlyList<string> tokens)
    {
        surface.SemiDiameter = Math.Abs(RequiredDouble(tokens, 1, "DIAM"));
        var solveCode = tokens.Count > 2
            ? RequiredInt(tokens, 2, "DIAM")
            : 0;

        // OpticStudio solve codes: 0 = automatic, 1 = user defined,
        // 2 = pickup. Preserve every explicit solve because the local
        // model currently exposes only automatic vs fixed.
        surface.SemiDiameterFixed = solveCode != 0;
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

        if (surface.RefractiveIndex is > 1 && surface.AbbeNumber is > 0)
        {
            optic.Materials.RegisterAbbeGlass(material, surface.RefractiveIndex.Value, surface.AbbeNumber.Value);
            return optic.Materials.Resolve(material);
        }

        var catalogs = glassCatalogs.Count == 0 ? "none declared" : string.Join(", ", glassCatalogs);
        throw new KeyNotFoundException(
            $"Zemax glass '{material}' was not found in the local catalog (GCAT: {catalogs}) and GLAS did not provide valid nd/Vd fallback data.");
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

    private static double RequiredDistance(IReadOnlyList<string> tokens, int index, string command)
    {
        var token = RequiredToken(tokens, index, command);
        return token.Equals("INFINITY", StringComparison.OrdinalIgnoreCase)
            ? double.PositiveInfinity
            : RequiredDouble(tokens, index, command);
    }

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

        if (index == source.Count(item => item.Type != "COORDBRK") - 1)
        {
            return "Image";
        }

        return surface.IsStop ? "Aperture stop" : $"Surface {index}";
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
        public List<ZemaxAperture> Apertures { get; } = new();
        public int FieldType { get; set; }
        public int FieldCount { get; set; }
        public int WavelengthCount { get; set; }
        public bool ObjectSpaceTelecentric { get; set; }

        public bool RayAimingEnabled { get; set; }
        public bool AfocalImageSpace { get; set; }
        public List<double> FieldX { get; set; } = new();
        public List<double> FieldY { get; set; } = new();
        public List<double> FieldWeights { get; set; } = new();
        public List<double> VignetteX { get; set; } = new();
        public List<double> VignetteY { get; set; } = new();
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

    private sealed class ZemaxSurface(int number)
    {
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
        public double? MinimumAperture { get; set; }
        public bool SemiDiameterFixed { get; set; }
        public bool IsStop { get; set; }
        public bool IsMirror { get; set; }
        public ZemaxMarginalRayHeightSolve? MarginalRayHeightSolve { get; set; }
        public Dictionary<int, double> Parameters { get; } = new();

        public double Parameter(int index) => Parameters.GetValueOrDefault(index);
    }

    private sealed record ZemaxAperture(string Key, ApertureKind Kind, double Value);
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
        string Label);
    private sealed record ConvertedSurface(
        int Index,
        OpticalSurface Surface,
        IGeometry Geometry,
        IMaterial MaterialBefore,
        IMaterial MaterialAfter,
        bool IsReflective,
        CoordinateSystem CoordinateSystem);
}
