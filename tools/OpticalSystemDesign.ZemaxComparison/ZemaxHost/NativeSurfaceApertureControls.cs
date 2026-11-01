using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ZOSAPI;

internal static class NativeSurfaceApertureControls
{
    // This host owns the application and only writes a new diagnostic copy in
    // its fresh output directory. Never save or change the input document.
    internal static void Apply(IOpticalSystem system)
    {
        if (!Host.Request.ContainsKey("diagnosticSurfaceAperture")) return;
        if (!Host.Request.ContainsKey("formalAcceptanceRequest") || Convert.ToBoolean(Host.Request["formalAcceptanceRequest"])
            || Convert.ToBoolean(Host.Request["captureScreenshots"])) throw new InvalidOperationException("Background diagnostic only");
        var control = (Dictionary<string, object>)Host.Request["diagnosticSurfaceAperture"];
        var index = Convert.ToInt32(control["surface"]);
        if (index < 1 || index >= system.LDE.NumberOfSurfaces - 1) throw new InvalidOperationException("Physical surface required");
        var aperture = system.LDE.GetSurfaceAt(index).ApertureData;
        var before = Host.Properties(aperture);
        if (control.ContainsKey("minimumRadius"))
        {
            var inner = Convert.ToDouble(control["minimumRadius"]); var outer = Convert.ToDouble(control["maximumRadius"]);
            if (!(inner >= 0 && outer > inner && outer <= 1000)) throw new InvalidOperationException("Bounded physical aperture required");
            var factory = aperture.GetType().GetInterfaces().SelectMany(t => t.GetMethods()).First(m => m.Name == "CreateApertureTypeSettings");
            var type = Enum.Parse(factory.GetParameters()[0].ParameterType, "CircularAperture");
            var settings = factory.Invoke(aperture, new[] { type });
            var typed = Property(settings, "_S_CircularAperture").GetValue(settings, null);
            Property(typed, "MinimumRadius").SetValue(typed, inner, null);
            Property(typed, "MaximumRadius").SetValue(typed, outer, null);
            aperture.GetType().GetInterfaces().SelectMany(t => t.GetMethods()).First(m => m.Name == "ChangeApertureTypeSettings").Invoke(aperture, new[] { settings });
        }
        system.UpdateStatus();
        Host.Write("surface-aperture-control.json", Host.Object("surface", index, "original", before, "actual", Host.Properties(aperture),
            "request", control, "inputSaved", false));
        var destination = Path.Combine(Host.Output, "controlled.ZMX");
        if (File.Exists(destination)) throw new IOException("Never overwrite a diagnostic document");
        system.SaveAs(destination);
    }
    private static PropertyInfo Property(object value, string name)
    {
        return value.GetType().GetInterfaces().SelectMany(t => t.GetProperties()).First(p => p.Name == name);
    }
}
