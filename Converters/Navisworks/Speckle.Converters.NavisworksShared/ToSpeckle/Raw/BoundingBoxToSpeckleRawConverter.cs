using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;
using Speckle.Objects.Primitive;

namespace Speckle.Converter.Navisworks.ToSpeckle.Raw;

public class BoundingBoxToSpeckleRawConverter : ITypedConverter<NAV.BoundingBox3D, Box>
{
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _settingsStore;

  public BoundingBoxToSpeckleRawConverter(IConverterSettingsStore<NavisworksConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public Box Convert(object target) => Convert((NAV.BoundingBox3D)target);

  public Box Convert(NAV.BoundingBox3D? target)
  {
    if (target != null)
    {
      var min = target.Min;
      var max = target.Max;

      var basePlane = new Plane
      {
        origin = new Point(min.X, min.Y, min.Z, _settingsStore.Current.SpeckleUnits),
        normal = new Vector(0, 0, 1, _settingsStore.Current.SpeckleUnits),
        xdir = new Vector(1, 0, 0, _settingsStore.Current.SpeckleUnits),
        ydir = new Vector(0, 1, 0, _settingsStore.Current.SpeckleUnits),
        units = _settingsStore.Current.SpeckleUnits
      };

      var boundingBox = new Box
      {
        units = _settingsStore.Current.SpeckleUnits,
        plane = basePlane,
        xSize = new Interval() { start = min.X, end = max.X },
        ySize = new Interval() { start = min.Y, end = max.Y },
        zSize = new Interval() { start = min.Z, end = max.Z }
      };

      return boundingBox;
    }
    else
    {
      return null!;
    }
  }
}
