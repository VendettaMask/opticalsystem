using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ZOSAPI;
using ZOSAPI.Analysis;
using ZOSAPI.Analysis.Settings.Mtf;

// Numerical diagnostic only. Independent pupil/image sampling on an owned,
// unsaved system; no changes to the source file or an interactive application.
internal static class NativeHuygensFieldSpectra
{
    internal static void Run(IOpticalSystem system)
    {
        if (Convert.ToBoolean(Host.Request["captureScreenshots"])) throw new InvalidOperationException("Background capture required");
        var fields = system.SystemData.Fields;
        if (fields.GetFieldType().ToString() != "Angle" || fields.NumberOfFields < 3)
            throw new InvalidOperationException("Three or more angular fields required");
        var maximum = Enumerable.Range(1, fields.NumberOfFields).Select(i => fields.GetField(i))
            .Max(f => Math.Sqrt(f.X * f.X + f.Y * f.Y));
        var coordinates = ((IEnumerable)Host.Request["diagnosticHuygensFields"]).Cast<Dictionary<string, object>>().ToArray();
        if (coordinates.Length < 2 || coordinates.Length > 32) throw new InvalidOperationException("Bounded field scan required");
        // Keep the original maximum-radius field, so physical field coordinates
        // and normalization do not move while replacing a non-extreme field.
        var selected = Host.Int("field");
        var editable = fields.GetField(selected);
        if (Math.Sqrt(editable.X * editable.X + editable.Y * editable.Y) >= maximum)
            throw new InvalidOperationException("Selected diagnostic field must not define the original maximum");
        var original = Host.Properties(editable);
        var rows = new List<object>();
        for (int i = 0; i < coordinates.Length; i++)
        {
            double x = Convert.ToDouble(coordinates[i]["x"]), y = Convert.ToDouble(coordinates[i]["y"]);
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)
                || Math.Sqrt(x * x + y * y) > maximum * (1 + 1e-12)) throw new InvalidOperationException("Field outside source extent");
            editable.SetXFixed(); editable.SetYFixed(); editable.X = x; editable.Y = y;
            editable.VDX = 0; editable.VDY = 0; editable.VCX = 0; editable.VCY = 0; editable.VAN = 0; editable.TAN = 0;
            system.UpdateStatus();
            if (editable.X != x || editable.Y != y) throw new InvalidOperationException("Native field readback differs");
            var analysis = system.Analyses.New_Analysis_SettingsFirst(AnalysisIDM.HuygensMtf);
            try
            {
                var s = (IAS_HuygensMtf)analysis.GetSettings(); s.Reset();
                new HuygensMtfAdapter().Configure(s);
                var settings = Host.Object("properties", Host.Properties(s), "field", s.Field.GetFieldNumber(),
                    "wavelength", s.Wavelength.GetWavelengthNumber(), "physicalField", Host.Properties(editable));
                var status = analysis.ApplyAndWaitForCompletion();
                if (status != null && status.ErrorCode != ErrorType.Success) throw new CaptureFailure(status.ErrorCode.ToString(), status.Text);
                var result = analysis.GetResults();
                var raw = Host.Capture(result);
                if (result.NumberOfMessages != 0 || result.NumberOfDataSeries != 1) throw new InvalidOperationException("Unusable native spectrum");
                bool text = result.GetTextFile(System.IO.Path.Combine(Host.Output, "field-" + i + ".txt"));
                if (!text) throw new InvalidOperationException("Native spectrum text missing");
                rows.Add(Host.Object("index", i, "x", x, "y", y, "settings", settings, "data", raw));
                Host.Write("spectra.json", Host.Object("complete", false, "originalField", original, "maximumField", maximum,
                    "request", Host.Request, "savedInput", false, "rows", rows));
            }
            finally { analysis.Close(); }
        }
        Host.Write("spectra.json", Host.Object("complete", true, "originalField", original, "maximumField", maximum,
            "request", Host.Request, "savedInput", false, "rows", rows));
    }
}
