using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.Components.BaseComponents;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Collections;

public class CollectionPathsSelector : ValueSet<IGH_Goo>
{
  public CollectionPathsSelector()
    : base(
      "Collection Selector",
      "cSelect",
      "Allows you to select a set of collection paths for querying. Right-click for 'Always select all' option.",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.COLLECTIONS
    ) { }

  public override Guid ComponentGuid => new Guid("65FC4D58-2209-41B6-9B22-BE51C8B28604");
  protected override Bitmap Icon => Resources.speckle_inputs_collection;

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
    foreach (SpeckleCollectionWrapper wrapper in collections)
    {
      // note: we are skipping the input collection, to make the output paths more intuitive
      foreach (var element in wrapper.Elements)
      {
        if (element is SpeckleCollectionWrapper childCollectionWrapper)
        {
          paths.AddRange(GetPaths(childCollectionWrapper));
        }
        else
        {
          // include the input collection only if there are objects directly inside
          paths.Add("_objects");
        }
      }
    }
    m_data.Clear();
    m_data.AppendRange(paths.Select(s => new GH_String(s)));
  }

  private List<string> GetPaths(SpeckleCollectionWrapper wrapper)
  {
    var currentPath = new List<string>();
    var allPaths = new HashSet<string>();

    void GetPathsInternal(SpeckleCollectionWrapper w)
    {
      currentPath.Add(w.Name);
      var subCols = w.Elements.OfType<SpeckleCollectionWrapper>().ToList();

      // NOTE: here we're basically outputting only paths that correspond to a collection
      // that has values inside of it.
      if (subCols.Count != w.Elements.Count)
      {
        allPaths.Add(string.Join(Constants.LAYER_PATH_DELIMITER, currentPath));
      }

      foreach (var subCol in subCols)
      {
        GetPathsInternal(subCol);
      }

      currentPath.RemoveAt(currentPath.Count - 1);
    }

    GetPathsInternal(wrapper);
    return allPaths.ToList();
  }
}
