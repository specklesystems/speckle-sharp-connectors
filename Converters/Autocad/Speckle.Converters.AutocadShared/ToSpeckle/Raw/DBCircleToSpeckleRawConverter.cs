using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Core.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBCircleToSpeckleRawConverter : ITypedConverter<ADB.Circle, SOG.Circle>
{
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly ITypedConverter<ADB.Extents3d, SOG.Box> _boxConverter;
  private readonly IConversionContextStack<Document, ADB.UnitsValue> _contextStack;

  public DBCircleToSpeckleRawConverter(
    ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
    ITypedConverter<ADB.Extents3d, SOG.Box> boxConverter,
    IConversionContextStack<Document, ADB.UnitsValue> contextStack
  )
  {
    _planeConverter = planeConverter;
    _boxConverter = boxConverter;
    _contextStack = contextStack;
  }

  public Base Convert(object target) => Convert((ADB.Circle)target);

  public SOG.Circle Convert(ADB.Circle target)
  {
    SOG.Plane plane = _planeConverter.Convert(target.GetPlane());
    SOG.Box bbox = _boxConverter.Convert(target.GeometricExtents);
    SOG.Circle circle =
      new(plane, target.Radius, _contextStack.Current.SpeckleUnits) { length = target.Circumference, bbox = bbox };

    return circle;
  }
}
