using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZOSAPI;
using ZOSAPI.Analysis.Data;
using ZOSAPI.Analysis.Settings;

// Inspection is deliberately distinct from a common numerical settings contract.
internal sealed class CapabilityAuditAdapter : Adapter
{
    public override void Configure(IAS_ settings)
    {
        if ((string)Host.Request["zemaxSettingsMode"] != "CapabilityInspection") throw new InvalidOperationException("Missing inspection scope");
        settings.Reset();
        var properties = settings.GetType().GetInterfaces().SelectMany(t => t.GetProperties()).GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First());
        string id = (string)Host.Request["analysisType"];
        if (id == "ImageSimulation")
        {
            // This analysis may export a bitmap even when every IAR channel is empty.
            var property = properties["OutputFile"];
            string path = Path.Combine(Host.Output, "native-image.bmp");
            property.SetValue(settings, path, null);
            if ((string)property.GetValue(settings, null) != path) throw new InvalidOperationException("Image output path rejected");
        }
        if (id == "GeometricImageAnalysis")
        {
            // SpotDiagram can hide the irradiance grid. Inspect a raster mode as well.
            var property = properties["ShowAs"];
            var expected = Enum.Parse(property.PropertyType, "GreyScale");
            property.SetValue(settings, expected, null);
            if (!object.Equals(property.GetValue(settings, null), expected)) throw new InvalidOperationException("GIA raster mode rejected");
        }
    }
    public override void Extra(IOpticalSystem system, IAR_ result, Dictionary<string, object> raw)
    {
        raw["inspectionOnly"] = true;
        raw["inspectionSemantics"] = "Native Reset settings, GIA GreyScale or ImageSimulation bitmap output; no common source or Workbench settings contract.";
        string image = Path.Combine(Host.Output, "native-image.bmp");
        raw["bitmapOutput"] = Host.Object("file", "native-image.bmp", "exists", File.Exists(image), "bytes", File.Exists(image) ? new FileInfo(image).Length : 0L);
    }
}
