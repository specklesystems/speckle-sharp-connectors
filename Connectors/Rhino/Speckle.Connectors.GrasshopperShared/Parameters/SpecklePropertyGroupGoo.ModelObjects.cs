#if RHINO8_OR_GREATER
using Grasshopper.Rhinoceros;
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;
using Grasshopper.Rhinoceros.Params;
using Rhino.DocObjects;
using Speckle.Connectors.GrasshopperShared.Registration;

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
        var processedDictionary = ConvertToNested(userText.ToDictionary(o => o.Key, o => (object)o.Value));
        return CastFrom(processedDictionary);

      default:
        return false;
    }
  }

  // Property keys may already be concatenated with the `.` char, eg if baked from grasshopper.
  public Dictionary<string, object> ConvertToNested(Dictionary<string, object> flatDict)
  {
    var nestedDict = new Dictionary<string, object>();

    foreach (string keyPath in flatDict.Keys)
    {
      var keys = keyPath.Split('.');
      var current = nestedDict;

      for (int i = 0; i < keys.Length; i++)
      {
        var key = keys[i];

        if (i == keys.Length - 1)
        {
          current[key] = flatDict[keyPath];
        }
        else
        {
          if (!current.TryGetValue(key, out var next))
          {
            var newDict = new Dictionary<string, object>();
            current[key] = newDict;
            current = newDict;
          }
          else
          {
            current = (Dictionary<string, object>)next;
          }
        }
      }
    }

    return nestedDict;
  }

  private bool CastToModelObject<T>(ref T target)
  {
    var type = typeof(T);

    // grasshopper interface types
    if (type == typeof(IGH_ModelContentData))
    {
      ObjectAttributes atts = new();
      AssignToObjectAttributes(atts);

      ModelObject modelObject = new(CurrentDocument.Document, atts);
      target = (T)(object)modelObject;
      return true;
    }

    return false;
  }
}
#endif
