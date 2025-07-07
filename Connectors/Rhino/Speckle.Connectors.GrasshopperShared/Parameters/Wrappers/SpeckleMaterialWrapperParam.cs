using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleMaterialParam : GH_Param<SpeckleMaterialWrapperGoo>, IGH_BakeAwareObject
{
  private const string NICKNAME = "Speckle Material";

  public SpeckleMaterialParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleMaterialParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleMaterialParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleMaterialParam(GH_ParamAccess access)
    : base(
      NICKNAME,
      "SM",
      "Represents a Speckle material",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("1A08CF79-2072-4B14-9430-E4465FF0C0FE");
  protected override Bitmap Icon => Resources.speckle_param_material;
  public override GH_Exposure Exposure => GH_Exposure.tertiary;

  bool IGH_BakeAwareObject.IsBakeCapable => // False if no data
    !VolatileData.IsEmpty;

  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, List<Guid> obj_ids)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleMaterialWrapperGoo goo)
      {
        // get the param nickname if it is a custom name.
        // this is used to override the name of the material.
        // the nickname should also be used in case of an empty name on the rhino material
        string? name =
          NickName != NICKNAME
            ? NickName
            : string.IsNullOrEmpty(goo.Value.Name)
              ? NickName
              : null;

        int bakeIndex = goo.Value.Bake(doc, name);

        if (bakeIndex == -1)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to add material {name} to document.");
        }
      }
    }
  }

  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleMaterialWrapperGoo goo)
      {
        // get the param nickname if it is a custom name.
        // this is used to override the name of the material.
        // the nickname should also be used in case of an empty name on the rhino material
        string? name =
          NickName != NICKNAME
            ? NickName
            : string.IsNullOrEmpty(goo.Value.Name)
              ? NickName
              : null;

        int bakeIndex = goo.Value.Bake(doc, name);
        if (bakeIndex == -1)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to add material {name} to document.");
        }

        obj_ids.Add(doc.Materials[bakeIndex].Id);
      }
    }
  }
}
