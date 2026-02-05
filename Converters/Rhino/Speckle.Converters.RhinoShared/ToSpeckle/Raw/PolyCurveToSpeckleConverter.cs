using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class PolyCurveToSpeckleConverter : ITypedConverter<RG.PolyCurve, SOG.Polycurve>
{
  private readonly IServiceProvider _serviceProvider;
  private readonly ITypedConverter<RG.Interval, SOP.Interval> _intervalConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public PolyCurveToSpeckleConverter(
    ITypedConverter<RG.Interval, SOP.Interval> intervalConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    IServiceProvider serviceProvider
  )
  {
    _intervalConverter = intervalConverter;
    _settingsStore = settingsStore;
    _serviceProvider = serviceProvider;
  }

  private Lazy<ITypedConverter<RG.Curve, ICurve>> CurveConverter =>
    new(() => _serviceProvider.GetRequiredService<ITypedConverter<RG.Curve, ICurve>>());

  /// <summary>
  /// Converts a Rhino PolyCurve to a Speckle Polycurve.
  /// </summary>
  /// <param name="target">The Rhino PolyCurve to convert.</param>
  /// <returns>The converted Speckle Polycurve.</returns>
  /// <remarks>
  /// This method removes the nesting of the PolyCurve by duplicating the segments at a granular level.
  /// All PolyLIne, PolyCurve and NURBS curves with G1 discontinuities will be broken down.
  /// </remarks>
  public SOG.Polycurve Convert(RG.PolyCurve target)
  {
    var myPoly = new SOG.Polycurve
    {
      closed = target.IsClosed,
      domain = _intervalConverter.Convert(target.Domain),
      length = target.GetLength(),
      segments = target.DuplicateSegments().Select(x => CurveConverter.Value.Convert(x)).ToList(),
      units = _settingsStore.Current.SpeckleUnits
    };
    return myPoly;
  }
}
