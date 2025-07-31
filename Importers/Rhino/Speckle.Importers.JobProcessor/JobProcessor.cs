using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Speckle.Importers.JobProcessor.Domain;

namespace Speckle.Importers.JobProcessor;

public sealed class JobProcessorInstance(
  Repository repository,
  ILogger<JobProcessorInstance> logger,
  IJobHandler jobHandler
)
{
  private static readonly TimeSpan s_idleTimeout = TimeSpan.FromSeconds(1);

  [SuppressMessage("Design", "CA1031:Do not catch general exception types")]
  public async Task StartProcessing(CancellationToken cancellationToken = default)
  {
    await using var connection = await repository.SetupConnection(cancellationToken).ConfigureAwait(false);

    while (true)
    {
      await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
      FileimportJob? job = await repository.GetNextJob(connection, transaction, cancellationToken);
      if (job == null)
      {
        logger.LogDebug("No job found, sleeping for {timeout}", s_idleTimeout);
        await Task.Delay(s_idleTimeout, cancellationToken);
        continue;
      }

      var jobId = job.Id;
      var jobStatus = JobStatus.QUEUED;
      var attempt = job.Attempt + 1;

      try
      {
        logger.LogDebug("Job {jobId} found!", job.Id);
        jobStatus = await ExecuteJob(job, cancellationToken);
      }
      catch (Exception ex)
      {
        logger.LogError(
          ex,
          "Attempt {attempt} to process {jobId} failed with {exception}",
          attempt,
          jobId,
          ex.GetType()
        );
        jobStatus = JobStatus.FAILED;
      }
      finally
      {
        await repository.SetJobStatus(connection, transaction, jobId, jobStatus, attempt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
      }
    }
  }

  private async Task<JobStatus> ExecuteJob(FileimportJob job, CancellationToken cancellationToken)
  {
    using CancellationTokenSource timeout = new();
    timeout.CancelAfter(TimeSpan.FromSeconds(job.Payload.TimeOutSeconds));
    using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
      timeout.Token,
      cancellationToken
    );
    try
    {
      logger.LogInformation("Starting job");

      await jobHandler.ProcessJob(job, linkedSource.Token);
      logger.LogInformation("Finished Job");

      return JobStatus.SUCCEEDED;
    }
    catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
    {
      logger.LogInformation(ex, "Executing job timed out");
      return job.Attempt < job.MaxAttempt
        ? JobStatus.QUEUED
        : throw new OperationCanceledException(
          $"Cancelling import after it exhausted retry attempts (was attempted {job.Attempt}/{job.MaxAttempt} times)",
          ex
        );
    }
  }
}
