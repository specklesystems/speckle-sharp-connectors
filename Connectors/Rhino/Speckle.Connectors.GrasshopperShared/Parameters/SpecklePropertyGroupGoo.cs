using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// The Speckle Property Group Goo is a flat dictionary of (speckle property path, speckle property).
/// The speckle property path is the concatenated string of all original flattened keys with the property delimiter
/// </summary>
public partial class SpecklePropertyGroupGoo : GH_Goo<Dictionary<string, ISpecklePropertyGoo>>, ISpecklePropertyGoo
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

  public SpecklePropertyGroupGoo(Dictionary<string, ISpecklePropertyGoo> value)
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

      case Dictionary<string, object?> properties:
        Value = WrapDictionary(properties);
        return true;
    }

    // Handle case of model objects in rhino 8
    return CastFromModelObject(source);
  }

#if !RHINO8_OR_GREATER
  private bool CastFromModelObject(object _) => false;
#endif

  public override bool CastTo<T>(ref T target)
  {
    var type = typeof(T);
    if (type == typeof(Dictionary<string, object?>))
    {
      Dictionary<string, object?> dictionary = Unwrap();
      target = (T)(object)dictionary;
      return true;
    }

    // call CastToModelObject for Rhino8+ model objects
    return CastToModelObject(ref target);
  }

#if !RHINO8_OR_GREATER
  private bool CastToModelObject<T>(ref T _) => false;
#endif

  /// <summary>
  /// Flattens the value into a dictionary with concatenated keys
  /// </summary>
  /// <returns></returns>
  public Dictionary<string, SpecklePropertyGoo> Flatten()
  {
    Dictionary<string, SpecklePropertyGoo> flattenedProps = new();
    FlattenWorker(Value, flattenedProps);
    return flattenedProps;
  }

  private void FlattenWorker(Dictionary<string, ISpecklePropertyGoo> props,
    Dictionary<string, SpecklePropertyGoo> flattenedProps,
    string keyPrefix = "")
  {
    foreach (var kvp in props)
    {
      string newKey = string.IsNullOrEmpty(keyPrefix)
        ? kvp.Key
        : $"{keyPrefix}{Constants.PROPERTY_PATH_DELIMITER}{kvp.Key}";

      switch (kvp.Value)
      {
        case SpecklePropertyGroupGoo childProps:
          FlattenWorker(childProps.Value, flattenedProps, newKey);
          break;
        case SpecklePropertyGoo prop:
          flattenedProps.Add(newKey, prop);
          break;
      }
    }
  }

  private Dictionary<string, ISpecklePropertyGoo> WrapDictionary(
    Dictionary<string, object?> dict
    )
  {
    Dictionary<string, ISpecklePropertyGoo> wrappedDict = new();

    foreach (var kvp in dict)
    {
      ISpecklePropertyGoo? val;
      if (kvp.Value is Dictionary<string, object?> childDict)
      {
        SpecklePropertyGroupGoo childPropertyGroup = new();
        childPropertyGroup.CastFrom(childDict);
        val = childPropertyGroup;
      }
      else
      {
        SpecklePropertyGoo entry = new();
        entry.CastFrom(kvp.Value);
        val = entry;
      }

      wrappedDict.Add(kvp.Key, val);
    }

    return wrappedDict;
  }

  /// <summary>
  /// Unwraps the value into a Dictionary of equivalent structure
  /// </summary>
  /// <returns></returns>
  public Dictionary<string, object?> Unwrap()
  {
    Dictionary<string, object?> dict = UnwrapWorker(Value);
    return dict;
  }

  private Dictionary<string, object?> UnwrapWorker(
    Dictionary<string, ISpecklePropertyGoo> properties
    )
  {
    Dictionary<string, object?> dict = new();
    foreach (var kvp in properties)
    {
      object? val = kvp.Value is SpecklePropertyGroupGoo propertyGroup ?
        UnwrapWorker(propertyGroup.Value) : 
        kvp.Value is SpecklePropertyGoo property ? 
        property.Value : null;
      dict.Add(kvp.Key, val);
    }

    return dict;
  }

  public override int GetHashCode() => base.GetHashCode();
}

public class SpecklePropertyGroupParam : GH_Param<SpecklePropertyGroupGoo>
{
  public override Guid ComponentGuid => new("AF4757C3-BA33-4ACD-A92B-C80356043129");
  protected override Bitmap Icon => Resources.speckle_param_properties;

  public SpecklePropertyGroupParam()
    : this(GH_ParamAccess.item) { }

  public SpecklePropertyGroupParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpecklePropertyGroupParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpecklePropertyGroupParam(GH_ParamAccess access)
    : base(
      "Speckle Properties",
      "SP",
      "Represents a set of Speckle Properties",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }
}
