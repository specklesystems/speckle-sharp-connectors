#if RHINO8_OR_GREATER
using Grasshopper.Rhinoceros;
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;
using Grasshopper.Rhinoceros.Params;
using Rhino;
using Rhino.DocObjects;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// The Speckle Property Group Goo is a nested dictionary of (key, speckle property or property group).
/// The <see cref="Flatten"/> method will use the property delimiter on the keys to flatten the property group into properties.
/// </summary>
public partial class SpecklePropertyGroupGoo : GH_Goo<Dictionary<string, ISpecklePropertyGoo>>, ISpecklePropertyGoo
{
  private bool CastFromModelObject(object source)
  {
    switch (source)
    {
      case ModelObject modelObject:
        return CastFromModelObject(modelObject.UserText);

      case ModelUserText userText:
        Dictionary<string, ISpecklePropertyGoo> dictionary = new();
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

      // flatten the props
      Dictionary<string, SpecklePropertyGoo> flattenedProps = Flatten();
      foreach (var entry in flattenedProps)
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
