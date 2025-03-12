using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared;

public class RevitRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;

  public RevitRootToSpeckleConverter(IConverterManager<IToSpeckleTopLevelConverter> toSpeckle)
  {
    _toSpeckle = toSpeckle;
  }

  public Base Convert(object target)
  {
    if (target is not DB.Element element)
    {
      throw new ValidationException($"Target object is not a db element, it's a {target.GetType()}");
    }

    var objectConverter = _toSpeckle.ResolveConverter(target.GetType());
    Base result = objectConverter.Convert(target);
    result.applicationId = element.UniqueId;
    return result;
  }
}
