using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RhinoShared.ToSpeckle.Grasshopper;

[NameAndRankValue(typeof(RG.Plane), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PlaneToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Plane, SOG.Plane> _planeConverter;

  public PlaneToSpeckleTopLevelConverter(ITypedConverter<RG.Plane, SOG.Plane> planeConverter)
  {
    _planeConverter = planeConverter;
  }

  public Base Convert(object target) => Convert((RG.Plane)target);

  public SOG.Plane Convert(RG.Plane target)
  {
    return _planeConverter.Convert(target);
  }
}
