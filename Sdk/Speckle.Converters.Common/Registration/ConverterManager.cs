using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Common.Registration;

[GenerateAutoInterface]
public class ConverterManager<T>(ConcurrentDictionary<string, Type> converterTypes, IServiceProvider serviceProvider)
  : IConverterManager<T>
{
  public string Name => typeof(T).Name;

  public T? ResolveConverter(Type type, bool checkBase = false)
  {
    var typeName = type.Name;
    var obj = GetType(typeName);
    if (obj is null && checkBase)
    {
      var baseType = type.BaseType;
      if (baseType is not null)
      {
        return GetType(baseType.Name);
      }
    }
    return default;
  }

  private T? GetType(string typeName)
  {
    if (converterTypes.TryGetValue(typeName, out var converter))
    {
      return (T)ActivatorUtilities.CreateInstance(serviceProvider, converter);
    }
    return default;
  }
}
