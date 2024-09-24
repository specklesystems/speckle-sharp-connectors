using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Common.Registration;

[GenerateAutoInterface]
public class ConverterManager<T>(ConcurrentDictionary<string, Type> converterTypes, IServiceProvider serviceProvider)
  : IConverterManager<T>
{
  public string Name => typeof(T).Name;

  public T? ResolveConverter(Type type, bool recursive = false)
  {
    while (true)
    {
      var typeName = type.Name;
      var converter = GetConverterByType(typeName);
      if (converter is null && recursive)
      {
        var baseType = type.BaseType;
        if (baseType is not null)
        {
          type = baseType;
        }
        else
        {
          return default;
        }
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
