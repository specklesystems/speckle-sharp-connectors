using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.DocObjects;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

/// <summary>
/// Given a collection, this component will output the objects in the collection that satisfy the input parameters
/// </summary>
[Guid("77CAEE94-F0B9-4611-897C-71F2A22BA311")]
public class QuerySpeckleObjects : GH_Component, IGH_VariableParameterComponent
{
  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_objects_query;

  public QuerySpeckleObjects()
    : base(
      "Query Speckle Objects",
      "qO",
      "Retrieves the Speckle objects inside a Speckle collection satisfying the input conditions",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleCollectionParam(),
      "Collection",
      "C",
      "Collection to retrieve objects from",
      GH_ParamAccess.item
    );

    pManager.AddTextParameter(
      "Path",
      "C",
      "Get the Speckle objects in the subcollection indicated by this path",
      GH_ParamAccess.item
    );

    Params.Input[1].Optional = true;
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleObjectParam(),
      "Objects",
      "O",
      "The objects in the input collection that match the queries",
      GH_ParamAccess.tree
    );
  }

  // The list of filters that can be added by the user as a dynamic output
  // The order of this array will determine the order of outputs in this component
  private List<ObjectType> Filters =>
    [
      ObjectType.InstanceReference,
      ObjectType.Point,
      ObjectType.PointSet,
      ObjectType.Curve,
      ObjectType.Extrusion,
      ObjectType.Brep,
      ObjectType.SubD,
      ObjectType.Mesh,
      ObjectType.Hatch
    ];

  private string GetFilterNickName(ObjectType type) =>
    type switch
    {
      ObjectType.InstanceReference => "Block Instances",
      ObjectType.Point => "Points",
      ObjectType.PointSet => "Point Clouds",
      ObjectType.Curve => "Curves",
      ObjectType.Extrusion => "Extrusions",
      ObjectType.Brep => "Breps",
      ObjectType.SubD => "SubDs",
      ObjectType.Mesh => "Meshes",
      ObjectType.Hatch => "Hatches",
      _ => ""
    };

  private List<int>? _outputFilterIndices;

  // Caches the list of all objects by geometrybase type
  private readonly Dictionary<ObjectType, List<SpeckleObjectWrapper>> _filterDict = new();

  protected override void SolveInstance(IGH_DataAccess dataAccess)
  {
    SpeckleCollectionWrapperGoo collectionWrapperGoo = new();
    dataAccess.GetData(0, ref collectionWrapperGoo);

    if (collectionWrapperGoo.Value == null)
    {
      return;
    }

    string path = "";
    dataAccess.GetData(1, ref path);

    // filter by collection path
    // Note: the collection paths selector will omit the target collection from the path of nested collections.
    // the discard ("_objects") will be used to indicate objects found directly in the target collection.
    List<SpeckleObjectWrapper> filteredObjects;
    SpeckleCollectionWrapper? targetCollectionWrapper = null;
    if (!string.IsNullOrEmpty(path))
    {
      targetCollectionWrapper =
        path == "_objects" ? collectionWrapperGoo.Value : FindCollectionAtPath(collectionWrapperGoo.Value, path);
      if (targetCollectionWrapper is null)
      {
        return;
      }

      filteredObjects = targetCollectionWrapper.Elements.OfType<SpeckleObjectWrapper>().ToList();
    }
    else
    {
      filteredObjects = GetAllObjectsFromCollection(collectionWrapperGoo.Value).ToList();
    }

    // sort objects by filters
    if (_filterDict.Count == 0)
    {
      SortObjectsByGeometryBaseType(filteredObjects);
    }

    // Set output objects
    for (int i = 0; i < Params.Output.Count; i++)
    {
      List<SpeckleObjectWrapper> outputValues = i == 0 ? filteredObjects : _filterDict[Filters[i - 1]];
      if (targetCollectionWrapper?.Topology is string topology && !string.IsNullOrEmpty(topology))
      {
        var tree = GrasshopperHelpers.CreateDataTreeFromTopologyAndItems(topology, outputValues);
        dataAccess.SetDataTree(i, tree);
      }
      else
      {
        dataAccess.SetDataList(i, outputValues);
      }
    }
  }

  // Sort the input objects by the FilterType enums, based on the type of their geometryBase
  private void SortObjectsByGeometryBaseType(List<SpeckleObjectWrapper> objs)
  {
    if (_filterDict.Count > 0)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Stored input objects are in an invalid state.");
      return;
    }

    foreach (ObjectType filter in Filters)
    {
      _filterDict.Add(filter, new());
    }

    foreach (var wrapper in objs)
    {
      if (
        wrapper.GeometryBase?.ObjectType is ObjectType objType
        && _filterDict.TryGetValue(objType, out List<SpeckleObjectWrapper>? value)
      )
      {
        value.Add(wrapper);
      }
    }
  }

  private IEnumerable<SpeckleObjectWrapper> GetAllObjectsFromCollection(SpeckleCollectionWrapper collectionWrapper)
  {
    foreach (ISpeckleCollectionObject element in collectionWrapper.Elements)
    {
      switch (element)
      {
        case SpeckleCollectionWrapper childCollectionWrapper:
          foreach (var item in GetAllObjectsFromCollection(childCollectionWrapper))
          {
            yield return item;
          }
          break;
        case SpeckleObjectWrapper objectWrapper:
          yield return objectWrapper;
          break;
      }
    }
  }

  private SpeckleCollectionWrapper? FindCollectionAtPath(SpeckleCollectionWrapper root, string unifiedPath)
  {
    // POC: SpeckleCollections now have a full list<string> path prop on them always. Is this easier to use?
    List<string> paths = unifiedPath.Split([Constants.LAYER_PATH_DELIMITER], StringSplitOptions.None).ToList();
    SpeckleCollectionWrapper currentCollectionWrapper = root;
    while (paths.Count != 0)
    {
      try
      {
        currentCollectionWrapper = currentCollectionWrapper
          .Elements.OfType<SpeckleCollectionWrapper>()
          .First(wrapper => wrapper.Name == paths.First());

        paths.RemoveAt(0);
        if (paths.Count == 0)
        {
          return currentCollectionWrapper;
        }
      }
      catch (InvalidOperationException) // when no wrappers match the current path
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"[{unifiedPath}] is an invalid path for this collection");
        return null;
      }
    }

    return null;
  }

  public bool CanInsertParameter(GH_ParameterSide side, int index)
  {
    if (side == GH_ParameterSide.Input || index == 0 || index > Filters.Count)
    {
      return false;
    }

    // repopulate current output params if needed
    if (_outputFilterIndices is null)
    {
      _outputFilterIndices = new();
      foreach (var output in Params.Output)
      {
        if (Enum.TryParse(output.Name, out ObjectType filter))
        {
          _outputFilterIndices.Add(Filters.IndexOf(filter));
        }
      }
    }

    int? previousFilterIndex = index == 1 ? null : _outputFilterIndices[index - 2];
    int? nextFilterIndex = index > _outputFilterIndices.Count ? null : _outputFilterIndices[index - 1];
    return (previousFilterIndex is null && nextFilterIndex != 0)
      || (nextFilterIndex is null && previousFilterIndex != Filters.Count - 1)
      || nextFilterIndex - previousFilterIndex > 1;
  }

  public bool CanRemoveParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Output && index != 0;

  public IGH_Param CreateParameter(GH_ParameterSide side, int index)
  {
    // get the next filter name based on what the previous output filter at this index is
    // index should account for the first output which is always all objects
    int? previousFilterIndex = _outputFilterIndices is null || index == 1 ? null : _outputFilterIndices[index - 2];
    _outputFilterIndices = null;

    ObjectType filter = previousFilterIndex is null ? Filters.First() : Filters[(int)previousFilterIndex + 1];
    return new Param_GenericObject
    {
      Name = filter.ToString(),
      NickName = GetFilterNickName(filter),
      MutableNickName = false,
      Optional = true
    };
  }

  public bool DestroyParameter(GH_ParameterSide side, int index)
  {
    _outputFilterIndices = null;
    return side == GH_ParameterSide.Output;
  }

  public void VariableParameterMaintenance() { }

  public override void AddedToDocument(GH_Document document)
  {
    base.AddedToDocument(document);
    Params.ParameterSourcesChanged += OnParameterSourceChanged;
  }

  public override void RemovedFromDocument(GH_Document document)
  {
    Params.ParameterSourcesChanged -= OnParameterSourceChanged;
    base.RemovedFromDocument(document);
  }

  private void OnParameterSourceChanged(object sender, GH_ParamServerEventArgs args)
  {
    // an empty filter dict will trigger the SortObjectsByGeometryBaseType method.
    // we only want to re-sort objects if an input has changed, not on every trigger of solve instance.
    _filterDict.Clear();
  }
}
