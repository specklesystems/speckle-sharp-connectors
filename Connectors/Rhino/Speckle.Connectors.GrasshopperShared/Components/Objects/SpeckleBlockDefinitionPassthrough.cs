using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

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
  public override GH_Exposure Exposure => GH_Exposure.tertiary;

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleBlockDefinitionWrapperParam(),
      "Block Definition",
      "BD",
      "Input Block Definition. Speckle definitions and Model definitions are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[0].Optional = true;

    pManager.AddGenericParameter(
      "Objects",
      "O",
      "Objects to include in the Block Definition. Speckle objects and instances or Model objects and instances are accepted.",
      GH_ParamAccess.list
    );
    Params.Input[1].Optional = true;

    pManager.AddTextParameter("Name", "N", "Name of the Speckle Definition", GH_ParamAccess.item);
    Params.Input[2].Optional = true;
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

    pManager.AddGenericParameter("Objects", "O", "Objects contained in the Block Definition", GH_ParamAccess.list);

    pManager.AddTextParameter("Name", "N", "Name of the Block Definition", GH_ParamAccess.item);
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    SpeckleBlockDefinitionWrapperGoo? inputDefinition = null;
    da.GetData(0, ref inputDefinition);

    List<IGH_Goo> inputObjects = new();
    da.GetDataList(1, inputObjects);

    if (inputDefinition == null && inputObjects.Count == 0)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Pass in a Definition or Objects.");
      return;
    }

    string? inputName = null;
    da.GetData(2, ref inputName);

    if (inputDefinition == null && string.IsNullOrWhiteSpace(inputName))
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Pass in a Name for the definition.");
      return;
    }

    // keep track of mutation
    bool mutated = false;

    // process the definition
    // deep copy so we don't mutate the object
    SpeckleBlockDefinitionWrapperGoo result = inputDefinition != null ? new(inputDefinition.Value.DeepCopy()) : new();

    // process geometry
    if (inputObjects.Count > 0)
    {
      List<SpeckleGeometryWrapper> processedObjects = new();
      foreach (IGH_Goo goo in inputObjects)
      {
        if (goo.ToSpeckleGeometryWrapper() is SpeckleGeometryWrapper gooWrapper)
        {
          processedObjects.Add(gooWrapper);
        }
        else
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Unsupported type {goo.TypeName} not added to definition");
        }
      }

      result.Value.Objects = processedObjects;
      result.Value.InstanceDefinitionProxy.objects = processedObjects.Select(o => o.ApplicationId!).ToList(); // TODO: this could also be set at the same time as `Objects` on the definition wrapper.
      mutated = true;
    }

    // process name
    if (inputName != null)
    {
      if (string.IsNullOrWhiteSpace(inputName))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Pass in a non-empty name for the definition.");
        return;
      }

      result.Value.Name = inputName;
      mutated = true;
    }

    // process application Id. Use a new appId if mutated, or if this is a new object
    result.Value.ApplicationId = mutated
      ? Guid.NewGuid().ToString()
      : result.Value.ApplicationId ?? Guid.NewGuid().ToString();

    // set outputs
    da.SetData(0, result);
    da.SetDataList(1, result.Value.Objects.Select(o => o.CreateGoo()));
    da.SetData(2, result.Value.Name);
  }
}
