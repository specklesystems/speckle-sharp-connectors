﻿using System.Collections.Concurrent;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.DUI.Bindings;

/// <summary>
/// Debouncing progress for every %1 update for UI.
/// This class requires a specific bridge in its binding, so registering it will create random bridge which we don't want to.
/// </summary>
[GenerateAutoInterface]
public class OperationProgressManager : IOperationProgressManager
{
  private const string SET_MODEL_PROGRESS_UI_COMMAND_NAME = "setModelProgress";
  private static readonly ConcurrentDictionary<string, (DateTime lastCallTime, string status)> s_lastProgressValues =
    new();
  private const int THROTTLE_INTERVAL_MS = 200;

  public IProgress<CardProgress> CreateOperationProgressEventHandler(
    IBrowserBridge bridge,
    string modelCardId,
    CancellationToken cancellationToken
  )
  {
    var progress = new Progress<CardProgress>();
    progress.ProgressChanged += (_, args) =>
      bridge.TopLevelExceptionHandler.FireAndForget(
        () =>
          SetModelProgress(
            bridge,
            modelCardId,
            new ModelCardProgress(modelCardId, args.Status, args.Progress),
            cancellationToken
          )
      );
    return progress;
  }

  public async Task SetModelProgress(
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

    if (!s_lastProgressValues.TryGetValue(modelCardId, out (DateTime, string) t))
    {
      t.Item1 = DateTime.Now;
      s_lastProgressValues[modelCardId] = (t.Item1, progress.Status);
      // Since it's the first time we get a call for this model card, we should send it out
      await SendProgress(bridge, modelCardId, progress).ConfigureAwait(false);
      return;
    }

    var currentTime = DateTime.Now;
    var elapsedMs = (currentTime - t.Item1).Milliseconds;

    if (elapsedMs < THROTTLE_INTERVAL_MS && t.Item2 == progress.Status)
    {
      return;
    }
    s_lastProgressValues[modelCardId] = (currentTime, progress.Status);
    await SendProgress(bridge, modelCardId, progress).ConfigureAwait(false);
  }

  private static async Task SendProgress(IBrowserBridge bridge, string modelCardId, ModelCardProgress progress) =>
    await bridge.Send(SET_MODEL_PROGRESS_UI_COMMAND_NAME, new { modelCardId, progress }).ConfigureAwait(false);
}
