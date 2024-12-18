using System.Collections;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Converters.Common.ToHost;

public sealed class ConverterWithFallback(ConverterWithoutFallback baseConverter, ILogger<ConverterWithFallback> logger)
  : IRootToHostConverter
{
  public HostResult Convert(Base target)
  {
    Type type = target.GetType();

    try
    {
      return baseConverter.Convert(target);
    }
    catch (ConversionNotSupportedException e)
    {
      logger.LogInformation(e, "Attempt to find conversion for type {type} failed", type);
    }

    // Fallback to display value if it exists.
    var displayValue = target.TryGetDisplayValue<Base>();

    if (displayValue == null || (displayValue is IList && !displayValue.Any()))
    {
      return HostResult.NoConversion(
        $"No direct conversion found for type {type} and it's fallback display value was null/empty"
      );
    }

    return FallbackToDisplayValue(displayValue); // 1 - many mapping
  }

  private HostResult FallbackToDisplayValue(IReadOnlyList<Base> displayValue)
  {
    var tempDisplayableObject = new DisplayableObject(displayValue);
    var conversionResult = baseConverter.Convert(tempDisplayableObject);

    // if the host app returns a list of objects as the result of the fallback conversion, we zip them together with the original base display value objects that generated them.
    if (conversionResult.Host is IEnumerable<object> result)
    {
      return HostResult.Success(result.Zip(displayValue, (a, b) => (a, b)));
    }

    // if not, and the host app "merges" together somehow multiple display values into one entity, we return that.
    return conversionResult;
  }
}
