using System.Collections.Concurrent;
using System.Diagnostics;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Plugin;

/// <summary>
/// OK.
/// Please do not try to generalize this class with other IdleManagers for whatever reason.
/// This class is simple, targeted to host app and singleton.
/// </summary>
public class RevitIdleManager(RevitContext revitContext)
{
  private readonly UIApplication _uiApplication = revitContext.UIApplication.NotNull();

  private readonly ConcurrentDictionary<string, Func<Task>> _calls = new();
  private volatile bool _hasSubscribed;

  /// <summary>
  /// Subscribe deferred action to Idling event to run it whenever Revit becomes idle.
  /// </summary>
  /// <param name="action"> Action to call whenever Revit becomes Idle.</param>
  /// some events in host app are trigerred many times, we might get 10x per object
  /// Making this more like a deferred action, so we don't update the UI many times
  public void SubscribeToIdle(string name, Action action)
  {
    // I want to be called back ONCE when the host app has become idle once more
    _calls[name] = () =>
    {
      action();
      return Task.CompletedTask;
    };

    if (_hasSubscribed)
    {
      return;
    }

    _hasSubscribed = true;
    _uiApplication.Idling += RevitAppOnIdle;
  }

  /// <summary>
  /// Run once on the next Revit idle tick (deduped by name).
  /// </summary>
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
    foreach (KeyValuePair<string, Func<Task>> kvp in _calls)
    {
      Debug.WriteLine($"{kvp.Key}");
      kvp.Value();
    }

    _calls.Clear();
    _uiApplication.Idling -= RevitAppOnIdle;

    // setting last will delay entering re-subscritption
    _hasSubscribed = false;
  }
}
