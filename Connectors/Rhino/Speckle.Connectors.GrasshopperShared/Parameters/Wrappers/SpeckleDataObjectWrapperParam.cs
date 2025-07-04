using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleDataObjectParam : GH_Param<SpeckleDataObjectWrapperGoo>, IGH_BakeAwareObject, IGH_PreviewObject
{
  public SpeckleDataObjectParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleDataObjectParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleDataObjectParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleDataObjectParam(GH_ParamAccess access)
    : base(
      "Speckle Data Object",
      "SDO",
      "A Speckle data object with structured properties and display geometries",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("47B930F9-587B-4A88-8CEB-19986E60BA61");
  protected override Bitmap Icon => Resources.speckle_param_object; // TODO: DataObject icon
  public override GH_Exposure Exposure => GH_Exposure.primary;

  bool IGH_BakeAwareObject.IsBakeCapable => !VolatileData.IsEmpty;

  // TODO: CNX-2095
  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, List<Guid> objIds) { }

  // TODO: CNX-2095
  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> objIds) { }

  // TODO: CNX-2094
  public void DrawViewportWires(IGH_PreviewArgs args) => throw new NotImplementedException();

  // TODO: CNX-2094
  public void DrawViewportMeshes(IGH_PreviewArgs args) => throw new NotImplementedException();

  public bool Hidden { get; set; }

  public bool IsPreviewCapable => !VolatileData.IsEmpty;

  // TODO: CNX-2094
  public BoundingBox ClippingBox { get; }
}
