using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Plugin;

/// <remarks>
/// Please do NOT try and refactor this class.
/// Whether it's to try and generalize with the <see cref="IdleCallManager"/> class
/// or to unnecessary try and make this class thread safe.
/// This class is a simple singleton, targeted to a Revit's host app requirements
/// where everything happens on the main thread, and we can avoid overly complex threading/thread-safty.
///
/// Previous good refactors with good intention have lead to poor debugging experiences, over-engineered threading,
/// and low confidence in the reliability.
/// </remarks>
/// should be registered as singleton
public class RevitIdleManager(
  ILogger<RevitIdleManager> asdf,
  RevitContext revitContext,
  ITopLevelExceptionHandler topLevelExceptionHandler
)
{
  private readonly UIApplication _uiApplication = revitContext.UIApplication.NotNull();

  private readonly Dictionary<string, Func<Task>> _calls = new();
  private bool _hasSubscribed;

  /// <summary>
  /// Defers the invocation of an <paramref name="action"/> until next Revit idle tick (deduped by name).
  /// The <paramref name="action"/> will be called only once.
  /// </summary>
  /// <param name="name">A key that prevents enqueuing duplicate events</param>
  /// <param name="action">The action to be invoked</param>
  /// <example>
  /// Some events in host app are triggered many times, we might get 10x per object
  /// Making this more like a deferred action, so we don't update the UI many times
  /// </example>
  /// <remarks>
  /// This function must be called on the main thread
  /// </remarks>
  public void SubscribeToIdle(string name, Action action)
  {
    SubscribeToIdle(
      name,
      () =>
      {
        action.Invoke();
        return Task.CompletedTask;
      }
    );
  }

  /// <inheritdoc cref="SubscribeToIdle(string, Action)"/>
  public void SubscribeToIdle(string name, Func<Task> action)
  {
    _calls[name] = action;

    if (_hasSubscribed)
    {
      return;
    }
    _hasSubscribed = true;

    _uiApplication.Idling += RevitAppOnIdle;
  }

  private void RevitAppOnIdle(object? sender, IdlingEventArgs e)
  {
    topLevelExceptionHandler.CatchUnhandled(() =>
    {
      foreach (KeyValuePair<string, Func<Task>> kvp in _calls)
      {
        topLevelExceptionHandler.FireAndForget(kvp.Value.Invoke);
      }

      _calls.Clear();
      _uiApplication.Idling -= RevitAppOnIdle;

      // setting last will delay entering re-subscription
      _hasSubscribed = false;
    });
  }
}
