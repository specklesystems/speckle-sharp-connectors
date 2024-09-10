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
        // TODO: currently we are setting raster, blob, and xml fields to null with this logic. Why are these sent as null and not skipped over?
        attributes[field.Name] = FieldValueToSpeckle(target, field);
      }
    }

    return attributes;
  }

  // TODO: often skipping over geometry, raster, blob, and xml fields. This happens in vector layer conversion as well. Why are we returning null here? We should encapsulate this in a field converter util.
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
