using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Plugin;

[GenerateAutoInterface]
public sealed class RevitIdleManager(RevitContext revitContext, IIdleCallManager idleCallManager) : IRevitIdleManager
{
  private readonly UIApplication _uiApplication = revitContext.UIApplication.NotNull();

  /// <summary>
  /// Subscribe deferred action to Idling event to run it whenever Revit becomes idle.
  /// </summary>
  /// <param name="action"> Action to call whenever Revit becomes Idle.</param>
  /// some events in host app are trigerred many times, we might get 10x per object
  /// Making this more like a deferred action, so we don't update the UI many times
  public void SubscribeToIdle(string id, Action action) =>
    idleCallManager.SubscribeToIdle(
      id,
      action,
      () =>
      {
        try
        {
          _uiApplication.Idling += RevitAppOnIdle;
        }
        catch (Exception e) when (!e.IsFatal())
        {
          // TODO: wrap this guy in the top level exception handler (?)
          // This happens very rarely, see previous report [CNX-125: Autodesk.Revit.Exceptions.InvalidOperationException: Can not subscribe to an event during execution of that event!](https://linear.app/speckle/issue/CNX-125/autodeskrevitexceptionsinvalidoperationexception-can-not-subscribe-to)
        }
      }
    );

  private void RevitAppOnIdle(object? sender, IdlingEventArgs e) =>
    idleCallManager.AppOnIdle(() => _uiApplication.Idling -= RevitAppOnIdle);

  public void RunAsync(Action action)
  {
#if REVIT2025
    global::Revit.Async.RevitTask.RunAsync(action);
#else
    action();
#endif
  }
}
