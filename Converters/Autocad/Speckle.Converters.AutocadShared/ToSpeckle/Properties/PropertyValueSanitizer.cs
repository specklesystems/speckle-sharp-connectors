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

    // decimal is not IsPrimitive and the Speckle V2 ObjectSerializer has no decimal case -> emit a serializer-safe double.
    if (value is decimal decimalValue)
    {
      return (double)decimalValue;
    }

    Type valueType = value.GetType();

    // Pass through only types the Speckle V2 ObjectSerializer accepts directly
    // (primitives, string, Guid, DateTime). DateTimeOffset/TimeSpan are NOT supported
    // and fall through to ToString() at the end.
    if (valueType.IsPrimitive || value is string || value is Guid || value is DateTime)
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
