using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class ModelPropertiesExtractor(IConverterSettingsStore<NavisworksConversionSettings> settingsStore)
{
  internal Dictionary<string, object?>? GetModelProperties(NAV.Model model)
  {
    if (settingsStore.Current.User.ExcludeProperties)
    {
      return null;
    }

    var propertyDictionary = ExtractModelProperties(model);

    return propertyDictionary;
  }

  /// <summary>
  /// Extracts model properties from a NAV.ModelItem and adds them to a dictionary,
  /// </summary>
  /// <param name="model">The NAV.ModelItem from which model properties are extracted.</param>
  /// <returns>A dictionary containing model properties of the modelItem.</returns>
  private static Dictionary<string, object?> ExtractModelProperties(NAV.Model model)
  {
    var propertyDictionary = new Dictionary<string, object?>();

    // Define properties and their values to be added to the dictionary
    var propertiesToAdd = new (string PropertyName, object? Value)[]
    {
      ("Creator", model.Creator),
      ("Filename", model.FileName),
      ("Source Filename", model.SourceFileName),
      ("Units", model.Units.ToString()),
      ("Transform", model.Transform.ToString()),
      ("Guid", model.Guid != Guid.Empty ? model.Guid : null),
      ("Source Guid", model.SourceGuid != Guid.Empty ? model.SourceGuid : null)
    };

    // Loop through properties and add them if they are not null or empty
    foreach ((string propertyName, object? value) in propertiesToAdd)
    {
      if (value != null)
      {
        Helpers.PropertyHelpers.AddPropertyIfNotNullOrEmpty(propertyDictionary, propertyName, value);
      }
    }

    return propertyDictionary;
  }
}
