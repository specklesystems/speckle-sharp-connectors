using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Plugin;

public interface IRevitIdleManager : IAppIdleManager
{
  public void RunAsync(Action action);
}

public sealed class RevitIdleManager(
  RevitContext revitContext,
  IIdleCallManager idleCallManager,
  ITopLevelExceptionHandler topLevelExceptionHandler
) : AppIdleManager(idleCallManager), IRevitIdleManager
{
  private readonly UIApplication _uiApplication = revitContext.UIApplication.NotNull();
  private readonly IIdleCallManager _idleCallManager = idleCallManager;

  protected override void AddEvent()
  {
    topLevelExceptionHandler.CatchUnhandled(() =>
    {
      try
      {
        _uiApplication.Idling += RevitAppOnIdle;
      }
      catch (Autodesk.Revit.Exceptions.InvalidOperationException)
      {
        // This happens very rarely, see previous report [CNX-125: Autodesk.Revit.Exceptions.InvalidOperationException: Can not subscribe to an event during execution of that event!](https://linear.app/speckle/issue/CNX-125/autodeskrevitexceptionsinvalidoperationexception-can-not-subscribe-to)
      }
    });
  }

  private void RevitAppOnIdle(object? sender, IdlingEventArgs e) =>
    _idleCallManager.AppOnIdle(() => _uiApplication.Idling -= RevitAppOnIdle);

  public void RunAsync(Action action)
  {
#if REVIT2025
    global::Revit.Async.RevitTask.RunAsync(action);
#else
    action();
#endif
  }
}
