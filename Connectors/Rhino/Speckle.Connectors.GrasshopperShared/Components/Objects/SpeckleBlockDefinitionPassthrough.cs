using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
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

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddGenericParameter(
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
    IGH_Goo? inputDefinition = null;
    da.GetData(0, ref inputDefinition);

    List<IGH_Goo> inputGeometry = new();
    da.GetDataList(1, inputGeometry);

    string? inputName = null;
    da.GetData(2, ref inputName);

    // keep track of mutation
    bool mutated = false;

    // process the definition
    SpeckleBlockDefinitionWrapperGoo result = new();
    if (inputDefinition != null)
    {
      if (!result.CastFrom(inputDefinition))
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Error,
          $"Definition input is not valid. Only Speckle definitions or Model definitions are accepted."
        );
        return;
      }
    }

    if (inputDefinition == null && inputGeometry.Count == 0)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Pass in a Definition or Objects.");
      return;
    }

    // process geometry
    if (inputGeometry.Count > 0)
    {
      List<SpeckleObjectWrapper> processedObjects = new();

      foreach (IGH_Goo geo in inputGeometry)
      {
        SpeckleObjectWrapper obj;

        // Try casting to SpeckleObjectWrapper first (handles object wrapper, model objects, loose geometry)
        SpeckleObjectWrapperGoo objectGoo = new();
        if (objectGoo.CastFrom(geo))
        {
          obj = objectGoo.Value;
        }
        else
        {
          // Try casting to SpeckleBlockInstanceWrapper (handles instance goo, model instances)
          SpeckleBlockInstanceWrapperGoo instanceGoo = new();
          if (instanceGoo.CastFrom(geo))
          {
            obj = instanceGoo.Value;
          }
          else
          {
            // Neither casting worked
            AddRuntimeMessage(
              GH_RuntimeMessageLevel.Warning,
              $"Object of type {geo.GetType().Name} could not be added to definition"
            );

            continue; // skip this object
          }
        }

        if (obj.ApplicationId == null)
        {
          throw new InvalidOperationException("Object ApplicationId should have been assigned during casting");
        }

        processedObjects.Add(obj);
      }

      result.Value.Objects = processedObjects;
      result.Value.InstanceDefinitionProxy.objects = processedObjects.Select(o => o.ApplicationId!).ToList(); // TODO: this could also be set at the same time as `Objects` on the definition wrapper.
      mutated = true;
    }

    // process name
    if (inputName != null)
    {
      result.Value.Name = inputName;
      mutated = true;
    }

    // Ensure we have a valid name since this is critical for block definitions on receiving in many apps
    if (string.IsNullOrEmpty(result.Value.Name))
    {
      result.Value.Name = "Unnamed Block";
    }

    // process application Id. Use a new appId if mutated, or if this is a new object
    result.Value.ApplicationId = mutated
      ? Guid.NewGuid().ToString()
      : result.Value.ApplicationId ?? Guid.NewGuid().ToString();

    // set outputs
    da.SetData(0, result);
    da.SetDataList(1, result.Value.Objects.Select(o => new SpeckleObjectWrapperGoo(o)));
    da.SetData(2, result.Value.Name);
  }
}
