using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Grasshopper8.Components.Objects;

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

    // TODO: add render material and color
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

    // convert the properties
    Dictionary<string, object?> props = new();
    properties.CastTo(ref props);

    // convert the geometries
    Base converted = ToSpeckleConversionContext.ToSpeckleConverter.Convert(geometry);

    Speckle.Objects.Data.DataObject grasshopperObject =
      new()
      {
        name = name,
        displayValue = new() { converted },
        properties = props
      };

    SpeckleObjectWrapper so =
      new()
      {
        Base = grasshopperObject,
        GeometryBase = geometry,
        Properties = properties,
        Name = name
      };

    da.SetData(0, new SpeckleObjectWrapperGoo(so));
  }
}
