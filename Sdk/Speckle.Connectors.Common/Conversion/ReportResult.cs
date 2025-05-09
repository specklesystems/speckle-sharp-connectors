using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Common.Conversion;

// Removing this for now, this was to faciliate us sending conversion results inside the commit object
// We may want this back in future, but need to spark a discussion first
// public sealed class Report : Base
// {
//   public required IEnumerable<ConversionResult> ConversionResults { get; set; }
// }

public enum Status
{
  NONE = 0, // Do not fucking use
  SUCCESS = 1,
  INFO = 2, // Not in use yet, maybe later as discussed
  WARNING = 3, // Not in use yet, maybe later as discussed
  ERROR = 4
}

public sealed class SendConversionResult : ConversionResult
{
  public SendConversionResult(
    Status status,
    string sourceId,
    string sourceType,
    Base? result = null,
    Exception? exception = null
  )
  {
    Status = status;
    SourceId = sourceId;
    SourceType = sourceType;
    ResultId = result?.id;
    ResultType = result?.speckle_type;
    Error = FormatError(exception);
  }
}

// HACK: I've unsealed this for Grasshopper, non-ideal. Should be discussed and a better pattern may be implemented.
public class ReceiveConversionResult : ConversionResult
{
  public ReceiveConversionResult(
    Status status,
    Base source,
    string? resultId = null,
    string? resultType = null,
    Exception? exception = null
  )
  {
    Status = status;
    SourceId = source.id.NotNull();
    SourceType = source.speckle_type; // Note: we'll parse it nicely in FE
    ResultId = resultId;
    ResultType = resultType;
    Error = FormatError(exception);
  }
}

/// <summary>
/// Base class for which we inherit send or receive conversion results. Note, the properties Source* and Result* swap meaning if they are a
/// send conversion result or a receive conversion result - but i do not believe this requires fully separate classes, especially
/// for what this is meant to be at its core: a list of green or red checkmarks in the UI. To make DX easier, the two classes above embody
/// this one and provided clean constructors for each case.
/// POC: Inherits from Base so we can attach the conversion report to the root commit object. Can be revisited later (it's not a problem to not inherit from base).
/// </summary>
public abstract class ConversionResult
{
  public Status Status { get; protected init; }

  /// <summary>
  ///  For receive conversion reports, this is the id of the speckle object. For send, it's the host app object id.
  /// </summary>
  public string SourceId { get; protected init; }

  /// <summary>
  /// For receive conversion reports, this is the type of the speckle object. For send, it's the host app object type.
  /// </summary>
  public string SourceType { get; protected init; }

  /// <summary>
  /// For receive conversion reports, this is the id of the host app object. For send, it's the speckle object id.
  /// </summary>
  public string? ResultId { get; protected init; }

  /// <summary>
  /// For receive conversion reports, this is the type of the host app object. For send, it's the speckle object type.
  /// </summary>
  public string? ResultType { get; protected init; }

  /// <summary>
  /// The exception, if any.
  /// </summary>
  public ErrorWrapper? Error { get; protected init; }

  // /// <summary>
  // /// Makes it easy for the FE to discriminate (against report types, not people).
  // /// </summary>
  // public string Type => this.GetType().ToString();

  protected static ErrorWrapper? FormatError(Exception? exception)
  {
    if (exception is null)
    {
      return null;
    }

    return new ErrorWrapper()
    {
      Message = exception.Message,
      StackTrace = $"{exception.Message}\n{exception.StackTrace}"
    };
  }
}

/// <summary>
/// Wraps around exceptions to make them nicely serializable for the ui.
/// </summary>
public class ErrorWrapper
{
  public required string Message { get; set; }
  public required string StackTrace { get; set; }
}
