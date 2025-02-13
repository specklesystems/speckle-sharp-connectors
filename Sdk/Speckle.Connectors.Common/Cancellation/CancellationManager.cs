using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Common.Cancellation;

public interface ICancellationItem : IDisposable
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
    public void Dispose() => manager.DisposeOperation(id);

    public CancellationToken Token => manager.GetToken(id);
  }

  /// <summary>
  /// Dictionary to relate <see cref="CancellationTokenSource"/> with registered id.
  /// </summary>
  private readonly Dictionary<string, CancellationTokenSource> _operationsInProgress = new();

  public int NumberOfOperations => _operationsInProgress.Count;

  //if we can't find it then it must be cancelled
  private CancellationToken GetToken(string id) => _operationsInProgress.TryGetValue(id, out var source) ? source.Token : new CancellationToken(true);

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
    DisposeOperation(id);

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

  private void DisposeOperation(string id)
  {
    if (_operationsInProgress.TryGetValue(id, out CancellationTokenSource? cts))
    {
      cts.Cancel();
      cts.Dispose();
      _operationsInProgress.Remove(id);
    }
  }

  /// <summary>
  /// Whether cancellation requested already or not.
  /// </summary>
  /// <param name="id"> Id to check cancellation requested already or not.</param>
  /// <returns></returns>
  public bool IsCancellationRequested(string id) => GetToken(id).IsCancellationRequested;
}
