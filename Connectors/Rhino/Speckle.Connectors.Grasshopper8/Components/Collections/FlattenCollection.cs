using Grasshopper.Kernel;
using Rhino.Geometry;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Components.Collections;

#pragma warning disable CA1711
public class FlattenCollection : GH_Component
#pragma warning restore CA1711
{
  public override Guid ComponentGuid => new Guid("720ED4BE-BA4B-4E85-8220-412B3DA1D2B7");

  public FlattenCollection()
    : base("Flatten Collection", "flatten", "Flattens a collection into objects and paths", "Speckle", "Collections")
  { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddGenericParameter("Collection", "C", "Collection to unpack", GH_ParamAccess.item);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddGenericParameter("Objects", "O", "Objects", GH_ParamAccess.list);
    pManager.AddGenericParameter("Paths", "P", "Collection paths", GH_ParamAccess.list);
  }

  protected override void SolveInstance(IGH_DataAccess dataAccess)
  {
    Collection res = new();
    dataAccess.GetData("Collection", ref res);

    _sgos = new();
    Flatten(res);
    var paths = new List<string>();
    var objs = new List<GeometryBase>();
    foreach (var sg in _sgos)
    {
      var path = string.Join("::", sg.Path.Select(c => c.name));
      paths.Add(path);
      objs.Add(sg.GeometryBase);
    }

    dataAccess.SetDataList(0, objs);
    dataAccess.SetDataList(1, paths);
  }

  private List<SpeckleGrasshopperObject> _sgos = new();

  public void Flatten(Collection c)
  {
    foreach (var element in c.elements)
    {
      if (element is Collection subCol)
      {
        Flatten(subCol);
      }

      if (element is SpeckleGrasshopperObject sg)
      {
        _sgos.Add(sg);
      }
    }
  }
}
