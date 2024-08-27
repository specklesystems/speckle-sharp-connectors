namespace Speckle.Converters.Common;

public class ContextWrapper<T> : IDisposable
{
  private IConversionContextStore<T>? _store;

  public T? Context { get; private set; }

  public ContextWrapper(IConversionContextStore<T> store)
  {
    _store = store;
    Context = _store.Current;
  }

  protected virtual void Dispose(bool disposing)
  {
    if (disposing && _store != null)
    {
      // technically we could be popping something not this but throwing in dispose is bad
      _store.Pop();
      _store = null;
      Context = default;
    }
  }

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }
}
