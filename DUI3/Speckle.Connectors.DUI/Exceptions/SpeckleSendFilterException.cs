using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Connectors.DUI.Exceptions;

/// <summary>
/// Thrown when the user-input via Send Filter is not valid for an ingestion (send)
/// e.g.  Zero objects selected, View doesn't exists.
/// </summary>
/// <remarks>
/// Should only be used for "bad user input", and not for general/unexpected errors.
/// <see cref="IngestionValidationException"/>
/// </remarks>
public class SpeckleSendFilterException : IngestionValidationException
{
  public SpeckleSendFilterException() { }

  public SpeckleSendFilterException(string? message)
    : base(message) { }

  public SpeckleSendFilterException(string? message, Exception? innerException)
    : base(message, innerException) { }
}
