using Grasshopper.Kernel.Types;
using Speckle.Connectors.Grasshopper8.HostApp.Special;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Components.Collections;

public class CollectionPathsSelector : ValueSet<IGH_Goo>
{
  public CollectionPathsSelector()
    : base(
      "Collection Paths Selector",
      "Paths",
      "Allows you to select a set of collection paths for filtering",
      "Speckle",
      "Collections"
    ) { }

  public override Guid ComponentGuid => new Guid("65FC4D58-2209-41B6-9B22-BE51C8B28604");

  protected override void LoadVolatileData()
  {
    var collections = VolatileData.AllData(true).OfType<SpeckleCollectionGoo>().Select(goo => goo.Value).ToList();
    if (collections.Count == 0)
    {
      return;
    }

    // NOTE: supporting multiple collections? maybe? not really?
    var myCollection = GetPaths(collections.First());
    m_data.AppendRange(myCollection.Select(s => new GH_String(s)));
  }

  private List<string> GetPaths(Collection c)
  {
    var currentPath = new List<string>();
    var allPaths = new HashSet<string>();

    void GetPathsInternal(Collection col)
    {
      currentPath.Add(col.name);
      allPaths.Add(string.Join(" > ", currentPath));
      var subCols = col.elements.OfType<Collection>();
      foreach (var subCol in subCols)
      {
        GetPathsInternal(subCol);
      }
      currentPath.RemoveAt(currentPath.Count - 1);
    }

    GetPathsInternal(c);

    return allPaths.ToList();
  }
}
