using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Speckle.Importers.JobProcessor.Domain;
using Speckle.Importers.JobProcessor.JobHandlers;
using Speckle.Importers.JobProcessor.JobQueue;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.JobProcessor;

internal sealed class JobProcessorInstance(
  Repository repository,
  ILogger<JobProcessorInstance> logger,
  IJobHandler jobHandler,
  IAccountFactory accountFactory,
  IClientFactory clientFactory
)
{
  private static readonly TimeSpan s_idleTimeout = TimeSpan.FromSeconds(1);

  [SuppressMessage("Design", "CA1031:Do not catch general exception types")]
  public async Task StartProcessing(CancellationToken cancellationToken = default)
  {
    await using var connection = await repository.SetupConnection(cancellationToken).ConfigureAwait(false);

    while (true)
    {
      FileimportJob? job = await repository.GetNextJob(connection, cancellationToken);
      if (job == null)
      {
        logger.LogInformation("No job found, sleeping for {timeout}", s_idleTimeout);
        await Task.Delay(s_idleTimeout, cancellationToken);
        continue;
      }
      logger.LogInformation("Starting {jobId}", job.Id);

      JobStatus jobStatus = await AttemptJob(job, cancellationToken);
      await repository.SetJobStatus(connection, job.Id, jobStatus, cancellationToken);
    }
  }

  private static async Task ReportSuccess(
    FileimportJob job,
    Version version,
    IClient client,
    CancellationToken cancellationToken
  )
  {
    var input = new FileImportSuccessInput
    {
      projectId = job.Payload.ProjectId,
      jobId = job.Payload.BlobId,
      warnings = [],
      result = new FileImportResult(0, 0, 0, "Rhino Importer", versionId: version.id)
    };
    await client.FileImport.FinishFileImportJob(input, cancellationToken);
  }

  private static async Task ReportFailed(
    FileimportJob job,
    IClient client,
    Exception ex,
    CancellationToken cancellationToken
  )
  {
    var input = new FileImportErrorInput()
    {
      projectId = job.Payload.ProjectId,
      jobId = job.Payload.BlobId,
      warnings = [],
      reason = ex.ToString(),
      result = new FileImportResult(0, 0, 0, "Rhino Importer", versionId: null)
    };
    await client.FileImport.FinishFileImportJob(input, cancellationToken);
  }

  private async Task<IClient> SetupClient(string token, CancellationToken cancellationToken)
  {
    var account = await accountFactory.CreateAccount(
      new("http://127.0.0.1"), // job.Payload.ServerUrl, //TODO: we should grab serverUrl from the job. But currently it reports the docker network url, which is no bueno
      token,
      cancellationToken: cancellationToken
    );

    return clientFactory.Create(account);
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types")]
  private async Task<JobStatus> AttemptJob(FileimportJob job, CancellationToken cancellationToken)
  {
    using IClient speckleClient = await SetupClient(job.Payload.Token, cancellationToken);
    try
    {
      if (job.Attempt > job.MaxAttempt)
      {
        //something went wrong, it should have been marked as failed
        throw new MaxAttemptsExceededException("Unhandled error silently failed the job multiple times");
      }

      try
      {
        Version version = await ExecuteJobWithTimeout(job, speckleClient, cancellationToken);
        await ReportSuccess(job, version, speckleClient, cancellationToken);
        logger.LogInformation("Job {jobId} has succeeded creating {versionId}", job.Id, version.id);

        return JobStatus.SUCCEEDED;
      }
      catch (JobTimeoutException ex)
      {
        logger.LogInformation(ex, "Executing job timed out");

        if (job.Attempt >= job.MaxAttempt)
        {
          throw new MaxAttemptsExceededException("The final attempt to process the job failed", ex);
        }

        return JobStatus.QUEUED;
      }
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Attempt {attempt} to process {jobId} failed", job.Attempt, job.Id);

      //TERMINAL ERROR
      await ReportFailed(job, speckleClient, ex, cancellationToken);
      return JobStatus.FAILED;
    }
  }

  /// <summary>
  ///
  /// </summary>
  /// <param name="job"></param>
  /// <param name="cancellationToken"></param>
  /// <returns><see cref="Version"/> if attempt was successful, <see langword="null"/> if job timedout, but can be re-attempted without exceeding <see cref="FileimportJob.MaxAttempt"/></returns>
  /// <exception cref="OperationCanceledException">Timeout was reached AND MaxAttempt was reached</exception>
  private async Task<Version> ExecuteJobWithTimeout(
    FileimportJob job,
    IClient client,
    CancellationToken cancellationToken
  )
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

      Version version = await jobHandler.ProcessJob(job, client, linkedSource.Token);
      logger.LogInformation("Finished Job");

      return version;
    }
    catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
    {
      throw new JobTimeoutException(
        $"Job was cancelled due to reaching the {job.Payload.TimeOutSeconds} second timeout",
        ex
      );
    }
  }
}
