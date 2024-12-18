using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Speckle.Converters.Common.Registration;


public class ConverterManager<T>(ConcurrentDictionary<string, Type> converterTypes, IServiceProvider serviceProvider)
  : IConverterManager<T>
{
  public string Name => typeof(T).Name;

  public ConverterResult<T> ResolveConverter(Type type, bool recursive = true)
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
          return new ConverterResult<T>(ConversionStatus.NoConverter, Message: $"No conversion found for {type.Name} or any of its base types");
        }
      }
      else if (converter is null)
      {
        return new ConverterResult<T>(ConversionStatus.NoConverter, Message: $"No conversion found for {type.Name}");
      }
      else
      {
         
        return new ConverterResult<T>(ConversionStatus.Success, converter);
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
