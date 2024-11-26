using System.ComponentModel.DataAnnotations;
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
      throw new ValidationException("Target object is null");
    }

    if (target is not NAV.ModelItem)
    {
      throw new ValidationException($"Target object is not a ModelObject. It's a ${target.GetType()}");
    }

    Type type = target.GetType();
    var objectConverter = toSpeckle.ResolveConverter(type);

    Base result = objectConverter.Convert(target);

    return result;
  }
}
