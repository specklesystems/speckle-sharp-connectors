using Speckle.Converters.Common;

namespace Speckle.Converters.Plant3dShared.ToSpeckle;

/// <summary>
/// Extracts extension dictionaries out from an element. Expects to be scoped per operation.
/// </summary>
public class ExtensionDictionaryExtractor
{
  private readonly IConverterSettingsStore<Plant3dConversionSettings> _settingsStore;

  public ExtensionDictionaryExtractor(IConverterSettingsStore<Plant3dConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Extracts extension dictionary out from an entity. Expects to be scoped per operation.
  /// </summary>
  public Dictionary<string, object?>? GetExtensionDictionary(ADB.Entity entity)
  {
    if (entity is null || entity.ExtensionDictionary == ADB.ObjectId.Null)
    {
      return null;
    }

    Dictionary<string, object?> extensionDictionaryDict = new();

    using (ADB.Transaction tr = _settingsStore.Current.Document.TransactionManager.StartTransaction())
    {
      var extensionDictionary = (ADB.DBDictionary)tr.GetObject(entity.ExtensionDictionary, ADB.OpenMode.ForRead, false);

      foreach (ADB.DBDictionaryEntry entry in extensionDictionary)
      {
        if (tr.GetObject(entry.Value, ADB.OpenMode.ForRead) is ADB.Xrecord xRecord)
        {
          Dictionary<string, object?> entryDict = new();
          foreach (ADB.TypedValue xEntry in xRecord.Data)
          {
            entryDict[xEntry.TypeCode.ToString()] = xEntry.Value;
          }

          if (entryDict.Count > 0)
          {
            extensionDictionaryDict[$"{entry.Key}"] = entryDict;
          }
        }
      }

      tr.Commit();
    }

    return extensionDictionaryDict.Count > 0 ? extensionDictionaryDict : null;
  }
}
