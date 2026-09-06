// Compiled on the licensed Windows machine against its own ZOS-API assemblies with the .NET Framework compiler.
// C# 5 syntax intentionally keeps this isolated host independent of SDK/NuGet additions and Python.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using ZOSAPI;
using ZOSAPI.Analysis;
using ZOSAPI.Analysis.Data;
using ZOSAPI.Analysis.Settings;
using ZOSAPI.Analysis.Settings.Fans;
using ZOSAPI.Analysis.Settings.Mtf;
using ZOSAPI.Analysis.Settings.Psf;
using ZOSAPI.Analysis.Settings.Spot;
using ZOSAPI.Editors.MFE;

internal static class Host
{
    internal static Dictionary<string, object> Request;
    internal static string Output;
    internal static int Int(string key) { return Convert.ToInt32(Request[key]); }
    internal static double Number(string key) { return Convert.ToDouble(Request[key]); }
    internal static T EnumValue<T>(string name) { return (T)Enum.Parse(typeof(T), name); }
    internal static SampleSizes Sampling(int count) { return EnumValue<SampleSizes>("S_" + count + "x" + count); }
    internal static Dictionary<string, object> Object(params object[] pairs)
    {
        var d = new Dictionary<string, object>();
        for (int i = 0; i < pairs.Length; i += 2) d.Add((string)pairs[i], pairs[i + 1]);
        return d;
    }
    internal static void Write(string name, object value)
    {
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        var path = Path.Combine(Output, name);
        File.WriteAllText(path + ".tmp", serializer.Serialize(value));
        if (File.Exists(path)) File.Delete(path);
        File.Move(path + ".tmp", path);
    }
    internal static object Finite(double n) { return double.IsNaN(n) || double.IsInfinity(n) ? null : (object)n; }
    internal static Dictionary<string, object> Properties(object obj)
    {
        var result = new Dictionary<string, object>();
        if (obj == null) return result;
        foreach (var p in obj.GetType().GetInterfaces().SelectMany(t => t.GetProperties()).GroupBy(p => p.Name).Select(g => g.First()))
        {
            if (p.GetIndexParameters().Length != 0) continue;
            try
            {
                var v = p.GetValue(obj, null);
                if (v == null || v is string || v is bool || v is int || v is uint) result[p.Name] = v;
                else if (v is double) result[p.Name] = Finite((double)v);
                else if (v is float) result[p.Name] = Finite((float)v);
                else if (v.GetType().IsEnum) result[p.Name] = v.ToString();
            }
            catch (Exception e) { result[p.Name] = "Unavailable: " + e.Message; }
        }
        return result;
    }
    [STAThread]
    public static int Main(string[] args)
    {
        Output = args[1]; Directory.CreateDirectory(Output);
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        Request = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(args[0]));
        string api = (string)Request["zosApiPath"];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string candidate = Path.Combine(api, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };
        try { return Run(api); }
        catch (Exception e)
        {
            var failure = e as CaptureFailure;
            Write("error.json", Object("type", e.GetType().FullName, "errorCode", failure == null ? "Internal" : failure.Code, "error", e.ToString()));
            Console.Error.WriteLine(e); return 2;
        }
    }
    private static int Run(string api)
    {
        if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(api)) throw new InvalidOperationException("ZOS-API initializer failed");
        IZOSAPI_Application app = null;
        IA_ analysis = null;
        try
        {
            app = new ZOSAPI_Connection().CreateNewApplication();
            if (app == null) throw new InvalidOperationException("CreateNewApplication returned null");
            var environment = Object("major", app.ZOSMajorVersion, "minor", app.ZOSMinorVersion,
                "servicePack", app.ZOSSPVersion, "opticStudioVersion", app.OpticStudioVersion,
                "licenseStatus", app.LicenseStatus.ToString(), "validLicense", app.IsValidLicenseForAPI,
                "initializationErrors", app.InitializationErrors, "analysisIds", Enum.GetNames(typeof(AnalysisIDM)));
            Write("environment.json", environment);
            if (!app.IsValidLicenseForAPI) throw new CaptureFailure("LicenseUnavailable", app.LicenseStatus.ToString());
            string expected = (string)Request["zemaxVersion"];
            string actual = (2000 + app.ZOSMajorVersion) + " R" + app.ZOSMinorVersion;
            if (actual != expected) throw new InvalidOperationException("Version mismatch: expected " + expected + ", received " + actual);
            app.BeginMessageLogging();
            var system = app.PrimarySystem;
            if (!system.LoadFile((string)Request["input"], false)) throw new InvalidOperationException("Zemax failed to load snapshot");
            system.MCE.SetCurrentConfiguration(Int("configuration"));
            var fields = Enumerable.Range(1, system.SystemData.Fields.NumberOfFields)
                .Select(i => Object("number", i, "data", Properties(system.SystemData.Fields.GetField(i)))).ToArray();
            var waves = Enumerable.Range(1, system.SystemData.Wavelengths.NumberOfWavelengths)
                .Select(i => Object("number", i, "data", Properties(system.SystemData.Wavelengths.GetWavelength(i)))).ToArray();
            Write("model.json", Object("mode", system.Mode.ToString(), "surfaceCount", system.LDE.NumberOfSurfaces,
                "configurationCount", system.MCE.NumberOfConfigurations, "fields", fields, "wavelengths", waves,
                "aperture", Properties(system.SystemData.Aperture), "units", Properties(system.SystemData.Units),
                "rayAiming", Properties(system.SystemData.RayAiming), "environment", Properties(system.SystemData.Environment),
                "surfaces", Enumerable.Range(0, system.LDE.NumberOfSurfaces).Select(i => Properties(system.LDE.GetSurfaceAt(i))).ToArray(),
                "warnings", system.GetCurrentStatus()));
            if ((string)Request["adapter"] == "probe") return 0;
            if ((string)Request["adapter"] == "inspect-settings")
            {
                InspectSettings(system);
                return 0;
            }
            var adapter = Adapter.Create((string)Request["adapter"]);
            var id = EnumValue<AnalysisIDM>((string)Request["analysisType"]);
            analysis = system.Analyses.New_Analysis_SettingsFirst(id);
            if (analysis == null) throw new InvalidOperationException("New_Analysis returned null: " + id);
            var settings = analysis.GetSettings();
            adapter.Configure(settings);
            if (!settings.SaveTo(Path.Combine(Output, "settings.CFG")))
                throw new CaptureFailure("SettingsCaptureFailed", "ZOS-API could not save the configured analysis settings");
            Write("captured-settings.json", Object("origin", "CapturedSettings", "properties", Properties(settings),
                "selectors", Selectors(settings), "request", Request));
            var applied = analysis.ApplyAndWaitForCompletion();
            if (applied != null && applied.ErrorCode != ErrorType.Success)
                throw new CaptureFailure(applied.ErrorCode.ToString(), applied.Text);
            var results = analysis.GetResults();
            var raw = Capture(results);
            adapter.Extra(system, results, raw);
            Write("data.json", raw);
            bool text = results.GetTextFile(Path.Combine(Output, "data.txt"));
            for (var i = 0; i < results.NumberOfMessages; i++)
            {
                var message = results.GetMessageAt(i);
                if (message.ErrorCode != ErrorType.Success || (!string.IsNullOrEmpty(message.Text)
                    && message.Text.IndexOf("calculation cannot proceed", StringComparison.OrdinalIgnoreCase) >= 0))
                    throw new CaptureFailure("NativeAnalysisFailed", message.Text);
            }
            string screenshotStatus = "NotRequested";
            if (Convert.ToBoolean(Request["captureScreenshots"]))
            {
                // Only the disposable working copy is saved, to preserve the selected MCE state for native screenshot export.
                system.SaveAs(Path.Combine(Output, "screenshot-input.ZMX"));
                screenshotStatus = "PendingNativeZplExport";
            }
            Write("capture.json", Object("textSaved", text, "analysisType", id.ToString(), "screenshotStatus", screenshotStatus));
            File.WriteAllText(Path.Combine(Output, "zemax.log"), app.RetrieveLogMessages());
            return 0;
        }
        finally
        {
            if (analysis != null) try { analysis.Close(); } catch (Exception e) { Console.Error.WriteLine(e.Message); }
            if (app != null) try { app.CloseApplication(); } catch (Exception e) { Console.Error.WriteLine(e.Message); }
        }
    }
    private static object[][] Matrix(double[,] values)
    {
        return Enumerable.Range(0, values.GetLength(0)).Select(y =>
            Enumerable.Range(0, values.GetLength(1)).Select(x => Finite(values[y, x])).ToArray()).ToArray();
    }

    private static void InspectSettings(IOpticalSystem system)
    {
        string root = Output;
        foreach (object item in (System.Collections.IEnumerable)Request["analysisTypes"])
        {
            string name = (string)item;
            IA_ a = null;
            try
            {
                Output = Path.Combine(root, name); Directory.CreateDirectory(Output);
                a = system.Analyses.New_Analysis_SettingsFirst(EnumValue<AnalysisIDM>(name));
                var s = a.GetSettings(); s.Reset();
                s.SaveTo(Path.Combine(Output, "reset-settings.CFG"));
                Write("settings-contract.json", Object("analysisType", name, "properties", Properties(s),
                    "selectors", Selectors(s), "interfaces", s.GetType().GetInterfaces().Select(t => t.FullName).ToArray()));
                object inspectResults;
                if (Request.TryGetValue("captureInspectionResults", out inspectResults) && Convert.ToBoolean(inspectResults))
                {
                    var status = a.ApplyAndWaitForCompletion();
                    if (status != null && status.ErrorCode != ErrorType.Success) throw new CaptureFailure(status.ErrorCode.ToString(), status.Text);
                    var result = a.GetResults();
                    var raw = Capture(result); raw["inspectionOnly"] = true;
                    Write("data.json", raw);
                    Write("export.json", Object("textSaved", result.GetTextFile(Path.Combine(Output, "data.txt")),
                        "semantics", "API capability inspection after Reset; no Workbench alignment or numerical equivalence claim."));
                }
            }
            catch (Exception e) { Write("error.json", Object("error", e.ToString())); }
            finally { if (a != null) a.Close(); Output = root; }
        }
    }
    private static Dictionary<string, object> Selectors(IAS_ settings)
    {
        var selectors = new Dictionary<string, object>();
        foreach (var property in settings.GetType().GetInterfaces().SelectMany(t => t.GetProperties()).GroupBy(p => p.Name).Select(g => g.First()))
        {
            var v = property.GetValue(settings, null);
            if (v is IAS_Field) selectors[property.Name] = ((IAS_Field)v).GetFieldNumber();
            if (v is IAS_Wavelength) selectors[property.Name] = ((IAS_Wavelength)v).GetWavelengthNumber();
            if (v is IAS_Surface) selectors[property.Name] = ((IAS_Surface)v).GetSurfaceNumber();
        }
        return selectors;
    }
    private static Dictionary<string, object> Capture(IAR_ r)
    {
        var series = Enumerable.Range(0, r.NumberOfDataSeries).Select(i =>
        {
            var s = r.GetDataSeries(i);
            return Object("index", i, "description", s.Description, "xLabel", s.XLabel, "seriesLabels", s.SeriesLabels,
                "x", s.XData.Data.Select(Finite).ToArray(), "y", Matrix(s.YData.Data));
        }).ToArray();
        var grids = Enumerable.Range(0, r.NumberOfDataGrids).Select(i =>
        {
            var g = r.GetDataGrid(i);
            return Object("index", i, "description", g.Description, "nx", g.Nx, "ny", g.Ny,
                "dx", Finite(g.Dx), "dy", Finite(g.Dy), "minX", Finite(g.MinX), "minY", Finite(g.MinY),
                "xLabel", g.XLabel, "yLabel", g.YLabel, "valueLabel", g.ValueLabel, "values", Matrix(g.Values));
        }).ToArray();
        var captured = Object("dataSeries", series, "dataGrids", grids, "metadata", Properties(r.MetaData),
            "header", r.HeaderData.Lines, "messages", Enumerable.Range(0, r.NumberOfMessages).Select(i => Properties(r.GetMessageAt(i))).ToArray());
        ExtendedCapture.Add(r, captured);
        return captured;
    }
}

internal sealed class CaptureFailure : Exception
{
    internal string Code;
    internal CaptureFailure(string code, string message) : base(message) { Code = code; }
}

internal abstract class Adapter
{
    public abstract void Configure(IAS_ settings);
    public virtual void Extra(IOpticalSystem system, IAR_ result, Dictionary<string, object> raw) { }
    protected void Select(IAS_Field field, IAS_Wavelength wavelength, IAS_Surface surface)
    {
        field.SetFieldNumber(Host.Int("field")); wavelength.SetWavelengthNumber(Host.Int("wavelength"));
        if (surface != null) surface.UseImageSurface();
        if (field.GetFieldNumber() != Host.Int("field") || wavelength.GetWavelengthNumber() != Host.Int("wavelength"))
            throw new InvalidOperationException("ZOS-API rejected field/wavelength selection");
    }
    internal static Adapter Create(string id)
    {
        var adapters = new Dictionary<string, Func<Adapter>> {
            { "first-order", () => new FirstOrderAdapter() }, { "spot", () => new SpotAdapter() },
            { "fan", () => new FanAdapter() }, { "fft-mtf", () => new FftMtfAdapter() },
            { "huygens-mtf", () => new HuygensMtfAdapter() }, { "wavefront", () => new WavefrontAdapter() },
            { "fft-psf", () => new FftPsfAdapter() }, { "huygens-psf", () => new HuygensPsfAdapter() },
            { "contract", () => new ContractAdapter() }, { "spot-layout", () => new ContractAdapter() },
            { "capability-audit", () => new CapabilityAuditAdapter() }
        };
        return adapters[id]();
    }
}
internal sealed class FirstOrderAdapter : Adapter
{
    public override void Configure(IAS_ s) { }
    public override void Extra(IOpticalSystem system, IAR_ result, Dictionary<string, object> raw)
    {
        double efl = system.MFE.GetOperandValue(MeritOperandType.EFFL, 0, 0, 0, 0, 0, 0, 0, 0);
        double epd = system.MFE.GetOperandValue(MeritOperandType.EPDI, 0, 0, 0, 0, 0, 0, 0, 0);
        raw["operands"] = Host.Object("EFFL", Host.Finite(efl), "EPDI", Host.Finite(epd));
        raw["scalars"] = Host.Object("EffectiveFocalLength", Host.Finite(efl), "FNumber", Host.Finite(Math.Abs(efl) / epd));
    }
}
internal sealed class SpotAdapter : Adapter
{
    public override void Configure(IAS_ settings)
    {
        var s = (IAS_Spot)settings; Select(s.Field, s.Wavelength, s.Surface);
        s.Pattern = Patterns.Hexapolar; s.ReferTo = Reference.ChiefRay; s.RayDensity = Host.Int("rayCount");
        s.Configuration = Host.Int("configuration"); s.DirectionCosines = false; s.UsePolarization = false;
        s.ScatterRays = false; s.IgnoreLateralColor = false; s.DeltaFocus = 0;
    }
    public override void Extra(IOpticalSystem system, IAR_ result, Dictionary<string, object> raw)
    {
        var spot = result.SpotData;
        raw["spot"] = Host.Object("fields", spot.NumberOfFields, "wavelengths", spot.NumberOfWavelengths,
            "rms", spot.GetRMSSpotSizeFor(Host.Int("field"), Host.Int("wavelength")),
            "geo", spot.GetGeoSpotSizeFor(Host.Int("field"), Host.Int("wavelength")));
    }
}
internal sealed class FanAdapter : Adapter
{
    public override void Configure(IAS_ settings)
    {
        var s = (IAS_Fan)settings; Select(s.Field, s.Wavelength, s.Surface);
        s.NumberOfRays = Host.Int("rayCount"); s.CheckApertures = true; s.VignettedPupil = true;
        s.Tangential = Host.EnumValue<TangentialAberrationComponent>("Aberration_Y");
        s.Sagittal = Host.EnumValue<SagittalAberrationComponent>("Aberration_X");
    }
}
internal sealed class FftMtfAdapter : Adapter
{
    public override void Configure(IAS_ settings)
    {
        var s = (IAS_FftMtf)settings; Select(s.Field, s.Wavelength, s.Surface);
        s.SampleSize = Host.Sampling(Host.Int("pupilSampling")); s.MaximumFrequency = Host.Number("maximumFrequency");
        s.UsePolarization = false; s.ShowDiffractionLimit = false; s.Type = Host.EnumValue<MtfTypes>("Modulation");
    }
}
internal sealed class HuygensMtfAdapter : Adapter
{
    public override void Configure(IAS_ settings)
    {
        var s = (IAS_HuygensMtf)settings; Select(s.Field, s.Wavelength, null);
        s.PupilSampleSize = Host.Sampling(Host.Int("pupilSampling")); s.ImageSampleSize = Host.Sampling(Host.Int("imageSampling"));
        s.MaximumFrequency = Host.Number("maximumFrequency"); s.ImageDelta = Host.Number("imageDeltaMicrometers");
        s.UsePolarization = false; s.Configuration = Host.Int("configuration");
        s.Type = Host.EnumValue<HuygensMtfTypes>("Modulation");
    }
}
internal sealed class WavefrontAdapter : Adapter
{
    public override void Configure(IAS_ settings)
    {
        var s = (IAS_WavefrontMap)settings; Select(s.Field, s.Wavelength, s.Surface);
        s.Sampling = Host.Sampling(Host.Int("pupilSampling")); s.RemoveTilt = false; s.UseExitPupil = true;
        s.ReferenceToPrimary = false; s.Subaperture_X = 0; s.Subaperture_Y = 0; s.Subaperture_R = 1;
        s.Rotation = Host.EnumValue<Rotations>("Rotate_0"); s.Polarization = Host.EnumValue<Polarizations>("None");
    }
}
internal sealed class FftPsfAdapter : Adapter
{
    public override void Configure(IAS_ settings)
    {
        var s = (IAS_FftPsf)settings; Select(s.Field, s.Wavelength, s.Surface);
        s.SampleSize = Host.EnumValue<PsfSampling>("PsfS_" + Host.Int("pupilSampling") + "x" + Host.Int("pupilSampling"));
        s.OutputSize = Host.EnumValue<PsfSampling>("PsfS_" + Host.Int("imageSampling") + "x" + Host.Int("imageSampling"));
        s.ImageDelta = Host.Number("imageDeltaMicrometers"); s.UsePolarization = false; s.Normalize = false;
        s.Rotation = Host.EnumValue<PsfRotation>("CW0"); s.Type = Host.EnumValue<FftPsfType>("Linear");
    }
}
internal sealed class HuygensPsfAdapter : Adapter
{
    public override void Configure(IAS_ settings)
    {
        var s = (IAS_HuygensPsf)settings; Select(s.Field, s.Wavelength, null);
        s.PupilSampleSize = Host.Sampling(Host.Int("pupilSampling")); s.ImageSampleSize = Host.Sampling(Host.Int("imageSampling"));
        s.ImageDelta = Host.Number("imageDeltaMicrometers"); s.UsePolarization = false; s.UseCentroid = false;
        s.Normalize = false; s.Configuration = Host.Int("configuration"); s.Type = Host.EnumValue<HuygensPsfTypes>("Linear");
        s.Rotation = Host.EnumValue<Rotations>("Rotate_0");
    }
}
