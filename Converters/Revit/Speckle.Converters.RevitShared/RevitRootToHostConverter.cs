using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared;

public class RevitRootToHostConverter : IRootToHostConverter
{
  private readonly IConverterManager<IToHostTopLevelConverter> _converterResolver;

  public RevitRootToHostConverter(IConverterManager<IToHostTopLevelConverter> converterResolver)
  {
    _converterResolver = converterResolver;
  }

  public object Convert(Base target)
  {
    var objectConverter = _converterResolver.ResolveConverter(target.GetType().Name);

    if (objectConverter == null)
    {
      throw new SpeckleConversionException($"No conversion found for {target.GetType().Name}");
    }

    return objectConverter.Convert(target)
      ?? throw new SpeckleConversionException($"Conversion of object with type {target.GetType()} returned null");
  }
}
