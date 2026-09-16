using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Plant3dShared.ToSpeckle.Geometry;

/// <summary>
/// Converts AutoCAD Solid3d entities to SolidX with SAT encoding for lossless round-trip.
/// </summary>
[NameAndRankValue(typeof(ADB.Solid3d), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK + 2)]
public class Solid3dToSpeckleConverter(ITypedConverter<ADB.Solid3d, SOG.Mesh> meshConverter)
  : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ADB.Solid3d, SOG.Mesh> _meshConverter = meshConverter;

  public Base Convert(object target) => RawConvert((ADB.Solid3d)target);

  public SOG.Mesh RawConvert(ADB.Solid3d target) => _meshConverter.Convert(target);
}
