using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

[Guid("F9418610-ACAE-4417-B010-19EBEA6A121F")]
public class SpeckleObjectPassthrough : GH_Component
{
  public SpeckleObjectPassthrough()
    : base(
      "Speckle Object",
      "SO",
      "Create or modify a Speckle Object",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_objects_object;
  public override GH_Exposure Exposure => GH_Exposure.primary;

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    int objIndex = pManager.AddParameter(
      new SpeckleObjectParam(),
      "Object",
      "O",
      "Input Object. Speckle Objects, Model Objects, and geometry are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[objIndex].Optional = true;

    int geoIndex = pManager.AddGenericParameter(
      "Geometry",
      "G",
      "Geometry of the Speckle Object. GeometryBase in Grasshopper includes text entities.",
      GH_ParamAccess.item
    );
    Params.Input[geoIndex].Optional = true;

    int nameIndex = pManager.AddTextParameter("Name", "N", "Name of the Speckle Object", GH_ParamAccess.item);
    Params.Input[nameIndex].Optional = true;

    int propIndex = pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "P",
      "The properties of the Speckle Object. Speckle Properties and User Content are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[propIndex].Optional = true;

    int colorIndex = pManager.AddColourParameter("Color", "c", "The color of the Speckle Object", GH_ParamAccess.item);
    Params.Input[colorIndex].Optional = true;

    int matIndex = pManager.AddParameter(
      new SpeckleMaterialParam(),
      "Material",
      "m",
      "The material of the Speckle Object. Display Materials, Model Materials, and Speckle Materials are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[matIndex].Optional = true;

    /* POC: disable for now as we are doing anything with this
    pManager.AddTextParameter(
      "Path",
      "p",
      "The Collection Path of the Speckle Object. Should be delimited with `:`",
      GH_ParamAccess.item
    );
    Params.Input[6].Optional = true;
    */
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleObjectParam(), "Object", "O", "Speckle Object", GH_ParamAccess.item);

    pManager.AddGenericParameter(
      "Geometry",
      "G",
      "Geometry of the Speckle Object. GeometryBase in Grasshopper includes text entities.",
      GH_ParamAccess.item
    );

    pManager.AddTextParameter("Name", "N", "Name of the Speckle Object", GH_ParamAccess.item);

    pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "P",
      "The properties of the Speckle Object",
      GH_ParamAccess.item
    );

    pManager.AddColourParameter("Color", "c", "The color of the Speckle Object", GH_ParamAccess.item);

    pManager.AddParameter(
      new SpeckleMaterialParam(),
      "Material",
      "M",
      "The material of the Speckle Object.",
      GH_ParamAccess.item
    );

    pManager.AddTextParameter(
      "Path",
      "p",
      $"The Collection Path of the Speckle Object, delimited with `{Constants.LAYER_PATH_DELIMITER}`",
      GH_ParamAccess.item
    );
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    SpeckleObjectWrapperGoo? inputObject = null;
    da.GetData(0, ref inputObject);

    IGH_GeometricGoo? inputGeometry = null;
    da.GetData(1, ref inputGeometry);

    if (inputObject == null && inputGeometry == null)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Pass in an Object or Geometry.");
      return;
    }

    string? inputName = null;
    da.GetData(2, ref inputName);

    SpecklePropertyGroupGoo? inputProperties = null;
    da.GetData(3, ref inputProperties);

    Color? inputColor = null;
    da.GetData(4, ref inputColor);

    SpeckleMaterialWrapperGoo? inputMaterial = null;
    da.GetData(5, ref inputMaterial);

    // keep track of mutation
    // poc: we should not mark mutations on color or material, as this shouldn't affect the appId of the object, and will allow original display values to stay intact on send.
    bool mutated = false;

    // process the object
    // deep copy so we don't mutate the object
    SpeckleObjectWrapperGoo result = inputObject != null ? new(inputObject.Value.DeepCopy()) : new();

    // process geometry
    if (inputGeometry != null)
    {
      result.Value = inputGeometry.ToSpeckleObjectWrapper();
      result.Value.Base[Constants.NAME_PROP] = result.Value.Name;
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
      result.Value.Properties = inputProperties;
      mutated = true;
    }

    // process color (no mutation)
    if (inputColor != null)
    {
      result.Value.Color = inputColor;
    }

    // process material (no mutation)
    if (inputMaterial != null)
    {
      result.Value.Material = inputMaterial.Value;
    }

    // process application Id. Use a new appId if mutated, or if this is a new object
    result.Value.ApplicationId = mutated
      ? Guid.NewGuid().ToString()
      : result.Value.ApplicationId ?? Guid.NewGuid().ToString();

    // get the path
    string path =
      result.Value.Path.Count > 1
        ? string.Join(Constants.LAYER_PATH_DELIMITER, result.Value.Path)
        : result.Value.Path.FirstOrDefault();

    // set all the data
    da.SetData(0, result);
    da.SetData(1, result.Value.GeometryBase);
    da.SetData(2, result.Value.Name);
    da.SetData(3, result.Value.Properties);
    da.SetData(4, result.Value.Color);
    da.SetData(5, result.Value.Material);
    da.SetData(6, path);
  }
}
