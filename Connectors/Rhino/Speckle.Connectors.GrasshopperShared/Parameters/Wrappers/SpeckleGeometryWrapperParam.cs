using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleGeometryWrapperParam : GH_Param<SpeckleGeometryWrapperGoo>, IGH_BakeAwareObject, IGH_PreviewObject
{
  public SpeckleGeometryWrapperParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleGeometryWrapperParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleGeometryWrapperParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleGeometryWrapperParam(GH_ParamAccess access)
    : base(
      "Speckle Geometry",
      "SG",
      "Represents a Speckle Geometry",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("22FD5510-D5D3-4101-8727-153FFD329E4F");
  protected override Bitmap Icon => Resources.speckle_param_geometry;
  public override GH_Exposure Exposure => GH_Exposure.secondary;

  public bool IsBakeCapable =>
    // False if no data
    !VolatileData.IsEmpty;

  public void BakeGeometry(RhinoDoc doc, List<Guid> objIds)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleGeometryWrapperGoo goo)
      {
        goo.Value.Bake(doc, objIds);
      }
    }
  }

  /// <summary>
  /// Bakes the object
  /// </summary>
  /// <param name="doc"></param>
  /// <param name="att"></param>
  /// <param name="objIds"></param>
  /// <remarks>
  /// The attributes come from the user dialog after calling bake.
  /// The selected layer from the dialog will only be user if no path is already present on the object.
  /// </remarks>
  public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> objIds)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleGeometryWrapperGoo goo)
      {
        int layerIndex = goo.Value.Path.Count == 0 ? att.LayerIndex : -1;
        bool layerCreated = goo.Value.Path.Count == 0;
        goo.Value.Bake(doc, objIds, layerIndex, layerCreated);
      }
    }
  }

  public bool IsPreviewCapable => !VolatileData.IsEmpty;

  public BoundingBox ClippingBox
  {
    get
    {
      BoundingBox clippingBox = new();

      // Iterate over all data stored in the parameter
      foreach (var item in VolatileData.AllData(true))
      {
        if (item is SpeckleGeometryWrapperGoo goo && goo.Value.GeometryBase is GeometryBase gb)
        {
          var box = gb.GetBoundingBox(false);
          clippingBox.Union(box);
        }
      }
      return clippingBox;
    }
  }
  bool IGH_PreviewObject.Hidden { get; set; }

  public void DrawViewportWires(IGH_PreviewArgs args)
  {
    // todo?
  }

  public void DrawViewportMeshes(IGH_PreviewArgs args)
  {
    var isSelected = args.Document.SelectedObjects().Contains(this) || OwnerSelected();
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleGeometryWrapperGoo goo)
      {
        goo.Value.DrawPreview(args, isSelected);
      }
    }
  }

  private bool OwnerSelected() => Attributes?.Parent?.Selected ?? false;
}
