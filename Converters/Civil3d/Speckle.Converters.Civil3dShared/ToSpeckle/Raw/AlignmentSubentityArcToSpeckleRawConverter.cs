using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.Raw;

public class AlignmentSubentityArcToSpeckleRawConverter : ITypedConverter<CDB.AlignmentSubEntityArc, SOG.Arc>
{
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public AlignmentSubentityArcToSpeckleRawConverter(
    ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore
  )
  {
    _planeConverter = planeConverter;
    _settingsStore = settingsStore;
  }

  public SOG.Arc Convert(object target) => Convert((CDB.AlignmentSubEntityArc)target);

  public SOG.Arc Convert(CDB.AlignmentSubEntityArc target)
  {
    // alignment arcs do not have the same properties as autocad arcs.
    // we're assuming they are always 2d arcs on the xy plane to calculate the midpoint
    string units = _settingsStore.Current.SpeckleUnits;

    // calculate start and end angles from center
    double startAngle = Math.Atan2(
      target.StartPoint.Y - target.CenterPoint.Y,
      target.StartPoint.X - target.CenterPoint.X
    );
    double endAngle = Math.Atan2(target.EndPoint.Y - target.CenterPoint.Y, target.EndPoint.X - target.CenterPoint.X);

    // calculate midpoint angle
    double midAngle = !target.Clockwise
      ? startAngle + ((endAngle - startAngle) / 2)
      : endAngle - ((endAngle - startAngle) / 2);

    // calculate midpoint coordinates
    double midX = target.CenterPoint.X + target.Radius * Math.Cos(midAngle);
    double midY = target.CenterPoint.Y + target.Radius * Math.Sin(midAngle);

    // find arc plane (normal is in clockwise dir)
    var center3 = new AG.Point3d(target.CenterPoint.X, target.CenterPoint.Y, 0);
    AG.Plane plane = new AG.Plane(center3, AG.Vector3d.ZAxis);

    // create arc
    SOG.Arc arc =
      new()
      {
        startPoint = new()
        {
          x = target.StartPoint.X,
          y = target.StartPoint.Y,
          z = 0,
          units = units
        },
        endPoint = new()
        {
          x = target.EndPoint.X,
          y = target.EndPoint.Y,
          z = 0,
          units = units
        },
        midPoint = new()
        {
          x = midX,
          y = midY,
          z = 0,
          units = units
        },
        plane = _planeConverter.Convert(plane),
        radius = target.Radius,
        length = target.Length,

        // additional alignment subentity props
        ["startStation"] = target.StartStation,
        ["endStation"] = target.EndStation,
        ["startDirection"] = target.StartDirection,
        ["endDirection"] = target.EndDirection,
        ["delta"] = target.Delta,
        ["deflectedAngle"] = target.DeflectedAngle,
        ["minumumRadius"] = target.MinimumRadius
      };

    return arc;
  }
}
