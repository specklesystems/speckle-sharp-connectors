using System.Collections;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common.Extensions;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Converters.Common.Registration;

// POC: CNX-9394 Find a better home for this outside `DependencyInjection` project
/// <summary>
/// <inheritdoc cref="ConverterWithoutFallback"/>
/// <br/>
/// If no suitable converter conversion is found, and the target <see cref="Base"/> object has a displayValue property
/// a converter with strong name of <see cref="DisplayableObject"/> is resolved for.
/// </summary>
/// <seealso cref="ConverterWithoutFallback"/>
public sealed class ConverterWithFallback : IRootToHostConverter
{
  private readonly ILogger<ConverterWithFallback> _logger;
  private readonly ConverterWithoutFallback _baseConverter;

  public ConverterWithFallback(ConverterWithoutFallback baseConverter, ILogger<ConverterWithFallback> logger)
  {
    _logger = logger;
    _baseConverter = baseConverter;
  }

  /// <summary>
  /// Converts a <see cref="Base"/> instance to a host object.
  /// </summary>
  /// <param name="target">The <see cref="Base"/> instance to convert.</param>
  /// <returns>The converted host object.
  /// Fallbacks to display value if a direct conversion is not possible.</returns>
  /// <remarks>
  /// The conversion is done in the following order of preference:
  /// 1. Direct conversion using the <see cref="ConverterWithoutFallback"/>.
  /// 2. Fallback to display value using the <see cref="Speckle.Sdk.Models.Extensions.BaseExtensions.TryGetDisplayValue{T}"/> method, if a direct conversion is not possible.
  ///
  /// If the direct conversion is not available and there is no displayValue, a <see cref="System.NotSupportedException"/> is thrown.
  /// </remarks>
  /// <exception cref="System.NotSupportedException">Thrown when no conversion is found for <paramref name="target"/>.</exception>
  public object Convert(Base target)
  {
    Type type = target.GetType();

    // Direct conversion if a converter is found
    if (_baseConverter.TryGetConverter(type, out IToHostTopLevelConverter? result))
    {
      return result.ConvertAndLog(target, _logger); // 1-1 mapping
    }

    // Fallback to display value if it exists.
    var displayValue = target.TryGetDisplayValue<Base>();
    if (displayValue != null)
    {
      if (displayValue is IList && !displayValue.Any())
      {
        throw new NotSupportedException($"No display value found for {type}");
      }
      return FallbackToDisplayValue(displayValue); // 1 - many mapping
    }

    throw new NotSupportedException($"No conversion found for {type}");
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