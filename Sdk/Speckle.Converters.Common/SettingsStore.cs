namespace Speckle.Converters.Common;

public sealed class SettingsStore<T> : ISettingsStore<T>
  where T : class
{
  private sealed class ContextWrapper(ISettingsStore<T> store) : IDisposable
  {
    public void Dispose() =>
      // technically we could be popping something not this but throwing in dispose is bad
      store.Pop();
  }

  public SettingsStore(T state)
  {
    _stack.Push(state);
  }

  private readonly Stack<T> _stack = new();

  public T Current => _stack.Peek();

  public IDisposable Push(T nextContext)
  {
    _stack.Push(nextContext);
    return new ContextWrapper(this);
  }

  void ISettingsStore<T>.Pop() => _stack.Pop();
}
