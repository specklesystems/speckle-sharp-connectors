using ArcGIS.Core.Data;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

public class AttributesToSpeckleConverter : ITypedConverter<Row, Base>
{
  public AttributesToSpeckleConverter() { }

  public Base Convert(Row target)
  {
    Base attributes = new();
    IReadOnlyList<Field> fields = target.GetFields();
    foreach (Field field in fields)
    {
      if (field.FieldType == FieldType.Geometry)
      {
        continue; // ignore fields with geometry
      }
      else
      {
        attributes[field.Name] = FieldValueToSpeckle(target, field);
      }
    }

    return attributes;
  }

  private object? FieldValueToSpeckle(Row row, Field field)
  {
    switch (field.FieldType)
    {
      // these FieldTypes are not properly supported through API
      case FieldType.Raster:
      case FieldType.Blob:
      case FieldType.XML:
        return null;

      case FieldType.DateOnly:
      case FieldType.TimeOnly:
      case FieldType.TimestampOffset:
        return row[field.Name]?.ToString();

      default:
        return row[field.Name];
    }
  }
}
