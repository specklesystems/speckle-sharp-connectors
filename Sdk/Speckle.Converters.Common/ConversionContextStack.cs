using System.Diagnostics.CodeAnalysis;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Common;

// POC: Suppressed naming warning for now, but we should evaluate if we should follow this or disable it.
[SuppressMessage(
  "Naming",
  "CA1711:Identifiers should not have incorrect suffix",
  Justification = "Name ends in Stack but it is in fact a Stack, just not inheriting from `System.Collections.Stack`"
)]
public class ConversionContextStore<T> : IConversionContextStore<T>
  where T : IConversionContext<T>
{
  protected ConversionContextStore(T state)
  {
    _stack.Push(state);
  }

  private readonly Stack<T> _stack = new();

  public T Current => _stack.Peek();

  public IDisposable Push()
  {
    _stack.Push(Current.Duplicate());
    return new ContextWrapper<T>(this);
  }

  public void Pop() => _stack.Pop();
}

public interface IConversionContextStore<T>
  where T : IConversionContext<T>
{
  T Current { get; }
  System.IDisposable Push();
  void Pop();
}
