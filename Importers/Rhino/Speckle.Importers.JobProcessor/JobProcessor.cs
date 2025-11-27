using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Logging;
using Speckle.Importers.JobProcessor.Domain;
using Speckle.Importers.JobProcessor.JobHandlers;
using Speckle.Importers.JobProcessor.JobQueue;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Common;
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
) : BackgroundService
{
  private static readonly TimeSpan s_idleTimeout = TimeSpan.FromSeconds(1);

  protected override async Task ExecuteAsync(CancellationToken cancellationToken)
  {
    try
    {
      await RunJobProcessorLoop(cancellationToken);
    }
    catch (Exception ex)
    {
      const int EXIT_CODE = 1;
      logger.LogError(ex, "Background service failed, returning {ExitCode}", EXIT_CODE);
      Environment.ExitCode = EXIT_CODE;
      throw;
    }
  }

  private async Task RunJobProcessorLoop(CancellationToken cancellationToken)
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
      logger.LogInformation(
        "Starting {jobId}, attempt {attempt} / {maxAttempts} - it has {computeBudgetSeconds}s remaining",
        job.Id,
        job.Attempt,
        job.MaxAttempt,
        job.RemainingComputeBudgetSeconds
      );

      using var activity = activityFactory.Start();
      using var scopeJobId = ActivityScope.SetTag("jobId", job.Id);
      using var scopeJobType = ActivityScope.SetTag("jobType", job.Payload.JobType);
      using var scopeAttempt = ActivityScope.SetTag("job.attempt", job.Attempt.ToString());
      using var scopeServerUrl = ActivityScope.SetTag("serverUrl", job.Payload.ServerUrl.ToString());
      using var scopeProjectId = ActivityScope.SetTag("projectId", job.Payload.ProjectId);
      using var scopeModelId = ActivityScope.SetTag("modelId", job.Payload.ModelId);
      using var scopeBlobId = ActivityScope.SetTag("blobId", job.Payload.BlobId);
      using var scopeFileType = ActivityScope.SetTag("fileType", job.Payload.FileType);

      try
      {
        await AttemptJob(job, connection, cancellationToken);
        activity?.SetStatus(SdkActivityStatusCode.Ok);
      }
      catch (Exception ex)
      {
        // This is a very exceptional case, something is wrong with our infra
        activity?.RecordException(ex);
        activity?.SetStatus(SdkActivityStatusCode.Error);
        throw;
      }
    }
  }

  private async Task ReportSuccess(
    FileimportJob job,
    Version version,
    IClient client,
    double elapsedSeconds,
    CancellationToken cancellationToken
  )
  {
    logger.LogInformation(
      "Attempt {attempt} of {jobId} has succeeded creating {versionId} after {elapsedSeconds}",
      job.Attempt,
      job.Id,
      version.id,
      elapsedSeconds
    );

    var input = new FileImportSuccessInput
    {
      projectId = job.Payload.ProjectId,
      jobId = job.Payload.BlobId,
      warnings = [],
      result = new FileImportResult(elapsedSeconds, 0, 0, "Rhino Importer", versionId: version.id)
    };
    await client.FileImport.FinishFileImportJob(input, cancellationToken);
  }

  private async Task ReportFailed(
    FileimportJob job,
    IClient client,
    Exception ex,
    double elapsedSeconds,
    CancellationToken cancellationToken
  )
  {
    logger.LogError(
      ex,
      "Attempt {attempt} to process {jobId} failed after {elapsedSeconds}",
      job.Attempt,
      job.Id,
      elapsedSeconds
    );

    var input = new FileImportErrorInput()
    {
      projectId = job.Payload.ProjectId,
      jobId = job.Payload.BlobId,
      warnings = [],
      reason = string.IsNullOrEmpty(ex.Message) ? ex.GetType().ToString() : ex.Message,
      result = new FileImportResult(elapsedSeconds, 0, 0, "Rhino Importer", versionId: null)
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
  private async Task AttemptJob(FileimportJob job, IDbConnection connection, CancellationToken cancellationToken)
  {
    using var activity = activityFactory.Start();
    IClient? speckleClient = null;
    Stopwatch stopwatch = Stopwatch.StartNew();
    double totalElapsedSeconds = 0;
    try
    {
      speckleClient = await SetupClient(job, cancellationToken);
      using var userScope = UserActivityScope.AddUserScope(speckleClient.Account);

      if (job.Attempt > job.MaxAttempt)
      {
        //something went wrong, it should have been marked as failed
        throw new MaxAttemptsExceededException("Unhandled error silently failed the job multiple times");
      }

      Version version = await ExecuteJobWithTimeout(job, speckleClient, cancellationToken);
      totalElapsedSeconds = stopwatch.Elapsed.TotalSeconds;

      await ReportSuccess(job, version, speckleClient, totalElapsedSeconds, cancellationToken);

      activity?.SetStatus(SdkActivityStatusCode.Ok);
    }
    catch (Exception ex)
    {
      activity?.RecordException(ex);
      activity?.SetStatus(SdkActivityStatusCode.Error);

      totalElapsedSeconds = stopwatch.Elapsed.TotalSeconds;

      try
      {
        await ReportFailed(job, speckleClient.NotNull(), ex, totalElapsedSeconds, cancellationToken);
      }
      catch (Exception ex2)
      {
        logger.LogError(new AggregateException(ex, ex2), "Failed to report failure status");
        await repository.ReturnJobToQueued(connection, job.Id, cancellationToken);

        if (ex2.IsFatal())
        {
          throw;
        }
      }
    }
    finally
    {
      speckleClient?.Dispose();

      if (totalElapsedSeconds <= 0)
      {
        totalElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
      }
      await repository.DeductFromComputeBudget(connection, job.Id, (long)totalElapsedSeconds, cancellationToken);
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
    //respect the remaining compute budget
    int jobTimeout = Math.Max(0, Math.Min(job.Payload.TimeOutSeconds, job.RemainingComputeBudgetSeconds));

    using CancellationTokenSource timeout = new();
    timeout.CancelAfter(TimeSpan.FromSeconds(jobTimeout));
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
      throw new JobTimeoutException($"Job was cancelled due to reaching the {jobTimeout} second timeout", ex);
    }
  }
}
