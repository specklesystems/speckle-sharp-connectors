using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Enums;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Send;

/// <summary>
/// Polls ingestion status via the SDK's GraphQL query API
/// and blocks until the ingestion reaches a terminal state (success/failed/cancelled).
/// </summary>
/// <remarks>
/// We use polling instead of subscriptions because GH components call WaitForIngestionCompletion
/// after SendViaPackfile returns — by that point the server may have already completed
/// the ingestion. Setting up a new WebSocket subscription is too slow to catch fast completions.
/// Polling with Ingestion.Get() is reliable regardless of timing.
/// </remarks>
public class IngestionTracker
{
  private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(1);

  public async Task WaitForIngestionCompletion(
    IClient client,
    string projectId,
    string ingestionId,
    Action<string, double>? reportProgress,
    string? reportProgressId,
    CancellationToken cancellationToken
  )
  {
    // NOTE: before start hating from this - read the class description
    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var ingestion = await client.Ingestion.Get(ingestionId, projectId, cancellationToken).ConfigureAwait(false);
      var status = ingestion.statusData.status;

      switch (status)
      {
        case ModelIngestionStatus.success:
          return;
        case ModelIngestionStatus.failed:
          throw new SpeckleException($"Server processing failed: {ingestion.statusData.progressMessage}");
        case ModelIngestionStatus.cancelled:
          throw new OperationCanceledException("Ingestion was cancelled by the server");
        case ModelIngestionStatus.processing:
        case ModelIngestionStatus.queued:
          reportProgress?.Invoke(reportProgressId ?? "Server", 0);
          break;
      }

      await Task.Delay(s_pollInterval, cancellationToken).ConfigureAwait(false);
    }
  }
}
