/*
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

/// <summary>
/// Given a collection, this component will output the objects in the collection or any child collection that match the queries.
/// </summary>
[Guid("77CAEE94-F0B9-4611-897C-71F2A22BA311")]
public class FilterSpeckleObjects : GH_Component
{
  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_objects_query;

  public FilterSpeckleObjects()
    : base(
      "QuerySpeckleObjects",
      "qO",
      "Queries a Speckle collection for any Speckle objects inside that match the input rules",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.COLLECTIONS
    ) { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleCollectionParam(),
      "Collection",
      "C",
      "Collection to query objects from",
      GH_ParamAccess.item
    );

    pManager.AddTextParameter(
      "Collection Path",
      "C",
      "Find objects in the child collection of this matching path",
      GH_ParamAccess.item
    );

    pManager.AddTextParameter("Name", "N", "Find objects with a matching name", GH_ParamAccess.item);

    pManager.AddTextParameter(
      "Property Key",
      "P",
      "Find objects with a property that has a matching key",
      GH_ParamAccess.item
    );

    pManager.AddTextParameter(
      "Material Name",
      "M",
      "Find objects with a render material that has a matching name",
      GH_ParamAccess.item
    );

    pManager.AddTextParameter(
      "Application Id",
      "aID",
      "Find objects with a matching applicationId",
      GH_ParamAccess.item
    );

    pManager.AddTextParameter("Speckle Id", "sID", "Find objects with a matching Speckle id", GH_ParamAccess.item);
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
    string name = "";
    dataAccess.GetData(2, ref name);
    string property = "";
    dataAccess.GetData(3, ref property);
    string material = "";
    dataAccess.GetData(4, ref material);
    string appId = "";
    dataAccess.GetData(5, ref appId);
    string speckleId = "";
    dataAccess.GetData(6, ref speckleId);

    // Step 0 - filter by collection path
    // Note: the collection paths selector will omit the target collection from the path of nested collections.
    // the discard ("_objects") will be used to indicate objects found directly in the target collection.
    List<SpeckleObjectWrapper> filteredObjects = new();
    SpeckleCollectionWrapper? targetCollectionWrapper = null;
    if (!string.IsNullOrEmpty(path))
    {
      targetCollectionWrapper =
        path == "_objects" ? collectionWrapperGoo.Value : FindCollection(collectionWrapperGoo.Value, path);
      filteredObjects = targetCollectionWrapper
        .Collection.elements.Where(e => e is SpeckleObjectWrapper)
        .Select(e => (SpeckleObjectWrapper)e)
        .ToList();
    }
    else
    {
      filteredObjects = GetAllObjectsFromCollection(collectionWrapperGoo.Value).ToList();
    }

    for (int i = filteredObjects.Count - 1; i >= 0; i--)
    {
      // filter by name
      if (!string.IsNullOrEmpty(name) && filteredObjects[i].Name != name)
      {
        filteredObjects.RemoveAt(i);
        continue;
      }

      // filter by property
      if (!string.IsNullOrEmpty(property) && !filteredObjects[i].Properties.Value.ContainsKey(property))
      {
        filteredObjects.RemoveAt(i);
        continue;
      }

      // filter by material name
      if (!string.IsNullOrEmpty(material) && filteredObjects[i].Material?.Base.name != name)
      {
        filteredObjects.RemoveAt(i);
        continue;
      }

      // filter by application id
      if (!string.IsNullOrEmpty(appId) && filteredObjects[i].applicationId != appId)
      {
        filteredObjects.RemoveAt(i);
        continue;
      }

      // filter by speckle id
      if (!string.IsNullOrEmpty(speckleId) && filteredObjects[i].Base.id != speckleId)
      {
        filteredObjects.RemoveAt(i);
        continue;
      }
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
    foreach (Base element in collectionWrapper.Collection.elements)
    {
      switch (element)
      {
        case SpeckleCollectionWrapper childCollectionWrapper:
          GetAllObjectsFromCollection(childCollectionWrapper);
          break;
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
        .Collection.elements.OfType<SpeckleCollectionWrapper>()
        .First(col => col.Collection.name == paths.First());
      paths.RemoveAt(0);
      if (paths.Count == 0)
      {
        return currentCollectionWrapper;
      }
    }

    throw new SpeckleException("Did not find collection");
  }
}
*/
