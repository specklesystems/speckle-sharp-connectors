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

    var paths = new HashSet<string>();
    foreach (var propertyGroup in objectPropertyGroups)
    {
      var objectPropertyPaths = GetPaths(propertyGroup);
      foreach (string path in GetPaths(propertyGroup))
      {
        paths.Add(path);
      }
    }
    m_data.AppendRange(paths.Select(s => new GH_String(s)));
  }

  private HashSet<string> GetPaths(Dictionary<string, object?> dictionary)
  {
    var result = new HashSet<string>();
    FlattenDictionaryRecursive(dictionary, string.Empty, result);
    return result;
  }

  private void FlattenDictionaryRecursive(
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
