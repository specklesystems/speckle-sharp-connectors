using ArcGIS.Core.Data;

namespace Speckle.Converters.ArcGIS3.Utils;

public static class GISAttributeFieldType
{
  public const string GUID_TYPE = "Guid";
  public const string OID = "Oid"; // object identifier: int
  public const string STRING_TYPE = "String";
  public const string FLOAT_TYPE = "Float"; // single-precision floating point number
  public const string INTEGER_TYPE = "Integer"; // 32-bit int
  public const string BIGINTEGER = "BigInteger"; // 64-bit int
  public const string SMALLINTEGER = "SmallInteger"; // 16-bit int
  public const string DOUBLE_TYPE = "Double";
  public const string DATETIME = "DateTime";
  public const string DATEONLY = "DateOnly";
  public const string TIMEONLY = "TimeOnly";
  public const string TIMESTAMPOFFSET = "TimeStampOffset";
  public const string BOOL = "Bool"; // not supported in ArcGIS, only in QGIS

  public static FieldType FieldTypeToNative(object fieldType)
  {
    if (fieldType is string fieldStringType)
    {
      return fieldStringType switch
      {
        GUID_TYPE => FieldType.GUID,
        OID => FieldType.OID,
        STRING_TYPE => FieldType.String,
        FLOAT_TYPE => FieldType.Single,
        INTEGER_TYPE => FieldType.Integer,
        BIGINTEGER => FieldType.BigInteger,
        SMALLINTEGER => FieldType.SmallInteger,
        DOUBLE_TYPE => FieldType.Double,
        DATETIME => FieldType.Date,
        DATEONLY => FieldType.DateOnly,
        TIMEONLY => FieldType.TimeOnly,
        TIMESTAMPOFFSET => FieldType.String, // sending and receiving as stings
        BOOL => FieldType.String, // not supported in ArcGIS, converting to string
        _ => throw new ArgumentOutOfRangeException(nameof(fieldType)),
      };
    }
    // old way:
    return (FieldType)(int)(long)fieldType;
  }

  public static object? SpeckleValueToNativeFieldType(FieldType fieldType, object? value)
  {
    // Geometry: ignored
    // Blob, Raster, TimestampOffset, XML: converted to String (field type already converted to String on Send)
    switch (fieldType)
    {
      case FieldType.GUID:
        return value;
      case FieldType.OID:
        return value;
    }

    if (value != null)
    {
      try
      {
        static object? GetValue(string? s, Func<string, object> func) => s is null ? null : func(s);
        return fieldType switch
        {
          FieldType.String => Convert.ToString(value),
          FieldType.Single => Convert.ToSingle(value),
          FieldType.Integer => Convert.ToInt32(value), // need this step because sent "ints" seem to be received as "longs"
          FieldType.BigInteger => Convert.ToInt64(value),
          FieldType.SmallInteger => Convert.ToInt16(value),
          FieldType.Double => Convert.ToDouble(value),
          FieldType.Date => GetValue(value.ToString(), s => DateTime.Parse(s, null)),
          FieldType.DateOnly => GetValue(value.ToString(), s => DateOnly.Parse(s, null)),
          FieldType.TimeOnly => GetValue(value.ToString(), s => TimeOnly.Parse(s, null)),
          _ => value,
        };
      }
      catch (Exception ex) when (ex is InvalidCastException or FormatException or ArgumentNullException)
      {
        return null;
      }
    }
    else
    {
      return null;
    }
  }

  public static FieldType GetFieldTypeFromRawValue(object? value)
  {
    // using "Blob" as a placeholder for unrecognized values/nulls.
    // Once all elements are iterated, FieldType.Blob will be replaced with FieldType.String if no better type found
    if (value is not null)
    {
      return value switch
      {
        string => FieldType.String,
        int => FieldType.Integer,
        long => FieldType.BigInteger,
        double => FieldType.Double,
        _ => FieldType.Blob,
      };
    }

    return FieldType.Blob;
  }
}
