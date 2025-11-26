using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

/// <summary>
/// Converts AutoCAD Solid3d entities to SolidX with DWG encoding.
/// </summary>
[NameAndRankValue(typeof(ADB.Solid3d), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK + 1)]
public class Solid3dToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ADB.Solid3d, SOG.SolidX> _solidConverter;

  public Solid3dToSpeckleConverter(ITypedConverter<ADB.Solid3d, SOG.SolidX> solidConverter)
  {
    _solidConverter = solidConverter;
  }

  public Base Convert(object target) => RawConvert((ADB.Solid3d)target);

  public SOG.SolidX RawConvert(ADB.Solid3d target)
  {
    return _solidConverter.Convert(target);
  }
}
