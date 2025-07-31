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
        Dictionary<string, ISpecklePropertyGoo> dictionary = new();
        foreach (KeyValuePair<string, string> entry in userText)
        {
          SpecklePropertyGoo value = new() { Value = entry.Value };
          dictionary.Add(entry.Key, value);
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
