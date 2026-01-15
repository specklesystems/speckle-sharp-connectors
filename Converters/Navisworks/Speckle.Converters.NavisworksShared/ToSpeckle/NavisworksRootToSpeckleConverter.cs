using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class NavisworksRootToSpeckleConverter(IConverterManager<IToSpeckleTopLevelConverter> toSpeckle)
  : IRootToSpeckleConverter
{
  public Base Convert(object target)
  {
    if (target == null)
    {
      throw new ArgumentNullException(nameof(target));
    }

    if (target is not NAV.ModelItem modelItem)
    {
      throw new InvalidOperationException($"The target object is not a ModelItem. It's a ${target.GetType()}.");
    }

    Type type = target.GetType();
    var objectConverter = toSpeckle.ResolveConverter(type);
    Base result = objectConverter.Convert(modelItem);
    result.applicationId = ElementSelectionHelper.ResolveModelItemToIndexPath(modelItem);

    return result;
  }
}
