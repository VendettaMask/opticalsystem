using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Rays;

internal static class GridPropagationRules
{
    internal static async Task Run(string root)
    {
        var output = new List<object>();
        foreach (var job in JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"jobs.json"))).RootElement.EnumerateArray())
        {
            var id=job.GetProperty("id").GetString()!;
            var optic=await new ZemaxZmxImporter().ImportFileAsync(Path.Combine(root,id+".ZMX"));
            var entrance=optic.SurfaceGroup.Items[2];var exit=optic.SurfaceGroup.Items[3];
            var material=(GradientIndexMaterial)entrance.MaterialAfter;var grid=(GridGradientIndexProfile)material.Profile;
            var rows=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,id,"model-controls.json"))).RootElement.GetProperty("rows").EnumerateArray().ToArray();
            foreach(var beam in rows.Where(r=>r.GetProperty("input").GetProperty("code").GetString()=="REAX"
                &&r.GetProperty("input").GetProperty("int1").GetInt32()==2))
            {
                var input=beam.GetProperty("input");var wave=input.GetProperty("int2").GetInt32();var hy=input.GetProperty("data2").GetDouble();
                var px=input.GetProperty("data3").GetDouble();var py=input.GetProperty("data4").GetDouble();
                double N(string code,int surface)=>rows.Single(r=>{var p=r.GetProperty("input");return p.GetProperty("code").GetString()==code
                    &&p.GetProperty("int1").GetInt32()==surface&&p.GetProperty("int2").GetInt32()==wave&&p.GetProperty("data2").GetDouble()==hy
                    &&p.GetProperty("data3").GetDouble()==px&&p.GetProperty("data4").GetDouble()==py;}).GetProperty("value").GetDouble();
                var position=new Vector3D(N("REAX",2),N("REAY",2),N("REAZ",2));var direction=new Vector3D(N("REAA",2),N("REAB",2),N("REAC",2));
                var wavelength=optic.Wavelengths[wave-1].Nanometers;
                foreach(var bodyRule in id.StartsWith("dispersion")?new[]{"global-index","square-index","local-index-global-gradient","boundary-momentum","analytic-dispersion"}:new[]{"global-index","square-index","local-index-global-gradient","boundary-momentum"})
                {
                ISpatialRefractiveIndex profile=bodyRule=="analytic-dispersion"?new AnalyticDispersionProfile()
                    :bodyRule=="square-index"?new SquareProfile(grid,wavelength)
                    :bodyRule=="boundary-momentum"?new MomentumProfile(grid,position,wavelength):new GlobalProfile(grid,bodyRule=="local-index-global-gradient");
                var trace=GradientIndexRayIntegrator.TraceToSurface(profile,entrance.CoordinateSystem,entrance.CoordinateSystem.ToGlobalPoint(position),direction,wavelength,
                    exit.Geometry,exit.CoordinateSystem,100,new GradientIndexIntegrationOptions(MaximumStep:.02));
                if(trace.Termination!=GradientIndexTermination.SurfaceReached)throw new InvalidOperationException(trace.Termination.ToString());
                var hit=exit.CoordinateSystem.ToLocalPoint(trace.End.Position);
                foreach(var boundary in new[]{"local","global"})
                {
                    var boundaryPoint=new Vector3D(trace.ProfileLocalPosition.X,trace.ProfileLocalPosition.Y,entrance.Thickness);
                    var index=boundary=="local"?grid.InterfaceRefractiveIndex(boundaryPoint,wavelength):profile.RefractiveIndex(boundaryPoint,wavelength);
                    var outgoing=new RefractiveReflectiveInteractionModel().Interact(new RealRay(hit,trace.End.Direction,wavelength),
                        new SurfaceInteractionContext(new(0,0,1),index,1,wavelength,false));
                    output.Add(new{id,wave,hy,px,py,boundary,bodyRule,
                        xError=hit.X-N("REAX",3),yError=hit.Y-N("REAY",3),
                        dxError=outgoing.Ray.Direction.X-N("REAA",3),dyError=outgoing.Ray.Direction.Y-N("REAB",3),
                        pathError=trace.End.OpticalPathLength-(N("OPTH",3)-N("OPTH",2))});
                }
                }
            }
            Console.WriteLine(id+" complete");
        }
        File.WriteAllText(Path.Combine(root,"propagation-rule-controls.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
    }
    private sealed class GlobalProfile(GridGradientIndexProfile grid,bool localIndex=false):ISpatialRefractiveIndex
    {
        public SpatialIndexSample Evaluate(Vector3D point,double wavelength)
        {
            var sample=grid.Evaluate(point,wavelength);return localIndex?new(grid.InterfaceRefractiveIndex(point,wavelength),sample.Gradient):sample;
        }
        public double RefractiveIndex(Vector3D point,double wavelength)=>Evaluate(point,wavelength).Index;
    }
    // Independent exact field used to generate the custom control's GGD nodes.
    // This analytical hypothesis never supplies product computation.
    private sealed class AnalyticDispersionProfile:ISpatialRefractiveIndex
    {
        public SpatialIndexSample Evaluate(Vector3D p,double wavelength)
        {
            var l2=wavelength*wavelength*1e-6;var root=Math.Sqrt(2.25+.5*p.X+.1*p.Z+(.03+.01*p.Y)*l2);
            return new(.05+root,new(.5/(2*root),.01*l2/(2*root),.1/(2*root)));
        }
        public double RefractiveIndex(Vector3D p,double wavelength)=>Evaluate(p,wavelength).Index;
    }
    private sealed class MomentumProfile:ISpatialRefractiveIndex
    {
        private readonly GlobalProfile _global;
        private readonly double _offset;
        internal MomentumProfile(GridGradientIndexProfile grid,Vector3D entry,double wave)
        {
            _global=new(grid);var local=grid.InterfaceRefractiveIndex(entry,wave);var global=_global.RefractiveIndex(entry,wave);_offset=local*local-global*global;
        }
        public SpatialIndexSample Evaluate(Vector3D p,double wavelength)
        {
            var sample=_global.Evaluate(p,wavelength);var effective=Math.Sqrt(sample.Index*sample.Index+_offset);
            return new(effective,sample.Index/effective*sample.Gradient);
        }
        public double RefractiveIndex(Vector3D p,double wavelength)=>Evaluate(p,wavelength).Index;
    }
    private sealed class SquareProfile:ISpatialRefractiveIndex
    {
        private readonly GlobalProfile _square;
        internal SquareProfile(GridGradientIndexProfile grid,double wavelength)
        {
            var values=new double[grid.Values.Count];var l2=wavelength*wavelength*1e-6;
            var powers=new[]{1d,l2,1/l2,1/(l2*l2),1/Math.Pow(l2,3),1/Math.Pow(l2,4)};
            for(var i=0;i<values.Length/11;i++)
            {
                var s=0d;for(var c=0;c<6;c++)s+=grid.Values[i*11+c+1]*powers[c];var n=grid.Values[i*11]+Math.Sqrt(s);values[i*11]=n*n;
            }
            _square=new(new GridGradientIndexProfile(grid.Nx,grid.Ny,grid.Nz,grid.Origin,grid.Spacing,values));
        }
        public SpatialIndexSample Evaluate(Vector3D p,double wavelength)
        {
            var s=_square.Evaluate(p,wavelength);var n=Math.Sqrt(s.Index);return new(n,s.Gradient/(2*n));
        }
        public double RefractiveIndex(Vector3D p,double wavelength)=>Evaluate(p,wavelength).Index;
    }
}
