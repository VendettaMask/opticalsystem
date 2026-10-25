using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.Raytrace;

public sealed partial class RayGenerator
{
    /// <summary>One wavelength across fields, retaining field-major/pupil-major order.</summary>
    internal bool TryGenerateGpuPupilGrid(IReadOnlyList<(double X,double Y)> fields,IReadOnlyList<PupilSample> samples,
        double micrometers,bool applyVignettingFactors,out RealRayBundle? bundle)
    {
        bundle=null;
        var session=SequentialGpuSession.Current;
        if (session is not {Enabled:true} || !_optic.RayAimingEnabled) return false;
        if (!ObjectConjugate.IsInfinite(_optic.SurfaceGroup.Items.FirstOrDefault())) return false;
        try
        {
            var stop=_optic.SurfaceGroup.Items.ToList().FindIndex(surface=>surface.IsStop);
            if (stop<=0) return false;
            var surfaces=_optic.SequentialRayTracer.GpuSurfaces(micrometers*1000);
            var count=checked(fields.Count*samples.Count);
            if (fields.Count is <1 or >101 || count is <1 or >100_000) throw new NotSupportedException("CUDA pupil grids require 1 to 101 fields and at most 100000 rays per wavelength.");
            var rays=new RealRay[count]; var inputs=new SequentialGpuRay[count]; var entrance=EntrancePupilRadius();
            for (var field=0;field<fields.Count;field++)
            {
                var f=fields[field]; var vignetting=applyVignettingFactors ? ResolveVignetting(f.X,f.Y) : PupilVignetting.Identity;
                var targets=ParaxialStopTargets(samples,vignetting) ?? throw new NotSupportedException("No paraxial stop map is available for the CUDA grid.");
                RealRayBundle unaimed;
                using (var cpu=new SequentialGpuSession(DesignComputeMode.Cpu,session.CancellationToken))
                    unaimed=GenerateNormalizedPupilSamples(f.X,f.Y,micrometers,samples,aimAtStop:false,applyVignettingFactors:applyVignettingFactors);
                for (var i=0;i<samples.Count;i++)
                {
                    var j=field*samples.Count+i; rays[j]=unaimed.Rays[i];
                    if (Math.Abs(rays[j].Direction.Z)<.5) throw new NotSupportedException("CUDA pupil-grid aiming requires forward fields within 60 degrees of the axis.");
                    var p=vignetting.Transform(samples[i].X,samples[i].Y);
                    var input=SequentialRayTracer.GpuRay(rays[j]);
                    input.TargetX=targets[i].X; input.TargetY=targets[i].Y;
                    input.PupilX=p.X;input.PupilY=p.Y;input.EntranceRadius=entrance;
                    input.AimTolerance=CircularAperture.BoundaryTolerance(Math.Max(_optic.SurfaceGroup.Items[stop].SemiDiameter,
                        StopAimingSolver.Hypot(input.TargetX,input.TargetY)))/4;
                    inputs[j]=input;
                }
            }
            for (var start=0;start<count;start+=4096)
            {
                var length=Math.Min(4096,count-start);
                var aimed=DesignGpu.Aim(surfaces,inputs.AsSpan(start,length).ToArray(),stop,session.CancellationToken);
                var traces=aimed.Rays.Sum(ray=>(long)ray.AimTraces); SequentialTraceMeasurement.Record(traces);
                session.Record(length,traces,0,aimed.Milliseconds);
                for (var i=0;i<length;i++)
                {
                    var j=start+i;
                    if (aimed.Rays[i].Valid!=0) { rays[j]=rays[j] with {Origin=aimed.Rays[i].Origin}; continue; }
                    var f=fields[j/samples.Count]; var pupil=samples[j%samples.Count];
                    using (var cpu=new SequentialGpuSession(DesignComputeMode.Cpu,session.CancellationToken))
                        rays[j]=GenerateNormalizedPupilSamples(f.X,f.Y,micrometers,[pupil],aimAtStop:true,
                            applyVignettingFactors:applyVignettingFactors,stopTargets:[(inputs[j].TargetX,inputs[j].TargetY)]).Rays[0];
                    session.Recover(1,"CPU extended chief-angle recovery after CUDA pupil-grid aiming.");
                }
            }
            bundle=new RealRayBundle(rays); return true;
        }
        catch (Exception error) when (session.Fallback(error)) { return false; }
    }

    private bool TryGenerateGpuPupils(PupilSample[] samples,IReadOnlyList<(double X,double Y)> targets,
        double wavelength,double entranceRadius,PupilVignetting vignetting,RealRay[] rays,
        Func<int,RealRay> unaimed,Action<int> recover)
    {
        var session=SequentialGpuSession.Current;
        if (session is not {Enabled:true}) return false;
        if (!ObjectConjugate.IsInfinite(_optic.SurfaceGroup.Items.FirstOrDefault()))
        {
            // CUDA's origin-shift aim is for collimated objects. A finite source
            // requires direction aiming with its object point held fixed.
            session.RecordCpuAiming(samples.Length, "Finite-object direction aiming uses formal Core CPU; CUDA propagation and derivatives remain enabled.");
            return false;
        }
        try
        {
            var stopIndex=_optic.SurfaceGroup.Items.ToList().FindIndex(surface=>surface.IsStop);
            if (stopIndex<=0) return false;
            var surfaces=_optic.SequentialRayTracer.GpuSurfaces(wavelength);
            var stop=_optic.SurfaceGroup.Items[stopIndex];
            for (var start=0;start<samples.Length;start+=4096)
            {
                var count=Math.Min(4096,samples.Length-start); var inputs=new SequentialGpuRay[count];
                for (var i=0;i<count;i++)
                {
                    var j=start+i; rays[j]=unaimed(j);
                    if (Math.Abs(rays[j].Direction.Z)<.5) throw new NotSupportedException("CUDA design aiming currently requires forward fields within 60 degrees of the axis.");
                    var p=vignetting.Transform(samples[j].X,samples[j].Y);
                    var value=SequentialRayTracer.GpuRay(rays[j]);
                    value.TargetX=targets[j].X; value.TargetY=targets[j].Y;
                    value.PupilX=p.X;value.PupilY=p.Y;value.EntranceRadius=entranceRadius;
                    value.AimTolerance=CircularAperture.BoundaryTolerance(Math.Max(stop.SemiDiameter,StopAimingSolver.Hypot(value.TargetX,value.TargetY)))/4;
                    inputs[i]=value;
                }
                var aimed=DesignGpu.Aim(surfaces,inputs,stopIndex,session.CancellationToken);
                var traces=aimed.Rays.Sum(ray=>(long)ray.AimTraces);
                SequentialTraceMeasurement.Record(traces);
                session.Record(count,traces,0,aimed.Milliseconds);
                for (var i=0;i<count;i++)
                {
                    var j=start+i;
                    if (aimed.Rays[i].Valid!=0) rays[j]=rays[j] with {Origin=aimed.Rays[i].Origin};
                    else
                    {
                        // Preserve the authoritative extended chief-angle continuation on rare branches.
                        // Recovery is counted separately, including rays ultimately marked untraceable.
                        session.Recover(1,"CPU extended chief-angle recovery after CUDA direct/continuation aiming.");
                        recover(j);
                    }
                }
            }
            return true;
        }
        catch (Exception error) when (session.Fallback(error)) { return false; }
    }
}
