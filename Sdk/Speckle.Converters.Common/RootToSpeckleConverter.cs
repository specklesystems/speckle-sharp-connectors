using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;

namespace Speckle.Converters.Common;

[GenerateAutoInterface]
public class RootToSpeckleConverter(IConverterManager<IToSpeckleTopLevelConverter> toSpeckle) : IRootToSpeckleConverter
{
  public BaseResult Convert(object target)
  {
    Type type = target.GetType();

    var result = toSpeckle.ResolveConverter(type);

    if (result.IsSuccess)
    {
      var convertedObject = result.Converter.NotNull().Convert(target);
      return convertedObject;
    }
    return BaseResult.NoConverter(result.Message);
  }
}
