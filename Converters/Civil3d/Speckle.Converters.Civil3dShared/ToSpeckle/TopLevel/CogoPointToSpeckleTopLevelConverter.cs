using Speckle.Converters.Civil3dShared.Extensions;
using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.TopLevel;

/// <summary>
/// This top level converter is needed because the namespace of CogoPoint is Autodesk.Civil.DatabaseServices, but the inheritance of CogoPoint is Autodesk.Autocad.Entity
/// This means cogo points will *not* be picked up by the top level civil entity converter.
/// POC: implementing a top level autocad entity converter can probably replace this converter.
/// </summary>
[NameAndRankValue(typeof(CDB.CogoPoint), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CogoPointToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;

  public CogoPointToSpeckleTopLevelConverter(
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
  }

  public Base Convert(object target) => Convert((CDB.CogoPoint)target);

  public Civil3dObject Convert(CDB.CogoPoint target)
  {
    PropertyHandler propHandler = new();
    string? name = propHandler.TryGetValue(() => target.PointName, out string? pointName) ? pointName : ""; // throws if name doesnt exist

    // extract display value as point
    SOG.Point displayPoint = _pointConverter.Convert(target.Location);

    // get additional class properties
    Dictionary<string, object?> props = new() { ["number"] = target.PointNumber, ["northing"] = target.Northing };

    Civil3dObject civilObject =
      new()
      {
        name = name ?? "",
        type = target.GetType().ToString().Split('.').Last(),
        baseCurves = null,
        elements = new(),
        displayValue = new() { displayPoint },
        properties = props,
        units = _settingsStore.Current.SpeckleUnits,
        applicationId = target.Id.GetSpeckleApplicationId(),
      };

    return civilObject;
  }
}
