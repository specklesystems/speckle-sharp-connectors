using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleBlockDefinitionWrapperParam
  : GH_Param<SpeckleBlockDefinitionWrapperGoo>,
    IGH_BakeAwareObject,
    IGH_PreviewObject
{
  public SpeckleBlockDefinitionWrapperParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleBlockDefinitionWrapperParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleBlockDefinitionWrapperParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleBlockDefinitionWrapperParam(GH_ParamAccess access)
    : base(
      "Speckle Block Definition",
      "SBD",
      "Returns a Speckle Block definition.",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("C71BE6AD-E27B-4E7F-87DA-569D4DEE77BE");

  protected override Bitmap Icon => Resources.speckle_param_block_def;

  public override GH_Exposure Exposure => GH_Exposure.secondary;

  public override void RegisterRemoteIDs(GH_GuidTable idList)
  {
    // Register Rhino InstanceDefinition GUIDs so Grasshopper can track when
    // block definitions change in the Rhino document and auto-expire this parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (
        item is SpeckleBlockDefinitionWrapperGoo goo
        && goo.Value?.ApplicationId != null
        && Guid.TryParse(goo.Value.ApplicationId, out Guid id)
      )
      {
        idList.Add(id, this);
      }
    }
  }

  public bool IsBakeCapable => !VolatileData.IsEmpty;
  public bool IsPreviewCapable => !VolatileData.IsEmpty;

  public void BakeGeometry(RhinoDoc doc, List<Guid> objIds) => BakeAllItems(doc, objIds);

  public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> objIds) =>
    // we "ignore" the ObjectAttributes parameter because definitions manage their own internal object attributes
    // atts aren't on a definition level, but either on an instance level and/or objects within a definition (right?)
    BakeAllItems(doc, objIds);

  private void BakeAllItems(RhinoDoc doc, List<Guid> objIds)
  {
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleBlockDefinitionWrapperGoo goo)
      {
        var (index, wasUpdated) = goo.Value.Bake(doc, objIds);

        if (index != -1)
        {
          string message = wasUpdated
            ? $"Updated existing block definition: '{goo.Value.Name}'"
            : $"Created new block definition: '{goo.Value.Name}'";
          AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
        }
        else
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to bake block definition: '{goo.Value.Name}'");
        }
      }
    }
  }

  public void DrawViewportWires(IGH_PreviewArgs args)
  {
    // TODO?
  }

  public void DrawViewportMeshes(IGH_PreviewArgs args)
  {
    var isSelected = args.Document.SelectedObjects().Contains(this) || OwnerSelected();
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleBlockDefinitionWrapperGoo goo)
      {
        goo.Value.DrawPreview(args, isSelected);
      }
    }
  }

  private bool OwnerSelected() => Attributes?.Parent?.Selected ?? false;

  public bool Hidden { get; set; }

  public BoundingBox ClippingBox
  {
    get
    {
      BoundingBox clippingBox = new();
      foreach (var item in VolatileData.AllData(true))
      {
        if (item is SpeckleBlockDefinitionWrapperGoo goo && goo.Value?.Objects != null)
        {
          foreach (var obj in goo.Value.Objects)
          {
            if (obj.GeometryBase != null)
            {
              clippingBox.Union(obj.GeometryBase.GetBoundingBox(false));
            }
          }
        }
      }
      return clippingBox;
    }
  }
}
