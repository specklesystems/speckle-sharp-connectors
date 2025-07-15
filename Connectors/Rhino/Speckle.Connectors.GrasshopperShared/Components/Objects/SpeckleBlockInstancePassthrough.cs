using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

[Guid("2F8A9B1C-3D4E-5F6A-7B8C-9D0E1F2A3B4C")]
public class SpeckleBlockInstancePassthrough : GH_Component
{
  public SpeckleBlockInstancePassthrough()
    : base(
      "Speckle Block Instance",
      "SBI",
      "Create or modify a Speckle Block Instance",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_objects_block_inst;
  public override GH_Exposure Exposure => GH_Exposure.tertiary;

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    int instanceIndex = pManager.AddParameter(
      new SpeckleBlockInstanceParam(),
      "Block Instance",
      "BI",
      "Input Block Instance. Speckle instances and Grasshopper instances are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[instanceIndex].Optional = true;

    int definitionIndex = pManager.AddParameter(
      new SpeckleBlockDefinitionWrapperParam(),
      "Definition",
      "D",
      "Block Instance Definition. Speckle definitions and Grasshopper definitions are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[definitionIndex].Optional = true;

    int transformIndex = pManager.AddGenericParameter(
      "Transform",
      "T",
      "Transform of the Speckle instance. Transforms and Planes are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[transformIndex].Optional = true;

    int nameIndex = pManager.AddTextParameter("Name", "N", "Name of the Speckle Instance", GH_ParamAccess.item);
    Params.Input[nameIndex].Optional = true;

    int propIndex = pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "P",
      "The properties of the Speckle Instance. Speckle Properties and User Content are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[propIndex].Optional = true;

    int colorIndex = pManager.AddColourParameter(
      "Color",
      "c",
      "The color of the Speckle Instance",
      GH_ParamAccess.item
    );
    Params.Input[colorIndex].Optional = true;

    int matIndex = pManager.AddParameter(
      new SpeckleMaterialParam(),
      "Material",
      "m",
      "The material of the Speckle Instance. Display Materials, Model Materials, and Speckle Materials are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[matIndex].Optional = true;
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

    pManager.AddColourParameter("Color", "c", "The color of the Speckle Object", GH_ParamAccess.item);

    pManager.AddParameter(
      new SpeckleMaterialParam(),
      "Material",
      "M",
      "The material of the Block Instance.",
      GH_ParamAccess.item
    );
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    SpeckleBlockInstanceWrapperGoo? inputInstance = null;
    da.GetData(0, ref inputInstance);

    SpeckleBlockDefinitionWrapperGoo? inputDefinition = null;
    da.GetData(1, ref inputDefinition);

    if (inputInstance == null && inputDefinition == null)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Pass in an Instance or Definition.");
      return;
    }

    IGH_Goo? inputTransform = null;
    da.GetData(2, ref inputTransform);

    string? inputName = null;
    da.GetData(3, ref inputName);

    SpecklePropertyGroupGoo? inputProperties = null;
    da.GetData(4, ref inputProperties);

    Color? inputColor = null;
    da.GetData(5, ref inputColor);

    SpeckleMaterialWrapperGoo? inputMaterial = null;
    da.GetData(6, ref inputMaterial);

    // process the instance
    // deep copy so we don't mutate the object
    SpeckleBlockInstanceWrapperGoo result =
      inputInstance != null ? new((SpeckleBlockInstanceWrapper)inputInstance.Value.DeepCopy()) : new();

    // process definition
    if (inputDefinition != null)
    {
      result.Value.Definition = inputDefinition.Value;
    }

    // Process transform
    if (inputTransform != null)
    {
      Transform? extractedTransform = ExtractTransform(inputTransform);
      if (extractedTransform.HasValue)
      {
        result.Value.Transform = extractedTransform.Value;
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
    }

    // Process properties
    if (inputProperties != null)
    {
      result.Value.Properties = inputProperties;
    }

    // process color (no mutation)
    if (inputColor != null)
    {
      result.Value.Color = inputColor;
    }

    // process  material (no mutation)
    if (inputMaterial != null)
    {
      result.Value.Material = inputMaterial.Value;
    }

    // no need to process application Id.
    // New definitions should have a new appID generated in the new() constructor, and we want to preserve old appID otherwise for changetracking.

    // Set outputs
    da.SetData(0, result);
    da.SetData(1, result.Value.Definition);
    da.SetData(2, new GH_Transform(result.Value.Transform));
    da.SetData(3, result.Value.Name);
    da.SetData(4, result.Value.Properties);
    da.SetData(5, result.Value.Color);
    da.SetData(6, result.Value.Material);
  }

  private Transform? ExtractTransform(IGH_Goo input) =>
    input switch
    {
      GH_Transform ghTransform => ghTransform.Value,
      GH_Plane ghPlane => Transform.PlaneToPlane(Plane.WorldXY, ghPlane.Value),
      _ => null
    };
}
