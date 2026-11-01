using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Core.NonSequential;

public sealed record BsdfMaterialAxis(double X = 1.0, double Y = 0.0, double Z = 0.0)
{
	[JsonIgnore]
	public Vector3D Direction => new Vector3D(X, Y, Z);

	internal void Validate()
	{
		BsdfContext.Unit(Direction);
	}
}
