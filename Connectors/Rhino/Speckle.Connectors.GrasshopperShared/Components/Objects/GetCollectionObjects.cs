using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

/// <summary>
/// Given a collection, this component will output the objects in the subcollection corresponding to the input path
/// </summary>
[Guid("77CAEE94-F0B9-4611-897C-71F2A22BA311")]
public class GetCollectionObjects : GH_Component
{
  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_objects_query;

  public GetCollectionObjects()
    : base(
      "Query Objects",
      "qO",
      "Retrieves the objects inside a Speckle collection at the specified path",
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
    List<SpeckleObjectWrapper> filteredObjects = new();
    SpeckleCollectionWrapper? targetCollectionWrapper = null;
    if (!string.IsNullOrEmpty(path))
    {
      targetCollectionWrapper =
        path == "_objects" ? collectionWrapperGoo.Value : FindCollection(collectionWrapperGoo.Value, path);
      filteredObjects = targetCollectionWrapper
        .Elements.Where(e => e is SpeckleObjectWrapper)
        .Select(e => (SpeckleObjectWrapper)e)
        .ToList();
    }
    else
    {
      filteredObjects = GetAllObjectsFromCollection(collectionWrapperGoo.Value).ToList();
    }

    // Set output objects
    if (targetCollectionWrapper?.Topology is string topology && !string.IsNullOrEmpty(topology))
    {
      var tree = GrasshopperHelpers.CreateDataTreeFromTopologyAndItems(topology, filteredObjects);
      dataAccess.SetDataTree(0, tree);
    }
    else
    {
      dataAccess.SetDataList(0, filteredObjects);
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

        // This includes SpeckleBlockInstanceWrapper since it inherits from SpeckleObjectWrapper
        case SpeckleObjectWrapper objectWrapper:
          yield return objectWrapper;
          break;
      }
    }
  }

  private SpeckleCollectionWrapper FindCollection(SpeckleCollectionWrapper root, string unifiedPath)
  {
    // POC: SpeckleCollections now have a full list<string> path prop on them always. Is this easier to use?
    List<string> paths = unifiedPath.Split([Constants.LAYER_PATH_DELIMITER], StringSplitOptions.None).ToList();
    SpeckleCollectionWrapper currentCollectionWrapper = root;
    while (paths.Count != 0)
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

    throw new SpeckleException("Did not find collection");
  }
}
