using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk.Models.Collections;
using Point = Rhino.Geometry.Point;

namespace Speckle.Connectors.Grasshopper8.Components.Collections;

#pragma warning disable CA1711
public class CreateCollection : GH_Component, IGH_VariableParameterComponent
#pragma warning restore CA1711
{
  public override Guid ComponentGuid => new("BDCE743E-7BDB-479B-AA81-19854AB5A254");

  public CreateCollection()
    : base("Create layer", "create", "Creates a new layer", "Speckle", "Collections") { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    var p = CreateParameter(GH_ParameterSide.Input, 0);
    pManager.AddParameter(p);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddGenericParameter("Layer", "L", "Collection that was created", GH_ParamAccess.tree);
  }

  protected override void SolveInstance(IGH_DataAccess dataAccess)
  {
    var rootCollection = new Collection() { name = Params.Output[0].NickName, applicationId = "temp" };
    foreach (var inputParam in Params.Input)
    {
      var data = inputParam.VolatileData.AllData(true).ToList();
      if (data.Count == 0)
      {
        continue;
      }

      var collections = data.Count(t => t is SpeckleCollectionGoo);
      var nonCollections = data.Count(t => t is not SpeckleCollectionGoo);
      if (collections != 0 && nonCollections != 0)
      {
        // TODO: error out! we want to disallow setting objects and collections in the same parent collection
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Error,
          $"Parameter {inputParam.NickName} should not contain both objects and collections."
        );
        return;
      }

      var childCollection = new Collection(inputParam.NickName) { applicationId = inputParam.InstanceGuid.ToString() };
      childCollection["topology"] = GetParamTopology(inputParam);

      foreach (var obj in data)
      {
        if (obj is SpeckleCollectionGoo collectionGoo)
        {
          childCollection.elements.Add(collectionGoo.Value);
        }
        else if (obj is SpeckleObjectGoo objectGoo)
        {
          childCollection.elements.Add(objectGoo.Value);
        }
        else if (obj is IGH_GeometricGoo geoGeo)
        {
          // if (geoGeo.CastTo<GeometryBase>(out GeometryBase? geometryBase)) // fails for points and lines
          // {
          //   var tempObj = new SpeckleObject() { GeometryBase = geometryBase };
          //   childCollection.elements.Add(tempObj);
          // }

          // TODO: right now creating incomplete speckle object wrappers, we should also convert
          try
          {
            var value = geoGeo.GetType().GetProperty("Value")?.GetValue(obj);
            if (value is GeometryBase gb)
            {
              childCollection.elements.Add(new SpeckleObject() { GeometryBase = gb });
            }

            if (value is Point3d pt)
            {
              var geometryBasePoint = new Point(pt);
              childCollection.elements.Add(new SpeckleObject() { GeometryBase = geometryBasePoint });
            }
            if (value is Line ln)
            {
              var geometryBaseLine = new LineCurve(ln);
              childCollection.elements.Add(new SpeckleObject() { GeometryBase = geometryBaseLine });
            }

            if (value is Rectangle3d rc)
            {
              childCollection.elements.Add(new SpeckleObject() { GeometryBase = rc.ToPolyline().ToPolylineCurve() });
            }

            // TODO: other fucking primitives, circles, ellipses(?), bla bla
            // Ask alan casting is so much bs right now in gh
          }
#pragma warning disable CA1031
          catch (Exception)
#pragma warning restore CA1031
          {
            // ignore
          }
        }
      }

      rootCollection.elements.Add(childCollection);
    }

    dataAccess.SetData(0, new SpeckleCollectionGoo(rootCollection));
  }

  public bool CanInsertParameter(GH_ParameterSide side, int index)
  {
    return side == GH_ParameterSide.Input;
  }

  public bool CanRemoveParameter(GH_ParameterSide side, int index)
  {
    return side == GH_ParameterSide.Input;
  }

  public IGH_Param CreateParameter(GH_ParameterSide side, int index)
  {
    var myParam = new Param_GenericObject
    {
      Name = $"Layer {Params.Input.Count + 1}",
      MutableNickName = true,
      Optional = true,
      Access = GH_ParamAccess.tree // always tree
    };

    myParam.NickName = myParam.Name;
    myParam.Optional = true;
    return myParam;
  }

  public bool DestroyParameter(GH_ParameterSide side, int index)
  {
    return side == GH_ParameterSide.Input;
  }

  public void VariableParameterMaintenance()
  {
    // TODO?
  }

  public override void AddedToDocument(GH_Document document)
  {
    base.AddedToDocument(document);
    Params.ParameterChanged += (sender, args) =>
    {
      switch (args.OriginalArguments.Type)
      {
        case GH_ObjectEventType.NickNameAccepted:
          args.Parameter.Name = args.Parameter.NickName;
          ExpireSolution(true);
          break;
      }
    };
  }

  public string GetParamTopology(IGH_Param param)
  {
    string topology = "";
    foreach (Grasshopper.Kernel.Data.GH_Path myPath in param.VolatileData.Paths)
    {
      topology += myPath.ToString(false) + "-" + param.VolatileData.get_Branch(myPath).Count + " ";
    }
    return topology;
  }
}
