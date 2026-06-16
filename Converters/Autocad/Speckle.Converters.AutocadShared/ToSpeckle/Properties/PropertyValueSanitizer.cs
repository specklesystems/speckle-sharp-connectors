using System.Collections;

namespace Speckle.Converters.AutocadShared.ToSpeckle;

public static class PropertyValueSanitizer
{
  public static Dictionary<string, object?> Sanitize(Dictionary<string, object?> properties) =>
    properties.ToDictionary(kvp => kvp.Key, kvp => Sanitize(kvp.Value));

  public static object? Sanitize(object? value)
  {
    if (value is null)
    {
      return null;
    }

    if (value is ADB.ObjectId objectId)
    {
      return objectId == ADB.ObjectId.Null ? null : objectId.Handle.Value.ToString();
    }

    Type valueType = value.GetType();
    if (
      valueType.IsPrimitive
      || value is string
      || value is decimal
      || value is Guid
      || value is DateTime
      || value is DateTimeOffset
      || value is TimeSpan
    )
    {
      return value;
    }

    if (valueType.IsEnum)
    {
      return value.ToString();
    }

    if (value is IDictionary dictionary)
    {
      Dictionary<string, object?> sanitized = new(dictionary.Count);
      foreach (DictionaryEntry entry in dictionary)
      {
        sanitized[entry.Key.ToString() ?? string.Empty] = Sanitize(entry.Value);
      }

      return sanitized;
    }

    if (value is IEnumerable enumerable)
    {
      List<object?> sanitized = new();
      foreach (object? item in enumerable)
      {
        sanitized.Add(Sanitize(item));
      }

      return sanitized;
    }

    return value.ToString();
  }
}
