using Speckle.Converters.Common;

namespace Speckle.Converters.Plant3dShared.ToSpeckle;

/// <summary>
/// Extracts properties for Plant3D objects.
/// </summary>
public class PropertiesExtractor : Speckle.Converters.AutocadShared.ToSpeckle.IPropertiesExtractor
{
  private readonly ExtensionDictionaryExtractor _extensionDictionaryExtractor;
  private readonly AutocadShared.ToSpeckle.TextPropertiesExtractor _textPropertiesExtractor;
  private readonly string _drawingName;

  public PropertiesExtractor(
    ExtensionDictionaryExtractor extensionDictionaryExtractor,
    AutocadShared.ToSpeckle.TextPropertiesExtractor textPropertiesExtractor,
    IConverterSettingsStore<Plant3dConversionSettings> settingsStore
  )
  {
    _extensionDictionaryExtractor = extensionDictionaryExtractor;
    _textPropertiesExtractor = textPropertiesExtractor;
    _drawingName = Path.GetFileName(settingsStore.Current.Document.Name);
  }

  public Dictionary<string, object?> GetProperties(ADB.Entity entity)
  {
    Dictionary<string, object?> properties = [];

    // TODO: Add Plant3D class-specific property extraction here
    // For example, extract pipe spec data, equipment data, etc.
    properties["Drawing Name"] = _drawingName;

    // add property sets and extension dictionaries to the properties dict
    AddDictionaryToPropertyDictionary(
      _extensionDictionaryExtractor.GetExtensionDictionary(entity),
      "Extension Dictionary",
      properties
    );
    // Plain AutoCAD annotation in a Plant drawing — keep its content queryable alongside the SGEO Text
    // geometry [ENG-8827].
    AddDictionaryToPropertyDictionary(_textPropertiesExtractor.GetTextProperties(entity), "Text", properties);

    AddAssetProperties(entity, properties);

    return properties;
  }

  private static void AddDictionaryToPropertyDictionary(
    Dictionary<string, object?>? entryDictionary,
    string entryName,
    Dictionary<string, object?> propertyDictionary
  )
  {
    if (entryDictionary is not null && entryDictionary.Count > 0)
    {
      propertyDictionary.Add(entryName, entryDictionary);
    }
  }

  private static void AddAssetProperties(ADB.Entity entity, Dictionary<string, object?> properties)
  {
    if (entity is PP.PnIDObjects.Asset asset && entity.Database.TransactionManager.TopTransaction is ADB.Transaction tr)
    {
      if (SafeGetObject(asset.BlockTableRecord, tr) is ADB.BlockTableRecord block && !block.IsAnonymous)
      {
        properties["Symbol Name"] = block.Name;
      }

      if (SafeGetObject(asset.StyleId, tr) is PP.Styles.AssetStyle assetStyle)
      {
        properties["Graphical Style"] = assetStyle.Name;
      }
    }
  }

  private static ADB.DBObject? SafeGetObject(ADB.ObjectId objectId, ADB.Transaction tr) =>
    !objectId.IsNull && !objectId.IsErased && objectId.IsValid ? tr.GetObject(objectId, ADB.OpenMode.ForRead) : null;
}
