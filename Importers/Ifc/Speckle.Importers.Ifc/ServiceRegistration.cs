using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Speckle.Importers.Ifc.Types;
using Speckle.Sdk;

namespace Speckle.Importers.Ifc;

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
