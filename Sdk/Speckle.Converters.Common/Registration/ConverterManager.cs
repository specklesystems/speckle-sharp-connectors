using Microsoft.Extensions.DependencyInjection;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Common.Registration;

[GenerateAutoInterface]
public class ConverterManager(
  IReadOnlyDictionary<(Type, Type), Type> toSpeckleConverters,
  IReadOnlyDictionary<(Type, Type), Type> toHostConverters,
  IServiceProvider serviceProvider
) : IConverterManager
{
  private readonly Dictionary<Type, (Type?, Type?)> _hostConverterCache = new();
  private readonly Dictionary<Type, (Type?, Type?)> _speckleConverterCache = new();

  public (object, Type) GetSpeckleConverter(Type sourceType)
  {
    Type? destinationType;
    Type? converterType;
    if (!_speckleConverterCache.TryGetValue(sourceType, out var cached))
    {
      (destinationType, converterType) = GetConverter(sourceType, toSpeckleConverters);
      _speckleConverterCache.Add(sourceType, (destinationType, converterType));
    }
    else
    {
      (destinationType, converterType) = cached;
    }

    if (converterType is null)
    {
      throw new ConversionNotSupportedException($"No conversion found for {sourceType.Name}");
    }

    return (ActivatorUtilities.CreateInstance(serviceProvider, converterType), destinationType.NotNull());
  }

  public (object, Type) GetHostConverter(Type sourceType)
  {
    Type? destinationType;
    Type? converterType;
    if (!_hostConverterCache.TryGetValue(sourceType, out var cached))
    {
      (destinationType, converterType) = GetConverter(sourceType, toHostConverters);
      _hostConverterCache.Add(sourceType, (destinationType, converterType));
    }
    else
    {
      (destinationType, converterType) = cached;
    }

    if (converterType is null)
    {
      throw new ConversionNotSupportedException($"No conversion found for {sourceType.Name}");
    }

    return (ActivatorUtilities.CreateInstance(serviceProvider, converterType), destinationType.NotNull());
  }

  private (Type?, Type?) GetConverter(Type sourceType, IReadOnlyDictionary<(Type, Type), Type> converters)
  {
    Type? mostBasicDestinationType = null;
    Type? mostBasicConverterType = null;
    foreach (var (d, converterType) in GetConverters(sourceType, converters))
    {
      if (mostBasicDestinationType is null || d.IsAssignableFrom(mostBasicDestinationType))
      {
        mostBasicDestinationType = d;
        mostBasicConverterType = converterType;
      }
    }

    return (mostBasicDestinationType, mostBasicConverterType);
  }

  private IEnumerable<(Type, Type)> GetConverters(Type sourceType, IReadOnlyDictionary<(Type, Type), Type> converters)
  {
    foreach (var (s, d) in converters.Keys)
    {
      Type? currentSourceType = sourceType;
      while (true)
      {
        if (currentSourceType == s)
        {
          yield return (d, converters[(s, d)]);
        }
        var baseType = currentSourceType.BaseType;
        currentSourceType = baseType;

        if (currentSourceType == null)
        {
          break;
        }
      }
    }
  }
}
