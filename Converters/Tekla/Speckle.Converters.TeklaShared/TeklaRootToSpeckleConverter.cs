using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.TeklaShared.Extensions;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Tekla.Structures.Model;

namespace Speckle.Converters.TeklaShared;

public class TeklaRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;

  public TeklaRootToSpeckleConverter(IConverterManager<IToSpeckleTopLevelConverter> toSpeckle)
  {
    _toSpeckle = toSpeckle;
  }

  public Base Convert(object target)
  {
    if (target is not ModelObject modelObject)
    {
      throw new ValidationException($"Target object is not a ModelObject. It's a ${target.GetType()}");
    }

    Type type = target.GetType();
    var objectConverter = _toSpeckle.ResolveConverter(type, true);

    Base result = objectConverter.Convert(target);

    result.applicationId = modelObject.GetSpeckleApplicationId();

    return result;
  }
}
