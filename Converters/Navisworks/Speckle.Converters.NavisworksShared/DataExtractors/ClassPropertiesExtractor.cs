using static Speckle.Converter.Navisworks.Helpers.PropertyHelpers;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class ClassPropertiesExtractor
{
  public Dictionary<string, object?> GetClassProperties(NAV.ModelItem modelItem) =>
    modelItem == null ? throw new ArgumentNullException(nameof(modelItem)) : ExtractClassProperties(modelItem);

  /// <summary>
  /// Extracts property sets from a NAV.ModelItem and adds them to a dictionary,
  /// including various details such as ClassName, ClassDisplayName, DisplayName,
  /// InstanceGuid, Source, and Source Guid, ensuring that null or empty values
  /// are not included in the final dictionary.
  /// </summary>
  /// <param name="modelItem">The NAV.ModelItem from which properties are extracted.</param>
  /// <returns>A dictionary containing non-null/non-empty properties of the modelItem.</returns>
  private Dictionary<string, object?> ExtractClassProperties(NAV.ModelItem modelItem)
  {
    var propertyDictionary = new Dictionary<string, object?>();

    var properties = new (string Key, object? Value)[]
    {
      ("ClassDisplayName", modelItem.ClassDisplayName),
      ("DisplayName", modelItem.DisplayName),
      ("InstanceGuid", modelItem.InstanceGuid != Guid.Empty ? modelItem.InstanceGuid : null),
      ("Source", modelItem.Model?.SourceFileName),
      ("Source Guid", modelItem.Model?.SourceGuid)
    };

    foreach ((string key, object? value) in properties)
    {
      AddPropertyIfNotNullOrEmpty(propertyDictionary, key, value);
    }

    return propertyDictionary;
  }
}
