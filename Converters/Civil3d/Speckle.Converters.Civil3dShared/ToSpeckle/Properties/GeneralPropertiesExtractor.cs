using System.Reflection;
using Autodesk.Civil.Runtime;
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
      // catchment -> properties -> Catchment Properties
      case CDB.Catchment catchment:
        return ExtractCatchmentProperties(catchment);

      // surface -> properties -> statistics -> general, extended, and tin/grid properties
      case CDB.Surface surface:
        return ExtractSurfaceProperties(surface);

      // alignment -> properties -> station control -> station equations, station information, reference point
      // alignment -> properties -> offset parameters -> parent alignment
      case CDB.Alignment alignment:
        return ExtractAlignmentProperties(alignment);

      // corridor -> properties -> codes, featurelines, surfaces
      case CDB.Corridor corridor:
        return ExtractCorridorProperties(corridor);

      // subassembly -> properties -> parameters, codes
      case CDB.Subassembly subassembly:
        return ExtractSubassemblyProperties(subassembly);

      default:
        return null;
    }
  }

  private Dictionary<string, object?> ExtractCatchmentProperties(CDB.Catchment catchment)
  {
    Dictionary<string, object?> generalPropertiesDict = new();

    // get catchment properties props
    Dictionary<string, object?> catchmentPropertiesDict = new();

    Dictionary<string, object?> hydrologicalProps = new() { ["runoffCoefficient"] = catchment.RunoffCoefficient };
    catchmentPropertiesDict["Hydrological Properties"] = hydrologicalProps;

#if CIVIL3D2024_OR_GREATER
    Dictionary<string, object?> sheetFlow =
      new()
      {
        ["sheetFlowSegments"] = catchment.SheetFlowSegments,
        ["sheetFlowTravelTime"] = catchment.SheetFlowTravelTime
      };
    catchmentPropertiesDict["Sheet Flow"] = sheetFlow;

    Dictionary<string, object?> shallowConcentratedFlow =
      new()
      {
        ["shallowFlowSegments"] = catchment.ShallowFlowSegments,
        ["shallowFlowTravelTime"] = catchment.ShallowFlowTravelTime
      };
    catchmentPropertiesDict["Shallow Concentrated Flow"] = shallowConcentratedFlow;

    Dictionary<string, object?> channelFlow =
      new()
      {
        ["channelFlowSegments"] = catchment.ChannelFlowSegments,
        ["channelFlowTravelTime"] = catchment.ChannelFlowTravelTime
      };
    catchmentPropertiesDict["Channel Flow"] = channelFlow;
#endif

    Dictionary<string, object?> timeOfConcentration =
      new()
      {
        ["timeOfConcentration"] = catchment.TimeOfConcentration,
        ["timeOfConcentrationCalculationMethod"] = catchment.TimeOfConcentrationCalculationMethod,
        ["hydrologicallyMostDistantPoint"] = catchment.HydrologicallyMostDistantPoint.ToArray(),
        ["hydrologicallyMostDistantLength"] = catchment.HydrologicallyMostDistantLength
      };
    catchmentPropertiesDict["Time of Concentration"] = timeOfConcentration;

    if (catchmentPropertiesDict.Count > 0)
    {
      generalPropertiesDict["Catchment Properties"] = catchmentPropertiesDict;
    }

    return generalPropertiesDict;
  }

  private Dictionary<string, object?> ExtractSubassemblyProperties(CDB.Subassembly subassembly)
  {
    Dictionary<string, object?> generalPropertiesDict = new();

    // get parameters props
    Dictionary<string, object?> parametersDict = new();
    foreach (ParamBool p in subassembly.ParamsBool)
    {
      parametersDict[p.DisplayName] = p.Value;
    }
    foreach (ParamDouble p in subassembly.ParamsDouble)
    {
      parametersDict[p.DisplayName] = p.Value;
    }
    foreach (ParamString p in subassembly.ParamsString)
    {
      parametersDict[p.DisplayName] = p.Value;
    }
    foreach (ParamLong p in subassembly.ParamsLong)
    {
      parametersDict[p.DisplayName] = p.Value;
    }
    if (parametersDict.Count > 0)
    {
      generalPropertiesDict["Parameters"] = parametersDict;
    }

    return generalPropertiesDict;
  }

  private void ProcessCorridorFeaturelinePoints(
    CDB.CorridorFeatureLine featureline,
    Dictionary<string, Dictionary<string, object?>> featureLinesDict
  )
  {
    if (featureLinesDict.TryGetValue(featureline.CodeName, out Dictionary<string, object?>? value))
    {
      Dictionary<string, object?> pointsDict = new(featureline.FeatureLinePoints.Count);
      int pointCount = 0;
      foreach (CDB.FeatureLinePoint point in featureline.FeatureLinePoints)
      {
        pointsDict[pointCount.ToString()] = new Dictionary<string, object?>()
        {
          ["station"] = point.Station,
          ["xyz"] = point.XYZ.ToArray(),
          ["isBreak"] = point.IsBreak,
          ["offset"] = point.Offset
        };

        pointCount++;
      }

      value["featureLinePoints"] = pointsDict;
    }
  }

  private Dictionary<string, object?> ExtractCorridorProperties(CDB.Corridor corridor)
  {
    Dictionary<string, object?> generalPropertiesDict = new();

    // get codes props
    Dictionary<string, object?> codesDict =
      new()
      {
        ["link"] = corridor.GetLinkCodes(),
        ["point"] = corridor.GetPointCodes(),
        ["shape"] = corridor.GetShapeCodes()
      };
    generalPropertiesDict["codes"] = codesDict;

    // get feature lines props
    // this is pretty complicated: need to extract featureline points as dicts, but can only do this by iterating through baselines. Need to match the iterated featurelines with the featureline code info.
    Dictionary<string, Dictionary<string, object?>> featureLinesDict = new();
    // first build dict from the code info
    foreach (CDB.FeatureLineCodeInfo featureLineCode in corridor.FeatureLineCodeInfos)
    {
      featureLinesDict[featureLineCode.CodeName] = new Dictionary<string, object?>()
      {
        ["codeName"] = featureLineCode.CodeName,
        ["isConnected"] = featureLineCode.IsConnected,
        ["payItems"] = featureLineCode.PayItems
      };
    }
    // then iterate through baseline featurelines to populate point info
    foreach (CDB.Baseline baseline in corridor.Baselines)
    {
      // main featurelines
      foreach (
        CDB.FeatureLineCollection mainFeaturelineCollection in baseline
          .MainBaselineFeatureLines
          .FeatureLineCollectionMap
      )
      {
        foreach (CDB.CorridorFeatureLine featureline in mainFeaturelineCollection)
        {
          ProcessCorridorFeaturelinePoints(featureline, featureLinesDict);
        }
      }

      // offset featurelines
      foreach (CDB.BaselineFeatureLines offsetFeaturelineCollection in baseline.OffsetBaselineFeatureLinesCol)
      {
        foreach (
          CDB.FeatureLineCollection featurelineCollection in offsetFeaturelineCollection.FeatureLineCollectionMap
        )
        {
          foreach (CDB.CorridorFeatureLine featureline in featurelineCollection)
          {
            ProcessCorridorFeaturelinePoints(featureline, featureLinesDict);
          }
        }
      }
    }
    if (featureLinesDict.Count > 0)
    {
      generalPropertiesDict["Feature Lines"] = featureLinesDict;
    }

    // get surfaces props
    Dictionary<string, object?> surfacesDict = new();
    foreach (CDB.CorridorSurface surface in corridor.CorridorSurfaces)
    {
      surfacesDict[surface.Name] = new Dictionary<string, object?>()
      {
        ["name"] = surface.Name,
        ["surfaceId"] = surface.SurfaceId.GetSpeckleApplicationId(),
        ["description"] = surface.Description,
        ["overhangCorrection"] = surface.OverhangCorrection.ToString()
      };
    }
    if (surfacesDict.Count > 0)
    {
      generalPropertiesDict["Surfaces"] = surfacesDict;
    }

    return generalPropertiesDict;
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
      equationCount++;
    }
    if (stationEquationsDict.Count > 0)
    {
      stationControlDict["Station Equations"] = stationEquationsDict;
    }

    Dictionary<string, object?> referencePointDict =
      new()
      {
        ["x"] = alignment.ReferencePoint.X,
        ["y"] = alignment.ReferencePoint.Y,
        ["station"] = alignment.ReferencePointStation
      };
    stationControlDict["Reference Point"] = referencePointDict;

    if (stationControlDict.Count > 0)
    {
      generalPropertiesDict["Station Control"] = stationControlDict;
    }

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
      speedsCount++;
    }
    designCriteriaDict["Design Speeds"] = designSpeedsDict;
    if (designSpeedsDict.Count > 0)
    {
      designCriteriaDict["Design Speeds"] = designSpeedsDict;
    }

    if (designCriteriaDict.Count > 0)
    {
      generalPropertiesDict["Design Critera"] = designCriteriaDict;
    }

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
        statisticsDict["Volume"] = ExtractPropertiesGeneric<CDB.VolumeSurfaceProperties>(
          tinVolumeSurface.GetVolumeProperties()
        );
        break;
      case CDB.GridSurface gridSurface:
        statisticsDict["Grid"] = ExtractPropertiesGeneric<CDB.GridSurfaceProperties>(gridSurface.GetGridProperties());
        break;
      case CDB.GridVolumeSurface gridVolumeSurface:
        statisticsDict["Grid"] = ExtractPropertiesGeneric<CDB.GridSurfaceProperties>(
          gridVolumeSurface.GetGridProperties()
        );
        statisticsDict["Volume"] = ExtractPropertiesGeneric<CDB.VolumeSurfaceProperties>(
          gridVolumeSurface.GetVolumeProperties()
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
      if (value is ADB.ObjectId id)
      {
        value = id.GetSpeckleApplicationId();
      }

      propertiesDict[property.Name] = value;
    }

    return propertiesDict;
  }
}
