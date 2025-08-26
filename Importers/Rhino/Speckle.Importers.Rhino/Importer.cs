using Microsoft.Extensions.DependencyInjection;
using Speckle.Importers.Rhino.Internal;
using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.Rhino;

/// <summary>
/// Entry point for the rhino import.
/// Is a wrapper around an internal DI container.
/// It's very important that the state of services doesn't bleed between job,
/// So every import creates a new container for its processing
/// I don't trust the current services to not hold on to caches or state that could influence the next run
/// </summary>
public static class Importer
{
  public static async Task<Version> Import(
    string filePath,
    string projectId,
    string modelId,
    Account account,
    CancellationToken cancellationToken
  )
  {
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddRhinoImporter();
    using var serviceProvider = serviceCollection.BuildServiceProvider();
    var instance = serviceProvider.GetRequiredService<ImporterInstance>();
    return await instance.RunRhinoImport(filePath, projectId, modelId, account, cancellationToken);
  }
}
