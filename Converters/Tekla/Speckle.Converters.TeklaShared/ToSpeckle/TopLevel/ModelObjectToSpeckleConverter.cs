using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.TeklaShared.Extensions;
using Speckle.Converters.TeklaShared.ToSpeckle.Helpers;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.TeklaShared.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(TSM.ModelObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ModelObjectToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly ClassPropertyExtractor _propertyExtractor;
  private readonly ReportPropertyExtractor _reportPropertyExtractor;

  public ModelObjectToSpeckleConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor,
    ClassPropertyExtractor propertyExtractor,
    ReportPropertyExtractor reportPropertyExtractor
  )
  {
    _settingsStore = settingsStore;
    _displayValueExtractor = displayValueExtractor;
    _propertyExtractor = propertyExtractor;
    _reportPropertyExtractor = reportPropertyExtractor;
  }

  public Base Convert(object target) => Convert((TSM.ModelObject)target);

  private TeklaObject Convert(TSM.ModelObject target)
  {
    // get children
    // POC: This logic should be same in the material unpacker in connector
    List<TeklaObject> children = new();
    foreach (TSM.ModelObject childObject in target.GetSupportedChildren())
    {
      var child = Convert(childObject);
      child.applicationId = childObject.GetSpeckleApplicationId();
      children.Add(child);
    }

    // get display value
    IEnumerable<Base> displayValue = _displayValueExtractor.GetDisplayValue(target).ToList();

    string type = target.GetType().ToString().Split('.').Last();
    var result = new TeklaObject()
    {
      name = type,
      type = type,
      elements = children,
      displayValue = displayValue.ToList(),
      units = _settingsStore.Current.SpeckleUnits
    };

    // get report properties
    var reportProperties = _reportPropertyExtractor.GetReportProperties(target);
    if (reportProperties.Count > 0)
    {
      var propertiesDict = new Dictionary<string, object?> { { "report", reportProperties } };
      result["properties"] = propertiesDict;
    }

    // get properties
    var properties = _propertyExtractor.GetProperties(target);
    foreach (var prop in properties)
    {
      result[prop.Key] = prop.Value;
    }

    return result;
  }
}
