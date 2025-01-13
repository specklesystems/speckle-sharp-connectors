using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Speckle.Sdk;
using Speckle.WebIfc.Importer.Ifc;

namespace Speckle.WebIfc.Importer;

public static class ServiceRegistration
{
  public static void AddSpeckleWebIfc(this IServiceCollection services)
  {
    services.AddSingleton<IIfcFactory, IfcFactory>();
  }

  public static IServiceCollection AddMatchingInterfacesAsTransient(
    this IServiceCollection serviceCollection,
    Assembly assembly
  )
  {
    foreach (var type in assembly.ExportedTypes.Where(t => t.IsNonAbstractClass()))
    {
      foreach (var matchingInterface in type.FindMatchingInterface())
      {
        serviceCollection.TryAddTransient(matchingInterface, type);
      }
    }

    return serviceCollection;
  }
}
