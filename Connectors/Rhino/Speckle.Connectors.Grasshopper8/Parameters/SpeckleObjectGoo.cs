using Grasshopper.Kernel.Types;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Parameters;

public class SpeckleObjectGoo : GH_Goo<Base>
{
  // TODO: Massive hack for setup only!!! We need some sort of `ShallowCopy` or a transparent wrapper for Speckle Objects
  // to prevent backwards propagation of changes of the same instance.
  public override IGH_Goo Duplicate() => new SpeckleObjectGoo { Value = m_value };

  public override string ToString() => $"Speckle Object [{m_value.GetType().Name}]";

  public override bool IsValid => true;
  public override string TypeName => "SpeckleObject";
  public override string TypeDescription => "A Speckle Object";

  public override bool CastFrom(object source)
  {
    Base? obj = source switch
    {
      Base speckleObject => speckleObject,
      SpeckleObjectGoo speckleObjectGoo => speckleObjectGoo.Value,
      SpeckleCollectionGoo speckleCollectionGoo => speckleCollectionGoo.Value,
      GH_Goo<Base> speckleObjectGoo => speckleObjectGoo.Value,
      _ => null
    };

    if (obj is null)
    {
      return false;
    }

    Value = obj;
    return true;
  }

  public override bool CastTo<TOut>(ref TOut target)
  {
    var type = typeof(TOut);
    var success = false;
    if (type == typeof(SpeckleObjectGoo))
    {
      target = (TOut)(object)new SpeckleObjectGoo { Value = Value };
      success = true;
    }
    else if (type == typeof(SpeckleCollectionGoo) && Value is Collection collection)
    {
      target = (TOut)(object)new SpeckleCollectionGoo { Value = collection };
      success = true;
    }
    else if (type == typeof(Base))
    {
      target = (TOut)(object)Value;
      success = true;
    }
    return success;
  }
}
