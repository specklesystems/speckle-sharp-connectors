#if RHINO8_OR_GREATER
using Grasshopper.Rhinoceros;
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// The Speckle Property Group Goo is a flat dictionary of (speckle property path, speckle property).
/// The speckle property path is the concatenated string of all original flattened keys with the property delimiter
/// </summary>
public partial class SpecklePropertyGroupGoo : GH_Goo<Dictionary<string, SpecklePropertyGoo>>, ISpeckleGoo
{
  private bool CastFromModelObject(object source)
  {
    switch (source)
    {
      case ModelObject modelObject:
        return CastFromModelObject(modelObject.UserText);

      case ModelUserText userText:
        Dictionary<string, SpecklePropertyGoo> dictionary = new();
        foreach (KeyValuePair<string, string> entry in userText)
        {
          string key = entry.Key;
          SpecklePropertyGoo value = new() { Path = key, Value = entry.Value };
          dictionary.Add(key, value);
        }

        Value = dictionary;
        return true;

      default:
        return false;
    }
  }
}
#endif
