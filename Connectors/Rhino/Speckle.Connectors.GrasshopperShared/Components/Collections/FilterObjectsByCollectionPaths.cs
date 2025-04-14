using System.Reflection;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk;

namespace Speckle.Connectors.GrasshopperShared.Components.Collections;

/// <summary>
/// Given a collection and a path, this component will output the objects in the corresponding collection.
/// Note: This component does not flatten the selected collection - if it contains sub collections those will not
/// be outputted.
///
/// To extract those objects out, you should select that specific sub path as well.
/// </summary>
[Guid("77CAEE94-F0B9-4611-897C-71F2A22BA311")]
public class FilterObjectsByCollectionPaths : GH_Component
{
  public override Guid ComponentGuid => GetType().GUID;

  protected override Bitmap? Icon
  {
    get
    {
      Assembly assembly = GetType().Assembly;
      var stream = assembly.GetManifestResourceStream(
        assembly.GetName().Name + "." + "Resources" + ".speckle_objects_query.png"
      );
      return stream != null ? new Bitmap(stream) : null;
    }
  }

  public FilterObjectsByCollectionPaths()
    : base(
      "FilterObjectsByCollectionPaths",
      "ocF",
      "Filters model objects by their collection path",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.COLLECTIONS
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

    // the collection paths selector will omit the target collection from the path of nested collections.
    // the discard ("_") will be used to indicate objects found directly in the target collection.
    // find collection should search inside the input collection.

    SpeckleCollectionWrapper targetCollectionWrapper =
      path == "_objects" ? collectionWrapperGoo.Value : FindCollection(collectionWrapperGoo.Value, path);

    if (string.IsNullOrEmpty(targetCollectionWrapper.Topology))
    {
      dataAccess.SetDataList(
        0,
        targetCollectionWrapper.Collection.elements.Where(e => e is SpeckleObjectWrapper).ToList()
      );
    }
    else
    {
      var tree = GrasshopperHelpers.CreateDataTreeFromTopologyAndItems(
        targetCollectionWrapper.Topology,
        targetCollectionWrapper.Collection.elements.Where(e => e is SpeckleObjectWrapper).ToList()
      );
      dataAccess.SetDataTree(0, tree);
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
