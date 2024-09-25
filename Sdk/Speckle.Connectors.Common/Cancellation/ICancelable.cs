namespace Speckle.Connectors.Common.Cancellation;

/// <summary>
/// Provides a mechanism for cancelling operations.
/// </summary>
public interface ICancelable
{
  public CancellationManager CancellationManager { get; }
}
