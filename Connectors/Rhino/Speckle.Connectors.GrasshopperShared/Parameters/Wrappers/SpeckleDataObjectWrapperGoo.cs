using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Goo wrapper for SpeckleDataObjectWrapper.
/// </summary>
public class SpeckleDataObjectWrapperGoo : GH_Goo<SpeckleDataObjectWrapper>, IGH_PreviewData
{
  /// <summary>
  /// Creates goo with a DataObject wrapper.
  /// </summary>
  public SpeckleDataObjectWrapperGoo(SpeckleDataObjectWrapper value)
  {
    Value = value;
  }

  // TODO: Do we need a parameterless constructor like other wrappers? Will see when we get to casting. Most probably :(

  public override bool IsValid => Value?.DataObject is not null && Value.ApplicationId is not null;
  public override string TypeName => "Speckle Data Object";
  public override string TypeDescription => "Represents a Speckle data object";

  public override IGH_Goo Duplicate() => new SpeckleDataObjectWrapperGoo(Value.DeepCopy());

  public override string ToString() =>
    $"Speckle Data Object : {(string.IsNullOrWhiteSpace(Value.Name) ? Value.Base.speckle_type : Value.Name)}";

  /// <summary>
  /// Handles casting from other types to DataObject wrapper.
  /// </summary>
  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleDataObjectWrapper wrapper:
        Value = wrapper;
        return true;
      case SpeckleDataObjectWrapperGoo wrapperGoo:
        Value = wrapperGoo.Value;
        return true;

      // TODO: CNX-2092: Castings form part of this scope
    }

    return false;
  }

  /// <summary>
  /// Handles casting to other types from DataObject wrapper.
  /// </summary>
  public override bool CastTo<T>(ref T target) =>
    // TODO: CNX-2092: Castings form part of this scope
    false;

  // TODO: CNX-2094
  public void DrawViewportWires(GH_PreviewWireArgs args) => throw new NotImplementedException();

  // TODO: CNX-2094
  public void DrawViewportMeshes(GH_PreviewMeshArgs args) => throw new NotImplementedException();

  // TODO: CNX-2094
  public BoundingBox ClippingBox { get; }
}
