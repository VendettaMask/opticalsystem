using System;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Core.NonSequential;

public sealed record NonSequentialSurfaceBsdf(NonSequentialBsdfKind Kind, double Reflectance, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MeasuredBsdfData? MeasuredData = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BsdfMaterialAxis? MaterialAxis = null)
{
	public IBsdf Create()
	{
		if ((object)MaterialAxis != null)
		{
			MaterialAxis.Validate();
			MeasuredBsdfData? measuredData = MeasuredData;
			if (measuredData == null || measuredData.Coordinates != BsdfCoordinates.FixedMaterialAxes)
			{
				throw new InvalidDataException("材料轴只适用于固定轴各向异性数据。");
			}
		}
		switch (Kind)
		{
		case NonSequentialBsdfKind.LambertianReflection:
			if (MeasuredData == null)
			{
				return new LambertianReflectionBsdf(Reflectance);
			}
			break;
		case NonSequentialBsdfKind.Measured:
			if (MeasuredData != null && Reflectance == 0.0)
			{
				return new MeasuredBsdf(MeasuredData);
			}
			break;
		}
		throw new InvalidDataException("BSDF 模型与实测载荷不一致；实测值不额外乘 Lambertian 反射率。");
	}

	internal void Validate(NonSequentialObjectParameters parameters)
	{
		Create();
		NonSequentialSurfaceBehavior? nonSequentialSurfaceBehavior = ((parameters is PlaneRectangleParameters planeRectangleParameters) ? new NonSequentialSurfaceBehavior?(planeRectangleParameters.Behavior) : ((parameters is SphereParameters sphereParameters) ? new NonSequentialSurfaceBehavior?(sphereParameters.Behavior) : ((parameters is CylinderParameters cylinderParameters) ? new NonSequentialSurfaceBehavior?(cylinderParameters.Behavior) : ((parameters is BoxParameters boxParameters) ? new NonSequentialSurfaceBehavior?(boxParameters.Behavior) : ((!(parameters is MeshObjectParameters meshObjectParameters)) ? ((NonSequentialSurfaceBehavior?)null) : new NonSequentialSurfaceBehavior?(meshObjectParameters.Behavior))))));
		NonSequentialSurfaceBehavior? nonSequentialSurfaceBehavior2 = nonSequentialSurfaceBehavior;
		MeasuredBsdfData? measuredData = MeasuredData;
		if (measuredData != null && measuredData.HasTransmission)
		{
			if (!MeasuredData.HasBothSides || nonSequentialSurfaceBehavior2 != NonSequentialSurfaceBehavior.Refractive)
			{
				throw new InvalidDataException("BTDF 需要正反两侧测量和折射交互，用声明的介质更新传播；不另加 Fresnel/Snell。");
			}
			return;
		}
		bool flag;
		if (nonSequentialSurfaceBehavior2.HasValue)
		{
			NonSequentialSurfaceBehavior valueOrDefault = nonSequentialSurfaceBehavior2.GetValueOrDefault();
			if ((uint)(valueOrDefault - 1) <= 1u)
			{
				flag = true;
				goto IL_0103;
			}
		}
		flag = false;
		goto IL_0103;
		IL_0103:
		if (!flag)
		{
			throw new InvalidDataException("Lambertian BSDF 仅用于不透明反射或吸收表面，替代其整个反射模型；不与折射/Fresnel 叠加。");
		}
	}

	public (Vector3D X, Vector3D Y) WorldAxes(NonSequentialDocument document, Guid objectId)
	{
		Vector3D vector3D = BsdfContext.Unit((MaterialAxis ?? new BsdfMaterialAxis()).Direction);
		Vector3D localDirection = BsdfContext.Unit(BsdfContext.Cross((Math.Abs(vector3D.Z) < 0.9) ? new Vector3D(0.0, 0.0, 1.0) : new Vector3D(0.0, 1.0, 0.0), vector3D));
		return (X: BsdfContext.Unit(document.ToWorldDirection(objectId, vector3D)), Y: BsdfContext.Unit(document.ToWorldDirection(objectId, localDirection)));
	}

	public static string? Protocol(NonSequentialDocument document)
	{
		if (!document.Objects.Any((NonSequentialObjectDefinition o) => o.Enabled && o.SurfaceBsdf?.MeasuredData?.ComputationProtocol == "MeasuredBsdfGrid/5"))
		{
			if (!document.Objects.Any((NonSequentialObjectDefinition o) => o.Enabled && o.SurfaceBsdf?.MeasuredData?.ComputationProtocol == "MeasuredBsdfGrid/4"))
			{
				if (!document.Objects.Any((NonSequentialObjectDefinition o) => o.Enabled && o.SurfaceBsdf?.MeasuredData?.ComputationProtocol == "MeasuredBsdfGrid/3"))
				{
					if (!document.Objects.Any((NonSequentialObjectDefinition o) => o.Enabled && o.SurfaceBsdf?.MeasuredData?.ComputationProtocol == "MeasuredBsdfGrid/2"))
					{
						if (!document.Objects.Any(delegate(NonSequentialObjectDefinition o)
						{
							if (o.Enabled)
							{
								NonSequentialSurfaceBsdf? surfaceBsdf = o.SurfaceBsdf;
								if ((object)surfaceBsdf == null)
								{
									return false;
								}
								return surfaceBsdf.Kind == NonSequentialBsdfKind.Measured;
							}
							return false;
						}))
						{
							if (!document.Objects.Any((NonSequentialObjectDefinition o) => o.Enabled && (object)o.SurfaceBsdf != null))
							{
								return null;
							}
							return "LambertianReflection/1";
						}
						return "MeasuredBsdfGrid/1";
					}
					return "MeasuredBsdfGrid/2";
				}
				return "MeasuredBsdfGrid/3";
			}
			return "MeasuredBsdfGrid/4";
		}
		return "MeasuredBsdfGrid/5";
	}
}
