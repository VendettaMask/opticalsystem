using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Scattering;

internal static class SpeosPeakBuilder
{
	internal static MeasuredBsdfData Apply(MeasuredBsdfData data, double ni, double nt, double? white, IReadOnlyList<SpeosCone>? cones = null, SpeosPeakImportOptions? manual = null)
	{
		if ((object)data.ZemaxDefinition != null || (cones == null && (object)manual == null))
		{
			return data;
		}
		_ = data.StoredQuantity;
		bool flag = (object)manual != null;
		if (flag)
		{
			bool flag2 = !double.IsFinite(manual.HalfAngleDegrees);
			if (!flag2)
			{
				double halfAngleDegrees = manual.HalfAngleDegrees;
				bool flag3 = ((halfAngleDegrees <= 0.0 || halfAngleDegrees >= 90.0) ? true : false);
				flag2 = flag3;
			}
			bool flag4 = flag2 || !double.IsFinite(manual.RelativeHeight);
			if (!flag4)
			{
				double halfAngleDegrees = manual.RelativeHeight;
				bool flag3 = ((halfAngleDegrees < 0.0 || halfAngleDegrees > 1.0) ? true : false);
				flag4 = flag3;
			}
			flag = flag4;
		}
		if (flag)
		{
			throw new InvalidDataException("镜面锥半角须在 0–90° 内，高度比例须为 0–1。");
		}
		List<MeasuredBsdfPoint> list = new List<MeasuredBsdfPoint>();
		List<MeasuredBsdfPoint> list2 = new List<MeasuredBsdfPoint>();
		foreach (IGrouping<(BsdfSide, double, double, double), MeasuredBsdfPoint> item in from p in data.Points
			group p by (Side: p.Side, WavelengthNanometers: p.WavelengthNanometers, IncidentThetaDegrees: p.IncidentThetaDegrees, IncidentPhiDegrees: p.IncidentPhiDegrees))
		{
			ComputationCancellation.ThrowIfCancellationRequested();
			MeasuredBsdfPoint first = item.First();
			double rotation = ((data.Coordinates == BsdfCoordinates.FixedMaterialAxes) ? Wrap(180.0 - first.IncidentPhiDegrees) : 0.0);
			double maximumR = item.Max((MeasuredBsdfPoint p) => p.BrdfPerSteradian);
			double maximumT = item.Max((MeasuredBsdfPoint p) => p.BtdfPerSteradian);
			foreach (MeasuredBsdfPoint item2 in item)
			{
				double num = Value(item2, transmission: false);
				double num2 = Value(item2, transmission: true);
				list2.Add(item2 with
				{
					BrdfPerSteradian = num,
					BtdfPerSteradian = num2
				});
				list.Add(item2 with
				{
					BrdfPerSteradian = item2.BrdfPerSteradian - num,
					BtdfPerSteradian = item2.BtdfPerSteradian - num2
				});
			}
			double Value(MeasuredBsdfPoint p, bool transmission)
			{
				double num3 = (transmission ? p.BtdfPerSteradian : p.BrdfPerSteradian);
				if (num3 == 0.0)
				{
					return 0.0;
				}
				SpeosCone[] array2 = cones?.Where((SpeosCone c) => c.Transmission == transmission && Math.Abs(c.Incidence - first.IncidentThetaDegrees) < 1E-09 && (c.Wavelength == 0.0 || Math.Abs(c.Wavelength - first.WavelengthNanometers) < 1E-09)).ToArray();
				SpeosCone speosCone = null;
				if (array2 != null && array2.Length > 0)
				{
					double num4 = array2.Max((SpeosCone c) => c.Rotation);
					double a = rotation;
					if (num4 <= 180.0 && a > 180.0)
					{
						a = 360.0 - a;
					}
					if (num4 == 90.0 && a > 90.0)
					{
						a = 180.0 - a;
					}
					if (num4 == 0.0)
					{
						a = 0.0;
					}
					speosCone = array2.FirstOrDefault((SpeosCone c) => Math.Abs(c.Rotation - a) < 1E-08) ?? throw new InvalidDataException("增强锥缺少展开后的材料旋转节点。");
				}
				if ((object)manual == null && (object)speosCone == null)
				{
					throw new InvalidDataException("增强锥缺少入射节点。");
				}
				double num5 = manual?.HalfAngleDegrees ?? speosCone.HalfAngleDegrees;
				double num6 = manual?.RelativeHeight ?? speosCone.RelativeHeight;
				double num7 = Math.Sin(p.IncidentThetaDegrees * Math.PI / 180.0) * ((!transmission) ? 1.0 : ((p.Side == BsdfSide.Front) ? (ni / nt) : (nt / ni)));
				if (num7 >= 1.0)
				{
					throw new InvalidDataException("镜面增强锥的透射方向不在有效半球内。");
				}
				double num8 = Math.Sqrt(1.0 - num7 * num7);
				double num9 = MeasuredBsdfGrid.Cos(p.OutgoingThetaDegrees);
				double num10 = ((data.Coordinates == BsdfCoordinates.FixedMaterialAxes) ? p.IncidentPhiDegrees : 0.0);
				if (num8 * num9 - num7 * Math.Sqrt(Math.Max(0.0, 1.0 - num9 * num9)) * Math.Cos((p.OutgoingPhiDegrees - num10) * Math.PI / 180.0) < Math.Cos(num5 * Math.PI / 180.0))
				{
					return 0.0;
				}
				double num11 = (transmission ? maximumT : maximumR);
				return Math.Max(0.0, num3 - num11 * num6);
			}
		}
		if (white.HasValue)
		{
			double valueOrDefault = white.GetValueOrDefault();
			if ((object)data.SpeosSpectral == null)
			{
				flag = !double.IsFinite(valueOrDefault);
				if (!flag)
				{
					bool flag4 = ((valueOrDefault < 200.0 || valueOrDefault > 20000.0) ? true : false);
					flag = flag4;
				}
				if (flag)
				{
					throw new InvalidDataException("White Specular 基准波长无效。");
				}
				List<MeasuredBsdfPoint> list3 = new List<MeasuredBsdfPoint>();
				foreach (IGrouping<(BsdfSide, double, double, double, double), MeasuredBsdfPoint> item3 in from p in list2
					group p by (Side: p.Side, IncidentThetaDegrees: p.IncidentThetaDegrees, IncidentPhiDegrees: p.IncidentPhiDegrees, OutgoingThetaDegrees: p.OutgoingThetaDegrees, OutgoingPhiDegrees: p.OutgoingPhiDegrees))
				{
					MeasuredBsdfPoint[] array = item3.OrderBy((MeasuredBsdfPoint p) => p.WavelengthNanometers).ToArray();
					(int, int, double) tuple = MeasuredBsdfGrid.Bracket(array.Select((MeasuredBsdfPoint p) => p.WavelengthNanometers).ToArray(), valueOrDefault);
					double r = array[tuple.Item1].BrdfPerSteradian * (1.0 - tuple.Item3) + array[tuple.Item2].BrdfPerSteradian * tuple.Item3;
					double t = array[tuple.Item1].BtdfPerSteradian * (1.0 - tuple.Item3) + array[tuple.Item2].BtdfPerSteradian * tuple.Item3;
					list3.AddRange(array.Select((MeasuredBsdfPoint p) => p with
					{
						BrdfPerSteradian = r,
						BtdfPerSteradian = t
					}));
				}
				list2 = list3;
			}
		}
		SpeosSpectralDefinition speosSpectral = data.SpeosSpectral;
		SpeosSpectralDefinition speosSpectralDefinition = (((object)speosSpectral != null && speosSpectral.ThetaLinear && white.HasValue) ? speosSpectral with
		{
			RuntimeWhite = true
		} : data.SpeosSpectral);
		string name = data.Name;
		string sourceFileName = data.SourceFileName;
		string sourceSha = data.SourceSha256;
		BsdfCoordinates coordinates = data.Coordinates;
		string? sourceFormat = data.SourceFormat;
		string[] obj = new string[5] { data.ImportNotes, "; declared cone split at retained nodes: excess above relative stored-quantity height inside half-angle; diffuse remains fixed, peak uses SlopePowerTransport/1 with analytic solid-angle Jacobian; ", null, null, null };
		object obj2;
		if ((object)speosSpectralDefinition == null || !speosSpectralDefinition.RuntimeWhite)
		{
			SpeosSpectralDefinition? speosSpectral2 = data.SpeosSpectral;
			obj2 = (((object)speosSpectral2 != null && speosSpectral2.ThetaLinear) ? "theta-linear angular power; White Specular reallocates node-local remaining power before input interpolation; directional commercial equivalence unverified; " : "");
		}
		else
		{
			obj2 = "Runtime White uses the upper input cell's shared power scales and retained conditional angular power; ";
		}
		obj[2] = (string)obj2;
		obj[3] = "white base nm=";
		obj[4] = white?.ToString("G17", CultureInfo.InvariantCulture);
		return new MeasuredBsdfData(name, sourceFileName, sourceSha, list, null, coordinates, sourceFormat, string.Concat(obj), null, data.StoredQuantity, new SpeosPeakDefinition(ni, nt, white, list2, "SlopePowerTransport/1", manual, cones?.Select((SpeosCone c) => new SpeosPeakCone(c.Transmission, c.Rotation, c.Incidence, c.Wavelength, c.HalfAngleDegrees, c.RelativeHeight)).ToArray()), null, speosSpectralDefinition);
	}

	private static double Wrap(double x)
	{
		return (x % 360.0 + 360.0) % 360.0;
	}
}
