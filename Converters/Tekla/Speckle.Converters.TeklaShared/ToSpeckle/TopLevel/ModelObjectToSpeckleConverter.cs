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

  public ModelObjectToSpeckleConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor,
    PropertiesExtractor propertiesExtractor
  )
  {
    _settingsStore = settingsStore;
    _displayValueExtractor = displayValueExtractor;
    _propertiesExtractor = propertiesExtractor;
  }

  public Base Convert(object target) => Convert((TSM.ModelObject)target);

  private TeklaObject Convert(TSM.ModelObject target)
  {
    string type = target.GetSpeckleType();

    // get children
    // POC: This logic should be same in the material unpacker in connector
    // Assemblies, pour units and pour objects are membership targets, not owners: their members reach the bundle as
    // objects of their own and point back with IN_ASSEMBLY (ENG-11151), so they carry no children here.
    List<TeklaObject> children = new();
    if (target is not (TSM.Assembly or TSM.PourUnit or TSM.PourObject))
    {
      foreach (TSM.ModelObject childObject in target.GetSupportedChildren())
      {
        var child = Convert(childObject);
        child.applicationId = childObject.GetSpeckleApplicationId();
        children.Add(child);
      }
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
      case TSM.Assembly assembly:
        name = assembly.Name;
        break;
      case TSM.PourUnit pourUnit:
        name = pourUnit.Name;
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
