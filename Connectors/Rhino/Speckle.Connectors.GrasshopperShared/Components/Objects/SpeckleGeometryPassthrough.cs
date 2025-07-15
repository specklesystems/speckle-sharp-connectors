using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

[Guid("F9418610-ACAE-4417-B010-19EBEA6A121F")]
public class SpeckleGeometryPassthrough : GH_Component
{
  public SpeckleGeometryPassthrough()
    : base(
      "Speckle Geometry",
      "SG",
      "Create or modify a Speckle Geometry",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_objects_geometry;
  public override GH_Exposure Exposure => GH_Exposure.secondary;

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    int objIndex = pManager.AddGenericParameter(
      "Speckle Geometry",
      "SG",
      "Input Speckle Geometry. Model Objects are also accepted.",
      GH_ParamAccess.item
    );
    Params.Input[objIndex].Optional = true;

    int geoIndex = pManager.AddGeometryParameter(
      "Geometry",
      "G",
      "Geometry of the Speckle Geometry.",
      GH_ParamAccess.item
    );
    Params.Input[geoIndex].Optional = true;

    int nameIndex = pManager.AddTextParameter("Name", "N", "Name of the Speckle Geometry", GH_ParamAccess.item);
    Params.Input[nameIndex].Optional = true;

    int propIndex = pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "P",
      "The properties of the Speckle Geometry. Speckle Properties and User Content are accepted.",
      GH_ParamAccess.item
    );
    Params.Input[propIndex].Optional = true;

    int colorIndex = pManager.AddColourParameter(
      "Color",
      "c",
      "The color of the Speckle Geometry",
      GH_ParamAccess.item
    );
    Params.Input[colorIndex].Optional = true;

    int matIndex = pManager.AddParameter(
      new SpeckleMaterialParam(),
      "Material",
      "m",
      "The material of the Speckle Geometry. Display Materials, Model Materials, and Speckle Materials are accepted.",
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
    pManager.AddGenericParameter("Speckle Geometry", "SG", "Speckle Geometry", GH_ParamAccess.item);

    pManager.AddGeometryParameter("Geometry", "G", "Geometry of the Speckle Geometry.", GH_ParamAccess.item);

    pManager.AddTextParameter("Name", "N", "Name of the Speckle Geometry", GH_ParamAccess.item);

    pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "P",
      "The properties of the Speckle Geometry",
      GH_ParamAccess.item
    );

    pManager.AddColourParameter("Color", "c", "The color of the Speckle Geometry", GH_ParamAccess.item);

    pManager.AddParameter(
      new SpeckleMaterialParam(),
      "Material",
      "M",
      "The material of the Speckle Geometry.",
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
    IGH_Goo? inputObject = null;
    SpeckleGeometryWrapper? result = null;
    if (da.GetData(0, ref inputObject))
    {
      if (inputObject?.ToSpeckleGeometryWrapper() is SpeckleGeometryWrapper gooWrapper)
      {
        result = gooWrapper.DeepCopy();
      }
      else
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Unsupported object type: {inputObject?.TypeName}");
        return;
      }
    }

    IGH_GeometricGoo? inputGeometry = null;
    da.GetData(1, ref inputGeometry);

    if (result == null && inputGeometry == null)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Pass in a Speckle Geometry or Geometry.");
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

    // process geometry
    // deep copy so we don't mutate the input geo which may be speckle objects
    if (inputGeometry != null)
    {
      if (inputGeometry.ToSpeckleGeometryWrapper() is SpeckleGeometryWrapper geoWrapper)
      {
        SpeckleGeometryWrapper mutatingGeo = geoWrapper.DeepCopy();
        if (result is null)
        {
          result = mutatingGeo;
        }
        else
        {
          // we need to switch to the actual object wrapper type of the incoming geo if this is a mutation on the object
          if (mutatingGeo is SpeckleBlockInstanceWrapper mutatingInstance && result is not SpeckleBlockInstanceWrapper)
          {
            MatchNonGeometryProps(mutatingInstance, result);
            result = mutatingInstance;
          }
          else if (mutatingGeo is not SpeckleBlockInstanceWrapper && result is SpeckleBlockInstanceWrapper)
          {
            MatchNonGeometryProps(mutatingGeo, result);
            result = mutatingGeo;
          }

          mutatingGeo.Base[Constants.NAME_PROP] = result.Name; // assign these before assigning base since otherwise wrapper name and app will reset
          mutatingGeo.Base.applicationId = result.ApplicationId; // assign these before assigning base since otherwise wrapper name and app will reset
          result.Base = mutatingGeo.Base;
          result.GeometryBase = mutatingGeo.GeometryBase;
        }
      }
      else
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Error,
          $"{inputGeometry.TypeName} is not a valid type for Speckle Geometry."
        );
        return;
      }
    }

    // process name
    if (inputName != null)
    {
      result!.Name = inputName;
    }

    // process properties
    if (inputProperties != null)
    {
      result!.Properties = inputProperties;
    }

    // process color (no mutation)
    if (inputColor != null)
    {
      result!.Color = inputColor;
    }

    // process material (no mutation)
    if (inputMaterial != null)
    {
      result!.Material = inputMaterial.Value;
    }

    // no need to process application Id.
    // New definitions should have a new appID generated in the new() constructor, and we want to preserve old appID otherwise for changetracking.

    // get the path
    string path =
      result!.Path.Count > 1
        ? string.Join(Constants.LAYER_PATH_DELIMITER, result!.Path)
        : result!.Path.FirstOrDefault();

    // set all the data
    da.SetData(0, result.CreateGoo());
    da.SetData(1, result.GeometryBase);
    da.SetData(2, result.Name);
    da.SetData(3, result.Properties);
    da.SetData(4, result.Color);
    da.SetData(5, result.Material);
    da.SetData(6, path);
  }

  // keeps the geometry and wrapped base the same while assigning all other props from the inut wrapper
  private void MatchNonGeometryProps(SpeckleGeometryWrapper wrapper, SpeckleGeometryWrapper wrapperToMatch)
  {
    wrapper.Name = wrapperToMatch.Name;
    wrapper.ApplicationId = wrapperToMatch.ApplicationId;
    wrapper.Properties = wrapperToMatch.Properties;
    wrapper.Parent = wrapperToMatch.Parent;
    wrapper.Path = wrapperToMatch.Path;
    wrapper.Color = wrapperToMatch.Color;
    wrapper.Material = wrapperToMatch.Material;
  }
}
