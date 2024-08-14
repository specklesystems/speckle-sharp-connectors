using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToHost.Geometry;

[NameAndRankValue(nameof(SOG.Circle), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CircleToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Circle, ADB.Circle>
{
  private readonly ITypedConverter<SOG.Point, AG.Point3d> _pointConverter;
  private readonly ITypedConverter<SOG.Vector, AG.Vector3d> _vectorConverter;
  private readonly IConversionContextStack<Document, ADB.UnitsValue> _contextStack;

  public CircleToHostConverter(
    ITypedConverter<SOG.Point, AG.Point3d> pointConverter,
    ITypedConverter<SOG.Vector, AG.Vector3d> vectorConverter,
    IConversionContextStack<Document, ADB.UnitsValue> contextStack
  )
  {
    _pointConverter = pointConverter;
    _vectorConverter = vectorConverter;
    _contextStack = contextStack;
  }

  public object Convert(Base target) => Convert((SOG.Circle)target);

  public ADB.Circle Convert(SOG.Circle target)
  {
    AG.Vector3d normal = _vectorConverter.Convert(target.plane.normal);
    AG.Point3d origin = _pointConverter.Convert(target.plane.origin);
    double f = Units.GetConversionFactor(target.units, _contextStack.Current.SpeckleUnits);

    var radius = f * target.radius;
    return new(origin, normal, radius);
  }
}
