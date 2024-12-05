using Grasshopper.Kernel.Types;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Parameters;

public class SpeckleCollectionGoo : GH_Goo<Collection>
{
  // TODO: Massive hack for setup only!!! We need some sort of `ShallowCopy` or a transparent wrapper for Speckle Objects
  // to prevent backwards propagation of changes of the same instance.
  public override IGH_Goo Duplicate() => new SpeckleCollectionGoo { Value = m_value };

  public override string ToString() => $"Speckle Collection [{m_value.name}]";

  public override bool IsValid => true;
  public override string TypeName => "SpeckleCollection";
  public override string TypeDescription => "A Speckle Collection";

  public override bool CastFrom(object source)
  {
    Collection? obj = null;
    switch (source)
    {
      case Collection speckleCollection:
        obj = speckleCollection;
        break;
      case SpeckleObjectGoo speckleObjectGoo:
        if (speckleObjectGoo.Value is Collection collection)
        {
          obj = collection;
        }
        break;
      case SpeckleCollectionGoo speckleCollectionGoo:
        obj = speckleCollectionGoo.Value;
        break;
      case GH_Goo<ISpeckleObject> speckleObjectGoo:
        if (speckleObjectGoo.Value is Collection collection2)
        {
          obj = collection2;
        }
        break;
      default:
        obj = null;
        break;
    }

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
    else if (type == typeof(SpeckleCollectionGoo))
    {
      target = (TOut)(object)new SpeckleObjectGoo { Value = Value };
      success = true;
    }
    else if (type == typeof(Collection))
    {
      target = (TOut)(object)Value;
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
