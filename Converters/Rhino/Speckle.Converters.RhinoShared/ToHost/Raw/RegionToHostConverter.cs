using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Rhino.ToHost.Raw;

public class RegionToHostConverter : ITypedConverter<SOG.Region, RG.Hatch>
{
  private readonly ITypedConverter<SOP.Interval, RG.Interval> _intervalConverter;
  private readonly IServiceProvider _serviceProvider;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public RegionToHostConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOP.Interval, RG.Interval> intervalConverter,
    IServiceProvider serviceProvider
  )
  {
    _settingsStore = settingsStore;
    _intervalConverter = intervalConverter;
    _serviceProvider = serviceProvider;
  }

  /// <summary>
  /// Converts a Speckle Region geometry to a Rhino Hatch.
  /// </summary>
  /// <param name="target">The Speckle Region geometry to convert.</param>
  /// <returns>The converted Rhino Hatch.</returns>
  /// <remarks>⚠️ This conversion does NOT perform scaling.</remarks>
  public RG.Hatch Convert(SOG.Region target)
  {
    List<RG.Curve> rhinoCurves = new() { ConvertAndValidateICurve(target.boundary) };
    foreach (var loop in target.innerLoops)
    {
      rhinoCurves.Add(ConvertAndValidateICurve(loop));
    }
    var tolerance = _settingsStore.Current.Document.ModelAbsoluteTolerance;
    // .Create method returns array, but in case of Speckle Region we always expect only 1 converted Hatch
    RG.Hatch result = RG.Hatch.Create(rhinoCurves, 0, 0, 1, tolerance)[0];
    return result;
  }

  private RG.Curve ConvertAndValidateICurve(ICurve curve)
  {
    RG.Curve rhinoCurve = _serviceProvider.GetRequiredService<ITypedConverter<ICurve, RG.Curve>>().Convert(curve);

    if (!rhinoCurve.IsValid)
    {
      throw new ConversionException($"Failed to convert hatch curve {curve}");
    }

    return rhinoCurve;
  }
}
