# Zemax parity probe

`zosapi_export.py` uses the official Python ZOS-API Standalone connection to
load a sequential ZMX file and export an FFT MTF result. `zosapi_probe.m`
provides an equivalent MATLAB connection probe.

Before running it, close every visible and background OpticStudio instance.
The script deliberately refuses to launch when an existing `OpticStudio.exe`
process is present, because a stale or second instance can consume the
available license and make `IsValidLicenseForAPI` return false.

Run from MATLAB:

```matlab
addpath("D:\Projects\opticalsystem\tools\zemax_parity");
result = zosapi_probe("C:\Users\19851\Desktop\123456.ZMX");
```

Run the Python exporter with the Python distribution bundled with Ansys:

```powershell
& "D:\Program Files\ANSYS Inc\v261\commonfiles\CPython\3_10\winx64\Release\python\python.exe" `
  "D:\Projects\opticalsystem\tools\zemax_parity\zosapi_export.py"
```

To export the Zemax FFT MTF frequency, tangential, and sagittal arrays as
JSON:

```matlab
result = zosapi_probe( ...
    "C:\Users\19851\Desktop\123456.ZMX", ...
    "D:\Projects\opticalsystem\artifacts\zemax\123456-fft-mtf.json");
```
