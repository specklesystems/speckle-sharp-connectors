using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

[Guid("2F8A9B1C-3D4E-5F6A-7B8C-9D0E1F2A3B4C")]
public class CreateSpeckleBlockInstance : GH_Component
{
  public CreateSpeckleBlockInstance()
    : base(
      "Speckle Block Instance",
      "SBI",
      "Create or modify a Speckle Block Instance",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_objects_object; // TODO: specific icon

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleBlockInstanceParam(),
      "Block Instance",
      "BI",
      "Input Block Instance. Speckle Block Instances are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[0].Optional = true;

    // TODO: Uncomment when block definitions are available
    // pManager.AddParameter(
    //   new SpeckleBlockDefinitionWrapperParam(),
    //   "Definition",
    //   "D",
    //   "Block Definition to instance",
    //   GH_ParamAccess.item
    // );
    // Params.Input[1].Optional = true;

    pManager.AddGenericParameter(
      "Transform",
      "T",
      "Transform for the block instance. Transforms and Planes are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[1].Optional = true; // TODO: Change to [2] when definition input is added

    pManager.AddTextParameter("Name", "N", "Name of the Block Instance", GH_ParamAccess.item);
    Params.Input[2].Optional = true; // TODO: Change to [3] when definition input is added

    pManager.AddGenericParameter(
      "Properties",
      "P",
      "The properties of the Block Instance. Speckle Properties are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[3].Optional = true; // TODO: Change to [4] when definition input is added

    // TODO: Add Color and Material inputs when supported
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleBlockInstanceParam(),
      "Block Instance",
      "BI",
      "Speckle Block Instance",
      GH_ParamAccess.item
    );

    // TODO: Uncomment when block definitions are available
    // pManager.AddParameter(
    //   new SpeckleBlockDefinitionWrapperParam(),
    //   "Definition",
    //   "D",
    //   "Block Definition of the instance",
    //   GH_ParamAccess.item
    // );

    pManager.AddGenericParameter("Transform", "T", "Transform of the Block Instance", GH_ParamAccess.item);

    pManager.AddTextParameter("Name", "N", "Name of the Block Instance", GH_ParamAccess.item);

    pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "P",
      "Properties of the Block Instance",
      GH_ParamAccess.item
    );

    // TODO: Add when supported
    // pManager.AddColourParameter("Color", "C", "Color of the Block Instance", GH_ParamAccess.item);
    // pManager.AddParameter(new SpeckleMaterialParam(), "Material", "M", "Material of the Block Instance", GH_ParamAccess.item);
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    SpeckleBlockInstanceWrapperGoo? inputInstance = null;
    da.GetData(0, ref inputInstance);

    IGH_Goo? inputTransform = null;
    da.GetData(1, ref inputTransform);

    string? inputName = null;
    da.GetData(2, ref inputName);

    IGH_Goo? inputProperties = null;
    da.GetData(3, ref inputProperties);

    // Track mutation
    bool mutated = false;

    // Process the instance
    SpeckleBlockInstanceWrapperGoo result = new();
    if (inputInstance != null)
    {
      if (!result.CastFrom(inputInstance))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Block Instance input is not valid.");
        return;
      }
    }

    // Process transform
    if (inputTransform != null)
    {
      var transformGoo = new SpeckleBlockInstanceWrapperGoo();
      if (transformGoo.CastFrom(inputTransform)) // This will handle Transform and Plane
      {
        result.Value.Transform = transformGoo.Value.Transform;
        mutated = true;
      }
      else
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Warning,
          "Transform input is not valid. Only Transforms and Planes are accepted."
        );
        return;
      }
    }

    // Process name
    if (inputName != null)
    {
      result.Value.Name = inputName;
      mutated = true;
    }

    // Process properties
    if (inputProperties != null)
    {
      SpecklePropertyGroupGoo propGoo = new();
      if (!propGoo.CastFrom(inputProperties))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Properties input is not valid.");
        return;
      }
      result.Value.Properties = propGoo;
      mutated = true;
    }

    // Generate new ApplicationId if mutated
    if (mutated)
    {
      result.Value.ApplicationId = Guid.NewGuid().ToString();
    }

    // Set outputs
    da.SetData(0, result);
    da.SetData(1, result.Value.Transform);
    da.SetData(2, result.Value.Name);
    da.SetData(3, result.Value.Properties);
  }
}
