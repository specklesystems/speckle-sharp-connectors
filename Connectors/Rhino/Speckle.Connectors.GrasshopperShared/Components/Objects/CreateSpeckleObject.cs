using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

[Guid("F9418610-ACAE-4417-B010-19EBEA6A121F")]
public class CreateSpeckleObject : GH_Component
{
  public CreateSpeckleObject()
    : base(
      "Create Speckle Object",
      "CSO",
      "Creates a Speckle Object",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  public override Guid ComponentGuid => GetType().GUID;

  protected override Bitmap Icon => BitmapBuilder.CreateCircleIconBitmap("cO");

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddGenericParameter("Geometry", "G", "The geometry of the new Speckle Object", GH_ParamAccess.item);

    pManager.AddTextParameter("Name", "N", "Name of the new Speckle Object", GH_ParamAccess.item);
    Params.Input[1].Optional = true;

    pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "P",
      "The properties of the new Speckle Object",
      GH_ParamAccess.item
    );
    Params.Input[2].Optional = true;

    pManager.AddColourParameter("Color", "c", "The color of the new Speckle Object", GH_ParamAccess.item);
    Params.Input[3].Optional = true;

    // TODO: add render material
    pManager.AddGenericParameter("Material", "m", "The material of the new Speckle Object", GH_ParamAccess.item);
    Params.Input[4].Optional = true;
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddGenericParameter("Speckle Object", "SO", "The created Speckle Object", GH_ParamAccess.item);
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    object gooGeometry = new();
    da.GetData(0, ref gooGeometry);
    GeometryBase geometry = ((IGH_GeometricGoo)gooGeometry).GeometricGooToGeometryBase();

    string name = "";
    da.GetData(1, ref name);

    SpecklePropertyGroupGoo properties = new();
    da.GetData(2, ref properties);

    Color? color = null;
    da.GetData(3, ref color);

    //IGH_Param? material = null;
    IGH_Goo? material = null;
    da.GetData(4, ref material);

    // convert the properties
    Dictionary<string, object?> props = new();
    properties.CastTo(ref props);

    // convert the geometries
    Base converted = SpeckleConversionContext.ConvertToSpeckle(geometry);

    // convert the material
    SpeckleMaterialWrapperGoo matWrapper = new();
    if (material != null)
    {
      matWrapper.CastFrom(material);
    }

    // generate an application Id
    Guid guid = Guid.NewGuid();

    Speckle.Objects.Data.DataObject grasshopperObject =
      new()
      {
        name = name,
        displayValue = new() { converted },
        properties = props,
        applicationId = guid.ToString()
      };

    SpeckleObjectWrapper so =
      new()
      {
        Base = grasshopperObject,
        GeometryBase = geometry,
        Properties = properties,
        Color = color,
        Material = matWrapper.Value,
        Name = name,
        applicationId = guid.ToString()
      };

    da.SetData(0, new SpeckleObjectWrapperGoo(so));
  }
}
