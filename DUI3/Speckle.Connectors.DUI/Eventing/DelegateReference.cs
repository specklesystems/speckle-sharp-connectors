using System.Reflection;

namespace Speckle.Connectors.DUI.Eventing;

public interface IDelegateReference
{
  /// <summary>
  /// Gets the referenced <see cref="Delegate" /> object.
  /// </summary>
  /// <value>A <see cref="Delegate"/> instance if the target is valid; otherwise <see langword="null"/>.</value>
  Delegate? Target { get; }
}

public class DelegateReference : IDelegateReference
{
  private readonly WeakReference _weakReference;
  private readonly MethodInfo _method;
  private readonly Type _delegateType;

  public DelegateReference(Delegate @delegate)
  {
    if (@delegate == null)
    {
      throw new ArgumentNullException(nameof(@delegate));
    }

      _weakReference = new WeakReference(@delegate.Target);
      _method = @delegate.GetMethodInfo();
      _delegateType = @delegate.GetType();
  }

  /// <summary>
  /// Gets the <see cref="Delegate" /> (the target) referenced by the current <see cref="DelegateReference"/> object.
  /// </summary>
  /// <value><see langword="null"/> if the object referenced by the current <see cref="DelegateReference"/> object has been garbage collected; otherwise, a reference to the <see cref="Delegate"/> referenced by the current <see cref="DelegateReference"/> object.</value>
  public Delegate? Target => TryGetDelegate();

  /// <summary>
  /// Checks if the <see cref="Delegate" /> (the target) referenced by the current <see cref="DelegateReference"/> object are equal to another <see cref="Delegate" />.
  /// This is equivalent with comparing <see cref="Target"/> with <paramref name="delegate"/>, only more efficient.
  /// </summary>
  /// <param name="delegate">The other delegate to compare with.</param>
  /// <returns>True if the target referenced by the current object are equal to <paramref name="delegate"/>.</returns>
  public bool TargetEquals(Delegate? @delegate)
  {
    if (@delegate == null)
    {
      return !_method.IsStatic && !_weakReference.IsAlive;
    }
    return _weakReference.Target == @delegate.Target && Equals(_method, @delegate.GetMethodInfo());
  }

  private Delegate? TryGetDelegate()
  {
    if (_method.IsStatic)
    {
      return _method.CreateDelegate(_delegateType, null);
    }
    object target = _weakReference.Target;
    if (target != null)
    {
      return _method.CreateDelegate(_delegateType, target);
    }
    return null;
  }
}
