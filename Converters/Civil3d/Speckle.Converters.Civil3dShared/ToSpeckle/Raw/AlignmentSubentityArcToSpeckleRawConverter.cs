using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk;

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
    string units = _settingsStore.Current.SpeckleUnits;

    // calculate midpoint of chord as between start and end point
    AG.Point2d chordMid =
      new((target.StartPoint.X + target.EndPoint.X) / 2, (target.StartPoint.Y + target.EndPoint.Y) / 2);

    // calculate sagitta as radius minus distance between arc center and chord midpoint
    double sagitta = target.Radius - target.CenterPoint.GetDistanceTo(chordMid);

    // get unit vector from arc center to chord mid
    AG.Vector2d midVector = target.CenterPoint.GetVectorTo(chordMid);
    AG.Vector2d unitMidVector = midVector.DivideBy(midVector.Length);

    // get midpoint of arc by moving chord mid point the length of the sagitta along mid vector
    // if greater than 180 >, move in other direction of distance radius + radius - sagitta
    // in the case of an exactly perfect half circle arc...🤷‍♀️
    AG.Point2d midPoint = chordMid.Add(unitMidVector.MultiplyBy(sagitta));
    try
    {
      if (target.GreaterThan180) // this can throw : The property gets an invalid value according to the entity's constraint type.
      {
        midPoint = chordMid.Add(unitMidVector.Negate().MultiplyBy(2 * target.Radius - sagitta));
      }
    }
    catch (Exception e) when (!e.IsFatal()) { } // continue with original midpoint if GreaterThan180 doesn't apply to this arc

    // find arc plane (normal is in clockwise dir)
    var center3 = new AG.Point3d(target.CenterPoint.X, target.CenterPoint.Y, 0);
    AG.Plane plane = target.Clockwise
      ? new AG.Plane(center3, AG.Vector3d.ZAxis.MultiplyBy(-1))
      : new AG.Plane(center3, AG.Vector3d.ZAxis);

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
          x = midPoint.X,
          y = midPoint.Y,
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
