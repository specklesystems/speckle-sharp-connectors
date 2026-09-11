namespace Speckle.Connectors.Common.Settings;

public sealed class GlobalConfig
{
  /// <summary>
  /// Setting this to <see langword="false"/> will suppress any UI notifications when updates are available.
  /// </summary>
  /// <remarks>
  /// Many IT teams installing Speckle have full control over update cadence,
  /// and end users may not beable to update themselves.
  /// </remarks>
  public bool IsUpdateNotificationDisabled { get; init; }

  /// <summary>
  /// The Url of the speckle server which the UI will handle as "default".
  /// </summary>
  /// <remarks>
  /// Will be <c>https://app.speckle.systems</c> for most installations,
  /// but we allow self hosters (e.g. enterprise self hosters) to override with their own URL
  /// </remarks>
  public Uri DefaultSpeckleServerUrl { get; init; }

  /// <summary>
  /// The Url of DUI which connectors will load
  /// </summary>
  /// <remarks>
  /// will be <c>https://dui.speckle.systems</c> for most installations,
  /// but we allow self hosters (e.g. enterprise self hosters) to override with their own URL
  /// </remarks>
  public Uri DuiUrl { get; init; }
}
