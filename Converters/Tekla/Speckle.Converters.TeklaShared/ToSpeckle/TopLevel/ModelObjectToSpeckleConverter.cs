using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.TeklaShared.Extensions;
using Speckle.Converters.TeklaShared.ToSpeckle.Helpers;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.TeklaShared.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(TSM.ModelObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ModelObjectToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly PropertiesExtractor _propertiesExtractor;
  private readonly ClassPropertyExtractor _classPropertyExtractor;

  public ModelObjectToSpeckleConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor,
    PropertiesExtractor propertiesExtractor,
    ClassPropertyExtractor classPropertyExtractor
  )
  {
    _settingsStore = settingsStore;
    _displayValueExtractor = displayValueExtractor;
    _propertiesExtractor = propertiesExtractor;
    _classPropertyExtractor = classPropertyExtractor;
  }

  public Base Convert(object target) => Convert((TSM.ModelObject)target);

  private TeklaObject Convert(TSM.ModelObject target)
  {
    string type = target.GetType().ToString().Split('.').Last();

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

    // get name
    string name = type;
    switch (target)
    {
      case TSM.Part part:
        name = part.Name;
        break;
      case TSM.Reinforcement reinforcement:
        name = reinforcement.Name;
        break;
    }

    // get properties
    var properties = _propertiesExtractor.GetProperties(target);

    var result = new TeklaObject()
    {
      name = name,
      type = type,
      elements = children,
      properties = properties,
      displayValue = displayValue.ToList(),
      units = _settingsStore.Current.SpeckleUnits,
    };

    return result;
  }
}
