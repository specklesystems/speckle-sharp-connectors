using ConnectorGrasshopper.Extras;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Rhino.Geometry;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models.Collections;
using Point = Rhino.Geometry.Point;

namespace Speckle.Connectors.Grasshopper8.Components.Collections;

#pragma warning disable CA1711
public class CreateCollection : GH_Component, IGH_VariableParameterComponent
#pragma warning restore CA1711
{
  public override Guid ComponentGuid => new("BDCE743E-7BDB-479B-AA81-19854AB5A254");

  private DebounceDispatcher _debounceDispatcher = new();

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
    using var scope = PriorityLoader.Container.CreateScope();
    var rhinoConversionSettingsFactory = scope.ServiceProvider.GetRequiredService<IRhinoConversionSettingsFactory>();
    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));
    var hostConverter = scope.ServiceProvider.GetService<IRootToSpeckleConverter>();

    var rootCollection = new Collection() { name = "Unnamed", applicationId = InstanceGuid.ToString() };
    foreach (var inputParam in Params.Input)
    {
      var data = inputParam.VolatileData.AllData(true).ToList();
      if (data.Count == 0)
      {
        continue;
      }

      var inputCollections = data.OfType<SpeckleCollectionGoo>().Empty().ToList();
      var inputNonCollections = data.Where(t => t is not SpeckleCollectionGoo).Empty().ToList();
      if (inputCollections.Count != 0 && inputNonCollections.Count != 0)
      {
        // TODO: error out! we want to disallow setting objects and collections in the same parent collection
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Error,
          $"Parameter {inputParam.NickName} should not contain both objects and collections."
        );
        return;
      }
      var childCollection = new Collection(inputParam.NickName) { applicationId = inputParam.InstanceGuid.ToString() };

      if (inputCollections.Count == data.Count)
      {
        // TODO
        var subCollections = new List<Collection>();
        foreach (var collection in inputCollections)
        {
          childCollection.elements.AddRange(collection.Value.elements);
        }
        rootCollection.elements.Add(childCollection);
        continue;
      }

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
          // TODO: right now creating incomplete speckle object wrappers, we should also convert
          try
          {
            var value = geoGeo.GetType().GetProperty("Value")?.GetValue(obj);
            if (value is GeometryBase gb)
            {
              var converted = hostConverter.Convert(gb);
              childCollection.elements.Add(new SpeckleObject() { GeometryBase = gb, OriginalObject = converted });
            }

            if (value is Point3d pt)
            {
              var geometryBasePoint = new Point(pt);
              var converted = hostConverter.Convert(geometryBasePoint);
              childCollection.elements.Add(
                new SpeckleObject() { GeometryBase = geometryBasePoint, OriginalObject = converted }
              );
            }

            if (value is Line ln)
            {
              var geometryBaseLine = new LineCurve(ln);
              var converted = hostConverter.Convert(geometryBaseLine);
              childCollection.elements.Add(
                new SpeckleObject() { GeometryBase = geometryBaseLine, OriginalObject = converted }
              );
            }

            if (value is Rectangle3d rc)
            {
              var gbRec = rc.ToPolyline().ToPolylineCurve();
              var converted = hostConverter.Convert(gbRec);
              childCollection.elements.Add(new SpeckleObject() { GeometryBase = gbRec, OriginalObject = converted });
            }

            if (value is Circle c)
            {
              var gbCircle = c.ToNurbsCurve();
              var converted = hostConverter.Convert(gbCircle);
              childCollection.elements.Add(new SpeckleObject() { GeometryBase = gbCircle, OriginalObject = converted });
            }

            if (value is Ellipse el) { }

            if (value is Sphere sph) { }

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
        else if (obj is ModelObject)
        {
          // TODO
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
        case GH_ObjectEventType.NickName:
          // This means the user is typing characters, debounce until it stops for 400ms before expiring the solution.
          // Prevents UI from locking too soon while writing new names for inputs.
          args.Parameter.Name = args.Parameter.NickName;
          _debounceDispatcher.Debounce(500, e => ExpireSolution(true));
          break;
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
