// Diagnostic only: raw operand results and schemas, never a replacement PSF scale.
using System;
using System.Collections.Generic;
using System.Linq;
using ZOSAPI;
using ZOSAPI.Editors.MFE;

internal static class AfocalDiagnosticCapture
{
    internal static void Run(IOpticalSystem system)
    {
        // Discover the installed API's NSC settings interface instead of guessing
        // a property name or manufacturing a default glue distance.
        var systemData = system.SystemData;
        var properties = systemData.GetType().GetInterfaces().SelectMany(t => t.GetProperties())
            .Where(p => p.GetIndexParameters().Length == 0).GroupBy(p => p.Name).Select(g => g.First()).ToArray();
        var sections = new List<object>();
        foreach (var property in properties.Where(p => p.Name.IndexOf("NonSequential", StringComparison.OrdinalIgnoreCase) >= 0
            || p.Name.IndexOf("NSC", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            try { sections.Add(Host.Object("name", property.Name, "properties", Host.Properties(property.GetValue(systemData, null)))); }
            catch (Exception e) { sections.Add(Host.Object("name", property.Name, "error", e.ToString())); }
        }
        Host.Write("afocal-nsc-context.json", Host.Object("configuration", Host.Int("configuration"),
            "systemDataPropertyNames", properties.Select(p => p.Name).OrderBy(n => n).ToArray(),
            "sections", sections, "completeNativeEdgeContract", false, "savedInput", false));
        var results = new List<object>();
        foreach (var code in new[] { "EPDI", "EXPD", "ENPP", "EXPP" })
        {
            if (!Enum.IsDefined(typeof(MeritOperandType), code))
            {
                results.Add(Host.Object("code", code, "available", false));
                continue;
            }
            try
            {
                var type = Host.EnumValue<MeritOperandType>(code);
                var operand = system.MFE.AddOperand();
                object schema;
                try
                {
                    operand.ChangeType(type);
                    schema = Enumerable.Range(2, 8).Select(i => Host.Object("column", i,
                        "cell", Host.Properties(operand.GetCellAt(i)))).ToArray();
                }
                finally { system.MFE.RemoveOperandAt(system.MFE.NumberOfOperands); }
                // Record actual call arguments. Interpretation requires the exported native schema.
                var value = system.MFE.GetOperandValue(type, 0, 0, 0, 0, 0, 0, 0, 0);
                results.Add(Host.Object("code", code, "available", true, "arguments", new int[8],
                    "schema", schema, "value", Host.Finite(value)));
            }
            catch (Exception e) { results.Add(Host.Object("code", code, "error", e.ToString())); }
        }
        Host.Write("afocal-pupil-operands.json", Host.Object("configuration", Host.Int("configuration"),
            "results", results, "savedInput", false, "replacesReference", false));
    }
}
