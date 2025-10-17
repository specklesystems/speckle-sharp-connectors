using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToSpeckle.Properties;
using Speckle.Objects.Data;
using Speckle.Objects.Other;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Helper class to proxify the levels. Runs over for every element with their LevelId prop.
/// We can handle bottom-top levels for elements later only if it is asked.
/// </summary>
public class LevelUnpacker
{
  private readonly LevelExtractor _levelExtractor;
  private readonly PropertiesExtractor _propertiesExtractor;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public LevelUnpacker(
    LevelExtractor levelExtractor,
    PropertiesExtractor propertiesExtractor,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _levelExtractor = levelExtractor;
    _propertiesExtractor = propertiesExtractor;
    _converterSettings = converterSettings;
  }

  public List<LevelProxy> Unpack(List<Element> elements)
  {
    Dictionary<string, LevelProxy> levelProxies = new();
    foreach (var element in elements)
    {
      // NOTE: Use level.UniqueId (not element.LevelId) as key
      // face-based instances don't have a valid element.LevelId, hence all the changes in the LevelExtractor
      var level = _levelExtractor.GetLevel(element);
      if (level is null)
      {
        continue;
      }

      string levelKey = level.UniqueId;

      if (levelProxies.TryGetValue(levelKey, out LevelProxy? levelProxy))
      {
        levelProxy.objects.Add(element.UniqueId);
      }
      else
      {
        var levelDataObject = new DataObject()
        {
          name = level.Name,
          displayValue = [],
          properties = _propertiesExtractor.GetProperties(level)
        };
        var unitSettings = _converterSettings.Current.Document.GetUnits();
        var lengthUnitType = unitSettings.GetFormatOptions(SpecTypeId.Length).GetUnitTypeId();
        levelDataObject["elevation"] = UnitUtils.ConvertFromInternalUnits(level.Elevation, lengthUnitType);
        levelDataObject["units"] = _converterSettings.Current.SpeckleUnits;

        levelProxies[levelKey] = new LevelProxy()
        {
          applicationId = level.UniqueId,
          objects = [element.UniqueId],
          value = levelDataObject
        };
      }
    }

    return levelProxies.Values.ToList();
  }
}
