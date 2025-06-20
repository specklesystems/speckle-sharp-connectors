using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

[Guid("8D2E3F4A-1B5C-4E7F-9A8B-3C6D9E2F1A4B")]
public class SpeckleBlockDefinitionPassthrough : GH_Component
{
  public SpeckleBlockDefinitionPassthrough()
    : base(
      "Speckle Block Definition",
      "SBD",
      "Create or modify a Speckle Block Definition",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  public override Guid ComponentGuid => GetType().GUID;

  protected override Bitmap Icon => Resources.speckle_objects_block_def;

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleBlockDefinitionWrapperParam(),
      "Block Definition",
      "BD",
      "Input Block Definition. Speckle Block Definitions and Rhino Instance Definitions are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[0].Optional = true;

    pManager.AddGenericParameter(
      "Objects",
      "O",
      "Objects to include in the Block Definition. Speckle Objects and Block Instances are accepted.",
      GH_ParamAccess.list
    );
    Params.Input[1].Optional = true;

    pManager.AddTextParameter("Name", "N", "Name of the Block Definition", GH_ParamAccess.item);
    Params.Input[2].Optional = true;

    // TODO: what about description, base point parameter, color/Material overrides for the definition itself
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleBlockDefinitionWrapperParam(),
      "Block Definition",
      "BD",
      "Speckle Block Definition",
      GH_ParamAccess.item
    );

    pManager.AddParameter(
      new SpeckleObjectParam(),
      "Objects",
      "O",
      "Objects contained in the Block Definition",
      GH_ParamAccess.list
    );

    pManager.AddTextParameter("Name", "N", "Name of the Block Definition", GH_ParamAccess.item);
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    // keep track of mutation
    bool mutated = false;

    // are we modifying an existing block definition or creating a new one?
    SpeckleBlockDefinitionWrapperGoo? inputBlockDef = null;
    da.GetData(0, ref inputBlockDef);

    SpeckleBlockDefinitionWrapper result;
    if (inputBlockDef != null) // if != null → user has piped in a definition, and we're modifying existing
    {
      result = inputBlockDef.Value.DeepCopy();
    }
    else // if null → we're creating a brand spanking new one
    {
      result = new SpeckleBlockDefinitionWrapper()
      {
        Base = new InstanceDefinitionProxy
        {
          name = "Unnamed Block",
          objects = new List<string>(),
          maxDepth = 0 // represent newly created, top-level objects. actual depth calculation happens in GrasshopperBlockPacker
        },
        Objects = new List<SpeckleObjectWrapper>()
      };
      mutated = true;
    }

    // get whatever objects the user wants inside the block definition
    List<IGH_Goo> inputObjects = new();
    da.GetDataList(1, inputObjects);

    if (inputObjects.Count > 0)
    {
      var processedObjects = new List<SpeckleObjectWrapper>();
      var objectIds = new List<string>();

      foreach (var objGoo in inputObjects)
      {
        SpeckleObjectWrapper? obj = null;

        // Handle SpeckleObjectWrapper
        if (objGoo is SpeckleObjectWrapperGoo speckleObjGoo && speckleObjGoo.Value != null)
        {
          obj = speckleObjGoo.Value.DeepCopy();
        }
        // Handle SpeckleBlockInstanceWrapper
        else if (objGoo is SpeckleBlockInstanceWrapperGoo blockInstGoo && blockInstGoo.Value != null)
        {
          obj = blockInstGoo.Value.DeepCopy();
        }
        // Handle other convertible types
        else if (objGoo != null)
        {
          // Try to convert geometry, etc.
          var tempGoo = new SpeckleObjectWrapperGoo();
          if (tempGoo.CastFrom(objGoo))
          {
            obj = tempGoo.Value;
          }
        }

        if (obj != null)
        {
          obj.ApplicationId ??= Guid.NewGuid().ToString();
          processedObjects.Add(obj);
          objectIds.Add(obj.ApplicationId);
        }
      }

      result.Objects = processedObjects;
      result.InstanceDefinitionProxy.objects = objectIds;
      mutated = true;
    }

    // name for the block
    string? inputName = null;
    da.GetData(2, ref inputName);

    if (inputName != null)
    {
      result.Name = inputName;
      result.InstanceDefinitionProxy.name = inputName;
      mutated = true;
    }

    if (mutated)
    {
      result.ApplicationId = Guid.NewGuid().ToString();
      result.InstanceDefinitionProxy.applicationId = result.ApplicationId;
    }

    // we need a valid name
    if (string.IsNullOrEmpty(result.Name))
    {
      result.Name = "Unnamed Block";
      result.InstanceDefinitionProxy.name = result.Name;
    }

    if (result.Objects.Count == 0 && inputBlockDef == null)
    {
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Warning,
        "Block Definition has no objects. Provide objects to create a valid block definition."
      );
    }

    // set outputs
    da.SetData(0, new SpeckleBlockDefinitionWrapperGoo(result)); // the "finished" block
    da.SetDataList(1, result.Objects.Select(o => new SpeckleObjectWrapperGoo(o))); // objects inside
    da.SetData(2, result.Name); // name of block
  }
}
