using Speckle.Converter.MicroStation.Extensions;
using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Geometry;
using Speckle.Objects.Primitive;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a MicroStation 2026 COM <see cref="MSIDGN.ArcElement"/> to a Speckle <see cref="Arc"/>.
/// The COM API exposes arc geometry as direct properties: center, radii, angles, and a rotation matrix.
/// </summary>
[NameAndRankValue(typeof(MSIDGN.ArcElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ArcElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((MSIDGN.ArcElement)target);

  private Arc Convert(MSIDGN.ArcElement element)
  {
    var s = settingsStore.Current;

    var center = element.CenterPoint.ToSpecklePoint(s.SpeckleUnits);

    // Matrix3d.RowZ is the normal vector of the arc plane;
    // RowX / RowY are the local x/y axes.
    var rot = element.Rotation;
    var normal = new Vector(rot.RowZ.X, rot.RowZ.Y, rot.RowZ.Z, s.SpeckleUnits);
    var xdir = new Vector(rot.RowX.X, rot.RowX.Y, rot.RowX.Z, s.SpeckleUnits);
    var ydir = new Vector(rot.RowY.X, rot.RowY.Y, rot.RowY.Z, s.SpeckleUnits);

    var plane = new Plane
    {
      origin = center,
      normal = normal,
      xdir = xdir,
      ydir = ydir,
      units = s.SpeckleUnits,
    };

    // Compute start, mid, and end points on the arc
    var r = element.PrimaryRadius;
    var startAngle = element.StartAngle;
    var sweepAngle = element.SweepAngle;
    var midAngle = startAngle + sweepAngle / 2.0;
    var endAngle = startAngle + sweepAngle;

    var cx = element.CenterPoint.X;
    var cy = element.CenterPoint.Y;
    var cz = element.CenterPoint.Z;

    var startPt = new Point(
      cx + r * (Math.Cos(startAngle) * rot.RowX.X + Math.Sin(startAngle) * rot.RowY.X),
      cy + r * (Math.Cos(startAngle) * rot.RowX.Y + Math.Sin(startAngle) * rot.RowY.Y),
      cz + r * (Math.Cos(startAngle) * rot.RowX.Z + Math.Sin(startAngle) * rot.RowY.Z),
      s.SpeckleUnits
    );
    var midPt = new Point(
      cx + r * (Math.Cos(midAngle) * rot.RowX.X + Math.Sin(midAngle) * rot.RowY.X),
      cy + r * (Math.Cos(midAngle) * rot.RowX.Y + Math.Sin(midAngle) * rot.RowY.Y),
      cz + r * (Math.Cos(midAngle) * rot.RowX.Z + Math.Sin(midAngle) * rot.RowY.Z),
      s.SpeckleUnits
    );
    var endPt = new Point(
      cx + r * (Math.Cos(endAngle) * rot.RowX.X + Math.Sin(endAngle) * rot.RowY.X),
      cy + r * (Math.Cos(endAngle) * rot.RowX.Y + Math.Sin(endAngle) * rot.RowY.Y),
      cz + r * (Math.Cos(endAngle) * rot.RowX.Z + Math.Sin(endAngle) * rot.RowY.Z),
      s.SpeckleUnits
    );

    return new Arc
    {
      plane = plane,
      startPoint = startPt,
      midPoint = midPt,
      endPoint = endPt,
      domain = Interval.UnitInterval,
      units = s.SpeckleUnits,
      applicationId = element.ID.ToString(),
    };
  }
}
