namespace Speckle.Converters.Common;

public sealed class ConverterSettingsStore<T> : IConverterSettingsStore<T>
  where T : class
{
  private readonly Stack<T> _stack = new();

  public T Current => _stack.Peek();

  public IDisposable Push(T nextContext)
  {
    _stack.Push(nextContext);
    return new ContextWrapper(this);
  }

  void IConverterSettingsStore<T>.Pop() => _stack.Pop();

  private sealed class ContextWrapper(IConverterSettingsStore<T> store) : IDisposable
  {
    public void Dispose() =>
      // technically we could be popping something not this but throwing in dispose is bad
      store.Pop();
  }
}
