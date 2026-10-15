using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ZOSAPI.Analysis.Settings;
using ZOSAPI;
using ZOSAPI.Analysis;
using ZOSAPI.Analysis.Data;
using ZOSAPI.Analysis.Settings.Aberrations;
using System.IO;

// Only explicit, audited bindings from the canonical contract are accepted. Every assignment is read back.
internal sealed class ContractAdapter : Adapter
{
    public override void Extra(IOpticalSystem system, IAR_ result, Dictionary<string, object> raw)
    {
        if (((string)Host.Request["canonicalAnalysisKey"]).StartsWith("Angle vs Image Height - Through ", StringComparison.Ordinal))
            AngleRayCapture.Add(system, raw);
        if ((string)Host.Request["analysisType"] == "SystemData")
        {
            new FirstOrderAdapter().Extra(system, result, raw);
            var scalars = (Dictionary<string, object>)raw["scalars"];
            scalars["EntrancePupilDiameter"] = system.MFE.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.EPDI, 0, 0, 0, 0, 0, 0, 0, 0);
            scalars["ExitPupilDiameter"] = system.MFE.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.EXPD, 0, 0, 0, 0, 0, 0, 0, 0);
        }
        if ((string)Host.Request["analysisType"] != "SeidelDiagram") return;
        var coefficients = system.Analyses.New_Analysis_SettingsFirst(AnalysisIDM.SeidelCoefficients);
        try
        {
            var settings = (IAS_SeidelCoefficients)coefficients.GetSettings(); settings.Reset();
            settings.Wavelength.SetWavelengthNumber(Host.Int("wavelength"));
            if (settings.Wavelength.GetWavelengthNumber() != Host.Int("wavelength")) throw new InvalidOperationException("Auxiliary Seidel wavelength rejected");
            if (!settings.SaveTo(Path.Combine(Host.Output, "coefficients-settings.CFG"))) throw new InvalidOperationException("Auxiliary settings capture failed");
            var status = coefficients.ApplyAndWaitForCompletion();
            if (status != null && status.ErrorCode != ErrorType.Success) throw new CaptureFailure(status.ErrorCode.ToString(), status.Text);
            if (!coefficients.GetResults().GetTextFile(Path.Combine(Host.Output, "coefficients.txt"))) throw new InvalidOperationException("Auxiliary Seidel text export failed");
            raw["auxiliary"] = Host.Object("analysisType", "SeidelCoefficients", "wavelength", settings.Wavelength.GetWavelengthNumber(), "settings", "coefficients-settings.CFG", "text", "coefficients.txt");
        }
        finally { coefficients.Close(); }
    }
    public override void Configure(IAS_ settings)
    {
        settings.Reset();
        if (Host.Request.ContainsKey("zemaxCfgSettings"))
        {
            var cfg = (IDictionary<string, object>)Host.Request["zemaxCfgSettings"];
            if (cfg.Count > 0)
            {
                if ((string)Host.Request["analysisType"] != "FootprintSettings") throw new InvalidOperationException("Unaudited CFG contract");
                var path = Path.Combine(Host.Output, "explicit-settings.CFG");
                if (!settings.SaveTo(path)) throw new InvalidOperationException("CFG SaveTo failed");
                foreach (var pair in cfg)
                {
                    if (!new[] { "FOO_RAYDENSITY", "FOO_SURFACE", "FOO_FIELD", "FOO_WAVELENGTH", "FOO_DELETEVIGNETTED" }.Contains(pair.Key)) throw new InvalidOperationException("Unaudited CFG key");
                    if (!settings.ModifySettings(path, pair.Key, (string)pair.Value)) throw new InvalidOperationException("CFG setting rejected: " + pair.Key);
                }
                if (!settings.LoadFrom(path)) throw new InvalidOperationException("CFG LoadFrom failed");
                return;
            }
        }
        var bindings = (IDictionary<string, object>)Host.Request["zemaxSettings"];
        if (bindings.Count == 0)
        {
            if ((string)Host.Request["zemaxSettingsMode"] == "ModelMetadataAndMfe" && new[] { "SystemData", "PrescriptionDataSettings" }.Contains((string)Host.Request["analysisType"])) return;
            var allowed = new[] { "CardinalPoints", "YYbarDiagram", "IncidentAnglevsImageHeight", "PolarizationPupilMap" };
            if ((string)Host.Request["zemaxSettingsMode"] != "ResetWithReportVerification" || !allowed.Contains((string)Host.Request["analysisType"]))
                throw new InvalidOperationException("Missing explicit Zemax settings contract");
            // These interfaces expose no properties. The normalizer must verify wavelength, range,
            // direction and sample count from the saved native report before accepting any numbers.
            return;
        }
        var properties = settings.GetType().GetInterfaces().SelectMany(t => t.GetProperties()).GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First());
        var expectedValues = new Dictionary<string, object>();
        foreach (var pair in bindings)
        {
            if (!properties.ContainsKey(pair.Key)) throw new InvalidOperationException("Unsupported setting: " + pair.Key);
            var property = properties[pair.Key];
            var current = property.GetValue(settings, null);
            object actual;
            object expected;
            if (current is IAS_Field)
            {
                expected = Convert.ToInt32(pair.Value); ((IAS_Field)current).SetFieldNumber((int)expected);
                actual = ((IAS_Field)current).GetFieldNumber();
            }
            else if (current is IAS_Wavelength)
            {
                expected = Convert.ToInt32(pair.Value); ((IAS_Wavelength)current).SetWavelengthNumber((int)expected);
                actual = ((IAS_Wavelength)current).GetWavelengthNumber();
            }
            else if (current is IAS_Surface)
            {
                expected = Convert.ToInt32(pair.Value);
                var status = (int)expected == -1 ? ((IAS_Surface)current).UseImageSurface() : ((IAS_Surface)current).SetSurfaceNumber((int)expected);
                if (status != null && status.ErrorCode != ErrorType.Success) throw new CaptureFailure(status.ErrorCode.ToString(), status.Text);
                actual = ((IAS_Surface)current).GetSurfaceNumber();
                if ((int)expected == -1)
                {
                    // IAS_Surface implementations return either the image row or the zero image sentinel.
                    int image = Host.Int("surfaceCount") - 1;
                    if ((int)actual != image && (int)actual != 0) throw new InvalidOperationException("Image surface selector differs from the captured model");
                    expected = actual;
                }
            }
            else
            {
                if (pair.Key == "AberrationType" && settings is IAS_FullFieldAberration)
                {
                    expected = Enum.Parse(property.PropertyType, (string)pair.Value, false);
                    if (!((IAS_FullFieldAberration)settings).SetAberrationByType((FFA_AberrationTypes)expected))
                        throw new InvalidOperationException("Aberration selection rejected");
                    actual = property.GetValue(settings, null);
                    if (!object.Equals(actual, expected)) throw new InvalidOperationException("Aberration readback differs");
                    expectedValues.Add(pair.Key, expected);
                    continue;
                }
                if (!property.CanWrite) throw new InvalidOperationException("Read-only setting: " + pair.Key);
                expected = property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType, Convert.ToString(pair.Value, CultureInfo.InvariantCulture), false)
                    : Convert.ChangeType(pair.Value, property.PropertyType, CultureInfo.InvariantCulture);
                if (property.PropertyType.IsEnum && !Enum.IsDefined(property.PropertyType, expected)) throw new InvalidOperationException("Undefined enum setting: " + pair.Key);
                property.SetValue(settings, expected, null); actual = property.GetValue(settings, null);
            }
            if (!object.Equals(actual, expected)) throw new InvalidOperationException("ZOS-API rejected setting " + pair.Key + ": requested " + expected + ", actual " + actual);
            expectedValues.Add(pair.Key, expected);
        }
        // Setting a later property can reset an earlier selector. Verify the
        // complete state after all assignments, not just individual writes.
        foreach (var pair in expectedValues)
        {
            var value = properties[pair.Key].GetValue(settings, null);
            object actual = value is IAS_Field ? (object)((IAS_Field)value).GetFieldNumber()
                : value is IAS_Wavelength ? (object)((IAS_Wavelength)value).GetWavelengthNumber()
                : value is IAS_Surface ? (object)((IAS_Surface)value).GetSurfaceNumber() : value;
            if (!object.Equals(actual, pair.Value)) throw new InvalidOperationException("Final ZOS-API settings readback differs: " + pair.Key);
        }
    }
}
