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
  private readonly PropertyExtractor _propertyExtractor;
  private readonly ColorHandler _colorHandler;

  public ModelObjectToSpeckleConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor,
    PropertyExtractor propertyExtractor,
    ColorHandler colorHandler
  )
  {
    _settingsStore = settingsStore;
    _displayValueExtractor = displayValueExtractor;
    _propertyExtractor = propertyExtractor;
    _colorHandler = colorHandler;
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
      ["units"] = _settingsStore.Current.SpeckleUnits
    };

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
    List<Base> children = GetModelObjectChildren(modelObject);
    if (children.Count > 0)
    {
      result["elements"] = children;
    }

    // process color
    _colorHandler.ProcessColor(modelObject);

    return result;
  }

  private List<Base> GetModelObjectChildren(TSM.ModelObject modelObject)
  {
    List<Base> children = new();
    
    foreach (TSM.ModelObject childObject in modelObject.GetChildren())
    {
      if (childObject is TSM.ControlPoint or TSM.Weld or TSM.Fitting)
      {
        continue;
      }
      children.Add(Convert(childObject));
    }
    return children;
  }
}
