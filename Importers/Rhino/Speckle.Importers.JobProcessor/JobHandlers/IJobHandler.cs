using Speckle.Importers.JobProcessor.Domain;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.JobProcessor.JobHandlers;

internal interface IJobHandler
{
  public Task<Version> ProcessJob(FileimportJob job, CancellationToken cancellationToken);
}
