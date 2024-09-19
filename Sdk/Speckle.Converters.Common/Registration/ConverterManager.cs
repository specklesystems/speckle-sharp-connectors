using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Common.Registration;

[GenerateAutoInterface]
public class ConverterManager<T>(ConcurrentDictionary<string, Type> converterTypes, IServiceProvider serviceProvider)
  : IConverterManager<T>
{
  public string Name => typeof(T).Name;

  public T? ResolveConverter(string typeName)
  {
    if (converterTypes.TryGetValue(typeName, out var converter))
    {
      return (T)ActivatorUtilities.CreateInstance(serviceProvider, converter);
    }
    return default;
  }
}
