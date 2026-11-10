using Rhino;
using Speckle.Sdk;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Runs document writes from binding methods on Rhino's main thread. Bindings run on the bridge's worker thread, and
/// on macOS anything that marks the document edited or touches the UI throws off the main thread and takes Rhino
/// down. Blocks the worker until the action has run so results and exceptions still reach the DUI. FEA-124.
/// </summary>
internal static class MacMainThread
{
  public static void Run(Action action)
  {
    if (!RhinoApp.InvokeRequired)
    {
      action();
      return;
    }

    var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    RhinoApp.InvokeOnUiThread(() =>
    {
      try
      {
        action();
        done.SetResult();
      }
      catch (Exception ex)
      {
        done.SetException(ex);
        if (ex.IsFatal())
        {
          throw;
        }
      }
    });
    done.Task.GetAwaiter().GetResult();
  }
}
