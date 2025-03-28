using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Rhino.ToHost.Raw;

public class RegionToHostConverter : ITypedConverter<SOG.Region, RG.Hatch>
{
  private readonly ITypedConverter<ICurve, RG.Curve> _curveConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public RegionToHostConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<ICurve, RG.Curve> curveConverter
  )
  {
    _settingsStore = settingsStore;
    _curveConverter = curveConverter;
  }

  /// <summary>
  /// Converts a Speckle Region geometry to a Rhino Hatch.
  /// </summary>
  /// <param name="target">The Speckle Region geometry to convert.</param>
  /// <returns>The converted Rhino Hatch.</returns>
  /// <remarks>⚠️ This conversion does NOT perform scaling.</remarks>
  public RG.Hatch Convert(SOG.Region target)
  {
    List<RG.Curve> rhinoCurves = new() { _curveConverter.Convert(target.boundary) };
    foreach (var loop in target.innerLoops)
    {
      rhinoCurves.Add(_curveConverter.Convert(loop));
    }
    var tolerance = _settingsStore.Current.Document.ModelAbsoluteTolerance;
    // .Create method returns array, but in case of Speckle Region we always expect only 1 converted Hatch
    RG.Hatch[] result = RG.Hatch.Create(rhinoCurves, 0, 0, 1, tolerance);
    if (result.Length != 1)
    {
      throw new ConversionException(
        $"Hatch conversion failed for {target}: unexpected number of shapes generated ({result.Length}). Make sure that input loops are planar, closed, non self-intersecting curves."
      );
    }
    return result[0];
  }
}
