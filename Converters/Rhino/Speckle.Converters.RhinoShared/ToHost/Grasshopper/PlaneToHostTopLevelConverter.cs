using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RhinoShared.ToHost.Grasshopper;

[NameAndRankValue(typeof(SOG.Plane), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PlaneToHostTopLevelConverter : IToHostTopLevelConverter
{
  private readonly ITypedConverter<SOG.Plane, RG.Plane> _planeConverter;

  public PlaneToHostTopLevelConverter(ITypedConverter<SOG.Plane, RG.Plane> planeConverter)
  {
    _planeConverter = planeConverter;
  }

  public object Convert(Base target) => Convert((SOG.Plane)target);

  public RG.Plane Convert(SOG.Plane target)
  {
    return _planeConverter.Convert(target);
  }
}
