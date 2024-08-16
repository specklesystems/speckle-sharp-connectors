using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class BoxToSpeckleRawConverter : ITypedConverter<ADB.Extents3d, SOG.Box>
{
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly IConversionContextStack<Document, ADB.UnitsValue> _contextStack;

  public BoxToSpeckleRawConverter(
    ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
    IConversionContextStack<Document, ADB.UnitsValue> contextStack
  )
  {
    _planeConverter = planeConverter;
    _contextStack = contextStack;
  }

  public Base Convert(object target) => Convert((ADB.Extents3d)target);

  public SOG.Box Convert(ADB.Extents3d target)
  {
    // get dimension intervals and volume
    SOP.Interval xSize = new() { start = target.MinPoint.X, end = target.MaxPoint.X };
    SOP.Interval ySize = new() { start = target.MinPoint.Y, end = target.MaxPoint.Y };
    SOP.Interval zSize = new() { start = target.MinPoint.Z, end = target.MaxPoint.Z };
    double volume = xSize.Length * ySize.Length * zSize.Length;

    // get the base plane of the bounding box from extents and current UCS
    var ucs = _contextStack.Current.Document.Editor.CurrentUserCoordinateSystem.CoordinateSystem3d;
    AG.Plane acadPlane = new(target.MinPoint, ucs.Xaxis, ucs.Yaxis);
    SOG.Plane plane = _planeConverter.Convert(acadPlane);

    SOG.Box box =
      new()
      {
        basePlane = plane,
        xSize = xSize,
        ySize = ySize,
        zSize = zSize,
        units = _contextStack.Current.SpeckleUnits,
        volume = volume,
      };

    return box;
  }
}
