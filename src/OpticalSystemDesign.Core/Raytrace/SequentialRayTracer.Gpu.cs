using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Raytrace;

public sealed partial class SequentialRayTracer
{
    internal SequentialGpuSurface[] GpuSurfaces(double wavelength)
    {
        var surfaces=_optic.SurfaceGroup.Items;
        if (surfaces.Count is <2 or >128 || !ObjectConjugate.IsInfinite(surfaces[0])
            || !_optic.Polarization.Unpolarized)
            throw new NotSupportedException("CUDA design propagation requires an infinite, unpolarized rotational system with 2 to 128 surfaces.");
        var result=new SequentialGpuSurface[surfaces.Count];
        for (var i=0;i<surfaces.Count;i++)
        {
            var s=surfaces[i]; var frame=s.CoordinateSystem;
            if (s.Geometry is not (PlaneGeometry or StandardGeometry or EvenAsphereGeometry)
                || s.PhysicalAperture is not (null or CircularAperture)
                || s.InteractionModel is not RefractiveReflectiveInteractionModel {IsReflective:false}
                || s.IsReflective || s.MaterialAfter.PropagationModel is not HomogeneousPropagationModel
                || s.CoatingModel is not NoneCoatingModel
                || s.ScatteringModel is not null
                || (frame.ToGlobalDirection(new(1,0,0))-new Vector3D(1,0,0)).Length>1e-14
                || (frame.ToGlobalDirection(new(0,1,0))-new Vector3D(0,1,0)).Length>1e-14
                || (frame.ToGlobalDirection(new(0,0,1))-new Vector3D(0,0,1)).Length>1e-14)
                throw new NotSupportedException("CUDA design propagation supports untilted plane/standard/even-aspheric surfaces, homogeneous transparent media, circular apertures and no coatings/scattering.");
            var n=s.MaterialAfter.RefractiveIndex(wavelength);
            if (!double.IsFinite(n) || n<=0) throw new NotSupportedException("CUDA propagation requires finite positive material indices.");
            var extinction=s.MaterialAfter.ExtinctionCoefficient(wavelength);
            if (!double.IsFinite(extinction) || extinction<0) throw new NotSupportedException("CUDA propagation requires finite nonnegative absorption.");
            result[i]=new() {Equation=RotationalSurfaceEquation.From(s.Geometry),Origin=frame.Origin,IndexAfter=n,ExtinctionAfter=extinction,
                ApertureBoundary=s.PhysicalAperture is CircularAperture a ? a.Radius+CircularAperture.BoundaryTolerance(a.Radius) : double.PositiveInfinity,
                Backtrack=AllowsReferenceBacktrack(surfaces,i) ? 1 : 0,Transmission=1,Reflection=1};
        }
        return result;
    }

    internal static SequentialGpuRay GpuRay(RealRay ray) => new()
    { Origin=ray.Origin,Direction=ray.Direction,Intensity=ray.Intensity,Opd=ray.OpticalPathDifference,WavelengthNanometers=ray.WavelengthNanometers,Valid=ray.CanTrace ? 1 : 0 };

    internal SequentialGpuSample[]? TraceGpuNominal(IReadOnlyList<RealRay> sources,CancellationToken token,bool diagnostic=false)
    {
        var session=SequentialGpuSession.Current;
        if (session is not {Enabled:true} || sources.Count==0) return null;
        try
        {
            if (sources.Select(ray=>ray.WavelengthNanometers).Distinct().Count()!=1) throw new NotSupportedException("A nominal CUDA bundle must be monochromatic.");
            var surfaces=GpuSurfaces(sources[0].WavelengthNanometers);
            if (diagnostic) for (var i=0;i<surfaces.Length;i++) surfaces[i].Backtrack=0;
            var results=new SequentialGpuSample[sources.Count*surfaces.Length];
            for (var start=0;start<sources.Count;start+=4096)
            {
                var count=Math.Min(4096,sources.Count-start);
                var input=Enumerable.Range(start,count).Select(i=>GpuRay(sources[i].Normalize())).ToArray();
                var data=DesignGpu.Propagate(surfaces,input,true,surfaces.Length-1,token,ignoreAbsorption:!diagnostic);
                Array.Copy(data.Samples,0,results,start*surfaces.Length,data.Samples.Length);
                SequentialTraceMeasurement.Record(count); session.Record(0,0,count,data.Milliseconds);
            }
            return results;
        }
        catch (Exception error) when (session.Fallback(error)) { return null; }
    }

    private bool TryTraceGpu(RealRayBundle bundle,TraceRequest request,int[] slots,int maximum,
        RayTraceSampleValue[] samples,bool[] hasSamples,double[]? finalOpticalPaths,bool[]? hasFinalOpticalPath)
    {
        var session=SequentialGpuSession.Current;
        if (session is not {Enabled:true} || bundle.Rays.Count==0) return false;
        try
        {
            if (request.StopAtIncidentSurfaceIndex is not null || bundle.Rays.Any(ray=>ray.PolarizationMatrix is not null))
                throw new NotSupportedException("This CUDA design trace does not support incident-detector or Jones requests.");
            var count=_optic.SurfaceGroup.Items.Count;
            foreach (var group in bundle.Rays.Select((ray,index)=>(Ray:ray,Index:index)).GroupBy(item=>item.Ray.WavelengthNanometers))
            {
                var surfaces=GpuSurfaces(group.Key);
                foreach (var chunk in group.Chunk(4096))
                {
                    session.CancellationToken.ThrowIfCancellationRequested();
                    var data=DesignGpu.Propagate(surfaces,chunk.Select(item=>GpuRay(item.Ray)).ToArray(),false,maximum,session.CancellationToken);
                    session.Record(0,0,chunk.Length,data.Milliseconds);
                    for (var j=0;j<chunk.Length;j++)
                    {
                        var ray=chunk[j].Index;
                        for (var i=0;i<=maximum;i++)
                        {
                            var v=data.Samples[j*count+i]; if (v.Present==0) continue;
                            if (slots[i]>=0)
                            {
                                var output=slots[i]*bundle.Rays.Count+ray;
                                samples[output]=GpuSample(v,i); hasSamples[output]=true;
                            }
                            if (i==count-1 && finalOpticalPaths is not null && hasFinalOpticalPath is not null && v.Blocked==0 && v.Miss==0)
                            { finalOpticalPaths[ray]=v.OpticalPath; hasFinalOpticalPath[ray]=true; }
                        }
                    }
                }
            }
            return true;
        }
        catch (Exception error) when (session.Fallback(error))
        {
            Array.Clear(hasSamples,0,bundle.Rays.Count*slots.Count(slot=>slot>=0));
            if (hasFinalOpticalPath is not null) Array.Clear(hasFinalOpticalPath,0,bundle.Rays.Count);
            return false;
        }
    }

    internal RayTraceSampleValue GpuSample(SequentialGpuSample v,int index)
    {
        var surface=_optic.SurfaceGroup.Items[index];
        return new(surface.Number,surface.Label,v.Point,v.Direction,v.Intensity,v.Blocked!=0,
            v.Segment,v.OpticalSegment,v.Path,v.OpticalPath,
            InteractionKind:v.Kind<0 ? null : (RayInteractionKind)v.Kind,
            IncidentDirection:v.Blocked!=0 ? null : v.Incident,
            PhaseInclusiveOpticalPathLength:v.Blocked!=0 ? null : v.Opd,
            RefractiveIndexBefore:index==0 || v.Miss!=0 ? null : v.Before,
            RefractiveIndexAfter:index==0 || v.Miss!=0 ? null : v.After);
    }
}
