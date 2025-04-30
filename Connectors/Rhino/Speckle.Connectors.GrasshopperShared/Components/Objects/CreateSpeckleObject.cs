using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

[Guid("F9418610-ACAE-4417-B010-19EBEA6A121F")]
public class CreateSpeckleObject : GH_Component
{
  public CreateSpeckleObject()
    : base(
      "Create Speckle Object",
      "CSO",
      "Create or mutate a Speckle Object",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_objects_create;

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddGenericParameter(
      "Object",
      "O",
      "Input Object. Speckle objects, Model Objects, and geometry are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[0].Optional = true;

    pManager.AddGenericParameter("Geometry", "G", "The geometry of the Speckle Object", GH_ParamAccess.item);
    Params.Input[1].Optional = true;

    pManager.AddTextParameter("Name", "N", "Name of the Speckle Object", GH_ParamAccess.item);
    Params.Input[2].Optional = true;

    pManager.AddGenericParameter(
      "Properties",
      "P",
      "The properties of the Speckle Object. Speckle Properties and User Content are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[3].Optional = true;

    pManager.AddColourParameter("Color", "c", "The color of the Speckle Object", GH_ParamAccess.item);
    Params.Input[4].Optional = true;

    pManager.AddGenericParameter(
      "Material",
      "m",
      "The material of the Speckle Object. Display Materials, Model Materials, and Speckle Materials are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[5].Optional = true;
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleObjectParam(), "Object", "O", "Speckle Object", GH_ParamAccess.item);

    pManager.AddGenericParameter("Geometry", "G", "The geometry of the Speckle Object", GH_ParamAccess.item);

    pManager.AddTextParameter("Name", "N", "Name of the Speckle Object", GH_ParamAccess.item);

    pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "P",
      "The properties of the Speckle Object",
      GH_ParamAccess.item
    );

    pManager.AddColourParameter("Color", "c", "The color of the Speckle Object", GH_ParamAccess.item);

    pManager.AddGenericParameter(
      "Material",
      "m",
      "The material of the Speckle Object. Display Materials, Model Materials, and Speckle Materials are accepted.",
      GH_ParamAccess.item
    );
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    IGH_Goo? inputObject = null;
    da.GetData(0, ref inputObject);

    IGH_GeometricGoo? inputGeometry = null;
    da.GetData(1, ref inputGeometry);

    string? inputName = null;
    da.GetData(2, ref inputName);

    IGH_Goo? inputProperties = null;
    da.GetData(3, ref inputProperties);

    Color? inputColor = null;
    da.GetData(4, ref inputColor);

    IGH_Goo? inputMaterial = null;
    da.GetData(5, ref inputMaterial);

    // keep track of mutation
    // poc: we should not mark mutations on color or material, as this shouldn't affect the appId of the object, and will allow original display values to stay intact on send.
    bool mutated = false;

    // process the object
    SpeckleObjectWrapperGoo result = new();
    if (inputObject != null)
    {
      if (!result.CastFrom(inputObject))
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Warning,
          $"Object input is not valid. Only Speckle Objects, Model Object, and Geometry are accepted."
        );
        return;
      }
    }

    // process geometry
    // at this point, we can ensure that the Base in the wrapper is a DataObject.
    if (inputObject == null && inputGeometry == null)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Pass in an Object or Geometry.");
      return;
    }

    if (inputGeometry != null)
    {
      result.Value.GeometryBase = inputGeometry.GeometricGooToGeometryBase();
      result.Value.Base = SpeckleConversionContext.ConvertToSpeckle(result.Value.GeometryBase);
      mutated = true;
    }

    // process name
    if (inputName != null)
    {
      result.Value.Name = inputName;
      mutated = true;
    }

    // process properties
    if (inputProperties != null)
    {
      SpecklePropertyGroupGoo propGoo = new();
      if (!propGoo.CastFrom(inputProperties))
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Warning,
          $"Properties input is not valid. Only Speckle Properties and User Content are accepted."
        );
        return;
      }

      result.Value.Properties = propGoo;
      Dictionary<string, object?> props = new();
      propGoo.CastTo(ref props);
      mutated = true;
    }

    // process color (no mutation)
    if (inputColor != null)
    {
      result.Value.Color = inputColor;
    }

    // process  material (no mutation)
    if (inputMaterial != null)
    {
      SpeckleMaterialWrapperGoo matWrapperGoo = new();
      if (!matWrapperGoo.CastFrom(inputMaterial))
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Warning,
          $"Material input is not valid. Only Display Materials, Model Materials, and Speckle Materials are accepted."
        );
        return;
      }

      result.Value.Material = matWrapperGoo.Value;
    }

    // process application Id. Use a new appId if mutated, or if this is a new object
    result.Value.ApplicationId = mutated
      ? Guid.NewGuid().ToString()
      : result.Value.ApplicationId ?? Guid.NewGuid().ToString();

    // set all the data
    da.SetData(0, result.Value);
    da.SetData(1, result.Value.GeometryBase);
    da.SetData(2, result.Value.Name);
    da.SetData(3, result.Value.Properties);
    da.SetData(4, result.Value.Color);
    da.SetData(5, result.Value.Material);
  }
}
