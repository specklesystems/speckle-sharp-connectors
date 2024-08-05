// using System.Collections.Concurrent;
// using System.Diagnostics;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;

namespace Speckle.Connectors.DUI.Bindings;

/// <summary>
/// Debouncing to progress update for UI.
/// </summary>
public class OperationProgressManager
{
  private const string SET_MODEL_PROGRESS_UI_COMMAND_NAME = "setModelProgress";
  private static readonly Dictionary<string, DateTime> s_lastUpdateTimes = new();

  // private static readonly Dictionary<string, Timer> s_timers = new();
  private const int THROTTLE_INTERVAL_MS = 200;

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

    var now = DateTime.UtcNow;
    if (!s_lastUpdateTimes.TryGetValue(modelCardId, out var lastUpdateTime))
    {
      lastUpdateTime = DateTime.MinValue;
    }

    var timeSinceLastUpdate = (now - lastUpdateTime).TotalMilliseconds;

    if (timeSinceLastUpdate >= THROTTLE_INTERVAL_MS)
    {
      // Send immediately if interval has passed
      SendProgress(modelCardId, progress);
      s_lastUpdateTimes[modelCardId] = now;
    }
    else
    {
      // // Schedule a send after the remaining interval time
      // var remainingTime = THROTTLE_INTERVAL_MS - (int)timeSinceLastUpdate;
      // if (s_timers.TryGetValue(modelCardId, out var existingTimer))
      // {
      //   existingTimer.Change(remainingTime, Timeout.Infinite);
      // }
      // else
      // {
      //   var timer = new Timer(
      //     _ =>
      //     {
      //       FinalizeProgress(modelCardId, progress, cts);
      //       s_lastUpdateTimes[modelCardId] = DateTime.UtcNow;
      //       s_timers.TryRemove(modelCardId, out Timer _);
      //     },
      //     null,
      //     (long)remainingTime,
      //     Timeout.Infinite
      //   );
      //
      //   s_timers[modelCardId] = timer;
      // }
    }
  }

  private void SendProgress(string modelCardId, ModelCardProgress progress)
  {
    Bridge.Send(SET_MODEL_PROGRESS_UI_COMMAND_NAME, new { modelCardId, progress });
  }

  // public void FinalizeProgress(string modelCardId, ModelCardProgress progress, CancellationTokenSource cts)
  // {
  //   if (cts.IsCancellationRequested)
  //   {
  //     return;
  //   }
  //
  //   // Cancel any existing timer and send the final progress update
  //   if (s_timers.TryRemove(modelCardId, out var timer))
  //   {
  //     timer.Dispose();
  //   }
  //
  //   SendProgress(modelCardId, progress);
  // }
}
