using System.Globalization;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.InterfaceGenerator;

namespace Speckle.Converter.Navisworks.Services;

[GenerateAutoInterface]
public class PropertyConverter(IUiUnitsCache uiUnitsCache) : IPropertyConverter
{
  public void Reset() => uiUnitsCache.Reset();

  public object? ConvertPropertyValue(NAV.VariantData? value, NAV.Units modelUnits, string propDisplayName) =>
    value == null
      ? null
      : _handlers.TryGetValue(value.DataType, out var f)
        ? f(value, (modelUnits, propDisplayName))
        : value.DataType is NAV.VariantDataType.None or NAV.VariantDataType.Point2D
          ? null
          : value.ToString();

  private readonly Dictionary<
    NAV.VariantDataType,
    Func<NAV.VariantData, (NAV.Units model, string name), object?>
  > _handlers =
    new()
    {
      { NAV.VariantDataType.Boolean, (v, _) => v.ToBoolean() },
      { NAV.VariantDataType.DisplayString, (v, _) => v.ToDisplayString() },
      { NAV.VariantDataType.IdentifierString, (v, _) => v.ToIdentifierString() },
      { NAV.VariantDataType.Int32, (v, _) => v.ToInt32() },
      { NAV.VariantDataType.Double, (v, _) => v.ToDouble() },
      // Angle as a dictionary with units
      { NAV.VariantDataType.DoubleAngle, (v, t) => NumObj(t.name, v.ToDoubleAngle(), "Degrees") },
      // Length → dictionary in UI units
      {
        NAV.VariantDataType.DoubleLength,
        (v, t) =>
        {
          var uiUnits = uiUnitsCache.Ensure();

          var k = NAV.UnitConversion.ScaleFactor(t.model, uiUnits);
          return NumObj(t.name, v.ToDoubleLength() * k, UnitLabels.Linear(uiUnits));
        }
      },
      // Area → dictionary in UI units^2
      {
        NAV.VariantDataType.DoubleArea,
        (v, t) =>
        {
          var uiUnits = uiUnitsCache.Ensure();
          var k = NAV.UnitConversion.ScaleFactor(t.model, uiUnits);
          k *= k;
          return NumObj(t.name, v.ToDoubleArea() * k, UnitLabels.Area(uiUnits));
        }
      },
      // Volume → dictionary in UI units^3
      {
        NAV.VariantDataType.DoubleVolume,
        (v, t) =>
        {
          var uiUnits = uiUnitsCache.Ensure();
          var k = NAV.UnitConversion.ScaleFactor(t.model, uiUnits);
          k = k * k * k;
          return NumObj(t.name, v.ToDoubleVolume() * k, UnitLabels.Volume(uiUnits));
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
          var uiUnits = uiUnitsCache.Ensure();
          var k = NAV.UnitConversion.ScaleFactor(t.model, uiUnits);
          var p = v.ToPoint3D();

          return new Speckle.Objects.Geometry.Point(p.X * k, p.Y * k, p.Z * k, UnitLabels.Linear(uiUnits));
        }
      },
    };

  private static Dictionary<string, object> NumObj(string name, double value, string units) =>
    new()
    {
      ["name"] = name,
      ["value"] = value,
      ["units"] = units,
    };
}
