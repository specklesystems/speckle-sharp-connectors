using Autodesk.Civil.DatabaseServices;
using Speckle.Converters.Civil3dShared.Extensions;
using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Civil3dShared.ToSpeckle;

/// <summary>
/// Extracts class properties deemed important from a civil entity.
/// Should not repeat any data that would be included on property sets and general properties on the object.
/// Expects to be scoped per operation.
/// </summary>
public class ClassPropertiesExtractor
{
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly ITypedConverter<AG.Point3dCollection, SOG.Polyline> _point3dCollectionConverter;
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly CatchmentGroupHandler _catchmentGroupHandler;
  private readonly PipeNetworkHandler _pipeNetworkHandler;

  public ClassPropertiesExtractor(
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
    ITypedConverter<AG.Point3dCollection, SOG.Polyline> point3dCollectionConverter,
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    CatchmentGroupHandler catchmentGroupHandler,
    PipeNetworkHandler pipeNetworkHandler
  )
  {
    _point3dCollectionConverter = point3dCollectionConverter;
    _pointConverter = pointConverter;
    _settingsStore = settingsStore;
    _catchmentGroupHandler = catchmentGroupHandler;
    _pipeNetworkHandler = pipeNetworkHandler;
  }

  /// <summary>
  /// Extracts general properties from a civil entity. Expects to be scoped per operation.
  /// </summary>
  /// <param name="entity"></param>
  /// <returns></returns>
  public Dictionary<string, object?>? GetClassProperties(CDB.Entity entity)
  {
    switch (entity)
    {
      case CDB.Catchment catchment:
        return ExtractCatchmentProperties(catchment);
      case CDB.Site site:
        return ExtractSiteProperties(site);
      case CDB.Pipe pipe:
        return ExtractPipeProperties(pipe);
      case CDB.Structure structure:
        return ExtractStructureProperties(structure);
      case CDB.Part part:
        return ExtractPartProperties(part);

      default:
        return null;
    }
  }

  // For more info on how points are used: https://help.autodesk.com/view/CIV3D/2024/ENU/?guid=GUID-CBABE972-D690-49AE-A7DE-60F2E1B0675D
  private Dictionary<string, object?> ExtractPointProperties(CDB.Point point)
  {
    Dictionary<string, object?> pointProperties =
      new()
      {
        ["elevation"] = point.Elevation,
        ["station"] = point.Station,
        ["isLoopPoint"] = point.IsLoopPoint
      };

    if (point.Codes.Count > 0)
    {
      pointProperties["codes"] = point.Codes.ToList();
    }

    return pointProperties;
  }

  private Dictionary<string, object?> ExtractPipeProperties(CDB.Pipe pipe)
  {
    Dictionary<string, object?> pipeProperties =
      new()
      {
        ["innerDiameterOrWidth"] = pipe.InnerDiameterOrWidth,
        ["innerHeight"] = pipe.InnerHeight,
        ["slope"] = pipe.Slope,
        ["shape"] = pipe.CrossSectionalShape.ToString(),
        ["length2d"] = pipe.Length2D,
        ["minimumCover"] = pipe.MinimumCover,
        ["maximumCover"] = pipe.MaximumCover,
        ["junctionLoss"] = pipe.JunctionLoss,
        ["flowDirection"] = pipe.FlowDirection.ToString(),
        ["flowRate"] = pipe.FlowRate
      };

    if (pipe.StartStructureId != ADB.ObjectId.Null)
    {
      pipeProperties["startStructureId"] = pipe.StartStructureId.GetSpeckleApplicationId();
    }

    if (pipe.EndStructureId != ADB.ObjectId.Null)
    {
      pipeProperties["endStructureId"] = pipe.EndStructureId.GetSpeckleApplicationId();
    }

    return pipeProperties;
  }

  private Dictionary<string, object?> ExtractStructureProperties(CDB.Structure structure)
  {
    var location = _pointConverter.Convert(structure.Location);

    Dictionary<string, object?> structureProperties =
      new()
      {
        ["location"] = location,
        ["northing"] = structure.Northing,
        ["rotation"] = structure.Rotation,
        ["sumpDepth"] = structure.SumpDepth,
        ["sumpElevation"] = structure.SumpElevation,

        ["innerDiameterOrWidth"] = structure.InnerDiameterOrWidth
      };

    if (structure.BoundingShape == BoundingShapeType.Box)
    {
      structureProperties["innerLength"] = structure.InnerLength;
      structureProperties["length"] = structure.Length;
    }

    return structureProperties;
  }

  private Dictionary<string, object?> ExtractPartProperties(CDB.Part part)
  {
    // process the part's pipe network with the pipe network handler
    _pipeNetworkHandler.HandlePipeNetwork(part);

    Dictionary<string, object?> partProperties =
      new()
      {
        ["domain"] = part.Domain.ToString(),
        ["partFamilyName"] = part.PartFamilyName,
        ["partType"] = part.PartType.ToString(),
      };

    if (part.RefSurfaceId != ADB.ObjectId.Null)
    {
      partProperties["surfaceId"] = part.RefSurfaceId.GetSpeckleApplicationId();
    }

    return partProperties;
  }

  private Dictionary<string, object?> ExtractSiteProperties(CDB.Site site)
  {
    Dictionary<string, object?> catchmentProperties = new();

    if (site.GetAlignmentIds().Count > 0)
    {
      catchmentProperties["alignmentIds"] = GetSpeckleApplicationIdsFromCollection(site.GetAlignmentIds());
    }

    if (site.GetFeatureLineIds().Count > 0)
    {
      catchmentProperties["featureLineIds"] = GetSpeckleApplicationIdsFromCollection(site.GetFeatureLineIds());
    }

    if (site.GetParcelIds().Count > 0)
    {
      catchmentProperties["parcelIds"] = GetSpeckleApplicationIdsFromCollection(site.GetParcelIds());
    }

    return catchmentProperties;
  }

  private Dictionary<string, object?> ExtractCatchmentProperties(CDB.Catchment catchment)
  {
    // get the bounding curve of the catchment
    SOG.Polyline boundary = _point3dCollectionConverter.Convert(catchment.BoundaryPolyline3d);

    // use the catchment group handler to process the catchment's group
    _catchmentGroupHandler.HandleCatchmentGroup(catchment);

    return new()
    {
      ["antecedentWetness"] = catchment.AntecedentWetness,
      ["area"] = catchment.Area,
      ["area2d"] = catchment.Area2d,
      ["boundary"] = boundary,
      ["exclusionary"] = catchment.Exclusionary,
      ["hydrologicalSoilGroup"] = catchment.HydrologicalSoilGroup.ToString(),
      ["imperviousArea"] = catchment.ImperviousArea,
      ["manningsCoefficient"] = catchment.ManningsCoefficient,
      ["perimeter2d"] = catchment.Perimeter2d,
      ["runoffCoefficient"] = catchment.RunoffCoefficient,
      ["timeOfConcentration"] = catchment.TimeOfConcentration
    };
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
}
