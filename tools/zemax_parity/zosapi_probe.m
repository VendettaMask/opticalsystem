function result = zosapi_probe(zmxPath, outputPath)
%ZOSAPI_PROBE Verify a standalone OpticStudio connection and read FFT MTF.
%   RESULT = ZOSAPI_PROBE(ZMXPATH) launches exactly one headless
%   OpticStudio instance, loads the supplied sequential ZMX file, runs the
%   FFT MTF analysis with a 64x64 pupil and 50 cycles/mm maximum frequency,
%   and returns connection, system, and data-series metadata.

arguments
    zmxPath (1, 1) string = "C:\Users\19851\Desktop\123456.ZMX"
    outputPath (1, 1) string = ""
end

zemaxDirectory = "D:\Program Files\ANSYS Inc\v261\Zemax OpticStudio";
netHelperPath = fullfile(zemaxDirectory, "ZOSAPI_NetHelper.dll");
zosApiPath = fullfile(zemaxDirectory, "ZOSAPI.dll");
zosApiInterfacesPath = fullfile(zemaxDirectory, "ZOSAPI_Interfaces.dll");

assert(isfile(zmxPath), "ZemaxParity:MissingZmx", ...
    "ZMX file does not exist: %s", zmxPath);
assert(isfile(netHelperPath), "ZemaxParity:MissingZosApi", ...
    "ZOS-API was not found under: %s", zemaxDirectory);

existing = System.Diagnostics.Process.GetProcessesByName("OpticStudio");
if existing.Length > 0
    processIds = arrayfun(@(process) double(process.Id), existing);
    error("ZemaxParity:ExistingInstance", ...
        "Refusing to start a second OpticStudio instance. Close PID(s): %s", ...
        strjoin(string(processIds), ", "));
end

NET.addAssembly(netHelperPath);
initialized = ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize( ...
    char(zemaxDirectory));
assert(initialized, "ZemaxParity:InitializationFailed", ...
    "ZOSAPI_Initializer could not initialize OpticStudio.");

NET.addAssembly(zosApiInterfacesPath);
NET.addAssembly(zosApiPath);
import ZOSAPI.*;

connection = ZOSAPI.ZOSAPI_Connection();
application = connection.CreateNewApplication();
assert(~isempty(application), "ZemaxParity:ConnectionFailed", ...
    "CreateNewApplication returned an empty application.");
cleanup = onCleanup(@() closeApplication(application));

assert(application.IsValidLicenseForAPI, "ZemaxParity:LicenseFailed", ...
    "ZOS-API license check failed. Status: %s", ...
    string(application.LicenseStatus));

system = application.PrimarySystem;
assert(~isempty(system), "ZemaxParity:MissingSystem", ...
    "ZOS-API did not return a primary optical system.");
system.LoadFile(char(zmxPath), false);

analysis = system.Analyses.New_FftMtf();
analysisCleanup = onCleanup(@() closeAnalysis(analysis));
settings = analysis.GetSettings();
settings.MaximumFrequency = 50;
settings.SampleSize = ZOSAPI.Analysis.SampleSizes.S_64x64;
settings.Field.SetFieldNumber(0);
settings.Wavelength.SetWavelengthNumber(0);
analysis.ApplyAndWaitForCompletion();
results = analysis.GetResults();

seriesCount = double(results.NumberOfDataSeries);
pointCounts = zeros(seriesCount, 1);
fftMtf = repmat(struct( ...
    "FrequencyCyclesPerMillimeter", [], ...
    "Tangential", [], ...
    "Sagittal", []), seriesCount, 1);
for index = 1:seriesCount
    series = results.GetDataSeries(index - 1);
    frequency = series.XData.Data.double;
    modulation = series.YData.Data.double;
    pointCounts(index) = numel(frequency);
    fftMtf(index).FrequencyCyclesPerMillimeter = frequency(:).';
    fftMtf(index).Tangential = modulation(:, 1).';
    fftMtf(index).Sagittal = modulation(:, 2).';
end

result = struct( ...
    "Initialized", logical(initialized), ...
    "LicenseValid", logical(application.IsValidLicenseForAPI), ...
    "LicenseStatus", string(application.LicenseStatus), ...
    "Mode", string(application.Mode), ...
    "SystemFile", string(system.SystemFile), ...
    "SurfaceCount", double(system.LDE.NumberOfSurfaces), ...
    "FieldCount", double(system.SystemData.Fields.NumberOfFields), ...
    "WavelengthCount", double(system.SystemData.Wavelengths.NumberOfWavelengths), ...
    "FftMtfSeriesCount", seriesCount, ...
    "FftMtfPointCounts", pointCounts, ...
    "FftMtf", fftMtf);

disp(result);
if strlength(outputPath) > 0
    outputDirectory = fileparts(outputPath);
    if strlength(outputDirectory) > 0 && ~isfolder(outputDirectory)
        mkdir(outputDirectory);
    end
    fileId = fopen(outputPath, "w", "n", "UTF-8");
    assert(fileId >= 0, "ZemaxParity:OutputOpenFailed", ...
        "Could not open output file: %s", outputPath);
    fileCleanup = onCleanup(@() fclose(fileId));
    fwrite(fileId, jsonencode(result, PrettyPrint=true), "char");
    clear fileCleanup;
end

analysis.Close();
clear analysisCleanup;
application.CloseApplication();
clear cleanup;
end

function closeAnalysis(analysis)
if ~isempty(analysis)
    analysis.Close();
end
end

function closeApplication(application)
if ~isempty(application)
    application.CloseApplication();
end
end
