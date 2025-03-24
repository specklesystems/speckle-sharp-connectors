using Grasshopper.Kernel.Types;
using Speckle.Connectors.Grasshopper8.HostApp.Special;
using Speckle.Connectors.Grasshopper8.Parameters;

namespace Speckle.Connectors.Grasshopper8.Components.Properties;

public class PropertyGroupPathsSelector : ValueSet<IGH_Goo>
{
  public PropertyGroupPathsSelector()
    : base(
      "Property Group Paths Selector",
      "Paths",
      "Allows you to select a set of property group paths for filtering",
      "Speckle",
      "Properties"
    ) { }

  public override Guid ComponentGuid => new Guid("8882BE3A-81F1-4416-B420-58D69E4CC8F1");

  protected override void LoadVolatileData()
  {
    var objectPropertyGroups = VolatileData
      .AllData(true)
      .OfType<SpeckleObjectGoo>()
      .Select(goo => goo.Value.Base["properties"] is Dictionary<string, object?> dict ? dict : null)
      .Where(dict => dict != null)
      .Cast<Dictionary<string, object?>>()
      .ToList();

    if (objectPropertyGroups.Count == 0)
    {
      return;
    }

    var paths = GetPropertyPaths(objectPropertyGroups);
    m_data.AppendRange(paths.Select(s => new GH_String(s)));
  }

  private static List<string> GetPropertyPaths(List<Dictionary<string, object?>> objectPropertyGroups)
  {
    var result = new HashSet<string>();
    foreach (var dict in objectPropertyGroups)
    {
      FlattenDictionaryRecursive(dict, string.Empty, result);
    }

    return result
      // This starts sucking, just as the frontend. I'm heavily inclined to make things more simple, and back to key value pairs.
      .Where(path => !(path.Contains(".name") || path.Contains(".units") || path.Contains(".internalDefinitionName")))
      .ToList();
  }

  private static void FlattenDictionaryRecursive(
    Dictionary<string, object?> dictionary,
    string parentKey,
    HashSet<string> result
  )
  {
    foreach (var kvp in dictionary)
    {
      string currentKey = string.IsNullOrEmpty(parentKey) ? kvp.Key : $"{parentKey}.{kvp.Key}";

      if (kvp.Value is Dictionary<string, object?> nestedDict)
      {
        // If the value is another dictionary, recurse into it
        FlattenDictionaryRecursive(nestedDict, currentKey, result);
      }
      else
      {
        // Otherwise, add just the key to the result
        result.Add(currentKey);
      }
    }
  }
}
