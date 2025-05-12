using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class ControlPointToSpeckleConverter : ITypedConverter<RG.ControlPoint, SOG.ControlPoint>
{
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public ControlPointToSpeckleConverter(IConverterSettingsStore<RhinoConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a ControlPoint object to a Speckle ControlPoint object.
  /// </summary>
  /// <param name="target">The ControlPoint object to convert.</param>
  /// <returns>The converted Speckle ControlPoint object.</returns>
  public SOG.ControlPoint Convert(RG.ControlPoint target) =>
    new(target.Location.X, target.Location.Y, target.Location.Z, target.Weight, _settingsStore.Current.SpeckleUnits);

  public Base Convert(object target) => Convert((RG.ControlPoint)target);
}
