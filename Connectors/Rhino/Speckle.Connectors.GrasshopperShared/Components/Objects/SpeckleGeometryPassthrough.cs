using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

/// <summary>
/// Geometry, colour and material only. Name and properties belong to a Speckle Object - eav is keyed by object, and
/// geometry rows are content-hash deduped, so attributes can't live there [ENG-9382].
/// </summary>
[Guid("6B4E1D07-9A83-4F2C-B5D1-7E0C3A9F4128")]
public class SpeckleGeometryPassthrough()
  : SpecklePassthroughComponentBase(
    "Speckle Geometry",
    "SG",
    "Create or modify a Speckle Geometry. Name and properties live on the Speckle Object that contains it.",
    ComponentCategories.PRIMARY_RIBBON,
    ComponentCategories.OBJECTS
  )
{
  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_objects_geometry;
  public override GH_Exposure Exposure => GH_Exposure.secondary;

  protected override int FixedInputCount => 4;
  protected override int FixedOutputCount => 5;

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
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddGenericParameter("Speckle Geometry", "SG", "Speckle Geometry", GH_ParamAccess.item);

    pManager.AddGeometryParameter("Geometry", "G", "Geometry of the Speckle Geometry.", GH_ParamAccess.item);

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
    // deep copy so we don't mutate the input
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
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Pass in a Speckle Geometry or Geometry");
      return;
    }

    Color? inputColor = null;
    da.GetData(2, ref inputColor);

    SpeckleMaterialWrapperGoo? inputMaterial = null;
    da.GetData(3, ref inputMaterial);

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
          // switch to the incoming geo's wrapper type if this is a mutation on the object
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

          // the base carries the properties, so swapping it below takes the incoming geometry's - the name is
          // carried across explicitly, the properties are not
          bool dropsProperties = result.Properties.Value.Count > 0 && !result.Properties.Equals(mutatingGeo.Properties);

          // assign before the base, otherwise wrapper name and app id reset
          mutatingGeo.Base[Constants.NAME_PROP] = result.Name;
          mutatingGeo.Base.applicationId = result.ApplicationId;
          result.Base = mutatingGeo.Base;
          result.GeometryBase = mutatingGeo.GeometryBase;

          if (dropsProperties)
          {
            AddRuntimeMessage(
              GH_RuntimeMessageLevel.Warning,
              "Replacing the geometry dropped its properties. The name was kept. Use a Speckle Object if you need "
                + "the properties."
            );
          }
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

    result.NotNull();

    if (inputColor != null)
    {
      result.Color = inputColor;
    }

    if (inputMaterial != null)
    {
      result.Material = inputMaterial.Value;
    }

    if (TryGetApplicationIdInput(da, out string? inputAppId))
    {
      result.ApplicationId = inputAppId;
    }

    string? path =
      result.Path.Count > 1 ? string.Join(Constants.LAYER_PATH_DELIMITER, result.Path) : result.Path.FirstOrDefault();

    da.SetData(0, result.CreateGoo());
    da.SetData(1, result.GeometryBase);
    da.SetData(2, result.Color);
    da.SetData(3, result.Material);
    da.SetData(4, path);
    SetApplicationIdOutput(da, result.ApplicationId);
  }

  /// <summary>Keeps geometry and wrapped base, assigns everything else from the input wrapper.</summary>
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
