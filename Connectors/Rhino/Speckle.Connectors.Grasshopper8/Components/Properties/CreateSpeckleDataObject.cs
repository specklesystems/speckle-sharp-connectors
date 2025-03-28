using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Grasshopper8.Components.Properties;

[Guid("F9418610-ACAE-4417-B010-19EBEA6A121F")]
public class CreateSpeckleDataObject : GH_Component
{
  public CreateSpeckleDataObject()
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
    pManager.AddGenericParameter("Geometry", "G", "The geometry of the new Speckle Object", GH_ParamAccess.list);

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
    pManager.AddGenericParameter("Speckle Data Object", "DO", "The created Speckle Data Object", GH_ParamAccess.item);
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    List<IGH_GeometricGoo> gooGeometries = new();
    da.GetDataList(0, gooGeometries);
    List<GeometryBase> geometries = gooGeometries.Select(o => o.GeometricGooToGeometryBase()).ToList();

    string name = "";
    da.GetData(1, ref name);

    SpecklePropertyGroupGoo properties = new();
    da.GetData(2, ref properties);

    // convert the properties
    Dictionary<string, object?> props = new();
    properties.CastTo(ref props);

    // convert the geometries
    List<Base> converted = new();
    foreach (GeometryBase geo in geometries)
    {
      var geoConverted = ToSpeckleConversionContext.ToSpeckleConverter.Convert(geo);
      converted.Add(geoConverted);
    }

    Objects.Data.DataObject grasshopperObject =
      new()
      {
        name = name,
        displayValue = converted,
        properties = props
      };

    SpeckleObjectWrapper so =
      new()
      {
        Base = grasshopperObject,
        GeometryBases = geometries,
        Properties = properties
      };

    da.SetData(0, new SpeckleObjectWrapperGoo(so));
  }
}
