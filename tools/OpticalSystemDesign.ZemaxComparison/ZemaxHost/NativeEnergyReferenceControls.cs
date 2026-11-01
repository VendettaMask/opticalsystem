using System;
using System.Collections.Generic;
using ZOSAPI;

// Independent, explicitly diagnostic controls; never save the owned system.
internal static class NativeEnergyReferenceControls
{
    internal static void Apply(IOpticalSystem system)
    {
        if (!Host.Request.ContainsKey("diagnosticEnergyReference")) return;
        if (!Convert.ToBoolean(Host.Request["diagnosticEnergyReference"])
            || (string)Host.Request["analysisType"] != "GeometricEncircledEnergy"
            || !Host.Request.ContainsKey("formalAcceptanceRequest") || Convert.ToBoolean(Host.Request["formalAcceptanceRequest"])
            || Convert.ToBoolean(Host.Request["captureScreenshots"]))
            throw new InvalidOperationException("Energy reference controls require a background non-acceptance diagnostic");
        var rows = new List<object>();
        for (var i = 1; i < system.LDE.NumberOfSurfaces - 1; i++)
        {
            var surface = system.LDE.GetSurfaceAt(i);
            var thickness = surface.Thickness; var solve = surface.ThicknessCell.Solve.ToString();
            if (!surface.ThicknessCell.MakeSolveFixed() || surface.Thickness != thickness)
                throw new InvalidOperationException("Cannot freeze energy-control prescription");
            rows.Add(Host.Object("surface", i, "thickness", thickness, "originalSolve", solve));
        }
        var before = Host.Properties(system.SystemData.Aperture);
        system.SystemData.Aperture.AFocalImageSpace = Convert.ToBoolean(Host.Request["imageSpaceAfocal"]);
        system.UpdateStatus();
        for (var i = 1; i < system.LDE.NumberOfSurfaces - 1; i++)
            if (system.LDE.GetSurfaceAt(i).Thickness != Convert.ToDouble(((Dictionary<string, object>)rows[i - 1])["thickness"]))
                throw new InvalidOperationException("Energy-control physical thickness changed");
        Host.Write("diagnostic-energy-reference.json", Host.Object("originalAperture", before,
            "actualAperture", Host.Properties(system.SystemData.Aperture), "frozenThicknesses", rows,
            "savedInput", false, "formalAcceptanceRequest", false));
    }
}
