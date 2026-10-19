using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Configuration;

if (args.Length != 2) throw new ArgumentException("prepare|workbench <fresh-riflescope-matrix>");
var root = Path.GetFullPath(args[1]);
var snapshot = Path.Combine(root, "models/001-afocal-riflescope-c1.json");
var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(snapshot));
if (!optic.ImageSpaceAfocal || optic.SurfaceGroup.Items.Count != 19 || optic.Fields.Count != 3)
    throw new InvalidDataException("Expected original Afocal Riflescope model.");
var requestsPath = Path.Combine(root, "afocal-controls.json");
if (args[0] == "prepare")
{
    if (File.Exists(requestsPath)) throw new IOException("Use fresh controls.");
    var runtime = new WorkbenchRuntime(optic);
    var nativeJobs = JsonFiles.Read<List<NativeJob>>(Path.Combine(root, "native-jobs.json"));
    var probe = JsonFiles.Read<JsonElement>(nativeJobs[0].Request);
    var controls = new List<Control>();
    foreach (var (key, field, wave, id) in new[]
    {
        ("Ray Fan", 1, 1, "ray-fan-axis-w1"),
        ("Ray Fan", 3, 1, "ray-fan-edge-w1"),
        ("MTF", 0, 0, "fft-mtf-all-fields-poly64"),
        ("Field Curvature and Distortion", 1, 1, "field-curvature-distortion-w1")
    })
    {
        var directory = Path.Combine(root, "afocal-controls", id);
        var entry = AnalysisComparisonRegistry.Get(key);
        var request = new CanonicalAnalysisRequest
        {
            CanonicalAnalysisKey = key,
            Field = field,
            Wavelength = wave,
            PupilSampling = 64,
            ImageSampling = 64,
            GridSize = 64,
            RayCount = 64,
            MaximumFrequency = 0.5 * 60 * 180 / (1000 * Math.PI),
            Apodization = JsonSerializer.Serialize(optic.ToSnapshot().Apodization, JsonFiles.Options),
            UseRayAiming = optic.RayAimingEnabled,
            WavelengthCount = optic.Wavelengths.Count,
            WavelengthScope = wave == 0 ? "AllDefined" : "Selected",
            FieldDefinition = optic.FieldDefinition.ToString(),
            SurfaceCount = optic.SurfaceGroup.Items.Count,
            FieldCount = optic.Fields.Count,
            DefinedFields = optic.Fields.Select(f => new[] { f.X, f.Y }).ToArray(),
            MaximumFieldRadius = 2.5,
            PrimaryWavelengthMicrometers = 0.587,
            WorkbenchSettings = runtime.MergeAnalysisSettings(key, null)
        };
        request = AfocalAnalysisContracts.Configure(request, true, File.ReadAllText(probe.GetProperty("input").GetString()!));
        if (entry.ZemaxSettingsMapper == "contract") request = ExtendedAnalysisContracts.Configure(entry, request, 2);
        request = request with { WorkbenchSettings = AnalysisComparisonRegistry.MapWorkbench(entry, request) };
        JsonFiles.Write(Path.Combine(directory, "workbench/request.json"),
            new WorkbenchJob(snapshot, Path.Combine(directory, "workbench"), request));
        var native = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonSerializer.Serialize(request, JsonFiles.Options), JsonFiles.Options)!;
        native["zosApiPath"] = probe.GetProperty("zosApiPath").GetString();
        native["input"] = probe.GetProperty("input").GetString();
        native["zemaxVersion"] = "2026 R1";
        native["adapter"] = entry.ZemaxSettingsMapper;
        native["analysisType"] = entry.ZemaxAnalysisType;
        native["captureScreenshots"] = false;
        AfocalAnalysisContracts.MapNativePayload(native, request);
        var nativeRequest = Path.Combine(directory, "native/request.json");
        JsonFiles.Write(nativeRequest, native);
        nativeJobs.Add(new NativeJob(id, nativeRequest, Path.Combine(directory, "native")));
        controls.Add(new Control(id, key, directory));
    }
    JsonFiles.Write(requestsPath, controls);
    JsonFiles.Write(Path.Combine(root, "native-jobs.json"), nativeJobs);
    JsonFiles.Write(Path.Combine(root, "afocal-unit-contract.json"), new
    {
        angularUnitAuthority = "Original source UNIT AM; verify native model.units.AfocalModeUnits=ArcMinutes before comparison",
        coreAngularUnit = "Milliradian",
        nativeAngleToMilliradians = 1000 * Math.PI / (180 * 60),
        nativeMtfMaximumCyclesPerArcminute = 0.5,
        coreMtfMaximumCyclesPerMilliradian = 0.5 * 60 * 180 / (1000 * Math.PI),
        focusUnit = "Diopter",
        sampling = 64,
        scope = "Explicit recorded settings matching visible screenshot fields/wavelength/frequency; hidden screenshot settings are not certified identical",
        acceptance = "Existing explicit-ray tolerances unchanged. Angular analysis residuals are diagnostic; no invented acceptance tolerance."
    });
    Console.WriteLine($"Prepared {controls.Count} afocal controls; {nativeJobs.Count} total native jobs.");
}
else if (args[0] == "workbench")
{
    foreach (var control in JsonFiles.Read<Control[]>(requestsPath))
    {
        var path = Path.Combine(control.Directory, "workbench/request.json");
        try
        {
            WorkbenchExecutor.Execute(JsonFiles.Read<WorkbenchJob>(path));
            Console.WriteLine("DONE " + control.Id);
        }
        catch (Exception error)
        {
            JsonFiles.Write(Path.Combine(control.Directory, "workbench/error.json"), new { error = error.ToString() });
            Console.WriteLine("ERROR " + control.Id + ": " + error.Message);
        }
    }
}
else if (args[0] == "formal-config")
{
    var config = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"));
    var angleTolerance = config.Analyses["Single Ray Trace"].Quantities["IncidentAngle"];
    config.Analyses["Ray Fan"] = config.Analyses["Ray Fan"] with
    {
        Field = 1,
        Wavelength = 1,
        RayCount = 64,
        AfocalQuantities = new() { ["IncidentAngle"] = angleTolerance }
    };
    config.Analyses["MTF"] = config.Analyses["MTF"] with
    {
        Field = 0,
        Wavelength = 0,
        PupilSampling = 64,
        ImageSampling = 64,
        MaximumFrequency = 0.5 * 60 * 180 / (1000 * Math.PI)
    };
    config.Analyses["Field Curvature and Distortion"] = config.Analyses["Field Curvature and Distortion"] with { Field = 1, Wavelength = 1 };
    config.Validate();
    JsonFiles.Write(Path.Combine(root, "afocal-comparison-settings.json"), config);
    JsonFiles.Write(Path.Combine(root, "afocal-tolerance-provenance.json"), new
    {
        angularRayFan = "Exact existing Single Ray Trace/IncidentAngle degree tolerances, explicitly selected for this scoped new angle comparison; not derived from focal ImageHeight tolerances.",
        originalFocalPolicy = "Unchanged comparison-settings.json; all focal Quantities retained exactly.",
        modulationAndDistortion = "Exact existing MTF/Modulation and Field Curvature and Distortion/Distortion tolerances.",
        afocalDefocus = "No power tolerance supplied; incompatible Core mm/native diopter output cannot pass."
    });
    Console.WriteLine("Wrote explicit afocal comparison configuration.");
}
else throw new ArgumentException("Unknown mode");

record NativeJob(string Id, string Request, string Output);
record Control(string Id, string Key, string Directory);
