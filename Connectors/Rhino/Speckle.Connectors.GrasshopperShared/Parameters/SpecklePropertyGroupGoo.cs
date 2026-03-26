using Grasshopper.Kernel.Types;
using Rhino.DocObjects;
using Speckle.Connectors.GrasshopperShared.HostApp;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// The Speckle Property Group Goo is a dictionary of (key, speckle property or property group).
/// Flattened property group keys are the concatenated strings of all nested keys with the property delimiter
/// </summary>
public partial class SpecklePropertyGroupGoo : GH_Goo<Dictionary<string, ISpecklePropertyGoo>>, ISpecklePropertyGoo
{
  public override IGH_Goo Duplicate() => Clone();

  public override string ToString() => $"Speckle Properties : ({Value.Count})";

  public override bool IsValid => true;
  public override string TypeName => "Speckle property group goo";
  public override string TypeDescription => "Speckle property group goo";

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

  public bool Equals(ISpecklePropertyGoo other)
  {
    if (other is not SpecklePropertyGroupGoo propGroup)
    {
      return false;
    }

    if (Value.Keys.Count != propGroup.Value.Keys.Count)
    {
      return false;
    }

    var thisProps = Flatten();
    var otherProps = propGroup.Flatten();
    foreach (var entry in thisProps)
    {
      if (!otherProps.TryGetValue(entry.Key, out SpecklePropertyGoo? otherValue) || !entry.Value.Equals(otherValue))
      {
        return false;
      }
    }

    return true;
  }

  /// <summary>
  /// Adds this property group to the input object attributes
  /// </summary>
  /// <param name="atts"></param>
  public void AssignToObjectAttributes(ObjectAttributes atts)
  {
    Dictionary<string, SpecklePropertyGoo> flattenedProps = Flatten();
    foreach (var kvp in flattenedProps)
    {
      atts.SetUserString(kvp.Key, kvp.Value.Value?.ToString() ?? "");
    }
  }

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

  private void FlattenWorker(
    Dictionary<string, ISpecklePropertyGoo> props,
    Dictionary<string, SpecklePropertyGoo> flattenedProps,
    string keyPrefix = ""
  )
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

  private Dictionary<string, ISpecklePropertyGoo> WrapDictionary(Dictionary<string, object?> dict)
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

  private Dictionary<string, object?> UnwrapWorker(Dictionary<string, ISpecklePropertyGoo> properties)
  {
    Dictionary<string, object?> dict = new();
    foreach (var kvp in properties)
    {
      object? val = null;
      switch (kvp.Value)
      {
        case SpecklePropertyGroupGoo propertyGroup:
          val = UnwrapWorker(propertyGroup.Value);
          break;
        case SpecklePropertyGoo property:
          switch (property.Value)
          {
            case Rhino.Geometry.Plane:
            case Rhino.Geometry.Vector3d:
            case Rhino.Geometry.Interval:
              val = SpeckleConversionContext.Current.ConvertToSpeckle(property.Value);
              break;
            default:
              val = property.Value;
              break;
          }
          break;
      }

      dict.Add(kvp.Key, val);
    }

    return dict;
  }

  /// <summary>
  /// Performs a deep clone of the property group to prevent mutating upstream Grasshopper data.
  /// </summary>
  /// <returns>
  /// A new SpecklePropertyGroupGoo instance with cloned nested properties.
  /// Needed since adding support for dot notation [CNX-3179]
  /// </returns>
  public SpecklePropertyGroupGoo Clone()
  {
    var newDict = new Dictionary<string, ISpecklePropertyGoo>();
    foreach (var kvp in Value)
    {
      newDict[kvp.Key] = kvp.Value is SpecklePropertyGroupGoo group ? group.Clone() : kvp.Value;
    }
    return new SpecklePropertyGroupGoo(newDict);
  }

  /// <summary>
  /// Sets a property value using dot-notation path traversal. Creates nested groups if they do not exist.
  /// </summary>
  /// <param name="path">The dot-notation property path.</param>
  /// <param name="value">The Speckle property to set.</param>
  public void SetValueByPath(string path, ISpecklePropertyGoo value)
  {
    string[] parts = path.Split('.');
    var current = Value;

    for (int i = 0; i < parts.Length - 1; i++)
    {
      string part = parts[i];
      if (!current.TryGetValue(part, out var existing) || existing is not SpecklePropertyGroupGoo group)
      {
        group = new SpecklePropertyGroupGoo();
        current[part] = group;
      }
      current = group.Value;
    }

    current[parts[^1]] = value;
  }

  /// <summary>
  /// Removes a property value using dot-notation path traversal.
  /// </summary>
  /// <param name="path">The dot-notation property path.</param>
  public void RemoveValueByPath(string path)
  {
    string[] parts = path.Split('.');
    var current = Value;

    for (int i = 0; i < parts.Length - 1; i++)
    {
      string part = parts[i];
      if (!current.TryGetValue(part, out var existing) || existing is not SpecklePropertyGroupGoo group)
      {
        return; // path does not exist
      }

      current = group.Value;
    }

    current.Remove(parts[^1]);
  }

  /// <summary>
  /// Retrieves a property value using dot-notation path traversal. Attempts exact match first.
  /// </summary>
  /// <param name="path">The dot-notation property path.</param>
  /// <returns>The matching property goo if found, otherwise null.</returns>
  public ISpecklePropertyGoo? GetValueByPath(string path)
  {
    // attempt exact match first for literal dots in native keys
    if (Value.TryGetValue(path, out var exactMatch))
    {
      return exactMatch;
    }

    string[] parts = path.Split('.');
    ISpecklePropertyGoo? current = this;

    foreach (var part in parts)
    {
      if (current is SpecklePropertyGroupGoo group && group.Value.TryGetValue(part, out var next))
      {
        current = next;
      }
      else
      {
        return null; // current is not a dictionary, or path is invalid
      }
    }

    return current;
  }

  public override int GetHashCode() => base.GetHashCode();
}
