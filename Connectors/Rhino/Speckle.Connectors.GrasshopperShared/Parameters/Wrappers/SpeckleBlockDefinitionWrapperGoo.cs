using Grasshopper.Kernel.Types;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// The goo of <see cref="SpeckleBlockDefinitionWrapper"/>.
/// </summary>
/// <remarks>Casting </remarks>
public partial class SpeckleBlockDefinitionWrapperGoo : GH_Goo<SpeckleBlockDefinitionWrapper>
{
  public override bool IsValid => Value?.InstanceDefinitionProxy is not null && Value.ApplicationId is not null;
  public override string TypeName => "Speckle Block Definition";
  public override string TypeDescription => "Represents an instance definition proxy from Speckle";

  public SpeckleBlockDefinitionWrapperGoo(SpeckleBlockDefinitionWrapper value)
  {
    Value = value;
  }

  public SpeckleBlockDefinitionWrapperGoo()
  {
    Value = new()
    {
      Base = new InstanceDefinitionProxy
      {
        name = "Unnamed Block",
        objects = new List<string>(),
        maxDepth = 0, // represent newly created, top-level objects. actual depth calculation happens in GrasshopperBlockPacker
      },

      ApplicationId = Guid.NewGuid().ToString(),
      Name = "Unnamed Block"
    };
  }

  public override IGH_Goo Duplicate() => new SpeckleBlockDefinitionWrapperGoo(Value.DeepCopy());

  public override string ToString() => $"Speckle Block Definition : {m_value.Name}";

  // POC: need to verify deep copies are needed, for memory reasons!!
  // May not be needed on GH_Goo if eg passing from param to param.
  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleBlockDefinitionWrapper sourceWrapper:
        Value = sourceWrapper;
        return true;
      case SpeckleBlockDefinitionWrapperGoo wrapperGoo:
        Value = wrapperGoo.Value;
        return true;
    }

    // Rhino 8 Model Objects
    return CastFromModelObject(source);
  }

#if !RHINO8_OR_GREATER
  private bool CastFromModelObject(object _) => false;

  private bool CastToModelObject<T>(ref T _) => false;
#endif

  public override bool CastTo<T>(ref T target)
  {
    return CastToModelObject(ref target);
  }

  /// <summary>
  /// Creates a deep copy of this block definition wrapper for proper data handling.
  /// Follows the same pattern as other Goo implementations in the codebase.
  /// </summary>
  /// <returns>A new instance with copied data</returns>
  public SpeckleBlockDefinitionWrapper DeepCopy() =>
    new()
    {
      Base = Value.InstanceDefinitionProxy.ShallowCopy(),
      Name = Value.Name,
      Objects = Value.Objects.Select(o => o.DeepCopy()).ToList(),
      ApplicationId = Value.ApplicationId
    };
}
