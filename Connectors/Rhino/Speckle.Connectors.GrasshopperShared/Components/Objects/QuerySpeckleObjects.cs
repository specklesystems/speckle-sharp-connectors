using System.Runtime.InteropServices;
using Grasshopper.Kernel;
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
  public override GH_Exposure Exposure => GH_Exposure.primary;

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
      "P",
      "Get the Speckle objects in the sub-collection indicated by this path",
      GH_ParamAccess.item
    );

    Params.Input[1].Optional = true;
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddGenericParameter(
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
      ObjectType.Hatch,
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
      _ => "",
    };

  private List<int>? _outputFilterIndices;

  // Caches the list of all objects by geometrybase type
  private readonly Dictionary<ObjectType, List<SpeckleGeometryWrapper>> _filterDict = [];

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

    // ensure fresh data for type-specific outputs
    _filterDict.Clear();

    // filter by collection path
    // Note: the collection paths selector will omit the target collection from the path of nested collections.
    // the discard ("_objects") will be used to indicate objects found directly in the target collection.
    List<SpeckleWrapper> filteredObjects;
    SpeckleCollectionWrapper? targetCollectionWrapper = null;
    if (!string.IsNullOrEmpty(path))
    {
      targetCollectionWrapper =
        path == "_objects" ? collectionWrapperGoo.Value : FindCollectionAtPath(collectionWrapperGoo.Value, path);
      if (targetCollectionWrapper is null)
      {
        return;
      }

      filteredObjects = targetCollectionWrapper.GetAtomicObjects(true).ToList();
    }
    else
    {
      filteredObjects = collectionWrapperGoo.Value.GetAtomicObjects(true).ToList();
    }

    // sort geometry objects by filters
    var geometryObjects = filteredObjects.OfType<SpeckleGeometryWrapper>().ToList();
    SortObjectsByGeometryBaseType(geometryObjects);

    // Set output objects
    for (int i = 0; i < Params.Output.Count; i++)
    {
      // determine output values based on parameter type
      List<SpeckleWrapper> outputValues;
      if (i == 0)
      {
        outputValues = filteredObjects;
      }
      else if (
        Enum.TryParse(Params.Output[i].Name, out ObjectType filterType)
        && _filterDict.TryGetValue(filterType, out var filteredList)
      )
      {
        outputValues = filteredList.Cast<SpeckleWrapper>().ToList();
      }
      else
      {
        outputValues = [];
      }

      var outputGoos = outputValues.Select(o => o.CreateGoo()).ToList();

      if (i == 0 && targetCollectionWrapper?.Topology is string topology && !string.IsNullOrEmpty(topology))
      {
        // include nulls to match topology count (CNX-2855)
        var outputGoosWithNulls = targetCollectionWrapper.ToGooListWithNulls();
        var tree = GrasshopperHelpers.CreateDataTreeFromTopologyAndItems(topology, outputGoosWithNulls);
        dataAccess.SetDataTree(i, tree);
      }
      else
      {
        dataAccess.SetDataList(i, outputGoos);
      }
    }
  }

  // Sort the input objects by the FilterType enums, based on the type of their geometryBase
  private void SortObjectsByGeometryBaseType(List<SpeckleGeometryWrapper> objs)
  {
    if (_filterDict.Count > 0)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Stored input objects are in an invalid state");
      return;
    }

    foreach (ObjectType filter in Filters)
    {
      _filterDict.Add(filter, []);
    }

    foreach (var wrapper in objs)
    {
      if (
        wrapper.GeometryBase?.ObjectType is ObjectType objType
        && _filterDict.TryGetValue(objType, out List<SpeckleGeometryWrapper>? value)
      )
      {
        value.Add(wrapper);
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
      _outputFilterIndices = [];
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
    return new SpeckleOutputParam
    {
      Name = filter.ToString(),
      NickName = GetFilterNickName(filter),
      MutableNickName = false,
      Optional = true,
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

  private void OnParameterSourceChanged(object sender, GH_ParamServerEventArgs args) { }
}
