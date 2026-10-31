using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ZOSAPI;
using ZOSAPI.Editors.MFE;
using ZOSAPI.Tools.RayTrace;

// Separate from the built-in plot capture: explicit rays/operands retain their
// own identity. A missing plotted sample is never filled in the frozen plot.
internal static class NativePointComparisonCapture
{
    internal static void Run(IOpticalSystem system)
    {
        if (system.SystemData.Units.LensUnits.ToString() != "Millimeters")
            throw new InvalidOperationException("Point comparison requires proven millimeter lens units");
        NativePupilLaunchControls.Apply(system);
        var inputs = ((IEnumerable)Host.Request["pointInputs"]).Cast<Dictionary<string, object>>().ToArray();
        if (inputs.Length == 0 || inputs.Length > 10000) throw new InvalidOperationException("Bounded explicit input list required");
        var initialThicknesses=Enumerable.Range(1,system.LDE.NumberOfSurfaces-2).Select(i=>system.LDE.GetSurfaceAt(i).Thickness).ToArray();
        Host.Write("point-reference-settings.json",Host.Object("wavelengths",Host.Properties(system.SystemData.Wavelengths),
            "aperture",Host.Properties(system.SystemData.Aperture)));
        if(Host.Request.ContainsKey("freezeThicknessSolves") && Convert.ToBoolean(Host.Request["freezeThicknessSolves"]))
        {
            var frozen=new List<object>();
            for(var i=1;i<system.LDE.NumberOfSurfaces-1;i++)
            {
                var s=system.LDE.GetSurfaceAt(i); var value=s.Thickness; var solve=s.ThicknessCell.Solve.ToString();
                if(!s.ThicknessCell.MakeSolveFixed()) throw new InvalidOperationException("Cannot freeze explicit prescription thickness");
                frozen.Add(Host.Object("surface",i,"thickness",value,"originalSolve",solve,"workingSolve",s.ThicknessCell.Solve.ToString()));
            }
            Host.Write("frozen-thickness-solves.json",frozen);
        }
        if (Host.Request.ContainsKey("referencePrimaryWavelength"))
        {
            var wave = system.SystemData.Wavelengths.GetWavelength(Host.Int("referencePrimaryWavelength"));
            if(Math.Abs(wave.Wavelength-Convert.ToDouble(Host.Request["primaryWavelengthMicrometers"]))>1e-12)
                throw new InvalidOperationException("Canonical primary wavelength differs from explicit reference setting");
            Host.Write("reference-wavelength-api.json",Host.Object("properties",Host.Properties(wave),
                "methods",wave.GetType().GetInterfaces().SelectMany(t => t.GetMethods()).Select(m => m.Name).Distinct().ToArray()));
            var makePrimary = wave.GetType().GetInterfaces().SelectMany(t => t.GetMethods()).FirstOrDefault(m => m.Name == "MakePrimary");
            if (makePrimary == null) throw new InvalidOperationException("Primary wavelength selector unavailable");
            makePrimary.Invoke(wave,null);
            if (!Convert.ToBoolean(Host.Properties(wave)["IsPrimary"]))
                throw new InvalidOperationException("Primary wavelength readback differs");
            Host.Write("reference-wavelength-readback.json",Host.Properties(wave));
        }
        system.UpdateStatus();
        if ((string)Host.Request["pointKind"] == "centroid")
        {
            int sampling = Host.Int("centroidSampling");
            if (sampling < 2 || sampling > 256) throw new InvalidOperationException("Bounded centroid sampling required");
            var centroids = new List<object>();
            foreach (var point in inputs)
            {
                var values = new Dictionary<string, object>();
                foreach (var code in new[] { "CNAX", "CNAY", "CNPX", "CNPY" })
                    values[code] = Host.Finite(system.MFE.GetOperandValue(Host.EnumValue<MeritOperandType>(code),
                        0, Host.Int("wavelength"), N(point,"hx"), N(point,"hy"), 0, sampling, 0, 0));
                centroids.Add(Host.Object("hx",N(point,"hx"),"hy",N(point,"hy"),"values",values));
            }
            Host.Write("centroid-reference.json",Host.Object("contract","NativeCentroidOperandsV1",
                "sampling",sampling,"wavelength",Host.Int("wavelength"),"angleUnit","Radian",
                "positionUnit","Millimeter","centroids",centroids,"savedInput",false));
            return;
        }
        var diameter = system.MFE.GetOperandValue(MeritOperandType.EPDI, 0, 0, 0, 0, 0, 0, 0, 0);
        if (double.IsNaN(diameter) || double.IsInfinity(diameter) || diameter <= 0) throw new InvalidOperationException("Invalid pupil");
        Host.Write("active-model.json",Host.Object("surfaces",Enumerable.Range(0,system.LDE.NumberOfSurfaces).Select(i=>Host.Properties(system.LDE.GetSurfaceAt(i))).ToArray(),
            "advanced",Host.Properties(system.SystemData.Advanced)));
        if(Host.Request.ContainsKey("freezeThicknessSolves") && Convert.ToBoolean(Host.Request["freezeThicknessSolves"]) &&
            initialThicknesses.Where((v,i)=>v!=system.LDE.GetSurfaceAt(i+1).Thickness).Any())
            throw new InvalidOperationException("Frozen prescription thickness changed");
        var surfaces = new List<object>();
        bool imageGeometryOnly = Host.Request.ContainsKey("imageGeometryOnly") && Convert.ToBoolean(Host.Request["imageGeometryOnly"]);
        var tool = system.Tools.OpenBatchRayTrace();
        try
        {
            if (tool == null) throw new InvalidOperationException("Native batch ray tool unavailable");
            var launches = imageGeometryOnly ? new object[0] : inputs.Select(p => {
                double x,y,z,l,m,n;
                var ok=tool.GetDirectFieldCoordinates(Host.Int("wavelength"),RaysType.Real,N(p,"hx"),N(p,"hy"),N(p,"px"),N(p,"py"),out x,out y,out z,out l,out m,out n);
                return Host.Object("success",ok,"x",Host.Finite(x),"y",Host.Finite(y),"z",Host.Finite(z),"l",Host.Finite(l),"m",Host.Finite(m),"n",Host.Finite(n));
            }).ToArray();
            Host.Write("direct-launches.json",launches);
            for (var surface = imageGeometryOnly ? system.LDE.NumberOfSurfaces : 1; surface <= system.LDE.NumberOfSurfaces; surface++)
            {
                var index=Math.Min(surface,system.LDE.NumberOfSurfaces-1);
                var data = tool.CreateNormUnpol(inputs.Length, RaysType.Real, index);
                foreach (var p in inputs)
                    if (!data.AddRay(Host.Int("wavelength"), N(p,"hx"), N(p,"hy"), N(p,"px"), N(p,"py"),
                        surface == system.LDE.NumberOfSurfaces - 1 ? OPDMode.CurrentAndChief : OPDMode.None))
                        throw new InvalidOperationException("Explicit point ray rejected");
                tool.RunAndWaitForCompletion();
                if (!data.StartReadingResults()) throw new InvalidOperationException("No native point results");
                int number, error, vignette;
                double x,y,z,l,m,n,nx,ny,nz,opd,intensity;
                var rows = new List<object>();
                while (data.ReadNextResult(out number,out error,out vignette,out x,out y,out z,out l,out m,out n,
                    out nx,out ny,out nz,out opd,out intensity))
                    rows.Add(Host.Object("number",number,"error",error,"vignette",vignette,"x",Host.Finite(x),"y",Host.Finite(y),
                        "z",Host.Finite(z),"l",Host.Finite(l),"m",Host.Finite(m),"n",Host.Finite(n),"opdWaves",Host.Finite(opd),"intensity",Host.Finite(intensity)));
                if (rows.Count != inputs.Length) throw new InvalidOperationException("Incomplete native ray result");
                if(surface==system.LDE.NumberOfSurfaces) Host.Write("image-geometry.json",rows);
                else surfaces.Add(Host.Object("surface",surface,"opdMode",surface == system.LDE.NumberOfSurfaces - 1 ? "CurrentAndChief" : "None","rays",rows)); data.ClearData();
            }
        }
        finally { if (tool != null) tool.Close(); }
        var operands = new List<object>();
        if ((string)Host.Request["pointKind"] == "curvature")
        {
            foreach (var code in new[] { "FCGS", "FCGT", "DISG" })
            {
                var type = Host.EnumValue<MeritOperandType>(code);
                var schema = system.MFE.AddOperand(); schema.ChangeType(type);
                Host.Write(code + "-schema.json", Enumerable.Range(2,8).Select(i => Host.Object("column",i,"cell",Host.Properties(schema.GetCellAt(i)))).ToArray());
                system.MFE.RemoveOperandAt(system.MFE.NumberOfOperands);
                foreach (var p in inputs.Where(p => N(p,"px") == 0 && N(p,"py") == 0))
                {
                    var value = system.MFE.GetOperandValue(type, code == "DISG" ? 1 : 0, Host.Int("wavelength"),
                        N(p,"hx"), N(p,"hy"), 0,0,0,0);
                    operands.Add(Host.Object("code",code,"hx",N(p,"hx"),"hy",N(p,"hy"),"value",Host.Finite(value)));
                }
            }
        }
        Host.Write("point-reference.json",Host.Object("contract","NativeExplicitPointsV1","units","Millimeters", "opdMode",imageGeometryOnly ? "None" : "CurrentAndChief",
            "imageGeometryOpdMode","None",
            "kind",Host.Request["pointKind"],"wavelength",Host.Int("wavelength"),"inputSha256",Host.Request["inputSha256"],
            "originalKeys",Host.Request["originalKeys"],"inputs",inputs,"surfaces",surfaces,"operands",operands,
            "savedInput",false,"builtInPlotReplaced",false,"imageGeometryOnly",imageGeometryOnly));
        if (Host.Request.ContainsKey("diagnosticPupilOperands") && Convert.ToBoolean(Host.Request["diagnosticPupilOperands"]))
            AfocalDiagnosticCapture.Run(system);
    }
    private static double N(Dictionary<string,object> p,string key) { return Convert.ToDouble(p[key]); }
}
