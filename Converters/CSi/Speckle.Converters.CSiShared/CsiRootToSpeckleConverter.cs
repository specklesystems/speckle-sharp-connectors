using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.CSiShared;

public class CsiRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;

  public CsiRootToSpeckleConverter(IConverterManager<IToSpeckleTopLevelConverter> toSpeckle)
  {
    _toSpeckle = toSpeckle;
  }

  public Base Convert(object target)
  {
    if (target is not ICsiWrapper)
    {
      throw new ValidationException($"Target object is not a CSiWrapper. It's a ${target.GetType()}");
    }

    Type type = target.GetType();
    var objectConverter = _toSpeckle.ResolveConverter(type, true);

    Base result = objectConverter.Convert(target);

    return result;
  }
}
