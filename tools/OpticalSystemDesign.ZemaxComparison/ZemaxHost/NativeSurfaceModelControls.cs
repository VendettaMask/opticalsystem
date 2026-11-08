using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ZOSAPI;
using ZOSAPI.Editors.MFE;

// Read-only, bounded native operands for validating imported surface definitions.
// These controls never replace a plot capture or supply product computation.
internal static class NativeSurfaceModelControls
{
    internal static void Run(IOpticalSystem system)
    {
        NativePupilLaunchControls.Apply(system);
        if (system.SystemData.Units.LensUnits.ToString() != "Millimeters")
            throw new InvalidOperationException("Explicit millimeter controls required.");
        var inputs = ((IEnumerable)Host.Request["modelOperands"]).Cast<Dictionary<string, object>>().ToArray();
        if (inputs.Length < 1 || inputs.Length > 10000) throw new InvalidOperationException("Bounded controls required.");
        system.UpdateStatus();
        if (Host.Request.ContainsKey("captureIndexApiSchema"))
            Host.Write("index-api-schema.json", new object[] { system.Tools, system.LDE, system.MFE }
                .Select(o => Host.Object("type", o.GetType().FullName,
                    "methods", o.GetType().GetInterfaces().SelectMany(t => t.GetMethods())
                        .Where(m => m.Name.IndexOf("Index", StringComparison.OrdinalIgnoreCase) >= 0
                            || m.Name.IndexOf("ZPL", StringComparison.OrdinalIgnoreCase) >= 0
                            || m.Name.IndexOf("Macro", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(m => m.ToString()).Distinct().ToArray())).ToArray());
        Host.Write("surface-readback.json", Enumerable.Range(0, system.LDE.NumberOfSurfaces)
            .Select(i => Host.Object("surface", i, "properties", Host.Properties(system.LDE.GetSurfaceAt(i)),
                "surfaceData", Host.Properties(system.LDE.GetSurfaceAt(i).SurfaceData),
                "parameters", Enumerable.Range(12, 12).Select(c => Host.Object("column", c,
                    "cell", Host.Properties(system.LDE.GetSurfaceAt(i).GetCellAt(c)))).ToArray())).ToArray());
        var rows = new List<object>();
        foreach (var p in inputs)
        {
            var code = (string)p["code"];
            if (code != "SSAG" && code != "SSLP" && code != "OPTH" && code != "OPDX" && code != "INDX"
                && code != "REAX" && code != "REAY" && code != "REAZ" && code != "REAA" && code != "REAB" && code != "REAC"
                && code != "EFFL" && code != "EFLX" && code != "EFLY" && code != "EXPP" && code != "EXPD" && code != "EPDI" && code != "FCGS" && code != "FCGT"
                && code != "RSCE" && code != "RELI" && code != "EFNO" && code != "SFNO" && code != "TFNO"
                && code != "PARY" && code != "PATY" && code != "PARX" && code != "PATX")
                throw new InvalidOperationException("Undeclared model-control operand.");
            var type = Host.EnumValue<MeritOperandType>(code);
            var schema = system.MFE.AddOperand(); schema.ChangeType(type);
            Host.Write(code + "-schema.json", Enumerable.Range(2, 8)
                .Select(i => Host.Object("column", i, "cell", Host.Properties(schema.GetCellAt(i)))).ToArray());
            system.MFE.RemoveOperandAt(system.MFE.NumberOfOperands);
            var value = system.MFE.GetOperandValue(type, Convert.ToInt32(p["int1"]), Convert.ToInt32(p["int2"]),
                N(p,"data1"), N(p,"data2"), N(p,"data3"), N(p,"data4"), N(p,"data5"), N(p,"data6"));
            rows.Add(Host.Object("input", p, "value", Host.Finite(value)));
        }
        Host.Write("model-controls.json", Host.Object("contract", "NativeSurfaceModelOperandsV1", "rows", rows,
            "savedInput", false, "builtInPlotReplaced", false));
    }
    private static double N(Dictionary<string,object> p, string key) { return Convert.ToDouble(p[key]); }
}
