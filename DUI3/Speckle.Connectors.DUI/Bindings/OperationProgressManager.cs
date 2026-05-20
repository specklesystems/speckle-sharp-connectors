using System.Collections.Concurrent;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.DUI.Bindings;

/// <summary>
/// Debouncing progress for every %1 update for UI.
/// This class requires a specific bridge in its binding, so registering it will create random bridge which we don't want to.
/// </summary>
[GenerateAutoInterface]
public class OperationProgressManager : IOperationProgressManager
{
  private class NonUIThreadProgress<T>(Action<T> handler) : IProgress<T>
  {
    public void Report(T value) => handler(value);
  }

  private const string SET_MODEL_PROGRESS_UI_COMMAND_NAME = "setModelProgress";
  private static readonly ConcurrentDictionary<string, (DateTime LastCallTime, string Status, double? LastProgress)>
    s_lastProgressValues = new();
  private const int THROTTLE_INTERVAL_MS = 400;

  public IProgress<CardProgress> CreateOperationProgressEventHandler(
    IBrowserBridge bridge,
    string modelCardId,
    CancellationToken cancellationToken
  )
  {
    var progress = new NonUIThreadProgress<CardProgress>(args =>
    {
      SetModelProgress(
        bridge,
        modelCardId,
        new ModelCardProgress(modelCardId, args.Status, args.Progress),
        cancellationToken
      );
    });
    return progress;
  }

  public void SetModelProgress(
    IBrowserBridge bridge,
    string modelCardId,
    ModelCardProgress progress,
    CancellationToken cancellationToken
  )
  {
    if (cancellationToken.IsCancellationRequested)
    {
      return;
    }

    if (!s_lastProgressValues.TryGetValue(modelCardId, out var last))
    {
      s_lastProgressValues[modelCardId] = (DateTime.Now, progress.Status, progress.Progress);
      SendProgress(bridge, modelCardId, progress);
      return;
    }

    var currentTime = DateTime.Now;
    var elapsedMs = (currentTime - last.LastCallTime).TotalMilliseconds;
    var statusChanged = !string.Equals(progress.Status, last.Status, StringComparison.Ordinal);
    var progressChanged =
      progress.Progress.HasValue != last.LastProgress.HasValue
      || (
        progress.Progress.HasValue
        && last.LastProgress.HasValue
        && Math.Abs(progress.Progress.Value - last.LastProgress.Value) >= 0.01
      );

    if (elapsedMs < THROTTLE_INTERVAL_MS && !statusChanged && !progressChanged)
    {
      return;
    }

    s_lastProgressValues[modelCardId] = (currentTime, progress.Status, progress.Progress);
    SendProgress(bridge, modelCardId, progress);
  }

  private static void SendProgress(IBrowserBridge bridge, string modelCardId, ModelCardProgress progress) =>
    bridge.SendProgress(SET_MODEL_PROGRESS_UI_COMMAND_NAME, new { modelCardId, progress });
}
