using Autodesk.AutoCAD.DatabaseServices;

namespace Speckle.Converters.AutocadShared.ToSpeckle;

/// <summary>
/// Extracts xdata from an element. Expects to be scoped per operation.
/// </summary>
/// <remarks>
/// XData entry types are designated by their DxfCode: https://help.autodesk.com/view/OARX/2025/ENU/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_DxfCode
/// </remarks>
public class XDataExtractor
{
  public XDataExtractor() { }

  /// <summary>
  /// Extracts xdata from an entity. Expects to be scoped per operation.
  /// </summary>
  /// <param name="entity"></param>
  /// <returns></returns>
  public Dictionary<string, object?>? GetXData(ADB.Entity entity)
  {
    if (entity is null || entity.XData is null)
    {
      return null;
    }

    // Xdata is applied by applications, and are stored under the application name.
    // We're storing the xdata dictionary as a set of subdictionaries per application.
    // in an app xdata dict, it is possible to have multiple entries of the same typecode
    Dictionary<string, object?> xDataDict = new();
    string? currentAppName = null;
    Dictionary<string, List<object?>> currentXData = new();

    foreach (TypedValue entry in entity.XData)
    {
      switch (entry.TypeCode)
      {
        case (int)DxfCode.ExtendedDataRegAppName:
          StoreAndClearCurrentAppXDataDict(currentAppName, currentXData, xDataDict);
          currentAppName = entry.Value as string;
          break;
        case (int)DxfCode.ExtendedDataControlString: // this is the start and end brace code for this list of entries
          break;
        default:
          if (GetValidValue(entry.Value) is object val)
          {
            string key = entry.TypeCode.ToString();
            if (currentXData.TryGetValue(key, out List<object?>? value))
            {
              value.Add(val);
            }
            else
            {
              currentXData[key] = new() { val };
            }
          }
          break;
      }
    }

    StoreAndClearCurrentAppXDataDict(currentAppName, currentXData, xDataDict);
    return xDataDict.Count > 0 ? xDataDict : null;
  }

  private void StoreAndClearCurrentAppXDataDict(
    string? appName,
    Dictionary<string, List<object?>> appXData,
    Dictionary<string, object?> xDataDict
  )
  {
    if (appName != null && appXData.Count > 0)
    {
      xDataDict[appName] = appXData.ToDictionary(o => o.Key, o => o.Value);
      appXData.Clear();
    }
  }

  // xrecord values can contain invalid serialisation types like objectIds
  private object? GetValidValue(object val) => val.GetType().IsPrimitive ? val : val.ToString();
}
