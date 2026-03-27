namespace Speckle.Connectors.Logging;

public enum LoggingActivityKind
{
  /// <summary>
  /// Default value.
  /// Indicates that the Activity represents an internal operation within an application, as opposed to an operations with remote parents or children.
  /// </summary>
  Internal = 0,

  /// <summary>
  /// Server activity represents request incoming from external component.
  /// </summary>
  Server = 1,

  /// <summary>
  /// Client activity represents outgoing request to the external component.
  /// </summary>
  Client = 2,

  /// <summary>
  /// Producer activity represents output provided to external components.
  /// </summary>
  Producer = 3,

  /// <summary>
  /// Consumer activity represents output received from an external component.
  /// </summary>
  Consumer = 4,
}
