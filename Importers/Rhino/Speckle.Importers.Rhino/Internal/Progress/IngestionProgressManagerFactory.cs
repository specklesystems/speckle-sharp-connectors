using Microsoft.Extensions.Logging;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Models;

namespace Speckle.Importers.Rhino.Internal.Progress;

[GenerateAutoInterface(VisibilityModifier = "public")]
internal sealed class IngestionProgressManagerFactory(ILogger<IngestionProgressManager> logger)
  : IIngestionProgressManagerFactory
{
  public IIngestionProgressManager CreateInstance(
    IClient speckleClient,
    ModelIngestion ingestion,
    string projectId,
    CancellationToken cancellationToken
  )
  {
    return new IngestionProgressManager(logger, speckleClient, ingestion, projectId, cancellationToken);
  }
}
