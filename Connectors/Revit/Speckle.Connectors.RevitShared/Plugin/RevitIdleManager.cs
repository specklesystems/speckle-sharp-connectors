using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Threading;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Plugin;

public interface IRevitIdleManager : IAppIdleManager
{
  public void RunAsync(Action action);
}

public sealed class RevitIdleManager : AppIdleManager, IRevitIdleManager
{
  private readonly UIApplication _uiApplication;
  private readonly IIdleCallManager _idleCallManager;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;

  private event EventHandler<IdlingEventArgs>? OnIdle;

  public RevitIdleManager(
    RevitContext revitContext,
    IIdleCallManager idleCallManager,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
    : base(idleCallManager)
  {
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _uiApplication = revitContext.UIApplication.NotNull();
    _idleCallManager = idleCallManager;
    _uiApplication.Idling += (s, e) => OnIdle?.Invoke(s, e); // will be called on the main thread always and fixing the Revit exceptions on subscribing/unsubscribing Idle events
  }

  protected override void AddEvent()
  {
    _topLevelExceptionHandler.CatchUnhandled(() =>
    {
      OnIdle += RevitAppOnIdle;
    });
  }

  private void RevitAppOnIdle(object? sender, IdlingEventArgs e) =>
    _idleCallManager.AppOnIdle(() => OnIdle -= RevitAppOnIdle);

  public void RunAsync(Action action)
  {
    if (!MainThreadContext.IsMainThread)
    {
      Console.WriteLine("Running async on a non-main thread!");
    }
#if REVIT2025
    global::Revit.Async.RevitTask.RunAsync(action);
#else
    action();
#endif
  }
}
