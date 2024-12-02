using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.TeklaShared.Extensions;
using Speckle.Converters.TeklaShared.ToSpeckle.Helpers;
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

    // get report properties
    var reportProperties = _reportPropertyExtractor.GetReportProperties(modelObject);
    if (reportProperties.Count > 0)
    {
      var propertiesDict = new Dictionary<string, object?> { { "report", reportProperties } };
      result["properties"] = propertiesDict;
    }

    // get properties
    var properties = _propertyExtractor.GetProperties(modelObject);
    foreach (var prop in properties)
    {
      result[prop.Key] = prop.Value;
    }

    // get display value
    var displayValue = _displayValueExtractor.GetDisplayValue(modelObject).ToList();
    if (displayValue.Count > 0)
    {
      result["displayValue"] = displayValue;
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
