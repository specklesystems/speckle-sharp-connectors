using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.Raw;

public class AlignmentSubentityArcToSpeckleRawConverter : ITypedConverter<CDB.AlignmentSubEntityArc, SOG.Arc>
{
  private readonly ITypedConverter<AG.Point2d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public AlignmentSubentityArcToSpeckleRawConverter(
    ITypedConverter<AG.Point2d, SOG.Point> pointConverter,
    ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _planeConverter = planeConverter;
    _settingsStore = settingsStore;
  }

  public SOG.Arc Convert(object target) => Convert((CDB.AlignmentSubEntityArc)target);

  public SOG.Arc Convert(CDB.AlignmentSubEntityArc target)
  {
    // alignment arcs do not have the same properties as autocad arcs.
    // we're assuming they are always 2d arcs on the xy plane to calculate the midpoint
    string units = _settingsStore.Current.SpeckleUnits;

    // calculate the mid vector (center to PI point (intersection of tangents)
    // note: what is the PI point for a perfect half circle?
    AG.Point2d piPoint = target.PIPoint;
    double midVectorX = piPoint.X - target.CenterPoint.X;
    double midVectorY = piPoint.Y - target.CenterPoint.Y;
    double midVectorMag = Math.Sqrt(Math.Pow(midVectorX, 2.0) + Math.Pow(midVectorY, 2));
    double midScalingVectorX = target.Radius * midVectorX / midVectorMag;
    double midScalingVectorY = target.Radius * midVectorY / midVectorMag;
    if (target.Delta > Math.PI)
    {
      midScalingVectorX *= -1;
      midScalingVectorY *= -1;
    }

    double midPointX = target.CenterPoint.X + midScalingVectorX;
    double midPointY = target.CenterPoint.Y + midScalingVectorY;

    // find arc plane (normal is in counterclockwise dir)
    var center3 = new AG.Point3d(target.CenterPoint.X, target.CenterPoint.Y, 0);
    AG.Plane plane = target.Clockwise
      ? new AG.Plane(center3, AG.Vector3d.ZAxis.MultiplyBy(-1))
      : new AG.Plane(center3, AG.Vector3d.ZAxis);

    // create arc
    SOG.Arc arc =
      new()
      {
        startPoint = _pointConverter.Convert(target.StartPoint),
        endPoint = _pointConverter.Convert(target.EndPoint),
        midPoint = _pointConverter.Convert(new AG.Point2d(midPointX, midPointY)),
        plane = _planeConverter.Convert(plane),
        units = units
      };

    // create a properties dictionary for additional props
    Dictionary<string, object?> props =
      new() { ["startStation"] = target.StartStation, ["endStation"] = target.EndStation };
    PropertyHandler propHandler = new();
    propHandler.TryAddToDictionary(props, "startDirection", () => target.StartDirection); // might throw
    propHandler.TryAddToDictionary(props, "endDirection", () => target.EndDirection); // might throw
    propHandler.TryAddToDictionary(props, "deflectedAngle", () => target.DeflectedAngle); // might throw
    propHandler.TryAddToDictionary(props, "minumumRadius", () => target.MinimumRadius); // might throw
    arc["properties"] = props;

    return arc;
  }
}
