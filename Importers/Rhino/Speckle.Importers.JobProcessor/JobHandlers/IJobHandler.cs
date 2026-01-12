using Speckle.Importers.JobProcessor.Domain;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Models;

namespace Speckle.Importers.JobProcessor.JobHandlers;

internal interface IJobHandler
{
  /// <param name="job"></param>
  /// <param name="client"></param>
  /// <param name="cancellationToken"></param>
  /// <returns>root object id</returns>
  public Task<string> ProcessJob(
    FileimportJob job,
    IClient client,
    ModelIngestion ingestion,
    CancellationToken cancellationToken
  );
}
