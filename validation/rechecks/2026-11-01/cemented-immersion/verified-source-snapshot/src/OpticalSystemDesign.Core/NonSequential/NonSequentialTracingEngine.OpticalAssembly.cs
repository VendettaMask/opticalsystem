using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.NonSequential;

public sealed partial class NonSequentialDocumentTracer
{
    private static LocalHit? IntersectOpticalAssembly(Vector3D origin,Vector3D direction,OpticalAssemblyParameters group)
    {
        var faces=group.Interfaces;double best=double.PositiveInfinity;LocalHit? result=null;
        for(int i=0;i<faces.Count;i++)
        {
            if((i&31)==0)Services.ComputationCancellation.ThrowIfCancellationRequested();
            var f=faces[i];var localOrigin=origin-new Vector3D(0,0,f.VertexZMillimeters);
            var hit=f.Geometry.DistanceToIntersection(localOrigin,direction);
            if(!hit.IsHit||hit.Distance<=0||hit.Distance>=best)continue;
            var point=origin+direction*hit.Distance;
            if(point.X*point.X+point.Y*point.Y>f.MechanicalSemiDiameterMillimeters*f.MechanicalSemiDiameterMillimeters)continue;
            best=hit.Distance;var oriented=hit.Normal.Z<0?-hit.Normal:hit.Normal;var normal=i==0?-oriented:oriented;
            result=new(point,normal,i+1,Dot(direction,normal)<0,true);
        }
        for(int i=0;i<faces.Count-1;i++)
        {
            if(group.IsAmbientRegion(i))continue;
            var a=faces[i];var b=faces[i+1];var z0=a.EdgeZ;var z1=b.EdgeZ;
            var slope=(b.MechanicalSemiDiameterMillimeters-a.MechanicalSemiDiameterMillimeters)/(z1-z0);
            var radius=a.MechanicalSemiDiameterMillimeters+slope*(origin.Z-z0);
            foreach(var t in Quadratic(direction.X*direction.X+direction.Y*direction.Y-slope*slope*direction.Z*direction.Z,
                2*(origin.X*direction.X+origin.Y*direction.Y-radius*slope*direction.Z),origin.X*origin.X+origin.Y*origin.Y-radius*radius))
            {
                if(t<=0||t>=best)continue;var p=origin+direction*t;if(p.Z<z0||p.Z>z1)continue;
                var r=Math.Sqrt(p.X*p.X+p.Y*p.Y);if(r==0)continue;
                var normal=Normalize(new(p.X/r,p.Y/r,-slope));best=t;result=new(p,normal,faces.Count+i+1,Dot(direction,normal)<0,true);
            }
        }
        return result;
    }

    private sealed record AssemblyBoundary(IMaterial Incident,IMaterial Destination,ICoatingModel Coating,bool Reverse,bool Clipped);
    private static AssemblyBoundary ResolveAssemblyBoundary(OpticalAssemblyParameters group,SceneHit hit)
    {
        var index=hit.FaceNumber-1;
        if(index<group.Interfaces.Count)
        {
            var face=group.Interfaces[index];var p=hit.LocalPoint-new Vector3D(0,0,face.VertexZMillimeters);
            var oriented=face.Geometry.SurfaceNormal(p);if(oriented.Z<0)oriented=-oriented;
            var forward=Dot(hit.LocalDirection,oriented)>0;
            return new(forward?face.Before:face.After,forward?face.After:face.Before,face.Film,!forward,
                !face.PassesAperture(p));
        }
        var material=group.Interfaces[index-group.Interfaces.Count].After;
        return hit.Entering?new(group.Ambient,material,new NoneCoatingModel(),false,false):new(material,group.Ambient,new NoneCoatingModel(),false,false);
    }

    private static (Vector3D Reflection,Vector3D Transmission,double R,double T,bool Tir) AssemblyOptics(
        RealRay ray,Vector3D normal,AssemblyBoundary boundary)
    {
        var wave=ray.WavelengthNanometers;var n1=boundary.Incident.RefractiveIndex(wave);var n2=boundary.Destination.RefractiveIndex(wave);
        var k1=boundary.Incident.ExtinctionCoefficient(wave);var k2=boundary.Destination.ExtinctionCoefficient(wave);
        if(!double.IsFinite(n1)||!double.IsFinite(n2)||n1<=0||n2<=0||!double.IsFinite(k1)||!double.IsFinite(k2)||k1<0||k2<0)throw new InvalidDataException("复合镜组材料 n/k 无效。");
        if(k1/n1>1e-6||k2/n2>1e-6)throw new NotSupportedException("复合镜组实光线方向仅支持透明/弱吸收体介质（k/n≤1e-6）。");
        var context=new SurfaceInteractionContext(normal,n1,n2,wave,false,IncidentDirection:ray.Direction,ExtinctionCoefficientBefore:k1,ExtinctionCoefficientAfter:k2);
        var transmitted=new RefractiveReflectiveInteractionModel().Interact(ray,context);
        var reflected=new RefractiveReflectiveInteractionModel(true).Interact(ray,context with{IsReflective=true});
        var tir=transmitted.Kind==RayInteractionKind.TotalInternalReflection;
        double r,t;
        if(boundary.Coating is NoneCoatingModel)
        {
            var cosine=Math.Clamp(Math.Abs(Dot(ray.Direction,normal)),1e-15,1);
            var coefficients=FresnelPower.EvaluateHomogeneousPower(new(n1,k1),new(n2,k2),cosine);
            r=(coefficients.ReflectionS.Magnitude*coefficients.ReflectionS.Magnitude+coefficients.ReflectionP.Magnitude*coefficients.ReflectionP.Magnitude)/2;
            t=tir?0:(coefficients.TransmissionS.Magnitude*coefficients.TransmissionS.Magnitude+coefficients.TransmissionP.Magnitude*coefficients.TransmissionP.Magnitude)/2;
        }
        else
        {
            var coating=boundary.Reverse&&boundary.Coating is CoherentMultilayerCoating multilayer?new CoherentMultilayerCoating(multilayer.Layers.Reverse()):boundary.Coating;
            var unit=ray with{Intensity=1};r=coating.Apply(unit,context with{IsReflective=true}).Intensity;
            t=tir?0:coating.Apply(unit,context).Intensity;
        }
        if(!double.IsFinite(r)||!double.IsFinite(t)||r<0||t<0||r+t>1+1e-12)throw new InvalidDataException("逐面镀膜产生无效或非被动 R/T。");
        return(Normalize(reflected.Ray.Direction),Normalize(transmitted.Ray.Direction),r,t,tir);
    }

    private static double AssemblyOffset(SceneHit hit)=>64*2.2204460492503131e-16*Math.Max(1,Math.Max(hit.WorldPoint.Length,((OpticalAssemblyParameters)hit.Item.Parameters).MaximumRadius));
}
