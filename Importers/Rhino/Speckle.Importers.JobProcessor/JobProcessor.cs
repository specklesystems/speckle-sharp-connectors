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
using Speckle.Sdk.Api.GraphQL.Enums;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;

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
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Job Processor crashed");
      Environment.Exit(1); //This is the only reliable way I've managed to figure out how to get windows services retry policy to actually kick in (see https://github.com/dotnet/runtime/issues/67146)
      throw;
    }
  }

  private async Task RunJobProcessorLoop(CancellationToken serviceCancellationToken)
  {
    await using var connection = await repository.SetupConnection(serviceCancellationToken).ConfigureAwait(false);

    logger.LogInformation("Listening for jobs...");

    while (true)
    {
      FileimportJob? job = await repository.GetNextJob(connection, serviceCancellationToken);
      if (job == null)
      {
        logger.LogDebug("No job found, sleeping for {Timeout}", s_idleTimeout);
        await Task.Delay(s_idleTimeout, serviceCancellationToken);
        continue;
      }
      logger.LogInformation(
        "Starting {JobId}, attempt {Attempt} / {MaxAttempts} - it has {ComputeBudgetSeconds}s remaining",
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
      using var scopeModelIngestionId = ActivityScope.SetTag("modelIngestionId", job.Payload.ModelIngestionId);
      using var scopeBlobId = ActivityScope.SetTag("blobId", job.Payload.BlobId);
      using var scopeFileType = ActivityScope.SetTag("fileType", job.Payload.FileType);

      try
      {
        await AttemptJob(job, connection, serviceCancellationToken);
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
    string rootObjectId,
    IClient client,
    double elapsedSeconds,
    CancellationToken cancellationToken
  )
  {
    string versionId = await client.Ingestion.Complete(
      new(job.Payload.ModelIngestionId, job.Payload.ProjectId, rootObjectId),
      cancellationToken
    );
    logger.LogInformation(
      "Attempt {Attempt} of {JobId} has succeeded creating {VersionId} after {ElapsedSeconds}",
      job.Attempt,
      job.Id,
      versionId,
      elapsedSeconds
    );

    var input = new FileImportSuccessInput
    {
      projectId = job.Payload.ProjectId,
      jobId = job.Payload.BlobId,
      warnings = [],
      result = new FileImportResult(elapsedSeconds, 0, 0, "Rhino Importer", versionId: versionId)
    };
    await client.FileImport.FinishFileImportJob(input, CancellationToken.None);
  }

  private async Task ReportCancelled(FileimportJob job, IClient client, Exception ex, double elapsedSeconds)
  {
    await client.Ingestion.FailWithCancel(
      new ModelIngestionCancelledInput(
        job.Payload.ModelIngestionId,
        job.Payload.ProjectId,
        "The ingestion handler observed a cancellation request, and has cancelled the ingestion before its completion"
      ),
      CancellationToken.None
    );
    logger.LogError(
      ex,
      "Attempt {Attempt} to process {JobId} cancelled after {ElapsedSeconds}",
      job.Attempt,
      job.Id,
      elapsedSeconds
    );
  }

  private async Task ReportFailed(
    FileimportJob job,
    IClient client,
    Exception ex,
    double elapsedSeconds,
    CancellationToken cancellationToken
  )
  {
    await client.Ingestion.FailWithError(
      ModelIngestionFailedInput.FromException(job.Payload.ModelIngestionId, job.Payload.ProjectId, ex),
      cancellationToken
    );
    logger.LogError(
      ex,
      "Attempt {Attempt} to process {JobId} failed after {ElapsedSeconds}",
      job.Attempt,
      job.Id,
      elapsedSeconds
    );
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
  private async Task AttemptJob(FileimportJob job, IDbConnection connection, CancellationToken serviceCancellationToken)
  {
    using var activity = activityFactory.Start();
    IClient? speckleClient = null;
    Stopwatch stopwatch = Stopwatch.StartNew();
    double totalElapsedSeconds = 0;
    try
    {
      speckleClient = await SetupClient(job, serviceCancellationToken);
      using var userScope = UserActivityScope.AddUserScope(speckleClient.Account);

      if (job.Attempt > job.MaxAttempt)
      {
        //something went wrong, it should have been marked as failed
        throw new MaxAttemptsExceededException("Unhandled error silently failed the job multiple times");
      }

      string rootObjectId = await ExecuteJobWithTimeout(job, speckleClient, serviceCancellationToken);
      totalElapsedSeconds = stopwatch.Elapsed.TotalSeconds;

      await ReportSuccess(job, rootObjectId, speckleClient, totalElapsedSeconds, serviceCancellationToken);

      activity?.SetStatus(SdkActivityStatusCode.Ok);
    }
    catch (OperationCanceledException ex) when (serviceCancellationToken.IsCancellationRequested)
    {
      logger.LogInformation(
        ex,
        "Service cancellation has interrupted a processing job, returning the job to the queue"
      );
      await repository.ReturnJobToQueued(connection, job.Id, CancellationToken.None);
    }
    catch (Exception ex)
    {
      activity?.RecordException(ex);
      activity?.SetStatus(SdkActivityStatusCode.Error);

      totalElapsedSeconds = stopwatch.Elapsed.TotalSeconds;

      try
      {
        speckleClient.NotNull();

        switch (ex)
        {
          case OperationCanceledException when serviceCancellationToken.IsCancellationRequested:
            // Windows service shut down, re-queue job
            logger.LogWarning(
              ex,
              "Re-enqueueing {JobId} because it was interrupted by the windows service is stopping",
              job.Id
            );
            await repository.ReturnJobToQueued(connection, job.Id, CancellationToken.None); //this behaviour needs to be kept aligned with the server's GC behaviour
            await speckleClient.Ingestion.Requeue(
              new(job.Payload.ModelIngestionId, job.Payload.ProjectId, "Re-enqueuing job"),
              CancellationToken.None
            );
            break;
          case IngestionCancelledException { Ingestion.statusData.status: ModelIngestionStatus.failed }:
            // Server GC will fail inactive jobs AND request cancel (despite it not being an explicit user cancel request)
            // since the job is already in failed status, we don't need to try and move it to Canceled status
            break;
          case IngestionCancelledException:
            await ReportCancelled(job, speckleClient, ex, totalElapsedSeconds);
            break;
          default:
            await ReportFailed(job, speckleClient, ex, totalElapsedSeconds, serviceCancellationToken);
            break;
        }
      }
      catch (Exception ex2)
      {
        logger.LogError(new AggregateException(ex, ex2), "Failed to report failure status");
        // somehow we're in a weird state,
        // let's return the job to the queued state where it will get picked up again until one of total timeout,
        // max attempts, or exhausted compute budget is reached.
        // The server is responsible for garbage collecting jobs which have reached these error conditions and moving
        // them to a failed status.
        await repository.ReturnJobToQueued(connection, job.Id, CancellationToken.None);

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
      await repository.DeductFromComputeBudget(connection, job.Id, (long)totalElapsedSeconds, CancellationToken.None);
    }
  }

  /// <summary>
  ///
  /// </summary>
  /// <param name="job"></param>
  /// <param name="cancellationToken"></param>
  /// <returns>rootObjectId if attempt was successful</returns>
  /// <exception cref="OperationCanceledException">Timeout was reached AND MaxAttempt was reached</exception>
  private async Task<string> ExecuteJobWithTimeout(
    FileimportJob job,
    IClient client,
    CancellationToken cancellationToken
  )
  {
    ModelIngestion ingestion = await client.Ingestion.Get(
      job.Payload.ModelIngestionId,
      job.Payload.ProjectId,
      cancellationToken
    );

    //respect the remaining compute budget
    int jobTimeout = Math.Max(0, Math.Min(job.Payload.TimeOutSeconds, job.RemainingComputeBudgetSeconds));
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(jobTimeout));
    using CancellationTokenSource ingestionCancelled = new();

    using var subscription = client.Subscription.CreateProjectModelIngestionCancellationRequestedSubscription(
      job.Payload.ModelIngestionId,
      job.Payload.ProjectId
    );
    subscription.Listeners += (_, e) =>
    {
      logger.LogInformation(
        "Cancellation of {ModelIngestionId} has been requested via {Type} update ({IsCancellationRequested})",
        e.modelIngestion.id,
        e.type,
        e.modelIngestion.cancellationRequested
      );
      ingestion = e.modelIngestion;
      ingestionCancelled.Cancel();
    };

    using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
      timeout.Token,
      ingestionCancelled.Token,
      cancellationToken
    );
    try
    {
      return await jobHandler.ProcessJob(job, client, ingestion, linkedSource.Token);
    }
    catch (OperationCanceledException ex) when (ingestionCancelled.IsCancellationRequested)
    {
      throw new IngestionCancelledException("Ingestion cancellation was requested", ex) { Ingestion = ingestion };
    }
    catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
    {
      throw new JobTimeoutException($"Job was cancelled due to reaching the {jobTimeout} second timeout", ex);
    }
  }
}
