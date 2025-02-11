using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Extensions;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle.Properties;

/// <summary>
/// For retrieving general properties used for business intelligence on the class, that are not already captured in parameters.
/// </summary>
public class ClassPropertiesExtractor
{
  private readonly ITypedConverter<DB.Level, Dictionary<string, object>> _levelConverter;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  private readonly Dictionary<DB.WorksetId, string> _worksetCache = new();
  private readonly Dictionary<DB.ElementId, Dictionary<string, object>> _levelCache = new();

  public ClassPropertiesExtractor(
    ITypedConverter<DB.Level, Dictionary<string, object>> levelConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _levelConverter = levelConverter;
    _converterSettings = converterSettings;
  }

  public Dictionary<string, object?> GetClassProperties(DB.Element element)
  {
    Dictionary<string, object?> elementPropertiesDict = ExtractElementProperties(element);
    switch (element)
    {
      case DBA.Room room:
        AddRoomProperties(room, elementPropertiesDict);
        break;

      default:
        break;
    }

    return elementPropertiesDict;
  }

  // gets the properties on the db.element class
  private Dictionary<string, object?> ExtractElementProperties(DB.Element element)
  {
    Dictionary<string, object?> elementProperties =
      new()
      {
        { "elementId", element.Id.ToString()! },
        { "builtInCategory", element.Category?.GetBuiltInCategory().ToString() },
        { "worksetId", element.WorksetId.ToString() }
      };

    // get workset name
    if (!_worksetCache.TryGetValue(element.WorksetId, out var worksetName))
    {
      DB.Workset workset = _converterSettings.Current.Document.GetWorksetTable().GetWorkset(element.WorksetId);
      worksetName = workset.Name;
      _worksetCache[element.WorksetId] = worksetName;
    }
    elementProperties.Add("worksetName", worksetName);

    // get level, if any
    if (element.LevelId != DB.ElementId.InvalidElementId)
    {
      if (
        !_levelCache.TryGetValue(element.LevelId, out var convertedLevel)
        && element.Document.GetElement(element.LevelId) is DB.Level level
      )
      {
        convertedLevel = _levelConverter.Convert(level);
        _levelCache[element.LevelId] = convertedLevel;
      }

      elementProperties.Add("Level", convertedLevel);
    }

    return elementProperties;
  }

  // gets properties specific to room class
  private void AddRoomProperties(DBA.Room room, Dictionary<string, object?> elementPropertiesDict)
  {
    string numberKey = "number";
    if (!elementPropertiesDict.ContainsKey(numberKey))
    {
      elementPropertiesDict.Add(numberKey, room.Number);
    }
  }
}
