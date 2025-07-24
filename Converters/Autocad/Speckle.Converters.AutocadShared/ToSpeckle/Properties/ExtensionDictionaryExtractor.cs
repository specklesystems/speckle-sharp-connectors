using Speckle.Converters.Autocad;
using Speckle.Converters.Common;

namespace Speckle.Converters.AutocadShared.ToSpeckle;

/// <summary>
/// Extracts extension dictionaries out from an element. Expects to be scoped per operation.
/// </summary>
/// <remarks>
/// Extension dictionary entry types are designated by their DxfCode: https://help.autodesk.com/view/OARX/2025/ENU/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_DxfCode
/// </remarks>
public class ExtensionDictionaryExtractor
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public ExtensionDictionaryExtractor(IConverterSettingsStore<AutocadConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Extracts extension dictionary out from an entity. Expects to be scoped per operation.
  /// </summary>
  /// <param name="entity"></param>
  /// <returns></returns>
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
        if (tr.GetObject(entry.Value, ADB.OpenMode.ForRead) is ADB.Xrecord xRecord) // sometimes these can be RXClass objects, in property sets
        {
          Dictionary<string, object?> entryDict = new();
          foreach (ADB.TypedValue xEntry in xRecord.Data)
          {
            if (GetValidValue(xEntry.Value) is object val)
            {
              entryDict[xEntry.TypeCode.ToString()] = val;
            }
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

  // xrecord values can contain invalid serialisation types like objectIds
  private object? GetValidValue(object val) => val.GetType().IsPrimitive ? val : val.ToString();
}
