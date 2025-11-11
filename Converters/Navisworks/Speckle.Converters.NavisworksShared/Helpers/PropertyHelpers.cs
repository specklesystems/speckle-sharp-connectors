using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.Navisworks.Api.Interop;
using static Autodesk.Navisworks.Api.Interop.LcUOption;

namespace Speckle.Converter.Navisworks.Helpers;

// context passed to handlers
public readonly struct UnitsCtx(NAV.Units model, NAV.Units ui, string unitLabel)
{
  public NAV.Units Model { get; } = model;
  public NAV.Units Ui { get; } = ui;
  public string UnitLabel { get; } = unitLabel;
}

public static class UiUnitsUtil
{
  // disp_units: 0=linear_format
  public static bool TryGetUiLinearUnits(out NAV.Units uiUnits)
  {
    using var opt = new LcUOptionLock();
    var root = GetRoot(opt);
    var disp = root.GetSubOptions("interface").GetSubOptions("disp_units");

    int code = -1;

    var v = new NAV.VariantData();
    disp.GetValue(0, v);
    var s = v.ToString();
    var colon = s.LastIndexOf(':');
    var open = s.IndexOf('(', colon + 1);
    if (colon >= 0 && open > colon && !int.TryParse(s.Substring(colon + 1, open - colon - 1), out code))
    {
      code = -1;
    }

    uiUnits = code switch
    {
      0 => NAV.Units.Kilometers,
      1 => NAV.Units.Meters,
      2 => NAV.Units.Centimeters,
      3 => NAV.Units.Millimeters,
      4 => NAV.Units.Micrometers,
      5 => NAV.Units.Miles,
      6 => NAV.Units.Miles,
      7 => NAV.Units.Yards,
      8 => NAV.Units.Yards,
      9 => NAV.Units.Feet,
      10 => NAV.Units.Feet,
      11 => NAV.Units.Feet,
      12 => NAV.Units.Inches,
      13 => NAV.Units.Inches,
      14 => NAV.Units.Mils,
      15 => NAV.Units.Microinches,
      _ => NAV.Units.Meters
    };

    return code >= 0;
  }

  internal static double LinearFactor(NAV.Units fromUnits, NAV.Units toUnits) =>
    NAV.UnitConversion.ScaleFactor(fromUnits, toUnits);

  internal static double AreaFactor(NAV.Units fromUnits, NAV.Units toUnits)
  {
    var k = LinearFactor(fromUnits, toUnits);
    return k * k;
  }

  internal static double VolumeFactor(NAV.Units fromUnits, NAV.Units toUnits)
  {
    var k = LinearFactor(fromUnits, toUnits);
    return k * k * k;
  }
}

public static class PropertyHelpers
{
  private static readonly HashSet<string> s_excludedCategories = ["Geometry", "Metadata"];

  internal static object? ConvertPropertyValue(
    NAV.VariantData? value,
    NAV.Units modelUnits,
    string propDisplayName,
    string propInternalName
  ) =>
    value == null
      ? null
      : s_handlers.TryGetValue(value.DataType, out var f)
        ? f(value, (modelUnits, propDisplayName, propInternalName))
        : value.DataType is NAV.VariantDataType.None or NAV.VariantDataType.Point2D
          ? null
          : value.ToString();

  /// <summary>
  /// Adds a property to an object (either a Base object or a Dictionary) if the value is not null or empty.
  /// </summary>
  /// <param name="baseObject">The object to which the property is to be added. Can be either a Base object or a Dictionary.</param>
  /// <param name="propertyName">The name of the property to add.</param>
  /// <param name="value">The value of the property.</param>
  internal static void AddPropertyIfNotNullOrEmpty(object baseObject, string propertyName, object? value)
  {
    switch (value)
    {
      case null:
        break; // Do not add null values
      case string stringValue:
      {
        if (!string.IsNullOrEmpty(stringValue))
        {
          AssignProperty(baseObject, propertyName, value);
        }

        break;
      }
      default:
        AssignProperty(baseObject, propertyName, value);
        break;
    }
  }

  private static readonly Dictionary<
    NAV.VariantDataType,
    Func<NAV.VariantData, (NAV.Units model, string name, string internalName), object?>
  > s_handlers =
    new()
    {
      { NAV.VariantDataType.Boolean, (v, _) => v.ToBoolean() },
      { NAV.VariantDataType.DisplayString, (v, _) => v.ToDisplayString() },
      { NAV.VariantDataType.IdentifierString, (v, _) => v.ToIdentifierString() },
      { NAV.VariantDataType.Int32, (v, _) => v.ToInt32() },
      { NAV.VariantDataType.Double, (v, _) => v.ToDouble() },
      // Angle as dictionary with units
      { NAV.VariantDataType.DoubleAngle, (v, t) => NumObj(t.name, v.ToDoubleAngle(), "Degrees", t.internalName) },
      // Length → dictionary in UI units
      {
        NAV.VariantDataType.DoubleLength,
        (v, t) =>
        {
          var ui = UiUnitsUtil.TryGetUiLinearUnits(out var uiUnits) ? uiUnits : t.model;
          var k = NAV.UnitConversion.ScaleFactor(t.model, ui);
          return NumObj(t.name, v.ToDoubleLength() * k, UnitLabels.Linear(ui), t.internalName);
        }
      },
      // Area → dictionary in UI units^2
      {
        NAV.VariantDataType.DoubleArea,
        (v, t) =>
        {
          var ui = UiUnitsUtil.TryGetUiLinearUnits(out var uiUnits) ? uiUnits : t.model;
          var k = NAV.UnitConversion.ScaleFactor(t.model, ui);
          k *= k;
          return NumObj(t.name, v.ToDoubleArea() * k, UnitLabels.Area(ui), t.internalName);
        }
      },
      // Volume → dictionary in UI units^3
      {
        NAV.VariantDataType.DoubleVolume,
        (v, t) =>
        {
          var ui = UiUnitsUtil.TryGetUiLinearUnits(out var uiUnits) ? uiUnits : t.model;
          var k = NAV.UnitConversion.ScaleFactor(t.model, ui);
          k = k * k * k;
          return NumObj(t.name, v.ToDoubleVolume() * k, UnitLabels.Volume(ui), t.internalName);
        }
      },
      { NAV.VariantDataType.DateTime, (v, _) => v.ToDateTime().ToString(CultureInfo.InvariantCulture) },
      { NAV.VariantDataType.NamedConstant, (v, _) => v.ToNamedConstant().DisplayName },
      { NAV.VariantDataType.None, (_, _) => null },
      { NAV.VariantDataType.Point2D, (_, _) => null },
      {
        NAV.VariantDataType.Point3D,
        (v, t) =>
        {
          var ui = UiUnitsUtil.TryGetUiLinearUnits(out var uiUnits) ? uiUnits : t.model;
          var k = NAV.UnitConversion.ScaleFactor(t.model, ui);
          var p = v.ToPoint3D();

          return new Speckle.Objects.Geometry.Point(p.X * k, p.Y * k, p.Z * k, UnitLabels.Linear(ui));
        }
      }
    };

  private static Dictionary<string, object> NumObj(
    string name,
    double value,
    string units,
    string? internalDef = null
  ) =>
    new()
    {
      ["name"] = name,
      ["value"] = value,
      ["units"] = units,
      ["internalDefinitionName"] = internalDef ?? string.Empty
    };

  /// <summary>
  /// Helper method to assign the property to the base object or dictionary.
  /// </summary>
  private static void AssignProperty(object baseObject, string propertyName, object value)
  {
    switch (baseObject)
    {
      case SSM.Base baseObj:
        baseObj[propertyName] = value;
        break;
      case Dictionary<string, object> baseDict:
        baseDict[propertyName] = value;
        break;
      default:
        throw new ArgumentException("Unsupported object type", nameof(baseObject));
    }
  }

  /// <summary>
  /// Sanitizes property names by replacing invalid characters with underscores.
  /// </summary>
  internal static string SanitizePropertyName(string name) =>
    // Regex pattern from speckle-sharp/Core/Core/Models/DynamicBase.cs IsPropNameValid
    name == "Item"
      // Item is a reserved term for Indexed Properties: https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/indexers/using-indexers
      ? "Item"
      : Regex.Replace(name, @"[\.\/\s]", "_");

  internal static bool IsCategoryToBeSkipped(NAV.PropertyCategory propertyCategory) =>
    s_excludedCategories.Contains(propertyCategory.DisplayName);
}

internal static class UnitLabels
{
  internal static string Linear(NAV.Units u) =>
    u switch
    {
      NAV.Units.Kilometers => "Kilometres",
      NAV.Units.Meters => "Metres",
      NAV.Units.Centimeters => "Centimetres",
      NAV.Units.Millimeters => "Millimetres",
      NAV.Units.Micrometers => "Micrometres",
      NAV.Units.Miles => "Miles",
      NAV.Units.Yards => "Yards",
      NAV.Units.Feet => "Feet",
      NAV.Units.Inches => "Inches",
      NAV.Units.Mils => "Mils",
      NAV.Units.Microinches => "MicroInches",
      _ => "Metres"
    };

  public static string Area(NAV.Units u) => $"Square {Linear(u).ToLower()}";

  public static string Volume(NAV.Units u) => $"Cubic {Linear(u).ToLower()}";
}

internal static class UiUnitsCache
{
  private static NAV.Units? s_ui;

  public static NAV.Units Ensure()
  {
    if (s_ui.HasValue)
    {
      return s_ui.Value;
    }

    UiUnitsUtil.TryGetUiLinearUnits(out var ui);
    s_ui = ui;
    return s_ui.Value;
  }

  public static void Reset() => s_ui = null;
}
