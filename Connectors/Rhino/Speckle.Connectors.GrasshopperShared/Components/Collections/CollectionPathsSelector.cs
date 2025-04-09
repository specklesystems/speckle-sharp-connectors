using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.Components.BaseComponents;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.GrasshopperShared.Components.Collections;

public class CollectionPathsSelector : ValueSet<IGH_Goo>
{
  public CollectionPathsSelector()
    : base(
      "Collection Paths Selector",
      "cSelect",
      "Allows you to select a set of collection paths for filtering",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.COLLECTIONS
    ) { }

  public override Guid ComponentGuid => new Guid("65FC4D58-2209-41B6-9B22-BE51C8B28604");

  protected override void LoadVolatileData()
  {
    var collections = VolatileData
      .AllData(true)
      .OfType<SpeckleCollectionWrapperGoo>()
      .Select(goo => goo.Value)
      .ToList();
    if (collections.Count == 0)
    {
      return;
    }

    // NOTE: supporting multiple collections? maybe? not really?
    var paths = new List<string>();
    foreach (SpeckleCollectionWrapper col in collections)
    {
      paths.AddRange(GetPaths(col.Collection));
    }
    m_data.AppendRange(paths.Select(s => new GH_String(s)));
  }

  private List<string> GetPaths(Collection c)
  {
    var currentPath = new List<string>();
    var allPaths = new HashSet<string>();

    void GetPathsInternal(Collection col)
    {
      currentPath.Add(col.name);
      var subCols = col.elements.OfType<SpeckleCollectionWrapper>().ToList();

      // NOTE: here we're basically outputting only paths that correspond to a collection
      // that has values inside of it.
      if (subCols.Count != col.elements.Count)
      {
        allPaths.Add(string.Join(Constants.LAYER_PATH_DELIMITER, currentPath));
      }

      foreach (var subCol in subCols)
      {
        GetPathsInternal(subCol.Collection);
      }
      currentPath.RemoveAt(currentPath.Count - 1);
    }

    GetPathsInternal(c);

    return allPaths.ToList();
  }
}
