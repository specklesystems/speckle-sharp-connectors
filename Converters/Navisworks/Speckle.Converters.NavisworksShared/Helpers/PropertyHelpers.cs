using System.Globalization;
using Speckle.Objects.Geometry;

namespace Speckle.Converter.Navisworks.Helpers;

public class PropertyHelpers
{
  private static dynamic ConvertPropertyValue(NAV.VariantData value)
  {
    dynamic propertyValue = null;

    NAV.VariantDataType type = value.DataType;

    switch (type)
    {
      case NAV.VariantDataType.Boolean:
        propertyValue = value.ToBoolean();
        break;
      case NAV.VariantDataType.DisplayString:
        propertyValue = value.ToDisplayString();
        break;
      case NAV.VariantDataType.IdentifierString:
        propertyValue = value.ToIdentifierString();
        break;
      case NAV.VariantDataType.Int32:
        propertyValue = value.ToInt32();
        break;
      case NAV.VariantDataType.Double:
        propertyValue = value.ToDouble();
        break;
      case NAV.VariantDataType.DoubleAngle:
        propertyValue = value.ToDoubleAngle();
        break;
      case NAV.VariantDataType.DoubleArea:
        propertyValue = value.ToDoubleArea();
        break;
      case NAV.VariantDataType.DoubleLength:
        propertyValue = value.ToDoubleLength();
        break;
      case NAV.VariantDataType.DoubleVolume:
        propertyValue = value.ToDoubleVolume();
        break;
      case NAV.VariantDataType.DateTime:
        propertyValue = value.ToDateTime().ToString(CultureInfo.InvariantCulture);
        break;
      case NAV.VariantDataType.NamedConstant:
        propertyValue = value.ToNamedConstant().DisplayName;
        break;
      case NAV.VariantDataType.Point3D:
        NAV.Point3D point = value.ToPoint3D();
        Point pointProperty = new(point.X, point.Y, point.Z);
        propertyValue = pointProperty.ToString();
        break;
      case NAV.VariantDataType.None:
        break;
      case NAV.VariantDataType.Point2D:
        break;
      default:
        propertyValue = value.ToString();
        break;
    }

    return propertyValue;
  }

  /// <summary>
  /// Adds a property to an object (either a Base object or a Dictionary) if the value is not null or empty.
  /// </summary>
  /// <param name="baseObject">The object to which the property is to be added. Can be either a Base object or a Dictionary.</param>
  /// <param name="propertyName">The name of the property to add.</param>
  /// <param name="value">The value of the property.</param>
  internal static void AddPropertyIfNotNullOrEmpty(object baseObject, string propertyName, object value)
  {
    switch (value)
    {
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
}
