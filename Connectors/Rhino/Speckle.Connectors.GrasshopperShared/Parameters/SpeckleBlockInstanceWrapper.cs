using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Wrapper around a block instance.
/// </summary>
public class SpeckleBlockInstanceWrapper : SpeckleWrapper
{
  public override required Base Base { get; set; }
}

public class SpeckleBlockInstanceWrapperGoo : GH_Goo<SpeckleBlockInstanceWrapper>, IGH_PreviewData, ISpeckleGoo
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $@"Speckle Block Instance Goo [{m_value.Base.speckle_type}]";

  public override bool IsValid => true;
  public override string TypeName => "Speckle block instance wrapper";
  public override string TypeDescription => "A wrapper around speckle grasshopper block instances.";

  public void DrawViewportWires(GH_PreviewWireArgs args) => throw new NotImplementedException();

  public void DrawViewportMeshes(GH_PreviewMeshArgs args) => throw new NotImplementedException();

  public BoundingBox ClippingBox { get; }
}
