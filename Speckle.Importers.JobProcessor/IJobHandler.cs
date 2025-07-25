using Microsoft.Extensions.Logging;
using Speckle.Importers.JobProcessor.Domain;

namespace Speckle.Importers.JobProcessor;

public interface IJobHandler
{
  public Task ProcessJob(FileimportJob job, CancellationToken cancellationToken);
}

public class FakeJobHandler(ILogger<FakeJobHandler> logger) : IJobHandler
{
  public Task ProcessJob(FileimportJob job, CancellationToken cancellationToken)
  {
    logger.LogInformation($"Job {job.Id} has fake completed!");
    return Task.CompletedTask;
  }
}
