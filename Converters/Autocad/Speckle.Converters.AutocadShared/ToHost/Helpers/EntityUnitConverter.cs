using Speckle.Converters.Common;
using Speckle.Sdk.Common;

namespace Speckle.Converters.Autocad.ToHost.Helpers;

/// <summary>
/// Scales AutoCAD entities from source units to target units.
/// SAT format is unitless, so we need to apply unit conversion manually after import.
/// </summary>
public class EntityUnitConverter(IConverterSettingsStore<AutocadConversionSettings> settingsStore)
{
  public void ScaleIfNeeded(List<ADB.Entity> entities, string? sourceUnits)
  {
    if (string.IsNullOrEmpty(sourceUnits))
    {
      return;
    }

    double scaleFactor = Units.GetConversionFactor(sourceUnits, settingsStore.Current.SpeckleUnits);
    if (Math.Abs(scaleFactor - 1.0) < 1e-10)
    {
      return;
    }

    var scaleMatrix = AG.Matrix3d.Scaling(scaleFactor, AG.Point3d.Origin);
    foreach (var entity in entities)
    {
      entity.TransformBy(scaleMatrix);
    }
  }
}
