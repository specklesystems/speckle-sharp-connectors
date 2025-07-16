using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RhinoShared.ToSpeckle.Grasshopper;

[NameAndRankValue(typeof(RG.Vector3d), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class VectorToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Vector3d, SOG.Vector> _vectorConverter;

  public VectorToSpeckleTopLevelConverter(ITypedConverter<RG.Vector3d, SOG.Vector> vectorConverter)
  {
    _vectorConverter = vectorConverter;
  }

  public Base Convert(object target) => Convert((RG.Vector3d)target);

  public SOG.Vector Convert(RG.Vector3d target)
  {
    return _vectorConverter.Convert(target);
  }
}
