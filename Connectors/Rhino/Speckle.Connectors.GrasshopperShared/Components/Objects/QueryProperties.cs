using System.Collections;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

[Guid("116F08A5-BAA7-45B3-B6C8-469E452C9AC7")]
public class QueryProperties : GH_Component
{
  public override Guid ComponentGuid => GetType().GUID;

  protected override Bitmap Icon => Resources.speckle_properties_query;
  public override GH_Exposure Exposure => GH_Exposure.quarternary;

  public QueryProperties()
    : base(
      "Query Properties",
      "qP",
      "Retrieves the values of the Properties at the specified keys",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "P",
      "Speckle Properties",
      GH_ParamAccess.item
    );
    pManager.AddTextParameter("Keys", "K", "Property keys to filter by", GH_ParamAccess.list);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddGenericParameter("Values", "V", "The values of the specified keys", GH_ParamAccess.list);
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    SpecklePropertyGroupGoo? properties = null;
    if (!da.GetData(0, ref properties))
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input a Speckle Properties item");
      return;
    }

    List<string> keys = [];
    if (!da.GetDataList(1, keys))
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input a key");
      return;
    }

    if (properties == null || properties.Value.Count == 0 || keys.Count == 0)
    {
      return;
    }

    List<object?> values = [];
    foreach (string key in keys)
    {
      var value = GetValueByPath(properties, key);
      var extractedValue = (value as SpecklePropertyGoo)?.Value ?? value;

      // NOTE: if property is a list, flatten into individual items for native gh list access
      if (extractedValue is IList itemList)
      {
        values.AddRange(itemList.Cast<object?>());
      }
      else
      {
        values.Add(extractedValue);
      }
    }

    da.SetDataList(0, values);
  }

  public static ISpecklePropertyGoo? GetValueByPath(SpecklePropertyGroupGoo data, string path)
  {
    string[] keys = path.Split('.');
    ISpecklePropertyGoo? current = data;

    foreach (var key in keys)
    {
      if (current is SpecklePropertyGroupGoo dict)
      {
        if (dict.Value.TryGetValue(key, out ISpecklePropertyGoo? next))
        {
          current = next;
        }
        else
        {
          return null;
        }
      }
      else
      {
        return null; // Current is not a dictionary, path is invalid
      }
    }

    return current;
  }
}
