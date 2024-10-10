using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Common.Registration;

[GenerateAutoInterface]
public class ConverterManager<T>(ConcurrentDictionary<string, Type> converterTypes, IServiceProvider serviceProvider)
  : IConverterManager<T>
{
  public string Name => typeof(T).Name;

  public T ResolveConverter(Type type, bool recursive = false)
  {
    var currentType = type;
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
          throw new ConversionNotSupportedException($"No conversion found for {type.Name} or any of its base types");
        }
      }
      else if (converter is null)
      {
        throw new ConversionNotSupportedException($"No conversion found for {type.Name}");
      }
      else
      {
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
