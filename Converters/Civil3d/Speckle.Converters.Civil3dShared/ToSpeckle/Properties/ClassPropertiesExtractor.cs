using System.Reflection;
using Autodesk.Civil.Runtime;
using Speckle.Converters.Civil3dShared.Extensions;
using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Common;

namespace Speckle.Converters.Civil3dShared.ToSpeckle;

/// <summary>
/// Extracts class properties deemed important for business intelligence workflows from a civil entity.
/// Should not repeat any data that would be included on property sets and general properties on the object.
/// Expects to be scoped per operation.
/// </summary>
public class ClassPropertiesExtractor
{
  private const string ASSIGNMENT_PROP = "Assignments";
  private const string DIMENSIONAL_PROP = "Dimensional Properties";
  private const string SITEID_PROP = "siteId";
  private const string SITENAME_PROP = "siteName";
  private const string SURFACEID_PROP = "surfaceId";
  private const string SURFACENAME_PROP = "surfaceName";
  private const string NETWORKID_PROP = "networkId";
  private const string NETWORKNAME_PROP = "networkName";
  private const string ALIGNMENTID_PROP = "alignmentId";
  private const string ALIGNMENTNAME_PROP = "alignmentName";
  private const string CODES_PROP = "codes";
  private const string SHAPES_PROP = "shapes";
  private const string LINKS_PROP = "links";
  private const string POINTS_PROP = "points";

  private readonly IConverterSettingsStore<Civil3dConversionSettings> _converterSettings;

  private readonly Dictionary<ADB.ObjectId, string> _catchmentGroupCache = new();
  private readonly Dictionary<ADB.ObjectId, string> _networkCache = new();

  public ClassPropertiesExtractor(IConverterSettingsStore<Civil3dConversionSettings> converterSettings)
  {
    _converterSettings = converterSettings;
  }

  /// <summary>
  /// Extracts general properties from a civil entity. Expects to be scoped per operation.
  /// </summary>
  /// <param name="entity"></param>
  /// <returns></returns>
  public Dictionary<string, object?> GetClassProperties(ADB.Entity entity)
  {
    switch (entity)
    {
      // site
      case CDB.Site site:
        return ExtractSiteProperties(site);
      case CDB.Catchment catchment:
        return ExtractCatchmentProperties(catchment);
      case CDB.Parcel parcel:
        return ExtractParcelProperties(parcel);
      case CDB.Surface surface:
        return ExtractSurfaceProperties(surface);

      // pipe networks
      case CDB.Pipe pipe:
        return ExtractPipeProperties(pipe);
      case CDB.Structure structure:
        return ExtractStructureProperties(structure);

      // corridors, alignments, profiles
      case CDB.Corridor corridor:
        return ExtractCorridorProperties(corridor);
      case CDB.Alignment alignment:
        return ExtractAlignmentProperties(alignment);
      case CDB.Profile profile:
        return ExtractProfileProperties(profile);

      // assemblies, subassemblies
      case CDB.Subassembly subassembly:
        return ExtractSubassemblyProperties(subassembly);

      default:
        return new();
    }
  }

  private Dictionary<string, object?> ExtractSiteProperties(CDB.Site site)
  {
    // get general props
    Dictionary<string, object?> properties = new() { };

    // get assignments like catchment group, reference surface, reference pipe networks
    Dictionary<string, object?> assignmentProps = new();
    if (site.GetAlignmentIds().Count > 0)
    {
      assignmentProps[ALIGNMENTID_PROP] = GetSpeckleApplicationIdsFromCollection(site.GetAlignmentIds());
    }
    if (site.GetFeatureLineIds().Count > 0)
    {
      assignmentProps["featureLineId"] = GetSpeckleApplicationIdsFromCollection(site.GetFeatureLineIds());
    }

    AddDictionaryToDictionary(assignmentProps, properties, ASSIGNMENT_PROP);
    return properties;
  }

  private Dictionary<string, object?> ExtractCatchmentProperties(CDB.Catchment catchment)
  {
    // get general props
    Dictionary<string, object?> properties = new() { };

    // get assignments like catchment group, reference surface, reference pipe networks
    Dictionary<string, object?> assignmentProps = new();
    if (catchment.ContainingGroupId != ADB.ObjectId.Null)
    {
      assignmentProps["catchmentGroupId"] = catchment.ContainingGroupId.GetSpeckleApplicationId();
      if (_catchmentGroupCache.TryGetValue(catchment.ContainingGroupId, out string? name))
      {
        assignmentProps["catchmentGroupName"] = name;
      }
      else
      {
        using (var tr = _converterSettings.Current.Document.Database.TransactionManager.StartTransaction())
        {
          var catchmentGroup = (CDB.CatchmentGroup)tr.GetObject(catchment.ContainingGroupId, ADB.OpenMode.ForRead);
          _catchmentGroupCache[catchment.ContainingGroupId] = catchmentGroup.Name;
          assignmentProps["catchmentGroupName"] = catchmentGroup.Name;
          tr.Commit();
        }
      }
    }

    if (catchment.ReferenceSurfaceId != ADB.ObjectId.Null)
    {
      assignmentProps[SURFACEID_PROP] = catchment.ReferenceSurfaceId.GetSpeckleApplicationId();
      assignmentProps[SURFACENAME_PROP] = catchment.ReferenceSurfaceName;
    }

    if (catchment.ReferencePipeNetworkId != ADB.ObjectId.Null)
    {
      assignmentProps[NETWORKID_PROP] = catchment.ReferencePipeNetworkId.GetSpeckleApplicationId();
      assignmentProps[NETWORKNAME_PROP] = catchment.ReferencePipeNetworkName;
    }

    if (catchment.ReferencePipeNetworkStructureId != ADB.ObjectId.Null)
    {
      assignmentProps["networkStructureId"] = catchment.ReferencePipeNetworkStructureId.GetSpeckleApplicationId();
      assignmentProps["networkStructureName"] = catchment.ReferencePipeNetworkStructureName;
    }

    AddDictionaryToDictionary(assignmentProps, properties, ASSIGNMENT_PROP);

    // get dimensional props
    properties[DIMENSIONAL_PROP] = new Dictionary<string, object?>()
    {
      ["area"] = catchment.Area,
      ["area2d"] = catchment.Area2d,
      ["imperviousArea"] = catchment.ImperviousArea,
      ["perimeter2d"] = catchment.Perimeter2d
    };

    // get hydrological props
    properties["Hydrological Properties"] = new Dictionary<string, object?>()
    {
      ["timeOfConcentration"] = catchment.TimeOfConcentration,
      ["timeOfConcentrationCalculationMethod"] = catchment.TimeOfConcentrationCalculationMethod,
      ["hydrologicallyMostDistantPoint"] = catchment.HydrologicallyMostDistantPoint.ToArray(),
      ["hydrologicallyMostDistantLength"] = catchment.HydrologicallyMostDistantLength,
      ["runoffCoefficient"] = catchment.RunoffCoefficient,
      ["hydrologicalSoilGroup"] = catchment.HydrologicalSoilGroup.ToString(),
      ["antecedentWetness"] = catchment.AntecedentWetness
    };

    // get hydraulic props
    properties["Hydraulic Properties"] = new Dictionary<string, object?>()
    {
      ["manningsCoefficient"] = catchment.ManningsCoefficient,
#if CIVIL3D2024_OR_GREATER
      ["sheetFlowSegments"] = catchment.SheetFlowSegments,
      ["sheetFlowTravelTime"] = catchment.SheetFlowTravelTime,
      ["shallowFlowSegments"] = catchment.ShallowFlowSegments,
      ["shallowFlowTravelTime"] = catchment.ShallowFlowTravelTime,
      ["channelFlowSegments"] = catchment.ChannelFlowSegments,
      ["channelFlowTravelTime"] = catchment.ChannelFlowTravelTime
#endif
    };

    return properties;
  }

  private Dictionary<string, object?> ExtractParcelProperties(CDB.Parcel parcel)
  {
    // get general props
    Dictionary<string, object?> properties = new() { ["number"] = parcel.Number, ["area"] = parcel.Area };
#if CIVIL3D2023_OR_GREATER
    properties["taxId"] = parcel.TaxId;
#endif
    return properties;
  }

  private Dictionary<string, object?> ExtractSurfaceProperties(CDB.Surface surface)
  {
    // get general props
    Dictionary<string, object?> properties = new() { };

    // get statistics props
    Dictionary<string, object?> statisticsProps = ExtractPropertiesGeneric<CDB.GeneralSurfaceProperties>(
      surface.GetGeneralProperties()
    );

    switch (surface)
    {
      case CDB.TinSurface tinSurface:
        AddDictionaryToDictionary(
          ExtractPropertiesGeneric<CDB.TerrainSurfaceProperties>(tinSurface.GetTerrainProperties()),
          statisticsProps,
          "Terrain"
        );
        AddDictionaryToDictionary(
          ExtractPropertiesGeneric<CDB.TinSurfaceProperties>(tinSurface.GetTinProperties()),
          statisticsProps,
          "TIN"
        );
        break;

      case CDB.TinVolumeSurface tinVolumeSurface:
        AddDictionaryToDictionary(
          ExtractPropertiesGeneric<CDB.TinSurfaceProperties>(tinVolumeSurface.GetTinProperties()),
          statisticsProps,
          "TIN"
        );
        AddDictionaryToDictionary(
          ExtractPropertiesGeneric<CDB.VolumeSurfaceProperties>(tinVolumeSurface.GetVolumeProperties()),
          statisticsProps,
          "Volume"
        );
        break;

      case CDB.GridSurface gridSurface:
        AddDictionaryToDictionary(
          ExtractPropertiesGeneric<CDB.TerrainSurfaceProperties>(gridSurface.GetTerrainProperties()),
          statisticsProps,
          "Terrain"
        );
        AddDictionaryToDictionary(
          ExtractPropertiesGeneric<CDB.GridSurfaceProperties>(gridSurface.GetGridProperties()),
          statisticsProps,
          "Grid"
        );
        break;

      case CDB.GridVolumeSurface gridVolumeSurface:
        AddDictionaryToDictionary(
          ExtractPropertiesGeneric<CDB.GridSurfaceProperties>(gridVolumeSurface.GetGridProperties()),
          statisticsProps,
          "Grid"
        );
        AddDictionaryToDictionary(
          ExtractPropertiesGeneric<CDB.VolumeSurfaceProperties>(gridVolumeSurface.GetVolumeProperties()),
          statisticsProps,
          "Volume"
        );
        break;
    }

    AddDictionaryToDictionary(statisticsProps, properties, "Statistics");
    return properties;
  }

  private Dictionary<string, object?> ExtractPipeProperties(CDB.Pipe pipe)
  {
    // get general props
    Dictionary<string, object?> properties =
      new()
      {
        ["domain"] = pipe.Domain.ToString(), // part prop
        ["partType"] = pipe.PartType.ToString(), // part prop
        ["bearing"] = pipe.Bearing,
        ["slope"] = pipe.Slope,
        ["shape"] = pipe.CrossSectionalShape.ToString(),
        ["minimumCover"] = pipe.MinimumCover,
        ["maximumCover"] = pipe.MaximumCover,
        ["junctionLoss"] = pipe.JunctionLoss,
        ["flowDirection"] = pipe.FlowDirection.ToString(),
        ["flowRate"] = pipe.FlowRate
      };

    // get assignments like catchment group, reference surface, reference pipe networks
    Dictionary<string, object?> assignmentProps = GetPartAssignments(pipe);

    if (pipe.StartStructureId != ADB.ObjectId.Null)
    {
      assignmentProps["startStructureId"] = pipe.StartStructureId.GetSpeckleApplicationId();
    }
    if (pipe.EndStructureId != ADB.ObjectId.Null)
    {
      assignmentProps["endStructureId"] = pipe.EndStructureId.GetSpeckleApplicationId();
    }

    AddDictionaryToDictionary(assignmentProps, properties, ASSIGNMENT_PROP);

    // get dimensional props
    properties[DIMENSIONAL_PROP] = new Dictionary<string, object?>()
    {
      ["innerDiameterOrWidth"] = pipe.InnerDiameterOrWidth,
      ["innerHeight"] = pipe.InnerHeight,
#pragma warning disable CS0618 // Type or member is obsolete
      ["length2d"] = pipe.Length2D, //Length2D was un-obsoleted in 2023, but is still marked obsolete in 2022
#pragma warning restore CS0618 // Type or member is obsolete
    };

    return properties;
  }

  private Dictionary<string, object?> ExtractStructureProperties(CDB.Structure structure)
  {
    // get general props
    Dictionary<string, object?> properties =
      new()
      {
        ["domain"] = structure.Domain.ToString(), // part prop
        ["partType"] = structure.PartType.ToString(), // part prop
        ["northing"] = structure.Northing,
        ["rotation"] = structure.Rotation,
      };

    // get assignments like catchment group, reference surface, reference pipe networks
    Dictionary<string, object?> assignmentProps = GetPartAssignments(structure);
    AddDictionaryToDictionary(assignmentProps, properties, ASSIGNMENT_PROP);

    // get dimensional props
    Dictionary<string, object?> dimensionalProps =
      new()
      {
        ["sumpDepth"] = structure.SumpDepth,
        ["sumpElevation"] = structure.SumpElevation,
        ["innerDiameterOrWidth"] = structure.InnerDiameterOrWidth
      };

    if (structure.BoundingShape == CDB.BoundingShapeType.Box)
    {
      dimensionalProps["innerLength"] = structure.InnerLength;
      dimensionalProps["length"] = structure.Length;
    }
    properties[DIMENSIONAL_PROP] = dimensionalProps;

    // get location
    properties["Location"] = new Dictionary<string, object?>()
    {
      ["x"] = structure.Location.X,
      ["y"] = structure.Location.Y,
      ["z"] = structure.Location.Z,
    };

    return properties;
  }

  private Dictionary<string, object?> GetPartAssignments(CDB.Part part)
  {
    Dictionary<string, object?> partAssignments = new();

    // get part family
    if (part.PartFamilyId != ADB.ObjectId.Null)
    {
      partAssignments["partFamilyId"] = part.PartFamilyId.GetSpeckleApplicationId();
      partAssignments["partFamilyName"] = part.PartFamilyName;
    }

    // get network
    if (part.NetworkId != ADB.ObjectId.Null)
    {
      partAssignments[NETWORKID_PROP] = part.NetworkId.GetSpeckleApplicationId();
      if (_networkCache.TryGetValue(part.NetworkId, out string? name))
      {
        partAssignments[NETWORKNAME_PROP] = name;
      }
      else
      {
        using (var tr = _converterSettings.Current.Document.Database.TransactionManager.StartTransaction())
        {
          var network = (CDB.Network)tr.GetObject(part.NetworkId, ADB.OpenMode.ForRead);
          _networkCache[part.NetworkId] = network.Name;
          partAssignments[NETWORKNAME_PROP] = name;
          tr.Commit();
        }
      }
    }

    // get surface
    if (part.RefSurfaceId != ADB.ObjectId.Null)
    {
      partAssignments[SURFACEID_PROP] = part.RefSurfaceId.GetSpeckleApplicationId();
      partAssignments[SURFACENAME_PROP] = part.RefSurfaceName;
    }

    // get alignment
    if (part.RefAlignmentId != ADB.ObjectId.Null)
    {
      partAssignments[ALIGNMENTID_PROP] = part.RefAlignmentId.GetSpeckleApplicationId();
      partAssignments[ALIGNMENTNAME_PROP] = part.RefAlignmentName;
    }

    return partAssignments;
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
        Dictionary<string, object?> pointPropertiesDict =
          new()
          {
            ["station"] = point.Station,
            ["x"] = point.XYZ.X,
            ["y"] = point.XYZ.Y,
            ["z"] = point.XYZ.Z,
            ["isBreak"] = point.IsBreak
          };

        PropertyHandler propHandler = new();
        propHandler.TryAddToDictionary(pointPropertiesDict, "offset", () => point.Offset); // not all points have offsets, will throw

        pointsDict[pointCount.ToString()] = pointPropertiesDict;
        pointCount++;
      }

      value["featureLinePoints"] = pointsDict;
    }
  }

  private Dictionary<string, object?> ExtractCorridorProperties(CDB.Corridor corridor)
  {
    static void AddArrayToDict(string[] array, Dictionary<string, object?> dict, string key)
    {
      if (array.Length > 0)
      {
        dict[key] = array;
      }
    }

    // get general props
    Dictionary<string, object?> properties = new();

    // get codes
    Dictionary<string, object?> codesDict = new();
    AddArrayToDict(corridor.GetShapeCodes(), codesDict, SHAPES_PROP);
    AddArrayToDict(corridor.GetLinkCodes(), codesDict, LINKS_PROP);
    AddArrayToDict(corridor.GetPointCodes(), codesDict, POINTS_PROP);
    AddDictionaryToDictionary(codesDict, properties, CODES_PROP);

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
      properties["Feature Lines"] = featureLinesDict;
    }

    return properties;
  }

  private Dictionary<string, object?> ExtractAlignmentProperties(CDB.Alignment alignment)
  {
    // get general props
    Dictionary<string, object?> properties =
      new()
      {
        ["startingStation"] = alignment.StartingStation,
        ["endingStation"] = alignment.EndingStation,
        ["alignmentType"] = alignment.AlignmentType.ToString()
      };

    // get assignments
    Dictionary<string, object?> assignmentProps = new();
    if (!alignment.IsSiteless)
    {
      assignmentProps[SITEID_PROP] = alignment.SiteId.GetSpeckleApplicationId();
      assignmentProps[SITENAME_PROP] = alignment.SiteName;
    }
    if (alignment.GetProfileIds().Count > 0)
    {
      assignmentProps["profileId"] = GetSpeckleApplicationIdsFromCollection(alignment.GetProfileIds());
    }
    AddDictionaryToDictionary(assignmentProps, properties, ASSIGNMENT_PROP);

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
    AddDictionaryToDictionary(stationEquationsDict, stationControlDict, "Station Equations");

    stationControlDict["Reference Point"] = new Dictionary<string, object?>()
    {
      ["x"] = alignment.ReferencePoint.X,
      ["y"] = alignment.ReferencePoint.Y,
      ["station"] = alignment.ReferencePointStation
    };

    AddDictionaryToDictionary(stationControlDict, properties, "Station Control");

    // get design speeds
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
    AddDictionaryToDictionary(designSpeedsDict, properties, "Design Speeds");

    // get offset alignment props
    if (alignment.IsOffsetAlignment)
    {
      // accessing "OffsetAlignmentInfo" on offset alignments will sometimes throw /shrug.
      // this happens when an offset alignment is unlinked from the parent and the CreateMode is still set to "ManuallyCreation"
      // https://help.autodesk.com/view/CIV3D/2024/ENU/?guid=2ecbe421-4c08-cbde-d078-56a9f03b93f9
      PropertyHandler propHandler = new();
      if (propHandler.TryGetValue(() => alignment.OffsetAlignmentInfo, out CDB.OffsetAlignmentInfo? offsetInfo))
      {
        properties["Offset Parameters"] = new Dictionary<string, object?>
        {
          ["side"] = offsetInfo?.Side.ToString(),
          ["parentAlignmentId"] = offsetInfo?.ParentAlignmentId.GetSpeckleApplicationId(),
          ["nominalOffset"] = offsetInfo?.NominalOffset
        };
      }
    }

    return properties;
  }

  private Dictionary<string, object?> ExtractProfileProperties(CDB.Profile profile)
  {
    return new()
    {
      ["offset"] = profile.Offset,
      ["startingStation"] = profile.StartingStation,
      ["endingStation"] = profile.EndingStation,
      ["profileType"] = profile.ProfileType.ToString(),
      ["elevationMin"] = profile.ElevationMin,
      ["elevationMax"] = profile.ElevationMax
    };
  }

  private Dictionary<string, object?> ExtractSubassemblyProperties(CDB.Subassembly subassembly)
  {
    static void AddCodesToDict(CDB.CodeCollection codes, Dictionary<string, object?> dict)
    {
      if (codes.Count > 0)
      {
        dict[CODES_PROP] = codes.ToList();
      }
    }

    // get general props
    Dictionary<string, object?> properties = new();
    if (subassembly.HasSide)
    {
      properties["side"] = subassembly.Side.ToString();
    }

    // get assignments
    Dictionary<string, object?> assignmentProps = new();
    if (subassembly.AssemblyId != ADB.ObjectId.Null)
    {
      assignmentProps["assemblyId"] = subassembly.AssemblyId.GetSpeckleApplicationId();
    }
    AddDictionaryToDictionary(assignmentProps, properties, ASSIGNMENT_PROP);

    // get parameters
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
    AddDictionaryToDictionary(parametersDict, properties, "Parameters");

    // get location
    properties["Location"] = new Dictionary<string, object?>()
    {
      ["x"] = subassembly.Origin.X,
      ["y"] = subassembly.Origin.Y,
      ["z"] = subassembly.Origin.Z
    };

    // get shapes > links > points info
    Dictionary<string, object?> shapes = new();
    int shapeCount = 0;
    foreach (CDB.Shape shape in subassembly.Shapes)
    {
      Dictionary<string, object?> shapeDict = new();
      AddCodesToDict(shape.Codes, shapeDict);

      Dictionary<string, object?> links = new();
      int linkCount = 0;
      foreach (CDB.Link link in shape.Links)
      {
        Dictionary<string, object?> linkDict = new();
        AddCodesToDict(link.Codes, linkDict);

        Dictionary<string, object?> points = new();
        int pointCount = 0;
        foreach (CDB.Point point in link.Points)
        {
          Dictionary<string, object?> pointDict = new() { ["elevation"] = point.Elevation, ["offset"] = point.Offset };
          AddCodesToDict(point.Codes, pointDict);
          pointCount++;
        }

        AddDictionaryToDictionary(points, linkDict, POINTS_PROP);
        AddDictionaryToDictionary(linkDict, links, linkCount.ToString());
        linkCount++;
      }

      AddDictionaryToDictionary(links, shapeDict, LINKS_PROP);
      AddDictionaryToDictionary(shapeDict, shapes, shapeCount.ToString());
    }
    AddDictionaryToDictionary(shapes, properties, SHAPES_PROP);

    return properties;
  }

  private List<string> GetSpeckleApplicationIdsFromCollection(ADB.ObjectIdCollection collection)
  {
    List<string> speckleAppIds = new(collection.Count);
    foreach (ADB.ObjectId parcelId in collection)
    {
      speckleAppIds.Add(parcelId.GetSpeckleApplicationId());
    }

    return speckleAppIds;
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

  private void AddDictionaryToDictionary(
    Dictionary<string, object?> dictionary,
    Dictionary<string, object?> parentDictionary,
    string name
  )
  {
    if (dictionary.Count == 0)
    {
      return;
    }

    if (parentDictionary.ContainsKey(name))
    {
      // TODO: log this
      return;
    }

    parentDictionary[name] = dictionary;
  }
}
