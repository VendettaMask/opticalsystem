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
