using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

[Guid("5CE8AA40-7706-4893-853D-4C77604548FA")]
public class SpeckleDataObjectPassthrough()
  : SpecklePassthroughComponentBase(
    "Speckle Data Object",
    "SDO",
    "Create or modify a Speckle Data Object",
    ComponentCategories.PRIMARY_RIBBON,
    ComponentCategories.OBJECTS
  )
{
  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_objects_dataobject;
  public override GH_Exposure Exposure => GH_Exposure.secondary;

  protected override int FixedInputCount => 4;
  protected override int FixedOutputCount => 5;

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    int objIndex = pManager.AddParameter(
      new SpeckleDataObjectParam(),
      "Speckle Data Object",
      "SDO",
      "Input Speckle DataObject. Model Objects are also accepted.",
      GH_ParamAccess.item
    );
    Params.Input[objIndex].Optional = true;

    int geoIndex = pManager.AddParameter(
      new SpeckleGeometryWrapperParam(),
      "Geometries",
      "G",
      "Geometries of the Speckle Data Object. Speckle Geometry and Grasshopper geometry are accepted.",
      GH_ParamAccess.list
    );
    Params.Input[geoIndex].Optional = true;

    int nameIndex = pManager.AddTextParameter("Name", "N", "Name of the Speckle Data Object", GH_ParamAccess.item);
    Params.Input[nameIndex].Optional = true;

    int propIndex = pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "P",
      "The properties of the Speckle Data Object. Speckle Properties and User Content are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[propIndex].Optional = true;
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleDataObjectParam(),
      "Speckle Data Object",
      "SDO",
      "Speckle Data Object",
      GH_ParamAccess.item
    );

    pManager.AddParameter(
      new SpeckleGeometryWrapperParam(),
      "Geometries",
      "G",
      "Geometries of the Speckle Data Object.",
      GH_ParamAccess.list
    );

    pManager.AddTextParameter("Name", "N", "Name of the Speckle Data Object", GH_ParamAccess.item);

    pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "P",
      "The properties of the Speckle Data Object",
      GH_ParamAccess.item
    );

    pManager.AddTextParameter(
      "Path",
      "p",
      $"The Collection Path of the Speckle Geometry, delimited with `{Constants.LAYER_PATH_DELIMITER}`",
      GH_ParamAccess.item
    );
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    // process the object
    // deep copy so we don't mutate the object
    SpeckleDataObjectWrapperGoo inputObject = new();
    SpeckleDataObjectWrapper? result = null;
    if (da.GetData(0, ref inputObject))
    {
      result = inputObject.Value.DeepCopy();
    }

    List<SpeckleGeometryWrapperGoo> inputGeometry = new();
    bool hasGeometries = da.GetDataList(1, inputGeometry);

    string? inputName = null;
    da.GetData(2, ref inputName);

    SpecklePropertyGroupGoo? inputProperties = null;
    da.GetData(3, ref inputProperties);

    bool hasAppId = TryGetApplicationIdInput(da, out string? inputAppId);

    if (result == null && !hasGeometries && inputName == null && inputProperties == null && !hasAppId)
    {
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Warning,
        "Pass in a Speckle DataObject or Geometries, Name, Properties or Application Id"
      );
    }

    foreach (var inputGeo in inputGeometry)
    {
      if (inputGeo.Value is SpeckleBlockInstanceWrapper)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "DataObjects cannot contain Block Instances");
        return;
      }
    }

    // process geometry
    if (result == null)
    {
      result = new SpeckleDataObjectWrapperGoo().Value;
    }

    if (inputGeometry.Count > 0)
    {
      result.Geometries.Clear();
      foreach (var inputGeo in inputGeometry)
      {
        // deep copy so we don't mutate the input geo which may be speckle geometry
        SpeckleGeometryWrapper mutatingGeo = inputGeo.Value.DeepCopy();

        // assign fields before adding, otherwise they will be out of sync with wrapper
        mutatingGeo.Base[Constants.NAME_PROP] = result.Name;
        mutatingGeo.Properties = result.Properties;
        mutatingGeo.Parent = result.Parent;
        mutatingGeo.Path = result.Path;

        result.Geometries.Add(mutatingGeo);
      }
    }

    // process name
    if (inputName != null)
    {
      result.Name = inputName;
    }

    // process properties
    if (inputProperties != null)
    {
      result.Properties = inputProperties;
    }

    // process application id (only if user provided one)
    if (hasAppId)
    {
      result.ApplicationId = inputAppId;
    }
    else
    {
      // generate application ID for new data objects. Unlike SpeckleGeometry, DataObject wrappers aren't created
      // through casting (which auto-generates IDs), so we must explicitly ensure an ID exists here
      result.ApplicationId ??= Guid.NewGuid().ToString();
    }

    // get the path
    string? path =
      result.Path.Count > 1 ? string.Join(Constants.LAYER_PATH_DELIMITER, result.Path) : result.Path.FirstOrDefault();

    // set all the data
    da.SetData(0, result.CreateGoo());
    da.SetDataList(1, result.Geometries);
    da.SetData(2, result.Name);
    da.SetData(3, result.Properties);
    da.SetData(4, path);
    SetApplicationIdOutput(da, result.ApplicationId);
  }
}
