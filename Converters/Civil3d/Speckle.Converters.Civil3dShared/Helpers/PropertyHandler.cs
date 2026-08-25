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
        || e is ArgumentException
        || e is Autodesk.AutoCAD.Runtime.Exception // eNotApplicable
        || e is Autodesk.Civil.CivilException
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

  /// <summary>Autodesk's literal DISPLAY text for "this definition has no unit" — UI text, never a unit.</summary>
  public const string NO_UNIT_DISPLAY = "(none)";

  /// <summary>The genuine unit display text behind a throwing <c>UnitType</c> getter, or null when there is
  /// none. Two distinct "no unit" signals collapse to null here: the getter throwing (units not applicable to
  /// the definition) and <see cref="NO_UNIT_DISPLAY"/>. The bundle's `unit` columns — definition rows AND value
  /// rows — plus the set_key recipe all contract on real-unit-or-absent, so every capture site must agree
  /// [ENG-9360].</summary>
  public string? TryGetUnitDisplay(Func<string> getDisplayName) =>
    TryGetValue(getDisplayName, out string? display) ? NormalizeUnitDisplay(display) : null;

  /// <summary>The <see cref="TryGetUnitDisplay"/> filter for a unit display string already in hand (no
  /// throwing getter to guard) — same sentinel, one definition of it.</summary>
  public static string? NormalizeUnitDisplay(string? display) =>
    display is { Length: > 0 } && display != NO_UNIT_DISPLAY ? display : null;
}
