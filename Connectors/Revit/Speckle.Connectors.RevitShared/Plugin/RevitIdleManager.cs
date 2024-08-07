using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Common;
using Speckle.InterfaceGenerator;

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
        _uiApplication.Idling += RevitAppOnIdle;
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
