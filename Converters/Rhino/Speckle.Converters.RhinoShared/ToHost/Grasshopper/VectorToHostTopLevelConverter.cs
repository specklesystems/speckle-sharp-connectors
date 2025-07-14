using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RhinoShared.ToHost.Grasshopper;

[NameAndRankValue(typeof(SOG.Vector), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class VectorToHostTopLevelConverter : IToHostTopLevelConverter
{
  private readonly ITypedConverter<SOG.Vector, RG.Vector3d> _vectorConverter;

  public VectorToHostTopLevelConverter(ITypedConverter<SOG.Vector, RG.Vector3d> vectorConverter)
  {
    _vectorConverter = vectorConverter;
  }

  public object Convert(Base target) => Convert((SOG.Vector)target);

  public RG.Vector3d Convert(SOG.Vector target)
  {
    return _vectorConverter.Convert(target);
  }
}
