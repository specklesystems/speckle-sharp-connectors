using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino;

public abstract class SpeckleToHostGeometryBaseTopLevelConverter<TIn, TOut> : IToHostTopLevelConverter
  where TIn : Base
  where TOut : RG.GeometryBase
{
  protected IConverterSettingsStore<RhinoConversionSettings> SettingsStore { get; private set; }
  private readonly ITypedConverter<TIn, TOut> _geometryBaseConverter;

  protected SpeckleToHostGeometryBaseTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<TIn, TOut> geometryBaseConverter
  )
  {
    SettingsStore = settingsStore;
    _geometryBaseConverter = geometryBaseConverter;
  }

  public object Convert(Base target)
  {
    var castedBase = (TIn)target;
    var result = _geometryBaseConverter.Convert(castedBase);

    /*
     * POC: CNX-9270 Looking at a simpler, more performant way of doing unit scaling on `ToNative`
     * by fully relying on the transform capabilities of the HostApp, and only transforming top-level stuff.
     * This may not hold when adding more complex conversions, but it works for now!
     */
    if (castedBase["units"] is string units)
    {
      var scaleFactor = Units.GetConversionFactor(units, SettingsStore.Current.SpeckleUnits);
      var scale = RG.Transform.Scale(RG.Point3d.Origin, scaleFactor);
      result.Transform(scale);
    }

    return result;
  }
}
