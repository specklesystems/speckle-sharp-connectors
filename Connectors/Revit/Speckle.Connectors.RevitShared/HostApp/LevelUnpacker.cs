using Autodesk.Revit.DB;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.ToSpeckle.Properties;
using Speckle.Objects.Data;
using Speckle.Objects.Other;

namespace Speckle.Connectors.Revit.HostApp;

public class LevelUnpacker
{
  private readonly LevelExtractor _levelExtractor;
  private readonly PropertiesExtractor _propertiesExtractor;

  public LevelUnpacker(LevelExtractor levelExtractor, PropertiesExtractor propertiesExtractor)
  {
    _levelExtractor = levelExtractor;
    _propertiesExtractor = propertiesExtractor;
  }

  public List<LevelProxy> Unpack(List<Element> elements)
  {
    Dictionary<string, LevelProxy> levelProxies = new();
    foreach (var element in elements)
    {
      if (levelProxies.TryGetValue(element.LevelId.ToString(), out LevelProxy? levelProxy))
      {
        levelProxy.objects.Add(element.UniqueId);
      }
      else
      {
        var level = _levelExtractor.GetLevel(element);
        if (level is null)
        {
          continue;
        }

        levelProxies[element.LevelId.ToString()] = new LevelProxy()
        {
          applicationId = level.UniqueId,
          objects = [element.UniqueId],
          value = new DataObject()
          {
            name = level.Name,
            displayValue = [],
            properties = _propertiesExtractor.GetProperties(level)
          }
        };
      }
    }

    return levelProxies.Values.ToList();
  }
}
