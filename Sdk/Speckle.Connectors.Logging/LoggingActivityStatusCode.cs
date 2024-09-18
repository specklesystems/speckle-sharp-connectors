namespace Speckle.Connectors.Logging;

public enum LoggingActivityStatusCode
{
  /// <summary>Unset status code is the default value indicating the status code is not initialized.</summary>
  Unset,

  /// <summary>Status code indicating the operation has been validated and completed successfully.</summary>
  Ok,

  /// <summary>Status code indicating an error is encountered during the operation.</summary>
  Error,
}
