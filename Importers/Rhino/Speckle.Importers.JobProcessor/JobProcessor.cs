using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Logging;
using Speckle.Importers.JobProcessor.Domain;
using Speckle.Importers.JobProcessor.JobHandlers;
using Speckle.Importers.JobProcessor.JobQueue;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.JobProcessor;

internal sealed class JobProcessorInstance(
  Repository repository,
  ILogger<JobProcessorInstance> logger,
  IJobHandler jobHandler,
  IAccountFactory accountFactory,
  IClientFactory clientFactory,
  ISdkActivityFactory activityFactory
)
{
  private static readonly TimeSpan s_idleTimeout = TimeSpan.FromSeconds(1);

  public async Task StartProcessing(CancellationToken cancellationToken = default)
  {
    await using var connection = await repository.SetupConnection(cancellationToken).ConfigureAwait(false);

    logger.LogInformation("Listening for jobs...");

    while (true)
    {
      FileimportJob? job = await repository.GetNextJob(connection, cancellationToken);
      if (job == null)
      {
        logger.LogDebug("No job found, sleeping for {timeout}", s_idleTimeout);
        await Task.Delay(s_idleTimeout, cancellationToken);
        continue;
      }
      logger.LogInformation("Starting {jobId}", job.Id);

      using var activity = activityFactory.Start();
      using var scopeJobId = ActivityScope.SetTag("jobId", job.Id);
      using var scopeJobType = ActivityScope.SetTag("jobType", job.Payload.JobType);
      using var scopeAttempt = ActivityScope.SetTag("job.attempt", job.Attempt.ToString());
      using var scopeServerUrl = ActivityScope.SetTag("serverUrl", job.Payload.ServerUrl.ToString());
      using var scopeProjectId = ActivityScope.SetTag("projectId", job.Payload.ProjectId);
      using var scopeModelId = ActivityScope.SetTag("modelId", job.Payload.ModelId);
      using var scopeBlobId = ActivityScope.SetTag("blobId", job.Payload.BlobId);

      try
      {
        JobStatus jobStatus = await AttemptJob(job, cancellationToken);
        if (jobStatus == JobStatus.QUEUED)
        {
          await repository.ReturnJobToQueued(connection, job.Id, cancellationToken);
        }
        activity?.SetStatus(SdkActivityStatusCode.Ok);
      }
      catch (Exception ex)
      {
        activity?.RecordException(ex);
        activity?.SetStatus(SdkActivityStatusCode.Error);
        throw;
      }
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
      reason = string.IsNullOrEmpty(ex.Message) ? ex.GetType().ToString() : ex.Message,
      result = new FileImportResult(0, 0, 0, "Rhino Importer", versionId: null)
    };
    await client.FileImport.FinishFileImportJob(input, cancellationToken);
  }

  private async Task<IClient> SetupClient(FileimportJob job, CancellationToken cancellationToken)
  {
    var account = await accountFactory.CreateAccount(
      job.Payload.ServerUrl,
      job.Payload.Token,
      cancellationToken: cancellationToken
    );

    return clientFactory.Create(account);
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types")]
  private async Task<JobStatus> AttemptJob(FileimportJob job, CancellationToken cancellationToken)
  {
    using var activity = activityFactory.Start();

    IClient? speckleClient = null;
    try
    {
      speckleClient = await SetupClient(job, cancellationToken);
      using var userScope = UserActivityScope.AddUserScope(speckleClient.Account);

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

        activity?.SetStatus(SdkActivityStatusCode.Ok);
        return JobStatus.SUCCEEDED;
      }
      catch (JobTimeoutException ex)
      {
        logger.LogInformation(ex, "Executing job timed out");

        if (job.Attempt >= job.MaxAttempt)
        {
          throw new MaxAttemptsExceededException("The final attempt to process the job failed", ex);
        }

        activity?.RecordException(ex);
        activity?.SetStatus(SdkActivityStatusCode.Error);
        return JobStatus.QUEUED;
      }
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Attempt {attempt} to process {jobId} failed", job.Attempt, job.Id);

      if (speckleClient is not null)
      {
        await ReportFailed(job, speckleClient, ex, cancellationToken);
      }

      activity?.RecordException(ex);
      activity?.SetStatus(SdkActivityStatusCode.Error);
      return JobStatus.FAILED;
    }
    finally
    {
      speckleClient?.Dispose();
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
      return await jobHandler.ProcessJob(job, client, linkedSource.Token);
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
