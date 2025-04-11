using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Plugin;

public sealed class RevitIdleManager : AppIdleManager
{
  private readonly UIApplication _uiApplication;
  private readonly IIdleCallManager _idleCallManager;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;

  private event EventHandler<IdlingEventArgs>? OnIdle;

  public RevitIdleManager(
    RevitContext revitContext,
    IIdleCallManager idleCallManager,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IRevitTask revitTask
  )
    : base(idleCallManager)
  {
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _uiApplication = revitContext.UIApplication.NotNull();
    _idleCallManager = idleCallManager;
    revitTask.Run(
      () => _uiApplication.Idling += (s, e) => OnIdle?.Invoke(s, e) // will be called on the main thread always and fixing the Revit exceptions on subscribing/unsubscribing Idle events
    );
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
}
