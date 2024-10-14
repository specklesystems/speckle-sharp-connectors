using System.Reflection;
using Speckle.Converters.Civil3dShared.Extensions;

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
    switch (entity)
    {
      // surface -> properties -> statistics -> general, extended, and tin/grid properties
      case CDB.Surface surface:
        return ExtractSurfaceProperties(surface);

      // alignment -> properties -> station control -> station equations, station information, reference point
      // alignment -> properties -> offset parameters -> parent alignment
      case CDB.Alignment alignment:
        return ExtractAlignmentProperties(alignment);

      default:
        return null;
    }
  }

  private Dictionary<string, object?> ExtractAlignmentProperties(CDB.Alignment alignment)
  {
    Dictionary<string, object?> generalPropertiesDict = new();

    // get station control props
    Dictionary<string, object?> stationControlDict = new();

    Dictionary<string, object?> stationEquationsDict = new();
    int equationCount = 0;
    foreach (var stationEquation in alignment.StationEquations)
    {
      stationEquationsDict[equationCount.ToString()] = new Dictionary<string, object>()
      {
        ["rawStationBack"] = stationEquation.RawStationBack,
        ["stationBack"] = stationEquation.StationBack,
        ["stationAhead"] = stationEquation.StationAhead,
        ["equationType"] = stationEquation.EquationType.ToString()
      };
    }
    stationControlDict["Station Equations"] = stationEquationsDict;

    Dictionary<string, object?> referencePointDict =
      new()
      {
        ["x"] = alignment.ReferencePoint.X,
        ["y"] = alignment.ReferencePoint.Y,
        ["station"] = alignment.ReferencePointStation
      };
    stationControlDict["Reference Point"] = referencePointDict;

    generalPropertiesDict["Station Control"] = stationControlDict;

    // get design criteria props
    Dictionary<string, object?> designCriteriaDict = new();

    Dictionary<string, object?> designSpeedsDict = new();
    int speedsCount = 0;
    foreach (CDB.DesignSpeed designSpeed in alignment.DesignSpeeds)
    {
      designSpeedsDict[speedsCount.ToString()] = new Dictionary<string, object>()
      {
        ["number"] = designSpeed.SpeedNumber,
        ["station"] = designSpeed.Station,
        ["value"] = designSpeed.Value
      };
    }
    designCriteriaDict["Design Speeds"] = designSpeedsDict;

    generalPropertiesDict["Design Critera"] = designCriteriaDict;

    // get offset alignment props
    if (alignment.IsOffsetAlignment)
    {
      var offsetInfo = alignment.OffsetAlignmentInfo;
      Dictionary<string, object?> offsetAlignmentDict =
        new()
        {
          ["side"] = offsetInfo.Side.ToString(),
          ["parentAlignmentId"] = offsetInfo.ParentAlignmentId.GetSpeckleApplicationId(),
          ["nominalOffset"] = offsetInfo.NominalOffset
        };

      generalPropertiesDict["Offset Parameters"] = offsetAlignmentDict;
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
