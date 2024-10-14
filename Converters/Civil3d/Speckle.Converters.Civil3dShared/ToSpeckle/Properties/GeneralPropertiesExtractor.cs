using System.Reflection;

namespace Speckle.Converters.Civil3dShared.ToSpeckle;

/// <summary>
/// Extracts general properties related to analysis, statistics, and calculations out from a civil entity. Expects to be scoped per operation.
/// </summary>
public class GeneralPropertiesExtractor
{
  public GeneralPropertiesExtractor() { }

  /// <summary>
  /// Extracts general properties from a civil entity. Expects to be scoped per operation.
  /// </summary>
  /// <param name="entity"></param>
  /// <returns></returns>
  public Dictionary<string, object?>? GetGeneralProperties(CDB.Entity entity)
  {
    Dictionary<string, object?>? generalPropertiesDict = null;
    switch (entity)
    {
      // surface -> properties -> statistics -> general, extended, and tin/grid properties
      case CDB.Surface surface:
        generalPropertiesDict = ExtractSurfaceProperties(surface);
        break;
    }

    return generalPropertiesDict;
  }

  private Dictionary<string, object?> ExtractSurfaceProperties(CDB.Surface surface)
  {
    Dictionary<string, object?> generalPropertiesDict = new();

    // get statistics props
    Dictionary<string, object?> statisticsDict = new();
    statisticsDict["General"] = ExtractPropertiesGeneric<CDB.GeneralSurfaceProperties>(surface.GetGeneralProperties());
    switch (surface)
    {
      case CDB.TinSurface tinSurface:
        statisticsDict["TIN"] = ExtractPropertiesGeneric<CDB.TinSurfaceProperties>(tinSurface.GetTinProperties());
        break;
      case CDB.TinVolumeSurface tinVolumeSurface:
        statisticsDict["TIN"] = ExtractPropertiesGeneric<CDB.TinSurfaceProperties>(tinVolumeSurface.GetTinProperties());
        break;
      case CDB.GridSurface gridSurface:
        statisticsDict["Grid"] = ExtractPropertiesGeneric<CDB.GridSurfaceProperties>(gridSurface.GetGridProperties());
        break;
      case CDB.GridVolumeSurface gridVolumeSurface:
        statisticsDict["Grid"] = ExtractPropertiesGeneric<CDB.GridSurfaceProperties>(
          gridVolumeSurface.GetGridProperties()
        );
        break;
    }

    // set all general props
    generalPropertiesDict["Statistics"] = statisticsDict;
    return generalPropertiesDict;
  }

  // A generic method to create a dictionary from an object types's properties
  private Dictionary<string, object?> ExtractPropertiesGeneric<T>(T obj)
  {
    Dictionary<string, object?> propertiesDict = new();

    var type = typeof(T);
    PropertyInfo[] properties = type.GetProperties();
    foreach (PropertyInfo? property in properties)
    {
      var value = property.GetValue(obj);
      propertiesDict[property.Name] = value;
    }

    return propertiesDict;
  }
}
