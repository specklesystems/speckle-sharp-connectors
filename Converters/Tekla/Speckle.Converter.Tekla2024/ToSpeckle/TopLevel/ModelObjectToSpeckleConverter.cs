using Speckle.Converter.Tekla2024.Extensions;
using Speckle.Converter.Tekla2024.ToSpeckle.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Tekla2024.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(TSM.ModelObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ModelObjectToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly ClassPropertyExtractor _propertyExtractor;
  private readonly ReportPropertyHandler _reportPropertyHandler;

  public ModelObjectToSpeckleConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor,
    ClassPropertyExtractor propertyExtractor,
    ReportPropertyHandler reportPropertyHandler
  )
  {
    _settingsStore = settingsStore;
    _displayValueExtractor = displayValueExtractor;
    _propertyExtractor = propertyExtractor;
    _reportPropertyHandler = reportPropertyHandler;
  }

  public Base Convert(object target)
  {
    if (target is not TSM.ModelObject modelObject)
    {
      throw new ArgumentException($"Target object is not a ModelObject. It's a {target.GetType()}");
    }

    var result = new Base
    {
      ["type"] = modelObject.GetType().ToString().Split('.').Last(),
      ["units"] = _settingsStore.Current.SpeckleUnits,
    };

    // get properties
    var properties = _propertyExtractor.GetProperties(modelObject);
    foreach (var prop in properties)
    {
      result[prop.Key] = prop.Value;
    }

    // get report properties
    var reportProperties = GetObjectReportProperties(modelObject);
    if (reportProperties.Count > 0)
    {
      result["properties"] = reportProperties;
    }

    // get display value
    var displayValue = _displayValueExtractor.GetDisplayValue(modelObject).ToList();
    if (displayValue.Count > 0)
    {
      result["displayValue"] = displayValue;
    }

    // get report properties
    Dictionary<string, object?> GetObjectReportProperties(TSM.ModelObject modelObject)
    {
      Dictionary<string, object?> properties = new();

      // get report properties
      var reportProperties = _reportPropertyHandler.GetProperties(modelObject);
      if (reportProperties.Count > 0)
      {
        properties["report"] = reportProperties;
      }

      // POC: might add user defined properties here

      return properties;
    }

    // get children
    // POC: This logic should be same in the material unpacker in connector
    List<Base> children = new();
    foreach (TSM.ModelObject childObject in modelObject.GetSupportedChildren())
    {
      var child = Convert(childObject);
      child.applicationId = childObject.GetSpeckleApplicationId();
      children.Add(child);
    }

    if (children.Count > 0)
    {
      result["elements"] = children;
    }

    return result;
  }
}
