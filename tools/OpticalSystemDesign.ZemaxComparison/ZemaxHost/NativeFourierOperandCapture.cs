using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ZOSAPI;
using ZOSAPI.Editors.MFE;

// Read-only numerical diagnostics on this host's owned, unsaved system.
internal static class NativeFourierOperandCapture
{
    internal static void Run(IOpticalSystem system)
    {
        if (Host.Request.ContainsKey("fourierFieldCoordinates"))
        {
            var fields = system.SystemData.Fields;
            if (fields.GetFieldType().ToString() != "Angle") throw new InvalidOperationException("Explicit diagnostic fields require angular source fields");
            var original = Enumerable.Range(1, fields.NumberOfFields).Select(i => Host.Properties(fields.GetField(i))).ToArray();
            var maximum = Enumerable.Range(1, fields.NumberOfFields).Select(i => fields.GetField(i))
                .Max(f => Math.Sqrt(f.X * f.X + f.Y * f.Y));
            var coordinates = ((IEnumerable)Host.Request["fourierFieldCoordinates"]).Cast<Dictionary<string, object>>().ToArray();
            if (coordinates.Length < 2 || coordinates.Length > 64) throw new InvalidOperationException("Two to 64 explicit field coordinates required");
            while (fields.NumberOfFields > coordinates.Length) fields.RemoveField(fields.NumberOfFields);
            while (fields.NumberOfFields < coordinates.Length) fields.AddField(0, 0, 1);
            for (var i = 0; i < coordinates.Length; i++)
            {
                var x = Convert.ToDouble(coordinates[i]["x"]); var y = Convert.ToDouble(coordinates[i]["y"]);
                if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)
                    || Math.Sqrt(x * x + y * y) > maximum * (1 + 1e-12)) throw new InvalidOperationException("Diagnostic field outside source extent");
                var field = fields.GetField(i + 1); field.SetXFixed(); field.SetYFixed(); field.X = x; field.Y = y;
                field.Weight = 1; field.VDX = 0; field.VDY = 0; field.VCX = 0; field.VCY = 0; field.VAN = 0; field.TAN = 0;
                if (field.X != x || field.Y != y) throw new InvalidOperationException("Native field coordinate readback differs");
            }
            system.UpdateStatus();
            Host.Write("fourier-fields.json", Host.Object("original", original, "originalMaximum", maximum,
                "actual", Enumerable.Range(1, fields.NumberOfFields).Select(i => Host.Properties(fields.GetField(i))).ToArray(),
                "fieldType", fields.GetFieldType().ToString(), "normalization", fields.Normalization.ToString(), "savedInput", false));
        }
        var inputs = ((IEnumerable)Host.Request["fourierOperandInputs"]).Cast<Dictionary<string, object>>().ToArray();
        if (inputs.Length < 1 || inputs.Length > 64) throw new InvalidOperationException("One to 64 explicit Fourier operand requests required");
        foreach (var code in new[] { "MTFT", "MTFS" })
        {
            var operand = system.MFE.AddOperand();
            try
            {
                operand.ChangeType(Host.EnumValue<MeritOperandType>(code));
                Host.Write(code + "-schema.json", Enumerable.Range(2, 8).Select(i => Host.Object("column", i, "cell", Host.Properties(operand.GetCellAt(i)))).ToArray());
            }
            finally { system.MFE.RemoveOperandAt(system.MFE.NumberOfOperands); }
        }
        var rows = new List<object>();
        var types = Host.Request.ContainsKey("fourierDataTypes")
            ? ((IEnumerable)Host.Request["fourierDataTypes"]).Cast<object>().Select(Convert.ToInt32).ToArray() : new[] { 0, 1, 2 };
        if (types.Length < 1 || types.Length > 3 || types.Distinct().Count() != types.Length || types.Any(t => t < 0 || t > 2))
            throw new InvalidOperationException("Explicit Fourier data types must be unique members of 0, 1, 2");
        foreach (var input in inputs)
        {
            int sampling = Convert.ToInt32(input["sampling"]), wave = Convert.ToInt32(input["wavelength"]), field = Convert.ToInt32(input["field"]);
            int grid = Convert.ToInt32(input["grid"]); double frequency = Convert.ToDouble(input["frequency"]);
            if (sampling < 1 || sampling > 5 || wave < 0 || wave > system.SystemData.Wavelengths.NumberOfWavelengths
                || field < 1 || field > system.SystemData.Fields.NumberOfFields || grid < 0 || grid > 1
                || double.IsNaN(frequency) || double.IsInfinity(frequency) || frequency <= 0 || frequency > 1e6)
                throw new InvalidOperationException("Fourier operand input is outside the diagnostic bounds");
            var values = new Dictionary<string, object>();
            foreach (var code in new[] { "MTFT", "MTFS" }) foreach (var type in types)
                values[code + "/" + type] = Host.Finite(system.MFE.GetOperandValue(Host.EnumValue<MeritOperandType>(code),
                    sampling, wave, field, frequency, grid, type, 0, 0));
            rows.Add(Host.Object("input", input, "values", values));
            Host.Write("fourier-operands.json", Host.Object("configuration", Host.Int("configuration"), "rows", rows,
                "savedInput", false, "replacesReference", false));
        }
    }
}
