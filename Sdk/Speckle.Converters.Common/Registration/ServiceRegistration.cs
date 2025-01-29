using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Common.Registration;

public static class ServiceRegistration
{
  private static readonly Type s_base = typeof(ISpeckleObject);

  public static void AddRootCommon<TRootToSpeckleConverter>(
    this IServiceCollection serviceCollection,
    Assembly converterAssembly
  )
    where TRootToSpeckleConverter : class, ISpeckleConverter
  {
    serviceCollection.AddScoped<ISpeckleConverter, TRootToSpeckleConverter>();
    /*
      POC: CNX-9267 Moved the Injection of converters into the converter module. Not sure if this is 100% right, as this doesn't just register the conversions within this converter, but any conversions found in any Speckle.*.dll file.
      This will require consolidating across other connectors.
    */


    serviceCollection.AddScoped<IHostConverter, ConverterWithFallback>();
    serviceCollection.AddScoped<HostConverter>(); //Register as self, only the `ConverterWithFallback` needs it
    serviceCollection.AddConverters(converterAssembly);
  }

  public static IServiceCollection AddApplicationConverters<THostToSpeckleUnitConverter, THostUnits>(
    this IServiceCollection serviceCollection,
    Assembly converterAssembly
  )
    where THostToSpeckleUnitConverter : class, IHostToSpeckleUnitConverter<THostUnits>
  {
    serviceCollection.AddScoped<IHostToSpeckleUnitConverter<THostUnits>, THostToSpeckleUnitConverter>();
    serviceCollection.RegisterRawConversions(converterAssembly);
    return serviceCollection;
  }

  public static void AddConverters(this IServiceCollection serviceCollection, Assembly converterAssembly)
  {
    Dictionary<(Type, Type), Type> toSpeckleConverters = new();
    Dictionary<(Type, Type), Type> toHostConverters = new();
    foreach (var type in converterAssembly.ExportedTypes)
    {
      foreach (var interfaceType in type.GetInterfaces())
      {
        if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(ITypedConverter<,>))
        {
          var genericTypes = interfaceType.GenericTypeArguments;
          if (s_base.IsAssignableFrom(genericTypes[0]))
          {
            toHostConverters.Add((genericTypes[0], genericTypes[1]), type);
          }
          else if (s_base.IsAssignableFrom(genericTypes[1]))
          {
            toSpeckleConverters.Add((genericTypes[0], genericTypes[1]), type);
          }
          else
          {
            throw new ConversionException(type.Name + " is not a valid converter.");
          }
        }
      }
    }
    serviceCollection.AddScoped<IConverterManager>(sp => new ConverterManager(
      toSpeckleConverters,
      toHostConverters,
      sp
    ));
  }

  public static void RegisterRawConversions(this IServiceCollection serviceCollection, Assembly conversionAssembly)
  {
    // POC: hard-coding speckle... :/
    foreach (Type speckleType in conversionAssembly.ExportedTypes)
    {
      RegisterRawConversionsForType(serviceCollection, speckleType);
    }
  }

  private static void RegisterRawConversionsForType(IServiceCollection serviceCollection, Type type)
  {
    if (!type.IsClass || type.IsAbstract)
    {
      return;
    }

    var rawConversionInterfaces = type.GetInterfaces()
      .Where(it => it.IsGenericType && it.GetGenericTypeDefinition() == typeof(ITypedConverter<,>));

    foreach (var conversionInterface in rawConversionInterfaces)
    {
      serviceCollection.Add(new ServiceDescriptor(conversionInterface, type, ServiceLifetime.Scoped));
    }
  }
}
