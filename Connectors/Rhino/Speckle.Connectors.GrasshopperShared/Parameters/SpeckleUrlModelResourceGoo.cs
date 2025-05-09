using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.HostApp;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleUrlModelResourceGoo : GH_Goo<SpeckleUrlModelResource>
{
  public override IGH_Goo Duplicate() => new SpeckleUrlModelResourceGoo() { Value = Value };

  public override string ToString() => Value.ToString();

  public override bool IsValid => true;
  public override string TypeName => "SpeckleUrlModelResource";
  public override string TypeDescription => "Points to a model/version/object in a Speckle server";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleUrlModelResource resource:
        Value = resource;
        return true;
      default:
        return false;
    }
  }

  public override bool CastTo<TOut>(ref TOut target)
  {
    var type = typeof(TOut);
    var success = false;
    if (type == typeof(SpeckleUrlModelResource))
    {
      target = (TOut)(object)Value;
      success = true;
    }

    return success;
  }
}
