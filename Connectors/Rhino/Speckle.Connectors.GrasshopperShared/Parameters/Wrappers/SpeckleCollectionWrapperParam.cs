using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleCollectionParam : GH_Param<SpeckleCollectionWrapperGoo>, IGH_BakeAwareObject, IGH_PreviewObject
{
  public SpeckleCollectionParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleCollectionParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleCollectionParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleCollectionParam(GH_ParamAccess access)
    : base(
      "Speckle Collection",
      "SCO",
      "A Speckle collection, corresponding to layers in Rhino",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("6E871D5B-B221-4992-882A-EFE6796F3010");
  protected override Bitmap Icon => Resources.speckle_param_collection;
  public override GH_Exposure Exposure => GH_Exposure.primary;

  bool IGH_BakeAwareObject.IsBakeCapable => // False if no data
    !VolatileData.IsEmpty;

  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, List<Guid> objIds)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleCollectionWrapperGoo goo)
      {
        goo.Value.Bake(doc, objIds, true);
      }
    }
  }

  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> objIds)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleCollectionWrapperGoo goo)
      {
        goo.Value.Bake(doc, objIds, true);
      }
    }
  }

  private BoundingBox _clippingBox;
  public BoundingBox ClippingBox => _clippingBox;

  bool IGH_PreviewObject.Hidden { get; set; }

  public bool IsPreviewCapable => !VolatileData.IsEmpty;

  private readonly List<SpeckleGeometryWrapper> _previewObjects = new();

  public void DrawViewportMeshes(IGH_PreviewArgs args)
  {
    if (_previewObjects.Count == 0)
    {
      return;
    }

    var isSelected = args.Document.SelectedObjects().Contains(this) || OwnerSelected();
    foreach (var elem in _previewObjects)
    {
      elem.DrawPreview(args, isSelected);
    }
  }

  private bool OwnerSelected()
  {
    return Attributes?.Parent?.Selected ?? false;
  }

  public void DrawViewportWires(IGH_PreviewArgs args)
  {
    // todo?
  }

  // Called when volatile data has been collected.
  // post-process or analyze the volatile data here.
  // this is where we will recompute and store the objects for preview
  protected override void OnVolatileDataCollected()
  {
    base.OnVolatileDataCollected();
    _previewObjects.Clear();
    _clippingBox = new();
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleCollectionWrapperGoo goo)
      {
        FlattenForPreview(goo.Value);
      }
    }
  }

  private void FlattenForPreview(SpeckleCollectionWrapper collWrapper)
  {
    foreach (var element in collWrapper.Elements)
    {
      if (element is SpeckleCollectionWrapper subCollWrapper)
      {
        FlattenForPreview(subCollWrapper);
      }

      if (element is SpeckleGeometryWrapper objWrapper)
      {
        _previewObjects.Add(objWrapper);
        var box = objWrapper.GeometryBase is null ? new() : objWrapper.GeometryBase.GetBoundingBox(false);
        _clippingBox.Union(box);
      }
    }
  }
}
