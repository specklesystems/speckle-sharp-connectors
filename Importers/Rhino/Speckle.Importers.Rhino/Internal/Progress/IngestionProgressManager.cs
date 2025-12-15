using System.Diagnostics;
using System.Diagnostics.Contracts;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Operations;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Api.GraphQL.Models;

namespace Speckle.Importers.Rhino.Internal.Progress;

public partial interface IIngestionProgressManager : IProgress<CardProgress>;

/// <summary>
/// An <see langword="IProgress{IngestionProgressEventArgs}"/> implementation for the entire client side Ingestion progress update reporting
/// Will throttles ingestion progress messages and reports their progress
/// </summary>
/// <remarks>
/// The same class exists also in the RVT ODA codebase
/// </remarks>
[GenerateAutoInterface(VisibilityModifier = "public")]
internal sealed class IngestionProgressManager(
  ILogger<IngestionProgressManager> logger,
  IClient speckleClient,
  ModelIngestion ingestion,
  string projectId,
  CancellationToken cancellationToken
) : IIngestionProgressManager
{
  /// <remarks>
  /// We've picked quite a coarse throttle window to try and avoid over pressure
  /// </remarks>
  private static readonly TimeSpan s_maxUpdatePeriod = TimeSpan.FromSeconds(1);
  private Task? _lastUpdate;
  private long _lastUpdatedAt;
  private readonly object _lock = new();

  [AutoInterfaceIgnore]
  public void Report(CardProgress value)
  {
    cancellationToken.ThrowIfCancellationRequested();

    string trimmedMessage;
    lock (_lock)
    {
      if (ShouldIgnoreProgressUpdate())
      {
        return;
      }

      OverPressureCheck();

      _lastUpdatedAt = Stopwatch.GetTimestamp();

      trimmedMessage = value.Status.TrimEnd('.');

      _lastUpdate = speckleClient
        .Ingestion.UpdateProgress(
          new ModelIngestionUpdateInput(ingestion.id, projectId, trimmedMessage, value.Progress),
          cancellationToken
        )
        .ContinueWith(
          HandleFaultedContinuation,
          CancellationToken.None,
          TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
          TaskScheduler.Default
        );
    }

    logger.LogInformation("Progress update {Message} {Progress}", trimmedMessage, value.Progress);
  }

  /// <remarks>
  /// I'm concerned that the time it takes for e2e update progress takes longer than MAX_UPDATE_FREQUENCY_MS
  /// with high enough latency, say during times of high load or with high latency regions
  /// </remarks>
  private void OverPressureCheck()
  {
    if (_lastUpdate is not null && !_lastUpdate.IsCompleted)
    {
      logger.LogWarning(
        "Sending progress updates too quickly! next update ready to send but the last progress is still updating!"
      );
    }
  }

  /// <returns><see langword="true"/> if the update should be ignored, otherwise <see langword="false"/></returns>
  [Pure]
  private bool ShouldIgnoreProgressUpdate()
  {
    TimeSpan msSinceLastUpdate = Stopwatch.GetElapsedTime(_lastUpdatedAt);
    return msSinceLastUpdate < s_maxUpdatePeriod;
  }

  private void HandleFaultedContinuation(Task updateTask)
  {
    // The progress report failed... could be many reasons.
    // For now, we're not letting this fail the Ingestion in any way
    // we'll log but otherwise let it slide while leaving no unobserved task exceptions
    if (updateTask.IsFaulted)
    {
      logger.LogWarning(updateTask.Exception, "A progress update failed unexpectedly");
    }
  }
}
