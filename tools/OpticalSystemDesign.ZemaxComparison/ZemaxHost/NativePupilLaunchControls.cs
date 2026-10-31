using System;
using System.Collections.Generic;
using System.Linq;
using ZOSAPI;

// Bounded diagnostic controls on this host's owned in-memory system only.
internal static class NativePupilLaunchControls
{
    internal static void Apply(IOpticalSystem system)
    {
        if (!Host.Request.ContainsKey("diagnosticPupilAiming")) return;
        if ((string)Host.Request["pointKind"] != "pupil-scale-origin"
            || !Host.Request.ContainsKey("formalAcceptanceRequest") || Convert.ToBoolean(Host.Request["formalAcceptanceRequest"]))
            throw new InvalidOperationException("Pupil aiming controls require a non-acceptance pupil diagnostic");
        var settings = (Dictionary<string, object>)Host.Request["diagnosticPupilAiming"];
        if (Host.Request.ContainsKey("diagnosticPupilApertureDiameter"))
        {
            var diameter = Convert.ToDouble(Host.Request["diagnosticPupilApertureDiameter"]);
            if (system.SystemData.Aperture.ApertureType.ToString() != "EntrancePupilDiameter"
                || double.IsNaN(diameter) || double.IsInfinity(diameter) || diameter <= 0 || diameter > 1000)
                throw new InvalidOperationException("Invalid diagnostic entrance diameter");
            var originalDiameter = system.SystemData.Aperture.ApertureValue;
            system.SystemData.Aperture.ApertureValue = diameter;
            Host.Write("diagnostic-pupil-aperture.json", Host.Object("original", originalDiameter,
                "requested", diameter, "actual", system.SystemData.Aperture.ApertureValue, "savedInput", false));
        }
        var target = system.SystemData.RayAiming;
        var original = Host.Properties(target);
        foreach (var pair in settings)
        {
            if (pair.Key != "RayAiming" && pair.Key != "UseRayAimingCache")
                throw new InvalidOperationException("Unsupported pupil aiming control");
            var property = target.GetType().GetInterfaces().SelectMany(t => t.GetProperties()).First(p => p.Name == pair.Key);
            object value = pair.Value;
            if (pair.Key == "RayAiming")
            {
                var mode = Convert.ToString(value);
                if (mode != "Off" && mode != "Paraxial") throw new InvalidOperationException("Unsupported aiming mode");
                value = Enum.Parse(property.PropertyType, mode);
            }
            property.SetValue(target, value, null);
            if (Convert.ToString(Host.Properties(target)[pair.Key]) != Convert.ToString(pair.Value))
                throw new InvalidOperationException("Pupil aiming readback differs");
        }
        system.UpdateStatus();
        Host.Write("diagnostic-pupil-aiming.json", Host.Object("original", original,
            "requested", settings, "actual", Host.Properties(target), "savedInput", false, "formalAcceptanceRequest", false));
    }
}
