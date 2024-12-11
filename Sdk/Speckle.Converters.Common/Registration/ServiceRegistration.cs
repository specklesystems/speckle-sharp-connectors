using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.ToHost;
using Speckle.Sdk.Common;

namespace Speckle.Converters.Common.Registration;

public static class ServiceRegistration
{
  public static void AddRootCommon<TRootToSpeckleConverter>(
    this IServiceCollection serviceCollection,
    Assembly converterAssembly
  )
    where TRootToSpeckleConverter : class, IRootToSpeckleConverter
  {
    serviceCollection.AddScoped<IRootToSpeckleConverter, TRootToSpeckleConverter>();
    /*
      POC: CNX-9267 Moved the Injection of converters into the converter module. Not sure if this is 100% right, as this doesn't just register the conversions within this converter, but any conversions found in any Speckle.*.dll file.
      This will require consolidating across other connectors.
    */



    serviceCollection.AddScoped<IRootToHostConverter, ConverterWithFallback>();
    serviceCollection.AddScoped<ConverterWithoutFallback>(); //Register as self, only the `ConverterWithFallback` needs it

    serviceCollection.AddConverters<IToSpeckleTopLevelConverter>(converterAssembly);
    serviceCollection.AddConverters<IToHostTopLevelConverter>(converterAssembly);
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

  public static void AddConverters<T>(this IServiceCollection serviceCollection, Assembly converterAssembly)
  {
    ConcurrentDictionary<string, Type> converterTypes = new();
    var types = converterAssembly.ExportedTypes.Where(x => x.GetInterfaces().Contains(typeof(T)));

    // we only care about named types
    var byName = types
      .Where(x => x.GetCustomAttribute<NameAndRankValueAttribute>() != null)
      .Select(x =>
      {
        var nameAndRank = x.GetCustomAttribute<NameAndRankValueAttribute>().NotNull();

        return (name: nameAndRank.Name, rank: nameAndRank.Rank, type: x);
      })
      .ToList();

    // we'll register the types accordingly
    var names = byName.Select(x => x.name).Distinct();
    foreach (string name in names)
    {
      var namedTypes = byName.Where(x => x.name == name).OrderByDescending(y => y.rank).ToList();

      // first type found
      var first = namedTypes[0];

      // POC: may need to be instance per lifecycle scope
      converterTypes.TryAdd(first.name, first.type);

      // POC: not sure yet if...
      // * This should be an array of types
      // * Whether the scope should be modified or modifiable
      // * Whether this is in the write project... hmmm
      // POC: IsAssignableFrom()
      var secondaryType = first.type.GetInterface(typeof(ITypedConverter<,>).Name);
      // POC: should we explode if no found?
      if (secondaryType != null)
      {
        converterTypes.TryAdd(first.name, secondaryType);
      }

      // register subsequent types with rank
      namedTypes.RemoveAt(0);
    }
    serviceCollection.AddScoped<IConverterManager<T>>(sp => new ConverterManager<T>(converterTypes, sp));
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
