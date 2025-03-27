using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros;
using Speckle.Connectors.Grasshopper8.Components;
using Speckle.Connectors.Grasshopper8.HostApp;

namespace Speckle.Connectors.Grasshopper8.Parameters;

/// <summary>
/// The Speckle Property Group Goo is a flat dictionary of (speckle property path, speckle property).
/// The speckle property path is the concatenated string of all original flattened keys with the property delimiter
/// </summary>
public class SpecklePropertyGroupGoo : GH_Goo<Dictionary<string, SpecklePropertyGoo>>, ISpeckleGoo
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $"PropertyGroup ({Value.Count})";

  public override bool IsValid => true;
  public override string TypeName => "Speckle property group wrapper";
  public override string TypeDescription => "Speckle property group wrapper";

  public SpecklePropertyGroupGoo()
  {
    Value = new();
  }

  public SpecklePropertyGroupGoo(Dictionary<string, SpecklePropertyGoo> value)
  {
    Value = value;
  }

  public SpecklePropertyGroupGoo(Dictionary<string, object?> value)
  {
    CastFrom(value);
  }

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpecklePropertyGroupGoo specklePropertyGroup:
        Value = specklePropertyGroup.Value;
        return true;

      case ModelUserText userText:
        Dictionary<string, SpecklePropertyGoo> dictionary = new();
        foreach (var entry in userText)
        {
          string key = entry.Key;
          SpecklePropertyGoo value = new() { Path = key, Value = entry.Value };
          dictionary.Add(key, value);
        }

        Value = dictionary;
        return true;

      case Dictionary<string, object?> properties:
        Dictionary<string, object> flattenedProperties = new();
        FlattenDictionary(properties, flattenedProperties, "");
        Dictionary<string, SpecklePropertyGoo> speckleProperties = new();
        foreach (var kvp in flattenedProperties)
        {
          speckleProperties.Add(kvp.Key, new() { Value = kvp.Value });
        }
        Value = speckleProperties;
        return true;
    }

    return false;
  }

  public override bool CastTo<T>(ref T target)
  {
    var type = typeof(T);
    if (type == typeof(Dictionary<string, object?>))
    {
      Dictionary<string, object?> dictionary = new();
      foreach (var entry in Value)
      {
        dictionary.Add(entry.Key, entry.Value);
      }

      target = (T)(object)dictionary;
      return true;
    }

    // TODO: cast to material, model object, etc.
    return false;
  }

  // Flattens a dictionary that may contain more dictionaries of the same type
  private void FlattenDictionary(
    Dictionary<string, object?> dict,
    Dictionary<string, object> flattenedDict,
    string keyPrefix = ""
  )
  {
    foreach (var kvp in dict)
    {
      string newKey = string.IsNullOrEmpty(keyPrefix)
        ? kvp.Key
        : $"{keyPrefix}{Constants.PROPERTY_PATH_DELIMITER}{kvp.Key}";

      if (kvp.Value is Dictionary<string, object?> childDict)
      {
        FlattenDictionary(childDict, flattenedDict, newKey);
      }
      else
      {
        flattenedDict.Add(newKey, kvp.Value ?? "");
      }
    }
  }
}

public class SpecklePropertyGroupParam : GH_Param<SpecklePropertyGroupGoo>
{
  public override Guid ComponentGuid => new("AF4757C3-BA33-4ACD-A92B-C80356043129");
  protected override Bitmap Icon => BitmapBuilder.CreateHexagonalBitmap("PG");

  public SpecklePropertyGroupParam()
    : this(GH_ParamAccess.item) { }

  public SpecklePropertyGroupParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpecklePropertyGroupParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpecklePropertyGroupParam(GH_ParamAccess access)
    : base(
      "Speckle Property Group",
      "SPGO",
      "Represents a Dictionary property group",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }
}
