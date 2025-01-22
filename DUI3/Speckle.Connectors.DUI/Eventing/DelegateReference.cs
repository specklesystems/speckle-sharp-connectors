﻿using System.Reflection;
using System.Runtime.CompilerServices;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.DUI.Eventing;

public class DelegateReference
{
  private readonly WeakReference<object>? _weakReference;
  private readonly MethodInfo _method;
  private readonly Type? _delegateType;

  public DelegateReference(Delegate @delegate, EventFeatures features)
  {
    var target = @delegate.Target;
    _method = @delegate.Method;
    if (target != null)
    {
      //anonymous methods are always strong....should we do this? - doing a brief search says yes
      if (
        features.HasFlag(EventFeatures.ForceStrongReference)
        || Attribute.IsDefined(_method.DeclaringType.NotNull(), typeof(CompilerGeneratedAttribute))
      )
      {
        throw new EventSubscriptionException("Cannot subscribe to a delegate that was generated by the compiler.");
      }

      _weakReference = new WeakReference<object>(target);

      var messageType = @delegate.Method.GetParameters()[0].ParameterType;
      if (features.HasFlag(EventFeatures.IsAsync))
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

  public async Task<bool> Invoke(object message)
  {
    if (_weakReference == null || !_weakReference.TryGetTarget(out object target))
    {
      return false;
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
