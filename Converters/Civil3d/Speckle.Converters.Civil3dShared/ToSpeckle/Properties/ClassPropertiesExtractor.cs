using Speckle.Converters.Common;

namespace Speckle.Converters.Civil3dShared.ToSpeckle;

/// <summary>
/// Extracts class properties deemed important from a civil entity.
/// Should not repeat any data that would be included on property sets and general properties on the object.
/// Expects to be scoped per operation.
/// </summary>
public class ClassPropertiesExtractor
{
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public ClassPropertiesExtractor(IConverterSettingsStore<Civil3dConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Extracts general properties from a civil entity. Expects to be scoped per operation.
  /// </summary>
  /// <param name="entity"></param>
  /// <returns></returns>
  public Dictionary<string, object?>? GetClassProperties(CDB.Entity entity)
  {
    Dictionary<string, object?>? classPropertiesDict = null;
    switch (entity)
    {
      case CDB.Catchment catchment:
        classPropertiesDict = ExtractCatchmentProperties(catchment);
        break;
    }

    return classPropertiesDict;
  }

  private Dictionary<string, object?> ExtractCatchmentProperties(CDB.Catchment catchment)
  {
    return new()
    {
      ["hydrologicalSoilGroup"] = catchment.HydrologicalSoilGroup.ToString(),
      ["imperviousArea"] = catchment.ImperviousArea,
      ["manningsCoefficient"] = catchment.ManningsCoefficient,
      ["perimeter2d"] = catchment.Perimeter2d,
      ["runoffCoefficient"] = catchment.RunoffCoefficient,
      ["timeOfConcentration"] = catchment.TimeOfConcentration
    };
  }
}
