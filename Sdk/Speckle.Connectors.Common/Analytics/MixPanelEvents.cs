namespace Speckle.Connectors.Common.Analytics;

/// <summary>
/// Default Mixpanel events
/// </summary>
public enum AnalyticsEvent
{
  /// <summary>
  /// Event triggered when data is sent to a Speckle Server
  /// </summary>
  Send,

  /// <summary>
  /// Event triggered when data is received from a Speckle Server
  /// </summary>
  Receive,
}
