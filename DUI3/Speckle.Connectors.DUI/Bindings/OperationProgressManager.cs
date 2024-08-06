using System.Collections.Concurrent;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;

namespace Speckle.Connectors.DUI.Bindings;

/// <summary>
/// Debouncing progress for every %1 update for UI.
/// This class requires a specific bridge in its binding, so registering it will create random bridge which we don't want to.
/// </summary>
public class OperationProgressManager
{
  private const string SET_MODEL_PROGRESS_UI_COMMAND_NAME = "setModelProgress";
  private static readonly ConcurrentDictionary<string, (DateTime lastCallTime, string status)> s_lastProgressValues =
    new();
  private const int THROTTLE_INTERVAL_MS = 50;

  private IBridge Bridge { get; }

  public OperationProgressManager(IBridge bridge)
  {
    Bridge = bridge;
  }

  public void SetModelProgress(string modelCardId, ModelCardProgress progress, CancellationTokenSource cts)
  {
    if (cts.IsCancellationRequested)
    {
      return;
    }

    if (!s_lastProgressValues.TryGetValue(modelCardId, out (DateTime, string) t))
    {
      t.Item1 = DateTime.Now;
      s_lastProgressValues[modelCardId] = (t.Item1, progress.Status);
      // Since it's the first time we get a call for this model card, we should send it out
      SendProgress(modelCardId, progress);
      return;
    }

    var elapsedMs = (DateTime.Now - t.Item1).Milliseconds;

    if (elapsedMs < THROTTLE_INTERVAL_MS && t.Item2 == progress.Status)
    {
      return;
    }
    SendProgress(modelCardId, progress);
    s_lastProgressValues[modelCardId] = (DateTime.Now, progress.Status);
  }

  private void SendProgress(string modelCardId, ModelCardProgress progress) =>
    Bridge.Send(SET_MODEL_PROGRESS_UI_COMMAND_NAME, new { modelCardId, progress });
}
