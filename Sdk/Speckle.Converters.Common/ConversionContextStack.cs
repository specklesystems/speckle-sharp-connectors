namespace Speckle.Converters.Common;

public sealed class ConversionContextStore<T> : IConversionContextStore<T>
  where T : class
{
  public ConversionContextStore(T state)
  {
    _stack.Push(state);
  }

  private readonly Stack<T> _stack = new();

  public T Current => _stack.Peek();

  public IDisposable Push(T nextContext)
  {
    _stack.Push(nextContext);
    return new ContextWrapper<T>(this);
  }

  void IConversionContextStore<T>.Pop() => _stack.Pop();
}

public interface IConversionContextStore<T>
{
  T Current { get; }
  System.IDisposable Push(T nextContext);
  internal void Pop();
}
