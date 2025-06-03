#if RHINO8_OR_GREATER
using Grasshopper.Rhinoceros;
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;
using Grasshopper.Rhinoceros.Params;
using Rhino;
using Rhino.DocObjects;

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

  private bool CastToModelObject<T>(ref T target)
  {
    var type = typeof(T);

    // grasshopper interface types
    if (type == typeof(IGH_ModelContentData))
    {
      var attributes = new ObjectAttributes();
      foreach (var entry in Value)
      {
        string stringValue = entry.Value.Value?.ToString() ?? "";
        attributes.SetUserString(entry.Key, stringValue);
      }

      var modelObject = new ModelObject(RhinoDoc.ActiveDoc, attributes);
      target = (T)(object)modelObject;
      return true;
    }

    return false;
  }
}
#endif
