using System.Reflection;
using System.Runtime.CompilerServices;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.DUI.Eventing;

public class DelegateReference
{
  private readonly WeakOrStrongReference? _weakReference;
  private readonly MethodInfo _method;
  private readonly Type? _delegateType;

  public DelegateReference(Delegate @delegate, bool isAsync)
  {
    var target = @delegate.Target;
    _method = @delegate.Method;
    if (target != null)
    {
      if (Attribute.IsDefined(_method.DeclaringType.NotNull(), typeof(CompilerGeneratedAttribute)))
      {
        _weakReference = WeakOrStrongReference.CreateStrong(target);
      }
      else
      {
        _weakReference = WeakOrStrongReference.CreateWeak(target);
      }

      var messageType = @delegate.Method.GetParameters()[0].ParameterType;
      if (isAsync)
      {
        _delegateType = typeof(Func<,>).MakeGenericType(messageType, typeof(Task));
      }
      else
      {
        _delegateType = typeof(Action<>).MakeGenericType(messageType);
      }
    }
    else
    {
      _weakReference = null;
    }
  }

  public bool IsAlive => _weakReference == null || _weakReference.IsAlive;

  public async Task<bool> Invoke(object message)
  {
    if (!IsAlive)
    {
      return false;
    }

    object? target = null;
    if (_weakReference != null)
    {
      target = _weakReference.Target;
    }
    var method = Delegate.CreateDelegate(_delegateType.NotNull(), target, _method);

    var task = method.DynamicInvoke(message) as Task;

    if (task is not null)
    {
      await task;
    }

    return true;
  }
}
