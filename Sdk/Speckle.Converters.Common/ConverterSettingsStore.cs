namespace Speckle.Converters.Common;

public sealed class ConverterSettingsStore<T> : IConverterSettingsStore<T>
  where T : IConverterSettings
{
  private readonly Stack<T> _stack = new();

  public T Current => _stack.Peek();

  public IDisposable Push(Func<T> nextContext)
  {
    _stack.Push(nextContext());
    return new ContextWrapper(this);
  }

  private sealed class ContextWrapper(ConverterSettingsStore<T> store) : IDisposable
  {
    public void Dispose() =>
      // technically we could be popping something not this but throwing in dispose is bad
      store._stack.Pop();
  }
}
