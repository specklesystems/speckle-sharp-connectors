using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleBlockInstanceParam
  : GH_Param<SpeckleBlockInstanceWrapperGoo>,
    IGH_BakeAwareObject,
    IGH_PreviewObject
{
  public SpeckleBlockInstanceParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleBlockInstanceParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleBlockInstanceParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleBlockInstanceParam(GH_ParamAccess access)
    : base(
      "Speckle Block Instance",
      "SBI",
      "Represents a Speckle block instance",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("938CCD6E-B202-4A0C-9D68-ABD7683B0EDE");
  protected override Bitmap Icon => Resources.speckle_param_block_instance;

  public override GH_Exposure Exposure => GH_Exposure.secondary;

  public override void RegisterRemoteIDs(GH_GuidTable idList)
  {
    // Register both the block definition and instance GUIDs so Grasshopper
    // auto-expires when either the definition or instance changes in Rhino
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleBlockInstanceWrapperGoo goo)
      {
        // Track the referenced block definition
        if (
          goo.Value?.Definition?.ApplicationId != null
          && Guid.TryParse(goo.Value.Definition.ApplicationId, out Guid defId)
        )
        {
          idList.Add(defId, this);
        }

        // Track the instance itself if it references a Rhino object
        if (goo.Value?.ApplicationId != null && Guid.TryParse(goo.Value.ApplicationId, out Guid instId))
        {
          idList.Add(instId, this);
        }
      }
    }
  }

  public bool IsBakeCapable => !VolatileData.IsEmpty;
  public bool IsPreviewCapable => !VolatileData.IsEmpty;

  public void BakeGeometry(RhinoDoc doc, List<Guid> objIds)
  {
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleBlockInstanceWrapperGoo goo)
      {
        goo.Value.Bake(doc, objIds);
      }
    }
  }

  public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> objIds) => BakeGeometry(doc, objIds); // Instances manage their own attributes

  public void DrawViewportWires(IGH_PreviewArgs args)
  {
    // TODO ?
  }

  public void DrawViewportMeshes(IGH_PreviewArgs args)
  {
    var isSelected = args.Document.SelectedObjects().Contains(this) || OwnerSelected();
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleBlockInstanceWrapperGoo goo)
      {
        goo.Value.DrawPreview(args, isSelected);
      }
    }
  }

  private bool OwnerSelected()
  {
    return Attributes?.Parent?.Selected ?? false;
  }

  public bool Hidden { get; set; }
  public BoundingBox ClippingBox
  {
    get
    {
      var clippingBox = new BoundingBox();
      foreach (var item in VolatileData.AllData(true))
      {
        if (item is SpeckleBlockInstanceWrapperGoo goo)
        {
          clippingBox.Union(goo.ClippingBox);
        }
      }
      return clippingBox;
    }
  }
}
