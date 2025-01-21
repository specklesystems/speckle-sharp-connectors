using Speckle.Sdk.Common;

namespace Speckle.Connectors.DUI.Eventing;

public class WeakOrStrongReference(WeakReference? weakReference, object? strongReference)
{
  public static WeakOrStrongReference CreateWeak(object? reference) => new(new WeakReference(reference), null);

  public static WeakOrStrongReference CreateStrong(object? reference) => new(null, reference);

  public bool IsAlive => weakReference?.IsAlive ?? true;

  public object Target
  {
    get
    {
      if (strongReference is not null)
      {
        return strongReference;
      }
      return (weakReference?.Target).NotNull();
    }
  }
}
