using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Core.NonSequential;

public static class SequentialOpticalAssemblyConverter
{
    public static NonSequentialDocument Convert(Optic optic,NonSequentialTraceSettings? settings=null)
    {
        ArgumentNullException.ThrowIfNull(optic);
        if (optic.Aperture.Kind == ApertureKind.ObjectConeAngle)
            throw new NotSupportedException("完整 NSC 转换尚不支持物方锥半角发射。");
        Capabilities.OpticCapabilityPreflight.EnsureSupported(optic,Capabilities.OpticCapabilityOperation.Conversion,"完整顺序光学组件");
        if(!optic.Polarization.Unpolarized||optic.Apodization is not (null or Apodization.UniformApodization))
            throw new NotSupportedException("完整 NSC 转换当前只支持无额外瞳面加权的非偏振标量光学系统。");
        var surfaces=optic.SurfaceGroup.Items;
        if(surfaces.Count<4)throw new NotSupportedException("完整转换需要至少两个实体光学界面。");
        if(surfaces.Count>514)throw new NotSupportedException("一次完整转换最多接受 512 个光学界面。");
        if(surfaces[0].MaterialAfter is Materials.AirMaterial { PressureAtmospheres: not 1 })
            throw new NotSupportedException("分段气压作为外部介质时需要显式非序列容器。");
        var origin=surfaces[1].CoordinateSystem;var faces=new List<NonSequentialOpticalInterface>();
        var extended=surfaces.Skip(1).Take(surfaces.Count-2).Any(s=>
        {
            var p=origin.ToLocalPoint(s.CoordinateSystem.Origin);
            var axis=origin.ToLocalDirection(s.CoordinateSystem.ToGlobalDirection(new(0,0,1)));
            return s.Geometry is not (StandardGeometry or PlaneGeometry)||p.X!=0||p.Y!=0||(axis-new Vector3D(0,0,1)).Length!=0;
        });
        for(int index=1;index<surfaces.Count-1;index++)
        {
            Services.ComputationCancellation.ThrowIfCancellationRequested();
            var s=surfaces[index];
            // SequentialRayTracer carries the preceding transmitted medium. Legacy
            // constructors may leave Surface.MaterialBefore at its default Air.
            var incident=surfaces[index-1].MaterialAfter;
            var local=origin.ToLocalPoint(s.CoordinateSystem.Origin);
            var z=origin.ToLocalDirection(s.CoordinateSystem.ToGlobalDirection(new(0,0,1)));
            if(s.Geometry is not (StandardGeometry or PlaneGeometry or EvenAsphereGeometry or OddAsphereGeometry)
                ||s.InteractionModel is not RefractiveReflectiveInteractionModel {IsReflective:false}
                ||s.ScatteringModel is not null||incident.PropagationModel is not HomogeneousPropagationModel||s.MaterialAfter.PropagationModel is not HomogeneousPropagationModel)
                throw new NotSupportedException($"S{s.Number} 的几何、坐标、交互、散射或介质无法完整转换；原非序列场景保持不变。");
            var x=origin.ToLocalDirection(s.CoordinateSystem.ToGlobalDirection(new(1,0,0)));
            var roll=Math.Atan2(x.Y,x.X)*180/Math.PI;
            faces.Add(new(s.Number,local.Z,s.Radius,s.Conic,s.SemiDiameter,s.MechanicalSemiDiameter,
                ComponentSnapshotFactory.FromMaterial(incident),ComponentSnapshotFactory.FromMaterial(s.MaterialAfter),
                ComponentSnapshotFactory.FromCoating(s.CoatingModel),ComponentSnapshotFactory.FromAperture(s.PhysicalAperture),s.IsStop,roll,s.Label,
                extended?OpticalAssemblySurfaceGeometry.FromGeometry(s.Geometry,s.Radius):null,
                extended?OpticalAssemblySurfaceFrame.RelativeTo(origin,s.CoordinateSystem):null));
        }
        var group=new OpticalAssemblyParameters(faces);
        foreach(var f in faces)foreach(var wavelength in optic.Wavelengths)
        {
            Services.ComputationCancellation.ThrowIfCancellationRequested();
            foreach(var material in new[]{f.Before,f.After})
            {
                var n=material.RefractiveIndex(wavelength.Nanometers);var k=material.ExtinctionCoefficient(wavelength.Nanometers);
                if(!double.IsFinite(n)||n<=0||!double.IsFinite(k)||k<0||k/n>1e-6)
                    throw new NotSupportedException($"S{f.SourceSurfaceNumber} 在 {wavelength.Nanometers:G17} nm 超出透明/弱吸收标量实光线范围（k/n≤1e-6）；吸收膜层仍由物理膜系计算。");
            }
        }
        var image=surfaces[^1];
        if(extended)
        {
            var imageFrame=OpticalAssemblySurfaceFrame.RelativeTo(origin,image.CoordinateSystem);
            var minimumImageZ=imageFrame.Origin.Z-(Math.Abs(imageFrame.AxisX.Z)+Math.Abs(imageFrame.AxisY.Z))*image.SemiDiameter;
            if(imageFrame.AxisZ.Z<=0||minimumImageZ<=group.BoundsMaximum.Z+1e-9)
                throw new NotSupportedException("实际像面必须位于完整镜组之后，且有限探测范围不能与镜组实体交叠。");
        }
        if(origin.ToLocalPoint(image.CoordinateSystem.Origin).Z<=faces[^1].VertexZMillimeters
            || !(image.Geometry is PlaneGeometry || image.Geometry is StandardGeometry g && (Math.Abs(g.Radius)<1e-12||double.IsInfinity(g.Radius)))
            || image.CoatingModel is not Coatings.NoneCoatingModel||image.ScatteringModel is not null
            ||image.InteractionModel is not RefractiveReflectiveInteractionModel {IsReflective:false}
            ||NonSequentialOpticalInterface.Identity(ComponentSnapshotFactory.FromMaterial(image.MaterialAfter))!=NonSequentialOpticalInterface.Identity(faces[^1].MaterialAfter))
            throw new NotSupportedException("像面必须是与出射介质一致的实际平面探测面。");
        var imagePort=new NonSequentialOpticalInterface(image.Number,0,0,0,image.SemiDiameter,image.MechanicalSemiDiameter,
            faces[^1].MaterialAfter,faces[^1].MaterialAfter,
            ComponentSnapshotFactory.FromCoating(image.CoatingModel),ComponentSnapshotFactory.FromAperture(image.PhysicalAperture),image.IsStop,sourceLabel:image.Label);
        var items=new[]{new NonSequentialObjectDefinition(Guid.NewGuid(),"完整顺序光学组件",NonSequentialObjectKind.OpticalAssembly,true,true,origin,null,null,group),
            new NonSequentialObjectDefinition(Guid.NewGuid(),"像面探测器",NonSequentialObjectKind.DetectorRectangle,true,true,image.CoordinateSystem,null,null,
                new DetectorRectangleParameters(image.SemiDiameter*2,image.SemiDiameter*2,IncidentSurface:imagePort))};
        return new(optic.Name+" 非序列场景",optic.Wavelengths.Select(w=>new NonSequentialWavelength(w.Label,w.Nanometers,w.Weight,w.IsPrimary)).ToArray(),items,group.Ambient.Name,settings);
    }
}
