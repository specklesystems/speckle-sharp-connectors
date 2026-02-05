using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Primitive;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class BoundingBoxXYZToSpeckleConverter : ITypedConverter<DB.BoundingBoxXYZ, SOG.Box>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;
  private readonly ITypedConverter<
    (DB.XYZ origin, DB.XYZ xDir, DB.XYZ yDir, DB.XYZ normal),
    SOG.Plane
  > _curveOriginToPlaneConverter;

  public BoundingBoxXYZToSpeckleConverter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter,
    ITypedConverter<(DB.XYZ origin, DB.XYZ xDir, DB.XYZ yDir, DB.XYZ normal), SOG.Plane> curveOriginToPlaneConverter
  )
  {
    _converterSettings = converterSettings;
    _xyzToPointConverter = xyzToPointConverter;
    _curveOriginToPlaneConverter = curveOriginToPlaneConverter;
  }

  public SOG.Box Convert(DB.BoundingBoxXYZ target)
  {
    // convert min and max pts to speckle first
    var min = _xyzToPointConverter.Convert(target.Min);
    var max = _xyzToPointConverter.Convert(target.Max);

    // get the base plane of the bounding box from the transform
    var transform = target.Transform;

    // assemble components for getting origin plane
    var xDir = transform.BasisX.Normalize();
    var yDir = transform.BasisY.Normalize();
    var normal = xDir.CrossProduct(yDir).Normalize();

    return new SOG.Box()
    {
      xSize = new Interval { start = min.x, end = max.x },
      ySize = new Interval { start = min.y, end = max.y },
      zSize = new Interval { start = min.z, end = max.z },
      plane = _curveOriginToPlaneConverter.Convert((transform.Origin, xDir, yDir, normal)),
      units = _converterSettings.Current.SpeckleUnits,
    };
  }
}
