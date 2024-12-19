using ConnectorGrasshopper.Extras;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;
using Rhino;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Components.Collections;

#pragma warning disable CA1711
public class CreateCollection : GH_Component, IGH_VariableParameterComponent
#pragma warning restore CA1711
{
  public override Guid ComponentGuid => new("BDCE743E-7BDB-479B-AA81-19854AB5A254");

  private DebounceDispatcher _debounceDispatcher = new();

  public CreateCollection()
    : base("Create collection", "Create collection", "Creates a new collection", "Speckle", "Collections") { }

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

      // if on this port we're only receiving collections, we should become "pass-through" to not create
      // needless nesting
      if (inputCollections.Count == data.Count)
      {
        var nameTest = new HashSet<string>();
        foreach (var collection in inputCollections)
        {
          foreach (var subCollectionName in collection.Value.elements.OfType<Collection>().Select(v => v.name))
          {
            var hasNotSeenNameBefore = nameTest.Add(subCollectionName);
            if (!hasNotSeenNameBefore)
            {
              AddRuntimeMessage(
                GH_RuntimeMessageLevel.Error,
                $"Duplicate collection name found: {subCollectionName} in input parameter {inputParam.NickName}. Please ensure collection names are unique per nesting level.\n See https://speckle.docs/grashopper/collections"
              );
              return;
            }
          }
          childCollection.elements.AddRange(collection.Value.elements);
        }
        rootCollection.elements.Add(childCollection);
        continue;
      }

      childCollection["topology"] = GetParamTopology(inputParam);

      foreach (var obj in data)
      {
        if (obj is SpeckleObjectGoo objectGoo)
        {
          childCollection.elements.Add(objectGoo.Value);
        }
        else if (obj is IGH_GeometricGoo geoGeo)
        {
          try
          {
            var geometryBase = geoGeo.GeometricGooToGeometryBase();
            var converted = ToSpeckleConversionContext.ToSpeckleConverter.Convert(geometryBase); // .Convert(geometryBase);

            var wrapper = new SpeckleObject() { GeometryBase = geometryBase, Base = converted };
            childCollection.elements.Add(wrapper);
          }
          catch (Exception e) when (!e.IsFatal())
          {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to convert object of type {obj.GetType()}");
            return;
          }
        }
        else if (obj is ModelObject { Id: not null } modelObject)
        {
          // TODO remove copy pasta
          var docObject = RhinoDoc.ActiveDoc.Objects.FindId(modelObject.Id.NotNull());
          var converted = ToSpeckleConversionContext.ToSpeckleConverter.Convert(docObject.Geometry); // .Convert(docObject.Geometry);

          var wrapper = new SpeckleObject() { GeometryBase = docObject.Geometry, Base = converted };
          childCollection.elements.Add(wrapper);
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
      if (args.ParameterSide == GH_ParameterSide.Output)
      {
        return;
      }
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
