using Speckle.Converters.Civil3dShared.Extensions;
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

  public ClassPropertiesExtractor(
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
    ITypedConverter<AG.Point3dCollection, SOG.Polyline> point3dCollectionConverter
  )
  {
    _point3dCollectionConverter = point3dCollectionConverter;
    _settingsStore = settingsStore;
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
        return ExtractCatchmentProperties(site);
      default:
        return null;
    }
  }

  private Dictionary<string, object?> ExtractCatchmentProperties(CDB.Site site)
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
