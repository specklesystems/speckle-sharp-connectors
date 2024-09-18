using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Primitive;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class BoundingBoxXYZToSpeckleConverter : ITypedConverter<DB.BoundingBoxXYZ, SOG.Box>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;
  private readonly ITypedConverter<DB.Plane, SOG.Plane> _planeConverter;

  public BoundingBoxXYZToSpeckleConverter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter,
    ITypedConverter<DB.Plane, SOG.Plane> planeConverter
  )
  {
    _converterSettings = converterSettings;
    _xyzToPointConverter = xyzToPointConverter;
    _planeConverter = planeConverter;
  }

  public SOG.Box Convert(DB.BoundingBoxXYZ target)
  {
    // convert min and max pts to speckle first
    var min = _xyzToPointConverter.Convert(target.Min);
    var max = _xyzToPointConverter.Convert(target.Max);

    // get the base plane of the bounding box from the transform
    var transform = target.Transform;
    var plane = DB.Plane.CreateByOriginAndBasis(
      transform.Origin,
      transform.BasisX.Normalize(),
      transform.BasisY.Normalize()
    );

    var box = new SOG.Box()
    {
      xSize = new Interval { start = min.x, end = max.x },
      ySize = new Interval { start = min.y, end = max.y },
      zSize = new Interval { start = min.z, end = max.z },
      basePlane = _planeConverter.Convert(plane),
      units = _converterSettings.Current.SpeckleUnits
    };

    return box;
  }
}
