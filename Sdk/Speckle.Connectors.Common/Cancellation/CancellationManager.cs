using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Common.Cancellation;

public interface ICancellationItem
{
  CancellationToken Token { get; }
}

/// <summary>
/// Util class to manage cancellations.
/// </summary>
[GenerateAutoInterface]
public class CancellationManager : ICancellationManager
{
  private sealed class CancellationItem(CancellationManager manager, string id) : ICancellationItem
  {
    public CancellationToken Token => manager.GetToken(id);
  }

  /// <summary>
  /// Dictionary to relate <see cref="CancellationTokenSource"/> with registered id.
  /// </summary>
  private readonly Dictionary<string, CancellationTokenSource> _operationsInProgress = new();

  public int NumberOfOperations => _operationsInProgress.Count;

  /// <summary>
  /// Get token with registered id.
  /// </summary>
  /// <param name="id"> Id of the operation.</param>
  /// <returns> CancellationToken that belongs to operation.</returns>
  public CancellationToken GetToken(string id) => _operationsInProgress[id].Token;

  /// <summary>
  /// Whether given id registered or not.
  /// </summary>
  /// <param name="id"> Id to check registration.</param>
  /// <returns> Whether given id registered or not.</returns>
  public bool IsExist(string id) => _operationsInProgress.ContainsKey(id);

  public void CancelAllOperations()
  {
    foreach (var operation in _operationsInProgress)
    {
      operation.Value.Cancel();
    }
  }

  /// <summary>
  /// Initialize a token source for cancellable operation,
  /// if one with the given <paramref name="id"/> already exists, it will be canceled first before creating the new one.
  /// </summary>
  /// <param name="id"> Id to register token.</param>
  /// <returns> Initialized cancellation token source.</returns>
  public ICancellationItem GetCancellationItem(string id)
  {
    var cts = new CancellationTokenSource();
    _operationsInProgress[id] = cts;
    return new CancellationItem(this, id);
  }

  /// <summary>
  /// Cancel operation.
  /// </summary>
  /// <param name="id">Id to cancel operation.</param>
  public void CancelOperation(string id)
  {
    if (_operationsInProgress.TryGetValue(id, out CancellationTokenSource? cts))
    {
      cts.Cancel();
    }
  }

  /// <summary>
  /// Whether cancellation requested already or not.
  /// </summary>
  /// <param name="id"> Id to check cancellation requested already or not.</param>
  /// <returns></returns>
  public bool IsCancellationRequested(string id) => _operationsInProgress[id].IsCancellationRequested;
}
