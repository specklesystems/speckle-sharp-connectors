using System.Collections;
using Microsoft.Extensions.Logging;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Converters.Common;

public sealed class ConverterWithFallback : IHostConverter
{
  private readonly ILogger<ConverterWithFallback> _logger;
  private readonly HostConverter _baseConverter;

  public ConverterWithFallback(HostConverter baseConverter, ILogger<ConverterWithFallback> logger)
  {
    _logger = logger;
    _baseConverter = baseConverter;
  }

  public object Convert(Base target)
  {
    try
    {
      return _baseConverter.Convert(target);
    }
    catch (ConversionNotSupportedException e)
    {
      _logger.LogInformation(e, "Attempt to find conversion for type {type} failed", target.GetType());
    }

    // Fallback to display value if it exists.
    var displayValue = target.TryGetDisplayValue<Base>();

    if (displayValue == null || (displayValue is IList && !displayValue.Any()))
    {
      // TODO: I'm not sure if this should be a ConversionNotSupported instead, but it kinda mixes support + validation so I went for normal conversion exception
      throw new ConversionException(
        $"No direct conversion found for type {target.GetType()} and it's fallback display value was null/empty"
      );
    }

    return FallbackToDisplayValue(displayValue); // 1 - many mapping
  }

  private object FallbackToDisplayValue(IReadOnlyList<Base> displayValue)
  {
    var tempDisplayableObject = new DisplayableObject(displayValue);
    var conversionResult = _baseConverter.Convert(tempDisplayableObject);

    // if the host app returns a list of objects as the result of the fallback conversion, we zip them together with the original base display value objects that generated them.
    if (conversionResult is IEnumerable<object> result)
    {
      return result.Zip(displayValue, (a, b) => (a, b));
    }

    // if not, and the host app "merges" together somehow multiple display values into one entity, we return that.
    return conversionResult;
  }
}
