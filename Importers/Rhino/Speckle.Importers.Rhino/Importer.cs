using Microsoft.Extensions.DependencyInjection;
using Speckle.Importers.Rhino.Internal;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.Rhino;

/// <summary>
/// DI Wrapper
/// </summary>
public static class Importer
{
  public static async Task<Version> Import(
    string filePath,
    string projectId,
    string modelId,
    Account account,
    ISdkActivityFactory activityFactory,
    CancellationToken cancellationToken
  )
  {
    // It's very important that the state of services doesn't bleed between job
    // I don't trust the current services to not hold on to caches or state that could influence the next run
    // So every import creates a new container for its processing
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddRhinoImporter();
    serviceCollection.AddSingleton<ISdkActivityFactory>(_ => activityFactory);

    using var serviceProvider = serviceCollection.BuildServiceProvider();
    var instance = serviceProvider.GetRequiredService<ImporterInstance>();
    return await instance.RunRhinoImport(filePath, projectId, modelId, account, cancellationToken);
  }
}
