using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
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
  protected override Bitmap Icon => Resources.speckle_objects_block_inst;

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

    pManager.AddParameter(
      new SpeckleBlockDefinitionWrapperParam(),
      "Definition",
      "D",
      "Block Definition to instance",
      GH_ParamAccess.item
    );
    Params.Input[1].Optional = true;

    pManager.AddGenericParameter(
      "Transform",
      "T",
      "Transform for the block instance. Transforms and Planes are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[2].Optional = true;

    pManager.AddTextParameter("Name", "N", "Name of the Block Instance", GH_ParamAccess.item);
    Params.Input[3].Optional = true;

    pManager.AddGenericParameter(
      "Properties",
      "P",
      "The properties of the Block Instance. Speckle Properties are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[4].Optional = true;

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

    pManager.AddParameter(
      new SpeckleBlockDefinitionWrapperParam(),
      "Definition",
      "D",
      "Block Definition of the instance",
      GH_ParamAccess.item
    );

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

    SpeckleBlockDefinitionWrapperGoo? inputDefinition = null;
    da.GetData(1, ref inputDefinition);

    IGH_Goo? inputTransform = null;
    da.GetData(2, ref inputTransform);

    string? inputName = null;
    da.GetData(3, ref inputName);

    IGH_Goo? inputProperties = null;
    da.GetData(4, ref inputProperties);

    // Create or copy result
    SpeckleBlockInstanceWrapper result;
    bool mutated = false;

    if (inputInstance?.Value != null)
    {
      result = (SpeckleBlockInstanceWrapper)inputInstance.Value.DeepCopy();
    }
    else
    {
      result = SpeckleBlockInstanceWrapper.CreateDefault();
      mutated = true;
    }

    // Process definition
    if (inputDefinition?.Value != null)
    {
      result.Definition = inputDefinition.Value;
      result.InstanceProxy.definitionId = inputDefinition.Value.ApplicationId ?? inputDefinition.Value.Name;
      mutated = true;
    }

    // Process transform
    if (inputTransform != null)
    {
      Transform? extractedTransform = ExtractTransform(inputTransform);
      if (extractedTransform.HasValue)
      {
        result.Transform = extractedTransform.Value;
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
    if (inputName is string validName && !string.IsNullOrWhiteSpace(validName))
    {
      result.Name = inputName;
      mutated = true;
    }

    // Process properties
    if (inputProperties != null)
    {
      SpecklePropertyGroupGoo propGoo = new();
      if (propGoo.CastFrom(inputProperties))
      {
        result.Properties = propGoo;
        mutated = true;
      }
      else
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Warning,
          "Properties input is not valid. Only Speckle Properties are accepted."
        );
        return;
      }
    }

    // Generate new ApplicationId if mutated
    if (mutated)
    {
      result.ApplicationId = Guid.NewGuid().ToString();
      result.InstanceProxy.applicationId = result.ApplicationId;
    }

    // Ensure we have a valid name
    if (string.IsNullOrEmpty(result.Name))
    {
      result.Name = "Block Instance";
    }

    // Set outputs
    da.SetData(0, new SpeckleBlockInstanceWrapperGoo(result));
    da.SetData(1, result.Definition != null ? new SpeckleBlockDefinitionWrapperGoo(result.Definition) : null);
    da.SetData(2, new GH_Transform(result.Transform));
    da.SetData(3, result.Name);
    da.SetData(4, result.Properties);
  }

  private Transform? ExtractTransform(IGH_Goo input) =>
    input switch
    {
      GH_Transform ghTransform => ghTransform.Value,
      GH_Plane ghPlane => Transform.PlaneToPlane(Plane.WorldXY, ghPlane.Value),
      _ => null
    };
}
