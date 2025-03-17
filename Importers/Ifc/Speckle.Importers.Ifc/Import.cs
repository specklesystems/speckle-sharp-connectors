using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Objects.Geometry;
using Speckle.Sdk;
using Speckle.Sdk.Host;
using Speckle.Sdk.Models;
using Speckle.Sdk.Transports;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.Ifc;

/// <summary>
/// Static DI Wrapper around <see cref="Importer"/>
/// </summary>
public static class Import
{
  public static ServiceProvider GetServiceProvider()
  {
    TypeLoader.Initialize(typeof(Base).Assembly, typeof(Point).Assembly);
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddIFCImporter();
    return serviceCollection.BuildServiceProvider();
  }

  public static void AddIFCImporter(this ServiceCollection serviceCollection)
  {
    serviceCollection.AddSpeckleSdk(new("IFC", "ifc"), HostAppVersion.v2024, "IFC-Importer");
    serviceCollection.AddSpeckleWebIfc();
    serviceCollection.AddSingleton<Importer>();
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());
  }

  public static async Task<Version> Ifc(
    ImporterArgs args,
    IProgress<ProgressArgs>? progress = null,
    CancellationToken cancellationToken = default
  )
  {
    var serviceProvider = GetServiceProvider();
    return await Ifc(serviceProvider, args, progress, cancellationToken);
  }

  public static async Task<Version> Ifc(
    ServiceProvider serviceProvider,
    ImporterArgs args,
    IProgress<ProgressArgs>? progress = null,
    CancellationToken cancellationToken = default
  )
  {
    var importer = serviceProvider.GetRequiredService<Importer>();
    return await importer.ImportIfc(args, progress, cancellationToken);
  }
}
