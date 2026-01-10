using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.GrasshopperShared.Components.Collections;

/// <summary>
/// Creates collections by matching name tree structure to elements tree structure.
/// Each branch in the names tree corresponds to the same-path branch in the elements tree.
/// </summary>
[Guid("7E8F9A1B-2C3D-4E5F-6A7B-8C9D0E1F2A3B")]
public class CollectionsByName : GH_Component
{
  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_collections_create; // TODO: Update to specific icon if available
  public override GH_Exposure Exposure => GH_Exposure.primary;

  public CollectionsByName()
    : base(
      "Collections by Name",
      "CbN",
      "Creates collections by matching name tree structure to objects tree structure. Each branch in the names tree corresponds to the same-path branch in the objects tree.",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.COLLECTIONS
    ) { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddTextParameter(
      "Names",
      "N",
      "Collection names (tree structure must match Objects tree structure)",
      GH_ParamAccess.tree
    );

    pManager.AddGenericParameter(
      "Objects",
      "O",
      "Objects to group into collections (tree structure must match Names tree structure)",
      GH_ParamAccess.tree
    );
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager) =>
    pManager.AddParameter(
      new SpeckleCollectionParam(),
      "Collection",
      "C",
      "Root collection containing named sub-collections",
      GH_ParamAccess.item
    );

  protected override void SolveInstance(IGH_DataAccess da)
  {
    // access the tree data directly from parameters
    var namesParam = Params.Input[0];
    var elementsParam = Params.Input[1];

    var namesTree = namesParam.VolatileData;
    var elementsTree = elementsParam.VolatileData;

    // validate that both inputs have data
    if (namesTree.IsEmpty)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Names tree is empty");
      return;
    }

    if (elementsTree.IsEmpty)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Objects tree is empty");
      return;
    }

    // validate tree structures match exactly
    if (!TreeStructuresMatch(namesTree, elementsTree))
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Tree structures and topologies must match exactly");
      return;
    }

    // create root collection
    var rootCollection = CollectionHelpers.CreateRootCollection(InstanceGuid.ToString());

    // process each path
    foreach (var path in namesTree.Paths)
    {
      var nameBranch = namesTree.get_Branch(path);
      var elementsBranch = elementsTree.get_Branch(path);

      // validate name branch has exactly one name
      if (nameBranch.Count == 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Name branch at path {path} is empty");
        return;
      }

      if (nameBranch.Count > 1)
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Warning,
          $"Name branch at path {path} has {nameBranch.Count} names - using first name only"
        );
      }

      // get the collection name
      string collectionName = GetCollectionName(nameBranch[0]);
      if (string.IsNullOrWhiteSpace(collectionName))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Invalid collection name at path {path}");
        return;
      }

      // skip empty element branches with warning
      if (elementsBranch.Count == 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Skipping empty elements branch at path {path}");
        continue;
      }

      // create child collection from this branch
      var childCollection = CreateCollectionFromBranch(collectionName, elementsBranch, path, rootCollection.Name);
      rootCollection.Elements.Add(childCollection);
    }

    // validate collection has content
    if (rootCollection.Elements.Count == 0)
    {
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Error,
        "Collection contains no valid geometry. All branches were empty or contained unsupported types."
      );
      return;
    }

    // validate for duplicate application IDs (following CreateCollection pattern)
    if (CollectionHelpers.HasDuplicateApplicationIds(rootCollection))
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The same object(s) cannot appear in multiple collections");
      return;
    }

    da.SetData(0, new SpeckleCollectionWrapperGoo(rootCollection));
  }

  /// <summary>
  /// Validates that two tree structures have exactly matching paths
  /// </summary>
  private bool TreeStructuresMatch(
    Grasshopper.Kernel.Data.IGH_Structure namesTree,
    Grasshopper.Kernel.Data.IGH_Structure elementsTree
  )
  {
    if (namesTree.PathCount != elementsTree.PathCount)
    {
      return false;
    }

    // check that all paths match exactly
    var namePaths = namesTree.Paths.ToList();
    var elementPaths = elementsTree.Paths.ToList();

    for (int i = 0; i < namePaths.Count; i++)
    {
      if (namePaths[i].CompareTo(elementPaths[i]) != 0)
      {
        return false;
      }
    }

    return true;
  }

  /// <summary>
  /// Extracts collection name, handling GH_String and other text types
  /// </summary>
  private string GetCollectionName(object nameObj) =>
    nameObj switch
    {
      GH_String ghString => ghString.Value,
      IGH_Goo goo => goo.ToString(),
      _ => nameObj.ToString()
    };

  /// <summary>
  /// Creates a collection wrapper from a branch of elements
  /// </summary>
  private SpeckleCollectionWrapper CreateCollectionFromBranch(
    string collectionName,
    System.Collections.IList elementsBranch,
    Grasshopper.Kernel.Data.GH_Path path,
    string rootName
  )
  {
    var childPath = new List<string> { rootName, collectionName };

    var childCollection = new SpeckleCollectionWrapper
    {
      Base = new Collection(),
      Name = collectionName,
      Path = childPath,
      Color = null,
      Material = null,
      Topology = GetBranchTopology(path, elementsBranch.Count),
      ApplicationId = Guid.NewGuid().ToString()
    };

    // process elements in this branch
    foreach (var item in elementsBranch)
    {
      if (item == null)
      {
        // preserve nulls for topology (CNX-2855 pattern)
        childCollection.Elements.Add(null);
        continue;
      }

      // convert to SpeckleWrapper if possible - cast to IGH_Goo first
      if (item is not IGH_Goo goo)
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Warning,
          $"Unsupported object type in branch {path}: {item.GetType().Name}"
        );
        continue;
      }

      var wrapper = goo.ToSpeckleObjectWrapper();
      if (wrapper == null)
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Warning,
          $"Unsupported object type in branch {path}: {item.GetType().Name}"
        );
        continue;
      }

      if (wrapper is ISpeckleCollectionObject collectionObject)
      {
        childCollection.Elements.Add(collectionObject);
      }
      else
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Warning,
          $"Object type {wrapper.GetType().Name} is not a valid collection element"
        );
      }
    }

    return childCollection;
  }

  /// <summary>
  /// Creates topology string for a single branch (following GrasshopperHelpers.GetParamTopology pattern)
  /// </summary>
  private string GetBranchTopology(Grasshopper.Kernel.Data.GH_Path path, int count) =>
    $"{path.ToString(false)}-{count}";
}
