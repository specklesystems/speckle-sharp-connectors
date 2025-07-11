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
  protected override Bitmap Icon => Resources.speckle_param_dataobject;
  public override GH_Exposure Exposure => GH_Exposure.secondary;

  bool IGH_BakeAwareObject.IsBakeCapable => !VolatileData.IsEmpty;

  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, List<Guid> objIds)
  {
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleDataObjectWrapperGoo goo)
      {
        goo.Value.Bake(doc, objIds);
      }
    }
  }

  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> objIds)
  {
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleDataObjectWrapperGoo goo)
      {
        int layerIndex = goo.Value.Path.Count == 0 ? att.LayerIndex : -1;
        bool layerCreated = goo.Value.Path.Count == 0;
        goo.Value.Bake(doc, objIds, layerIndex, layerCreated);
      }
    }
  }

  /// <summary>
  /// Draws viewport wires for all data objects in this parameter.
  /// </summary>
  public void DrawViewportWires(IGH_PreviewArgs args)
  {
    // Following the pattern - most wire drawing is handled in DrawViewportMeshes
    // Keep this minimal like other parameter types
  }

  /// <summary>
  /// Draws viewport meshes for all data objects in this parameter.
  /// </summary>
  public void DrawViewportMeshes(IGH_PreviewArgs args)
  {
    var isSelected = args.Document.SelectedObjects().Contains(this) || OwnerSelected();

    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleDataObjectWrapperGoo goo)
      {
        goo.Value.DrawPreview(args, isSelected);
      }
    }
  }

  public bool Hidden { get; set; }

  public bool IsPreviewCapable => !VolatileData.IsEmpty;

  /// <summary>
  /// Calculates the clipping box for all data objects in this parameter.
  /// </summary>
  public BoundingBox ClippingBox
  {
    get
    {
      var clippingBox = new BoundingBox();

      foreach (var item in VolatileData.AllData(true))
      {
        if (item is SpeckleDataObjectWrapperGoo goo)
        {
          clippingBox.Union(goo.ClippingBox);
        }
      }

      return clippingBox;
    }
  }

  private bool OwnerSelected() => Attributes?.Parent?.Selected ?? false;
}
