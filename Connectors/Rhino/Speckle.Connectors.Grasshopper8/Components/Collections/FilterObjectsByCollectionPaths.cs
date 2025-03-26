using Grasshopper.Kernel;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk;

namespace Speckle.Connectors.Grasshopper8.Components.Collections;

/// <summary>
/// Given a collection and a path, this component will output the objects in the corresponding collection.
/// Note: This component does not flatten the selected collection - if it contains sub collections those will not
/// be outputted.
///
/// To extract those objects out, you should select that specific sub path as well.
/// </summary>
public class FilterObjectsByCollectionPaths : GH_Component
{
  public override Guid ComponentGuid => new("77CAEE94-F0B9-4611-897C-71F2A22BA311");

  public FilterObjectsByCollectionPaths()
    : base(
      "FilterObjectsByCollectionPaths",
      "ocF",
      "Filters model objects by their collection path",
      "Speckle",
      "Collections"
    ) { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleCollectionParam(),
      "Collection",
      "C",
      "Collection to filter objects from",
      GH_ParamAccess.item
    );
    pManager.AddTextParameter("Path", "P", "Collection path to filter by", GH_ParamAccess.item);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleObjectParam(),
      "Objects",
      "O",
      "The contents of the selected collection",
      GH_ParamAccess.tree
    );
  }

  protected override void SolveInstance(IGH_DataAccess dataAccess)
  {
    string path = "";
    dataAccess.GetData(1, ref path);

    if (string.IsNullOrEmpty(path))
    {
      return;
    }

    SpeckleCollectionWrapperGoo collectionWrapperGoo = new();
    dataAccess.GetData(0, ref collectionWrapperGoo);

    if (collectionWrapperGoo.Value == null)
    {
      return;
    }

    SpeckleCollectionWrapper targetCollectionWrapper = FindCollection(collectionWrapperGoo.Value, path);
    if (string.IsNullOrEmpty(targetCollectionWrapper.Topology))
    {
      dataAccess.SetDataList(0, targetCollectionWrapper.Collection.elements);
    }
    else
    {
      var tree = GrasshopperHelpers.CreateDataTreeFromTopologyAndItems(
        targetCollectionWrapper.Topology,
        targetCollectionWrapper.Collection.elements
      );
      dataAccess.SetDataTree(0, tree);
    }
  }

  private SpeckleCollectionWrapper FindCollection(SpeckleCollectionWrapper root, string unifiedPath)
  {
    // POC: SpeckleCollections now have a full list<string> path prop on them always. Is this easier to use?
    List<string> paths = unifiedPath.Split([Constants.LAYER_PATH_DELIMITER], StringSplitOptions.None).Skip(1).ToList();
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
