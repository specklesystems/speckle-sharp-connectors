using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;
using Speckle.Objects.Primitive;

namespace Speckle.Converter.Navisworks.ToSpeckle.Raw;

public class BoundingBoxToSpeckleRawConverter(IConverterSettingsStore<NavisworksConversionSettings> settingsStore)
  : ITypedConverter<NAV.BoundingBox3D, Box>
{
  public Box Convert(object target) => Convert((NAV.BoundingBox3D)target);

  public Box Convert(NAV.BoundingBox3D? target)
  {
    if (target == null)
    {
      return null!; // returns null for reference types (Box is a reference type)
    }

    var minPoint = target.Min;
    var maxPoint = target.Max;

    var units = settingsStore.Current.Derived.SpeckleUnits;

    var basePlane = new Plane
    {
      origin = new Point(minPoint.X, minPoint.Y, minPoint.Z, units),
      normal = new Vector(0, 0, 1, units),
      xdir = new Vector(1, 0, 0, units),
      ydir = new Vector(0, 1, 0, units),
      units = units
    };

    var boundingBox = new Box
    {
      units = units,
      plane = basePlane,
      xSize = new Interval() { start = minPoint.X, end = maxPoint.X },
      ySize = new Interval() { start = minPoint.Y, end = maxPoint.Y },
      zSize = new Interval() { start = minPoint.Z, end = maxPoint.Z }
    };

    return boundingBox;
  }
}
