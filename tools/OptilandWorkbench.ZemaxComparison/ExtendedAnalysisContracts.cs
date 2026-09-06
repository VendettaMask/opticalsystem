using System.Globalization;

namespace OptilandWorkbench.ZemaxComparison;

// Settings below describe this validation experiment, never global OpticStudio defaults.
public static partial class ExtendedAnalysisContracts
{
    public static CanonicalAnalysisRequest Configure(AnalysisComparisonEntry entry, CanonicalAnalysisRequest r, int primary)
    {
        if (entry.ZemaxSettingsMapper == "capability-audit")
            return r with { SettingsOrigin = "NativeCapabilityInspectionNotAligned", ZemaxSettingsMode = "CapabilityInspection", ZemaxSettings = [] };
        if (entry.ZemaxSettingsMapper == "spot-layout")
            return r with
            {
                Field = 0,
                Wavelength = primary,
                WavelengthScope = "Primary",
                Reference = "ChiefRay",
                ZemaxSettings = new()
                {
                    ["Field"] = 0,
                    ["Wavelength"] = primary,
                    ["Surface"] = -1,
                    ["Configuration"] = r.Configuration,
                    ["Pattern"] = "Hexapolar",
                    ["ReferTo"] = "ChiefRay",
                    ["RayDensity"] = 6,
                    ["ShowScale"] = "ScaleBar",
                    ["ColorRaysBy"] = "Waves",
                    ["DirectionCosines"] = false,
                    ["UseSymbols"] = true,
                    ["UsePolarization"] = false,
                    ["ScatterRays"] = false,
                    ["ShowAiryDisk"] = false,
                    ["IgnoreLateralColor"] = false,
                    ["PlotScale"] = 0d,
                    ["DeltaFocus"] = entry.CanonicalAnalysisKey == "Through Focus" ? 50d : 0d,
                    ["Exaggerate"] = 1d
                }
            };
        if (entry.ZemaxSettingsMapper != "contract") return r;
        var settings = new Dictionary<string, object>(StringComparer.Ordinal);
        if (ConfigureScans(ref r, settings)) return r with { ZemaxSettings = settings };
        if (ConfigureDiffraction(ref r, settings)) return r with { ZemaxSettings = settings };
        if (ConfigureEnergy(ref r, settings)) return r with { ZemaxSettings = settings };
        void Set(params object[] pairs) { for (var i = 0; i < pairs.Length; i += 2) settings.Add((string)pairs[i], pairs[i + 1]); }
        switch (entry.CanonicalAnalysisKey)
        {
            case "Extended Source Encircled Energy":
                var sourcePath = Path.Combine(AppContext.BaseDirectory, "assets", "uniform-square.IMA");
                r = r with { SourceImagePath = sourcePath, SourceImageSha256 = JsonFiles.Hash(File.ReadAllBytes(sourcePath)) };
                Set("Field", r.Field, "Wavelength", r.Wavelength, "Surface", -1, "Type", "Encircled", "ReferTo", "Centroid", "RaysX1000", 100,
                    "MultiplyByDiffractionLimit", false, "RemoveVignettingFactors", true, "UseDashes", false, "UsePolarization", false,
                    "MaximumDistance", 10d, "FieldSize", 0.1, "Rotation", 0d, "ImageName", sourcePath); break;
            case "Jones Pupil":
                r = r with { Wavelength = primary, WavelengthScope = "Primary", Field = 1, ZemaxSettingsMode = "ResetWithReportVerification" }; break;
            case "Footprint Diagram":
                r = r with
                {
                    Wavelength = primary,
                    WavelengthScope = "Primary",
                    Field = 0,
                    ZemaxSettingsMode = "CfgBindings",
                    ZemaxCfgSettings = new()
                    { ["FOO_RAYDENSITY"] = "1", ["FOO_SURFACE"] = (r.SurfaceCount - 1).ToString(CultureInfo.InvariantCulture), ["FOO_FIELD"] = "0", ["FOO_WAVELENGTH"] = primary.ToString(CultureInfo.InvariantCulture), ["FOO_DELETEVIGNETTED"] = "1" }
                }; break;
            case "Contrast Loss Map":
                r = r with { MaximumFrequency = 0 };
                Set("Field", r.Field, "Wavelength", r.Wavelength, "SampleSize", "S_13x13", "Frequency", 0d, "Normalize", false, "ShowOPD", true); break;
            case "Single Ray Trace":
                Set("Field", r.Field, "Wavelength", r.Wavelength, "UseGlobal", false, "Px", 0d, "Py", 0.8, "Type", "DirectionCosines"); break;
            case "Cardinal Points Data":
            case "Y-Ybar":
            case "Angle vs Image Height":
            case "Angle vs Image Height - Through Pupil":
            case "Angle vs Image Height - Through Field":
                r = r with { Wavelength = primary, WavelengthScope = "Primary", ZemaxSettingsMode = "ResetWithReportVerification" }; break;
            case "Prescription Report":
            case "System Data Report":
                r = r with { Wavelength = primary, WavelengthScope = "Primary", ZemaxSettingsMode = "ModelMetadataAndMfe" }; break;
            case "Grid Distortion":
                Set("Field", r.Field, "Wavelength", r.Wavelength, "SymmetricMagnification", false, "ScaleFactor", 1d,
                    "Aspect", 1d, "FieldWidth", 0d, "GridNumber", 5, "Method", 0, "RotateText", 0); break;
            case "Full Field Aberration":
                Set("Field", r.Field, "Wavelength", r.Wavelength, "FieldShape", "Elliptical", "XFieldWidth", r.MaximumFieldRadius,
                    "YFieldWidth", r.MaximumFieldRadius, "Decomposition", "ZernikeTerms", "MaximumTerm", 37, "AberrationType", "Defocus",
                    "XFieldSampling", 11, "YFieldSampling", 11, "PupilSampling", $"S_{r.PupilSampling}x{r.PupilSampling}", "ShowAs", "FalseColor", "Display", "Absolute"); break;
            case "Seidel Coefficients": Set("Wavelength", r.Wavelength); break;
            case "Seidel Diagram":
                r = r with { Wavelength = primary, WavelengthScope = "Primary" };
                Set("IgnoreChromatic", false, "IgnoreDistortion", false, "SuppressFrame", false, "PlotScale", 0d); break;
            case "Field Curvature":
            case "Field Curvature and Distortion":
                Set("Wavelength", r.Wavelength, "ReferenceField", r.Field, "DisplayAs", "Percent", "Distortion", "F_TanTheta",
                    "ScanType", "Plus_Y", "UseDashes", false, "IgnoreVignette", true, "MaximumCurvature", 0d, "MaximumDistortion", 0d, "FieldAspectRatio", 1d); break;
            case "Color Focus Shift":
                r = r with { Wavelength = 0, WavelengthScope = "ContinuousBetweenDefinedExtrema" };
                Set("MaximumShift", 0d, "PupilZone", 0d); break;
            case "Lateral Color":
                r = r with { Wavelength = 0, WavelengthScope = "DefinedExtremaAndPrimaryAiry" };
                Set("AllWavelengths", false, "ShowAiryDisk", true, "UseRealRays", true, "PlotScale", 0d); break;
            case "Axial Aberration":
                r = r with { Wavelength = 0, WavelengthScope = "AllDefined" };
                Set("UseDashes", false, "PlotScale", 0d); break;
            default: throw new InvalidOperationException("Missing explicit extended contract: " + entry.CanonicalAnalysisKey);
        }
        return r with { ZemaxSettings = settings };
    }

    public static void MapWorkbench(AnalysisComparisonEntry entry, CanonicalAnalysisRequest r, Dictionary<string, string> settings)
    {
        if (entry.ZemaxSettingsMapper == "spot-layout")
        {
            settings["RayDensity"] = "6"; settings["NumRings"] = "6"; settings["Pattern"] = "hexapolar";
            settings["Reference"] = "chief"; settings["FieldNumber"] = "0"; settings["DefocusStepMicrometers"] = "50";
            return;
        }
        if (entry.ZemaxSettingsMapper != "contract") return;
        if (MapScans(r, settings)) return;
        if (MapDiffraction(r, settings)) return;
        if (MapEnergy(r, settings)) return;
        void Set(string name, object value) => settings[name] = Convert.ToString(value, CultureInfo.InvariantCulture)!;
        switch (entry.CanonicalAnalysisKey)
        {
            case "Extended Source Encircled Energy":
                Set("SourceFile", r.SourceImagePath!); Set("FieldSize", 0.1); Set("SourceSampling", 7); Set("NumRays", 100000);
                Set("ZemaxCompatibleOutput", true);
                Set("NumPoints", 401); Set("Type", "encircled"); Set("Reference", "centroid"); Set("MaximumDistanceMicrometers", 10); break;
            case "Jones Pupil": Set("GridSize", 17); break;
            case "Footprint Diagram": Set("RayDensity", 10); Set("DeleteVignetted", true); break;
            case "Contrast Loss Map": Set("Sampling", 13); Set("Frequency", 0); Set("Normalize", false); Set("ShowOPD", true); break;
            case "Single Ray Trace": Set("Px", 0); Set("Py", 0.8); Set("GlobalCoordinates", false); Set("Type", "方向余弦"); break;
            case "Y-Ybar": Set("ZemaxCompatible", true); break;
            case "Angle vs Image Height": Set("FieldDensity", 20); break;
            case "Angle vs Image Height - Through Pupil":
            case "Angle vs Image Height - Through Field": Set("NumPoints", 33); Set("Axis", "Y"); Set("SurfaceIndex", r.SurfaceCount - 1); break;
            case "Grid Distortion":
                Set("NumPoints", 13); Set("ReferenceFieldNumber", r.Field); Set("Display", "截面"); Set("Scale", 1); Set("HeightWidthAspect", 1);
                Set("SymmetricMagnification", false); Set("FieldWidth", 0); break;
            case "Full Field Aberration":
                Set("FieldShape", "椭圆"); Set("XFieldWidth", r.MaximumFieldRadius); Set("YFieldWidth", r.MaximumFieldRadius); Set("MaximumTerm", 37);
                Set("Aberration", "离焦"); Set("XFieldSamples", 11); Set("YFieldSamples", 11); Set("PupilSampling", r.PupilSampling);
                Set("DisplayAs", "伪彩色"); Set("DisplayMode", "绝对值"); break;
            case "Field Curvature":
            case "Field Curvature and Distortion":
                Set("NumPoints", 101); Set("ParabasalDelta", 1e-5); Set("ScanDirection", "+y"); Set("IgnoreVignettingFactors", true);
                Set("DistortionType", "F-Tan(Theta)"); Set("DisplayAs", "百分比"); Set("ReferenceFieldNumber", r.Field); break;
            case "Color Focus Shift": Set("MaximumShift", 0); Set("PupilZone", 0); break;
            case "Lateral Color": Set("AllWavelengths", false); Set("ShowAiryDisk", true); Set("UseRealRays", true); Set("GraphScale", 0); break;
            case "Axial Aberration": Set("UseDashes", false); Set("GraphScale", 0); break;
        }
    }
}
