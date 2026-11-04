#if NETFRAMEWORK
using System.Windows.Threading;

namespace Speckle.Connectors.GrasshopperShared.HostApp.Extras;

/// <summary>
/// Provides Debounce() and Throttle() methods.
/// Use these methods to ensure that events aren't handled too frequently.
///
/// Throttle() ensures that events are throttled by the interval specified.
/// Only the last event in the interval sequence of events fires.
///
/// Debounce() fires an event only after the specified interval has passed
/// in which no other pending event has fired. Only the last event in the
/// sequence is fired.
/// </summary>
public class DebounceDispatcher
{
  private DispatcherTimer? _timer;
  private DateTime TimerStarted { get; set; } = DateTime.UtcNow.AddYears(-1);

  /// <summary>
  /// Debounce an event by resetting the event timeout every time the event is
  /// fired. The behavior is that the Action passed is fired only after events
  /// stop firing for the given timeout period.
  ///
  /// Use Debounce when you want events to fire only after events stop firing
  /// after the given interval timeout period.
  ///
  /// Wrap the logic you would normally use in your event code into
  /// the  Action you pass to this method to debounce the event.
  /// Example: https://gist.github.com/RickStrahl/0519b678f3294e27891f4d4f0608519a
  /// </summary>
  /// <param name="interval">Timeout in Milliseconds</param>
  /// <param name="action">Action<object> to fire when debounced event fires</object></param>
  /// <param name="param">optional parameter</param>
  /// <param name="priority">optional priorty for the dispatcher</param>
  /// <param name="disp">optional dispatcher. If not passed or null CurrentDispatcher is used.</param>
  public void Debounce(
    int interval,
    Action<object?> action,
    object? param = null,
    DispatcherPriority priority = DispatcherPriority.ApplicationIdle,
    Dispatcher? disp = null
  )
  {
    // kill pending timer and pending ticks
    _timer?.Stop();
    _timer = null;

    if (disp == null)
    {
      disp = Dispatcher.CurrentDispatcher;
    }

    // timer is recreated for each event and effectively
    // resets the timeout. Action only fires after timeout has fully
    // elapsed without other events firing in between
    _timer = new DispatcherTimer(
      TimeSpan.FromMilliseconds(interval),
      priority,
      (s, e) =>
      {
        if (_timer == null)
        {
          return;
        }

        _timer?.Stop();
        _timer = null;
        action.Invoke(param);
      },
      disp
    );

    _timer.Start();
  }

  /// <summary>
  /// This method throttles events by allowing only 1 event to fire for the given
  /// timeout period. Only the last event fired is handled - all others are ignored.
  /// Throttle will fire events every timeout ms even if additional events are pending.
  ///
  /// Use Throttle where you need to ensure that events fire at given intervals.
  /// </summary>
  /// <param name="interval">Timeout in Milliseconds</param>
  /// <param name="action">Action<object> to fire when debounced event fires</object></param>
  /// <param name="param">optional parameter</param>
  /// <param name="priority">optional priorty for the dispatcher</param>
  /// <param name="disp">optional dispatcher. If not passed or null CurrentDispatcher is used.</param>
  public void Throttle(
    int interval,
    Action<object?> action,
    object? param = null,
    DispatcherPriority priority = DispatcherPriority.ApplicationIdle,
    Dispatcher? disp = null
  )
  {
    // kill pending timer and pending ticks
    _timer?.Stop();
    _timer = null;

    if (disp == null)
    {
      disp = Dispatcher.CurrentDispatcher;
    }

    var curTime = DateTime.UtcNow;

    // if timeout is not up yet - adjust timeout to fire
    // with potentially new Action parameters
    if (curTime.Subtract(TimerStarted).TotalMilliseconds < interval)
    {
      interval -= (int)curTime.Subtract(TimerStarted).TotalMilliseconds;
    }

    _timer = new DispatcherTimer(
      TimeSpan.FromMilliseconds(interval),
      priority,
      (s, e) =>
      {
        if (_timer == null)
        {
          return;
        }

        _timer?.Stop();
        _timer = null;
        action.Invoke(param);
      },
      disp
    );

    _timer.Start();
    TimerStarted = curTime;
  }
}
#else
using Rhino;

namespace Speckle.Connectors.GrasshopperShared.HostApp.Extras;

/// <summary>
/// Debounce(): fires an action only after the specified interval has passed in which no other
/// pending call has been made. Only the last call in the sequence fires, on the Rhino UI thread.
/// </summary>
/// <remarks>
/// .NET Core heads (Rhino 8 Mac) have no WindowsBase/DispatcherTimer; <see cref="RhinoApp.InvokeOnUiThread"/> is the
/// marshal instead (rhino-mac-connector spec, ticket 02). A version counter rather than a timer field keeps the type
/// free of disposable state (CA1001 here and on the owning component). The net48 implementation above is untouched.
/// </remarks>
public class DebounceDispatcher
{
  private int _version;

  /// <summary>
  /// Debounce an event by resetting the timeout every time it is called. The action fires only after calls
  /// stop for the given timeout period.
  /// </summary>
  /// <param name="interval">Timeout in milliseconds</param>
  /// <param name="action">Action to fire, on the Rhino UI thread</param>
  /// <param name="param">Optional parameter passed to <paramref name="action"/></param>
  public void Debounce(int interval, Action<object?> action, object? param = null)
  {
    var version = Interlocked.Increment(ref _version);
    Task.Delay(interval)
      .ContinueWith(
        _ =>
        {
          if (Volatile.Read(ref _version) != version)
          {
            return; // superseded by a later call
          }
          RhinoApp.InvokeOnUiThread(() => action.Invoke(param));
        },
        TaskScheduler.Default
      );
  }
}
#endif
