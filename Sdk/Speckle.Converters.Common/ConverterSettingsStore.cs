namespace Speckle.Converters.Common;

public sealed class ConverterSettingsStore<T> : IConverterSettingsStore<T>
  where T : class
{
  private readonly Stack<T> _stack = new();

  public T Current => _stack.Peek();

  public IDisposable Push(Func<T, T> nextContext)
  {
    _stack.Push(nextContext(Current));
    return new ContextWrapper(this);
  }

  public void Initialize(T context)
  {
    if (_stack.Count != 0)
    {
      throw new ArgumentException("Already initialized");
    }
    _stack.Push(context);
  }

  private sealed class ContextWrapper(ConverterSettingsStore<T> store) : IDisposable
  {
    public void Dispose() =>
      // technically we could be popping something not this but throwing in dispose is bad
      store._stack.Pop();
  }
}
