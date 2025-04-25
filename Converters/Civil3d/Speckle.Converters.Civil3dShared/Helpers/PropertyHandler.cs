using Speckle.Sdk;

namespace Speckle.Converters.Civil3dShared.Helpers;

/// <summary>
/// Used to help with properties on classes that may throw exceptions when accessed
/// </summary>
public sealed class PropertyHandler
{
  public bool TryGetValue<T>(Func<T> getValue, out T? value)
  {
    try
    {
      value = getValue();
      return true;
    }
    catch (Exception e)
      when (e is InvalidOperationException
        || e is ArgumentException argEx && !argEx.IsFatal()
        || e is Autodesk.AutoCAD.Runtime.Exception acEx && !acEx.IsFatal() // eNotApplicable
        || e is Autodesk.Civil.CivilException civilEx && !civilEx.IsFatal()
      )
    {
      value = default;
      return false;
    }
  }

  public bool TryAddToDictionary<T>(Dictionary<string, object?> dict, string key, Func<T> getValue)
  {
    if (dict.ContainsKey(key))
    {
      return false;
    }

    if (TryGetValue<T>(getValue, out var value))
    {
      dict.Add(key, value);
      return true;
    }

    return false;
  }
}
