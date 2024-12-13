using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper;

public class GrasshopperRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;

  public GrasshopperRootToSpeckleConverter(IConverterManager<IToSpeckleTopLevelConverter> toSpeckle)
  {
    _toSpeckle = toSpeckle;
  }

  public Base Convert(object target)
  {
    var objectConverter = _toSpeckle.ResolveConverter(target.GetType());
    var result = objectConverter.Convert(target);

#if RHINO8_OR_GREATER
    if (target is GM.ModelObject modelObject)
    {
      result.applicationId = modelObject.Id.ToString();
    }
#endif

    return result;
  }
}
