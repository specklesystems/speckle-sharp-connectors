using Grasshopper.Kernel;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Components.Collections;

/// <summary>
/// Given a collection and a path, this component will output the objects in the corresponding collection.
/// Note: This component does not flatten the selected collection - if it contains sub collections those will not
/// be outputted.
///
/// To extract those objects out, you should select that specific sub path as well.
/// </summary>
public class FilterObjectsByPaths : GH_Component
{
  public override Guid ComponentGuid => new("77CAEE94-F0B9-4611-897C-71F2A22BA311");

  public FilterObjectsByPaths()
    : base("FilterObjectsByPaths", "FP", "todo", "Speckle", "Collections") { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleCollectionWrapperParam(),
      "Collection",
      "C",
      "Collection to filter objects from",
      GH_ParamAccess.item
    );
    pManager.AddTextParameter("Path", "P", "Path to filter by", GH_ParamAccess.item);
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

    SpeckleCollectionGoo collectionGoo = new();
    dataAccess.GetData(0, ref collectionGoo);

    if (collectionGoo.Value == null)
    {
      return;
    }

    var targetCollection = FindCollection(collectionGoo.Value, path);
    var topology = targetCollection["topology"] as string;
    if (topology is null)
    {
      dataAccess.SetDataList(0, targetCollection.elements);
    }
    else
    {
      var tree = GrasshopperHelpers.CreateDataTreeFromTopologyAndItems(topology, targetCollection.elements);
      dataAccess.SetDataTree(0, tree);
    }
  }

  private Collection FindCollection(Collection root, string unifiedPath)
  {
    var collectionNames = unifiedPath.Split([" :: "], StringSplitOptions.None).Skip(1).ToList();
    Collection currentCollection = root;
    while (collectionNames.Count != 0)
    {
      currentCollection = currentCollection
        .elements.OfType<Collection>()
        .First(col => col.name == collectionNames.First());
      collectionNames.RemoveAt(0);
      if (collectionNames.Count == 0)
      {
        return currentCollection;
      }
    }

    throw new SpeckleException("Did not find collection");
  }
}
