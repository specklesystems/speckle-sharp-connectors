using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Common.Registration;

public class ConverterManager<T>(ConcurrentDictionary<string, Type> converterTypes, IServiceProvider serviceProvider)
  : IConverterManager<T>
{
  private readonly Dictionary<Type, string?> _foundTypes = new();
  public string Name => typeof(T).Name;

  public T ResolveConverter(Type type, bool recursive = true)
  {
    var currentType = type;
    if (_foundTypes.TryGetValue(type, out var foundType))
    {
      if (foundType is null)
      {
        throw new ConversionNotSupportedException($"No conversion found for {type.Name}");
      }
      return GetConverterByType(foundType)
        ?? throw new ConversionNotSupportedException($"No conversion found for {type.Name}");
    }
    while (true)
    {
      var typeName = currentType.Name;
      var converter = GetConverterByType(typeName);
      if (converter is null && recursive)
      {
        var baseType = currentType.BaseType;
        currentType = baseType;

        if (currentType == null)
        {
          _foundTypes.Add(type, null);
          throw new ConversionNotSupportedException($"No conversion found for {type.Name} or any of its base types");
        }
      }
      else if (converter is null)
      {
        _foundTypes.Add(type, null);
        throw new ConversionNotSupportedException($"No conversion found for {type.Name}");
      }
      else
      {
        _foundTypes.Add(type, typeName);
        return converter;
      }
    }
  }

  private T? GetConverterByType(string typeName)
  {
    if (converterTypes.TryGetValue(typeName, out var converter))
    {
      return (T)ActivatorUtilities.CreateInstance(serviceProvider, converter);
    }
    return default;
  }
}
