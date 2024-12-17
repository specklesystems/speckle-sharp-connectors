namespace Speckle.Converters.ArcGIS3.ToSpeckle.Helpers;

public sealed class PropertiesExtractor
{
  public PropertiesExtractor() { }

  public Dictionary<string, object?> GetProperties(AC.CoreObjectsBase coreObjectsBase)
  {
    switch (coreObjectsBase)
    {
      case ACD.Row row:
        return GetRowFields(row);
    }

    return new();
  }

  public Dictionary<string, object?> GetRowFields(ACD.Row row)
  {
    Dictionary<string, object?> rowFields = new();
    foreach (ACD.Field field in row.GetFields())
    {
      // POC: do not set null values
      // POC: we are not filtering by the layer visible fields
      if (FieldValueToSpeckle(row, field) is object value)
      {
        rowFields[field.Name] = value;
      }
    }

    return rowFields;
  }

  private object? FieldValueToSpeckle(ACD.Row row, ACD.Field field)
  {
    switch (field.FieldType)
    {
      // these FieldTypes are not properly supported through API
      case ACD.FieldType.Geometry:
      case ACD.FieldType.Raster:
      case ACD.FieldType.Blob:
      case ACD.FieldType.XML:
        return null;

      case ACD.FieldType.DateOnly:
      case ACD.FieldType.TimeOnly:
      case ACD.FieldType.TimestampOffset:
        return row[field.Name]?.ToString();

      default:
        return row[field.Name];
    }
  }
}
