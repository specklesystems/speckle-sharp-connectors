namespace Speckle.Converters.Civil3dShared.ToSpeckle;

/// <summary>
/// Extracts parameters out from an element. Expects to be scoped per operation.
/// </summary>
public class PartDataExtractor
{
  /// POC: Note that we're abusing dictionaries in here because we've yet to have a simple way to serialize non-base derived classes (or structs?)
  public PartDataExtractor() { }

  /// <summary>
  /// Extracts part data out from an entity. Expects to be scoped per operation.
  /// </summary>
  /// <param name="entity"></param>
  /// <returns></returns>
  public Dictionary<string, object?>? GetPartData(ADB.Entity entity)
  {
    return entity switch
    {
      CDB.Part part => ParsePartData(part.PartData),
      CDB.PressurePart pressurePart => ParsePartData(pressurePart.PartData),
      _ => null,
    };
  }

  private Dictionary<string, object?> ParsePartData(CDB.PressureNetworkPartData partData)
  {
    var result = new Dictionary<string, object?>();
    foreach (CDB.PressurePartProperty prop in partData)
    {
      if (!prop.HasValue)
      {
        continue; // don't send null props
      }

      if (!result.ContainsKey(prop.DisplayName))
      {
        result.Add(prop.DisplayName, prop.Value);
      }
    }

    return result;
  }

  private Dictionary<string, object?> ParsePartData(CDB.PartDataRecord partData)
  {
    var result = new Dictionary<string, object?>();

    foreach (CDB.PartDataField field in partData.GetAllDataFields())
    {
      var value = GetValue(field);

      if (value is null)
      {
        continue; // don't send null props
      }

      string fieldName = field.Context.ToString(); // we're using the context for the field name because it is more human-readable than the name prop

      var fieldDictionary = new Dictionary<string, object?>()
      {
        ["value"] = value,
        ["name"] = field.Name,
        ["context"] = fieldName,
        ["units"] = field.Units,
      };

      if (!result.ContainsKey(fieldName))
      {
        result.Add(fieldName, fieldDictionary);
      }
    }

    return result;
  }

  private object? GetValue(CDB.PartDataField field)
  {
    switch (field.DataType)
    {
      case CDB.PartCatalogDataType.Double:
        return field.IsFromList
          ? GetValueListGeneric<double>(field.ValueList)
          : field.IsFromRange
            ? GetValueRangeGeneric<double>(field.ValueRange)
            : field.Value as double?;
      case CDB.PartCatalogDataType.Int:
        return field.IsFromList
          ? GetValueListGeneric<int>(field.ValueList)
          : field.IsFromRange
            ? GetValueRangeGeneric<int>(field.ValueRange)
            : field.Value as int?;
      case CDB.PartCatalogDataType.Bool:
        return field.IsFromList
          ? GetValueListGeneric<bool>(field.ValueList)
          : field.IsFromRange
            ? GetValueRangeGeneric<bool>(field.ValueRange)
            : field.Value as bool?;
      default:
        return field.IsFromList
          ? GetValueListGeneric<string>(field.ValueList)
          : field.IsFromRange
            ? GetValueRangeGeneric<string>(field.ValueRange)
            : field.Value.ToString();
    }
  }

  private List<TResult>? GetValueListGeneric<TResult>(CDB.PartDataList list)
  {
    if (list == null || list.Count == 0)
    {
      return default;
    }

    List<TResult> result = new();
    for (int i = 0; i < list.Count; i++)
    {
      if (list[i] is TResult item)
      {
        result.Add(item);
      }
    }

    return result;
  }

  private List<TResult>? GetValueRangeGeneric<TResult>(CDB.PartDataRange range)
  {
    if (range == null)
    {
      return default;
    }

    if (range.RangeMin is TResult min && range.RangeMax is TResult max)
    {
      return new() { min, max };
    }

    return default;
  }
}
