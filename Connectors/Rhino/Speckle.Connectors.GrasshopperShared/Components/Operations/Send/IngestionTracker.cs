using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Enums;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Send;

/// <summary>
/// The server-side half of a Publish: polls ingestion status via the SDK's GraphQL query API and blocks until the
/// ingestion reaches a terminal state (success/failed/cancelled), then stamps the version message the server had no
/// way of knowing about.
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

  public async Task<string> WaitForIngestionCompletion(
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
          return ingestion.statusData.versionId.NotNull();
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

  /// <summary>
  /// Writes <paramref name="versionMessage"/> onto a version the server minted from an ingestion. No-op when the
  /// user left the input empty.
  /// </summary>
  /// <remarks>
  /// NOTE: the version message can only be set after the fact on these rails. The packfile / artefact / bundle sends
  /// create the version server-side once ingestion finishes, and neither the ingestion nor the upload carries a
  /// message - only the legacy <c>completeWithVersion</c> rail takes one from the client, and that one is the
  /// fallback we no longer hit. So <c>SendOperation</c>'s versionMessage is silently dropped there, and the caller
  /// has to stamp it once the version exists (ENG-9835).
  /// </remarks>
  public async Task SetVersionMessage(
    IClient client,
    string projectId,
    string versionId,
    string? versionMessage,
    CancellationToken cancellationToken
  )
  {
    if (string.IsNullOrWhiteSpace(versionMessage))
    {
      return;
    }

    await client.Version.Update(new(versionId, projectId, versionMessage), cancellationToken).ConfigureAwait(false);
  }
}
