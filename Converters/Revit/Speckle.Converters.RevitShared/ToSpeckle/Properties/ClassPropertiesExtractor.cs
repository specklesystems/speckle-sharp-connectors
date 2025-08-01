using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Extensions;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle.Properties;

/// <summary>
/// For retrieving general properties used for business intelligence on the class, that are not already captured in parameters.
/// </summary>
public class ClassPropertiesExtractor
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  private readonly Dictionary<int, string> _worksetCache = new();

  public ClassPropertiesExtractor(IConverterSettingsStore<RevitConversionSettings> converterSettings)
  {
    _converterSettings = converterSettings;
  }

  public Dictionary<string, object?> GetClassProperties(DB.Element element)
  {
    Dictionary<string, object?> elementPropertiesDict = ExtractElementProperties(element);

    // add type specific props not included in parameters.
    // so far, no extra props are needed
    /*
    switch (element)
    {
      default:
        break;
    }
    */

    return elementPropertiesDict;
  }

  // gets the properties on the db.element class
  private Dictionary<string, object?> ExtractElementProperties(DB.Element element)
  {
    Dictionary<string, object?> elementProperties =
      new()
      {
        { "elementId", element.Id.ToString() },
        { "builtInCategory", element.Category?.GetBuiltInCategory().ToString() },
        { "worksetId", element.WorksetId?.ToString() }
      };

    int? worksetId = element.WorksetId?.IntegerValue;
    if (worksetId is not null)
    {
      // get workset name
      if (!_worksetCache.TryGetValue(worksetId.Value, out var worksetName))
      {
        DB.Workset workset = _converterSettings.Current.Document.GetWorksetTable().GetWorkset(element.WorksetId);
        worksetName = workset.Name;
        _worksetCache[worksetId.Value] = worksetName;
      }

      elementProperties.Add("worksetName", worksetName);
    }
    return elementProperties;
  }
}
